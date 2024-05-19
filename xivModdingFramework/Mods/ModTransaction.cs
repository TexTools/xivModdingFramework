using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;
using Index = xivModdingFramework.SqPack.FileTypes.Index;

namespace xivModdingFramework.Mods
{
    #region Enums and Structs
    public enum ETransactionTarget
    {
        Invalid,

        // Write the modified files to the game .DATs on transaction commit.
        GameFiles,

        // Write the modified files to the given folder on transaction commit, in Lumina-style folder chains.
        LuminaFolders,

        // Write the modified files to a TTMP at the given destination on transaction commit.
        TTMP,

        // Write the modified files to PMP file at the given destination on transaction commit.
        PMP
    }

    public enum ETransactionState
    {
        Invalid,

        // Just hanging out reading stuff.
        ReadOnly,

        // TX has been created, and is in setup phase.
        // During this period, files that are imported into the TX will not be tracked for final output writing,
        // But will still be imported into the Transaction state.
        // Tl;Dr: Import Dependencies here.
        Preparing,

        // TX is actively open and recording file/index changes.
        Open,

        // TX in the process of either cancelling or committing.
        Closing,

        // TX has been cancelled or commited and is now closed.
        Closed
    }

    public struct ModTransactionSettings
    {
        public EFileStorageType StorageType { get; set; }
        public ETransactionTarget Target { get; set; }
        public string TargetPath { get; set; }
    }
    #endregion

    /// <summary>
    /// A class representing the holistic state of a file in the system.
    /// In particular, this store the index and mod information about the file.
    /// Used in saving and restoring states.
    /// </summary>
    public class TxFileState
    {
        public TxFileState(string path)
        {
            Path = path;
        }

        // Internal File Path
        public string Path { get; set; }
        public XivDataFile DataFile
        {
            get
            {
                return IOUtil.GetDataFileFromPath(Path);
            }
        }

        // Tracking Flags
        public bool OriginalOffset_Set { get; private set; } = false;
        public bool OriginalMod_Set { get; private set; } = false;

        // Index Data
        private long _OriginalOffset = 0;
        public long OriginalOffset
        {
            get
            {
                return _OriginalOffset;
            }
            set
            {
                _OriginalOffset = value;
                OriginalOffset_Set = true;
            }
        }

        // ModList Data
        private Mod? _OriginalMod;
        public Mod? OriginalMod
        {
            get
            {
                return _OriginalMod;
            }
            set
            {
                _OriginalMod = value;
                OriginalMod_Set = true;
            }
        }
    }

    public class ModTransaction : IDisposable
    {

        #region Events
        public delegate void TransactionEventHandler(ModTransaction sender);
        public delegate void TransactionCancelledEventHandler(ModTransaction sender, bool graceful);
        public delegate void TransactionStateChangedEventHandler(ModTransaction sender, ETransactionState oldState, ETransactionState newState);

        public event TransactionEventHandler TransactionCommitted;
        public event TransactionCancelledEventHandler TransactionCancelled;
        public event TransactionEventHandler TransactionClosed;
        public event TransactionStateChangedEventHandler TransactionStateChanged;

        public static event TransactionEventHandler ActiveTransactionCommitted;
        public static event TransactionCancelledEventHandler ActiveTransactionCancelled;
        public static event TransactionEventHandler ActiveTransactionClosed;
        public static event TransactionStateChangedEventHandler ActiveTransactionStateChanged;

        public static event TransactionEventHandler ActiveTransactionBlocked;

        private static bool _CANCEL_BLOCKED_TX = false;
        private static bool _ACTIVE_TX_BLOCKED = false;
        #endregion


        #region Properties and Accessors

        private Dictionary<XivDataFile, IndexFile> _IndexFiles = new Dictionary<XivDataFile, IndexFile>();
        private Dictionary<XivDataFile, uint> _NextDataOffset = new Dictionary<XivDataFile, uint>();


        // Collections used in data tracking of modified files.
        
        // Primary collection of data, used in tracking what has been changed.
        private Dictionary<string, TxFileState> _OriginalStates = new Dictionary<string, TxFileState>();

        // Collection of files imported during Prep mode.
        private Dictionary<string, TxFileState> _PrePrepStates = new Dictionary<string, TxFileState>();

        // Offset mapping of Temporary Offset => File paths referencing that offset.
        private Dictionary<XivDataFile, Dictionary<long, HashSet<string>>> _TemporaryOffsetMapping = new Dictionary<XivDataFile, Dictionary<long, HashSet<string>>>();

        // Dictionary of Temporary offsets to Real offsets, only populated after committing a transaction.
        private Dictionary<XivDataFile, Dictionary<long, long>> _TempToRealOffsetMapping = new Dictionary<XivDataFile, Dictionary<long, long>>();

        // Modified times the first time we access the index files.
        private Dictionary<XivDataFile, DateTime> _Index1ModifiedTimes = new Dictionary<XivDataFile, DateTime>();
        private Dictionary<XivDataFile, DateTime> _Index2ModifiedTimes = new Dictionary<XivDataFile, DateTime>();

        // File sizes of the .DAT files the first time we encountered them.
        private Dictionary<XivDataFile, List<long>> _DatFileSizes = new Dictionary<XivDataFile, List<long>>();

        private DateTime _ModListModifiedTime;

        private ModList _ModList;
        private readonly bool _ReadOnly = false;
        public bool ReadOnly
        {
            get
            {
                return _ReadOnly;
            }
        }

        private TransactionDataHandler _DataHandler;

        private DirectoryInfo _GameDirectory;

        public ModTransactionSettings Settings { get; private set; }
        public bool AffectsGameFiles
        {
            get
            {
                if(Settings.Target == ETransactionTarget.GameFiles && Settings.TargetPath == XivCache.GameInfo.GameDirectory.FullName)
                {
                    return true;
                }
                return false;
            }
        }

        private bool _Disposed;
        public ModPack? ModPack { get; set; }


        private static bool _WorkerStatus = false;
        private static ModTransaction _ActiveTransaction = null;
        internal static ModTransaction ActiveTransaction
        {
            get
            {
                return _ActiveTransaction;
            }
        }
        public List<XivDataFile> ActiveDataFiles
        {
            get
            {
                return _IndexFiles.Select(x => x.Key).ToList();
            }
        }

        private ETransactionState _State;
        public ETransactionState State { 
            get {
                return _State;
            } 
            private set
            {
                var isActiveTx = this == ActiveTransaction;

                var oldState = _State;
                _State = value;

                TransactionStateChanged?.Invoke(this, oldState, _State);
                if (isActiveTx)
                {
                    ActiveTransactionStateChanged?.Invoke(this, oldState, _State);
                }

                if(_State == ETransactionState.Closed)
                {
                    TransactionClosed?.Invoke(this);
                    if (isActiveTx)
                    {
                        ActiveTransactionClosed?.Invoke(this);
                    }
                }
            }
        }

        /// <summary>
        /// Cancels the current write-blocked transaction.
        /// </summary>
        public static void CancelBlockedTransaction()
        {
            if (_ACTIVE_TX_BLOCKED)
            {
                _CANCEL_BLOCKED_TX = true;
            }
        }

        private bool AreGameFilesWritable()
        {
            foreach (var ik in _IndexFiles)
            {
                var df = ik.Key;
                var index1Path = XivDataFiles.GetFullPath(df, Index.IndexExtension);
                var index2Path = XivDataFiles.GetFullPath(df, Index.Index2Extension);

                try
                {
                    var f1 = File.Open(index1Path, FileMode.Open, FileAccess.ReadWrite);
                    var f2 = File.Open(index2Path, FileMode.Open, FileAccess.ReadWrite);
                    f1.Dispose();
                    f2.Dispose();
                }
                catch(Exception ex)
                {
                    return false;
                }
            }
            return true;
        }

        private bool _LoadingIndexFiles;
        public async Task<IndexFile> GetIndexFile(XivDataFile dataFile)
        {
            while (_LoadingIndexFiles)
            {
                Thread.Sleep(1);
            }
            
            if(!_IndexFiles.ContainsKey(dataFile))
            {
                _LoadingIndexFiles = true;
                try
                {
                    if (!_ReadOnly)
                    {
                        var index1Path = XivDataFiles.GetFullPath(dataFile, Index.IndexExtension);
                        var index2Path = XivDataFiles.GetFullPath(dataFile, Index.Index2Extension);

                        _Index1ModifiedTimes.Add(dataFile, File.GetLastWriteTimeUtc(index1Path));
                        _Index2ModifiedTimes.Add(dataFile, File.GetLastWriteTimeUtc(index2Path));

                        // Store the file sizes of the DAT files on encounter.
                        // This allows us to truncate transaction data from the end on Transaction Cancel,
                        // as transaction data is always written to the end of the DAT files.
                        _DatFileSizes.Add(dataFile, new List<long>());
                        for (int i = 0; i < Dat._MAX_DATS; i++)
                        {
                            var datPath = Dat.GetDatPath(dataFile, i);
                            if (!File.Exists(datPath))
                            {
                                break;
                            }

                            var size = new FileInfo(datPath).Length;
                            _DatFileSizes[dataFile].Add(size);
                        }
                    }

                    var idx = await Index.GetIndexFile(dataFile, false, ReadOnly);
                    _IndexFiles.Add(dataFile, idx);
                }
                finally
                {
                    _LoadingIndexFiles = false;
                }

            }
            return _IndexFiles[dataFile];
        }

        private bool _LoadingModlist;
        public async Task<ModList> GetModList()
        {
            while (_LoadingModlist)
            {
                Thread.Sleep(1);
            }

            if(_ModList == null)
            {
                // TODO: Should store modified times here to validate like we do for Index Files.
                _LoadingModlist = true;
                try
                {
                    _ModListModifiedTime = File.GetLastWriteTimeUtc(Modding.ModListDirectory);
                    _ModList = await Modding.GetModList(!ReadOnly);
                }
                finally
                {
                    _LoadingModlist = false;
                }
            }
            return _ModList;
        }

        #endregion


        #region Constructor/Disposable Pattern
        public ModTransaction()
        {
            throw new NotImplementedException("Mod Transactions must be created via ModTransaction.Begin()");
        }
        private ModTransaction(bool writeEnabled = false, ModPack? modpack = null, ModTransactionSettings? settings = null, bool waitToStart = false)
        {
            _GameDirectory = XivCache.GameInfo.GameDirectory;
            ModPack = modpack;

            _ReadOnly = !writeEnabled;
            State = ETransactionState.Preparing;




            // NOTE: Readonly Transactions should not implement anything that requires disposal via IDisposable.
            // Readonly Tx are intended to be lightweight and used in non-disposable/standard memory managed contexts.
            if (_ReadOnly)
            {
                // Readonly Data Handlers do not technically need to be disposed as they never create a data store.
                _DataHandler = new TransactionDataHandler(EFileStorageType.ReadOnly);
                Settings = new ModTransactionSettings()
                {
                    StorageType = EFileStorageType.ReadOnly,
                    Target = ETransactionTarget.Invalid,
                    TargetPath = null,
                };
                State = ETransactionState.ReadOnly;
            } else
            {
                if (settings == null)
                {
                    settings = GetDefaultSettings();
                }
                Settings = settings.Value;

                if (Settings.Target == ETransactionTarget.Invalid)
                {
                    throw new Exception("Invalid Transaction Target.");
                }

                if (Settings.Target == ETransactionTarget.GameFiles && Settings.TargetPath == null)
                {
                    // If we weren't given an explicit game path to commit to, use the default game directory.
                    var s = Settings;
                    s.TargetPath = XivCache.GameInfo.GameDirectory.FullName;
                    Settings = s;
                }

                if (String.IsNullOrWhiteSpace(Settings.TargetPath))
                {
                    throw new Exception("A target path must be supplied for non-GameDat Transactions.");
                }

                _DataHandler = new TransactionDataHandler(EFileStorageType.UncompressedIndividual);
            }

            if (waitToStart && Settings.Target == ETransactionTarget.GameFiles)
            {
                throw new NotImplementedException("Prep-File support for game file writing is not yet implemented (Need to wrap ModList to prevent mod bashing).");
            }

            if (!waitToStart)
            {
                State = ETransactionState.Open;
            }
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (State != ETransactionState.Closed)
                {
                    if (_DataHandler != null)
                    {
                        // Clear data handler temp files.
                        _DataHandler.Dispose();
                        _DataHandler = null;
                    }

                    // If we haven't been cancelled or committed, do so.
                    ModTransaction.CancelTransaction(this);
                }

                _Disposed = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion


        #region Open/Close Transaction Functions

        /// <summary>
        /// Advances the Transaction state from Preparing to Open
        /// </summary>
        public void Start()
        {
            if(State != ETransactionState.Preparing)
            {
                throw new Exception("Cannot start transaction that is not in the Preparing State.");
            }
            State = ETransactionState.Open;
        }

        /// <summary>
        /// Opens a new mod transaction.
        /// Transactions will still write data to DATs in real time, but will cache index and modlist changes until they are committed.
        /// </summary>
        /// <param name="modpack"></param>
        /// <returns></returns>
        public static ModTransaction BeginTransaction(bool writeEnabled = false, ModPack? modpack = null, ModTransactionSettings? settings = null, bool waitToStart = false)
        {

            if (!writeEnabled)
            {
                // Read-Only Transactions don't block anything else, and really just serve as
                // caches for index/modlist data.
                var readonlyTx = new ModTransaction(writeEnabled, modpack);
                return readonlyTx;
            }

            if (_ActiveTransaction != null)
            {
                throw new Exception("Cannot have two write-enabled mod transactions open simultaneously.");
            }

            // Disable the cache worker during transactions.
            _WorkerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;

            var tx = new ModTransaction(writeEnabled, modpack, settings, waitToStart);
            if (!Dat.AllowDatAlteration && tx.Settings.Target == ETransactionTarget.GameFiles)
            {
                _ActiveTransaction = tx;
                CancelTransaction(tx);
                throw new Exception("Cannot open write transaction while DAT writing is disabled.");
            }
            _ActiveTransaction = tx;


            return tx;
        }

        /// <summary>
        /// Commits this transaction to the game files.
        /// This causes Index and Modlist writes to disk for all affected mods.
        /// </summary>
        /// <returns></returns>
        public static async Task CommitTransaction(ModTransaction tx)
        {
            if (tx._ReadOnly)
            {
                throw new Exception("Attempted to commit a Read Only Transaction.");
            }

            if (tx != _ActiveTransaction)
            {
                throw new Exception("Attempted to commit transaction other than the current open mod transation.");
            }

            if(tx.State != ETransactionState.Open)
            {
                throw new Exception("Cannot Commit transaction that is not in the Open State.");
            }

            try
            {
                await tx.CommitTransaction();
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
            finally
            {
                _ActiveTransaction = null;
                tx.State = ETransactionState.Closed;
                _CANCEL_BLOCKED_TX = false;
                XivCache.CacheWorkerEnabled = _WorkerStatus;
            }
        }
        private async Task CommitTransaction()
        {
            if (_ReadOnly)
            {
                throw new Exception("Attempted to commit a Read Only Transaction.");
            }

            if (XivCache.GameInfo.UseLumina && Settings.Target == ETransactionTarget.GameFiles)
            {
                throw new Exception("Attempted to write to game files while Lumina mode was enabled.");
            }


            State = ETransactionState.Closing;

            CheckWriteTimes();

            if(Settings.Target == ETransactionTarget.GameFiles)
            {
                var cancelled = await Task.Run(() =>
                {
                    _ACTIVE_TX_BLOCKED = false;
                    _CANCEL_BLOCKED_TX = false;
                    while (!AreGameFilesWritable())
                    {
                        if (_ACTIVE_TX_BLOCKED == false)
                        {
                            _ACTIVE_TX_BLOCKED = true;
                            ActiveTransactionBlocked.Invoke(null);
                        }

                        if(_CANCEL_BLOCKED_TX)
                        {
                            _ACTIVE_TX_BLOCKED = false;
                            _CANCEL_BLOCKED_TX = false;
                            State = ETransactionState.Open;
                            ModTransaction.CancelTransaction(this, true);
                            return true;
                        }
                        Thread.Sleep(1000);
                    }
                    return false;
                });
                if (cancelled) return;
            }


            // Perform, in order...
            // DATA WRITE => MODLIST WRITE => INDEX WRITE
            // This way if anything breaks, it is least dangerous.

            // Writing to dats/data store is largely harmless, as we can just truncate the file back.
            // Saving the modlist is fine once the data is in the dats.  Just need to re-validate mod eanbled status.
            // Once Index save is done, so are we.

            // Write data from the transaction store to the real data target.
            var pathMap = await _DataHandler.WriteAllToTarget(Settings, this);

            await WriteDisabledMods(pathMap);

            // If the data handler returned a null, that means we aren't doing
            // anything else to the base game files/modlist here.
            if (pathMap != null || !AffectsGameFiles)
            {
                foreach(var kv in _PrePrepStates)
                {
                    // Validation
                    if (IsPrepFile(kv.Key))
                    {
                        // Reset file to pre-TX state.
                        await ResetFile(kv.Key, true);
                    }
                }

                // Update all the indexes with the real, post-write offsets.
                foreach (var kv in pathMap)
                {
                    var lastOffset = await Set8xDataOffset(kv.Key, kv.Value.RealOffset);
                    if (lastOffset != kv.Value.TempOffset)
                    {
                        // This occurs when a new mod was added, then disabled, but not deleted.
                        // So we still write the Disabled data to the DATs, but don't update indexes.
                        continue;
                    }
                    var df = IOUtil.GetDataFileFromPath(kv.Key);

                    if (!_TempToRealOffsetMapping.ContainsKey(df))
                    {
                        _TempToRealOffsetMapping.Add(df, new Dictionary<long, long>());
                    }

                    _TempToRealOffsetMapping[df].Add(kv.Value.TempOffset, kv.Value.RealOffset);
                }
                
                if(_ModList == null)
                {
                    await GetModList();
                }

                // Update the modlist with the real, post-write offsets.
                foreach(var kv in pathMap)
                {
                    var mod = _ModList.GetMod(kv.Key);
                    if (mod != null)
                    {
                        var m = mod.Value;
                        if(m.ModOffset8x != kv.Value.TempOffset)
                        {
                            throw new InvalidDataException("Mod entry has mismatching temporary offset: " + kv.Key);
                        }

                        m.ModOffset8x = kv.Value.RealOffset;
                        if(m.OriginalOffset8x == kv.Value.TempOffset)
                        {
                            // Don't think we ever use this path now that we set original value to 0 for custom file mods.
                            // But it's good to leave it in for safety.
                            m.OriginalOffset8x = kv.Value.RealOffset;
                        }

                        _ModList.AddOrUpdateMod(m);
                    } else
                    {
                        // User deleted the mod... But we updated the index file to new data?
                        throw new InvalidDataException("Missing mod entry for imported file: " + kv.Key);
                    }
                }

                await Modding.SaveModListAsync(_ModList);

                foreach (var index in _IndexFiles)
                {
                    index.Value.Save();
                }

                // We have to queue all of the touched files up as possibly changed in the Mod Cache to be safe.
                HashSet<string> files = new HashSet<string>();
                files.Union(_PrePrepStates.Keys);
                files.Union(_OriginalStates.Keys);
                XivCache.QueueDependencyUpdate(files);
            }

            TransactionCommitted?.Invoke(this);
            if(ActiveTransaction == this)
            {
                ActiveTransactionCommitted?.Invoke(this);
            }
        }

        /// <summary>
        /// Cancels the given transaction.
        /// This discards the internal cached index and modlist pointers and truncates the .DAT files back to their pre-transaction states.
        /// </summary>
        /// <param name="tx"></param>
        public static void CancelTransaction(ModTransaction tx, bool graceful = false)
        {

            // Readonly transactions don't really have a true cancel, or need to be cancelled, but we can at least mark them done.
            if (tx._ReadOnly)
            {
                tx.CancelTransaction();
                tx.State = ETransactionState.Closed;
                _CANCEL_BLOCKED_TX = false;
                return;
            }

            if (tx.State == ETransactionState.Closed)
            {
                // TX has already been completed/cancelled.
                return;
            }

            if (tx != _ActiveTransaction)
            {
                throw new Exception("Attempted to cancel transaction other than the current open mod transation.");
            }

            try
            {
                tx.CancelTransaction(graceful);
            }
            finally
            {
                _ActiveTransaction = null;
                tx.State = ETransactionState.Closed;
                XivCache.CacheWorkerEnabled = _WorkerStatus;
            }
        }
        private void CancelTransaction(bool graceful = false)
        {
            if (!_ReadOnly && State != ETransactionState.Closed)
            {
                State = ETransactionState.Closing;

                // Call this before cleanup.
                // That way event handlers can potentially
                // swoop in to save the TX data store/Index data if desired.
                TransactionCancelled?.Invoke(this, graceful);
                if (ActiveTransaction == this ) {
                    ActiveTransactionCancelled?.Invoke(this, graceful);
                }

                // We have to queue all of the touched files up as possibly changed in the Mod Cache.
                HashSet<string> files = new HashSet<string>();

                files.Union(_PrePrepStates.Keys);
                files.Union(_OriginalStates.Keys);
                XivCache.QueueDependencyUpdate(files);


                if (Settings.Target == ETransactionTarget.GameFiles)
                {
                    // Validate that nothing has touched our Indexes.
                    CheckWriteTimes();

                    // Reset our DAT sizes back to what they were before we started the Transaction.
                    TruncateDats();
                }
                if (_DataHandler != null)
                {
                    _DataHandler.Dispose();
                }
            }
            _IndexFiles = null;
            _ModList = null;
            ModPack = null;
        }
        private void CheckWriteTimes()
        {
            if (_ModListModifiedTime != File.GetLastWriteTimeUtc(Modding.ModListDirectory) && _ModListModifiedTime != new DateTime())
            {
                throw new Exception("Modlist file were modified since beginning transaction.  Cannot safely commit/cancel transaction");
            }

            foreach (var kv in _IndexFiles)
            {
                var dataFile = kv.Key;
                var index1Path = XivDataFiles.GetFullPath(dataFile, Index.IndexExtension);
                var index2Path = XivDataFiles.GetFullPath(dataFile, Index.Index2Extension);

                var index1Time = File.GetLastWriteTimeUtc(index1Path);
                var index2Time = File.GetLastWriteTimeUtc(index2Path);

                if (_Index1ModifiedTimes[dataFile] != index1Time
                    || _Index2ModifiedTimes[dataFile] != index2Time)
                {
                    throw new Exception("Index files were modified since beginning transaction.  Cannot safely commit/cancel transaction.");
                }
            }
        }
        private void TruncateDats()
        {
            foreach (var kv in _IndexFiles)
            {
                var dataFile = kv.Key;
                for (int i = 0; i < Dat._MAX_DATS; i++)
                {
                    var datPath = Dat.GetDatPath(dataFile, i);
                    if (File.Exists(datPath) && _DatFileSizes[dataFile].Count > i)
                    {
                        using (var fs = File.Open(datPath, FileMode.Open))
                        {
                            fs.SetLength(_DatFileSizes[dataFile][i]);
                        }
                    }
                }
            }
        }
        #endregion


        #region Shortcut Index/Modlist Accessors
        /// <summary>
        /// Syntactic shortcut for retrieving the 8x Data offset from the index files.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<long> Get8xDataOffset(string path, bool forceOriginal = false)
        {
            if (!forceOriginal)
            {
                // Standard index retrieval.
                var df = IOUtil.GetDataFileFromPath(path);
                var idx = await GetIndexFile(df);
                return idx.Get8xDataOffset(path);
            } else
            {
                var mod = (await GetModList()).GetMod(path);
                if(mod == null)
                {
                    // Standard index retrieval
                    var df = IOUtil.GetDataFileFromPath(path);
                    var idx = await GetIndexFile(df);
                    return idx.Get8xDataOffset(path);
                }
                else
                {
                    // Return original base game offset.
                    return mod.Value.OriginalOffset8x;
                }
            }
        }

        /// <summary>
        /// Retrieves a data offset Synchronously.
        /// The Index File and ModList in question must already be cached or this will error.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="forceOriginal"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public long Get8xDataOffsetSync(string path, bool forceOriginal = false)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            if (!_IndexFiles.ContainsKey(df))
            {
                throw new Exception("Cannot use Synchronous Data Offset Retrieval when Index File has not been loaded.");
            }
            if(_ModList == null)
            {
                throw new Exception("Cannot use Synchronous Data Offset Retrieval when ModList has not been loaded.");
            }

            var idx = _IndexFiles[df];
            var modList = _ModList;

            if (!forceOriginal)
            {
                // Standard index retrieval.
                return idx.Get8xDataOffset(path);
            }
            else
            {
                var mod = modList.GetMod(path);
                if (mod == null)
                {
                    return idx.Get8xDataOffset(path);
                }
                else
                {
                    // Return original base game offset.
                    return mod.Value.OriginalOffset8x;
                }
            }

        }

        /// <summary>
        /// Syntactic shortcut for retrieving the uint32 Dat-Embedded, FFXIV-Style Data offset from the index files.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<uint> GetRawDataOffset(string path, bool forceOriginal = false)
        {
            return (uint) (await Get8xDataOffset(path, forceOriginal) / 8);
        }

        /// <summary>
        /// Syntactic shortcut for validating a file exists.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<bool> FileExists(string path, bool forceOriginal = false)
        {
            var offset = await Get8xDataOffset(path, forceOriginal);
            return offset != 0;
        }

        /// <summary>
        /// Syntactic shortcut for updating a given index's offset.
        /// Takes and returns the 8x dat-embedded index offset.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        public async Task<long> Set8xDataOffset(string path, long offset8x)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var idx = await GetIndexFile(df);
            return idx.Set8xDataOffset(path, offset8x);
        }

        /// <summary>
        /// Syntactic shortcut for updating a given index's offset.
        /// Takes and returns a dat-embedded uint32 FFXIV style offset.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="sqOffset"></param>
        /// <returns></returns>
        public async Task<uint> SetRawDataOffset(string path, uint sqOffset)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var idx = await GetIndexFile(df);
            return idx.SetRawDataOffset(path, sqOffset);
        }

        /// <summary>
        /// Syntactic shortcut for retrieving a mod (if it exists) at a given path.
        /// Returns null if the mod does not exist.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<Mod?> GetMod(string path)
        {
            var ml = await GetModList();
            return ml.GetMod(path);
        }

        /// <summary>
        /// Adds, Removes, or Updates mod as dictated by the given Mod state and path.
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="path"></param>
        public async Task UpdateMod(Mod? mod, string path)
        {
            var ml = await GetModList();
            if (mod == null)
            {
                ml.RemoveMod(path);
            }
            else
            {
                if(path != mod.Value.FilePath)
                {
                    throw new InvalidDataException("Mod path did not match given path.");
                }

                ml.AddOrUpdateMod(mod.Value);
            }
        }

        /// <summary>
        /// Syntactic shortcut for adding or updating a mod.
        /// </summary>
        /// <param name="mod"></param>
        public async Task AddOrUpdateMod(Mod mod)
        {
            var ml = await GetModList();
            ml.AddOrUpdateMod(mod);
        }

        /// <summary>
        /// Syntactic shortcut for removing a mod.
        /// </summary>
        /// <param name="mod"></param>
        public async Task RemoveMod(Mod mod)
        {
            var ml = await GetModList();
            ml.RemoveMod(mod);
        }

        /// <summary>
        /// Syntactic shortcut for removing a subset of mods.
        /// </summary>
        /// <param name="mods"></param>
        public async Task RemoveMod(IEnumerable<Mod> mods)
        {
            var ml = await GetModList();
            ml.RemoveMods(mods);
        }

        #endregion


        #region Internals

        private void CheckStateData(string path)
        {

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new Exception("Internal File Path was invalid (NULL or Empty).");
            }

            if (ReadOnly)
            {
                // Don't need to do anything here.
                return;
            }

            if (State == ETransactionState.Preparing && _PrePrepStates.ContainsKey(path))
            {
                return;
            }

            if (_OriginalStates.ContainsKey(path))
            {
                return;
            }

            var data = new TxFileState(path);
            if (State == ETransactionState.Preparing)
            {
                // If we're in prep state, note that the file was added in prep.
                _PrePrepStates.Add(path, data);
            }
            else
            {
                _OriginalStates.Add(path, data);
            }
        }
        private TxFileState GetOrCreateStateBackup(string path)
        {
            CheckStateData(path);
            if (State == ETransactionState.Preparing)
            {
                return _PrePrepStates[path];
            }
            else
            {
                return _OriginalStates[path];
            }
        }

        /// <summary>
        /// Resets the state of a given file to the pre-transaction state, or optionally to the pre-prep state of the file.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public async Task ResetFile(string file, bool prePrep = false)
        {
            await RestoreFileState(await GetPreTransactionState(file, prePrep));
        }
        public async Task<TxFileState> GetPreTransactionState(string file, bool prePrep = false)
        {
            TxFileState data = null;
            if (_OriginalStates.ContainsKey(file))
            {
                data = _OriginalStates[file];
            }

            if (_PrePrepStates.ContainsKey(file) && prePrep)
            {
                data = _PrePrepStates[file];
            }

            if (data == null)
            {
                data = await SaveFileState(file);
            }
            return data;
        }

        /// <summary>
        /// Retrieves the real offset written over the given temporary offset when the transaction was committed.
        /// Returns 0 if the file was not written to the final transaction state, or if the final transaction was to 
        /// a data store other than the game files.
        /// </summary>
        /// <param name="df"></param>
        /// <param name="temporary8xOffset"></param>
        /// <returns></returns>
        public long GetRealOffsetFromTempOffset(XivDataFile df, long temporary8xOffset)
        {
            if (!_TempToRealOffsetMapping.ContainsKey(df))
            {
                return 0;
            }
            if (!_TempToRealOffsetMapping[df].ContainsKey(temporary8xOffset))
            {
                return 0;
            }
            return _TempToRealOffsetMapping[df][temporary8xOffset];
        }


        private async Task WriteDisabledMods(Dictionary<string, (long RealOffset, long TempOffset)> writtenOffsets)
        {
            if(Settings.Target != ETransactionTarget.GameFiles || !AffectsGameFiles)
            {
                return;
            }

            var ret = new Dictionary<string, long>();

            // Here we have to handle writing disabled mods.
            var modlist = await GetModList();
            var mods = modlist.GetMods().ToList();
            foreach (var mod in mods)
            {
                if (writtenOffsets.ContainsKey(mod.FilePath))
                {
                    // Mod was already written.
                    continue;
                }

                var df = mod.DataFile;
                if (!_TemporaryOffsetMapping.ContainsKey(df))
                    continue;

                if (!_TemporaryOffsetMapping[df].ContainsKey(mod.ModOffset8x))
                    continue;

                // Ok, if we got here, we have a mod with a temporary transaction offset.
                // But the mod data didn't get written yet, because it wasn't enabled in the indexes,
                // And the data didn't already exist in the DATs to start.

                var path = mod.FilePath;
                var tempOffset = mod.ModOffset8x;

                var forceType2 = path.EndsWith(".atex");
                
                // Retrieve the compressed data and write it to the DATs.
                var data = await _DataHandler.GetCompressedFile(df, tempOffset, forceType2);
                var realOffset = (await Dat.Unsafe_WriteToDat(data, df)) * 8L;

                writtenOffsets.Add(path, (realOffset, tempOffset));
            }
        }


        /// <summary>
        /// Internal listener function for updates to our constituend modlist file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="originalMod"></param>
        /// <param name="newMod"></param>
        /// <exception cref="Exception"></exception>
        internal void INTERNAL_OnModUpdate(string path, Mod? originalMod, Mod? newMod)
        {
            if (ReadOnly)
            {
                throw new Exception("Attempted to write to ModList inside a ReadOnly Transaction.");
            }

            if (State != ETransactionState.Open && State != ETransactionState.Preparing && State != ETransactionState.Closing)
            {
                throw new Exception("Attempted to write to ModList during invalid Transaction State.");
            }

            var data = GetOrCreateStateBackup(path);
            if (!data.OriginalMod_Set)
            {
                data.OriginalMod = originalMod;
            }
        }

        /// <summary>
        /// Internal listener function for updates to our constintuent index files.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="originalOffset"></param>
        /// <param name="updatedOffset"></param>
        internal void INTERNAL_OnIndexUpdate(XivDataFile dataFile, string path, long originalOffset, long updatedOffset)
        {
            if (ReadOnly)
            {
                // This should never actually called since Readonly TX don't use our wrapped index files currently.
                throw new Exception("Attempted to write to index files inside a ReadOnly Transaction.");
            }

            if(State != ETransactionState.Open && State != ETransactionState.Preparing && State != ETransactionState.Closing)
            {
                throw new Exception("Attempted to write to index files during invalid Transaction State.");
            }

            if(originalOffset == updatedOffset)
            {
                return;
            }

            var data = GetOrCreateStateBackup(path);

            if (!data.OriginalOffset_Set)
            {
                data.OriginalOffset = originalOffset;
            }

            if (!_TemporaryOffsetMapping.ContainsKey(dataFile))
            {
                _TemporaryOffsetMapping.Add(dataFile, new Dictionary<long, HashSet<string>>());
            }


            // If we have an old file pointer.
            if (_TemporaryOffsetMapping[dataFile].ContainsKey(originalOffset) && _TemporaryOffsetMapping[dataFile][originalOffset].Contains(path))
            {
                // And the file has a live modification.
                if (_OriginalStates.ContainsKey(path))
                {
                    // And we're currently /back/ in prep mode...
                    if(State == ETransactionState.Preparing)
                    {
                        // Uhh... Fuck.  This is a really complicated state that we don't know how to handle just yet.
                        throw new Exception("Cannot update Prep mode Index Offset over Modified Transaction Index Offset.");
                    }
                }
            }

            // Update the Offset's pathlist.
            if (!_TemporaryOffsetMapping[dataFile].ContainsKey(updatedOffset))
            {
                _TemporaryOffsetMapping[dataFile][updatedOffset] = new HashSet<string>();
            }
            _TemporaryOffsetMapping[dataFile][updatedOffset].Add(path);


            // Remove from old Offset's pathlist.
            if (_TemporaryOffsetMapping[dataFile].ContainsKey(originalOffset))
            {
                _TemporaryOffsetMapping[dataFile][originalOffset].Remove(path);
            }
        }

        /// <summary>
        /// Retrieves the file paths currently pointing to a given temporary offset.
        /// </summary>
        /// <param name="df"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        internal HashSet<string> GetFilePathsFromTempOffset(XivDataFile df, long offset)
        {
            if (!_TemporaryOffsetMapping.ContainsKey(df))
                return new HashSet<string>();
            if (!_TemporaryOffsetMapping[df].ContainsKey(offset))
                return new HashSet<string>();
            return _TemporaryOffsetMapping[df][offset];
        }

        internal bool IsPrepFile(string path)
        {
            var inPrep = _PrePrepStates.ContainsKey(path);
            var inLive = _OriginalStates.ContainsKey(path);

            return inPrep && !inLive;
        }

        /// <summary>
        /// Gets the next temporary offset to use for transaction storage file writing.
        /// These start at UINT.MAX * 8 and decrement down by 16.  (By functionally 1 for the real uint based offset pointer each time)
        /// These offsets are then replaced with real offsets when the transaction is committed.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        private long GetNextTempOffset(XivDataFile dataFile)
        {
            if (!_NextDataOffset.ContainsKey(dataFile))
            {
                // Start at Max value (without synonym flag) and go down.
                _NextDataOffset.Add(dataFile, uint.MaxValue - 1);
            }

            var offset = _NextDataOffset[dataFile];

            // Decrement by 16, which is really just 1 less in the actual offset field.
            _NextDataOffset[dataFile] = _NextDataOffset[dataFile] - 16;

            long longOffset = offset * 8L;

            return longOffset;
        }

        #endregion


        #region Raw File I/O

        /// <summary>
        /// Retrieves the data for a given path/mod status.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        public async Task<byte[]> ReadFile(string path, bool forceOriginal = false, bool compressed = false)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var offset = await Get8xDataOffset(path, forceOriginal);

            return await ReadFile(df, offset, compressed);
        }

        /// <summary>
        /// Retrieves the data for a given data file/offset key.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        public async Task<byte[]> ReadFile(XivDataFile dataFile, long offset8x, bool compressed = false)
        {
            if (compressed)
            {
                return await _DataHandler.GetCompressedFile(dataFile, offset8x);
            } else
            {
                return await _DataHandler.GetUncompressedFile(dataFile, offset8x);
            }
        }

        /// <summary>
        /// Syntactic shortcut to Dat.WriteModFile()
        /// </summary>
        /// <param name="path"></param>
        /// <param name="data"></param>
        /// <param name="sourceApplication"></param>
        /// <returns></returns>
        public async Task<long> WriteFile(string path, byte[] data, string sourceApplication = "Unknown")
        {

            return await Dat.WriteModFile(data, path, sourceApplication, null, this);
        }


        /// <summary>
        /// Gets the SqPack type of a file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="forceOriginal"></param>
        /// <returns></returns>
        public async Task<uint> GetSqPackType(string path, bool forceOriginal = false)
        {
            var forceType2 = path.EndsWith(".atex");
            var offset = await Get8xDataOffset(path, forceOriginal);
            return await GetSqPackType(IOUtil.GetDataFileFromPath(path), offset, forceType2);
        }

        /// <summary>
        /// Gets the SqPack type of a file.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset"></param>
        /// <param name="forceType2"></param>
        /// <returns></returns>
        public async Task<uint> GetSqPackType(XivDataFile dataFile, long offset, bool forceType2 = false)
        {
            var parts = IOUtil.Offset8xToParts(offset);
            using (var br = await _DataHandler.GetCompressedFileStream(dataFile, parts.Offset))
            {
                return Dat.GetSqPackType(br, offset);
            }
        }


        /// <summary>
        /// Writes the given data to the default transaction data store for the data file, returning the next available placeholder offset.
        /// Returns the 8x Dat-embeded placeholder transactionary offset.
        /// Does /NOT/ update any index file offsets.
        /// 
        /// Should largely only be used by Dat.WriteModFile() unless you want to use the transaction data store as as scratch pad.
        /// Files stored in the transaction data store without a valid game path in the indexes will not be written on transaction commit.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        internal async Task<long> WriteData(XivDataFile dataFile, byte[] data, bool compressed = false)
        {
            var offset = GetNextTempOffset(dataFile);
            await WriteData(dataFile, offset, data, compressed);

            return offset;
        }


        /// <summary>
        /// Data-Writing function that allows for setting explicit transaction store information.
        /// Mostly useful when we know we're performing a task that would have significant performance
        /// penalties when stored in the wrong [Compressed/Uncompressed] state.
        /// </summary>
        /// <param name="storageSettings"></param>
        /// <param name="dataFile"></param>
        /// <param name="data"></param>
        /// <param name="compressed"></param>
        /// <returns></returns>
        internal async Task<long> ExplicitWriteData(FileStorageInformation storageSettings, XivDataFile dataFile, byte[] data, bool compressed = false)
        {
            var offset = GetNextTempOffset(dataFile);
            await WriteData(dataFile, offset, data, compressed);
            await _DataHandler.WriteFile(storageSettings, dataFile, offset, data, compressed);
            return offset;
        }

        /// <summary>
        /// Gets the temp folder used by the internal transaction store.
        /// </summary>
        /// <returns></returns>
        internal string UNSAFE_GetTransactionStore()
        {
            return _DataHandler.DefaultPathRoot;
        }

        /// <summary>
        /// Adds a file storage handle into the internal data store.
        /// Does not add any raw data in the process.  The file is expected to exist and be managed by the caller,
        /// or be manually added to the data manager's temp path.
        /// </summary>
        /// <param name="storageSettings"></param>
        /// <param name="dataFile"></param>
        internal long UNSAFE_AddFileInfo(FileStorageInformation storageSettings, XivDataFile dataFile)
        {
            var offset = GetNextTempOffset(dataFile);
            _DataHandler.UNSAFE_AddFileInfo(storageSettings, dataFile, offset);
            return offset;
        }

        /// <summary>
        /// Writes the given data to the default transaction data store, keyed to the given data file/offset key.
        /// Does /NOT/ update any index file offsets, and expects a transaction temporary offset.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <param name="compressed"></param>
        /// <returns></returns>
        private async Task WriteData(XivDataFile dataFile, long offset8x, byte[] data, bool compressed = false)
        {
            await _DataHandler.WriteFile(dataFile, offset8x, data, compressed);
        }

        /// <summary>
        /// Retrieve a readable file stream to the base file.
        /// Depending on the file store, this may be a direct stream to a file on disk, or may have to compress/decompress
        /// the data first before attaching it to a memorystream.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="forceOriginal"></param>
        /// <param name="compressed"></param>
        /// <returns></returns>
        public async Task<BinaryReader> GetFileStream(string path, bool forceOriginal = false, bool compressed = false)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var offset = await Get8xDataOffset(path, forceOriginal);
            return await GetFileStream(df, offset, compressed);
        }

        /// <summary>
        /// Retrieve a readable file stream to the base file.
        /// Depending on the file store, this may be a direct stream to a file on disk, or may have to compress/decompress
        /// the data first before attaching it to a memorystream.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <param name="compressed"></param>
        /// <returns></returns>
        public async Task<BinaryReader> GetFileStream(XivDataFile dataFile, long offset8x, bool compressed = false)
        {
            if (compressed)
            {
                return await _DataHandler.GetCompressedFileStream(dataFile, offset8x);
            }
            else
            {
                return await _DataHandler.GetUncompressedFileStream(dataFile, offset8x);
            }
        }


        /// <summary>
        /// Gets the Compressed/SQPacked size of a file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="forceOriginal"></param>
        /// <returns></returns>
        public async Task<int> GetCompressedFileSize(string path, bool forceOriginal = false)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var offset = await Get8xDataOffset(path, forceOriginal);
            var size =  await GetCompressedFileSize(df, offset);
            return size;
        }
        public async Task<int> GetCompressedFileSize(XivDataFile dataFile, long offset8x)
        {
            using (var stream = await GetFileStream(dataFile, offset8x, true))
            {
                var size =  Dat.GetCompressedFileSize(stream);
                return size;
            }
        }


        /// <summary>
        /// Gets the uncompressed/un-SQPacked size of a file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="forceOriginal"></param>
        /// <returns></returns>
        public async Task<int> GetUncompressedFileSize(string path, bool forceOriginal = false)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var offset = await Get8xDataOffset(path, forceOriginal);
            return GetUncompressedFileSize(df, offset);
        }
        public int GetUncompressedFileSize(XivDataFile dataFile, long offset8x)
        {
            return _DataHandler.GetUncompressedSize(dataFile, offset8x);
        }

        #endregion


        #region Save/Restore Functions

        /// <summary>
        /// Saves the full state of a given file, returning a TxPathData object that can be used to restore that state later.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<TxFileState> SaveFileState(string path) {

            var data = new TxFileState(path);

            var offset = await Get8xDataOffset(path);
            var mod = await GetMod(path);
            data.OriginalMod = mod;
            data.OriginalOffset = offset;
            return data;
        }

        /// <summary>
        /// Restores the full state of a given file to the given state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public async Task RestoreFileState(TxFileState state)
        {
            if (state.OriginalOffset_Set)
            {
                await Set8xDataOffset(state.Path, state.OriginalOffset);
            }

            if (state.OriginalMod_Set)
            {
                await UpdateMod(state.OriginalMod, state.Path);
            }

            if (state.Path.EndsWith(".meta"))
            {
                await ItemMetadata.ApplyMetadata(state.Path, false, this);
            } else if(state.Path.EndsWith(".rgsp"))
            {
                await CMP.ApplyRgspFile(state.Path, false, this);
            }
        }

        #endregion


        #region Static Default Settings

        private static ModTransactionSettings _DefaultSettings = new ModTransactionSettings()
        {
            StorageType = EFileStorageType.CompressedIndividual,
            Target = ETransactionTarget.GameFiles,
            TargetPath = null
        };
        public static ModTransactionSettings? GetDefaultSettings()
        {
            return _DefaultSettings;
        }
        public static void SetDefaultTransactionSettings(ModTransactionSettings settings)
        {
            _DefaultSettings = settings;
        }

        #endregion
    }
}

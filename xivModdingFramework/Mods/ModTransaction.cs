using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Mods.DataContainers;
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
        #endregion


        #region Properties and Accessors

        private Dictionary<XivDataFile, IndexFile> _IndexFiles = new Dictionary<XivDataFile, IndexFile>();
        private Dictionary<XivDataFile, uint> _NextDataOffset = new Dictionary<XivDataFile, uint>();


        // Collections used in data tracking of modified files.
        // Used to remap temporary files on commit.
        private Dictionary<string, long> _TemporaryPathMapping = new Dictionary<string, long>();
        private Dictionary<string, long> _OriginalOffsets = new Dictionary<string, long>();
        private Dictionary<XivDataFile, Dictionary<long, List<string>>> _TemporaryOffsetMapping = new Dictionary<XivDataFile, Dictionary<long, List<string>>>();
        private HashSet<string> _PrepFiles = new HashSet<string>();

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

        private TransactionDataHandler _DataHandler;

        private SqPack.FileTypes.Index __Index;
        private Modding __Modding;
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
        public ModPack ModPack { get; set; }


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


        public async Task<IndexFile> GetIndexFile(XivDataFile dataFile)
        {
            if(!_IndexFiles.ContainsKey(dataFile))
            {
                if (!_ReadOnly)
                {
                    var index1Path = Path.Combine(_GameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index.IndexExtension}");
                    var index2Path = Path.Combine(_GameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index.Index2Extension}");

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

                var idx = await __Index.GetIndexFile(dataFile, false, _ReadOnly);
                _IndexFiles.Add(dataFile, idx);

            }
            return _IndexFiles[dataFile];
        } 

        public async Task<ModList> GetModList()
        {
            if(_ModList == null)
            {
                _ModListModifiedTime = File.GetLastWriteTimeUtc(__Modding.ModListDirectory.FullName);
                _ModList = await __Modding.GetModList();
            }
            return _ModList;
        }

        #endregion


        #region Constructor/Disposable Pattern
        public ModTransaction()
        {
            throw new NotImplementedException("Mod Transactions must be created via ModTransaction.Begin()");
        }
        private ModTransaction(bool readOnly = false, ModPack modpack = null, ModTransactionSettings? settings = null, bool waitToStart = false)
        {
            _GameDirectory = XivCache.GameInfo.GameDirectory;
            ModPack = modpack;
            __Index = new SqPack.FileTypes.Index(XivCache.GameInfo.GameDirectory);
            __Modding = new Modding(XivCache.GameInfo.GameDirectory);

            _ReadOnly = readOnly;
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
        public static ModTransaction BeginTransaction(bool readOnly = false, ModPack modpack = null, ModTransactionSettings? settings = null, bool waitToStart = false)
        {

            if (readOnly)
            {
                // Read-Only Transactions don't block anything else, and really just serve as
                // caches for index/modlist data.
                var readonlyTx = new ModTransaction(readOnly, modpack);
                return readonlyTx;
            }

            if (_ActiveTransaction != null)
            {
                throw new Exception("Cannot have two write-enabled mod transactions open simultaneously.");
            }

            // Disable the cache worker during transactions.
            _WorkerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;

            var tx = new ModTransaction(readOnly, modpack, settings, waitToStart);
            _ActiveTransaction = tx;

            if (!Dat.AllowDatAlteration && tx.Settings.Target == ETransactionTarget.GameFiles)
            {
                CancelTransaction(tx);
                throw new Exception("Cannot open write transaction while DAT writing is disabled.");
            }


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

            // Perform, in order...
            // DATA WRITE => MODLIST WRITE => INDEX WRITE
            // This way if anything breaks, it is least dangerous.

            // Writing to dats/data store is largely harmless, as we can just truncate the file back.
            // Saving the modlist is fine once the data is in the dats.  Just need to re-validate mod eanbled status.
            // Once Index save is done, so are we.

            // Write data from the transaction store to the real data target.
            var pathMap = await _DataHandler.WriteAllToTarget(Settings, this);

            // If the data handler returned a null, that means we aren't doing
            // anything else to the base game files/modlist here.
            if (pathMap != null)
            {
                // Update all the indexes with the real, post-write offsets.
                foreach (var kv in pathMap)
                {
                    if (IsPrepFile(kv.Key))
                    {
                        // Prep files have their index restored instead.
                        var offset = await GetPreTransactionOffset(kv.Key);
                        await Set8xDataOffset(kv.Key, offset);
                        continue;
                    }

                    var lastOffset = await Set8xDataOffset(kv.Key, kv.Value.RealOffset);
                    if (lastOffset != kv.Value.TempOffset)
                    {
                        throw new Exception("Temp-Real Offset mismatch.");
                    }
                    var df = IOUtil.GetDataFileFromPath(kv.Key);

                    if (!_TempToRealOffsetMapping.ContainsKey(df))
                    {
                        _TempToRealOffsetMapping.Add(df, new Dictionary<long, long>());
                    }

                    _TempToRealOffsetMapping[df].Add(kv.Value.TempOffset, kv.Value.RealOffset);
                }

                // We only write to the modlist for actual game data saves.
                if (_ModList != null && AffectsGameFiles)
                {
                    // Update the modlist with the real, post-write offsets.
                    foreach(var kv in pathMap)
                    {
                        if (IsPrepFile(kv.Key))
                        {
                            // TODO: Need to restore the Mod entry here.
                            // But that involves Wrapping the Modlist class...
                            // That's going to be a pain.
                            continue;
                        }

                        var mod = _ModList.GetMod(kv.Key);
                        if (mod != null)
                        {
                            mod.data.modOffset = kv.Value.RealOffset;
                            if(mod.data.originalOffset == kv.Value.TempOffset)
                            {
                                mod.data.originalOffset = kv.Value.RealOffset;
                            }
                        }
                    }

                    await __Modding.SaveModListAsync(_ModList);
                }

                // We only write index files if we're writing to a game file store.
                if (Settings.Target == ETransactionTarget.GameFiles)
                {
                    foreach (var index in _IndexFiles)
                    {
                        index.Value.Save();
                    }
                }
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
                XivCache.CacheWorkerEnabled = XivCache.CacheWorkerEnabled;
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

                if (Settings.Target == ETransactionTarget.GameFiles)
                {
                    // Validate that nothing has touched our Indexes.
                    CheckWriteTimes();

                    // Reset our DAT sizes back to what they were before we started the Transaction.
                    TruncateDats();
                }
                _DataHandler.Dispose();
            }
            _IndexFiles = null;
            _ModList = null;
            ModPack = null;
        }
        private void CheckWriteTimes()
        {
            if (_ModListModifiedTime != File.GetLastWriteTimeUtc(__Modding.ModListDirectory.FullName) && _ModListModifiedTime != new DateTime())
            {
                throw new Exception("Modlist file were modified since beginning transaction.  Cannot safely commit/cancel transaction");
            }

            foreach (var kv in _IndexFiles)
            {
                var dataFile = kv.Key;
                var index1Path = Path.Combine(_GameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index.IndexExtension}");
                var index2Path = Path.Combine(_GameDirectory.FullName, $"{dataFile.GetDataFileName()}{Index.Index2Extension}");

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


        #region Shortcut Accessors
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
                    // Re-call to default path.
                    return await Get8xDataOffset(path, false);
                }
                else
                {
                    // Return original base game offset.
                    return mod.data.originalOffset;
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
        public async Task<bool> FileExists(string path)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var idx = await GetIndexFile(df);
            return idx.FileExists(path);
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
        public async Task<Mod> GetMod(string path)
        {
            var ml = await GetModList();
            return ml.GetMod(path);
        }


        /// <summary>
        /// Retrieves the real offset written over the given temporary offset when the transaction was committed.
        /// Returns 0 if the file was not written to the final transaction state.
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


        /// <summary>
        /// Resets a given file's Index pointer back to it's pre-Transaction state.
        /// Does not interact with the ModList at all.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public async Task ResetFile(string file)
        {
            if (!_OriginalOffsets.ContainsKey(file))
            {
                return;
            }
            await Set8xDataOffset(file, _OriginalOffsets[file]);
        }

        /// <summary>
        /// Gets the pre-transaction offset for this file.
        /// May or may not be a modded file.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public async Task<long> GetPreTransactionOffset(string file)
        {
            if (_OriginalOffsets.ContainsKey(file))
            {
                return _OriginalOffsets[file];
            }

            return await Get8xDataOffset(file);
        }
        #endregion


        #region Raw Data I/O

        /// <summary>
        /// Internal listener function for updates to our constintuent index files.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="originalOffset"></param>
        /// <param name="updatedOffset"></param>
        internal void INTERNAL_OnIndexUpdate(XivDataFile dataFile, string path, long originalOffset, long updatedOffset)
        {
            if(State != ETransactionState.Open && State != ETransactionState.Preparing)
            {
                throw new Exception("Attempted to write to index files in a non Open or Preparing Transaction.");
            }

            if (State == ETransactionState.Preparing)
            {
                // If we're in prep state, note that the file was added in prep.
                _PrepFiles.Add(path);
            } else
            {
                // Otherwise, make sure it's not included in prep anymore, if we wrote over it.
                _PrepFiles.Remove(path);
            }

            // Update the Path's pointer.
            if (!_TemporaryPathMapping.ContainsKey(path))
            {
                _TemporaryPathMapping.Add(path, updatedOffset);
            }
            else
            {
                _TemporaryPathMapping[path] = updatedOffset;
            }

            // Update the Offset's pathlist.
            if (!_TemporaryOffsetMapping.ContainsKey(dataFile))
            {
                _TemporaryOffsetMapping.Add(dataFile, new Dictionary<long, List<string>>());
            }

            if (!_TemporaryOffsetMapping[dataFile].ContainsKey(updatedOffset))
            {
                _TemporaryOffsetMapping[dataFile][updatedOffset] = new List<string>();
            }
            _TemporaryOffsetMapping[dataFile][updatedOffset].Add(path);

            // Remove from old Offset's pathlist.
            if (_TemporaryOffsetMapping[dataFile].ContainsKey(originalOffset))
            {
                _TemporaryOffsetMapping[dataFile][originalOffset].Remove(path);
            }

            // Store the original offset if this is the first time the path has been modified.
            if (!_OriginalOffsets.ContainsKey(path))
            {
                _OriginalOffsets.Add(path, originalOffset);
            }
        }

        internal List<string> GetFilePathsFromTempOffset(XivDataFile df, long offset)
        {
            if (!_TemporaryOffsetMapping.ContainsKey(df))
                return new List<string>();
            if (!_TemporaryOffsetMapping[df].ContainsKey(offset))
                return new List<string>();
            return _TemporaryOffsetMapping[df][offset];
        }

        internal bool IsPrepFile(string path)
        {
            return _PrepFiles.Contains(path);
        }

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
            var dat = new Dat(XivCache.GameInfo.GameDirectory);

            return await dat.WriteModFile(data, path, sourceApplication, null, this);
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

        /// <summary>
        /// Writes the given data to the default transaction data store for the data file, returning the next available placeholder offset.
        /// Returns the 8x Dat-embeded placeholder transactionary offset.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task<long> WriteData(XivDataFile dataFile, byte[] data, bool compressed = false)
        {
            var offset = GetNextTempOffset(dataFile);
            await WriteData(dataFile, offset, data, compressed);

            return offset;
        }

        /// <summary>
        /// Writes the given data to the default transaction data store, keyed to the given data file/offset key.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <param name="compressed"></param>
        /// <returns></returns>
        internal async Task WriteData(XivDataFile dataFile, long offset8x, byte[] data, bool compressed = false)
        {
            await _DataHandler.WriteFile(dataFile, offset8x, data, compressed);
        }

        /// <summary>
        /// Retrieve an SQPack File read stream for a given data file and offset.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        internal async Task<BinaryReader> GetCompressedFileStream(XivDataFile dataFile, long offset8x)
        {
            var data = await _DataHandler.GetCompressedFile(dataFile, offset8x);
            return new BinaryReader(new MemoryStream(data));
        }

        /// <summary>
        /// Retrieve an uncompressed read stream for a given data file and offset.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        internal async Task<BinaryReader> GetUncompressedFilesStream(XivDataFile dataFile, long offset8x)
        {
            var data = await _DataHandler.GetUncompressedFile(dataFile, offset8x);
            return new BinaryReader(new MemoryStream(data));
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

using HelixToolkit.SharpDX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
    public class ModTransaction : IDisposable
    {

        #region Properties and Accessors

        private Dictionary<XivDataFile, IndexFile> _IndexFiles = new Dictionary<XivDataFile, IndexFile>();

        // Modified times the first time we access the index files.
        private Dictionary<XivDataFile, DateTime> _Index1ModifiedTimes = new Dictionary<XivDataFile, DateTime>();
        private Dictionary<XivDataFile, DateTime> _Index2ModifiedTimes = new Dictionary<XivDataFile, DateTime>();

        // File sizes of the .DAT files the first time we encountered them.
        private Dictionary<XivDataFile, List<long>> _DatFileSizes = new Dictionary<XivDataFile, List<long>>();

        private DateTime _ModListModifiedTime;

        private ModList _ModList;
        public ModPack ModPack { get; set; }
        private readonly bool _ReadOnly = false;
        private bool _Finished = false;
        private TransactionDataHandler _DataHandler;

        private SqPack.FileTypes.Index __Index;
        private Modding __Modding;
        private DirectoryInfo _GameDirectory;

        private static ModTransaction _OpenTransaction = null;
        internal ModTransaction ActiveTransaction
        {
            get
            {
                return _OpenTransaction;
            }
        }

        private static bool _WorkerStatus = false;
        private bool _Disposed;

        public List<XivDataFile> ActiveDataFiles
        {
            get
            {
                return _IndexFiles.Select(x => x.Key).ToList();
            }
        }

        public static bool OpenTransaction
        {
            get
            {
                return _OpenTransaction != null;
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
        private ModTransaction(bool readOnly = false, ModPack modpack = null)
        {
            _GameDirectory = XivCache.GameInfo.GameDirectory;
            ModPack = modpack;
            __Index = new SqPack.FileTypes.Index(XivCache.GameInfo.GameDirectory);
            __Modding = new Modding(XivCache.GameInfo.GameDirectory);

            // NOTE: Readonly Transactions should not implement anything that requires disposal via IDisposable.
            // Readonly Tx are intended to be lightweight and used in non-disposable/standard memory managed contexts.
            _ReadOnly = readOnly;
            if (_ReadOnly)
            {
                // Readonly Data Handlers do not technically need to be disposed as they never create a data store.
                _DataHandler = new TransactionDataHandler(EFileStorageType.ReadOnly);
            } else
            {
                _DataHandler = new TransactionDataHandler(EFileStorageType.UncompressedIndividual);
            }
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (!_Finished)
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
        /// Opens a new mod transaction.
        /// Transactions will still write data to DATs in real time, but will cache index and modlist changes until they are committed.
        /// </summary>
        /// <param name="modpack"></param>
        /// <returns></returns>
        public static ModTransaction BeginTransaction(bool readOnly = false, ModPack modpack = null)
        {
            if (readOnly)
            {
                // Read-Only Transactions don't block anything else, and really just serve as
                // caches for index/modlist data.
                var readonlyTx = new ModTransaction(readOnly, modpack);
                return readonlyTx;
            }

            if (OpenTransaction)
            {
                throw new Exception("Cannot have two open mod transactions simultaneously.");
            }

            // Disable the cache worker during transactions.
            _WorkerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;

            var tx = new ModTransaction(readOnly, modpack);
            _OpenTransaction = tx;
            return tx;
        }

        /// <summary>
        /// Commits this transaction to the game files.
        /// This causes Index and Modlist writes to disk for all affected mods.
        /// </summary>
        /// <returns></returns>
        public static async Task CommitTransaction(ModTransaction tx)
        {
            if (tx != _OpenTransaction)
            {
                throw new Exception("Attempted to commit transaction other than the current open mod transation.");
            }

            try
            {
                await tx.CommitTransaction();
            }
            finally
            {
                _OpenTransaction = null;
                tx._Finished = true;
                XivCache.CacheWorkerEnabled = XivCache.CacheWorkerEnabled;
            }
        }
        private async Task CommitTransaction()
        {
            if (_ReadOnly)
            {
                throw new Exception("Attempted to commit a Read Only Transaction.");
            }

            // Lumina import mode does not write to came indexes/modlist.
            if (XivCache.GameInfo.UseLumina)
            {
                return;
            }

            CheckWriteTimes();

            foreach (var index in _IndexFiles)
            {
                index.Value.Save();
            }
            if (_ModList != null)
            {
                await __Modding.SaveModListAsync(_ModList);
            }
        }

        /// <summary>
        /// Cancels the given transaction.
        /// This discards the internal cached index and modlist pointers and truncates the .DAT files back to their pre-transaction states.
        /// </summary>
        /// <param name="tx"></param>
        public static void CancelTransaction(ModTransaction tx)
        {

            // Readonly transactions don't really have a true cancel, or need to be cancelled, but we can at least mark them done.
            if (tx._ReadOnly)
            {
                tx.CancelTransaction();
                tx._Finished = true;
                return;
            }

            if (tx != _OpenTransaction)
            {
                throw new Exception("Attempted to cancel transaction other than the current open mod transation.");
            }

            try
            {
                tx.CancelTransaction();
            }
            finally
            {
                _OpenTransaction = null;
                tx._Finished = true;
                XivCache.CacheWorkerEnabled = XivCache.CacheWorkerEnabled;
            }
        }
        private void CancelTransaction()
        {
            if (!_ReadOnly && !_Finished)
            {
                // Validate that nothing has touched our Indexes.
                CheckWriteTimes();

                // Reset our DAT sizes back to what they were before we started the Transaction.
                TruncateDats();
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


        #region Index File Shortcut Accessors
        /// <summary>
        /// Syntactic shortcut for retrieving the 8x Data offset from the index files.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<long> Get8xDataOffset(string path)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var idx = await GetIndexFile(df);
            return idx.Get8xDataOffset(path);
        }
        /// <summary>
        /// Syntactic shortcut for retrieving the uint32 Dat-Embedded, FFXIV-Style Data offset from the index files.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<uint> GetRawOffset(string path)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var idx = await GetIndexFile(df);
            return idx.GetRawDataOffset(path);
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
        public async Task<uint> UpdateDataOffset(string path, long offset8x)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var idx = await GetIndexFile(df);
            return 8 * idx.SetDataOffset(path, offset8x);
        }

        /// <summary>
        /// Syntactic shortcut for updating a given index's offset.
        /// Takes and returns a dat-embedded uint32 FFXIV style offset.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="sqOffset"></param>
        /// <returns></returns>
        public async Task<uint> UpdateDataOffset(string path, uint sqOffset)
        {
            var df = IOUtil.GetDataFileFromPath(path);
            var idx = await GetIndexFile(df);
            return idx.SetDataOffset(path, sqOffset);
        }
        #endregion


        #region Raw Data I/O

        /// <summary>
        /// Retrieves the data for a given data file/offset key.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <returns></returns>
        public async Task<byte[]> GetData(XivDataFile dataFile, long offset8x, bool compressed = false)
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
        /// Writes the given data to the default transaction data store, keyed to the given data file/offset key.
        /// </summary>
        /// <param name="dataFile"></param>
        /// <param name="offset8x"></param>
        /// <param name="compressed"></param>
        /// <returns></returns>
        public async Task WriteData(XivDataFile dataFile, long offset8x, byte[] data, bool compressed = false)
        {
            await _DataHandler.WriteFile(dataFile, offset8x, data, compressed);
        }

        #endregion

    }
}

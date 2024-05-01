using HelixToolkit.SharpDX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Mods
{
    public class ModTransaction
    {
        private Dictionary<XivDataFile, IndexFile> _IndexFiles = new Dictionary<XivDataFile, IndexFile>();
        private ModList _ModList;
        private ModPack _ModPack;
        private bool _ReadOnly = false;

        private SqPack.FileTypes.Index __Index;
        private Modding __Modding;

        private static ModTransaction _OpenTransaction = null;
        private static bool _WorkerStatus = false;

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
                var idx = await __Index.GetIndexFile(dataFile, false, _ReadOnly);
                _IndexFiles.Add(dataFile, idx);
            }
            return _IndexFiles[dataFile];
        } 

        public async Task<ModList> GetModList()
        {
            if(_ModList == null)
            {
                _ModList = await __Modding.GetModListAsync();
            }
            return _ModList;
        }

        public ModPack GetModPack()
        {
            return _ModPack;
        }

        private ModTransaction(ModPack modpack = null, bool readOnly = false)
        {
            _ModPack = modpack;
            __Index = new SqPack.FileTypes.Index(XivCache.GameInfo.GameDirectory);
            __Modding = new Modding(XivCache.GameInfo.GameDirectory);
            _ReadOnly = readOnly;
        }

        /// <summary>
        /// Opens a new mod transaction.
        /// Transactions will still write data to DATs in real time, but will cache index and modlist changes until they are committed.
        /// </summary>
        /// <param name="modpack"></param>
        /// <returns></returns>
        public static ModTransaction BeginTransaction(ModPack modpack = null, bool readOnly = false)
        {
            if(OpenTransaction)
            {
                throw new Exception("Cannot have two open mod transactions simultaneously.");
            }

            // Disable the cache worker during transactions.
            _WorkerStatus = XivCache.CacheWorkerEnabled;
            XivCache.CacheWorkerEnabled = false;            

            var tx = new ModTransaction(modpack, readOnly);
            _OpenTransaction = tx;
            return tx;
        }

        public static async Task CommitTransaction(ModTransaction tx)
        {
            if(tx != _OpenTransaction)
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
                XivCache.CacheWorkerEnabled = XivCache.CacheWorkerEnabled;
            }
        }
        public static void CancelTransaction(ModTransaction tx)
        {
            try
            {
                tx.CancelTransaction();
            }
            finally
            {
                _OpenTransaction = null;
                XivCache.CacheWorkerEnabled = XivCache.CacheWorkerEnabled;
            }
        }

        /// <summary>
        /// Commits this transaction to the game files.
        /// This causes Index and Modlist writes to disk for all affected mods.
        /// </summary>
        /// <returns></returns>
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

            foreach(var index in _IndexFiles)
            {
                await __Index.SaveIndexFile(index.Value);
            }
            await __Modding.SaveModListAsync(_ModList);
        }

        private void CancelTransaction()
        {
            _IndexFiles = null;
            _ModList = null;
            _ModPack = null;
        }
    }
}

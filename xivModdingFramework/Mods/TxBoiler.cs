using SharpDX.Direct2D1.Effects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Mods.DataContainers;

namespace xivModdingFramework.Mods
{
    /// <summary>
    /// Class for helping manage boilerplate related to transactions
    /// </summary>
    public class TxBoiler
    {
        private bool ownTx;
        private bool ownBatch;
        private ModTransaction tx;
        private ModPack? originalModpack;
        private ModPack? ownModpack;

        public ModTransaction Transaction {
            get => tx;
        }
        public bool OwnTx
        {
            get => ownTx;
        }

        public bool OwnBatch
        {
            get => ownBatch;
        }

        public ModPack? OriginalModpack
        {
            get => originalModpack;
        }

        public ModPack? OwnModpack
        {
            get => ownModpack;
            set
            {
                ownModpack = value;
                tx.ModPack = ownModpack;
            }
        }

        private Dictionary<string, TxFileState> OriginalStates = new Dictionary<string, TxFileState>();
        private TxBoiler() { 
        }

        public async Task Commit()
        {
            if (ownTx)
            {
                // It is not necessary to end any open batch here, as commit will already end it.
                await ModTransaction.CommitTransaction(tx);
            } else if (ownBatch && tx.IsBatchingNotifications)
            {
                tx.INTERNAL_EndBatchingNotifications();
            }

            if (!OwnTx)
            {
                tx.ModPack = originalModpack;
            }
        }

        public async Task Catch()
        {
            await Cancel(false);
        }

        public async Task Cancel(bool graceful = false)
        {
            if (ownTx)
            {
                await ModTransaction.CancelTransaction(tx, graceful);
            }
            else
            {
                tx.ModPack = originalModpack;
            }

            if (!ownTx && tx.State == ETransactionState.Open || tx.State == ETransactionState.Preparing)
            {
                foreach (var state in OriginalStates)
                {
                    await tx.RestoreFileState(state.Value);
                }
            }

            if (ownBatch && tx.IsBatchingNotifications)
            {
                tx.INTERNAL_EndBatchingNotifications();
            }
            UnbindEvents();
        }

        private void BindEvents()
        {
            tx.INTERNAL_IndexChanging += Tx_INTERNAL_IndexChanging;
            tx.INTERNAL_ModChanging += Tx_INTERNAL_ModChanging;
        }
        private void UnbindEvents()
        {
            if (tx == null) return;
            tx.INTERNAL_IndexChanging -= Tx_INTERNAL_IndexChanging;
            tx.INTERNAL_ModChanging -= Tx_INTERNAL_ModChanging;
        }

        private void Tx_INTERNAL_ModChanging(string internalFilePath, Mod? previousMod)
        {
            var exists = OriginalStates.ContainsKey(internalFilePath);
            if (exists && OriginalStates[internalFilePath].OriginalMod_Set)
            {
                return;
            }

            if (!exists)
            {
                OriginalStates.Add(internalFilePath, new TxFileState(internalFilePath));  
            }

            OriginalStates[internalFilePath].OriginalMod = previousMod;
        }

        private void Tx_INTERNAL_IndexChanging(string internalFilePath, long previousOffset)
        {
            var exists = OriginalStates.ContainsKey(internalFilePath);
            if (exists && OriginalStates[internalFilePath].OriginalOffset_Set)
            {
                return;
            }

            if (!exists)
            {
                OriginalStates.Add(internalFilePath, new TxFileState(internalFilePath));
            }
            OriginalStates[internalFilePath].OriginalOffset = previousOffset;
        }


        public static async Task<TxBoiler> BeginWrite(ModTransaction tx, bool doBatch = true, ModPack? modpack = null, bool throwawayTx = false)
        {
            return await Task.Run(async () =>
            {
                var ownTx = false;
                ModPack? originalModpack = null;
                ModPack? ownModpack = null;
                if (tx == null)
                {
                    ownTx = true;
                    ModTransactionSettings? settings = null;
                    if (throwawayTx)
                    {
                        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                        Directory.CreateDirectory(tempPath);
                        settings = new ModTransactionSettings()
                        {
                            StorageType = SqPack.FileTypes.EFileStorageType.UncompressedIndividual,
                            Target = ETransactionTarget.FolderTree,
                            TargetPath = tempPath,
                            Unsafe = false,
                        };
                    }
                    tx = await ModTransaction.BeginTransaction(true, modpack, settings);
                }
                else
                {
                    originalModpack = tx.ModPack;
                }

                if (modpack != null && tx.ModPack == null)
                {
                    tx.ModPack = modpack;
                }

                var ownBatch = false;
                if (doBatch && !tx.IsBatchingNotifications)
                {
                    ownBatch = true;
                    tx.INTERNAL_BeginBatchingNotifications();
                }
                var boiler = new TxBoiler()
                {
                    tx = tx,
                    ownBatch = ownBatch,
                    ownTx = ownTx,
                    ownModpack = ownModpack,
                    originalModpack = originalModpack,
                };
                boiler.BindEvents();

                return boiler;
            });
        }
    }
}

using System;
using System.Collections.Generic;
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

        public ModTransaction Transaction
        {
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

        private TxBoiler() { 
        }

        public async Task Commit()
        {
            if (ownTx)
            {
                // It is not necessary to end any open batch here, as commit will already end it.
                await ModTransaction.CommitTransaction(tx);
            } else if (ownBatch)
            {
                tx.INTERNAL_EndBatchingNotifications();
            }

            if (!OwnTx)
            {
                tx.ModPack = originalModpack;
            }
        }

        public async Task Catch(TxFileState restoreState)
        {
            await Cancel(false, restoreState);
        }
        public async Task Catch(Dictionary<string, TxFileState> restoreStates)
        {
            await Cancel(false, restoreStates);
        }
        public async Task Catch(IEnumerable<TxFileState> restoreStates)
        {
            await Cancel(false, restoreStates);
        }
        public void Catch()
        {
            Cancel(false);
        }

        public async Task Cancel(bool graceful, TxFileState restoreState)
        {
            await Cancel(graceful, new List<TxFileState>() { restoreState });
        }
        public async Task Cancel(bool graceful, Dictionary<string, TxFileState> restoreStates)
        {
            await Cancel(graceful, restoreStates.Values);
        }
        public async Task Cancel(bool graceful, IEnumerable<TxFileState> restoreStates)
        {
            Cancel(graceful);
            if (!ownTx && tx.State == ETransactionState.Open || tx.State == ETransactionState.Preparing)
            {
                foreach(var state in restoreStates)
                {
                    await tx.RestoreFileState(state);
                }
            }
        }

        public void Cancel(bool graceful = false)
        {
            if (ownTx)
            {
                ModTransaction.CancelTransaction(tx, graceful);
            }
            else
            {
                tx.ModPack = originalModpack;
            }

            if(ownBatch && tx.IsBatchingNotifications)
            {
                tx.INTERNAL_EndBatchingNotifications();
            }
        }

        public void Finally()
        {
            if (tx != null)
            {
                tx.ModPack = originalModpack;

                if (ownTx)
                {
                    if (tx.State != ETransactionState.Closed)
                    {
                        Cancel(true);
                    }
                }

                if (ownBatch && tx.IsBatchingNotifications)
                {
                    tx.INTERNAL_EndBatchingNotifications();
                }
            }
        }

        public static TxBoiler BeginWrite(ref ModTransaction tx, bool doBatch = true, ModPack? modpack = null)
        {
            var ownTx = false;
            ModPack? originalModpack = null;
            ModPack? ownModpack = null;
            if (tx == null)
            {
                ownTx = true;
                tx = ModTransaction.BeginTransaction(true, modpack);
            }
            else
            {
                originalModpack = tx.ModPack;
            }

            if(modpack != null && tx.ModPack == null)
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
            return boiler;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Mods;

namespace xivModdingFramework.SqPack.DataContainers
{
    /// <summary>
    /// Wrapped Index File class that notifies the active global transaction whenever the index is updated.
    /// </summary>
    internal class TransactionIndexFile : IndexFile
    {
        public TransactionIndexFile(XivDataFile dataFile, BinaryReader index1Stream, BinaryReader index2Stream, bool readOnly = true) : base(dataFile, index1Stream, index2Stream, readOnly)
        {
        }

        protected override uint INTERNAL_SetDataOffset(string filePath, uint newRawOffsetWithDatNumEmbed, bool allowRepair = false)
        {
            // Loop back to notify the Transaction of our changes.
            var tx = ModTransaction.ActiveTransaction;
            if(tx != null && tx.State == ETransactionState.Closed)
            {
                throw new AccessViolationException("Cannot read data from a closed transaction.");
            }


            var originalOffset = Get8xDataOffset(filePath);
            if(tx != null && originalOffset != newRawOffsetWithDatNumEmbed)
            {
                tx.INTERNAL_OnIndexUpdate(DataFile, filePath, originalOffset, newRawOffsetWithDatNumEmbed * 8L);
            }

            var ret = base.INTERNAL_SetDataOffset(filePath, newRawOffsetWithDatNumEmbed, allowRepair);
            if (tx != null && originalOffset != newRawOffsetWithDatNumEmbed)
            {
                tx.INTERNAL_OnFileChanged(filePath, newRawOffsetWithDatNumEmbed * 8L, true);
            }
            return ret;
        }
    }
}

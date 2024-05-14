using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace xivModdingFramework.Mods.DataContainers
{
    /// <summary>
    /// Wrapped ModList handler that reports to the active transaction when changes are made.
    /// </summary>
    internal class TransactionModList : ModList
    {
        protected override void INTERNAL_AddOrUpdateMod(Mod mod)
        {
            // Loop back to notify the Transaction of our changes.
            var tx = ModTransaction.ActiveTransaction;
            if (tx != null)
            {
                var originalMod = GetMod(mod.FilePath);
                tx.INTERNAL_OnModUpdate(mod.FilePath, originalMod, mod);
            }
            base.INTERNAL_AddOrUpdateMod(mod);
        }

        protected override void INTERNAL_RemoveMod(string path)
        {
            // Loop back to notify the Transaction of our changes.
            var tx = ModTransaction.ActiveTransaction;
            if (tx != null)
            {
                var originalMod = GetMod(path);
                tx.INTERNAL_OnModUpdate(path, originalMod, null);
            }
            base.INTERNAL_RemoveMod(path);
        }
    }
}

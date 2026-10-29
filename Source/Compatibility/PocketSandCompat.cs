using AutoArm.Helpers;
using System;
using System.Linq;
using Verse;

namespace AutoArm.Compatibility
{
    internal static class PocketSandCompat
    {
        private static bool? _isLoaded;

        public static bool IsLoaded
        {
            get
            {
                if (_isLoaded.HasValue) return _isLoaded.Value;

                try
                {
                    _isLoaded = ModLister.AllInstalledMods.Any(m =>
                        m.Active &&
                        (m.Name?.IndexOf("Pocket Sand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         m.PackageIdPlayerFacing?.IndexOf("pocketsand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         m.PackageIdPlayerFacing?.IndexOf("reisen.pocketsand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         m.PackageIdPlayerFacing?.IndexOf("usagirei.pocketsand", StringComparison.OrdinalIgnoreCase) >= 0));
                }
                catch (Exception ex)
                {
                    AutoArmLogger.Debug(() => $"PocketSand detection failed: {ex.GetType().Name}: {ex.Message}");
                    _isLoaded = false;
                }

                return _isLoaded.Value;
            }
        }

        private static JobDef _equipDef;
        private static bool _equipDefResolved;

        private static JobDef EquipDef
        {
            get
            {
                if (!_equipDefResolved)
                {
                    _equipDef = IsLoaded ? DefDatabase<JobDef>.GetNamedSilentFail("PS_Equip") : null;
                    _equipDefResolved = true;
                }
                return _equipDef;
            }
        }

        public static bool EquipJob(JobDef def) => def != null && def == EquipDef;

        public static bool WantsUnarmed(Pawn pawn)
        {
            if (EquipDef == null || pawn.equipment?.Primary != null || pawn.inventory == null)
                return false;
            var items = pawn.inventory.innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                if (Validation.IsWeapon(items[i]))
                    return true;
            }
            return false;
        }
    }
}

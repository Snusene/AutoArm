using System;
using System.Linq;
using Verse;

namespace AutoArm.Compatibility
{
    /// <summary>
    /// Pocket Sand detection
    /// Uses vanilla equip
    /// PocketSand compat
    /// </summary>
    public static class PocketSandCompat
    {
        private static bool? _isLoaded;

        /// <summary>
        /// Pocket Sand active
        /// </summary>
        public static bool Active
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
                catch
                {
                    _isLoaded = false;
                }

                return _isLoaded.Value;
            }
        }

        /// <summary>
        /// No-op stub for compatibility with existing call sites
        /// Previous pending equip system was removed in favor of vanilla job flow
        /// </summary>
        public static void ClearPending(Pawn pawn)
        {
        }
    }
}

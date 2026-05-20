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
    }
}

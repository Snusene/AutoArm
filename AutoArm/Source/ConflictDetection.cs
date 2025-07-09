using System.Linq;
using Verse;

namespace AutoArm
{
    [StaticConstructorOnStartup]
    public static class ConflictDetection
    {
        static ConflictDetection()
        {
            CheckForKnownConflicts();
        }

        private static void CheckForKnownConflicts()
        {
            // Check for mods that might conflict
            var potentialConflicts = new[]
            {
                new { PackageId = "Uuugggg.SmartSpeed", Name = "Smart Speed", Issue = "May cause performance issues with TickRare patches" },
                new { PackageId = "Mehni.PickUpAndHaul", Name = "Pick Up And Haul", Issue = "May conflict with weapon pickup behavior" },
                new { PackageId = "Roolo.RunAndGun", Name = "Run and Gun", Issue = "May interfere with weapon switching while drafted" },
                new { PackageId = "Fluffy.WorkTab", Name = "Work Tab", Issue = "Minor: May affect weapon assignment priorities" }
            };

            bool foundConflicts = false;

            foreach (var conflict in potentialConflicts)
            {
                if (ModLister.GetActiveModWithIdentifier(conflict.PackageId) != null ||
                    ModLister.GetActiveModWithIdentifier(conflict.PackageId.ToLower()) != null)
                {
                    if (!foundConflicts)
                    {
                        Log.Warning("[AutoArm] Detected potentially conflicting mods:");
                        foundConflicts = true;
                    }
                    Log.Warning($"  - {conflict.Name}: {conflict.Issue}");
                }
            }

            if (foundConflicts)
            {
                Log.Warning("[AutoArm] These mods should still work together, but please report any issues!");
            }

            // Check load order
            CheckLoadOrder();
        }

        private static void CheckLoadOrder()
        {
            var autoArmIndex = ModLister.AllInstalledMods
                .Where(m => m.Active)
                .ToList()
                .FindIndex(m => m.PackageIdPlayerFacing.ToLower().Contains("autoarm"));

            var importantMods = new[]
            {
                new { PackageId = "brrainz.harmony", Name = "Harmony", ShouldBeAfter = false },
                new { PackageId = "ludeon.rimworld", Name = "Core", ShouldBeAfter = false },
                new { PackageId = "ludeon.rimworld.royalty", Name = "Royalty", ShouldBeAfter = false },
                new { PackageId = "ludeon.rimworld.ideology", Name = "Ideology", ShouldBeAfter = false },
                new { PackageId = "ludeon.rimworld.biotech", Name = "Biotech", ShouldBeAfter = false },
                new { PackageId = "ceteam.combatextended", Name = "Combat Extended", ShouldBeAfter = false },
                new { PackageId = "petetimessix.simplesidearms", Name = "Simple Sidearms", ShouldBeAfter = false }
            };

            foreach (var mod in importantMods)
            {
                var modIndex = ModLister.AllInstalledMods
                    .Where(m => m.Active)
                    .ToList()
                    .FindIndex(m => m.PackageIdPlayerFacing.ToLower() == mod.PackageId.ToLower());

                if (modIndex >= 0 && autoArmIndex >= 0)
                {
                    bool isAfter = autoArmIndex > modIndex;
                    if (isAfter != mod.ShouldBeAfter)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] Load order note: AutoArm is {(isAfter ? "after" : "before")} {mod.Name}");
                        }
                    }
                }
            }
        }

        // Check if we should disable certain features due to conflicts
        public static bool ShouldDisableFeature(string feature)
        {
            switch (feature)
            {
                case "TickRarePatches":
                    // If performance mods are detected, maybe use less frequent checks
                    return ModLister.GetActiveModWithIdentifier("Uuugggg.SmartSpeed") != null;

                case "JobInterruption":
                    // Be more careful with job interruption if certain mods are present
                    return ModLister.GetActiveModWithIdentifier("Fluffy.WorkTab") != null;

                default:
                    return false;
            }
        }
    }
}
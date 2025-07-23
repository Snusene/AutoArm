using System;
using System.Linq;
using Verse;
using RimWorld;

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
            var potentialConflicts = new[]
            {
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

            CheckLoadOrder();
        }

        private static void CheckLoadOrder()
        {
            // Get mods in their actual load order
            var activeModsInOrder = ModsConfig.ActiveModsInLoadOrder.ToList();
            
            // Find AutoArm in the load order
            var autoArmIndex = activeModsInOrder.FindIndex(m => 
                m.PackageIdPlayerFacing.ToLower().Contains("autoarm") || 
                m.PackageId.ToLower().Contains("autoarm"));

            AutoArmDebug.Log("Active mods in load order:");
            for (int i = 0; i < Math.Min(20, activeModsInOrder.Count); i++)
            {
                var mod = activeModsInOrder[i];
                AutoArmDebug.Log($"  {i}: {mod.PackageIdPlayerFacing} - {mod.Name}");
            }
            AutoArmDebug.Log($"Found AutoArm at index: {autoArmIndex}");

            var importantMods = new[]
            {
                new { PackageId = "brrainz.harmony", Name = "Harmony", ShouldBeAfter = true },
                new { PackageId = "ludeon.rimworld", Name = "Core", ShouldBeAfter = true },
                new { PackageId = "ludeon.rimworld.royalty", Name = "Royalty", ShouldBeAfter = true },
                new { PackageId = "ludeon.rimworld.ideology", Name = "Ideology", ShouldBeAfter = true },
                new { PackageId = "ludeon.rimworld.biotech", Name = "Biotech", ShouldBeAfter = true },
                new { PackageId = "ludeon.rimworld.anomaly", Name = "Anomaly", ShouldBeAfter = true },
                new { PackageId = "ludeon.rimworld.odyssey", Name = "Odyssey", ShouldBeAfter = true },
                new { PackageId = "ceteam.combatextended", Name = "Combat Extended", ShouldBeAfter = true },
                new { PackageId = "petetimessix.simplesidearms", Name = "Simple Sidearms", ShouldBeAfter = true }
            };

            foreach (var mod in importantMods)
            {
                var modIndex = activeModsInOrder.FindIndex(m => 
                    m.PackageIdPlayerFacing.ToLower() == mod.PackageId.ToLower() ||
                    m.PackageId.ToLower() == mod.PackageId.ToLower());

                if (modIndex >= 0 && autoArmIndex >= 0)
                {
                    bool isAfter = autoArmIndex > modIndex;
                    if (isAfter != mod.ShouldBeAfter)
                    {
                        Log.Warning($"[AutoArm] Load order issue: AutoArm should be {(mod.ShouldBeAfter ? "after" : "before")} {mod.Name} but is currently {(isAfter ? "after" : "before")} it");
                        AutoArmDebug.Log($"WARNING: AutoArm index: {autoArmIndex}, {mod.Name} index: {modIndex}");
                    }
                }
            }
        }

        public static bool ShouldDisableFeature(string feature)
        {
            switch (feature)
            {
                case "JobInterruption":
                    return ModLister.GetActiveModWithIdentifier("Fluffy.WorkTab") != null;

                default:
                    return false;
            }
        }
    }
}

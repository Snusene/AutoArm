// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Weapon categorization diagnostic tool
// Identifies mods that might break weapon detection

using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Diagnostic tool to identify mods that might be breaking weapon categorization
    /// </summary>
    public static class WeaponCategorizationDiagnostic
    {
        public static void RunDiagnostic(Map map)
        {
            Log.Message("\n[AutoArm] === WEAPON CATEGORIZATION DIAGNOSTIC ===");

            // Check weapon defs
            var allWeaponDefs = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.IsWeapon && !d.IsApparel)
                .ToList();

            Log.Message($"[AutoArm] Total weapon defs in game: {allWeaponDefs.Count}");

            // Group by mod
            var weaponsByMod = allWeaponDefs
                .GroupBy(d => d.modContentPack?.Name ?? "Core")
                .OrderByDescending(g => g.Count())
                .ToList();

            Log.Message("[AutoArm] Weapons by mod:");
            foreach (var modGroup in weaponsByMod.Take(10))
            {
                Log.Message($"  - {modGroup.Key}: {modGroup.Count()} weapons");
            }

            // Check for suspicious patterns
            var suspiciousWeapons = allWeaponDefs
                .Where(d => d.thingCategories == null ||
                           !d.thingCategories.Any() ||
                           d.equipmentType == EquipmentType.None ||
                           !d.HasComp(typeof(CompEquippable)))
                .ToList();

            if (suspiciousWeapons.Any())
            {
                Log.Warning($"[AutoArm] Found {suspiciousWeapons.Count} weapons with missing/incorrect configuration:");
                foreach (var weapon in suspiciousWeapons.Take(5))
                {
                    Log.Warning($"  - {weapon.defName} from {weapon.modContentPack?.Name ?? "Unknown"}:");
                    Log.Warning($"    Categories: {weapon.thingCategories?.Count ?? 0}");
                    Log.Warning($"    EquipmentType: {weapon.equipmentType}");
                    Log.Warning($"    HasCompEquippable: {weapon.HasComp(typeof(CompEquippable))}");
                }
            }

            // Check actual weapons on map
            if (map != null)
            {
                var weaponGroup = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToList();
                var allWeapons = map.listerThings.AllThings.Where(t => t.def.IsWeapon).ToList();

                Log.Message($"\n[AutoArm] Map weapon detection:");
                Log.Message($"  - ThingRequestGroup.Weapon: {weaponGroup.Count}");
                Log.Message($"  - All IsWeapon items: {allWeapons.Count}");

                if (weaponGroup.Count < allWeapons.Count / 2)
                {
                    Log.Error($"[AutoArm] CRITICAL: ThingRequestGroup.Weapon is severely broken!");
                    Log.Error($"[AutoArm] Only {weaponGroup.Count} of {allWeapons.Count} weapons are properly categorized.");

                    // Find which weapons are missing
                    var missingWeapons = allWeapons.Except(weaponGroup).ToList();
                    Log.Error($"[AutoArm] Examples of uncategorized weapons:");
                    foreach (var weapon in missingWeapons.Take(5))
                    {
                        Log.Error($"  - {weapon.Label} ({weapon.def.defName}) from {weapon.def.modContentPack?.Name ?? "Unknown"}");
                    }
                }
            }

            // Check for known problematic mods
            CheckForKnownIssues();

            Log.Message("[AutoArm] === END DIAGNOSTIC ===\n");
        }

        private static void CheckForKnownIssues()
        {
            var loadedMods = LoadedModManager.RunningMods.Select(m => m.PackageId.ToLower()).ToList();

            // Known mods that can cause issues
            var problematicMods = new Dictionary<string, string>
            {
                { "mehni.pickupandhaul", "Pick Up And Haul - Can interfere with item categorization" },
                { "fluffy.colonymanager", "Colony Manager - May change how items are tracked" },
                { "uuugggg.replacestuff", "Replace Stuff - Can affect item spawning" },
                { "lwm.deepstorage", "LWM's Deep Storage - Changes storage mechanics" }
            };

            var foundIssues = false;
            foreach (var mod in problematicMods)
            {
                if (loadedMods.Contains(mod.Key))
                {
                    if (!foundIssues)
                    {
                        Log.Warning("\n[AutoArm] Potentially conflicting mods detected:");
                        foundIssues = true;
                    }
                    Log.Warning($"  - {mod.Value}");
                }
            }

            if (foundIssues)
            {
                Log.Warning("[AutoArm] These mods may interfere with weapon categorization.");
                Log.Warning("[AutoArm] Try adjusting your mod load order or check for updates.");
            }
        }
    }
}
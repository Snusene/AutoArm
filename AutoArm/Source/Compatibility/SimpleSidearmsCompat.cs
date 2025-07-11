using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoArm
{
    public static class SimpleSidearmsCompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        // Types
        private static Type compSidearmMemoryType;
        private static Type thingDefStuffDefPairType;
        private static Type statCalculatorType;
        private static MethodInfo canPickupSidearmInstanceMethod;
        // Properties and Fields
        private static PropertyInfo rememberedWeaponsProperty;
        private static FieldInfo rememberedWeaponsField;
        private static FieldInfo thingField;
        private static FieldInfo stuffField;

        // JobDef
        private static JobDef equipSecondaryJobDef;

        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && m.PackageIdPlayerFacing == "PeteTimesSix.SimpleSidearms");

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Simple Sidearms detection result: {_isLoaded}");
                }
            }
            return _isLoaded.Value;
        }

        private static void EnsureInitialized()
        {
            if (_initialized || !IsLoaded())
                return;

            try
            {
                // Find CompSidearmMemory type
                compSidearmMemoryType = GenTypes.AllTypes
                    .FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");

                if (compSidearmMemoryType == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Warning("[AutoArm] Could not find CompSidearmMemory type");
                    }
                    return;
                }

                // Find ThingDefStuffDefPair type
                thingDefStuffDefPairType = GenTypes.AllTypes
                    .FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.ThingDefStuffDefPair");

                if (thingDefStuffDefPairType != null)
                {
                    thingField = thingDefStuffDefPairType.GetField("thing");
                    stuffField = thingDefStuffDefPairType.GetField("stuff");
                }

                // Get the RememberedWeapons property or field
                rememberedWeaponsProperty = compSidearmMemoryType.GetProperty("RememberedWeapons", BindingFlags.Public | BindingFlags.Instance);
                if (rememberedWeaponsProperty == null)
                {
                    rememberedWeaponsField = compSidearmMemoryType.GetField("rememberedWeapons", BindingFlags.Public | BindingFlags.Instance);
                }

                // Find the EquipSecondary JobDef - try more variations
                string[] possibleJobNames = {
            "EquipSecondary",
            "SimpleSidearms_EquipSecondary",
            "Sidearms_EquipSecondary",
            "EquipSecondarySidearm",
            "PickupSidearm"
        };

                foreach (var name in possibleJobNames)
                {
                    equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail(name);
                    if (equipSecondaryJobDef != null)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] Found sidearm job def: {equipSecondaryJobDef.defName}");
                        }
                        break;
                    }
                }

                // If not found, search by pattern
                if (equipSecondaryJobDef == null)
                {
                    var allJobDefs = DefDatabase<JobDef>.AllDefs.ToList();
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Searching through {allJobDefs.Count} job defs for sidearm job...");
                    }

                    equipSecondaryJobDef = allJobDefs
                        .FirstOrDefault(j => j.defName.Contains("EquipSecondary") ||
                                           (j.defName.Contains("Sidearm") && j.defName.Contains("Equip")) ||
                                           (j.defName.Contains("Sidearm") && j.defName.Contains("Pickup")));

                    if (equipSecondaryJobDef != null && AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Found sidearm job def by search: {equipSecondaryJobDef.defName}");
                    }
                }

                // NEW: Cache StatCalculator type and method
                statCalculatorType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "StatCalculator" &&
                    t.Namespace?.Contains("SimpleSidearms") == true);

                if (statCalculatorType != null)
                {
                    canPickupSidearmInstanceMethod = statCalculatorType.GetMethod("CanPickupSidearmInstance",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() },
                        null);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Cached StatCalculator type and CanPickupSidearmInstance method");
                    }
                }

                _initialized = true;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Simple Sidearms compatibility initialized: " +
                              $"MemoryComp={compSidearmMemoryType != null}, " +
                              $"JobDef={equipSecondaryJobDef?.defName ?? "null"}, " +
                              $"StatCalc={statCalculatorType != null}, " +
                              $"CanPickupMethod={canPickupSidearmInstanceMethod != null}");

                    if (equipSecondaryJobDef == null)
                    {
                        Log.Warning("[AutoArm] Could not find Simple Sidearms equip job def! Sidearm auto-equip will not work.");

                        // List all job defs that might be related
                        var possibleJobs = DefDatabase<JobDef>.AllDefs
                            .Where(j => j.defName.ToLower().Contains("sidearm") ||
                                      j.defName.ToLower().Contains("secondary"))
                            .Take(10)
                            .ToList();

                        if (possibleJobs.Any())
                        {
                            Log.Message("[AutoArm] Possible sidearm job defs found:");
                            foreach (var job in possibleJobs)
                            {
                                Log.Message($"  - {job.defName}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to initialize Simple Sidearms compatibility: {e.Message}");
            }
        }

        private static IEnumerable GetRememberedWeapons(ThingComp comp)
        {
            if (comp == null) return null;

            if (rememberedWeaponsProperty != null)
                return rememberedWeaponsProperty.GetValue(comp) as IEnumerable;

            if (rememberedWeaponsField != null)
                return rememberedWeaponsField.GetValue(comp) as IEnumerable;

            return null;
        }

        private static ThingComp GetSidearmComp(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null) return null;

            EnsureInitialized();

            if (compSidearmMemoryType == null) return null;

            return pawn.AllComps?.FirstOrDefault(c => c.GetType() == compSidearmMemoryType);
        }

        public static bool CanPickupWeaponAsSidearm(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";

            if (!IsLoaded() || weapon == null || pawn == null)
                return true;

            EnsureInitialized();

            try
            {
                // Use cached values instead of searching every time
                if (statCalculatorType == null || canPickupSidearmInstanceMethod == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] StatCalculator not found, allowing weapon");
                    }
                    return true;
                }

                // Call the method using cached MethodInfo
                object[] parameters = new object[] { weapon, pawn, null };
                bool result = (bool)canPickupSidearmInstanceMethod.Invoke(null, parameters);
                reason = (string)parameters[2] ?? "";

                if (AutoArmMod.settings?.debugLogging == true && !result)
                {
                    Log.Message($"[AutoArm] Simple Sidearms rejected {weapon.Label} for {pawn.Name}: {reason}");
                }

                return result;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error checking sidearm compatibility: {e.Message}");
                }
                return true;
            }
        }

        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Sidearm check skipped - not loaded or invalid pawn");
                }
                return null;
            }

            // Check if sidearm auto-equip is enabled
            if (AutoArmMod.settings?.autoEquipSidearms != true)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Sidearm check skipped - auto-equip disabled");
                }
                return null;
            }

            EnsureInitialized();

            if (!_initialized || equipSecondaryJobDef == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Sidearm check failed - not initialized or no job def");
                }
                return null;
            }

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} has no sidearm memory component");
                    }
                    return null;
                }

                // Get current sidearms
                var currentSidearmDefs = new HashSet<ThingDef>();
                var sidearmsList = GetRememberedWeapons(comp);

                if (sidearmsList != null && thingField != null)
                {
                    foreach (var sidearmInfo in sidearmsList)
                    {
                        if (sidearmInfo != null)
                        {
                            var weaponDef = thingField.GetValue(sidearmInfo) as ThingDef;
                            if (weaponDef != null)
                            {
                                currentSidearmDefs.Add(weaponDef);
                            }
                        }
                    }
                }

                // Also count weapons in inventory
                int inventoryWeaponCount = pawn.inventory?.innerContainer?.Count(t => t.def.IsWeapon) ?? 0;

                int maxSidearms = GetMaxSidearmsForPawn(pawn);
                int currentSidearmCount = Math.Max(currentSidearmDefs.Count, inventoryWeaponCount);
                int availableSlots = maxSidearms - currentSidearmCount;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} sidearm check: {currentSidearmCount}/{maxSidearms} slots used");
                }

                // Check Simple Sidearms limits
                if (availableSlots <= 0)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} has no available sidearm slots");
                    }
                    // Try to upgrade existing sidearms
                    return TryUpgradeExistingSidearm(pawn, currentSidearmDefs);
                }

                // Look for good weapons to add as sidearms
                var job = TryFindNewSidearm(pawn, currentSidearmDefs);

                if (job == null && AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} - no suitable sidearm found");
                }

                return job;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error finding sidearm upgrade: {e.Message}");
                    Log.Warning($"[AutoArm] Stack trace: {e.StackTrace}");
                }
                return null;
            }
        }

        private static Job TryUpgradeExistingSidearm(Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            var worstSidearm = pawn.inventory.innerContainer
                .OfType<ThingWithComps>()
                .Where(t => t.def.IsWeapon && currentSidearmDefs.Contains(t.def) &&
                           !ForcedWeaponTracker.IsForcedSidearm(pawn, t.def))
                .OrderBy(w => GetBasicWeaponScore(w))
                .FirstOrDefault();

            if (worstSidearm == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} - no upgradeable sidearms");
                }
                return null;
            }

            float worstScore = GetBasicWeaponScore(worstSidearm);

            var betterWeapon = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => IsValidSidearmCandidate(w, pawn) &&
                           !currentSidearmDefs.Contains(w.def) &&
                           GetBasicWeaponScore(w) > worstScore * 1.15f)
                .OrderByDescending(w => GetBasicWeaponScore(w))
                .FirstOrDefault();

            if (betterWeapon != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} upgrading sidearm {worstSidearm.Label} to {betterWeapon.Label}");
                }

                // Drop the worst sidearm
                Thing droppedThing;
                pawn.inventory.innerContainer.TryDrop(worstSidearm, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedThing);

                return JobMaker.MakeJob(equipSecondaryJobDef, betterWeapon);
            }

            return null;
        }

        private static Job TryFindNewSidearm(Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            if (pawn?.Map == null)
                return null;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name} looking for new sidearm...");
            }

            // Get all weapons within reasonable distance
            var candidateWeapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => w != null && w.def != null &&
                           w.Position.DistanceTo(pawn.Position) <= 50f &&
                           IsValidSidearmCandidate(w, pawn) &&
                           !currentSidearmDefs.Contains(w.def))
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .Take(30)
                .ToList();

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name} found {candidateWeapons.Count} candidate weapons");
            }

            // Score and sort candidates
            var scoredWeapons = candidateWeapons
                .Select(w => new { Weapon = w, Score = GetSidearmScore(w, pawn) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score / (1f + x.Weapon.Position.DistanceTo(pawn.Position) / 100f))
                .ToList();

            if (AutoArmMod.settings?.debugLogging == true && scoredWeapons.Any())
            {
                Log.Message($"[AutoArm] {pawn.Name} top sidearm candidates:");
                foreach (var sw in scoredWeapons.Take(3))
                {
                    Log.Message($"  - {sw.Weapon.Label}: score {sw.Score:F0} at distance {sw.Weapon.Position.DistanceTo(pawn.Position):F0}");
                }
            }

            var bestWeapon = scoredWeapons.FirstOrDefault()?.Weapon;

            if (bestWeapon != null && equipSecondaryJobDef != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} will add {bestWeapon.Label} as sidearm");
                }
                return JobMaker.MakeJob(equipSecondaryJobDef, bestWeapon);
            }

            return null;
        }

        private static float GetSidearmScore(ThingWithComps weapon, Pawn pawn)
        {
            float score = GetBasicWeaponScore(weapon);

            // Diversification bonus - prefer different weapon types
            bool hasRangedSidearm = pawn.inventory?.innerContainer?.Any(t => t.def.IsRangedWeapon) ?? false;
            bool hasMeleeSidearm = pawn.inventory?.innerContainer?.Any(t => t.def.IsMeleeWeapon) ?? false;

            if (weapon.def.IsRangedWeapon && !hasRangedSidearm)
                score *= 1.5f;
            else if (weapon.def.IsMeleeWeapon && !hasMeleeSidearm)
                score *= 1.5f;

            // Skill-based adjustments
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;

            if (weapon.def.IsRangedWeapon && shootingSkill > 5)
                score *= 1f + (shootingSkill / 20f);
            else if (weapon.def.IsMeleeWeapon && meleeSkill > 5)
                score *= 1f + (meleeSkill / 20f);

            return score;
        }

        private static float GetBasicWeaponScore(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            float score = 100f;

            // Quality
            if (weapon.TryGetQuality(out QualityCategory qc))
            {
                score += (int)qc * 20f;
            }

            // Condition
            if (weapon.MaxHitPoints > 0)
            {
                float hpPercent = weapon.HitPoints / (float)weapon.MaxHitPoints;
                score += hpPercent * 50f;

                if (hpPercent < 0.3f)
                    return 0f;
            }

            // Tech level
            score += (int)weapon.def.techLevel * 10f;

            // Penalize basic weapons
            if (weapon.def.defName == "WoodLog" || weapon.def.defName == "MeleeWeapon_Club")
                score *= 0.5f;

            // Damage potential
            if (weapon.def.IsRangedWeapon)
            {
                float dps = JobGiverHelpers.GetRangedWeaponDPS(weapon.def, weapon);
                score += dps * 5f;
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                float meleeDPS = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_CooldownMultiplier);
                float meleeDamage = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_DamageMultiplier);
                score += (meleeDPS + meleeDamage) * 10f;
            }

            return score;
        }

        private static bool IsValidSidearmCandidate(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || weapon.def == null || weapon.Destroyed || weapon.IsForbidden(pawn))
            {
                return false;
            }

            // Don't take weapons from inventory or equipment
            if (weapon.ParentHolder is Pawn_InventoryTracker || weapon.ParentHolder is Pawn_EquipmentTracker)
            {
                return false;
            }

            // Check outfit filter
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def))
            {
                return false;
            }

            // Check Simple Sidearms restrictions
            if (!CanPickupWeaponAsSidearm(weapon, pawn, out string reason))
            {
                return false;
            }

            // Check if reachable
            if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                return false;
            }

            return true;
        }

        public static int GetMaxSidearmsForPawn(Pawn pawn)
        {
            // Try to get actual Simple Sidearms settings
            try
            {
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "SimpleSidearms_Settings" ||
                    (t.Name == "Settings" && t.Namespace?.Contains("SimpleSidearms") == true));

                if (settingsType != null)
                {
                    var modType = GenTypes.AllTypes.FirstOrDefault(t =>
                        t.Name == "SimpleSidearmsMod" ||
                        (t.Name.Contains("SimpleSidearms") && t.IsSubclassOf(typeof(Mod))));

                    if (modType != null)
                    {
                        var settingsField = modType.GetField("settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
                                          modType.GetField("Settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                        if (settingsField != null)
                        {
                            var settings = settingsField.GetValue(null);
                            if (settings != null)
                            {
                                // Try various field names
                                string[] possibleFieldNames = {
                                    "SidearmLimit",
                                    "LimitModeSingle",
                                    "maxSidearms",
                                    "MaxSidearms",
                                    "LimitModeAmount",
                                    "sidearmLimit"
                                };

                                foreach (var fieldName in possibleFieldNames)
                                {
                                    var limitField = settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (limitField != null)
                                    {
                                        var value = limitField.GetValue(settings);
                                        if (value is int limit)
                                            return limit;
                                        else if (value is float fLimit)
                                            return (int)fLimit;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error getting max sidearms: {e.Message}");
                }
            }

            // Default fallback
            return 3;
        }

        public static int GetCurrentSidearmCount(Pawn pawn)
        {
            if (!IsLoaded() || pawn?.inventory?.innerContainer == null)
                return 0;

            return pawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
        }

        public static bool ShouldSkipAutoEquip(Pawn pawn)
        {
            // Don't skip anyone - let Simple Sidearms handle the heavy lifting
            return false;
        }

        public static bool ShouldUpgradeMainWeapon(Pawn pawn, ThingWithComps newWeapon, float currentScore, float newScore)
        {
            // Always allow main weapon upgrades - Simple Sidearms will manage switching between them
            return true;
        }

        public static bool IsSimpleSidearmsSwitch(Pawn pawn, Thing weapon)
        {
            // Would need to track this through Harmony patches
            return false;
        }

        public static void CheckPendingSidearmRegistrations(Pawn pawn)
        {
            // Removed - not needed with simplified system
        }

        public static void CleanupPendingRegistrations()
        {
            // Removed - not needed with simplified system
        }

        public static bool HasNoSidearms(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null)
                return false;

            // Quick check - no weapons in inventory at all
            if (pawn.inventory?.innerContainer == null ||
                !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon))
                return true;

            // More thorough check using Simple Sidearms data
            EnsureInitialized();
            if (!_initialized)
                return true;

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp == null)
                    return true;

                var sidearmsList = GetRememberedWeapons(comp);
                if (sidearmsList == null)
                    return true;

                // Check if list is empty
                foreach (var item in sidearmsList)
                {
                    return false; // Has at least one sidearm
                }

                return true; // Empty list
            }
            catch
            {
                // On error, fall back to simple inventory check
                return !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon);
            }
        }

        // Debug helper to list all Simple Sidearms job defs
        public static void DebugListSidearmJobs()
        {
            Log.Message("\n[AutoArm] === Simple Sidearms Job Defs ===");

            var sidearmJobs = DefDatabase<JobDef>.AllDefs
                .Where(j => j.defName.ToLower().Contains("sidearm") ||
                          j.defName.ToLower().Contains("secondary") ||
                          j.modContentPack?.Name?.Contains("Simple Sidearms") == true)
                .ToList();

            Log.Message($"[AutoArm] Found {sidearmJobs.Count} possible sidearm job defs:");
            foreach (var job in sidearmJobs)
            {
                Log.Message($"  - {job.defName} (from {job.modContentPack?.Name ?? "unknown"})");
            }

            Log.Message("[AutoArm] === End Job Defs ===\n");
        }
    }
}
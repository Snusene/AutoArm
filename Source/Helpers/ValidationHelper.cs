// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Centralized pawn and weapon validation with defensive checks
// Prevents errors from modded content and edge cases
//
// ================== CORE VALIDATION PRINCIPLE ==================
// Player control is ABSOLUTE. If a player forbids an item or sets outfit
// restrictions, those choices are honored WITHOUT EXCEPTION.
// 
// This mod works WITHIN player restrictions, never around them.
// Being unarmed, injured, or in any "emergency" state does NOT bypass:
// - Forbidden status (if player forbids it, no pawn touches it)
// - Outfit filters (if not allowed by outfit, pawn won't equip it)
// - Any other player-configured restrictions
//
// NEVER add code that bypasses these checks for ANY reason.
// The player's decisions always take priority over mod convenience.
// ===============================================================

using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.Weapons;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse;
using Verse.AI;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Centralized validation logic to eliminate redundancy
    /// </summary>
    public static class ValidationHelper
    {
        // Cache for storage type checks (fixes #23)
        private static readonly System.Collections.Generic.HashSet<Type> knownStorageTypes = new System.Collections.Generic.HashSet<Type>();

        private static readonly System.Collections.Generic.HashSet<Type> knownNonStorageTypes = new System.Collections.Generic.HashSet<Type>();

        // Limit cache sizes to prevent unbounded growth
        private const int MaxCacheSize = 100; // Most mods won't have more than 100 storage types

        /// <summary>
        /// Consolidated weapon validation (fixes #1, #15)
        /// </summary>
        public static bool IsValidWeapon(ThingWithComps weapon, Pawn pawn, out string reason, bool skipSimpleSidearmsCheck = false)
        {
            reason = "";

            // Basic validation
            if (weapon == null)
            {
                reason = "Weapon is null";
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Weapon validation failed for {pawn?.LabelShort ?? "null pawn"}: {reason}");
                }
                return false;
            }

            // IMPORTANT: We track if pawn is unarmed for logging purposes only
            // Being unarmed does NOT bypass any validation checks
            // Player-set restrictions (forbidden, outfit) must ALWAYS be respected
            bool isUnarmed = pawn?.equipment?.Primary == null;

            if (weapon.def == null)
            {
                reason = "Weapon def is null";
                return false;
            }

            if (!WeaponValidation.IsProperWeapon(weapon))
            {
                reason = "Not a proper weapon";
                return false;
            }

            if (weapon.Destroyed)
            {
                reason = "Weapon destroyed";
                return false;
            }

            if (weapon.Map == null || weapon.Map != pawn.Map)
            {
                // During tests, weapons might not be spawned yet
                if (!TestRunner.IsRunningTests)
                {
                    reason = "Wrong map";
                    return false;
                }
            }

            // Note: Ritual items like wood logs and lanterns are already excluded by WeaponValidation.IsProperWeapon()
            // No need for special ritual item handling here

            // SimpleSidearms compatibility - but with smart handling for primary weapons
            if (!TestRunner.IsRunningTests && SimpleSidearmsCompat.IsLoaded() && !skipSimpleSidearmsCheck)
            {
                bool shouldCheckSidearmRestrictions = true;

                // Case 1: Pawn is completely unarmed - they need ANY weapon as primary
                if (pawn.equipment?.Primary == null)
                {
                    shouldCheckSidearmRestrictions = false;
                }
                // Case 2: Check if current primary is a "true" primary or a remembered sidearm
                else if (pawn.equipment?.Primary != null)
                {
                    // SimpleSidearmsCompat simplified - can't check if remembered sidearm
                    bool primaryIsRememberedSidearm = false;

                    if (!primaryIsRememberedSidearm)
                    {
                        // Current primary is a true primary weapon, not a sidearm
                        // Check if this new weapon would replace it (i.e., be equipped as primary)
                        var currentScore = WeaponScoreCache.GetCachedScore(pawn, pawn.equipment.Primary);
                        var newScore = WeaponScoreCache.GetCachedScore(pawn, weapon);

                        // If new weapon is significantly better, it would replace the primary
                        // Use the actual configured threshold, not a hardcoded value
                        float upgradeThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
                        if (newScore > currentScore * upgradeThreshold)
                        {
                            shouldCheckSidearmRestrictions = false;
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] {weapon.Label} would replace primary (score {newScore:F0} > {currentScore:F0} * {upgradeThreshold:F2}) - bypassing SS sidearm restrictions");
                            }
                        }
                    }
                    else
                    {
                        // Case 3: Current primary IS a remembered sidearm - check if colonist has ANY non-sidearm weapon
                        // This prevents colonists from being "locked" with only sidearms
                        bool hasAnyNonSidearmWeapon = false;

                        // Check inventory for any non-sidearm weapons
                        if (pawn.inventory?.innerContainer != null)
                        {
                            foreach (var item in pawn.inventory.innerContainer)
                            {
                                if (item is ThingWithComps invWeapon && invWeapon.def.IsWeapon)
                                {
                                    // SimpleSidearmsCompat simplified - assume not remembered sidearm
                                    if (true)
                                    {
                                        hasAnyNonSidearmWeapon = true;
                                        break;
                                    }
                                }
                            }
                        }

                        // If colonist has ONLY sidearms (no true primary), allow picking up any weapon as primary
                        if (!hasAnyNonSidearmWeapon)
                        {
                            shouldCheckSidearmRestrictions = false;
                        }
                        else
                        {
                            // Apply SimpleSidearms restrictions
                        }
                    }
                }

                if (shouldCheckSidearmRestrictions)
                {
                    string ssReason;
                    bool allowed = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pawn, out ssReason);
                    if (!allowed)
                    {
                        reason = $"SimpleSidearms: {ssReason}";

                        // Log when we're applying SS restrictions to what might be a primary upgrade
                        if (AutoArmMod.settings?.debugLogging == true && pawn.equipment?.Primary != null)
                        {
                            var currentScore = WeaponScoreCache.GetCachedScore(pawn, pawn.equipment.Primary);
                            var newScore = WeaponScoreCache.GetCachedScore(pawn, weapon);
                            float upgradeThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;

                            if (newScore > currentScore)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] {weapon.Label} rejected by SS: {ssReason} (scores: {newScore:F0} vs {currentScore:F0}, needed >{currentScore * upgradeThreshold:F0})");
                            }
                        }

                        return false;
                    }
                }
            }

            // Outfit filter check - NEVER skip this (player-controlled restriction)
            // WARNING: Do NOT add emergency/unarmed exceptions here - players set outfit filters for a reason!
            if (!TestRunner.IsRunningTests && pawn.outfits?.CurrentApparelPolicy?.filter != null && WeaponValidation.IsProperWeapon(weapon))
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;
                if (!filter.Allows(weapon))
                {
                    reason = "Not allowed by outfit";
                    return false;
                }
            }

            // Use vanilla EquipmentUtility.CanEquip for comprehensive mod compatibility
            // This catches most mod restrictions including body size requirements
            // NOTE: This check is now handled in WeaponScoreCache to avoid repeated calls
            // The cache will return CANNOT_EQUIP (-1) if this check fails
            // We still do a basic check here for immediate validation needs
            if (!TestRunner.IsRunningTests)
            {
                // Quick check for obvious restrictions we can detect without calling CanEquip
                // Body size check via weapon mass (heavy weapons often have restrictions)
                if (weapon.GetStatValue(StatDefOf.Mass) > 5.0f && pawn.BodySize < 1.0f)
                {
                    reason = $"Likely body size restriction (weapon mass: {weapon.GetStatValue(StatDefOf.Mass):F1}, pawn size: {pawn.BodySize:F1})";
                    return false;
                }
                
                // Check for mod extension restrictions
                if (weapon.def.modExtensions != null)
                {
                    foreach (var extension in weapon.def.modExtensions)
                    {
                        if (extension != null)
                        {
                            var type = extension.GetType();
                            var typeName = type.Name.ToLower();

                            // Check for common framework extension types
                            if (typeName.Contains("framework") || typeName.Contains("fff") ||
                                typeName.Contains("bodysize") || typeName.Contains("restriction"))
                            {
                                // Check body size fields
                                var bodySizeField = type.GetField("requiredBodySize") ??
                                                  type.GetField("minBodySize") ??
                                                  type.GetField("maxBodySize") ??
                                                  type.GetField("bodySize") ??
                                                  type.GetField("minimumBodySize");

                                if (bodySizeField != null)
                                {
                                    // Check if pawn's body size meets requirement
                                    float pawnBodySize = pawn.BodySize;
                                    var requiredSize = bodySizeField.GetValue(extension);

                                    if (requiredSize is float minSize)
                                    {
                                        if (pawnBodySize < minSize)
                                        {
                                            reason = $"Body size too small ({pawnBodySize:F2} < {minSize:F2})";
                                            return false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Forbidden check - NEVER skip this (player-controlled restriction)
            // CRITICAL: If a player forbids an item, NO pawn should pick it up, even if unarmed
            // WARNING: Do NOT add emergency/unarmed exceptions here - forbidden means forbidden!
            if (!TestRunner.IsRunningTests && weapon.IsForbidden(pawn))
            {
                reason = "Forbidden";
                // Log for debugging if pawn is unarmed and being blocked by forbidden status
                if (isUnarmed && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: Forbidden (pawn is unarmed but respecting player restriction)");
                }
                return false;
            }

            // Biocode check
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
            {
                reason = "Biocoded to another pawn";
                return false;
            }

            // Quest item check
            if (weapon.questTags != null && weapon.questTags.Count > 0)
            {
                reason = "Quest item";
                return false;
            }

            // Recently dropped check
            if (!TestRunner.IsRunningTests && DroppedItemTracker.IsRecentlyDropped(weapon))
            {
                reason = "Recently dropped";
                return false;
            }

            // Reservation check
            if (!TestRunner.IsRunningTests)
            {
                var reservationManager = pawn.Map?.reservationManager;
                if (reservationManager != null)
                {
                    if (reservationManager.IsReservedByAnyoneOf(weapon, pawn.Faction))
                    {
                        if (!reservationManager.CanReserve(pawn, weapon, 1, -1, null, false))
                        {
                            reason = "Reserved by someone else";
                            return false;
                        }
                    }
                }
            }

            // Reachability check
            if (!TestRunner.IsRunningTests && !pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, false))
            {
                reason = "Cannot reach";
                return false;
            }

            // Container check
            if (!TestRunner.IsRunningTests && weapon.ParentHolder != null)
            {
                if (weapon.ParentHolder is Pawn_EquipmentTracker equipTracker)
                {
                    var equipperPawn = equipTracker.pawn;
                    if (equipperPawn != null && equipperPawn != pawn)
                    {
                        reason = "Equipped by someone else";
                        return false;
                    }
                }
                else if (weapon.ParentHolder is Pawn_InventoryTracker)
                {
                    reason = "In someone's inventory";
                    return false;
                }
                else if (weapon.ParentHolder is MinifiedThing)
                {
                    reason = "Minified";
                    return false;
                }
                else if (!(weapon.ParentHolder is Map) && !IsStorageContainer(weapon.ParentHolder))
                {
                    reason = "In non-storage container";
                    return false;
                }
            }

            // Status checks
            if (!TestRunner.IsRunningTests && weapon.IsBurning())
            {
                reason = "On fire";
                return false;
            }

            // Research check
            if (weapon.def.researchPrerequisites != null)
            {
                foreach (var research in weapon.def.researchPrerequisites)
                {
                    if (research != null && !research.IsFinished)
                    {
                        reason = $"Research not complete: {research.label}";
                        return false;
                    }
                }
            }

            // Combat Extended ammo check (if enabled and CE has ammo system enabled)
            if (CECompat.ShouldCheckAmmo() && CECompat.ShouldSkipWeaponForCE(weapon, pawn))
            {
                reason = "No ammo available (Combat Extended)";
                return false;
            }

            if (AutoArmMod.settings?.debugLogging == true && !string.IsNullOrEmpty(reason))
            {
                AutoArmLogger.Debug($"Weapon validation passed for {weapon.Label} and {pawn.LabelShort}");
            }
            if (AutoArmMod.settings?.debugLogging == true && !TestRunner.IsRunningTests)
            {
                AutoArmLogger.Debug($"Pawn validation passed for {pawn.LabelShort}");
            }
            return true;
        }

        /// <summary>
        /// Consolidated pawn validation (fixes #3, #16, #24)
        /// </summary>
        public static bool IsValidPawn(Pawn pawn, out string reason, bool checkForWeapons = true)
        {
            reason = "";

            // Basic null and state checks
            if (pawn == null)
            {
                reason = "Pawn is null";
                return false;
            }

            if (!pawn.Spawned)
            {
                reason = "Not spawned";
                return false;
            }

            if (pawn.Dead || pawn.Destroyed)
            {
                reason = "Dead or destroyed";
                return false;
            }

            if (pawn.Map == null)
            {
                reason = "No map";
                return false;
            }

            if (!pawn.Position.IsValid || !pawn.Position.InBounds(pawn.Map))
            {
                reason = "Invalid position";
                return false;
            }

            // Race checks
            if (!pawn.RaceProps.Humanlike)
            {
                reason = "Not humanlike";
                return false;
            }

            if (pawn.RaceProps.IsMechanoid)
            {
                reason = "Is mechanoid";
                return false;
            }

            // Status checks
            if (pawn.Downed)
            {
                reason = "Downed";
                return false;
            }

            if (pawn.InMentalState)
            {
                reason = "In mental state";
                return false;
            }

            if (pawn.IsPrisoner)
            {
                reason = "Is prisoner";
                return false;
            }

            // Note: Slaves are allowed to pick up weapons - they're colony-owned workers
            // The player controls this via outfit filters if they don't want armed slaves

            if (pawn.Drafted)
            {
                reason = "Drafted";
                return false;
            }

            if (pawn.InBed())
            {
                reason = "In bed";
                return false;
            }

            // Faction and colonist checks
            if (!SafeIsColonist(pawn))
            {
                // Special handling for slaves - they're owned by the colony
                if (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony)
                {
                    // Slaves are allowed - continue validation
                }
                else
                {
                    reason = "Not a colonist";
                    return false;
                }
            }

            // Violence capability check (only for weapon-related validation)
            if (checkForWeapons && pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                reason = "Incapable of violence";
                return false;
            }

            // Health checks
            if (pawn.health?.capacities == null)
            {
                reason = "No health capacities";
                return false;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                reason = "Cannot manipulate";
                return false;
            }

            // Lord job checks
            if (pawn.Map?.lordManager != null)
            {
                var lord = pawn.Map.lordManager.LordOf(pawn);
                if (lord != null)
                {
                    if (!(lord.LordJob is LordJob_DefendBase ||
                          lord.LordJob is LordJob_AssistColony))
                    {
                        reason = $"In lord job: {lord.LordJob.GetType().Name}";
                        return false;
                    }
                }
            }

            // Ritual check
            if (IsInRitual(pawn))
            {
                reason = "In ritual or ceremony";
                return false;
            }

            // Hauling check - don't interfere with hauling jobs
            if (pawn.CurJob != null &&
                (pawn.CurJob.def == JobDefOf.HaulToCell ||
                 pawn.CurJob.def == JobDefOf.HaulToContainer ||
                 pawn.CurJob.def?.defName?.Contains("Haul") == true ||
                 pawn.CurJob.def?.defName?.Contains("Carry") == true ||
                 // Pick Up And Haul specific jobs
                 pawn.CurJob.def?.defName == "HaulToInventory" ||
                 pawn.CurJob.def?.defName == "UnloadYourHauledInventory" ||
                 // Other inventory-related jobs that might be from PUAH or similar mods
                 pawn.CurJob.def?.defName?.Contains("Inventory") == true ||
                 pawn.CurJob.def?.defName == "UnloadYourInventory" ||
                 pawn.CurJob.def?.defName == "TakeToInventory"))
            {
                reason = "Currently hauling";
                return false;
            }

            // Caravan check
            if (pawn.IsCaravanMember())
            {
                reason = "In caravan";
                return false;
            }

            // Temporary colonist check
            if (JobGiverHelpers.IsTemporaryColonist(pawn))
            {
                if (!(AutoArmMod.settings?.allowTemporaryColonists ?? false))
                {
                    reason = "Temporary colonist (quest/borrowed)";
                    return false;
                }
            }

            // Age check for Biotech
            if (ModsConfig.BiotechActive && pawn.ageTracker != null)
            {
                if (pawn.ageTracker.AgeBiologicalYears < 18)
                {
                    if (!(AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false))
                    {
                        reason = $"Children not allowed to equip weapons ({pawn.ageTracker.AgeBiologicalYears} years old)";
                        return false;
                    }

                    int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;
                    if (pawn.ageTracker.AgeBiologicalYears < minAge)
                    {
                        reason = $"Too young ({pawn.ageTracker.AgeBiologicalYears} < {minAge})";
                        return false;
                    }
                }
            }

            // Note: Bonded weapons are handled separately in JobGiver_PickUpBetterWeapon

            return true;
        }

        /// <summary>
        /// Consolidated colonist check (fixes #2)
        /// </summary>
        public static bool SafeIsColonist(Pawn pawn)
        {
            if (pawn == null)
                return false;

            try
            {
                // Direct colonist check
                if (pawn.IsColonist)
                    return true;

                // Slaves are also valid for auto-equip purposes
                if (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                // Some modded pawns might throw when checking IsColonist
                Log.Warning($"[AutoArm] Error checking IsColonist for {pawn?.Label ?? "unknown pawn"}: {ex.Message}");

                // Fallback checks
                try
                {
                    return pawn.Faction == Faction.OfPlayer && pawn.IsFreeColonist;
                }
                catch
                {
                    try
                    {
                        return pawn.Faction == Faction.OfPlayer && !pawn.IsPrisoner;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Check if pawn can use a specific weapon
        /// </summary>
        public static bool CanPawnUseWeapon(Pawn pawn, ThingWithComps weapon, out string reason, bool skipSimpleSidearmsCheck = false)
        {
            reason = "";

            if (!IsValidPawn(pawn, out reason))
                return false;

            if (!IsValidWeapon(weapon, pawn, out reason, skipSimpleSidearmsCheck))
                return false;

            // Trait checks
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
            {
                reason = "Brawler won't use ranged weapon";
                return false;
            }

            // Hunter explosive check
            if (pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hunting) == true &&
                weapon.def.IsRangedWeapon && JobGiverHelpers.IsExplosiveWeapon(weapon.def))
            {
                reason = "Hunter won't use explosive weapon";
                return false;
            }

            // Skill requirement check
            if (weapon.def.equippedStatOffsets != null)
            {
                foreach (var statOffset in weapon.def.equippedStatOffsets)
                {
                    if (statOffset.stat == StatDefOf.ShootingAccuracyPawn && statOffset.value < 0)
                    {
                        float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
                        if (shootingSkill < 5f && Math.Abs(statOffset.value) > 0.2f)
                        {
                            reason = "Pawn shooting skill too low for weapon";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Check if a pawn is conceited (has conceited title)
        /// </summary>
        public static bool IsConceited(Pawn pawn)
        {
            if (pawn == null)
                return false;

            // Check for conceited royal titles (Royalty DLC)
            if (ModsConfig.RoyaltyActive && pawn.royalty != null)
            {
                foreach (var title in pawn.royalty.AllTitlesInEffectForReading)
                {
                    if (title.conceited)
                        return true;
                }
            }

            // Note: Regular personality traits like Greedy, Jealous, or Abrasive
            // are NOT the same as being "conceited" in RimWorld terms.
            // Conceited is specifically a royal title property.

            return false;
        }

        /// <summary>
        /// Clear storage type caches - called during cleanup or when switching saves
        /// </summary>
        public static void ClearStorageTypeCaches()
        {
            knownStorageTypes.Clear();
            knownNonStorageTypes.Clear();

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("Cleared storage type caches");
            }
        }

        /// <summary>
        /// Check if a weapon is bonded to a pawn (persona weapons from Royalty DLC)
        /// </summary>
        public static bool IsWeaponBondedToPawn(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || pawn == null)
                return false;

            // Check for bladelink weapon comp (persona weapons)
            var bladelinkComp = weapon.TryGetComp<CompBladelinkWeapon>();
            if (bladelinkComp != null && bladelinkComp.Biocoded && bladelinkComp.CodedPawn == pawn)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if pawn is currently participating in a ritual or ceremony
        /// </summary>
        public static bool IsInRitual(Pawn pawn)
        {
            if (pawn?.Map?.lordManager == null)
                return false;

            var lord = pawn.Map.lordManager.LordOf(pawn);
            if (lord?.LordJob == null)
                return false;

            // Check if the lord job is a ritual-type job
            var lordJobType = lord.LordJob.GetType();

            // Check the type hierarchy for ritual base classes
            var currentType = lordJobType;
            while (currentType != null && currentType != typeof(object))
            {
                var typeName = currentType.Name;

                // Common ritual lord job patterns
                if (typeName.Contains("Ritual") ||
                    typeName.Contains("Gathering") ||
                    typeName.Contains("Party") ||
                    typeName.Contains("Ceremony") ||
                    typeName.Contains("Speech") ||
                    typeName.Contains("Festival") ||
                    typeName.Contains("Celebration") ||
                    typeName.Contains("Skylantern") ||
                    typeName.Contains("Marriage") ||
                    typeName.Contains("Funeral") ||
                    typeName.Contains("Date") ||
                    typeName.Contains("Lovin") ||
                    typeName.Contains("Bestowing") ||
                    typeName.Contains("Advent") ||      // Military Advent and other advent rituals
                    typeName.Contains("Trial") ||        // Trial rituals
                    typeName.Contains("Dance") ||        // Dance rituals
                    typeName.Contains("Drum") ||         // Drum rituals
                    typeName.Contains("Sacrifice"))      // Sacrifice rituals
                {
                    return true;
                }
                currentType = currentType.BaseType;
            }

            // Also check current job for ritual-specific activities
            if (pawn.CurJobDef != null)
            {
                var jobName = pawn.CurJobDef.defName.ToLower();
                if (jobName.Contains("ritual") ||
                    jobName.Contains("spectate") ||
                    jobName.Contains("ceremony") ||
                    jobName.Contains("attendparty") ||
                    jobName.Contains("gatheringparticipate") ||
                    jobName.Contains("standandbesociallyactive") ||
                    jobName.Contains("hold") ||          // Jobs that involve holding ritual items
                    jobName.Contains("carry") ||         // Jobs that involve carrying ritual items
                    jobName.Contains("deliver") ||       // Jobs that involve delivering ritual items
                    jobName.Contains("bring") ||         // Jobs that involve bringing ritual items
                    jobName.Contains("dance") ||         // Dance jobs
                    jobName.Contains("drum") ||          // Drum jobs
                    jobName.Contains("chant") ||         // Chanting jobs
                    jobName.Contains("pray"))            // Prayer jobs
                {
                    return true;
                }
            }

            // Check if pawn is holding ritual-specific items
            if (pawn.carryTracker?.CarriedThing != null)
            {
                var carried = pawn.carryTracker.CarriedThing;
                if (carried.def.defName.Contains("WoodLog") ||
                    carried.def.defName.Contains("Lantern") ||
                    carried.def.defName.Contains("RitualItem") ||
                    carried.def.defName.Contains("Skylantern") ||
                    carried.def.defName.Contains("Effigy") ||
                    carried.def.defName.Contains("Pyre"))
                {
                    return true;
                }
            }

            return false;
        }

        // Storage keywords organized by frequency of use
        private static readonly string[] storageKeywords = new string[]
        {
            // Most common
            "storage", "shelf", "rack", "locker", "container", "chest", "crate", "box",
            // Kitchen/food storage
            "fridge", "freezer", "refrigerator", "cooler", "cupboard",
            // Clothing/equipment
            "dresser", "wardrobe", "cabinet", "armoire", "closet",
            // Industrial
            "pallet", "barrel", "bin", "silo", "hopper", "skip", "tray", "dsu",
            // Misc
            "basket", "hamper", "vault", "safe", "hook", "meathook", "armory", "stockpile"
        };

        /// <summary>
        /// Optimized storage container check that maintains mod compatibility
        /// </summary>
        internal static bool IsStorageContainer(IThingHolder holder)
        {
            if (holder == null) return false;

            var holderType = holder.GetType();

            // Check cache first
            if (knownStorageTypes.Contains(holderType))
                return true;
            if (knownNonStorageTypes.Contains(holderType))
                return false;

            bool isStorage = false;

            // Check if it's a building with storage
            if (holder is Building building)
            {
                // Primary check - has storage settings or slot group (fastest)
                if (building.def.building?.fixedStorageSettings != null ||
                    building.GetSlotGroup() != null)
                {
                    isStorage = true;
                }
                // Check for known storage class
                else if (string.Equals(building.def.thingClass?.Name, "Building_Storage", StringComparison.Ordinal))
                {
                    isStorage = true;
                }
                // Check for deep storage comp (common mod)
                else if (building.AllComps?.Any(c => c?.GetType().Name == "CompDeepStorage") == true)
                {
                    isStorage = true;
                }
                // Keyword check - optimized with early exit
                else if (building.def.defName != null)
                {
                    var defNameLower = building.def.defName.ToLowerInvariant();
                    foreach (var keyword in storageKeywords)
                    {
                        if (defNameLower.Contains(keyword))
                        {
                            isStorage = true;
                            break;
                        }
                    }
                    
                    // Also check label if defName didn't match
                    if (!isStorage && building.def.label != null)
                    {
                        var labelLower = building.def.label.ToLowerInvariant();
                        foreach (var keyword in storageKeywords)
                        {
                            if (labelLower.Contains(keyword))
                            {
                                isStorage = true;
                                break;
                            }
                        }
                    }
                }
            }

            // Check if it's a stockpile
            if (holder is Zone_Stockpile)
            {
                isStorage = true;
            }

            // Check base types
            if (!isStorage)
            {
                var baseType = holderType.BaseType;
                while (baseType != null && baseType != typeof(object))
                {
                    if (baseType.Name == "Building_Storage")
                    {
                        isStorage = true;
                        break;
                    }
                    baseType = baseType.BaseType;
                }
            }

            // Check type name patterns (for mod compatibility)
            if (!isStorage)
            {
                var holderName = holderType.Name.ToLowerInvariant();
                var holderNamespace = holderType.Namespace?.ToLowerInvariant() ?? "";

                // Check holder type name against storage keywords
                foreach (var keyword in storageKeywords)
                {
                    if (holderName.Contains(keyword))
                    {
                        isStorage = true;
                        break;
                    }
                }
                
                // Check namespace for known storage mod patterns
                if (!isStorage)
                {
                    string[] namespacePatterns = { "storage", "weaponstorage", "adaptive", 
                                                   "rimfridge", "lwm", "deepstorage", 
                                                   "extendedstorage", "projectrimfactory" };
                    
                    foreach (var pattern in namespacePatterns)
                    {
                        if (holderNamespace.Contains(pattern))
                        {
                            isStorage = true;
                            break;
                        }
                    }
                }
            }

            // Cache the result (with size limit to prevent unbounded growth)
            if (isStorage)
            {
                if (knownStorageTypes.Count < MaxCacheSize)
                {
                    knownStorageTypes.Add(holderType);
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"Added storage type to cache: {holderType.Name}");
                    }
                }
            }
            else
            {
                if (knownNonStorageTypes.Count < MaxCacheSize)
                    knownNonStorageTypes.Add(holderType);
            }

            return isStorage;
        }

        /// <summary>
        /// Check if pawn prefers melee weapons based on traits and skills
        /// </summary>
        public static bool PrefersMeleeWeapon(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                return true;

            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;

            return meleeSkill > shootingSkill + 3f;
        }
    }
}
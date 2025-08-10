// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Main job generator for weapon pickup decisions
// Handles: Weapon searches, scoring, raid detection, forced weapon logic
// Uses: Per-tick limiting and caching for performance optimization
// Critical: Primary entry point for Think Tree weapon checks

using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.Weapons;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    /// <summary>
    /// Main JobGiver for weapon pickup - optimized with caching and per-tick limiting
    /// - Per-tick limiting (prevents lag spikes)
    /// - Weapon score caching (avoids recalculation)
    /// - Progressive search radius (finds closer weapons first)
    /// - Equip cooldowns (prevents constant switching)
    /// </summary>
    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        // Simplified per-tick limiting with pawn rotation
        private static int lastProcessTick = 0;
        private static readonly HashSet<Pawn> processedThisTick = new HashSet<Pawn>();
        private static int pawnsProcessedThisTick = 0;
        
        // Weapon equip cooldown tracking - prevents constant switching
        private static readonly Dictionary<Pawn, int> lastWeaponEquipTick = new Dictionary<Pawn, int>();
        
        // ========== VALIDATION CACHE WITH SIDE EFFECT HANDLING ==========
        // Cache only AFTER all side effects (blacklisting) have occurred
        private static readonly Dictionary<int, Dictionary<int, (bool isValid, int expiryTick)>> validationCache = 
            new Dictionary<int, Dictionary<int, (bool, int)>>(32);
            
        // Ultra-short cache to only avoid redundant checks in same tick/search
        private const int VALIDATION_CACHE_DURATION = 10; // 0.17 seconds - VERY short

        protected override Job TryGiveJob(Pawn pawn)
        {
            return TestTryGiveJob(pawn);
        }

        public Job TestTryGiveJob(Pawn pawn)
        {
            try
            {
                // Early returns for basic checks
                if (pawn == null) return null;
                if (AutoArmMod.settings?.modEnabled != true) return null;

                // === Per-tick limiting ===
                int currentTick = Find.TickManager.TicksGame;
                
                // PERFORMANCE: Only process every Nth tick for better performance
                // E.g., if ProcessEveryNthTick = 2, only process on even ticks (0, 2, 4, 6...)
                // This reduces processing load by factor of N
                if (currentTick % Constants.ProcessEveryNthTick != 0)
                {
                    return null; // Skip this tick
                }
                
                if (currentTick != lastProcessTick)
                {
                    lastProcessTick = currentTick;
                    processedThisTick.Clear();
                    pawnsProcessedThisTick = 0;
                }

                // Skip if this pawn was already processed this tick
                if (processedThisTick.Contains(pawn))
                {
                    return null;
                }
                
                // Apply unified limit for all pawns
                if (pawnsProcessedThisTick >= Constants.MaxPawnsPerTick)
                {
                    return null;
                }
                
                // Determine if emergency (unarmed) - ThinkNodes already validated colonist/violence/drafted
                bool isEmergency = pawn.equipment?.Primary == null;
                
                // Log emergency calls less frequently to reduce spam
                if (AutoArmMod.settings?.debugLogging == true && isEmergency && pawn.IsHashIntervalTick(600))
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] EMERGENCY JobGiver called - pawn is UNARMED");
                }
                
                // === Check weapon equip cooldown ===
                // Prevents flip-flopping after just equipping something
                if (lastWeaponEquipTick.TryGetValue(pawn, out int lastEquipTick))
                {
                    int ticksSinceEquip = currentTick - lastEquipTick;
                    
                    if (ticksSinceEquip < Constants.WeaponEquipCooldownTicks)
                    {
                        // Still on cooldown from last weapon equip
                        return null;
                    }
                }
                
                // Mark pawn as processed this tick and increment counter
                processedThisTick.Add(pawn);
                pawnsProcessedThisTick++;

                // Get current weapon and check restrictions
                var currentWeapon = pawn.equipment?.Primary;
                var weaponRestriction = GetWeaponRestriction(pawn, currentWeapon);
                
                if (weaponRestriction.blockSearch)
                    return null;

                // Use cached weapon scores for performance
                float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : 0f;

                // Look for best weapon
                ThingWithComps bestWeapon = FindBestWeapon(pawn, currentScore, weaponRestriction.restrictToType);

                // Removed spammy FindBestWeapon result logging

                // Try sidearms ONLY if pawn already has a primary weapon
                // CRITICAL FIX: Unarmed pawns should NEVER look for sidearms - they need a primary first!
                if (bestWeapon == null && 
                    pawn.equipment?.Primary != null &&  // Only look for sidearms if we have a primary
                    SimpleSidearmsCompat.IsLoaded() && 
                    !SimpleSidearmsCompat.ReflectionFailed && 
                    AutoArmMod.settings?.autoEquipSidearms == true)
                {
                    Job sidearmJob = SimpleSidearmsCompat.FindBestSidearmJob(pawn, GetWeaponScore, (int)Constants.DefaultSearchRadius);
                    if (sidearmJob != null)
                    {
                        AutoEquipTracker.MarkAsAutoEquip(sidearmJob, pawn);
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Found sidearm job: {sidearmJob.def.defName} for {sidearmJob.targetA.Thing?.Label}");
                        }
                        return sidearmJob;
                    }
                    else if (AutoArmMod.settings?.debugLogging == true && pawn.IsHashIntervalTick(3600))  // Log less frequently
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] No sidearm upgrades found");
                    }
                }
                // Removed spammy skipping sidearm search log

                if (bestWeapon == null)
                {
                    // Always log details for unarmed pawns
                    if (isEmergency)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Emergency: is unarmed but found no weapon!");
                        LogWeaponRejectionReasons(pawn);  // Always log why for unarmed pawns
                    }
                    else
                    {
                        LogDebugSummary(pawn, "found no suitable weapon");
                    }
                    return null;
                }

                // Check if we already have this weapon type in inventory
                if (!ShouldPickupWeaponType(pawn, bestWeapon, currentWeapon))
                    return null;

                // Final reachability check
                if (!pawn.CanReserveAndReach(bestWeapon, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, false))
                    return null;

                // Create and configure job - pass pawn for smart swap logic
                Job job = JobHelper.CreateEquipJob(bestWeapon, isSidearm: false, pawn: pawn);
                if (job != null)
                {
                    ConfigureAutoEquipJob(job, pawn, currentWeapon, bestWeapon, weaponRestriction.wasForced);
                    
                    // Emergency jobs (unarmed pawns) should never expire
                    if (isEmergency)
                    {
                        job.expiryInterval = Constants.EmergencyJobExpiry;
                        job.checkOverrideOnExpire = false;
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Emergency equip: {bestWeapon.Label}");
                        }
                    }
                    
                    // Record that this pawn is about to equip a weapon
                    RecordWeaponEquip(pawn);
                }

                return job;
            }
            catch (Exception ex)
            {
                AutoArmLogger.LogError($"Error in TryGiveJob for {pawn?.Name?.ToStringShort ?? "unknown"}", ex);
                return null;
            }
        }

        /// <summary>
        /// Record that a pawn has equipped a weapon to start cooldown
        /// </summary>
        public static void RecordWeaponEquip(Pawn pawn)
        {
            if (pawn == null) return;
            
            lastWeaponEquipTick[pawn] = Find.TickManager.TicksGame;
        }
        
        /// <summary>
        /// Clear cooldown for a specific pawn (e.g., when forced by player)
        /// </summary>
        public static void ClearWeaponCooldown(Pawn pawn)
        {
            if (pawn == null) return;
            
            lastWeaponEquipTick.Remove(pawn);
        }
        
        /// <summary>
        /// Get remaining cooldown time in ticks
        /// </summary>
        public static int GetRemainingCooldown(Pawn pawn)
        {
            if (pawn == null) return 0;
            
            if (lastWeaponEquipTick.TryGetValue(pawn, out int lastEquipTick))
            {
                int currentTick = Find.TickManager.TicksGame;
                int ticksSinceEquip = currentTick - lastEquipTick;
                
                if (ticksSinceEquip < Constants.WeaponEquipCooldownTicks)
                {
                    return Constants.WeaponEquipCooldownTicks - ticksSinceEquip;
                }
            }
            
            return 0;
        }
        
        /// <summary>
        /// Consolidated weapon restriction logic
        /// </summary>
        private (ThingDef restrictToType, bool blockSearch, bool wasForced) GetWeaponRestriction(Pawn pawn, ThingWithComps currentWeapon)
        {
            if (currentWeapon == null)
                return (null, false, false);

            bool isForced = ForcedWeaponHelper.IsForced(pawn, currentWeapon);
            bool isSSManaged = SimpleSidearmsCompat.IsLoaded() && 
                              !SimpleSidearmsCompat.ReflectionFailed &&
                              SimpleSidearmsCompat.IsSimpleSidearmsManaged(pawn, currentWeapon);

            if (isForced)
            {
                if (AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                    return (null, true, true); // Block search entirely
                return (currentWeapon.def, false, true); // Restrict to same type
            }
            
            if (isSSManaged)
            {
                return (currentWeapon.def, false, false); // Restrict to same type
            }

            return (null, false, false); // No restrictions
        }

        /// <summary>
        /// Check if pawn should consider this weapon - WITH SAFE CACHING
        /// </summary>
        private bool ShouldConsiderWeapon(Pawn pawn, ThingWithComps weapon, ThingWithComps currentWeapon)
        {
            // CRITICAL: All validation checks must be applied equally to all pawns
            // Being unarmed does NOT bypass player-set restrictions (forbidden, outfit filters)
            // WARNING: Do NOT add "emergency" or "unarmed" exceptions to any checks below
            
            // DEBUG: Log entry for unarmed pawns to trace rejection reasons
            bool isUnarmed = pawn.equipment?.Primary == null;
            // Removed verbose per-weapon logging - too spammy
            
            // ========== ALWAYS CHECK THESE FIRST (can change instantly) ==========
            
            // Basic weapon validation - null checks, def checks
            if (!WeaponValidation.IsProperWeapon(weapon))
                return false;
            
            // SIMPLIFIED APPROACH: Check if weapon is accessible
            // A weapon is accessible if it's on the map and not currently being used by someone else
            
            // Basic validation - weapon must exist and not be destroyed
            if (weapon.Destroyed)
                return false;
            
            // Must be on our map
            if (weapon.Map != pawn.Map)
                return false;
            
            // Must have a valid position on the map
            if (!weapon.Position.IsValid || !weapon.Position.InBounds(pawn.Map))
                return false;
            
            // Must not be forbidden
            if (weapon.IsForbidden(pawn))
                return false;
            
            // Check if weapon is in someone else's possession
            // We only care about equipment and inventory - everything else is fair game
            if (weapon.holdingOwner != null)
            {
                // Get the parent holder
                var holder = weapon.holdingOwner.Owner;
                
                // Check if the holder is a pawn
                if (holder is Pawn otherPawn && otherPawn != pawn)
                {
                    // Is it equipped?
                    if (otherPawn.equipment?.Primary == weapon)
                        return false;
                    // Is it in inventory?
                    if (otherPawn.inventory?.innerContainer?.Contains(weapon) == true)
                        return false;
                    // Is it being carried?
                    if (otherPawn.carryTracker?.CarriedThing == weapon)
                        return false;
                }
                // All other holders (storage, zones, etc.) are OK
            }
            
            // ========== CHECK CACHE (only for expensive validations) ==========
            int currentTick = Find.TickManager.TicksGame;
            int pawnID = pawn.thingIDNumber;
            int weaponID = weapon.thingIDNumber;
            
            // Check if we have a cached result
            if (validationCache.TryGetValue(pawnID, out var pawnCache))
            {
                if (pawnCache.TryGetValue(weaponID, out var cached))
                {
                    if (currentTick < cached.expiryTick)
                    {
                        // Cache hit - but ONLY if weapon hasn't changed state
                        // Double-check critical state that could have changed
                        if (!weapon.IsForbidden(pawn) && !weapon.Destroyed && weapon.holdingOwner == null)
                        {
                            return cached.isValid;
                        }
                        // State changed - remove from cache and recalculate
                        pawnCache.Remove(weaponID);
                    }
                }
            }
            else
            {
                pawnCache = new Dictionary<int, (bool, int)>(20);
                validationCache[pawnID] = pawnCache;
            }
            
            // ========== TIER 2: Fast Simple Lookups (not worth caching) ==========
            
            // HashSet lookup - very fast
            if (DroppedItemTracker.IsRecentlyDropped(weapon))
                return false;
            
            // Quest item check - simple null check + count
            if (weapon.questTags != null && weapon.questTags.Count > 0)
                return false; // Don't take quest items
            
            // Simple reference equality check (NOT scoring yet)
            bool isDuplicateType = (currentWeapon?.def == weapon.def && weapon != currentWeapon);
            
            // ========== CRITICAL: Handle side effects BEFORE caching ==========
            
            // Body size check - MUST come before blacklist check (adds to blacklist on failure)
            // This HAS SIDE EFFECTS so we must do it every time, can't cache before it
            if (!CheckBodySizeRequirement(pawn, weapon))
            {
                // Add to blacklist to prevent repeated attempts
                WeaponBlacklist.AddToBlacklist(weapon.def, pawn, "Body size requirement not met");
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] {weapon.Label} requires larger body size, blacklisting");
                }
                
                // Cache the rejection AFTER the side effect
                pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                return false;
            }
            
            // Blacklist check - dictionary lookup (MUST be after body size check)
            if (WeaponBlacklist.IsBlacklisted(weapon.def, pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] {weapon.Label} is blacklisted, skipping");
                }
                // Cache this rejection
                pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                return false;
            }
            
            // ========== TIER 4: More Expensive Checks (worth caching) ==========
            
            // Trait-based restrictions - multiple lookups but still relatively fast
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
            {
                pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                return false; // Brawler won't use ranged
            }
            
            // Biocode check - GetComp is a virtual call
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
            {
                pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                return false; // Biocoded to another pawn
            }
            
            // Outfit filter check - can be complex with modded filters
            if (pawn.outfits?.CurrentApparelPolicy?.filter != null && 
                !pawn.outfits.CurrentApparelPolicy.filter.Allows(weapon))
            {
                pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                return false;
            }
            
            // ========== TIER 5: Most Expensive Checks ==========
            
            // Check inventory for duplicates - requires iteration
            if (pawn.inventory?.innerContainer != null)
            {
                if (pawn.inventory.innerContainer.Any(item => item is ThingWithComps invWeapon && invWeapon.def == weapon.def))
                {
                    pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                    return false;
                }
            }
            
            // Now do the expensive duplicate scoring check if needed
            if (isDuplicateType)
            {
                // OPTIMIZATION: Quick quality check to avoid scoring obviously inferior weapons
                QualityCategory currentQuality = QualityCategory.Normal;
                QualityCategory newQuality = QualityCategory.Normal;
                
                currentWeapon.TryGetQuality(out currentQuality);
                weapon.TryGetQuality(out newQuality);
                
                // If quality is clearly worse, skip without scoring or logging
                if (newQuality < currentQuality)
                {
                    return false; // Obviously inferior, no need to score
                }
                
                // If same quality but different material, or better quality, do full score comparison
                float existingScore = GetWeaponScore(pawn, currentWeapon);
                float newScore = GetWeaponScore(pawn, weapon);
                float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
                
                if (newScore <= existingScore * threshold)
                {
                    // Only log if debug enabled AND it's same or better quality (otherwise we already know it's worse)
                    if (AutoArmMod.settings?.debugLogging == true && newQuality >= currentQuality)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: Not enough upgrade over {currentWeapon.Label} ({newScore:F1} <= {existingScore:F1} * {threshold:F2})");
                    }
                    pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                    return false; // Not enough of an upgrade
                }
            }
            
            // Temporary colonist check - last because it's rare and involves settings lookup
            if (JobGiverHelpers.IsTemporaryColonist(pawn) && !(AutoArmMod.settings?.allowTemporaryColonists ?? false))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: Temporary colonist not allowed to auto-equip");
                }
                pawnCache[weaponID] = (false, currentTick + VALIDATION_CACHE_DURATION);
                return false;
            }
            
            // Weapon is valid - cache the positive result
            pawnCache[weaponID] = (true, currentTick + VALIDATION_CACHE_DURATION);
            return true;
        }

        /// <summary>
        /// Check if pawn should pickup this weapon type
        /// </summary>
        private bool ShouldPickupWeaponType(Pawn pawn, ThingWithComps newWeapon, ThingWithComps currentWeapon)
        {
            // Same-type upgrade is OK
            if (currentWeapon != null && currentWeapon.def == newWeapon.def)
                return true;

            // Check inventory for duplicates
            if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing item in pawn.inventory.innerContainer)
                {
                    if (item is ThingWithComps invWeapon && invWeapon.def == newWeapon.def)
                        return false; // Already has this type
                }
            }

            return true;
        }

        /// <summary>
        /// Configure the auto-equip job with tracking
        /// </summary>
        private void ConfigureAutoEquipJob(Job job, Pawn pawn, ThingWithComps currentWeapon, ThingWithComps newWeapon, bool wasForced)
        {
            AutoEquipTracker.MarkAsAutoEquip(job, pawn);
            AutoEquipTracker.SetPreviousWeapon(pawn, currentWeapon?.def);

            // Transfer forced status if upgrading a forced weapon
            if (wasForced && AutoArmMod.settings?.allowForcedWeaponUpgrades == true)
            {
                AutoEquipTracker.SetWeaponToForce(pawn, newWeapon);
            }
        }

        /// <summary>
        /// Optimized weapon finding with progressive search and early exits
        /// </summary>
        private ThingWithComps FindBestWeapon(Pawn pawn, float currentScore, ThingDef restrictToType = null)
        {
            // EMERGENCY: Unarmed pawns search progressively but not the entire map immediately
            bool isUnarmed = pawn.equipment?.Primary == null;
            
            float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
            // CRITICAL FIX: For unarmed pawns, ANY weapon is better than nothing (score > 0)
            // Don't apply upgrade threshold when we have no weapon at all!
            float bestScore = isUnarmed ? 0f : (currentScore * threshold);
            ThingWithComps bestWeapon = null;
            
            // Removed spammy search start logging
            float[] searchRadii = isUnarmed 
                ? new float[] { 30f, 60f, 90f }                            // Unarmed: wider search but not whole map
                : new float[] { 15f, 30f, Constants.DefaultSearchRadius };  // Armed: progressive search
            int weaponsChecked = 0;
            const int maxWeaponsToCheck = 20;
            
            // Check if we should only look in storage
            bool storageOnly = AutoArmMod.settings?.onlyAutoEquipFromStorage == true;
            
            foreach (float radius in searchRadii)
            {
                // OPTIMIZATION: Pre-calculate radius squared for distance checks
                float radiusSquared = radius * radius;
                
                // Use optimized cache method based on storage setting
                var cachedWeapons = storageOnly 
                    ? ImprovedWeaponCacheManager.GetStorageWeaponsNear(pawn.Map, pawn.Position, radius)
                    : ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, radius);
                
                // Log once if no weapons found in storage
                if (storageOnly && cachedWeapons.Count == 0 && AutoArmMod.settings?.debugLogging == true && radius == searchRadii[0])
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] No weapons found in storage within {radius} units");
                }
                
                // Apply type restriction if needed
                if (restrictToType != null)
                {
                    cachedWeapons = cachedWeapons.Where(w => w.def == restrictToType).ToList();
                }
                
                // OPTIMIZATION: Don't sort if we have few weapons
                IEnumerable<ThingWithComps> weaponsToCheck;
                if (cachedWeapons.Count <= 5)
                {
                    // Few weapons - check them all without sorting overhead
                    weaponsToCheck = cachedWeapons;
                }
                else
                {
                    // Many weapons - sort by distance for optimal checking order
                    weaponsToCheck = cachedWeapons
                        .OrderBy(w => w.Position.DistanceToSquared(pawn.Position));
                }
                
                foreach (var weapon in weaponsToCheck)
                {
                    // OPTIMIZATION 1: Early distance check before expensive operations
                    float distSquared = weapon.Position.DistanceToSquared(pawn.Position);
                    if (distSquared > radiusSquared)
                    {
                        // Since weapons are sorted by distance, all remaining are too far
                        if (cachedWeapons.Count > 5) // Only break if we sorted
                            break;
                        else
                            continue; // If not sorted, keep checking others
                    }
                    
                    if (weaponsChecked++ >= maxWeaponsToCheck)
                        break;
                    
                    // Only do expensive validation for weapons actually in range
                    if (!ShouldConsiderWeapon(pawn, weapon, pawn.equipment?.Primary))
                        continue;
                    
                    float score = GetWeaponScore(pawn, weapon);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                        
                        // OPTIMIZATION 3: Stop searching if we found an excellent upgrade
                        if (score > currentScore * 1.5f)
                        {
                            // 50% better? That's good enough, stop searching
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Found excellent weapon {bestWeapon.Label} (50% upgrade), stopping search");
                            }
                            return bestWeapon;
                        }
                    }
                }
                
                // Found good weapon at this radius - don't expand search
                if (bestWeapon != null)
                {
                    // OPTIMIZATION 4: More aggressive radius exit based on score
                    if (bestScore > currentScore * 1.2f)
                    {
                        // 20% better at this radius? Don't search further
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Found good weapon {bestWeapon.Label} at radius {radius}, stopping expansion");
                        }
                        break;
                    }
                    else if (bestScore > currentScore * 1.1f && radius >= 30f)
                    {
                        // 10% better and already searched medium range? Stop
                        break;
                    }
                }
            }
            
            return bestWeapon;
        }

        /// <summary>
        /// Get cached weapon score
        /// </summary>
        public float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn == null)
                return 0f;

            // Use weapon score cache for performance
            return WeaponScoreCache.GetCachedScoreWithCE(pawn, weapon);
        }

        /// <summary>
        /// Simple debug logging for unarmed pawns
        /// </summary>
        private void LogDebugSummary(Pawn pawn, string result)
        {
            if (AutoArmMod.settings?.debugLogging != true || pawn.equipment?.Primary != null)
                return;
                
            if (pawn.IsHashIntervalTick(600)) // Log less frequently
            {
                AutoArmLogger.Debug($"[{pawn.LabelShort}] UNARMED: {result}");
            }
        }

        /// <summary>
        /// Log why weapons were rejected (for debugging)
        /// </summary>
        private void LogWeaponRejectionReasons(Pawn pawn)
        {
            if (AutoArmMod.settings?.debugLogging != true || pawn.equipment?.Primary != null)
                return;

            // For unarmed pawns, check progressively larger areas to find ANY weapons
            float[] debugRadii = { 15f, 30f, 60f, 100f, 9999f };
            foreach (float radius in debugRadii)
            {
                var cachedWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, radius);
                if (cachedWeapons.Count > 0)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] {cachedWeapons.Count} weapons found within {radius} units:");
                    foreach (var w in cachedWeapons.Take(5))
                    {
                        if (!ValidationHelper.CanPawnUseWeapon(pawn, w, out string rejectReason, skipSimpleSidearmsCheck: true, fromJobGiver: true))
                        {
                            AutoArmLogger.Debug($"  - {w.Label} at {w.Position}: {rejectReason}");
                        }
                        else
                        {
                            AutoArmLogger.Debug($"  - {w.Label} at {w.Position}: VALID but not selected?!");
                        }
                    }
                    break; // Found weapons at this radius, stop searching
                }
            }
            
            // If still no weapons found anywhere
            var allMapWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 9999f);
            if (allMapWeapons.Count == 0)
            {
                AutoArmLogger.Debug($"[{pawn.LabelShort}] NO WEAPONS found on entire map! Cache may be empty.");
                
                // DEBUG: Check what's actually on the map vs what's in cache
                var actualWeapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                    .OfType<ThingWithComps>()
                    .Where(w => WeaponValidation.IsProperWeapon(w))
                    .ToList();
                    
                if (actualWeapons.Count > 0)
                {
                    AutoArmLogger.Warn($"[CACHE BUG] Cache returned 0 weapons but {actualWeapons.Count} weapons exist on map!");
                    // Log first few actual weapons
                    foreach (var weapon in actualWeapons.Take(3))
                    {
                        AutoArmLogger.Warn($"  - Real weapon: {weapon.Label} at {weapon.Position} (forbidden: {weapon.IsForbidden(pawn)})");
                    }
                }
                else
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Confirmed: No valid weapons exist on map");
                }
            }
        }

        // Static cache for body size requirements - checked once per weapon type
        private static readonly Dictionary<ThingDef, float> weaponBodySizeCache = new Dictionary<ThingDef, float>();
        
        /// <summary>
        /// Check if pawn meets body size requirements for weapon (cached for performance)
        /// </summary>
        private bool CheckBodySizeRequirement(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return true;
            
            // BYPASS CHECK: If children weapons are allowed and this is a child within the allowed age range
            // This lets your "fun setting" work - 3-year-olds can pick up miniguns!
            if (AutoArmMod.settings?.allowChildrenToEquipWeapons == true && 
                pawn.ageTracker != null && 
                pawn.ageTracker.AgeBiologicalYears < 18 &&
                pawn.ageTracker.AgeBiologicalYears >= (AutoArmMod.settings?.childrenMinAge ?? 13))
            {
                // Removed spammy child bypass logging
                return true; // Allow children to use any weapon
            }
                
            // Normal body size check for adults and non-allowed children
            // Check cache first (extremely fast)
            if (!weaponBodySizeCache.TryGetValue(weapon.def, out float requiredSize))
            {
                // Not in cache - determine requirement ONCE for this weapon type
                requiredSize = DetermineBodySizeRequirement(weapon.def);
                weaponBodySizeCache[weapon.def] = requiredSize;
            }
            
            // Simple comparison
            bool canUse = pawn.BodySize >= requiredSize;
            
            if (!canUse && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[{pawn.LabelShort}] Body size {pawn.BodySize:F2} < required {requiredSize:F2} for {weapon.Label}");
            }
            
            return canUse;
        }
        
        /// <summary>
        /// Determine body size requirement for a weapon type (called once per weapon def)
        /// </summary>
        private float DetermineBodySizeRequirement(ThingDef weaponDef)
        {
            // Check def name patterns for known large weapons
            string defName = weaponDef.defName.ToLower();
            
            // Mech weapons typically require large body
            if (defName.Contains("mech") || defName.Contains("charge") || 
                defName.Contains("plasma") || defName.Contains("inferno"))
            {
                return 1.5f; // Large body required
            }
            
            // Use mass as primary indicator
            float mass = weaponDef.GetStatValueAbstract(StatDefOf.Mass);
            
            if (mass > 10f) 
                return 1.5f;  // Very heavy weapons = large body (mechs)
            if (mass > 5f) 
                return 1.0f;  // Heavy weapons = normal body
            if (mass > 3f) 
                return 0.75f; // Medium weapons = teen or adult
                
            return 0f;        // Light weapons = any body size
        }
        
        /// <summary>
        /// Cleanup static caches
        /// </summary>
        public static void CleanupCaches()
        {
            // TimingHelper removed - it only had empty methods
            WeaponBlacklist.CleanupOldEntries(); // Also cleanup blacklist
            
            // ========== CLEAN VALIDATION CACHE ==========
            int currentTick = Find.TickManager.TicksGame;
            
            // Clean expired entries and dead pawns
            var toRemove = new List<int>();
            foreach (var kvp in validationCache)
            {
                // Remove all expired entries for this pawn
                var expiredWeapons = kvp.Value
                    .Where(w => currentTick >= w.Value.expiryTick)
                    .Select(w => w.Key)
                    .ToList();
                
                foreach (var weaponID in expiredWeapons)
                {
                    kvp.Value.Remove(weaponID);
                }
                
                // Mark empty caches for removal
                if (kvp.Value.Count == 0)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            // Remove empty pawn caches
            foreach (var pawnID in toRemove)
            {
                validationCache.Remove(pawnID);
            }
            
            // Safety: Clear if too large (shouldn't happen with 10-tick expiry)
            if (validationCache.Count > 100)
            {
                validationCache.Clear();
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Validation cache cleared (exceeded 100 pawns)");
                }
            }
            
            // ========== REST OF EXISTING CLEANUP CODE ==========
            
            // Clean up weapon equip cooldown tracking for dead pawns
            var deadPawns = lastWeaponEquipTick.Keys
                .Where(p => p == null || p.Destroyed || p.Dead)
                .ToList();
            
            foreach (var pawn in deadPawns)
            {
                lastWeaponEquipTick.Remove(pawn);
            }
            
            // Also clean up expired cooldowns to prevent dictionary growth
            var expiredCooldowns = lastWeaponEquipTick
                .Where(kvp => (currentTick - kvp.Value) > Constants.WeaponEquipCooldownTicks)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var pawn in expiredCooldowns)
            {
                lastWeaponEquipTick.Remove(pawn);
            }
            
            // Clear body size cache if it gets too large (shouldn't happen but good practice)
            if (weaponBodySizeCache.Count > 500)
            {
                weaponBodySizeCache.Clear();
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Cleared weapon body size cache (exceeded 500 entries)");
                }
            }
        }
        
        /// <summary>
        /// Clear validation cache for a specific pawn (call when outfit changes, etc.)
        /// </summary>
        public static void InvalidatePawnValidationCache(Pawn pawn)
        {
            if (pawn != null)
            {
                validationCache.Remove(pawn.thingIDNumber);
            }
        }
    }
}

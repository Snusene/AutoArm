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
    /// </summary>
    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        // Simplified per-tick limiting with pawn rotation
        private static int lastProcessTick = 0;
        private static readonly HashSet<Pawn> processedThisTick = new HashSet<Pawn>();
        private static int unarmedProcessedThisTick = 0;
        private static int armedProcessedThisTick = 0;

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

                // === Per-tick limiting with emergency priority ===
                int currentTick = Find.TickManager.TicksGame;
                if (currentTick != lastProcessTick)
                {
                    lastProcessTick = currentTick;
                    processedThisTick.Clear();
                    unarmedProcessedThisTick = 0;
                    armedProcessedThisTick = 0;
                }

                // Skip if this pawn was already processed this tick
                if (processedThisTick.Contains(pawn))
                {
                    return null;
                }
                
                // Determine if emergency (unarmed) - ThinkNodes already validated colonist/violence/drafted
                bool isEmergency = pawn.equipment?.Primary == null;
                
                // Log emergency calls for debugging
                if (AutoArmMod.settings?.debugLogging == true && isEmergency)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] EMERGENCY JobGiver called - pawn is UNARMED");
                }
                
                // Apply appropriate limit based on armed status
                if (isEmergency)
                {
                    // Unarmed pawns have higher limit but still capped for performance
                    if (unarmedProcessedThisTick >= Constants.MaxUnarmedPawnsPerTick)
                    {
                        return null;
                    }
                }
                else
                {
                    // Armed pawns have lower limit
                    if (armedProcessedThisTick >= Constants.MaxPawnsPerTick)
                    {
                        return null;
                    }
                }

                // Hash interval check removed - per-tick limiting is sufficient for performance

                // REMOVED redundant validation - ThinkNodes already checked:
                // - Is colonist
                // - Can do violence
                // - Is drafted
                // - Armed/unarmed status
                // We only validate things that can change or aren't checked by ThinkNodes

                // Global performance kill-switch - checked FIRST before anything else
                if (AutoArmMod.settings?.disableDuringRaids == true && ModInit.IsLargeRaidActive)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Skipping weapon switch - large raid active (performance mode)");
                    }
                    return null;
                }

                // Mark pawn as processed this tick and increment appropriate counter
                processedThisTick.Add(pawn);
                if (isEmergency)
                    unarmedProcessedThisTick++;
                else
                    armedProcessedThisTick++;

                // Get current weapon and check restrictions
                var currentWeapon = pawn.equipment?.Primary;
                var weaponRestriction = GetWeaponRestriction(pawn, currentWeapon);
                
                if (weaponRestriction.blockSearch)
                    return null;

                // Use cached weapon scores for performance
                float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : 0f;

                // Look for best weapon
                ThingWithComps bestWeapon = FindBestWeapon(pawn, currentScore, weaponRestriction.restrictToType);

                // Try sidearms if no primary upgrade found
                if (bestWeapon == null && SimpleSidearmsCompat.IsLoaded() && 
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

                // Create and configure job
                Job job = JobHelper.CreateEquipJob(bestWeapon);
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
        /// Check if pawn should consider this weapon
        /// </summary>
        private bool ShouldConsiderWeapon(Pawn pawn, ThingWithComps weapon, ThingWithComps currentWeapon)
        {
            // CRITICAL: All validation checks must be applied equally to all pawns
            // Being unarmed does NOT bypass player-set restrictions (forbidden, outfit filters)
            // WARNING: Do NOT add "emergency" or "unarmed" exceptions to any checks below
            
            // Check blacklist first to avoid repeated failed attempts
            if (WeaponBlacklist.IsBlacklisted(weapon.def, pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] {weapon.Label} is blacklisted, skipping");
                }
                return false;
            }
            
            // Body size check - CRITICAL for preventing equip loops
            if (!CheckBodySizeRequirement(pawn, weapon))
            {
                // Add to blacklist to prevent repeated attempts
                WeaponBlacklist.AddToBlacklist(weapon.def, pawn, "Body size requirement not met");
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] {weapon.Label} requires larger body size, blacklisting");
                }
                return false;
            }
            
            // Check if it's a duplicate type
            if (currentWeapon?.def == weapon.def && weapon != currentWeapon)
            {
                float existingScore = GetWeaponScore(pawn, currentWeapon);
                float newScore = GetWeaponScore(pawn, weapon);
                float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
                
                if (newScore <= existingScore * threshold)
                    return false; // Not enough of an upgrade
            }
            
            // Check inventory for duplicates
            if (pawn.inventory?.innerContainer != null)
            {
                if (pawn.inventory.innerContainer.Any(item => item is ThingWithComps invWeapon && invWeapon.def == weapon.def))
                    return false;
            }
            
            // Direct weapon validation without redundant pawn checks
            // ThinkNodes already validated: colonist, violence capability, drafted status
            
            // Basic weapon checks
            if (!WeaponValidation.IsProperWeapon(weapon) || weapon.IsForbidden(pawn))
                return false;
                
            // Recently dropped check
            if (DroppedItemTracker.IsRecentlyDropped(weapon))
                return false;
                
            // Outfit filter check
            if (pawn.outfits?.CurrentApparelPolicy?.filter != null && 
                !pawn.outfits.CurrentApparelPolicy.filter.Allows(weapon))
                return false;
            
            // Biocode check
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
                return false; // Biocoded to another pawn
                
            // Quest item check
            if (weapon.questTags != null && weapon.questTags.Count > 0)
                return false; // Don't take quest items
            
            // Trait-based restrictions
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
                return false; // Brawler won't use ranged
                
            // Hunter explosive check
            if (pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hunting) == true &&
                weapon.def.IsRangedWeapon && JobGiverHelpers.IsExplosiveWeapon(weapon.def))
                return false; // Hunter won't use explosives
            
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
        /// Optimized weapon finding with progressive search
        /// </summary>
        private ThingWithComps FindBestWeapon(Pawn pawn, float currentScore, ThingDef restrictToType = null)
        {
            float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
            float bestScore = currentScore * threshold;
            ThingWithComps bestWeapon = null;
            
            // EMERGENCY: Unarmed pawns search entire map immediately
            bool isUnarmed = pawn.equipment?.Primary == null;
            float[] searchRadii = isUnarmed 
                ? new float[] { Constants.DefaultSearchRadius, 100f, 9999f }  // Unarmed: aggressive search including whole map
                : new float[] { 15f, 30f, Constants.DefaultSearchRadius };     // Armed: progressive search
            int weaponsChecked = 0;
            const int maxWeaponsToCheck = 20;
            
            foreach (float radius in searchRadii)
            {
                var cachedWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(
                    pawn.Map, pawn.Position, radius);
                
                // Apply type restriction if needed
                if (restrictToType != null)
                {
                    cachedWeapons = cachedWeapons.Where(w => w.def == restrictToType).ToList();
                }
                
                // Sort by distance for this radius
                var sortedWeapons = cachedWeapons
                    .OrderBy(w => w.Position.DistanceToSquared(pawn.Position));
                
                foreach (var weapon in sortedWeapons)
                {
                    if (weaponsChecked++ >= maxWeaponsToCheck)
                        break;
                        
                    if (!ShouldConsiderWeapon(pawn, weapon, pawn.equipment?.Primary))
                        continue;
                    
                    float score = GetWeaponScore(pawn, weapon);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                        
                        // Found excellent weapon - stop searching
                        if (score > currentScore * 1.5f)
                            return bestWeapon;
                    }
                }
                
                // Found good weapon at this radius - don't expand search
                if (bestWeapon != null && bestScore > currentScore * 1.2f)
                    break;
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
                        if (!ValidationHelper.CanPawnUseWeapon(pawn, w, out string rejectReason, skipSimpleSidearmsCheck: true))
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

        /// <summary>
        /// Check if pawn meets body size requirements for weapon
        /// </summary>
        private bool CheckBodySizeRequirement(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def?.modExtensions == null)
                return true; // No mod extensions, no restrictions
                
            foreach (var extension in weapon.def.modExtensions)
            {
                if (extension == null)
                    continue;
                    
                var type = extension.GetType();
                var typeName = type.Name.ToLower();
                
                // Check for body size related extensions
                if (typeName.Contains("bodysize") || typeName.Contains("restriction") || 
                    typeName.Contains("framework") || typeName.Contains("fff"))
                {
                    // Try to find body size fields
                    var fields = new string[] { 
                        "requiredBodySize", "minBodySize", "minimumBodySize", 
                        "bodySize", "requiredSize", "minimumSize", "minSize",
                        "supportedBodysize", "supportedBodySize" // Common in mech mods
                    };
                    
                    foreach (var fieldName in fields)
                    {
                        var field = type.GetField(fieldName, 
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.NonPublic | 
                            System.Reflection.BindingFlags.Instance);
                            
                        if (field != null)
                        {
                            var value = field.GetValue(extension);
                            if (value is float minSize)
                            {
                                if (pawn.BodySize < minSize)
                                {
                                    if (AutoArmMod.settings?.debugLogging == true)
                                    {
                                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Body size {pawn.BodySize:F2} < required {minSize:F2} for {weapon.Label}");
                                    }
                                    return false;
                                }
                            }
                        }
                    }
                    
                    // Also check properties (some mods use properties instead of fields)
                    var properties = new string[] { 
                        "RequiredBodySize", "MinBodySize", "MinimumBodySize", 
                        "BodySize", "RequiredSize", "MinimumSize", "MinSize",
                        "SupportedBodysize", "SupportedBodySize"
                    };
                    
                    foreach (var propName in properties)
                    {
                        var prop = type.GetProperty(propName, 
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.NonPublic | 
                            System.Reflection.BindingFlags.Instance);
                            
                        if (prop != null && prop.CanRead)
                        {
                            var value = prop.GetValue(extension);
                            if (value is float minSize)
                            {
                                if (pawn.BodySize < minSize)
                                {
                                    if (AutoArmMod.settings?.debugLogging == true)
                                    {
                                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Body size {pawn.BodySize:F2} < required {minSize:F2} for {weapon.Label}");
                                    }
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
            
            // Also check weapon's equippedStatOffsets for indirect body size hints
            // Some mods use mass thresholds as a proxy for body size
            if (weapon.GetStatValue(StatDefOf.Mass) > 5.0f && pawn.BodySize < 1.0f)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon too heavy ({weapon.GetStatValue(StatDefOf.Mass):F1}kg) for body size {pawn.BodySize:F2}");
                }
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Cleanup static caches
        /// </summary>
        public static void CleanupCaches()
        {
            TimingHelper.CleanupOldCooldowns();
            WeaponBlacklist.CleanupOldEntries(); // Also cleanup blacklist
            // Raid cache cleanup removed - now using global ModInit.IsLargeRaidActive
        }
    }
}

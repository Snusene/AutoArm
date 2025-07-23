using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    /// <summary>
    /// Main JobGiver for weapon pickup - now uses consolidated helpers
    /// </summary>
    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        private static readonly Dictionary<Pawn, PawnWeaponCheckData> pawnCheckData = new Dictionary<Pawn, PawnWeaponCheckData>();
        
        private class PawnWeaponCheckData
        {
            public int lastSearchTick = 0;
            public int consecutiveFailures = 0;
            public ThingWithComps lastWeaponSearched = null;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return TestTryGiveJob(pawn);
        }

        public Job TestTryGiveJob(Pawn pawn)
        {
            try
            {
                // Use consolidated validation
                if (!ValidationHelper.IsValidPawn(pawn, out string reason))
                {
                    AutoArmDebug.LogPawn(pawn, $"Invalid pawn: {reason}");
                    return null;
                }



                // Check appropriate cooldown based on whether pawn is armed
                bool isUnarmed = pawn.equipment?.Primary == null;
                var cooldownType = isUnarmed ? 
                    TimingHelper.CooldownType.WeaponSearch :        // Emergency: 5 seconds
                    TimingHelper.CooldownType.FailedUpgradeSearch;  // Upgrades: 30 seconds
                
                if (TimingHelper.IsOnCooldown(pawn, cooldownType))
                {
                    // Don't log cooldown messages - they spam too much
                    return null;
                }

                // Check forced weapon status
                if (ForcedWeaponHelper.HasForcedWeapon(pawn))
                {
                    // Log with cooldown to prevent spam
                    TimingHelper.LogWithCooldown(pawn, "Has forced weapon - skipping check", 
                        TimingHelper.CooldownType.ForcedWeaponLog);
                    return null;
                }

                // SimpleSidearms compatibility check
                if (SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(pawn))
                {
                    AutoArmDebug.LogPawn(pawn, "Has temporary sidearm equipped - skipping");
                    return null;
                }

                var currentWeapon = pawn.equipment?.Primary;
                
                // Check if current weapon is a SimpleSidearms-managed weapon
                // When you manually swap to a sidearm using SimpleSidearms, that weapon becomes
                // a "remembered sidearm". This prevents AutoArm from suggesting different weapon types.
                bool isSimpleSidearmsWeapon = currentWeapon != null && 
                                             SimpleSidearmsCompat.IsLoaded() && 
                                             SimpleSidearmsCompat.PrimaryIsRememberedSidearm(pawn);
                
                if (isSimpleSidearmsWeapon)
                {
                    AutoArmDebug.LogPawn(pawn, $"Current primary {currentWeapon.def.defName} is a SimpleSidearms weapon - will only consider same-type upgrades");
                }
                
                float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : 0f;

                // Get best weapon
                var bestWeapon = FindBestWeapon(pawn, currentScore, isSimpleSidearmsWeapon ? currentWeapon?.def : null);
                if (bestWeapon == null)
                {
                    // Set failed search cooldown - same type we checked earlier
                    bool wasUnarmed = pawn.equipment?.Primary == null;
                    TimingHelper.SetCooldown(pawn, wasUnarmed ? 
                        TimingHelper.CooldownType.WeaponSearch : 
                        TimingHelper.CooldownType.FailedUpgradeSearch);
                    return null;
                }

                // Check if we're upgrading to the same weapon type
                if (currentWeapon != null && currentWeapon.def == bestWeapon.def)
                {
                    // Mark current weapon to prevent SimpleSidearms from saving it
                    DroppedItemTracker.MarkPendingSameTypeUpgrade(currentWeapon);
                    AutoArmDebug.LogWeapon(pawn, bestWeapon, "Upgrading to better version of same weapon type");
                }
                
                // Create standard equip job - vanilla equip handles the swap perfectly
                var job = JobHelper.CreateEquipJob(bestWeapon);
                if (job != null)
                {
                    // Mark as auto-equip
                    AutoEquipTracker.MarkAsAutoEquip(job, pawn);
                    AutoEquipTracker.SetPreviousWeapon(pawn, currentWeapon?.def);
                    
                    AutoArmDebug.LogWeapon(pawn, bestWeapon, "Found better weapon to equip");
                }
                return job;
            }
            catch (Exception ex)
            {
                AutoArmDebug.LogError($"Error in TryGiveJob for {pawn?.Name?.ToStringShort ?? "unknown"}", ex);
                return null;
            }
        }

        private ThingWithComps FindBestWeapon(Pawn pawn, float currentScore, ThingDef restrictToType = null)
        {
            // Use improved weapon cache
            IEnumerable<ThingWithComps> nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 40f)
                .Where(w => ValidationHelper.CanPawnUseWeapon(pawn, w, out _))
                .OrderBy(w => w.Position.DistanceToSquared(pawn.Position))
                .Take(20);

            // If restricted to a specific type (SimpleSidearms weapon), filter to that type only
            // This ensures that when using SimpleSidearms-managed weapons, AutoArm will only
            // suggest upgrades of the same weapon type (e.g., normal knife -> excellent knife)
            if (restrictToType != null)
            {
                nearbyWeapons = nearbyWeapons.Where(w => w.def == restrictToType);
                AutoArmDebug.Log($"Restricting weapon search to type: {restrictToType.defName}");
            }

            ThingWithComps bestWeapon = null;
            float bestScore = currentScore * 1.05f; // 5% improvement threshold

            foreach (var weapon in nearbyWeapons)
            {
                float score = GetWeaponScore(pawn, weapon);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWeapon = weapon;
                }
            }

            return bestWeapon;
        }

        public float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn == null)
                return 0f;

            // Use weapon score cache instead of calling helper directly
            return WeaponScoreCache.GetCachedScore(pawn, weapon);
        }

        public static void CleanupCaches()
        {
            // Use consolidated cleanup
            CleanupHelper.CleanupPawnDictionary(pawnCheckData);
        }
    }
}

using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    /// <summary>
    /// Helper methods for job givers - now uses consolidated helpers
    /// </summary>
    public static class JobGiverHelpers
    {
        /// <summary>
        /// Check if a pawn is a temporary colonist (quest lodger, borrowed, etc)
        /// </summary>
        public static bool IsTemporaryColonist(Pawn pawn)
        {
            if (pawn == null || !pawn.IsColonist)
                return false;
            
            // Quest lodgers (Royalty DLC)
            if (ModsConfig.RoyaltyActive)
            {
                try
                {
                    if (pawn.IsQuestLodger())
                    {
                        AutoArmDebug.LogPawn(pawn, "Is quest lodger");
                        return true;
                    }
                }
                catch { }
            }
            
            // Has quest tags
            if (pawn.questTags != null && pawn.questTags.Count > 0)
            {
                AutoArmDebug.LogPawn(pawn, $"Has quest tags: {string.Join(", ", pawn.questTags)}");
                return true;
            }
            
            // Borrowed by another faction
            if (pawn.IsBorrowedByAnyFaction())
            {
                AutoArmDebug.LogPawn(pawn, "Is borrowed by faction");
                return true;
            }
            
            // Guest from another faction
            if (pawn.guest != null && pawn.guest.HostFaction == Faction.OfPlayer && 
                pawn.Faction != Faction.OfPlayer)
            {
                AutoArmDebug.LogPawn(pawn, $"Is guest from {pawn.Faction?.Name}");
                return true;
            }
            
            // On loan to another faction
            if (pawn.Faction == Faction.OfPlayer && pawn.HostFaction != null && 
                pawn.HostFaction != Faction.OfPlayer)
            {
                AutoArmDebug.LogPawn(pawn, $"Is on loan to {pawn.HostFaction.Name}");
                return true;
            }
            
            // Has foreign royal title
            if (ModsConfig.RoyaltyActive && pawn.royalty != null)
            {
                foreach (var title in pawn.royalty.AllTitlesInEffectForReading)
                {
                    if (title.faction != Faction.OfPlayer)
                    {
                        AutoArmDebug.LogPawn(pawn, $"Has foreign royal title from {title.faction.Name}");
                        return true;
                    }
                }
            }
            
            return false;
        }

        // Delegate all validation to ValidationHelper (fixes #1, #2, #3, #16, #24)
        public static bool SafeIsColonist(Pawn pawn) => ValidationHelper.SafeIsColonist(pawn);
        
        public static bool IsValidPawnForAutoEquip(Pawn pawn, out string reason) => ValidationHelper.IsValidPawn(pawn, out reason);
        
        public static bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn, out string reason) => ValidationHelper.IsValidWeapon(weapon, pawn, out reason);
        
        public static bool CanPawnUseWeapon(Pawn pawn, ThingWithComps weapon, out string reason) => ValidationHelper.CanPawnUseWeapon(pawn, weapon, out reason);

        // Delegate job logic to JobHelper (fixes #17)
        public static bool IsCriticalJob(Pawn pawn, bool hasNoSidearms = false) => JobHelper.IsCriticalJob(pawn);
        
        public static bool IsSafeToInterrupt(JobDef jobDef, float upgradePercentage = 0f)
        {
            if (jobDef == null)
                return true;
            
            // Create a temporary job to check
            var tempJob = new Job(jobDef);
            return JobHelper.IsSafeToInterrupt(tempJob, upgradePercentage);
        }
        
        public static bool IsLowPriorityWork(Pawn pawn) => JobHelper.IsLowPriorityWork(pawn);

        /// <summary>
        /// Calculate ranged weapon DPS
        /// </summary>
        public static float GetRangedWeaponDPS(ThingDef weaponDef, ThingWithComps weapon = null)
        {
            if (weaponDef?.Verbs == null || weaponDef.Verbs.Count == 0)
                return 0f;

            var verb = weaponDef.Verbs[0];
            if (verb == null)
                return 0f;

            float damage = 0f;
            if (verb.defaultProjectile?.projectile != null)
            {
                damage = (float)verb.defaultProjectile.projectile.GetDamageAmount(weapon);
            }

            float warmup = Math.Max(0.1f, verb.warmupTime);
            float cooldown = weaponDef.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
            float burstShots = Math.Max(1, verb.burstShotCount);

            float timePerSalvo = warmup + cooldown + 0.1f;

            return (damage * burstShots) / timePerSalvo;
        }

        /// <summary>
        /// Check if weapon is explosive
        /// </summary>
        public static bool IsExplosiveWeapon(ThingDef weaponDef)
        {
            if (weaponDef?.Verbs == null || weaponDef.Verbs.Count == 0)
                return false;

            var verb = weaponDef.Verbs[0];
            if (verb?.defaultProjectile?.projectile == null)
                return false;

            return verb.defaultProjectile.projectile.explosionRadius > 0f;
        }
        
        /// <summary>
        /// Check if weapon is EMP
        /// </summary>
        public static bool IsEMPWeapon(ThingDef weaponDef)
        {
            if (weaponDef?.Verbs == null || weaponDef.Verbs.Count == 0)
                return false;

            var verb = weaponDef.Verbs[0];
            if (verb?.defaultProjectile?.projectile == null)
                return false;

            // Check for EMP damage type
            if (verb.defaultProjectile.projectile.damageDef?.defName == "EMP")
                return true;
                
            // Check for EMP in projectile name
            if (verb.defaultProjectile.defName.ToLower().Contains("emp"))
                return true;
                
            return false;
        }

        /// <summary>
        /// Get weapon rejection reason
        /// </summary>
        public static string GetWeaponRejectionReason(Pawn pawn, ThingWithComps weapon)
        {
            string reason;

            if (!ValidationHelper.IsValidPawn(pawn, out reason))
                return $"Pawn: {reason}";

            if (!ValidationHelper.IsValidWeapon(weapon, pawn, out reason))
                return $"Weapon: {reason}";

            if (!ValidationHelper.CanPawnUseWeapon(pawn, weapon, out reason))
                return $"Compatibility: {reason}";

            return "Unknown reason";
        }

        /// <summary>
        /// Cleanup log cooldowns - now delegates to TimingHelper
        /// </summary>
        public static void CleanupLogCooldowns()
        {
            // TimingHelper handles all cooldown cleanup now (fixes #11, #13)
            TimingHelper.CleanupOldCooldowns();
        }
    }
}

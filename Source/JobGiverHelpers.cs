using RimWorld;
using System;
using System.Linq;
using Verse;

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

            // Quest lodgers are ALWAYS temporary (Royalty DLC)
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

            // Has quest tags - but check what kind
            if (pawn.questTags != null && pawn.questTags.Count > 0)
            {
                // Check for specific temporary quest patterns
                foreach (var tag in pawn.questTags)
                {
                    // These patterns indicate temporary colonists
                    if (tag.Contains("Lodger") ||
                        tag.Contains("Temporary") ||
                        tag.Contains("Visitor") ||
                        tag.Contains("Guest") ||
                        tag.Contains("Shuttle") ||
                        tag.Contains("Helper") ||      // Imperial helpers
                        tag.Contains("OnDuty") ||       // Military guests on duty
                        tag.Contains("Defender") ||     // Stellic defenders/wardens
                        tag.Contains("Wardens") ||
                        tag.Contains("OnLoan") ||       // Pawns on loan
                        tag.Contains("Lend") ||         // Pawn lend quests
                        tag.Contains("Borrowed"))
                    {
                        AutoArmDebug.LogPawn(pawn, $"Has temporary quest tag: {tag}");
                        return true;
                    }
                }

                // Check if they're part of a "lending" quest
                var activeQuests = Find.QuestManager.QuestsListForReading
                    .Where(q => !q.Historical && q.State == QuestState.Ongoing);

                foreach (var quest in activeQuests)
                {
                    // Check if it's a temporary colonist quest
                    if (quest.root?.defName?.Contains("Hospitality") == true ||
                        quest.root?.defName?.Contains("Lodgers") == true ||
                        quest.root?.defName?.Contains("Helpers") == true ||
                        quest.root?.defName?.Contains("PawnLend") == true ||
                        quest.root?.defName?.Contains("ShuttleCrash_Rescue") == true ||
                        (quest.root?.defName?.Contains("RefugeePodCrash") == true && quest.name.Contains("depart")) ||
                        (quest.root?.defName?.Contains("RefugeeBetrayal") == true))
                    {
                        // Check if this pawn is part of the quest
                        var questPawns = quest.QuestLookTargets
                            .Where(t => t.Thing is Pawn)
                            .Select(t => t.Thing as Pawn);

                        if (questPawns.Contains(pawn))
                        {
                            AutoArmDebug.LogPawn(pawn, $"Is part of temporary quest: {quest.name}");
                            return true;
                        }
                    }
                }

                // If they came from a ritual or permanent recruitment quest, they're permanent
                if (pawn.questTags.Any(tag =>
                    tag.Contains("RitualReward") ||
                    tag.Contains("JoinPermanent") ||
                    tag.Contains("WandererJoins") ||
                    tag.Contains("RefugeeJoins") ||
                    tag.Contains("AcceptJoiner") ||
                    tag.Contains("Ambassador") ||     // Hospitality mod ambassadors
                    tag.Contains("BeggarsJoin") ||    // Beggars who join permanently
                    tag.Contains("QuestReward") && !tag.Contains("Temporary"))) // Quest rewards that aren't temporary
                {
                    AutoArmDebug.LogPawn(pawn, "Has permanent joiner quest tag - treating as permanent");
                    return false; // These are permanent joiners
                }

                // If they just have generic quest tags (like "Quest3.pawn") and have been here a while
                // with work and a bed assigned, they're probably permanent rewards
                if (pawn.workSettings?.EverWork == true &&
                    pawn.ownership?.OwnedBed != null &&
                    pawn.ownership.OwnedBed.Map == pawn.Map)
                {
                    AutoArmDebug.LogPawn(pawn, "Has quest tags but is integrated (work + bed) - treating as permanent");
                    return false;
                }

                // Log that we found quest tags but treating as permanent by default
                AutoArmDebug.LogPawn(pawn, $"Has quest tags ({string.Join(", ", pawn.questTags)}) but no temporary indicators - treating as permanent");
                return false; // Default to permanent for unknown quest types
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

            // For critical jobs, require higher improvement threshold
            if (IsCriticalJobDef(jobDef))
            {
                return upgradePercentage >= 1.20f; // 20% improvement required
            }

            // For hauling and similar jobs, require moderate improvement
            if (jobDef == JobDefOf.HaulToCell ||
                jobDef == JobDefOf.CarryDownedPawnToExit ||
                jobDef == JobDefOf.Rescue ||
                jobDef == JobDefOf.TendPatient)
            {
                return upgradePercentage >= 1.15f; // 15% improvement required
            }

            // For regular work, allow smaller improvements
            return upgradePercentage >= 1.10f; // 10% improvement required
        }

        private static bool IsCriticalJobDef(JobDef jobDef)
        {
            return jobDef == JobDefOf.ExtinguishSelf ||
                   jobDef == JobDefOf.FleeAndCower ||
                   jobDef == JobDefOf.Vomit ||
                   jobDef == JobDefOf.Wait_Downed ||
                   jobDef == JobDefOf.GotoSafeTemperature ||
                   jobDef == JobDefOf.BeatFire;
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
            if (verb.defaultProjectile.defName.IndexOf("emp", StringComparison.OrdinalIgnoreCase) >= 0)
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
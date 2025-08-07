// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Shared job generation utilities and pawn validation
// Common functions used by multiple job generators

using AutoArm.Helpers;
using AutoArm.Logging;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Jobs
{
    /// <summary>
    /// Helper methods for job givers - now uses consolidated helpers
    /// </summary>
    public static class JobGiverHelpers
    {
        // Quest tag patterns that indicate temporary colonists
        private static readonly HashSet<string> TemporaryQuestTags = new HashSet<string>
        {
            "Lodger", "Temporary", "Visitor", "Guest", "Shuttle", "Helper",
            "OnDuty", "Defender", "Wardens", "OnLoan", "Lend", "Borrowed"
        };

        // Quest tag patterns that indicate permanent joiners
        private static readonly HashSet<string> PermanentQuestTags = new HashSet<string>
        {
            "RitualReward", "JoinPermanent", "WandererJoins", "RefugeeJoins",
            "AcceptJoiner", "Ambassador", "BeggarsJoin"
        };

        // Quest def names that indicate temporary colonists
        private static readonly HashSet<string> TemporaryQuestDefs = new HashSet<string>
        {
            "Hospitality", "Lodgers", "Helpers", "PawnLend", "ShuttleCrash_Rescue", "RefugeeBetrayal"
        };

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
                        // Quest lodger - expected
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
                    // Check temporary patterns
                    foreach (var pattern in TemporaryQuestTags)
                    {
                        if (tag.Contains(pattern))
                        {
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"{pawn.LabelShort}: Temporary quest tag '{tag}'");
                            }
                            return true;
                        }
                    }
                }

                // Check if they're part of a "lending" quest
                var activeQuests = Find.QuestManager.QuestsListForReading
                    .Where(q => !q.Historical && q.State == QuestState.Ongoing);

                foreach (var quest in activeQuests)
                {
                    // Check if it's a temporary colonist quest
                    foreach (var pattern in TemporaryQuestDefs)
                    {
                        if (quest.root?.defName?.Contains(pattern) == true)
                        {
                            // Check if this pawn is part of the quest
                            var questPawns = quest.QuestLookTargets
                                .Where(t => t.Thing is Pawn)
                                .Select(t => t.Thing as Pawn);

                            if (questPawns.Contains(pawn))
                            {
                                // Part of temporary quest
                                return true;
                            }
                            break;
                        }
                    }

                    // Special case for refugee departure
                    if (quest.root?.defName?.Contains("RefugeePodCrash") == true && quest.name.Contains("depart"))
                    {
                        // Check if this pawn is part of the quest
                        var questPawns = quest.QuestLookTargets
                            .Where(t => t.Thing is Pawn)
                            .Select(t => t.Thing as Pawn);

                        if (questPawns.Contains(pawn))
                        {
                            // Refugee departure quest
                            return true;
                        }
                    }
                }

                // If they came from a ritual or permanent recruitment quest, they're permanent
                bool hasPermanentTag = false;
                foreach (var tag in pawn.questTags)
                {
                    foreach (var pattern in PermanentQuestTags)
                    {
                        if (tag.Contains(pattern))
                        {
                            hasPermanentTag = true;
                            break;
                        }
                    }
                    if (hasPermanentTag) break;

                    // Special case: QuestReward without Temporary
                    if (tag.Contains("QuestReward") && !tag.Contains("Temporary"))
                    {
                        hasPermanentTag = true;
                        break;
                    }
                }

                if (hasPermanentTag)
                {
                    // Permanent joiner
                    return false;
                }

                // If they just have generic quest tags (like "Quest3.pawn") and have been here a while
                // with work and a bed assigned, they're probably permanent rewards
                if (pawn.workSettings?.EverWork == true &&
                    pawn.ownership?.OwnedBed != null &&
                    pawn.ownership.OwnedBed.Map == pawn.Map)
                {
                    // Integrated colonist with work + bed
                    return false;
                }

                // Log that we found quest tags but treating as permanent by default
                // Quest tags but no temporary indicators
                return false; // Default to permanent for unknown quest types
            }

            // Borrowed by another faction
            if (pawn.IsBorrowedByAnyFaction())
            {
                // Borrowed by faction
                return true;
            }

            // Guest from another faction
            if (pawn.guest != null && pawn.guest.HostFaction == Faction.OfPlayer &&
                pawn.Faction != Faction.OfPlayer)
            {
                // Guest from another faction
                return true;
            }

            // On loan to another faction
            if (pawn.Faction == Faction.OfPlayer && pawn.HostFaction != null &&
                pawn.HostFaction != Faction.OfPlayer)
            {
                // On loan to another faction
                return true;
            }

            return false;
        }

        // Note: Callers should use ValidationHelper and JobHelper directly instead of these delegations

        // Keeping for backward compatibility - delegates to ValidationHelper
        public static bool SafeIsColonist(Pawn pawn) => ValidationHelper.SafeIsColonist(pawn);

        /// <summary>
        /// Check if a pawn is valid for auto-equip (wrapper for compatibility)
        /// </summary>
        public static bool IsValidPawnForAutoEquip(Pawn pawn)
        {
            string reason;
            return ValidationHelper.IsValidPawn(pawn, out reason);
        }

        /// <summary>
        /// Check if a pawn is valid for auto-equip with reason
        /// </summary>
        public static bool IsValidPawnForAutoEquip(Pawn pawn, out string reason)
        {
            return ValidationHelper.IsValidPawn(pawn, out reason);
        }

        /// <summary>
        /// Check if a weapon is a valid candidate (wrapper for compatibility)
        /// </summary>
        public static bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn)
        {
            string reason;
            return ValidationHelper.IsValidWeapon(weapon, pawn, out reason);
        }

        /// <summary>
        /// Check if a weapon is a valid candidate with reason
        /// </summary>
        public static bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            return ValidationHelper.IsValidWeapon(weapon, pawn, out reason);
        }

        /// <summary>
        /// Check if a pawn is in a critical job (wrapper for compatibility)
        /// </summary>
        public static bool IsCriticalJob(Pawn pawn, bool hasNoSidearms = false)
        {
            return JobHelper.IsCriticalJob(pawn);
        }

        // Removed IsSafeToInterrupt wrappers - we rely entirely on think tree priority

        /// <summary>
        /// Check if pawn is doing low priority work (wrapper for compatibility)
        /// </summary>
        public static bool IsLowPriorityWork(Pawn pawn)
        {
            return JobHelper.IsLowPriorityWork(pawn);
        }

        /// <summary>
        /// Check if a weapon is explosive
        /// </summary>
        public static bool IsExplosiveWeapon(ThingDef weaponDef)
        {
            if (weaponDef == null || !weaponDef.IsRangedWeapon)
                return false;

            // Check verbs for explosive damage
            if (weaponDef.Verbs != null)
            {
                foreach (var verb in weaponDef.Verbs)
                {
                    if (verb.defaultProjectile?.projectile?.explosionRadius > 0)
                        return true;

                    if (verb.defaultProjectile?.projectile?.damageDef?.defName?.Contains("Bomb") == true ||
                        verb.defaultProjectile?.projectile?.damageDef?.defName?.Contains("Explosion") == true)
                        return true;
                }
            }

            // Check weapon name patterns
            var defName = weaponDef.defName.ToLower();
            var label = weaponDef.label?.ToLower() ?? "";

            if (defName.Contains("grenade") || label.Contains("grenade") ||
                defName.Contains("rocket") || label.Contains("rocket") ||
                defName.Contains("launcher") || label.Contains("launcher") ||
                defName.Contains("explosive") || label.Contains("explosive") ||
                defName.Contains("bomb") || label.Contains("bomb") ||
                defName.Contains("molotov") || label.Contains("molotov") ||
                defName.Contains("emp") || label.Contains("emp") ||
                defName.Contains("frag") || label.Contains("frag"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clean up log cooldowns globally
        /// </summary>
        public static void CleanupLogCooldowns()
        {
            // This is handled by TimingHelper
            TimingHelper.CleanupOldCooldowns();
        }

        /// <summary>
        /// Check if pawn is currently doing any hauling-related job
        /// </summary>
        public static bool IsHaulingJob(Pawn pawn)
        {
            if (pawn?.CurJob == null)
                return false;

            var jobDef = pawn.CurJob.def;
            var jobDefName = jobDef?.defName;

            return jobDef == JobDefOf.HaulToCell ||
                   jobDef == JobDefOf.HaulToContainer ||
                   jobDefName?.Contains("Haul") == true ||
                   // Pick Up And Haul specific jobs
                   jobDefName == "HaulToInventory" ||
                   jobDefName == "UnloadYourHauledInventory" ||
                   // Other inventory-related jobs
                   jobDefName?.Contains("Inventory") == true ||
                   jobDefName == "UnloadYourInventory" ||
                   jobDefName == "TakeToInventory";
        }
    }
}

using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    /// <summary>
    /// Job helpers
    /// </summary>
    public static class Jobs
    {

        private static readonly HashSet<string> TemporaryQuestTags = new HashSet<string>
        {
            "Lodger", "Temporary", "Visitor", "Guest",
            "Shuttle", "ShuttleDown", "ShuttleCrash",
            "Helper", "OnDuty", "Defender", "Wardens",
            "OnLoan", "Lend", "Borrowed",
            "Escort", "Protection", "Guard"
        };

        private static readonly HashSet<string> PermanentQuestTags = new HashSet<string>
        {
            "RitualReward", "JoinPermanent", "WandererJoins", "RefugeeJoins",
            "AcceptJoiner", "Ambassador", "BeggarsJoin"
        };

        private static readonly HashSet<string> TemporaryQuestDefs = new HashSet<string>
        {
            "Hospitality", "Lodgers", "Helpers", "PawnLend", "ShuttleCrash_Rescue", "RefugeeBetrayal"
        };

        private static readonly HashSet<int> loggedGenericTagsPawns = new HashSet<int>();

        /// <summary>
        /// Create an equip job for a weapon - uses smart swap when replacing existing weapon
        /// </summary>
        public static Job CreateEquipJob(ThingWithComps weapon, bool isSidearm = false, Pawn pawn = null)
        {
            if (weapon == null)
            {
                AutoArmLogger.Debug(() => "CreateEquipJob called with null weapon");
                return null;
            }

            if (pawn != null)
            {
                var currentPrimary = pawn.equipment?.Primary;

                if (currentPrimary != null && currentPrimary.def == weapon.def)
                {
                    AutoArmLogger.Debug(() => $"Creating primary swap job for upgrade: {currentPrimary.Label} -> {weapon.Label}");
                    return CreateSwapPrimaryJob(pawn, weapon, currentPrimary);
                }

                if (SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed &&
                    pawn.inventory?.innerContainer != null)
                {
                    ThingWithComps existingSidearm = null;
                    foreach (var thing in pawn.inventory.innerContainer)
                    {
                        if (thing is ThingWithComps comp && comp.def == weapon.def && comp.def.IsWeapon)
                        {
                            existingSidearm = comp;
                            break;
                        }
                    }

                    if (existingSidearm != null)
                    {
                        AutoArmLogger.Debug(() => $"Creating sidearm swap job for upgrade: {existingSidearm.Label} -> {weapon.Label}");
                        return CreateSwapSidearmJob(pawn, weapon, existingSidearm);
                    }
                }
            }

            if (isSidearm && AutoArmDefOf.EquipSecondary != null)
            {
                AutoArmLogger.Debug(() => $"Creating EquipSecondary job for {weapon.Label} (new sidearm)");
                return JobMaker.MakeJob(AutoArmDefOf.EquipSecondary, weapon);
            }

            var job = JobMaker.MakeJob(JobDefOf.Equip, weapon);
            job.count = 1;
            return job;
        }


        private static Job CreateSwapPrimaryJob(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            Job job = JobMaker.MakeJob(AutoArmDefOf.AutoArmSwapPrimary, newWeapon, oldWeapon);
            job.count = 1;
            return job;
        }


        private static Job CreateSwapSidearmJob(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            Job job = JobMaker.MakeJob(AutoArmDefOf.AutoArmSwapSidearm, newWeapon, oldWeapon);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// Temp colonist
        /// Check temp before fast
        /// Prevent temp misclass
        /// </summary>
        public static bool IsTemporary(Pawn pawn)
        {
            if (pawn == null || !pawn.IsColonist)
                return false;

            var playerFaction = Find.FactionManager?.OfPlayer;

            if (ModsConfig.RoyaltyActive)
            {
                if (pawn.IsQuestLodger())
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Quest lodger (temporary)");
                    return true;
                }
            }

            if (playerFaction != null && pawn.HomeFaction != null && pawn.HomeFaction != playerFaction)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: DIFFERENT HOME FACTION - Faction={pawn.Faction?.Name ?? "null"}, HomeFaction={pawn.HomeFaction.Name} (treating as temporary)");
                return true;
            }

            if (pawn.questTags != null && pawn.questTags.Count > 0)
            {
                for (int i = 0; i < pawn.questTags.Count; i++)
                {
                    var tag = pawn.questTags[i];
                    if (string.IsNullOrEmpty(tag)) continue;

                    foreach (var pattern in TemporaryQuestTags)
                    {
                        if (tag.Contains(pattern))
                        {
                            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Temporary quest tag '{tag}'");
                            return true;
                        }
                    }
                }
            }

            if (IsInActiveTemporaryQuest(pawn))
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Part of active temporary quest");
                return true;
            }

            if (playerFaction != null && pawn.Faction == playerFaction)
            {
                var hostFaction = pawn.guest != null ? pawn.guest.HostFaction : null;
                bool notHostedOrHostedByPlayer = hostFaction == null || hostFaction == playerFaction;

                if (notHostedOrHostedByPlayer)
                {
                    if (HasOnlyGenericQuestTags(pawn))
                    {
                        if (AutoArmMod.settings?.debugLogging == true && !loggedGenericTagsPawns.Contains(pawn.thingIDNumber))
                        {
                            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Generic tags only, treating as permanent");
                            loggedGenericTagsPawns.Add(pawn.thingIDNumber);
                        }
                        return false;
                    }
                }
            }

            if (pawn.questTags != null && pawn.questTags.Count > 0)
            {
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

                    if (tag.Contains("QuestReward") && !tag.Contains("Temporary"))
                    {
                        hasPermanentTag = true;
                        break;
                    }
                }

                if (hasPermanentTag)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Permanent quest tag found");
                    return false;
                }

                if (pawn.equipment?.Primary != null &&
                    Caching.Components.IsBiocodedTo(pawn.equipment.Primary, pawn))
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Has biocoded weapon (former quest lodger with locked equipment)");
                    return true;
                }

                if (pawn.workSettings?.EverWork == true &&
                    pawn.ownership?.OwnedBed != null &&
                    pawn.ownership.OwnedBed.Map == pawn.Map)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Integrated colonist (work + bed assigned)");
                    return false;
                }
            }


            if (pawn.IsBorrowedByAnyFaction())
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Borrowed by another faction");
                return true;
            }

            if (playerFaction != null && pawn.guest != null && pawn.guest.HostFaction == playerFaction &&
                pawn.Faction != playerFaction)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Guest from another faction");
                return true;
            }

            if (playerFaction != null && pawn.Faction == playerFaction && pawn.HostFaction != null &&
                pawn.HostFaction != playerFaction)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: On loan to another faction");
                return true;
            }

            if (pawn.questTags != null && pawn.questTags.Count > 0)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Unknown quest tags, defaulting to temporary (safe): {string.Join(", ", pawn.questTags)}");
                }
                return true;
            }

            return false;
        }


        private static bool IsInActiveTemporaryQuest(Pawn pawn)
        {
            if (pawn.questTags == null || pawn.questTags.Count == 0)
                return false;

            var activeQuests = Find.QuestManager.QuestsListForReading
                .Where(q => !q.Historical && q.State == QuestState.Ongoing);

            foreach (var quest in activeQuests)
            {
                foreach (var pattern in TemporaryQuestDefs)
                {
                    if (quest.root?.defName?.Contains(pattern) == true)
                    {
                        var questPawns = quest.QuestLookTargets
                            .Where(t => t.Thing is Pawn)
                            .Select(t => t.Thing as Pawn);

                        if (questPawns.Contains(pawn))
                        {
                            return true;
                        }
                        break;
                    }
                }

                if (quest.root?.defName?.Contains("RefugeePodCrash") == true && quest.name.Contains("depart"))
                {
                    var questPawns = quest.QuestLookTargets
                        .Where(t => t.Thing is Pawn)
                        .Select(t => t.Thing as Pawn);

                    if (questPawns.Contains(pawn))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static readonly string[] _questDenylist = { "lodger", "temporary", "guest", "borrowed", "shuttle" };


        private static bool HasOnlyGenericQuestTags(Pawn pawn)
        {
            var tags = pawn.questTags;
            if (tags == null || tags.Count == 0)
                return false;

            bool onlyGeneric = true;
            for (int i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                if (string.IsNullOrEmpty(t)) continue;
                var lower = t.ToLowerInvariant();

                foreach (var denyWord in _questDenylist)
                {
                    if (lower.Contains(denyWord))
                        return false;
                }

                if (!(lower.Contains("quest") && lower.Contains("pawn")))
                {
                    onlyGeneric = false;
                    break;
                }
            }
            return onlyGeneric;
        }


        public static bool SafeIsColonist(Pawn pawn) => ValidationHelper.SafeIsColonist(pawn);

        /// <summary>
        /// Valid auto-equip
        /// </summary>
        public static bool IsValidPawn(Pawn pawn)
        {
            string reason;
            return ValidationHelper.IsValidPawn(pawn, out reason);
        }

        /// <summary>
        /// Valid auto-equip reason
        /// </summary>
        public static bool IsValidPawn(Pawn pawn, out string reason)
        {
            return ValidationHelper.IsValidPawn(pawn, out reason);
        }

        /// <summary>
        /// Valid candidate
        /// Testing only
        /// </summary>
        public static bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn)
        {
            string reason;
            return ValidationHelper.IsValidWeapon(weapon, pawn, out reason);
        }

        /// <summary>
        /// Valid candidate reason
        /// Testing only
        /// </summary>
        public static bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            return ValidationHelper.IsValidWeapon(weapon, pawn, out reason);
        }

        /// <summary>
        /// Hauling job
        /// </summary>
        public static bool IsHaulingJob(Pawn pawn)
        {
            if (pawn?.CurJob == null)
                return false;

            var jobDef = pawn.CurJob.def;
            var jobDefName = jobDef?.defName;

            return jobDef == JobDefOf.HaulToCell ||
                   jobDef == JobDefOf.HaulToContainer ||
                   jobDef == AutoArmDefOf.HaulToInventory ||
                   jobDef == AutoArmDefOf.UnloadYourHauledInventory ||
                   jobDef == AutoArmDefOf.UnloadYourInventory ||
                   jobDefName?.Contains("Haul") == true ||
                   jobDefName?.Contains("Inventory") == true;
        }
    }
}

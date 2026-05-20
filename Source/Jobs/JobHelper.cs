
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    internal static class JobHelper
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
                    QualityCategory existingQuality = QualityCategory.Normal;
                    QualityCategory newQuality = QualityCategory.Normal;
                    Caching.Components.TryGetWeaponQuality(currentPrimary, out existingQuality);
                    Caching.Components.TryGetWeaponQuality(weapon, out newQuality);

                    if (newQuality > existingQuality)
                    {
                        AutoArmLogger.Debug(() => $"Quality upgrade for primary: from {currentPrimary.Label} ({existingQuality}) to {weapon.Label} ({newQuality})");
                        return CreateSwapPrimaryJob(weapon, currentPrimary);
                    }

                    if (newQuality < existingQuality)
                    {
                        AutoArmLogger.Debug(() => $"Skipping primary swap - lower quality: {currentPrimary.Label} ({existingQuality}) vs {weapon.Label} ({newQuality})");
                        return null;
                    }

                    float existingScore = Scoring.GetTotalScore(pawn, currentPrimary);
                    float newScore = Scoring.GetTotalScore(pawn, weapon);

                    if (newScore - existingScore > Constants.ScoreEpsilon)
                    {
                        AutoArmLogger.Debug(() => $"Primary upgrade: from {currentPrimary.Label} ({existingScore:F1}) to {weapon.Label} ({newScore:F1})");
                        return CreateSwapPrimaryJob(weapon, currentPrimary);
                    }

                    AutoArmLogger.Debug(() => $"Skipping primary swap - no improvement: {currentPrimary.Label} ({existingScore:F1}) vs {weapon.Label} ({newScore:F1})");
                    return null;
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
                        if (ForcedWeapons.IsForced(pawn, existingSidearm) &&
                            AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                        {
                            AutoArmLogger.Debug(() => $"Sidearm swap skipped - existing {existingSidearm.Label} is forced and upgrades are disabled");
                            return null;
                        }

                        QualityCategory existingQuality = QualityCategory.Normal;
                        QualityCategory newQuality = QualityCategory.Normal;
                        Caching.Components.TryGetWeaponQuality(existingSidearm, out existingQuality);
                        Caching.Components.TryGetWeaponQuality(weapon, out newQuality);

                        if (newQuality < existingQuality)
                        {
                            AutoArmLogger.Debug(() => $"Skipping sidearm swap - lower quality: {existingSidearm.Label} ({existingQuality}) vs {weapon.Label} ({newQuality})");
                            return null;
                        }

                        bool isQualityUpgrade = newQuality > existingQuality;

                        if (!isQualityUpgrade)
                        {
                            float existingScore = Scoring.GetTotalScore(pawn, existingSidearm);
                            float newScore = Scoring.GetTotalScore(pawn, weapon);

                            if (newScore - existingScore <= Constants.ScoreEpsilon)
                            {
                                AutoArmLogger.Debug(() => $"Skipping sidearm swap - no improvement: {existingSidearm.Label} ({existingScore:F1}) vs {weapon.Label} ({newScore:F1})");
                                return null;
                            }
                        }

                        string swapReason;
                        if (!SimpleSidearmsCompat.CanUseSidearmForSwap(weapon, existingSidearm, pawn, out swapReason))
                        {
                            AutoArmLogger.Debug(() => $"Sidearm swap rejected by SS: {swapReason}");
                            return null;
                        }
                        AutoArmLogger.Debug(() => $"Sidearm swap: from {existingSidearm.Label} ({existingQuality}) to {weapon.Label} ({newQuality})");
                        return CreateSwapSidearmJob(weapon, existingSidearm);
                    }
                }
            }

            if (pawn != null && pawn.equipment?.Primary != null && !isSidearm)
            {
                var oldPrimary = pawn.equipment.Primary;

                if (SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed)
                {
                    bool isCrossType = weapon.def.IsRangedWeapon != oldPrimary.def.IsRangedWeapon;
                    if (isCrossType)
                    {
                        string ssReason;
                        if (!SimpleSidearmsCompat.CanPickupSidearm(weapon, pawn, out ssReason))
                        {
                            var sameTypeSwap = TryFindSameTypeSidearmSwap(pawn, weapon);
                            if (sameTypeSwap != null)
                                return sameTypeSwap;

                            string r = ssReason;
                            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Cross-type primary swap blocked by SS ({r}); routing {weapon.Label} to sidearm logic");
                            var fallback = SimpleSidearmsCompat.TryGetWeaponJob(pawn, weapon, bypassCooldown: true);
                            if (fallback == null)
                                JobGiver_PickUpBetterWeapon.RecordFailedJob(pawn, weapon);
                            return fallback;
                        }
                    }
                }

                AutoArmLogger.Debug(() => $"Cross-def primary swap: from {oldPrimary.Label} to {weapon.Label}");
                return CreateSwapPrimaryJob(weapon, oldPrimary);
            }

            if (isSidearm && AutoArmDefOf.EquipSecondary != null)
            {
                AutoArmLogger.Debug(() => $"Creating EquipSecondary job for {weapon.Label} (new sidearm)");
                return MakeJob(AutoArmDefOf.EquipSecondary, weapon);
            }

            return MakeJob(JobDefOf.Equip, weapon);
        }

        private static Job MakeJob(JobDef def, ThingWithComps target, ThingWithComps swapOld = null)
        {
            Job job = swapOld != null
                ? JobMaker.MakeJob(def, target, swapOld)
                : JobMaker.MakeJob(def, target);
            job.count = 1;
            return job;
        }

        private static Job CreateSwapPrimaryJob(ThingWithComps newWeapon, ThingWithComps oldWeapon)
            => MakeJob(AutoArmDefOf.AutoArmSwapPrimary, newWeapon, oldWeapon);

        private static Job CreateSwapSidearmJob(ThingWithComps newWeapon, ThingWithComps oldWeapon)
            => MakeJob(AutoArmDefOf.AutoArmSwapSidearm, newWeapon, oldWeapon);

        private static Job TryFindSameTypeSidearmSwap(Pawn pawn, ThingWithComps weapon)
        {
            var inventory = pawn.inventory?.innerContainer;
            if (inventory == null) return null;

            bool weaponIsMelee = weapon.def.IsMeleeWeapon;
            ThingWithComps worstSameType = null;
            float worstScore = float.MaxValue;

            foreach (var thing in inventory)
            {
                if (!(thing is ThingWithComps comp) || !comp.def.IsWeapon) continue;
                if (comp.def.IsMeleeWeapon != weaponIsMelee) continue;
                if (ForcedWeapons.IsForced(pawn, comp))
                {
                    if (AutoArmMod.settings?.allowForcedWeaponUpgrades != true) continue;
                    if (comp.def != weapon.def) continue;
                }

                float score = Scoring.GetTotalScore(pawn, comp);
                if (score < worstScore)
                {
                    worstSameType = comp;
                    worstScore = score;
                }
            }

            if (worstSameType == null) return null;

            float newScore = Scoring.GetTotalScore(pawn, weapon);
            if (newScore - worstScore <= Constants.ScoreEpsilon)
            {
                AutoArmLogger.Debug(() => $"Same-type sidearm swap skipped - no improvement: {worstSameType.Label} ({worstScore:F1}) vs {weapon.Label} ({newScore:F1})");
                return null;
            }

            string swapReason;
            if (!SimpleSidearmsCompat.CanUseSidearmForSwap(weapon, worstSameType, pawn, out swapReason))
            {
                AutoArmLogger.Debug(() => $"Same-type sidearm swap rejected by SS: {swapReason}");
                return null;
            }

            AutoArmLogger.Debug(() => $"Cross-type primary blocked; routing through sidearm swap from {worstSameType.Label} ({worstScore:F1}) to {weapon.Label} ({newScore:F1})");
            return CreateSwapSidearmJob(weapon, worstSameType);
        }

        public static bool IsTemporary(Pawn pawn)
        {
            if (pawn == null || !pawn.IsColonist)
                return false;

            var playerFaction = Faction.OfPlayerSilentFail;

            if (ModsConfig.RoyaltyActive)
            {
                if (pawn.IsQuestLodger())
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}]: Quest lodger (temporary)");
                    return true;
                }
            }

            if (playerFaction != null && pawn.HomeFaction != null && pawn.HomeFaction != playerFaction && !pawn.IsSlaveOfColony)
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
                            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Temporary quest tag '{tag}'");
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
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] On loan to another faction");
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

            var quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (quest.Historical || quest.State != QuestState.Ongoing)
                    continue;

                var defName = quest.root?.defName;
                if (defName == null)
                    continue;

                bool isTemporaryQuest = TemporaryQuestDefs.Contains(defName);

                if (!isTemporaryQuest && defName.Contains("RefugeePodCrash") && quest.name.Contains("depart"))
                {
                    isTemporaryQuest = true;
                }

                if (isTemporaryQuest && QuestContainsPawn(quest, pawn))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool QuestContainsPawn(Quest quest, Pawn pawn)
        {
            foreach (var target in quest.QuestLookTargets)
            {
                if (target.Thing == pawn)
                    return true;
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


    }
}

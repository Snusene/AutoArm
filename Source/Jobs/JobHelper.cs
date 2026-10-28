
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    internal static class JobHelper
    {
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

                if (SimpleSidearmsCompat.IsManagingPawn(pawn))
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
            if (playerFaction == null)
                return false;

            if (pawn.HomeFaction != playerFaction && !pawn.IsSlaveOfColony)
                return true;

            if (pawn.HostFaction != null && pawn.HostFaction != playerFaction)
                return true;

            return pawn.IsBorrowedByAnyFaction();
        }
    }
}

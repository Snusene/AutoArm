
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace AutoArm.Helpers
{
    public static class ValidationHelper
    {
        private static readonly System.Collections.Generic.Dictionary<System.Type, bool> ritualLordTypeCache = new System.Collections.Generic.Dictionary<System.Type, bool>();

        private static readonly System.Collections.Generic.Dictionary<JobDef, bool> ritualJobDefCache = new System.Collections.Generic.Dictionary<JobDef, bool>();
        private static readonly System.Collections.Generic.Dictionary<JobDef, bool> haulingJobDefCache = new System.Collections.Generic.Dictionary<JobDef, bool>();

        private const int MaxRitualLordTypeCacheSize = 500;

        private const int MaxJobDefCacheSize = 1000;

        private static readonly System.Collections.Generic.HashSet<string> NegativeIdeologyDispositions =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "Despised",
                "Disapproved",
                "Disliked",
                "Forbidden",
                "Prohibited",
                "Abhorrent",
                "Reviled"
            };


        private static System.Reflection.FieldInfo FindFieldWithFallback(Type type, params string[] fieldNames)
        {
            foreach (var name in fieldNames)
            {
                var field = AccessTools.Field(type, name);
                if (field != null) return field;
            }
            return null;
        }


        internal static bool IsHaulingOrInventoryJob(JobDef jobDef)
        {
            if (jobDef == null) return false;

            if (haulingJobDefCache.TryGetValue(jobDef, out var cached))
                return cached;

            bool isHaul = false;

            var dc = jobDef.driverClass;
            if (dc != null)
            {
                if (typeof(JobDriver_HaulToCell).IsAssignableFrom(dc) ||
                    typeof(JobDriver_HaulToContainer).IsAssignableFrom(dc) ||
                    typeof(JobDriver_HaulToTransporter).IsAssignableFrom(dc))
                {
                    isHaul = true;
                }
            }

            if (!isHaul)
            {
                if (jobDef == AutoArmDefOf.HaulToInventory ||
                    jobDef == AutoArmDefOf.UnloadYourHauledInventory ||
                    jobDef == AutoArmDefOf.UnloadYourInventory)
                {
                    isHaul = true;
                }
            }

            if (!isHaul && jobDef.defName != null)
            {
                var name = jobDef.defName;
                if (name.IndexOf("Haul", System.StringComparison.Ordinal) >= 0 ||
                    name.IndexOf("Carry", System.StringComparison.Ordinal) >= 0 ||
                    name.IndexOf("Inventory", System.StringComparison.Ordinal) >= 0)
                {
                    isHaul = true;
                }
            }

            if (haulingJobDefCache.Count >= MaxJobDefCacheSize)
            {
                AutoArmLogger.Debug(() => $"Hauling job cache exceeded {MaxJobDefCacheSize} entries, clearing");
                haulingJobDefCache.Clear();
            }

            haulingJobDefCache[jobDef] = isHaul;
            return isHaul;
        }

        /// <summary>
        /// Ideology despised
        /// </summary>
        public static bool IsDespisedByIdeology(ThingWithComps weapon, Pawn pawn)
        {
            string ideologyReason;
            IdeoWeaponDisposition? disposition;
            return TryGetIdeologyWeaponBlock(weapon, pawn, out ideologyReason, out disposition)
                && disposition.HasValue && disposition.Value == IdeoWeaponDisposition.Despised;
        }

        /// <summary>
        /// Ideology blocks
        /// </summary>
        public static bool TryGetIdeologyWeaponBlock(ThingWithComps weapon, Pawn pawn, out string reason, out IdeoWeaponDisposition? disposition)
        {
            reason = null;
            disposition = null;

            if (weapon?.def == null || pawn == null)
                return false;
            if (!ModsConfig.IdeologyActive)
                return false;

            var ideo = pawn.Ideo;
            if (ideo != null)
            {
                try
                {
                    var weaponDisposition = ideo.GetDispositionForWeapon(weapon.def);
                    disposition = weaponDisposition;

                    if (weaponDisposition == IdeoWeaponDisposition.Despised)
                    {
                        reason = "Despised by ideology";
                        return true;
                    }

                    var dispositionName = weaponDisposition.ToString();
                    if (NegativeIdeologyDispositions.Contains(dispositionName))
                    {
                        reason = "Forbidden by ideology";
                        return true;
                    }
                }
                catch (Exception ex) when (ex is MissingMethodException || ex is TypeLoadException || ex is MissingFieldException)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Ideology weapon disposition check failed: {ex.GetType().Name} - {ex.Message}");
                    disposition = null;
                }
            }

            if (IsBlockedByIdeologyRole(weapon, pawn, out var roleReason))
            {
                reason = EquipReasonHelper.Normalize(roleReason);
                if (string.IsNullOrEmpty(reason))
                {
                    reason = "Ideology role forbids";
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ideology role forbids this weapon (NoRanged, NoMelee, etc)?
        /// </summary>
        public static bool IsBlockedByIdeologyRole(Thing thing, Pawn pawn, out string reason)
        {
            reason = null;

            if (thing == null || pawn == null)
                return false;
            if (!ModsConfig.IdeologyActive)
                return false;

            var ideo = pawn.Ideo;
            var role = ideo?.GetRole(pawn);
            if (role == null)
                return false;

            try
            {
                return !role.CanEquip(pawn, thing, out reason);
            }
            catch (Exception ex) when (ex is MissingMethodException || ex is TypeLoadException || ex is MissingFieldException)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Ideology role.CanEquip check failed: {ex.GetType().Name} - {ex.Message}");
                reason = null;
                return false;
            }
        }

        private static readonly StatDef CachedMass = StatDefOf.Mass;

        /// <summary>
        /// Full weapon validation (tests & UI only; hot path uses JobGiver)
        /// </summary>
        public static bool IsValidWeapon(ThingWithComps weapon, Pawn pawn, out string reason, bool skipSimpleSidearmsCheck = false)
        {
            reason = "";

            if (weapon == null)
            {
                reason = "Weapon is null";
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Weapon validation failed for {pawn?.LabelShort ?? "null pawn"}: {reason}");
                }
                return false;
            }

            bool isUnarmed = pawn?.equipment?.Primary == null;

            if (weapon.def == null)
            {
                reason = "Weapon def is null";
                return false;
            }

            if (!WeaponValidation.IsWeapon(weapon))
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
                if (!TestRunner.IsRunningTests)
                {
                    reason = "Wrong map";
                    return false;
                }
            }

            if (!TestRunner.IsRunningTests && SimpleSidearmsCompat.IsLoaded && !skipSimpleSidearmsCheck)
            {
                bool shouldCheckSidearmRestrictions = true;

                if (pawn.equipment?.Primary == null)
                {
                    shouldCheckSidearmRestrictions = false;
                    if (AutoArmMod.settings?.debugLogging == true && isUnarmed)
                    {
                        AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] UNARMED - bypassing ALL SimpleSidearms restrictions for primary weapon pickup");
                    }
                }
                else if (pawn.equipment?.Primary != null)
                {
                    if (SimpleSidearmsCompat.ShouldTreatAsPrimaryReplacement(pawn, weapon, pawn.equipment.Primary))
                    {
                        shouldCheckSidearmRestrictions = false;
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            var currentScore = WeaponCacheManager.GetCachedScore(pawn, pawn.equipment.Primary);
                            var newScore = WeaponCacheManager.GetCachedScore(pawn, weapon);
                            float upgradeThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
                            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] {weapon.Label} would replace primary (score {newScore:F0} > {currentScore:F0} * {upgradeThreshold:F2}) - bypassing SS sidearm restrictions");
                        }
                    }
                }

                if (shouldCheckSidearmRestrictions)
                {
                    string ssReason;
                    bool allowed = SimpleSidearmsCompat.CanPickupSidearm(weapon, pawn, out ssReason);
                    if (!allowed)
                    {
                        reason = $"SimpleSidearms: {ssReason}";

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            if (pawn.equipment?.Primary != null)
                            {
                                var currentScore = WeaponCacheManager.GetCachedScore(pawn, pawn.equipment.Primary);
                                var newScore = WeaponCacheManager.GetCachedScore(pawn, weapon);
                                float upgradeThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
                                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] {weapon.Label} rejected by SS: {ssReason} (scores: {newScore:F0} vs {currentScore:F0}, needed >{currentScore * upgradeThreshold:F0})");
                            }
                            else
                            {
                                AutoArmLogger.Warn($"[{pawn.LabelShort}] UNARMED but SS restrictions still applied! {weapon.Label} rejected: {ssReason}");
                            }
                        }

                        return false;
                    }
                }
            }

            if (!TestRunner.IsRunningTests && pawn.outfits?.CurrentApparelPolicy?.filter != null && WeaponValidation.IsWeapon(weapon))
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;
                if (!filter.Allows(weapon.def))
                {
                    reason = "Not allowed by outfit";
                    return false;
                }

                if (filter.AllowedQualityLevels != QualityRange.All)
                {
                    if (weapon.TryGetQuality(out QualityCategory quality))
                    {
                        if (!filter.AllowedQualityLevels.Includes(quality))
                        {
                            reason = "Quality not allowed by outfit";
                            return false;
                        }
                    }
                }
            }

            if (!TestRunner.IsRunningTests)
            {
                string vanillaReason;
                if (!EquipmentUtility.CanEquip(weapon, pawn, out vanillaReason, false))
                {
                    reason = EquipReasonHelper.Normalize(vanillaReason);
                    if (string.IsNullOrEmpty(reason))
                    {
                        reason = "Cannot equip (vanilla restriction)";
                    }
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] EquipmentUtility.CanEquip blocked {weapon.Label}: {vanillaReason} -> {reason}");
                    return false;
                }
            }

            if (!TestRunner.IsRunningTests && weapon.IsForbidden(pawn))
            {
                reason = "Forbidden";
                if (isUnarmed && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: Forbidden (pawn is unarmed but respecting player restriction)");
                }
                return false;
            }

            if (!TestRunner.IsRunningTests)
            {
                string cantEquipReason;
                if (!EquipEligibilityCache.CanEquip(pawn, weapon, out cantEquipReason, checkBonded: true))
                {
                    reason = EquipReasonHelper.Normalize(cantEquipReason);
                    if (AutoArmMod.settings?.debugLogging == true && !string.IsNullOrEmpty(cantEquipReason))
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Cannot equip {weapon.Label}: {cantEquipReason} -> {reason}");
                    }
                    return false;
                }
            }

            if (weapon.questTags != null && weapon.questTags.Count > 0)
            {
                reason = "Quest item";
                return false;
            }

            if (!TestRunner.IsRunningTests && DroppedItemTracker.IsDropped(weapon))
            {
                reason = "Recently dropped";
                return false;
            }

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
                else if (!(weapon.ParentHolder is Map) && !StoreUtility.IsInAnyStorage(weapon))
                {
                    reason = "In non-storage container";
                    return false;
                }
            }

            if (!TestRunner.IsRunningTests && !pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, false))
            {
                reason = "Cannot reach";
                return false;
            }

            if (!TestRunner.IsRunningTests && weapon.IsBurning())
            {
                reason = "On fire";
                return false;
            }

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

            if (CECompat.ShouldCheckAmmo() && CECompat.ShouldSkipWeaponForCE(weapon, pawn))
            {
                reason = "No ammo available (Combat Extended)";
                return false;
            }

            AutoArmPerfOverlayWindow.ReportValidation(true);
            return true;
        }

        /// <summary>
        /// Full pawn validation (ThinkNode pre-validates most checks)
        /// </summary>
        public static bool IsValidPawn(Pawn pawn, out string reason, bool checkForWeapons = true, bool fromJobGiver = false)
        {
            reason = "";

            if (pawn == null)
            {
                reason = "Pawn is null";
                return false;
            }

            if (pawn.Map == null)
            {
                reason = "Map is null";
                return false;
            }

            if (!pawn.Position.IsValid || !pawn.Position.InBounds(pawn.Map))
            {
                reason = "Invalid position";
                return false;
            }

            if (!fromJobGiver)
            {
                if (pawn.RaceProps == null)
                {
                    reason = "No race properties";
                    return false;
                }

                if (pawn.RaceProps.Animal)
                {
                    reason = "Is animal";
                    return false;
                }

                if (pawn.RaceProps.IsMechanoid)
                {
                    reason = "Is mechanoid";
                    return false;
                }

                if (!pawn.RaceProps.ToolUser)
                {
                    reason = "Cannot use tools";
                    return false;
                }

                if (pawn.RaceProps.intelligence < Intelligence.ToolUser)
                {
                    reason = "Intelligence too low";
                    return false;
                }

                if (pawn.health?.capacities == null)
                {
                    reason = "No health capacities";
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

                if (pawn.Downed)
                {
                    reason = "Downed";
                    return false;
                }

                if (pawn.Drafted)
                {
                    reason = "Drafted";
                    return false;
                }

                if (pawn.InMentalState)
                {
                    reason = "In mental state";
                    return false;
                }

                if (pawn.InBed())
                {
                    reason = "In bed";
                    return false;
                }

                if (pawn.IsPrisoner)
                {
                    reason = "Is prisoner";
                    return false;
                }

                if (!SafeIsColonist(pawn))
                {
                    if (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony)
                    {
                    }
                    else
                    {
                        reason = "Not a colonist";
                        return false;
                    }
                }

                if (pawn.outfits == null)
                {
                    reason = "Temporary faction member (no outfit policy)";
                    return false;
                }

                if (ModsConfig.RoyaltyActive && pawn.IsQuestLodger())
                {
                    reason = "Quest lodger with locked equipment";
                    return false;
                }

                if (global::AutoArm.Jobs.Jobs.IsTemporary(pawn))
                {
                    if (QuestUtility.IsReservedByQuestOrQuestBeingGenerated(pawn))
                    {
                        reason = "Quest-reserved pawn";
                        return false;
                    }

                    if (!(AutoArmMod.settings?.allowTemporaryColonists ?? false))
                    {
                        reason = "Temporary colonist (quest/borrowed) - not allowed";
                        return false;
                    }
                }

                if (checkForWeapons && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    reason = "Incapable of violence";
                    return false;
                }

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

                if (IsInRitual(pawn))
                {
                    reason = "In ritual or ceremony";
                    return false;
                }

                if (pawn.CurJob != null && IsHaulingOrInventoryJob(pawn.CurJob.def))
                {
                    reason = "Currently hauling";
                    return false;
                }

                if (CaravanCompat.IsCaravanMember(pawn))
                {
                    reason = "In caravan";
                    return false;
                }


                if (ModsConfig.BiotechActive)
                {
                    if (!(AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false))
                    {
                        if (pawn.DevelopmentalStage < DevelopmentalStage.Child)
                        {
                            int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                            reason = $"Too young to equip weapons ({age} years old)";
                            return false;
                        }
                    }
                    else
                    {
                        int minAge = AutoArmMod.settings?.childrenMinAge ?? Constants.ChildDefaultMinAge;
                        int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;

                        if (minAge <= 13)
                        {
                            if (pawn.DevelopmentalStage < DevelopmentalStage.Adult && age < minAge)
                            {
                                reason = $"Too young ({age} < {minAge})";
                                return false;
                            }
                        }
                        else
                        {
                            if (age < minAge)
                            {
                                reason = $"Too young ({age} < {minAge})";
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Colonist or slave of colony?
        /// </summary>
        public static bool SafeIsColonist(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (pawn.IsColonist)
                return true;

            if (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony)
                return true;

            return false;
        }

        /// <summary>
        /// Pawn can equip this weapon?
        /// </summary>
        public static bool CanPawnUseWeapon(Pawn pawn, ThingWithComps weapon, out string reason, bool skipSimpleSidearmsCheck = false, bool fromJobGiver = false)
        {
            reason = "";

            if (!IsValidPawn(pawn, out reason, checkForWeapons: true, fromJobGiver: fromJobGiver))
            {
                AutoArmPerfOverlayWindow.ReportValidation(false);
                return false;
            }

            if (!IsValidWeapon(weapon, pawn, out reason, skipSimpleSidearmsCheck))
            {
                AutoArmPerfOverlayWindow.ReportValidation(false);
                return false;
            }

            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
            {
                reason = "Brawler won't use ranged weapon";
                return false;
            }

            if (ModsConfig.IdeologyActive)
            {
                string ideologyReason;
                IdeoWeaponDisposition? ideologyDisposition;
                if (TryGetIdeologyWeaponBlock(weapon, pawn, out ideologyReason, out ideologyDisposition))
                {
                    reason = ideologyReason ?? "Ideology forbids";
                    return false;
                }
            }

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
        /// Pawn has conceited title?
        /// </summary>
        public static bool IsConceited(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (ModsConfig.RoyaltyActive && pawn.royalty != null)
            {
                foreach (var title in pawn.royalty.AllTitlesInEffectForReading)
                {
                    if (title.conceited)
                        return true;
                }
            }


            return false;
        }

        /// <summary>
        /// Clear all validation caches (tests)
        /// </summary>
        public static void ClearCaches()
        {
        }

        /// <summary>
        /// Bonded check
        /// </summary>
        public static bool IsWeaponBondedToPawn(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || pawn == null)
                return false;

            if (pawn.equipment?.bondedWeapon == weapon)
                return true;

            if (CompBiocodable.IsBiocodedFor(weapon, pawn))
            {
                var bladelinkComp = Caching.Components.GetBladelink(weapon);
                if (bladelinkComp != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Pawn in ritual or ceremony?
        /// </summary>
        public static bool IsInRitual(Pawn pawn)
        {
            if (pawn?.Map?.lordManager == null)
                return false;

            var lord = pawn.Map.lordManager.LordOf(pawn);
            if (lord?.LordJob != null)
            {
                var lordJobType = lord.LordJob.GetType();
                if (ritualLordTypeCache.TryGetValue(lordJobType, out var isRitualLord))
                {
                    if (isRitualLord) return true;
                }
                else
                {
                    bool determined = false;
                    var currentType = lordJobType;
                    while (currentType != null && currentType != typeof(object))
                    {
                        var typeName = currentType.Name;
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
                            typeName.Contains("Advent") ||
                            typeName.Contains("Trial") ||
                            typeName.Contains("Dance") ||
                            typeName.Contains("Drum") ||
                            typeName.Contains("Sacrifice"))
                        {
                            determined = true;
                            break;
                        }
                        currentType = currentType.BaseType;
                    }
                    if (ritualLordTypeCache.Count >= MaxRitualLordTypeCacheSize)
                    {
                        AutoArmLogger.Debug(() => $"Ritual lord type cache exceeded {MaxRitualLordTypeCacheSize} entries, clearing");
                        ritualLordTypeCache.Clear();
                    }

                    ritualLordTypeCache[lordJobType] = determined;
                    if (determined) return true;
                }
            }

            if (pawn.CurJobDef != null)
            {
                if (ritualJobDefCache.TryGetValue(pawn.CurJobDef, out var isRitualJob))
                {
                    if (isRitualJob) return true;
                }
                else
                {
                    bool jobMatches = false;
                    var name = pawn.CurJobDef.defName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        var lower = name.ToLowerInvariant();
                        jobMatches = lower.Contains("ritual") ||
                                     lower.Contains("spectate") ||
                                     lower.Contains("ceremony") ||
                                     lower.Contains("attendparty") ||
                                     lower.Contains("gatheringparticipate") ||
                                     lower.Contains("standandbesociallyactive") ||
                                     lower.Contains("hold") ||
                                     lower.Contains("carry") ||
                                     lower.Contains("deliver") ||
                                     lower.Contains("bring") ||
                                     lower.Contains("dance") ||
                                     lower.Contains("drum") ||
                                     lower.Contains("chant") ||
                                     lower.Contains("pray");
                    }
                    if (ritualJobDefCache.Count >= MaxJobDefCacheSize)
                    {
                        AutoArmLogger.Debug(() => $"Ritual job def cache exceeded {MaxJobDefCacheSize} entries, clearing");
                        ritualJobDefCache.Clear();
                    }

                    ritualJobDefCache[pawn.CurJobDef] = jobMatches;
                    if (jobMatches) return true;
                }
            }

            if (pawn.carryTracker?.CarriedThing != null)
            {
                var carried = pawn.carryTracker.CarriedThing;
                var defName = carried.def?.defName;
                if (!string.IsNullOrEmpty(defName))
                {
                    if (defName.Contains("WoodLog") ||
                        defName.Contains("Lantern") ||
                        defName.Contains("RitualItem") ||
                        defName.Contains("Skylantern") ||
                        defName.Contains("Effigy") ||
                        defName.Contains("Pyre"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Pawn prefers melee (brawler or high melee skill)?
        /// </summary>
        public static bool PrefersMelee(Pawn pawn)
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


    internal static class CaravanCompat
    {
        private static readonly Func<Pawn, RimWorld.Planet.Caravan> GetCaravanPawn;
        private static readonly Func<Thing, RimWorld.Planet.Caravan> GetCaravanThing;

        static CaravanCompat()
        {
            try
            {
                var cu = AccessTools.TypeByName("RimWorld.Planet.CaravanUtility");
                if (cu != null)
                {
                    var mThing = AccessTools.Method(cu, "GetCaravan", new[] { typeof(Thing) });
                    if (mThing != null)
                    {
                        GetCaravanThing = AccessTools.MethodDelegate<Func<Thing, RimWorld.Planet.Caravan>>(mThing);
                    }

                    var mPawn = AccessTools.Method(cu, "GetCaravan", new[] { typeof(Pawn) });
                    if (mPawn != null)
                    {
                        GetCaravanPawn = AccessTools.MethodDelegate<Func<Pawn, RimWorld.Planet.Caravan>>(mPawn);
                    }
                }
            }
            catch
            {
            }
        }

        public static RimWorld.Planet.Caravan GetCaravan(Pawn pawn)
        {
            if (pawn == null) return null;
            try
            {
                if (GetCaravanPawn != null) return GetCaravanPawn(pawn);
                if (GetCaravanThing != null) return GetCaravanThing(pawn);
            }
            catch
            {
            }
            return null;
        }

        public static bool IsCaravanMember(Pawn pawn)
        {
            return GetCaravan(pawn) != null;
        }
    }
}

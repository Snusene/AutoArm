using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    internal static class ForcedWeaponUpgradeTestHelper
    {
        public static void AllowWeaponInOutfit(Pawn pawn, params ThingDef[] defs)
        {
            var policy = pawn?.outfits?.CurrentApparelPolicy;
            if (policy?.filter == null) return;

            foreach (var def in defs)
            {
                if (def != null)
                    policy.filter.SetAllow(def, true);
            }

            var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
            if (weaponsCat != null)
                policy.filter.SetAllow(weaponsCat, true);

            var rangedCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsRanged");
            if (rangedCat != null)
                policy.filter.SetAllow(rangedCat, true);

            var meleeCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsMelee");
            if (meleeCat != null)
                policy.filter.SetAllow(meleeCat, true);

            WeaponCache.OnOutfitFilterChanged(policy);
            WeaponCache.ClearScoreCache();
        }
    }

    internal sealed class ForcedPrimarySameDefUpgradeTest : ITestScenario
    {
        public string Name => "Forced primary quality upgrade";

        private Pawn testPawn;
        private ThingWithComps forcedPrimary;
        private ThingWithComps groundUpgrade;
        private bool? originalAllowForcedUpgrades;

        public void Setup(Map map)
        {
            if (map == null || AutoArmDefOf.Gun_AssaultRifle == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            forcedPrimary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (forcedPrimary != null)
            {
                forcedPrimary.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                testPawn.equipment.AddEquipment(forcedPrimary);
                ForcedWeapons.SetForced(testPawn, forcedPrimary, "test", log: false);
            }

            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(testPawn, AutoArmDefOf.Gun_AssaultRifle);

            var groundPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            groundUpgrade = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle, groundPos, QualityCategory.Excellent);
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedPrimary == null || groundUpgrade == null)
                return TestResult.Failure("Setup incomplete");

            originalAllowForcedUpgrades = AutoArmMod.settings.allowForcedWeaponUpgrades;
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;
            WeaponCache.ClearScoreCache();

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            var result = new TestResult { Success = true };
            result.Data["JobCreated"] = job != null;
            result.Data["JobDef"] = job?.def?.defName;
            result.Data["TargetA"] = (job?.targetA.Thing as ThingWithComps)?.def.defName;

            if (job == null)
            {
                result.Success = false;
                result.FailureReason = "Expected same-def primary upgrade job, got null";
                return result;
            }

            if (job.def != AutoArmDefOf.AutoArmSwapPrimary)
            {
                result.Success = false;
                result.FailureReason = $"Expected AutoArmSwapPrimary, got {job.def?.defName}";
                return result;
            }

            if (job.targetA.Thing != groundUpgrade)
            {
                result.Success = false;
                result.FailureReason = $"Expected target {groundUpgrade.def.defName} (Excellent), got {(job.targetA.Thing as ThingWithComps)?.def.defName}";
            }

            return result;
        }

        public void Cleanup()
        {
            if (originalAllowForcedUpgrades.HasValue && AutoArmMod.settings != null)
                AutoArmMod.settings.allowForcedWeaponUpgrades = originalAllowForcedUpgrades.Value;

            if (testPawn != null)
                ForcedWeapons.ClearForced(testPawn);

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            TestHelpers.SafeDestroyWeapon(groundUpgrade);
        }
    }

    internal sealed class ForcedPrimaryCrossDefBlockedTest : ITestScenario
    {
        public string Name => "Forced primary keeps its def";

        private Pawn testPawn;
        private ThingWithComps forcedPrimary;
        private ThingWithComps groundCrossDef;
        private bool? originalAllowForcedUpgrades;
        private bool crossDefAvailable;

        public void Setup(Map map)
        {
            if (map == null || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (TestDefOf.Gun_ChargeRifle == null) return;
            crossDefAvailable = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            forcedPrimary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (forcedPrimary != null)
            {
                forcedPrimary.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                testPawn.equipment.AddEquipment(forcedPrimary);
                ForcedWeapons.SetForced(testPawn, forcedPrimary, "test", log: false);
            }

            var groundPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            groundCrossDef = TestHelpers.CreateWeapon(map, TestDefOf.Gun_ChargeRifle, groundPos, QualityCategory.Excellent);
        }

        public TestResult Run()
        {
            if (!crossDefAvailable)
                return TestResult.Skip("Gun_ChargeRifle def not present");

            if (testPawn == null || forcedPrimary == null || groundCrossDef == null)
                return TestResult.Failure("Setup incomplete");

            originalAllowForcedUpgrades = AutoArmMod.settings.allowForcedWeaponUpgrades;
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            var result = new TestResult { Success = true };
            result.Data["JobCreated"] = job != null;
            result.Data["JobDef"] = job?.def?.defName;
            result.Data["TargetA"] = (job?.targetA.Thing as ThingWithComps)?.def.defName;

            bool replacesPrimary = job != null &&
                (job.def == AutoArmDefOf.AutoArmSwapPrimary || job.def == JobDefOf.Equip);
            if (replacesPrimary)
            {
                result.Success = false;
                result.FailureReason = $"Expected forced primary kept (cross-def blocked), got {job.def?.defName} targeting {(job.targetA.Thing as ThingWithComps)?.def.defName}";
            }

            return result;
        }

        public void Cleanup()
        {
            if (originalAllowForcedUpgrades.HasValue && AutoArmMod.settings != null)
                AutoArmMod.settings.allowForcedWeaponUpgrades = originalAllowForcedUpgrades.Value;

            if (testPawn != null)
                ForcedWeapons.ClearForced(testPawn);

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            TestHelpers.SafeDestroyWeapon(groundCrossDef);
        }
    }

    internal sealed class ForcedSidearmSettingOffTest : ITestScenario
    {
        public string Name => "Forced sidearm protected";

        private Pawn testPawn;
        private ThingWithComps primary;
        private ThingWithComps forcedSidearm;
        private ThingWithComps groundUpgrade;
        private bool? originalAllowForcedUpgrades;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            primary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (primary != null)
                testPawn.equipment.AddEquipment(primary);

            forcedSidearm = ThingMaker.MakeThing(AutoArmDefOf.Gun_Autopistol) as ThingWithComps;
            if (forcedSidearm != null)
            {
                forcedSidearm.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                if (testPawn.inventory?.innerContainer?.TryAdd(forcedSidearm) == true)
                    ForcedWeapons.AddSidearm(testPawn, forcedSidearm);
            }

            var groundPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            groundUpgrade = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol, groundPos, QualityCategory.Excellent);
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedSidearm == null || groundUpgrade == null)
                return TestResult.Failure("Setup incomplete");

            originalAllowForcedUpgrades = AutoArmMod.settings.allowForcedWeaponUpgrades;
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            var result = new TestResult { Success = true };
            result.Data["JobCreated"] = job != null;
            result.Data["JobDef"] = job?.def?.defName;
            result.Data["TargetA"] = (job?.targetA.Thing as ThingWithComps)?.def.defName;
            result.Data["TargetB"] = (job?.targetB.Thing as ThingWithComps)?.def.defName;

            if (job != null && (job.targetB.Thing == forcedSidearm || job.targetA.Thing == groundUpgrade))
            {
                result.Success = false;
                result.FailureReason = $"Forced sidearm scheduled for swap despite setting OFF: {job.def?.defName}";
            }

            return result;
        }

        public void Cleanup()
        {
            if (originalAllowForcedUpgrades.HasValue && AutoArmMod.settings != null)
                AutoArmMod.settings.allowForcedWeaponUpgrades = originalAllowForcedUpgrades.Value;

            if (testPawn != null)
                ForcedWeapons.ClearForced(testPawn);

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            TestHelpers.SafeDestroyWeapon(groundUpgrade);
        }
    }

    internal sealed class ForcedSidearmSameDefUpgradeTest : ITestScenario
    {
        public string Name => "Forced sidearm quality upgrade";

        private Pawn testPawn;
        private ThingWithComps primary;
        private ThingWithComps forcedSidearm;
        private ThingWithComps groundUpgrade;
        private bool? originalAllowForcedUpgrades;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            primary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (primary != null)
                testPawn.equipment.AddEquipment(primary);

            forcedSidearm = ThingMaker.MakeThing(AutoArmDefOf.Gun_Autopistol) as ThingWithComps;
            if (forcedSidearm != null)
            {
                forcedSidearm.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);
                if (testPawn.inventory?.innerContainer?.TryAdd(forcedSidearm) == true)
                    ForcedWeapons.AddSidearm(testPawn, forcedSidearm);
            }

            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(testPawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.Gun_Autopistol);

            var groundPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            groundUpgrade = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol, groundPos, QualityCategory.Legendary);
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded || SimpleSidearmsCompat.ReflectionFailed)
                return TestResult.Skip("SimpleSidearms not loaded");

            if (testPawn == null || forcedSidearm == null || groundUpgrade == null)
                return TestResult.Failure("Setup incomplete");

            originalAllowForcedUpgrades = AutoArmMod.settings.allowForcedWeaponUpgrades;
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;
            WeaponCache.ClearScoreCache();

            var job = JobHelper.CreateEquipJob(groundUpgrade, isSidearm: false, pawn: testPawn);

            var result = new TestResult { Success = true };
            result.Data["JobCreated"] = job != null;
            result.Data["JobDef"] = job?.def?.defName;
            result.Data["TargetA"] = (job?.targetA.Thing as ThingWithComps)?.def.defName;
            result.Data["TargetB"] = (job?.targetB.Thing as ThingWithComps)?.def.defName;

            if (job == null)
            {
                result.Success = false;
                result.FailureReason = "Expected sidearm swap job from JobHelper, got null";
                return result;
            }

            if (job.def != AutoArmDefOf.AutoArmSwapSidearm)
            {
                result.Success = false;
                result.FailureReason = $"Expected AutoArmSwapSidearm, got {job.def?.defName}";
                return result;
            }

            if (job.targetA.Thing != groundUpgrade)
            {
                result.Success = false;
                result.FailureReason = $"Expected target Legendary autopistol, got {(job.targetA.Thing as ThingWithComps)?.def.defName}";
            }

            if (job.targetB.Thing != forcedSidearm)
            {
                result.Success = false;
                result.FailureReason = $"Expected swap-out target Awful forced autopistol, got {(job.targetB.Thing as ThingWithComps)?.def.defName}";
            }

            return result;
        }

        public void Cleanup()
        {
            if (originalAllowForcedUpgrades.HasValue && AutoArmMod.settings != null)
                AutoArmMod.settings.allowForcedWeaponUpgrades = originalAllowForcedUpgrades.Value;

            if (testPawn != null)
                ForcedWeapons.ClearForced(testPawn);

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            TestHelpers.SafeDestroyWeapon(groundUpgrade);
        }
    }

    internal sealed class ForcedSidearmCrossDefBlockedTest : ITestScenario
    {
        public string Name => "Forced sidearm keeps its def";

        private Pawn testPawn;
        private ThingWithComps primary;
        private ThingWithComps forcedSidearm;
        private ThingWithComps groundCrossDef;
        private bool? originalAllowForcedUpgrades;
        private bool crossDefAvailable;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (TestDefOf.Gun_Revolver == null) return;
            crossDefAvailable = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            primary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (primary != null)
                testPawn.equipment.AddEquipment(primary);

            forcedSidearm = ThingMaker.MakeThing(AutoArmDefOf.Gun_Autopistol) as ThingWithComps;
            if (forcedSidearm != null)
            {
                forcedSidearm.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                if (testPawn.inventory?.innerContainer?.TryAdd(forcedSidearm) == true)
                    ForcedWeapons.AddSidearm(testPawn, forcedSidearm);
            }

            var groundPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            groundCrossDef = TestHelpers.CreateWeapon(map, TestDefOf.Gun_Revolver, groundPos, QualityCategory.Excellent);
        }

        public TestResult Run()
        {
            if (!crossDefAvailable)
                return TestResult.Skip("Gun_Revolver def not present");

            if (testPawn == null || forcedSidearm == null || groundCrossDef == null)
                return TestResult.Failure("Setup incomplete");

            originalAllowForcedUpgrades = AutoArmMod.settings.allowForcedWeaponUpgrades;
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            var result = new TestResult { Success = true };
            result.Data["JobCreated"] = job != null;
            result.Data["JobDef"] = job?.def?.defName;
            result.Data["TargetA"] = (job?.targetA.Thing as ThingWithComps)?.def.defName;
            result.Data["TargetB"] = (job?.targetB.Thing as ThingWithComps)?.def.defName;

            if (job != null && job.targetB.Thing == forcedSidearm)
            {
                result.Success = false;
                result.FailureReason = $"Forced sidearm scheduled for cross-def swap: {job.def?.defName} targeting {(job.targetA.Thing as ThingWithComps)?.def.defName}";
            }

            return result;
        }

        public void Cleanup()
        {
            if (originalAllowForcedUpgrades.HasValue && AutoArmMod.settings != null)
                AutoArmMod.settings.allowForcedWeaponUpgrades = originalAllowForcedUpgrades.Value;

            if (testPawn != null)
                ForcedWeapons.ClearForced(testPawn);

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            TestHelpers.SafeDestroyWeapon(groundCrossDef);
        }
    }

    internal sealed class SameDefLowerQualityBlockedTest : ITestScenario
    {
        public string Name => "Worse quality ignored";

        private Pawn testPawn;
        private ThingWithComps currentPrimary;
        private ThingWithComps lowerQualityGround;

        public void Setup(Map map)
        {
            if (map == null || AutoArmDefOf.Gun_AssaultRifle == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            currentPrimary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (currentPrimary != null)
            {
                currentPrimary.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                testPawn.equipment.AddEquipment(currentPrimary);
            }

            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(testPawn, AutoArmDefOf.Gun_AssaultRifle);

            var groundPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            lowerQualityGround = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle, groundPos, QualityCategory.Awful);
        }

        public TestResult Run()
        {
            if (testPawn == null || currentPrimary == null || lowerQualityGround == null)
                return TestResult.Failure("Setup incomplete");

            WeaponCache.ClearScoreCache();

            var job = JobHelper.CreateEquipJob(lowerQualityGround, isSidearm: false, pawn: testPawn);

            var result = new TestResult { Success = true };
            result.Data["JobCreated"] = job != null;
            result.Data["JobDef"] = job?.def?.defName;

            if (job != null)
            {
                result.Success = false;
                result.FailureReason = $"Awful rifle should not replace Excellent primary, but got {job.def?.defName} targeting {(job.targetA.Thing as ThingWithComps)?.def.defName}";
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            TestHelpers.SafeDestroyWeapon(lowerQualityGround);
        }
    }

    internal abstract class ForcedSidearmScenario : ITestScenario
    {
        public abstract string Name { get; }

        protected Pawn pawn;
        protected Map map;
        private readonly List<ThingWithComps> ground = new List<ThingWithComps>();
        private bool? originalAllowForcedUpgrades;
        private AutoArmSettings settingsBefore;

        protected AutoArmSettings Settings
        {
            get
            {
                if (settingsBefore == null)
                    settingsBefore = AutoArmMod.settings.Clone();
                return AutoArmMod.settings;
            }
        }

        public abstract void Setup(Map map);
        public abstract TestResult Run();

        protected bool MakePawn(Map map, ThingDef primaryDef, QualityCategory quality = QualityCategory.Normal)
        {
            this.map = map;
            if (map == null) return false;

            pawn = TestHelpers.CreateTestPawn(map);
            if (pawn == null) return false;
            pawn.equipment?.DestroyAllEquipment();

            if (primaryDef != null)
            {
                var primary = ThingMaker.MakeThing(primaryDef) as ThingWithComps;
                if (primary == null) return false;
                primary.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
                pawn.equipment.AddEquipment(primary);
                ground.Add(primary);
            }
            return true;
        }

        protected ThingWithComps AddForcedSidearm(ThingDef def, QualityCategory quality)
        {
            var sidearm = ThingMaker.MakeThing(def) as ThingWithComps;
            if (sidearm == null) return null;
            sidearm.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
            if (pawn.inventory?.innerContainer?.TryAdd(sidearm) != true) return null;
            ForcedWeapons.AddSidearm(pawn, sidearm);
            return Track(sidearm);
        }

        protected ThingWithComps AddCarried(ThingDef def, bool remembered)
        {
            var weapon = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null) as ThingWithComps;
            if (weapon == null || pawn.inventory?.innerContainer?.TryAdd(weapon) != true) return null;
            if (remembered)
                SimpleSidearmsCompat.InformOfAddedSidearm(pawn, weapon);
            ground.Add(weapon);
            return weapon;
        }

        protected ThingWithComps Ground(ThingDef def, QualityCategory quality, float minDistance = 1.5f, float maxDistance = 3f)
        {
            var pos = TestPositions.GetNearbyPosition(pawn.Position, minDistance, maxDistance, map);
            return Track(TestHelpers.CreateWeapon(map, def, pos, quality));
        }

        protected ThingWithComps Track(ThingWithComps weapon)
        {
            if (weapon != null) ground.Add(weapon);
            return weapon;
        }

        private readonly List<Thing> hidden = new List<Thing>();

        private void HideOthers()
        {
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToArray())
            {
                if (thing is ThingWithComps weapon && !ground.Contains(weapon) && !weapon.IsForbidden(Faction.OfPlayer))
                {
                    weapon.SetForbidden(true, false);
                    hidden.Add(weapon);
                }
            }
        }

        protected SimScope Live(bool actor = true)
        {
            HideOthers();
            return actor ? new SimScope(map, pawn) : new SimScope(map);
        }

        protected void SetForcedUpgrades(bool upgrades)
        {
            if (!originalAllowForcedUpgrades.HasValue)
                originalAllowForcedUpgrades = AutoArmMod.settings.allowForcedWeaponUpgrades;
            AutoArmMod.settings.allowForcedWeaponUpgrades = upgrades;
        }

        protected Job GiveJob(bool upgrades = true)
        {
            SetForcedUpgrades(upgrades);
            HideOthers();
            WeaponCache.ClearScoreCache();
            using (JobGiver_PickUpBetterWeapon.EnterTestMode())
                return new JobGiver_PickUpBetterWeapon().TestTryGiveJob(pawn);
        }

        protected string Blocked(ThingWithComps weapon)
        {
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return "incapable of violence";
            if (weapon.def.IsRangedWeapon && pawn.WorkTagIsDisabled(WorkTags.Shooting))
                return "incapable of shooting";
            if (weapon.def.IsRangedWeapon && Caching.PawnValidation.IsBrawler(pawn))
                return "brawler trait";
            if (ValidationHelper.TryGetIdeologyWeaponBlock(weapon, pawn, out string ideoReason, out _))
                return $"ideology: {ideoReason}";
            if (!EquipmentUtility.CanEquip(weapon, pawn, out string equipReason))
                return $"vanilla: {equipReason}";
            return null;
        }

        protected string Checks(JobGiver_PickUpBetterWeapon giver, ThingWithComps weapon)
        {
            return $"size={giver.IsValidSize(pawn, weapon)}, blacklisted={Blacklist.IsBlacklisted(weapon.def, pawn)}, dropped={DroppedItems.IsDropped(weapon)}, temp={JobHelper.IsTemporary(pawn)}, dps={weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS):F1}, brawler={Caching.PawnValidation.IsBrawler(pawn)}";
        }

        protected static string Describe(Job job)
        {
            if (job == null) return "no job";
            return $"{job.def?.defName} A={(job.targetA.Thing as ThingWithComps)?.def.label} B={(job.targetB.Thing as ThingWithComps)?.def.label}";
        }

        public virtual void Cleanup()
        {
            if (originalAllowForcedUpgrades.HasValue && AutoArmMod.settings != null)
                AutoArmMod.settings.allowForcedWeaponUpgrades = originalAllowForcedUpgrades.Value;
            if (settingsBefore != null)
            {
                AutoArmMod.settings.CopyFrom(settingsBefore);
                WeaponCache.ClearScoreCache();
                EquipEligibility.Clear();
            }

            if (pawn != null)
            {
                AutoEquipState.ClearWeaponToForce(pawn);
                SimpleSidearmsCompat.SetForcedUnarmed(pawn, false);
                ForcedWeapons.ClearForced(pawn);
                if (!pawn.Destroyed)
                {
                    pawn.equipment?.DestroyAllEquipment();
                    TestHelpers.SafeDestroyPawn(pawn);
                }
            }

            foreach (var weapon in ground)
                TestHelpers.SafeDestroyWeapon(weapon);
            ground.Clear();

            foreach (var thing in hidden)
            {
                if (!thing.Destroyed)
                    thing.SetForbidden(false, false);
            }
            hidden.Clear();
        }
    }

    internal sealed class ForcedSidearmAllowsPrimaryUpgradeTest : ForcedSidearmScenario
    {
        public override string Name => "Forced sidearm allows primary upgrade";
        private ThingWithComps rifle;
        private bool ready;

        public override void Setup(Map map)
        {
            if (TestDefOf.Gun_Revolver == null || AutoArmDefOf.Gun_Autopistol == null || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, TestDefOf.Gun_Revolver)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, TestDefOf.Gun_Revolver, AutoArmDefOf.Gun_Autopistol, AutoArmDefOf.Gun_AssaultRifle);
            ready = AddForcedSidearm(AutoArmDefOf.Gun_Autopistol, QualityCategory.Normal) != null;
            rifle = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Excellent);
        }

        public override TestResult Run()
        {
            if (!ready || rifle == null)
                return TestResult.Failure("Setup incomplete");

            var job = GiveJob();
            var target = job?.targetA.Thing as ThingWithComps;
            if (target == null || target.def == AutoArmDefOf.Gun_Autopistol)
                return TestResult.Failure($"Expected the unforced revolver to upgrade past the forced sidearm's type, got {Describe(job)}");
            return TestResult.Pass();
        }
    }

    internal sealed class ForcedSidearmUpgradeStaysForcedTest : ForcedSidearmScenario
    {
        public override string Name => "Forced sidearm upgrade stays forced";
        private ThingWithComps forced;
        private ThingWithComps upgrade;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_Autopistol == null || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.Gun_Autopistol);
            forced = AddForcedSidearm(AutoArmDefOf.Gun_Autopistol, QualityCategory.Awful);
            upgrade = Ground(AutoArmDefOf.Gun_Autopistol, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded || SimpleSidearmsCompat.ReflectionFailed)
                return TestResult.Skip("SimpleSidearms not loaded");
            if (forced == null || upgrade == null)
                return TestResult.Failure("Setup incomplete");

            var job = GiveJob();
            if (job?.def != AutoArmDefOf.AutoArmSwapSidearm || job.targetA.Thing != upgrade || job.targetB.Thing != forced)
                return TestResult.Failure($"Expected sidearm swap of the forced autopistol, got {Describe(job)}");
            if (!AutoEquipState.ShouldForceWeapon(pawn, upgrade))
                return TestResult.Failure("Upgrade is not queued to inherit forced status");
            return TestResult.Pass();
        }
    }

    internal sealed class ForcedDefPickedByPrimarySearchStaysForcedTest : ForcedSidearmScenario
    {
        public override string Name => "Forced def from primary search stays forced";
        private ThingWithComps forced;
        private ThingWithComps upgrade;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_Autopistol == null || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_Autopistol, QualityCategory.Awful)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.Gun_Autopistol);
            forced = AddForcedSidearm(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Normal);
            upgrade = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded || SimpleSidearmsCompat.ReflectionFailed)
                return TestResult.Skip("SimpleSidearms not loaded");
            if (forced == null || upgrade == null)
                return TestResult.Failure("Setup incomplete");

            var job = GiveJob();
            if (job?.def != AutoArmDefOf.AutoArmSwapSidearm || job.targetA.Thing != upgrade || job.targetB.Thing != forced)
                return TestResult.Failure($"Expected sidearm swap of the forced rifle, got {Describe(job)}");
            if (!AutoEquipState.ShouldForceWeapon(pawn, upgrade))
                return TestResult.Failure("Upgrade is not queued to inherit forced status");
            return TestResult.Pass();
        }
    }

    internal sealed class SecondForcedSidearmUpgradesTest : ForcedSidearmScenario
    {
        public override string Name => "Second forced sidearm upgrades";
        private ThingWithComps second;
        private ThingWithComps upgrade;
        private bool ready;

        public override void Setup(Map map)
        {
            if (TestDefOf.Gun_Revolver == null || AutoArmDefOf.Gun_Autopistol == null || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.Gun_Autopistol, TestDefOf.Gun_Revolver);
            ready = AddForcedSidearm(AutoArmDefOf.Gun_Autopistol, QualityCategory.Legendary) != null;
            second = AddForcedSidearm(TestDefOf.Gun_Revolver, QualityCategory.Awful);
            upgrade = Ground(TestDefOf.Gun_Revolver, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded || SimpleSidearmsCompat.ReflectionFailed)
                return TestResult.Skip("SimpleSidearms not loaded");
            if (!ready || second == null || upgrade == null)
                return TestResult.Failure("Setup incomplete");

            var job = GiveJob();
            if (job?.def != AutoArmDefOf.AutoArmSwapSidearm || job.targetA.Thing != upgrade || job.targetB.Thing != second)
                return TestResult.Failure($"Expected sidearm swap of the second forced sidearm, got {Describe(job)}");
            return TestResult.Pass();
        }
    }

    internal sealed class ForcedPrimaryStillGetsSidearmTest : ForcedSidearmScenario
    {
        public override string Name => "Forced primary still gets sidearm";
        private ThingWithComps knife;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.MeleeWeapon_Knife == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.MeleeWeapon_Knife);
            ForcedWeapons.SetForced(pawn, pawn.equipment.Primary, "test", log: false);
            knife = Ground(AutoArmDefOf.MeleeWeapon_Knife, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsManagingPawn(pawn))
                return TestResult.Skip("SimpleSidearms not managing pawn");
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return TestResult.Skip("Auto equip sidearms off");
            if (knife == null)
                return TestResult.Failure("Setup incomplete");
            if (!SimpleSidearmsCompat.CanPickupSidearm(knife, pawn, out string ssReason))
                return TestResult.Skip($"SimpleSidearms refuses the knife: {ssReason}");
            string blocked = Blocked(knife);
            if (blocked != null)
                return TestResult.Skip($"Knife blocked for this pawn by {blocked}");

            var job = GiveJob(upgrades: false);
            if (job?.targetA.Thing != knife || job.def == AutoArmDefOf.AutoArmSwapPrimary)
            {
                var giver = new JobGiver_PickUpBetterWeapon();
                bool considered = giver.ShouldConsiderWeapon(pawn, knife, pawn.equipment.Primary);
                var found = giver.FindBestSidearm(pawn, out string sidearmReason);
                return TestResult.Failure($"Expected sidearm pickup of the knife, got {Describe(job)}: considered={considered}, best={found?.def.defName ?? "none"} ({sidearmReason}), forbidden={knife.IsForbidden(pawn)}, {Checks(giver, knife)}");
            }
            return TestResult.Pass();
        }
    }

    internal sealed class AreaRestrictedPicksAllowedWeaponTest : ForcedSidearmScenario
    {
        public override string Name => "Area restricted pawn picks allowed weapon";
        private ThingWithComps rifle;
        private ThingWithComps pistol;
        private Area_Allowed area;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (!MakePawn(map, null)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.Gun_Autopistol);
            rifle = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
            pistol = Ground(AutoArmDefOf.Gun_Autopistol, QualityCategory.Poor);
        }

        public override TestResult Run()
        {
            if (AutoArmMod.settings?.onlyAutoEquipFromStorage == true)
                return TestResult.Skip("Storage only setting on");
            if (rifle == null || pistol == null || pawn.playerSettings == null)
                return TestResult.Failure("Setup incomplete");
            if (rifle.Position == pistol.Position)
                return TestResult.Skip("Weapons share a cell");

            var job = GiveJob();
            if (job?.targetA.Thing != rifle)
                return TestResult.Skip($"Rifle not preferred without an area, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(rifle);
            JobGiverMapComponent.ClearAllState();

            if (!map.areaManager.TryMakeNewAllowed(out area))
                return TestResult.Skip("No free area slot");
            area[pawn.Position] = true;
            area[pistol.Position] = true;
            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;

            job = GiveJob();
            if (job?.targetA.Thing != pistol)
                return TestResult.Failure($"Expected the autopistol inside the area, got {Describe(job)}");
            return TestResult.Pass();
        }

        public override void Cleanup()
        {
            if (pawn?.playerSettings != null)
                pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
            area?.Delete();
            base.Cleanup();
        }
    }

    internal sealed class EqualQualityBetterStuffSidearmSwapTest : ForcedSidearmScenario
    {
        public override string Name => "Equal quality better material sidearm swap";
        private ThingWithComps carried;
        private ThingWithComps better;
        private bool? oldUpgrades, oldEquipped, oldAutoSidearms;

        public override void Setup(Map map)
        {
            var club = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Club");
            if (club == null || ThingDefOf.WoodLog == null || ThingDefOf.Uranium == null || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, club);

            carried = Make(club, ThingDefOf.WoodLog);
            if (carried == null || pawn.inventory?.innerContainer?.TryAdd(carried) != true)
            {
                carried = null;
                return;
            }
            SimpleSidearmsCompat.InformOfAddedSidearm(pawn, carried);

            better = Make(club, ThingDefOf.Uranium);
            if (better == null) return;
            GenSpawn.Spawn(better, TestPositions.GetNearbyPosition(pawn.Position, 1.5f, 3f, map), map);
        }

        private static ThingWithComps Make(ThingDef def, ThingDef stuff)
        {
            var weapon = ThingMaker.MakeThing(def, stuff) as ThingWithComps;
            weapon?.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
            return weapon;
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsManagingPawn(pawn))
                return TestResult.Skip("SimpleSidearms not managing pawn");
            if (carried == null || better == null || !better.Spawned)
                return TestResult.Failure("Setup incomplete");
            if (!SimpleSidearmsCompat.CanUseSidearmForSwap(better, carried, pawn, out string ssReason))
                return TestResult.Skip($"SimpleSidearms rejects the club: {ssReason}");

            oldUpgrades = AutoArmMod.settings.allowSidearmUpgrades;
            oldEquipped = AutoArmMod.settings.upgradeEquippedWeapons;
            oldAutoSidearms = AutoArmMod.settings.autoEquipSidearms;
            AutoArmMod.settings.allowSidearmUpgrades = true;
            AutoArmMod.settings.upgradeEquippedWeapons = true;
            AutoArmMod.settings.autoEquipSidearms = true;
            WeaponCache.ClearScoreCache();

            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Club blocked for this pawn by {blocked}");

            var giver = new JobGiver_PickUpBetterWeapon();
            if (!giver.ShouldConsiderWeapon(pawn, better, pawn.equipment.Primary))
            {
                var same = SimpleSidearmsCompat.SameDefSwap(pawn, better, out _);
                return TestResult.Failure($"Weapon check rejected the uranium club: swap={same}, wood={WeaponCache.GetCachedScore(pawn, carried):F1}, uranium={WeaponCache.GetCachedScore(pawn, better):F1}, {Checks(giver, better)}");
            }

            var helperJob = JobHelper.CreateEquipJob(better, isSidearm: false, pawn: pawn);
            if (helperJob?.def != AutoArmDefOf.AutoArmSwapSidearm || helperJob.targetB.Thing != carried)
                return TestResult.Failure($"Job creation: expected sidearm swap of the wood club, got {Describe(helperJob)}");

            var ssJob = SimpleSidearmsCompat.TryGetWeaponJob(pawn, better, bypassCooldown: true);
            if (ssJob?.def != AutoArmDefOf.AutoArmSwapSidearm || ssJob.targetB.Thing != carried)
                return TestResult.Failure($"SS path: expected sidearm swap of the wood club, got {Describe(ssJob)}");

            return TestResult.Pass();
        }

        public override void Cleanup()
        {
            if (AutoArmMod.settings != null)
            {
                if (oldUpgrades.HasValue) AutoArmMod.settings.allowSidearmUpgrades = oldUpgrades.Value;
                if (oldEquipped.HasValue) AutoArmMod.settings.upgradeEquippedWeapons = oldEquipped.Value;
                if (oldAutoSidearms.HasValue) AutoArmMod.settings.autoEquipSidearms = oldAutoSidearms.Value;
            }
            TestHelpers.SafeDestroyWeapon(better);
            base.Cleanup();
        }
    }

    internal sealed class SSForcedUnarmedStaysUnarmedTest : ForcedSidearmScenario
    {
        public override string Name => "SS forced unarmed stays unarmed";
        private ThingWithComps rifle;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, null)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            rifle = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Normal);
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsManagingPawn(pawn))
                return TestResult.Skip("SimpleSidearms not managing pawn");
            if (rifle == null)
                return TestResult.Failure("Setup incomplete");

            SimpleSidearmsCompat.SetForcedUnarmed(pawn, true);
            if (!SimpleSidearmsCompat.WantsUnarmed(pawn))
                return TestResult.Failure("Could not read SS forced unarmed");

            var job = GiveJob();
            if (job != null)
                return TestResult.Failure($"SS forced unarmed pawn got {Describe(job)}");

            SimpleSidearmsCompat.SetForcedUnarmed(pawn, false);
            job = GiveJob();
            if (!(job?.targetA.Thing is ThingWithComps))
                return TestResult.Failure($"Pawn without SS forced unarmed should arm, got {Describe(job)}");
            return TestResult.Pass();
        }
    }

    internal abstract class CargoPolicyScenario : ForcedSidearmScenario
    {
        protected ThingWithComps carried;
        protected abstract bool Remembered { get; }

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary)) return;
            SimpleSidearmsCompat.IsRememberedSidearm(pawn, pawn.equipment.Primary);
            carried = AddCarried(AutoArmDefOf.Gun_Autopistol, Remembered);
        }

        protected bool StillCarried()
        {
            var policy = pawn.outfits.CurrentApparelPolicy;
            var saved = new ThingFilter();
            saved.CopyAllowancesFrom(policy.filter);
            try
            {
                policy.filter.SetAllow(ThingCategoryDefOf.Weapons, false);
                policy.filter.SetAllow(AutoArmDefOf.Gun_AssaultRifle, true);
                WeaponCache.OnOutfitFilterChanged(policy);
                GiveJob();
                return pawn.inventory.innerContainer.Contains(carried);
            }
            finally
            {
                policy.filter.CopyAllowancesFrom(saved);
                WeaponCache.OnOutfitFilterChanged(policy);
            }
        }
    }

    internal sealed class CargoWeaponKeptByPolicyTest : CargoPolicyScenario
    {
        public override string Name => "Cargo weapon kept by policy";
        protected override bool Remembered => false;

        public override TestResult Run()
        {
            if (carried == null || pawn.outfits?.CurrentApparelPolicy?.filter == null)
                return TestResult.Failure("Setup incomplete");
            if (!StillCarried())
                return TestResult.Failure("Disallowed cargo weapon was dropped from inventory");
            return TestResult.Pass();
        }
    }

    internal sealed class RememberedSidearmDroppedByPolicyTest : CargoPolicyScenario
    {
        public override string Name => "Remembered sidearm dropped by policy";
        protected override bool Remembered => true;

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsManagingPawn(pawn))
                return TestResult.Skip("SimpleSidearms not managing pawn");
            if (carried == null || pawn.outfits?.CurrentApparelPolicy?.filter == null)
                return TestResult.Failure("Setup incomplete");
            if (!SimpleSidearmsCompat.IsRememberedSidearm(pawn, carried))
                return TestResult.Failure("SS does not remember the sidearm");
            if (StillCarried())
                return TestResult.Failure("Disallowed SS sidearm was not dropped");
            return TestResult.Pass();
        }
    }
}

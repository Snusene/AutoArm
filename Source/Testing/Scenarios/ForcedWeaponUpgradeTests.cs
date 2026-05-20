using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
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

            if (job != null)
            {
                result.Success = false;
                result.FailureReason = $"Expected no job (cross-def blocked), got {job.def?.defName} targeting {(job.targetA.Thing as ThingWithComps)?.def.defName}";
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
}

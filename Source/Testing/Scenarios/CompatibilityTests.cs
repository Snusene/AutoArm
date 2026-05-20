using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class SimpleSidearmsIntegrationTest : ITestScenario
    {
        public string Name => "SS integration";
        private Pawn testPawn;
        private ThingWithComps primaryWeapon;
        private ThingWithComps sidearmWeapon;
        private bool originalAutoEquip;

        public void Setup(Map map)
        {
            if (map == null) return;

            originalAutoEquip = AutoArmMod.settings.autoEquipSidearms;
            AutoArmMod.settings.autoEquipSidearms = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                primaryWeapon = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (primaryWeapon != null)
                {
                    testPawn.equipment?.AddEquipment(primaryWeapon);
                    SimpleSidearmsCompat.InformOfAddedPrimary(testPawn, primaryWeapon);
                }

                var meleeDef = AutoArmDefOf.MeleeWeapon_Knife ?? AutoArmDefOf.MeleeWeapon_LongSword;
                if (meleeDef != null)
                {
                    sidearmWeapon = TestHelpers.CreateWeapon(map, meleeDef,
                        testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);

                    if (sidearmWeapon != null)
                    {
                        WeaponCache.AddWeaponToCache(sidearmWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");
            if (testPawn == null || sidearmWeapon == null)
                return TestResult.Failure("Test setup failed");

            string reason;
            bool canPickup = SimpleSidearmsCompat.CanPickupSidearm(sidearmWeapon, testPawn, out reason);
            if (!canPickup)
                return TestResult.Skip($"SS user config rejects pickup: {reason ?? "no reason"}");

            var job = SimpleSidearmsCompat.TryGetWeaponJob(testPawn, sidearmWeapon, bypassCooldown: true);
            if (job == null)
                return TestResult.Failure("TryGetWeaponJob returned null for valid sidearm");
            if (job.targetA.Thing != sidearmWeapon)
                return TestResult.Failure($"Job targets {job.targetA.Thing?.Label}, expected {sidearmWeapon.Label}");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            AutoArmMod.settings.autoEquipSidearms = originalAutoEquip;
            TestHelpers.SafeDestroyWeapon(sidearmWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(primaryWeapon);
        }
    }


    internal sealed class SimpleSidearmsSlotLimitTest : ITestScenario
    {
        public string Name => "SS slot management";
        private Pawn testPawn;
        private List<ThingWithComps> ownedWeapons = new List<ThingWithComps>();
        private ThingWithComps betterWeapon;
        private bool originalSetting;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded) return;

            originalSetting = AutoArmMod.settings.allowSidearmUpgrades;
            AutoArmMod.settings.allowSidearmUpgrades = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                for (int i = 0; i < 3; i++)
                {
                    var weaponDef = i % 2 == 0 ? AutoArmDefOf.Gun_Autopistol : AutoArmDefOf.MeleeWeapon_Knife;
                    if (weaponDef != null)
                    {
                        ThingDef stuff = null;
                        if (weaponDef.MadeFromStuff)
                        {
                            stuff = ThingDefOf.Steel;
                        }
                        var weapon = ThingMaker.MakeThing(weaponDef, stuff) as ThingWithComps;
                        if (weapon != null)
                        {
                            var comp = weapon.TryGetComp<CompQuality>();
                            comp?.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);

                            if (i == 0)
                            {
                                testPawn.equipment?.AddEquipment(weapon);
                            }
                            else
                            {
                                if (weapon.Spawned) weapon.DeSpawn();
                                testPawn.inventory?.innerContainer?.TryAdd(weapon);
                            }
                            ownedWeapons.Add(weapon);
                        }
                    }
                }

                betterWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                    TestPositions.GetNearbyPosition(testPawn.Position, 2, 4, map),
                    QualityCategory.Legendary);

                if (betterWeapon != null)
                {
                    WeaponCache.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");
            if (testPawn == null || betterWeapon == null)
                return TestResult.Failure("Test setup failed");

            var job = SimpleSidearmsCompat.TryGetWeaponJob(testPawn, betterWeapon);

            string reason;
            bool canAddMore = SimpleSidearmsCompat.CanPickupSidearm(betterWeapon, testPawn, out reason);

            if (canAddMore)
            {
                if (job == null)
                    return TestResult.Failure("SS allows pickup but no job created");
                if (job.targetA.Thing != betterWeapon)
                    return TestResult.Failure($"Job targets {job.targetA.Thing?.Label}, expected {betterWeapon.Label}");
                return TestResult.Pass();
            }

            if (job != null && job.targetA.Thing == betterWeapon && job.def == JobDefOf.Equip)
                return TestResult.Failure($"Created plain Equip for new sidearm despite SS rejection: {reason}");

            return TestResult.Pass().WithData("SSRejection", reason ?? "(none)");
        }

        public void Cleanup()
        {
            AutoArmMod.settings.allowSidearmUpgrades = originalSetting;

            TestHelpers.SafeDestroyWeapon(betterWeapon);

            TestHelpers.SafeDestroyPawn(testPawn);

            foreach (var weapon in ownedWeapons)
            {
                if (weapon != null && !weapon.Destroyed && weapon.ParentHolder is Map)
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            ownedWeapons.Clear();
        }
    }

    internal sealed class SimpleSidearmsForcedWeaponTest : ITestScenario
    {
        public string Name => "SS respects forced sidearms";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                forcedWeapon = ThingMaker.MakeThing(AutoArmDefOf.Gun_Autopistol) as ThingWithComps;
                if (forcedWeapon != null)
                {
                    testPawn.equipment?.AddEquipment(forcedWeapon);
                    ForcedWeapons.SetForced(testPawn, forcedWeapon);
                }

                betterWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                    TestPositions.GetNearbyPosition(testPawn.Position, 2, 4, map),
                    QualityCategory.Legendary);

                if (betterWeapon != null)
                {
                    WeaponCache.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");

            if (testPawn == null || forcedWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            try
            {
                result.Data["Forced Weapon Upgrades Allowed"] = AutoArmMod.settings.allowForcedWeaponUpgrades;
                result.Data["Weapon Is Forced"] = ForcedWeapons.IsForced(testPawn, forcedWeapon);
                result.Data["Current Weapon"] = forcedWeapon.Label;
                result.Data["Better Weapon Available"] = betterWeapon?.Label ?? "None";

                var job = betterWeapon != null ? SimpleSidearmsCompat.TryGetWeaponJob(testPawn, betterWeapon) : null;

                if (AutoArmMod.settings.allowForcedWeaponUpgrades)
                {
                    result.Data["Upgrade Job Created"] = job != null;
                    if (job != null)
                    {
                        result.Data["Upgrade Target"] = job.targetA.Thing?.Label ?? "Unknown";
                    }
                }
                else
                {
                    if (job != null && job.targetA.Thing == betterWeapon)
                    {
                        result.Success = false;
                        result.Data["Error"] = "Created upgrade job for forced weapon when not allowed";
                    }
                    else
                    {
                        result.Data["Correctly Blocked"] = true;
                    }
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Data["Error"] = $"Forced weapon test failed: {e.Message}";
                AutoArmLogger.Warn("[TEST] SimpleSidearmsForcedWeaponTest failed", e);
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeapons.ClearForced(testPawn);
            TestHelpers.SafeDestroyWeapon(betterWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(forcedWeapon);
        }
    }

    internal sealed class CombatExtendedAmmoTest : ITestScenario
    {
        public string Name => "CE integration";
        private Pawn testPawn;
        private ThingWithComps ceWeapon;

        public void Setup(Map map)
        {
            if (!CECompat.IsLoaded) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                var ceWeaponDef = DefDatabase<ThingDef>.AllDefs
                    .FirstOrDefault(d => d.IsWeapon && d.IsRangedWeapon &&
                                   d.defName.Contains("Gun_"));

                if (ceWeaponDef != null)
                {
                    ceWeapon = TestHelpers.CreateWeapon(map, ceWeaponDef,
                        TestPositions.GetNearbyPosition(testPawn.Position, 2, 4, map));

                    if (ceWeapon != null)
                    {
                        WeaponCache.AddWeaponToCache(ceWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!CECompat.IsLoaded)
                return TestResult.Skip("Combat Extended not loaded");
            if (testPawn == null || ceWeapon == null)
                return TestResult.Failure("Test setup failed");

            bool savedSetting = AutoArmMod.settings.checkCEAmmo;
            try
            {
                AutoArmMod.settings.checkCEAmmo = false;
                if (CECompat.ShouldCheckAmmo())
                    return TestResult.Failure("ShouldCheckAmmo true with checkCEAmmo=false");
                if (CECompat.ShouldSkipWeaponForCE(ceWeapon, testPawn))
                    return TestResult.Failure("ShouldSkipWeaponForCE true when ammo check disabled");

                AutoArmMod.settings.checkCEAmmo = true;
                if (!CECompat.ShouldCheckAmmo())
                    return TestResult.Failure("ShouldCheckAmmo false with checkCEAmmo=true");

                bool skipsWhenEnabled = CECompat.ShouldSkipWeaponForCE(ceWeapon, testPawn);
                return TestResult.Pass().WithData("SkipsWhenEnabled", skipsWhenEnabled);
            }
            finally
            {
                AutoArmMod.settings.checkCEAmmo = savedSetting;
            }
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(ceWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class SimpleSidearmsCrossTypePrimarySwapTest : ITestScenario
    {
        public string Name => "SS blocks bad cross-swap";

        private Pawn testPawn;
        private ThingWithComps primaryRanged;
        private ThingWithComps sidearmMelee;
        private ThingWithComps mapMelee;
        private bool originalAllowSidearmUpgrades;
        private bool originalAutoEquipSidearms;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded) return;

            originalAllowSidearmUpgrades = AutoArmMod.settings.allowSidearmUpgrades;
            originalAutoEquipSidearms = AutoArmMod.settings.autoEquipSidearms;
            AutoArmMod.settings.allowSidearmUpgrades = true;
            AutoArmMod.settings.autoEquipSidearms = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            primaryRanged = ThingMaker.MakeThing(AutoArmDefOf.Gun_BoltActionRifle) as ThingWithComps;
            if (primaryRanged != null)
            {
                primaryRanged.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);
                testPawn.equipment?.AddEquipment(primaryRanged);
                SimpleSidearmsCompat.InformOfAddedPrimary(testPawn, primaryRanged);
            }

            sidearmMelee = ThingMaker.MakeThing(AutoArmDefOf.MeleeWeapon_Knife, ThingDefOf.Steel) as ThingWithComps;
            if (sidearmMelee != null)
            {
                sidearmMelee.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                if (sidearmMelee.Spawned) sidearmMelee.DeSpawn();
                testPawn.inventory?.innerContainer?.TryAdd(sidearmMelee);
                SimpleSidearmsCompat.InformOfAddedSidearm(testPawn, sidearmMelee);
            }

            mapMelee = TestHelpers.CreateWeapon(map, AutoArmDefOf.MeleeWeapon_LongSword,
                TestPositions.GetNearbyPosition(testPawn.Position, 2, 4, map),
                QualityCategory.Legendary);
            if (mapMelee != null)
            {
                WeaponCache.AddWeaponToCache(mapMelee);
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");

            if (testPawn == null || primaryRanged == null || sidearmMelee == null || mapMelee == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            try
            {
                result.Data["Initial Primary"] = $"{primaryRanged.Label} (ranged)";
                result.Data["Initial Sidearm"] = $"{sidearmMelee.Label} (melee)";
                result.Data["Map Candidate"] = $"{mapMelee.Label} (melee)";

                string preReason;
                bool preCanPickup = SimpleSidearmsCompat.CanPickupSidearm(mapMelee, testPawn, out preReason);
                result.Data["SS Would Allow Adding New Melee"] = preCanPickup;
                if (preCanPickup)
                {
                    result.Data["Note"] = "Test preconditions not met: SS would allow adding another melee. " +
                                          "Set SS to Separate modes with 1 melee slot / 1 ranged slot to exercise the bug path.";
                    return result;
                }
                result.Data["SS Rejection Reason"] = preReason ?? "(none)";

                var job = AutoArm.Jobs.JobHelper.CreateEquipJob(mapMelee, isSidearm: false, pawn: testPawn);

                if (job == null)
                {
                    result.Success = false;
                    result.FailureReason = "CreateEquipJob returned null; expected a sidearm swap job routed from the blocked primary swap.";
                    return result;
                }

                result.Data["Returned Job Def"] = job.def?.defName ?? "(null)";
                var targetA = job.targetA.Thing as ThingWithComps;
                var targetB = job.targetB.Thing as ThingWithComps;
                result.Data["Job Target A (new)"] = targetA?.Label ?? "(null)";
                result.Data["Job Target B (old)"] = targetB?.Label ?? "(null)";

                if (job.def == AutoArmDefOf.AutoArmSwapPrimary)
                {
                    bool crossTypeSwap = targetA != null && targetB != null &&
                                         targetA.def.IsMeleeWeapon != targetB.def.IsMeleeWeapon;
                    if (crossTypeSwap)
                    {
                        result.Success = false;
                        result.FailureReason = $"BUG: AutoArm created a cross-type primary swap ({targetB?.Label} ranged for {targetA?.Label} melee) " +
                                               $"that would leave the pawn with 2 melees and 0 ranged, violating SS slot limits.";
                        return result;
                    }
                }

                if (job.def == AutoArmDefOf.AutoArmSwapSidearm)
                {
                    if (targetA == mapMelee && targetB == sidearmMelee)
                    {
                        result.Data["Outcome"] = "PASS: routed to same-type sidearm swap (melee knife replaced with melee longsword)";
                        return result;
                    }
                    result.Success = false;
                    result.FailureReason = $"Sidearm swap created but with unexpected targets. " +
                                           $"Got: {targetA?.Label} replacing {targetB?.Label}. " +
                                           $"Expected: {mapMelee.Label} replacing {sidearmMelee.Label}.";
                    return result;
                }

                result.Success = false;
                result.FailureReason = $"Unexpected job type: {job.def?.defName}. " +
                                       $"Expected AutoArmSwapSidearm (pawn should upgrade the existing melee sidearm, not swap the ranged primary).";
                return result;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.FailureReason = $"Exception: {e.Message}";
                AutoArmLogger.Warn("[TEST] SimpleSidearmsCrossTypePrimarySwapTest failed", e);
                return result;
            }
        }

        public void Cleanup()
        {
            AutoArmMod.settings.allowSidearmUpgrades = originalAllowSidearmUpgrades;
            AutoArmMod.settings.autoEquipSidearms = originalAutoEquipSidearms;

            TestHelpers.SafeDestroyWeapon(mapMelee);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class SimpleSidearmsReflectionFixTest : ITestScenario
    {
        public string Name => "SS reflection binds";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                testWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0));

                if (testWeapon != null)
                {
                    WeaponCache.AddWeaponToCache(testWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");
            if (testPawn == null || testWeapon == null)
                return TestResult.Failure("Test setup failed");

            try
            {
                if (testWeapon.Spawned) testWeapon.DeSpawn();
                testPawn.inventory?.innerContainer?.TryAdd(testWeapon);
                SimpleSidearmsCompat.InformOfAddedSidearm(testPawn, testWeapon);
            }
            catch (Exception e)
            {
                return TestResult.Failure($"InformOfAddedSidearm threw: {e.Message}");
            }

            if (SimpleSidearmsCompat.ReflectionFailed)
                return TestResult.Failure("SS reflection failed on known-good input");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(testWeapon);
        }
    }
}

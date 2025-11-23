using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Jobs;
using RimWorld;
using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// CRITICAL: Test specific SimpleSidearms reflection issues found in logs
    /// </summary>
    public class SimpleSidearmsReflectionFixTest : ITestScenario
    {
        public string Name => "SimpleSidearms Reflection Fix Validation";
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
                    WeaponCacheManager.AddWeaponToCache(testWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Pass().WithData("Note", "SimpleSidearms not loaded");

            if (testPawn == null || testWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            result.Data["Testing_InformOfAddedSidearm"] = true;

            try
            {
                if (testWeapon.Spawned) testWeapon.DeSpawn();
                testPawn.inventory?.innerContainer?.TryAdd(testWeapon);

                result.Data["InformOfAddedSidearm_Success"] = true;
            }
            catch (Exception e)
            {
                result.Data["InformOfAddedSidearm_Error"] = e.Message;

                if (e.Message.Contains("parameters specified does not match"))
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR1"] = "InformOfAddedSidearm parameter mismatch still exists!";

                    DiagnoseInformOfAddedSidearm(result);
                }
            }

            result.Data["ReflectionFailed"] = SimpleSidearmsCompat.ReflectionFailed;

            if (SimpleSidearmsCompat.ReflectionFailed)
            {
                result.Data["Warning"] = "SimpleSidearms reflection has failed - functionality degraded";
            }

            return result;
        }

        private void DiagnoseInformOfAddedSidearm(TestResult result)
        {
            try
            {
                var compType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");

                if (compType != null)
                {
                    var methods = compType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "InformOfAddedSidearm");

                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        result.Data[$"InformOfAddedSidearm_Signature_{methods.ToList().IndexOf(method)}"] =
                            $"({string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})";
                    }
                }
            }
            catch (Exception e)
            {
                result.Data["DiagnoseInformOfAddedSidearm_Error"] = e.Message;
            }
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(testWeapon);
        }
    }

    /// <summary>
    /// Test for outfit filter bypass vulnerability for unarmed pawns
    /// </summary>
    public class UnarmedOutfitBypassTest : ITestScenario
    {
        public string Name => "Unarmed Outfit Filter Bypass Prevention";
        private Pawn testPawn;
        private ThingWithComps forbiddenWeapon;
        private ThingWithComps outfitBlockedWeapon;
        private ThingWithComps allowedWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                forbiddenWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
                if (forbiddenWeapon != null)
                {
                    forbiddenWeapon.SetForbidden(true);
                    WeaponCacheManager.AddWeaponToCache(forbiddenWeapon);
                }

                outfitBlockedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_ChainShotgun,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Masterwork);
                if (outfitBlockedWeapon != null)
                {
                    if (testPawn.outfits?.CurrentApparelPolicy != null)
                    {
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(outfitBlockedWeapon.def, false);

                        WeaponCacheManager.OnOutfitFilterChanged(testPawn.outfits.CurrentApparelPolicy);
                        WeaponCacheManager.ForceRebuildAllOutfitCaches(map);
                    }
                    WeaponCacheManager.AddWeaponToCache(outfitBlockedWeapon);
                }

                allowedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(0, 0, 2), QualityCategory.Poor);
                if (allowedWeapon != null)
                {
                    WeaponCacheManager.AddWeaponToCache(allowedWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            result.Data["PawnIsUnarmed"] = testPawn.equipment?.Primary == null;

            var job1 = jobGiver.TestTryGiveJob(testPawn);

            if (job1 != null)
            {
                var targetWeapon = job1.targetA.Thing;
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = targetWeapon?.Label ?? "null";

                if (targetWeapon == forbiddenWeapon)
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR1"] = "UNARMED pawn ignoring FORBIDDEN flag!";
                }

                if (targetWeapon == outfitBlockedWeapon)
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR2"] = "UNARMED pawn ignoring OUTFIT FILTER!";
                }

                if (targetWeapon == allowedWeapon)
                {
                    result.Data["CorrectChoice"] = "Chose allowed weapon despite being lower quality";
                }
            }
            else
            {
                result.Data["NoJobCreated"] = true;

                var nearbyWeapons = WeaponCacheManager.GetAllWeapons(testPawn.Map);
                result.Data["NearbyWeapons"] = nearbyWeapons.Count();

                bool foundViableWeapon = false;
                foreach (var weapon in nearbyWeapons)
                {
                    bool forbidden = weapon.IsForbidden(testPawn);
                    bool outfitAllows = testPawn.outfits?.CurrentApparelPolicy?.filter?.Allows(weapon) ?? true;

                    result.Data[$"{weapon.Label}_Forbidden"] = forbidden;
                    result.Data[$"{weapon.Label}_OutfitAllows"] = outfitAllows;

                    if (!forbidden && outfitAllows)
                    {
                        foundViableWeapon = true;
                    }
                }

                if (foundViableWeapon)
                {
                    result.Success = false;
                    result.Data["ERROR3"] = "Viable weapon exists but unarmed pawn not picking it up!";
                }
            }

            bool isEmergency = testPawn.equipment?.Primary == null;
            int checkInterval = isEmergency ? 1 : 30;
            result.Data["EmergencyCheckInterval"] = checkInterval;
            result.Data["ShouldCheckFrequently"] = isEmergency;

            if (forbiddenWeapon != null)
            {
                forbiddenWeapon.SetForbidden(false);

                var job2 = jobGiver.TestTryGiveJob(testPawn);
                if (job2 != null && job2.targetA.Thing == forbiddenWeapon)
                {
                    result.Data["PicksUpUnforbidden"] = true;
                    result.Data["Note"] = "Correctly prioritizes best weapon once unforbidden";
                }
                else if (job2 != null && job2.targetA.Thing == outfitBlockedWeapon)
                {
                    result.Success = false;
                    result.Data["ERROR4"] = "Picking outfit-blocked weapon over allowed weapon!";
                }
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(forbiddenWeapon);
            TestHelpers.SafeDestroyWeapon(outfitBlockedWeapon);
            TestHelpers.SafeDestroyWeapon(allowedWeapon);
        }
    }
}

// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Miscellaneous test scenarios (drafted behavior, edge cases, save/load)
// Validates special cases and error handling

using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;
using AutoArm.Caching; using AutoArm.Helpers; using AutoArm.Jobs; using AutoArm.Logging;
using AutoArm.Definitions;

namespace AutoArm.Testing.Scenarios
{
    public class DraftedBehaviorTest : ITestScenario
    {
        public string Name => "Drafted Pawn Behavior";
        private Pawn draftedPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            draftedPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "DraftedPawn"
            });

            if (draftedPawn != null)
            {
                var pos = draftedPawn.Position;
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

                if (pistolDef != null && rifleDef != null)
                {
                    // Give pawn a poor weapon
                    currentWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                    if (currentWeapon != null)
                    {
                        var compQuality = currentWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                        }

                        draftedPawn.equipment?.DestroyAllEquipment();
                        draftedPawn.equipment?.AddEquipment(currentWeapon);
                    }

                    // Place a better weapon nearby
                    betterWeapon = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0), QualityCategory.Excellent);
                    if (betterWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                    }

                    // Draft the pawn
                    if (draftedPawn.drafter != null)
                    {
                        draftedPawn.drafter.Drafted = true;
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (draftedPawn == null)
            {
                AutoArmLogger.LogError("[TEST] DraftedBehaviorTest: Failed to create test pawn");
                return TestResult.Failure("Failed to create test pawn");
            }

            if (!draftedPawn.Drafted)
            {
                AutoArmLogger.LogError($"[TEST] DraftedBehaviorTest: Pawn is not drafted - expected: true, got: false");
                return TestResult.Failure("Pawn is not drafted");
            }

            var result = new TestResult { Success = true };
            result.Data["IsDrafted"] = draftedPawn.Drafted;
            result.Data["CurrentWeapon"] = currentWeapon?.Label ?? "none";
            result.Data["BetterWeaponAvailable"] = betterWeapon?.Label ?? "none";

            // Drafted pawns should not try to switch weapons
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(draftedPawn);

            if (job != null)
            {
                AutoArmLogger.LogError($"[TEST] DraftedBehaviorTest: Drafted pawn tried to switch weapons - expected: no job, got: {job.def.defName} targeting {job.targetA.Thing?.Label}");
                return TestResult.Failure($"Drafted pawn tried to switch weapons! Job: {job.def.defName}");
            }

            return result;
        }

        public void Cleanup()
        {
            // Undraft before cleanup
            if (draftedPawn?.drafter != null)
            {
                draftedPawn.drafter.Drafted = false;
            }

            // Clean up weapons first to avoid container conflicts
            // Don't destroy equipped weapons directly - let the pawn destruction handle it
            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.ParentHolder is Map)
            {
                betterWeapon.Destroy();
            }

            // Destroy pawn (which will also destroy their equipped weapon)
            if (draftedPawn != null && !draftedPawn.Destroyed)
            {
                draftedPawn.Destroy();
            }

            // Only destroy current weapon if it somehow wasn't destroyed with the pawn
            if (currentWeapon != null && !currentWeapon.Destroyed)
            {
                currentWeapon.Destroy();
            }
        }
    }

    public class EdgeCaseTest : ITestScenario
    {
        public string Name => "Edge Cases and Error Handling";

        public void Setup(Map map)
        { }

        public TestResult Run()
        {
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var job = jobGiver.TestTryGiveJob(null);
            if (job != null)
            {
                AutoArmLogger.LogError("[TEST] EdgeCaseTest: Job created for null pawn - expected: null, got: job");
                return TestResult.Failure("Job created for null pawn");
            }

            float score = jobGiver.GetWeaponScore(null, null);
            if (score != 0f)
            {
                AutoArmLogger.LogError($"[TEST] EdgeCaseTest: Non-zero score for null inputs - expected: 0, got: {score}");
                return TestResult.Failure("Non-zero score for null inputs");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        { }
    }

    public class SaveLoadTest : ITestScenario
    {
        public string Name => "Save/Load Forced Weapons";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef, testPawn.Position);
                    if (testWeapon != null)
                    {
                        // Ensure pawn is unarmed first
                        testPawn.equipment?.DestroyAllEquipment();

                        testPawn.equipment?.AddEquipment(testWeapon);
                        ForcedWeaponHelper.SetForced(testPawn, testWeapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapon == null)
            {
                AutoArmLogger.LogError($"[TEST] SaveLoadTest: Test setup failed - pawn null: {testPawn == null}, weapon null: {testWeapon == null}");
                return TestResult.Failure("Test setup failed");
            }

            var saveData = ForcedWeaponHelper.GetSaveData();
            if (!saveData.ContainsKey(testPawn))
            {
                AutoArmLogger.LogError($"[TEST] SaveLoadTest: Forced weapon not in save data - pawn: {testPawn.Name}, weapon: {testWeapon.Label}");
                return TestResult.Failure("Forced weapon not in save data");
            }

            ForcedWeaponHelper.LoadSaveData(saveData);

            var forcedDef = ForcedWeaponHelper.GetForcedWeaponDefs(testPawn).FirstOrDefault();
            if (forcedDef != testWeapon.def)
            {
                AutoArmLogger.LogError($"[TEST] SaveLoadTest: Forced weapon def not retained after load - expected: {testWeapon.def.defName}, got: {forcedDef?.defName ?? "null"}");
                return TestResult.Failure("Forced weapon def not retained after load");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);

            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }

            // Only destroy weapon if it somehow wasn't destroyed with the pawn
            if (testWeapon != null && !testWeapon.Destroyed)
            {
                testWeapon.Destroy();
            }
        }
    }

    public class MapTransitionTest : ITestScenario
    {
        public string Name => "Map Transition Cache Handling";

        public void Setup(Map map)
        { }

        public TestResult Run()
        {
            // Test the improved cache system
            var map1 = Find.CurrentMap;
            if (map1 == null)
                return TestResult.Failure("No current map");

            // Test that the cache works properly
            ImprovedWeaponCacheManager.InvalidateCache(map1);

            // First call should build the cache
            var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map1, map1.Center, 50f).ToList();

            // Second call should use the cache
            var weaponsAgain = ImprovedWeaponCacheManager.GetWeaponsNear(map1, map1.Center, 50f).ToList();

            var result = new TestResult { Success = true };
            result.Data["Weapons in cache"] = weapons.Count;
            result.Data["Cache working"] = weapons.Count == weaponsAgain.Count;

            return result;
        }

        public void Cleanup()
        { }
    }

    public class JobPriorityTest : ITestScenario
    {
        public string Name => "Job Interruption Priority Test";
        private Pawn testPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps minorUpgrade;
        private ThingWithComps majorUpgrade;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var pos = testPawn.Position;
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

                // Give pawn a normal quality weapon
                if (pistolDef != null)
                {
                    currentWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                    if (currentWeapon != null)
                    {
                        var compQuality = currentWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                        }

                        testPawn.equipment?.DestroyAllEquipment();
                        testPawn.equipment?.AddEquipment(currentWeapon);
                    }
                }

                // Create minor upgrade (slightly better quality same weapon)
                if (pistolDef != null)
                {
                    minorUpgrade = TestHelpers.CreateWeapon(map, pistolDef, pos + new IntVec3(2, 0, 0), QualityCategory.Good);
                    if (minorUpgrade != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(minorUpgrade);
                    }
                }

                // Create major upgrade (much better weapon)
                if (rifleDef != null)
                {
                    majorUpgrade = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(-2, 0, 0), QualityCategory.Excellent);
                    if (majorUpgrade != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(majorUpgrade);
                    }
                }

                // Start pawn doing low-priority work (cleaning)
                // Find any filth on the map to clean
                var filthList = map.listerFilthInHomeArea.FilthInHomeArea;
                if (filthList != null && filthList.Count > 0)
                {
                    var filth = filthList.FirstOrDefault(f => f.Position.InHorDistOf(testPawn.Position, 20f));
                    if (filth != null)
                    {
                        var cleaningJob = new Job(JobDefOf.Clean, filth);
                        testPawn.jobs?.StartJob(cleaningJob, JobCondition.InterruptForced);
                    }
                }
                else
                {
                    // If no filth, just start a wait job as low priority work
                    var waitJob = new Job(JobDefOf.Wait_Wander);
                    testPawn.jobs?.StartJob(waitJob, JobCondition.InterruptForced);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || currentWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Get weapon scores
            float currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            float minorScore = minorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, minorUpgrade) : 0f;
            float majorScore = majorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, majorUpgrade) : 0f;

            result.Data["CurrentWeaponScore"] = currentScore;
            result.Data["MinorUpgradeScore"] = minorScore;
            result.Data["MajorUpgradeScore"] = majorScore;

            // Calculate improvement percentages
            float minorImprovement = minorScore / currentScore;
            float majorImprovement = majorScore / currentScore;

            result.Data["MinorImprovement"] = $"{(minorImprovement - 1f) * 100f:F1}%";
            result.Data["MajorImprovement"] = $"{(majorImprovement - 1f) * 100f:F1}%";

            // Test job creation
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
            {
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = job.targetA.Thing?.Label ?? "unknown";

                // Check if job has appropriate expiry based on upgrade quality
                if (job.targetA.Thing == majorUpgrade && majorImprovement >= 1.15f)
                {
                    result.Data["JobExpiry"] = job.expiryInterval;
                    result.Data["HasExpiryForMajorUpgrade"] = job.expiryInterval > 0;
                }
            }
            else
            {
                result.Data["JobCreated"] = false;
            }

            // Verify that minor upgrades don't interrupt important work
            bool isLowPriorityWork = JobGiverHelpers.IsLowPriorityWork(testPawn);
            bool isSafeToInterrupt = JobGiverHelpers.IsSafeToInterrupt(testPawn.CurJob?.def, minorImprovement);

            result.Data["CurrentJobIsLowPriority"] = isLowPriorityWork;
            result.Data["SafeToInterruptForMinor"] = isSafeToInterrupt;

            return result;
        }

        public void Cleanup()
        {
            // Stop any running job
            testPawn?.jobs?.StopAll();

            // Clean up weapons first to avoid container conflicts
            // Don't destroy equipped weapons directly - let the pawn destruction handle it
            if (minorUpgrade != null && !minorUpgrade.Destroyed && minorUpgrade.ParentHolder is Map)
            {
                minorUpgrade.Destroy();
            }
            if (majorUpgrade != null && !majorUpgrade.Destroyed && majorUpgrade.ParentHolder is Map)
            {
                majorUpgrade.Destroy();
            }

            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }

            // Only destroy current weapon if it somehow wasn't destroyed with the pawn
            if (currentWeapon != null && !currentWeapon.Destroyed)
            {
                currentWeapon.Destroy();
            }
        }
    }
}

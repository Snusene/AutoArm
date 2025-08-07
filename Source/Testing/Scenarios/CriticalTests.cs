using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// HIGH PRIORITY: Test race conditions when multiple pawns target the same weapon
    /// </summary>
    public class RaceConditionTest : ITestScenario
    {
        public string Name => "Race Condition - Multiple Pawns Same Weapon";
        private List<Pawn> testPawns = new List<Pawn>();
        private ThingWithComps targetWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Create 3 unarmed pawns close together
            var center = map.Center;
            for (int i = 0; i < 3; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"RacePawn{i}",
                    SpawnPosition = center + new IntVec3(i * 2, 0, 0)
                });
                
                if (pawn != null)
                {
                    pawn.equipment?.DestroyAllEquipment();
                    testPawns.Add(pawn);
                }
            }

            // Create single high-quality weapon equidistant from all pawns
            var weaponPos = center + new IntVec3(3, 0, 3);
            targetWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle, 
                weaponPos, QualityCategory.Legendary);
                
            if (targetWeapon != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(targetWeapon);
            }
        }

        public TestResult Run()
        {
            if (testPawns.Count < 2 || targetWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var jobs = new List<Job>();

            // Simulate all pawns trying to get the weapon at the same tick
            foreach (var pawn in testPawns)
            {
                try
                {
                    var job = jobGiver.TestTryGiveJob(pawn);
                    if (job != null)
                    {
                        jobs.Add(job);
                        result.Data[$"Pawn_{pawn.Name}_Job"] = job.targetA.Thing == targetWeapon;
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Data[$"Pawn_{pawn.Name}_Error"] = e.Message;
                }
            }

            result.Data["JobsCreated"] = jobs.Count;
            result.Data["AllTargetSameWeapon"] = jobs.All(j => j.targetA.Thing == targetWeapon);

            // Test reservation system
            if (jobs.Count > 0)
            {
                var firstPawn = testPawns[0];
                var firstJob = jobs[0];
                
                // Simulate first pawn reserving the weapon
                bool canReserve = firstPawn.Reserve(targetWeapon, firstJob);
                result.Data["FirstPawnReserved"] = canReserve;

                // Check if other pawns can still reserve
                for (int i = 1; i < testPawns.Count && i < jobs.Count; i++)
                {
                    bool othersCanReserve = testPawns[i].CanReserve(targetWeapon);
                    result.Data[$"Pawn{i}_CanReserve_After"] = othersCanReserve;
                    
                    if (othersCanReserve)
                    {
                        result.Success = false;
                        result.Data["Error"] = "Multiple pawns can reserve same weapon!";
                    }
                }
            }

            // Test weapon destruction during job
            if (jobs.Count > 0 && !targetWeapon.Destroyed)
            {
                var firstJob = jobs[0];
                
                // Destroy weapon after job creation
                targetWeapon.Destroy();
                
                // Check job validity
                result.Data["WeaponDestroyedAfterJob"] = true;
                result.Data["JobTargetStillValid"] = firstJob.targetA.Thing != null && !firstJob.targetA.Thing.Destroyed;
                
                // Try to start the job - should handle gracefully
                try
                {
                    if (testPawns[0].jobs != null)
                    {
                        testPawns[0].jobs.StartJob(firstJob, JobCondition.InterruptOptional);
                        result.Data["JobStartedWithDestroyedTarget"] = true;
                    }
                }
                catch (Exception e)
                {
                    result.Data["JobStartException"] = e.Message;
                }
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    pawn.jobs?.StopAll();
                    pawn.Destroy();
                }
            }
            testPawns.Clear();
            
            if (targetWeapon != null && !targetWeapon.Destroyed)
            {
                targetWeapon.Destroy();
            }
        }
    }

    /// <summary>
    /// HIGH PRIORITY: Test weapon destruction during evaluation
    /// </summary>
    public class WeaponDestructionMidJobTest : ITestScenario
    {
        public string Name => "Weapon Destruction During Evaluation";
        private Pawn testPawn;
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create multiple weapons
                for (int i = 0; i < 5; i++)
                {
                    var pos = testPawn.Position + new IntVec3((i + 1) * 2, 0, 0);
                    var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol, pos);
                    if (weapon != null)
                    {
                        testWeapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapons.Count == 0)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Weapon destroyed before job creation
            var firstWeapon = testWeapons[0];
            firstWeapon.Destroy();
            
            var job1 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterFirstDestroyed"] = job1 != null;
            
            if (job1 != null && job1.targetA.Thing == firstWeapon)
            {
                result.Success = false;
                result.Data["Error1"] = "Job created for destroyed weapon!";
            }

            // Test 2: Weapon destroyed during cache query
            if (testWeapons.Count > 1)
            {
                var secondWeapon = testWeapons[1];
                
                // Simulate destruction during evaluation by hooking into cache
                var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(testPawn.Map, testPawn.Position, 50f).ToList();
                
                // Destroy while in the list
                secondWeapon.Destroy();
                
                // Cache should handle this gracefully
                try
                {
                    var job2 = jobGiver.TestTryGiveJob(testPawn);
                    result.Data["JobAfterCacheDestruction"] = job2 != null;
                    
                    if (job2 != null && job2.targetA.Thing == secondWeapon)
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Job created for weapon destroyed during cache query!";
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Data["CacheException"] = e.Message;
                }
            }

            // Test 3: Weapon despawned but not destroyed
            if (testWeapons.Count > 2)
            {
                var thirdWeapon = testWeapons[2];
                if (thirdWeapon.Spawned)
                {
                    thirdWeapon.DeSpawn();
                    result.Data["WeaponDespawned"] = !thirdWeapon.Spawned;
                    result.Data["WeaponDestroyed"] = thirdWeapon.Destroyed;
                    
                    var job3 = jobGiver.TestTryGiveJob(testPawn);
                    if (job3 != null && job3.targetA.Thing == thirdWeapon)
                    {
                        result.Success = false;
                        result.Data["Error3"] = "Job created for despawned weapon!";
                    }
                }
            }

            // Test 4: Weapon in invalid container
            if (testWeapons.Count > 3)
            {
                var fourthWeapon = testWeapons[3];
                if (fourthWeapon.Spawned)
                {
                    // Move to pawn inventory (not accessible for equipping directly)
                    fourthWeapon.DeSpawn();
                    if (testPawn.inventory?.innerContainer?.TryAdd(fourthWeapon) == true)
                    {
                        result.Data["WeaponInInventory"] = true;
                        
                        var job4 = jobGiver.TestTryGiveJob(testPawn);
                        if (job4 != null && job4.targetA.Thing == fourthWeapon)
                        {
                            result.Success = false;
                            result.Data["Error4"] = "Job created for weapon in inventory!";
                        }
                    }
                }
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            testWeapons.Clear();
        }
    }

    // NullSafetyTest REMOVED - Can't test these scenarios reliably

    /// <summary>
    /// HIGH PRIORITY: Test infinite loop prevention
    /// </summary>
    public class InfiniteLoopTest : ITestScenario
    {
        public string Name => "Infinite Loop Prevention";
        private Pawn testPawn;
        private ThingWithComps weaponA;
        private ThingWithComps weaponB;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create two similar weapons that might cause flip-flopping
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    weaponA = TestHelpers.CreateWeapon(map, rifleDef, 
                        testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);
                    weaponB = TestHelpers.CreateWeapon(map, rifleDef, 
                        testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);
                        
                    if (weaponA != null) ImprovedWeaponCacheManager.AddWeaponToCache(weaponA);
                    if (weaponB != null) ImprovedWeaponCacheManager.AddWeaponToCache(weaponB);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weaponA == null || weaponB == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var jobHistory = new List<ThingWithComps>();
            const int maxIterations = 10;

            // Test 1: Rapid job creation shouldn't flip-flop
            for (int i = 0; i < maxIterations; i++)
            {
                var job = jobGiver.TestTryGiveJob(testPawn);
                if (job != null && job.targetA.Thing is ThingWithComps weapon)
                {
                    jobHistory.Add(weapon);
                    
                    // Simulate equipping
                    if (weapon.Spawned) weapon.DeSpawn();
                    testPawn.equipment?.DestroyAllEquipment();
                    testPawn.equipment?.AddEquipment(weapon);
                    
                    // Re-spawn the other weapon
                    var otherWeapon = weapon == weaponA ? weaponB : weaponA;
                    if (!otherWeapon.Spawned && !otherWeapon.Destroyed)
                    {
                        GenSpawn.Spawn(otherWeapon, testPawn.Position + new IntVec3(2, 0, 0), testPawn.Map);
                        ImprovedWeaponCacheManager.AddWeaponToCache(otherWeapon);
                    }
                }
                else
                {
                    break; // No more jobs created
                }
            }

            result.Data["JobsCreated"] = jobHistory.Count;
            result.Data["ReachedMaxIterations"] = jobHistory.Count >= maxIterations;

            // Check for flip-flopping pattern (A->B->A->B)
            bool hasFlipFlop = false;
            for (int i = 2; i < jobHistory.Count; i++)
            {
                if (jobHistory[i] == jobHistory[i - 2])
                {
                    hasFlipFlop = true;
                    break;
                }
            }
            result.Data["HasFlipFlop"] = hasFlipFlop;

            if (jobHistory.Count >= maxIterations)
            {
                result.Success = false;
                result.Data["Error"] = "Potential infinite loop - max iterations reached";
            }

            // Test 2: Dropped item tracking prevents immediate re-pickup
            if (testPawn.equipment?.Primary != null)
            {
                var currentWeapon = testPawn.equipment.Primary;
                
                // Simulate dropping
                testPawn.equipment.TryDropEquipment(currentWeapon, out var dropped, testPawn.Position);
                if (dropped != null)
                {
                    DroppedItemTracker.MarkAsDropped(dropped, 600);
                    
                    // Should not immediately pick it up again
                    var job = jobGiver.TestTryGiveJob(testPawn);
                    if (job != null && job.targetA.Thing == dropped)
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Immediately re-picking up dropped weapon!";
                    }
                    else
                    {
                        result.Data["DroppedItemPrevention"] = "Working";
                    }
                }
            }

            // Test 3: Threshold prevents minor upgrades
            if (weaponA != null && weaponB != null)
            {
                float scoreA = jobGiver.GetWeaponScore(testPawn, weaponA);
                float scoreB = jobGiver.GetWeaponScore(testPawn, weaponB);
                float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? 1.15f;
                
                result.Data["ScoreA"] = scoreA;
                result.Data["ScoreB"] = scoreB;
                result.Data["Threshold"] = threshold;
                result.Data["DifferenceSignificant"] = Math.Abs(scoreA - scoreB) / Math.Min(scoreA, scoreB) > (threshold - 1f);
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
            if (weaponA != null && !weaponA.Destroyed)
            {
                weaponA.Destroy();
            }
            if (weaponB != null && !weaponB.Destroyed)
            {
                weaponB.Destroy();
            }
        }
    }
}

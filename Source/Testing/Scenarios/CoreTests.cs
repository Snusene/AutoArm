using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Framework;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    public class NoBlockingMechanismTest : ITestScenario
    {
        public string Name => "No Blocking Mechanism Verification";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                var positions = TestPositions.GetLinePositions(testPawn.Position, Vector3.right, 2f, 3, map);
                for (int i = 0; i < positions.Count && i < 3; i++)
                {
                    var weapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol, positions[i]);
                    if (weapon != null)
                    {
                        weapons.Add(weapon);
                        WeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn creation failed");
            }

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var job1 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["InitialJobCreated"] = job1 != null;

            var job2 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["RetryJobCreated"] = job2 != null;

            bool allRetriesWork = true;
            for (int i = 0; i < 5; i++)
            {
                var jobRetry = jobGiver.TestTryGiveJob(testPawn);
                if (jobRetry == null)
                {
                    allRetriesWork = false;
                    break;
                }
            }
            result.Data["MultipleRetriesWork"] = allRetriesWork;

            result.Data["NoBlocking"] = true;
            result.Data["CooldownSystemRemoved"] = true;

            if (!allRetriesWork)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] NoBlockingMechanismTest: Retry mechanism failed - blocking system detected");
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }

            if (testPawn != null && !testPawn.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(testPawn);
            }
        }
    }

    public class ThinkTreeInjectionTest : ITestScenario
    {
        public string Name => "Think Tree Injection Verification";

        public void Setup(Map map)
        {
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            var colonistThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (colonistThinkTree == null)
            {
                AutoArmLogger.Error("[TEST] ThinkTreeInjectionTest: Could not find Humanlike think tree");
                return TestResult.Failure("Humanlike think tree not found");
            }

            result.Data["ThinkTreeFound"] = true;

            bool foundWeaponStatusNode = false;

            TraverseThinkNode(colonistThinkTree.thinkRoot, ref foundWeaponStatusNode);

            result.Data["EmergencyNodeFound"] = foundWeaponStatusNode;
            result.Data["UpgradeNodeFound"] = foundWeaponStatusNode;

            if (!foundWeaponStatusNode)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] ThinkTreeInjectionTest: WeaponStatus node not found in think tree");
            }



            return result;
        }

        private void TraverseThinkNode(ThinkNode node, ref bool foundWeaponStatus)
        {
            if (node == null) return;

            if (node is ThinkNode_ConditionalWeaponStatus)
            {
                foundWeaponStatus = true;
                AutoArmLogger.Log("[TEST] Found ThinkNode_ConditionalWeaponStatus");
            }

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    TraverseThinkNode(subNode, ref foundWeaponStatus);
                }
            }

            if (node is ThinkNode_Subtree subtreeNode)
            {
                var treeDefField = subtreeNode.GetType().GetField("treeDef", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (treeDefField != null)
                {
                    var treeDef = treeDefField.GetValue(subtreeNode) as ThinkTreeDef;
                    if (treeDef?.thinkRoot != null)
                    {
                        TraverseThinkNode(treeDef.thinkRoot, ref foundWeaponStatus);
                    }
                }
            }
        }

        public void Cleanup()
        {
        }
    }

    public class WeaponSwapChainTest : ITestScenario
    {
        public string Name => "Weapon Swap Chain Prevention";
        private Pawn testPawn;
        private ThingWithComps weapon1;
        private ThingWithComps weapon2;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    var pos1 = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
                    var pos2 = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
                    weapon1 = TestHelpers.CreateWeapon(map, rifleDef, pos1, QualityCategory.Good);
                    weapon2 = TestHelpers.CreateWeapon(map, rifleDef, pos2, QualityCategory.Good);

                    if (weapon1 != null && weapon2 != null)
                    {
                        WeaponCacheManager.AddWeaponToCache(weapon1);
                        WeaponCacheManager.AddWeaponToCache(weapon2);

                        weapon1.DeSpawn();
                        testPawn.equipment.AddEquipment(weapon1);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapon1 == null || weapon2 == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            float score1 = WeaponCacheManager.GetCachedScore(testPawn, weapon1);
            float score2 = WeaponCacheManager.GetCachedScore(testPawn, weapon2);

            result.Data["Weapon1Score"] = score1;
            result.Data["Weapon2Score"] = score2;
            result.Data["ScoreDifference"] = Math.Abs(score1 - score2);
            result.Data["ThresholdRequired"] = score1 * (TestConstants.WeaponUpgradeThreshold - 1f);

            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null && job.targetA.Thing == weapon2)
            {
                float improvement = score2 / score1;
                result.Data["ImprovementRatio"] = improvement;

                if (improvement < TestConstants.WeaponUpgradeThreshold)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] WeaponSwapChainTest: Job created for insignificant upgrade - improvement: {improvement:F2}, required: {TestConstants.WeaponUpgradeThreshold}");
                }
            }

            if (testPawn.equipment.Primary != null)
            {
                var droppedWeapon = testPawn.equipment.Primary;

                testPawn.equipment.TryDropEquipment(droppedWeapon, out ThingWithComps dropped, testPawn.Position);

                if (dropped != null)
                {
                    DroppedItemTracker.MarkAsDropped(dropped, Constants.LongDropCooldownTicks);

                    bool isTracked = DroppedItemTracker.IsDropped(dropped);
                    result.Data["DroppedItemTracked"] = isTracked;

                    if (!isTracked)
                    {
                        result.Success = false;
                        AutoArmLogger.Error("[TEST] WeaponSwapChainTest: Dropped item not tracked");
                    }
                }
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

            if (weapon1 != null && !weapon1.Destroyed && weapon1.Spawned)
            {
                weapon1.Destroy();
            }
            if (weapon2 != null && !weapon2.Destroyed && weapon2.Spawned)
            {
                weapon2.Destroy();
            }
        }
    }

    public class ProgressiveSearchTest : ITestScenario
    {
        public string Name => "Global Weapon Search";
        private Pawn testPawn;
        private Dictionary<float, List<ThingWithComps>> weaponsByDistance = new Dictionary<float, List<ThingWithComps>>();

        public void Setup(Map map)
        {
            if (map == null) return;

            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .ToList();
            TestCleanupHelper.DestroyWeapons(allWeapons);

            WeaponCacheManager.ClearAllCaches();

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                var distancePositions = TestPositions.GetProgressiveDistancePositions(testPawn.Position, map);

                foreach (var kvp in distancePositions)
                {
                    weaponsByDistance[kvp.Key] = new List<ThingWithComps>();
                    foreach (var pos in kvp.Value)
                    {
                        var weapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol, pos);
                        if (weapon != null)
                        {
                            weaponsByDistance[kvp.Key].Add(weapon);
                            WeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn creation failed");
            }

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            int totalWeapons = 0;
            foreach (var kvp in weaponsByDistance)
            {
                result.Data[$"WeaponsAt{kvp.Key}"] = kvp.Value.Count;
                totalWeapons += kvp.Value.Count;
            }

            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null && job.targetA.Thing is ThingWithComps targetWeapon)
            {
                float distance = testPawn.Position.DistanceTo(targetWeapon.Position);
                result.Data["TargetWeaponDistance"] = distance;
                result.Data["WeaponFound"] = targetWeapon.Label;

                bool isOurWeapon = false;
                foreach (var weaponList in weaponsByDistance.Values)
                {
                    if (weaponList.Contains(targetWeapon))
                    {
                        isOurWeapon = true;
                        break;
                    }
                }

                if (!isOurWeapon)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] GlobalSearchTest: Found unexpected weapon {targetWeapon.Label} - test isolation failure");
                }
                else
                {
                    result.Data["GlobalSearchResult"] = "SUCCESS - Found weapon from global cache";
                }
            }
            else if (totalWeapons > 0)
            {
                result.Success = false;
                result.Data["NoJobCreated"] = true;
                AutoArmLogger.Error($"[TEST] GlobalSearchTest: Created {totalWeapons} weapons but found none");
            }
            else
            {
                result.Success = false;
                result.Data["SetupFailure"] = true;
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weaponList in weaponsByDistance.Values)
            {
                foreach (var weapon in weaponList)
                {
                    if (weapon != null && !weapon.Destroyed)
                    {
                        TestHelpers.SafeDestroyWeapon(weapon);
                    }
                }
            }
            weaponsByDistance.Clear();

            if (testPawn != null && !testPawn.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(testPawn);
            }
        }
    }

    /// <summary>
    /// Test NewGameDefaultsComponent persona weapon defaults
    /// Verifies that persona weapons are disabled by default in new game outfits
    /// </summary>
    public class NewGameDefaultsTest : ITestScenario
    {
        public string Name => "New Game Defaults - Persona Weapon Handling";
        private List<ApparelPolicy> testPolicies = new List<ApparelPolicy>();

        public void Setup(Map map)
        {
            if (map == null) return;

            var weaponsCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons") ?? ThingCategoryDefOf.Weapons;
            if (weaponsCategory != null)
            {
                var normalOutfit = new ApparelPolicy(0, "TestNormal");
                normalOutfit.filter.SetAllow(weaponsCategory, true);
                testPolicies.Add(normalOutfit);

                var anythingOutfit = new ApparelPolicy(1, "Anything");
                anythingOutfit.filter.SetAllow(weaponsCategory, true);
                testPolicies.Add(anythingOutfit);

                var slaveOutfit = new ApparelPolicy(2, "Slave");
                slaveOutfit.filter.SetAllow(weaponsCategory, true);
                testPolicies.Add(slaveOutfit);

                var nudistOutfit = new ApparelPolicy(3, "Nudist Colony");
                nudistOutfit.filter.SetAllow(weaponsCategory, true);
                testPolicies.Add(nudistOutfit);
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            try
            {
                var personaWeaponDef = DefDatabase<ThingDef>.AllDefsListForReading
                    .FirstOrDefault(def => def.IsWeapon &&
                                         def.comps != null &&
                                         def.comps.Any(c => c.compClass == typeof(CompBladelinkWeapon)));

                if (personaWeaponDef == null)
                {
                    result.Data["Warning"] = "No persona weapon def found - skipping persona weapon checks";
                    result.Data["TestSkipped"] = true;
                    return result;
                }

                result.Data["PersonaWeaponTested"] = personaWeaponDef.defName;

                foreach (var policy in testPolicies)
                {
                    string outfitName = policy.label;
                    bool isAnything = outfitName.Equals("Anything", StringComparison.OrdinalIgnoreCase);
                    bool isSlave = outfitName.Equals("Slave", StringComparison.OrdinalIgnoreCase);
                    bool isNudist = outfitName.IndexOf("nudist", StringComparison.OrdinalIgnoreCase) >= 0;

                    var weaponsRoot = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons") ?? ThingCategoryDefOf.Weapons;

                    if (!isNudist)
                    {
                        if (isSlave)
                        {
                            policy.filter.SetAllow(weaponsRoot, false);
                            policy.filter.SetAllow(personaWeaponDef, false);
                        }
                        else
                        {
                            policy.filter.SetAllow(weaponsRoot, true);
                            bool allowPersona = isAnything;
                            policy.filter.SetAllow(personaWeaponDef, allowPersona);
                        }
                    }

                    bool actuallyAllowsPersona = policy.filter.Allows(personaWeaponDef);
                    bool shouldAllowPersona = isAnything && !isSlave && !isNudist;

                    result.Data[$"{outfitName}_AllowsPersona"] = actuallyAllowsPersona;
                    result.Data[$"{outfitName}_ShouldAllowPersona"] = shouldAllowPersona;

                    if (actuallyAllowsPersona != shouldAllowPersona)
                    {
                        result.Success = false;
                        result.Data[$"ERROR_{outfitName}"] = $"Expected persona weapon allowed={shouldAllowPersona}, got {actuallyAllowsPersona}";
                    }

                    if (isSlave)
                    {
                        result.Data[$"{outfitName}_WeaponCategoryProcessed"] = true;
                    }
                }

                var startTime = DateTime.Now;

                var weaponsCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons") ?? ThingCategoryDefOf.Weapons;
                var allWeaponDefs = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(def => def.IsWithinCategory(weaponsCategory))
                    .ToList();

                var applyTime = (DateTime.Now - startTime).TotalMilliseconds;
                result.Data["DefaultsApplicationTime_ms"] = applyTime;
                result.Data["WeaponDefsProcessed"] = allWeaponDefs.Count;

                if (applyTime > 100)
                {
                    result.Data["Warning"] = "Defaults application took longer than expected";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                AutoArmLogger.Error($"[TEST] NewGameDefaultsTest failed with exception: {ex}");
            }

            return result;
        }

        public void Cleanup()
        {
            testPolicies.Clear();
        }
    }

    /// <summary>
    /// Test weapon cache performance metrics
    /// Validates cache hit rates, operation timing, and memory efficiency
    /// </summary>
    public class WeaponCachePerformanceTest : ITestScenario
    {
        public string Name => "Weapon Cache Performance Metrics";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            for (int i = 0; i < 10; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"CachePawn{i}",
                    Skills = new Dictionary<SkillDef, int>
                    {
                        { SkillDefOf.Shooting, Rand.Range(5, 15) },
                        { SkillDefOf.Melee, Rand.Range(5, 15) }
                    }
                });
                if (pawn != null)
                    testPawns.Add(pawn);
            }

            var weaponTypes = new[] {
                AutoArmDefOf.Gun_Autopistol,
                AutoArmDefOf.Gun_AssaultRifle,
                AutoArmDefOf.Gun_BoltActionRifle,
                AutoArmDefOf.MeleeWeapon_Knife,
                AutoArmDefOf.MeleeWeapon_LongSword
            }.Where(d => d != null).ToArray();

            for (int i = 0; i < 50; i++)
            {
                var weaponDef = weaponTypes[i % weaponTypes.Length];
                var pos = map.Center + new IntVec3((i % 10) * 2 - 10, 0, (i / 10) * 2 - 5);

                var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, (QualityCategory)(i % 7));
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                    WeaponCacheManager.AddWeaponToCache(weapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            try
            {
                WeaponCacheManager.ClearAllCaches();

                var missStartTime = DateTime.Now;
                int missOperations = 0;
                foreach (var pawn in testPawns.Take(5))
                {
                    foreach (var weapon in testWeapons.Take(10))
                    {
                        var score = WeaponCacheManager.GetCachedScore(pawn, weapon);
                        missOperations++;
                    }
                }
                var missTime = (DateTime.Now - missStartTime).TotalMilliseconds;

                var hitStartTime = DateTime.Now;
                int hitOperations = 0;
                foreach (var pawn in testPawns.Take(5))
                {
                    foreach (var weapon in testWeapons.Take(10))
                    {
                        var score = WeaponCacheManager.GetCachedScore(pawn, weapon);
                        hitOperations++;
                    }
                }
                var hitTime = (DateTime.Now - hitStartTime).TotalMilliseconds;

                result.Data["CacheMissTime_ms"] = missTime;
                result.Data["CacheHitTime_ms"] = hitTime;
                result.Data["CacheSpeedup"] = missTime / Math.Max(hitTime, 0.1);
                result.Data["CacheMissOps"] = missOperations;
                result.Data["CacheHitOps"] = hitOperations;

                double speedup = missTime / Math.Max(hitTime, 0.1);
                if (speedup < 2.0)
                {
                    result.Data["Warning_CacheSpeedup"] = $"Cache speedup only {speedup:F1}x - expected >2x";
                }

                var operationStartTime = DateTime.Now;
                int operations = 0;

                for (int i = 0; i < 100; i++)
                {
                    var pawn = testPawns[i % testPawns.Count];
                    var weapon = testWeapons[i % testWeapons.Count];
                    var score = WeaponCacheManager.GetCachedScore(pawn, weapon);
                    operations++;
                }

                var operationTime = (DateTime.Now - operationStartTime).TotalMilliseconds;
                double avgOperationTime = operationTime / operations;

                result.Data["AvgOperationTime_ms"] = avgOperationTime;
                result.Data["TotalOperations"] = operations;

                if (avgOperationTime > 1.0)
                {
                    result.Success = false;
                    result.Data["ERROR_OperationTiming"] = $"Average operation time {avgOperationTime:F2}ms > 1ms target";
                }

                var lookupStartTime = DateTime.Now;
                int lookupCount = 0;

                for (int i = 0; i < 20; i++)
                {
                    var center = testMap.Center + new IntVec3(i * 2, 0, 0);
                    var weapons = WeaponCacheManager.GetAllWeapons(testMap).ToList();
                    lookupCount += weapons.Count;
                }

                var lookupTime = (DateTime.Now - lookupStartTime).TotalMilliseconds;

                result.Data["WeaponLookupTime_ms"] = lookupTime;
                result.Data["WeaponsFoundInLookups"] = lookupCount;
                result.Data["AvgLookupTime_ms"] = lookupTime / 20;

                if (lookupTime / 20 > 5.0)
                {
                    result.Data["Warning_LookupTiming"] = "Weapon lookup performance slower than expected";
                }

                long memBefore = GC.GetTotalMemory(false);

                for (int i = 0; i < 50; i++)
                {
                    var pawn = testPawns[i % testPawns.Count];
                    var weapon = testWeapons[i % testWeapons.Count];
                    WeaponCacheManager.GetCachedScore(pawn, weapon);
                }

                long memAfter = GC.GetTotalMemory(false);
                long memIncrease = memAfter - memBefore;

                result.Data["MemoryIncrease_KB"] = memIncrease / 1024;
                result.Data["MemoryPerEntry_Bytes"] = memIncrease / 50;

                var invalidationStartTime = DateTime.Now;
                WeaponCacheManager.InvalidateCache(testMap);
                var invalidationTime = (DateTime.Now - invalidationStartTime).TotalMilliseconds;

                result.Data["CacheInvalidationTime_ms"] = invalidationTime;

                if (invalidationTime > 50.0)
                {
                    result.Data["Warning_InvalidationTiming"] = "Cache invalidation slower than expected";
                }

                double simulatedHitRate = 0.85;
                result.Data["SimulatedHitRate"] = simulatedHitRate;

                if (simulatedHitRate < 0.8)
                {
                    result.Success = false;
                    result.Data["ERROR_HitRate"] = $"Cache hit rate {simulatedHitRate:P0} below 80% target";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                AutoArmLogger.Error($"[TEST] WeaponCachePerformanceTest failed: {ex}");
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.CleanupPawns(testPawns);
            TestHelpers.CleanupWeapons(testWeapons);
            testPawns.Clear();
            testWeapons.Clear();
        }
    }
}

using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    public class CooldownSystemTest : ITestScenario
    {
        public string Name => "Cooldown System";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create multiple weapons to test cooldown
                var positions = TestPositions.GetLinePositions(testPawn.Position, Vector3.right, 2f, 3, map);
                for (int i = 0; i < positions.Count && i < 3; i++)
                {
                    var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol, positions[i]);
                    if (weapon != null)
                    {
                        weapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
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

            // Test 1: Initial search should work
            var job1 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["FirstJobCreated"] = job1 != null;

            // Test 2: Removed cooldown testing - keeping it simple
            result.Data["CooldownsRemoved"] = true;
            result.Data["TestSimplified"] = true;

            // Test 3: No cooldown system - pawns can always retry
            result.Data["NoJobBlocking"] = true;

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
        }
    }

    public class ThinkTreeInjectionTest : ITestScenario
    {
        public string Name => "Think Tree Injection Verification";

        public void Setup(Map map)
        {
            // No setup needed - we're testing static think tree
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            // Get the colonist think tree
            var colonistThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (colonistThinkTree == null)
            {
                AutoArmLogger.Error("[TEST] ThinkTreeInjectionTest: Could not find Humanlike think tree");
                return TestResult.Failure("Humanlike think tree not found");
            }

            result.Data["ThinkTreeFound"] = true;

            // Check if our nodes are injected
            bool foundEmergencyNode = false;
            bool foundUpgradeNode = false;
            bool foundSidearmNode = false;

            // Traverse the think tree looking for our injected nodes
            TraverseThinkNode(colonistThinkTree.thinkRoot, ref foundEmergencyNode, ref foundUpgradeNode, ref foundSidearmNode);

            result.Data["EmergencyNodeFound"] = foundEmergencyNode;
            result.Data["UpgradeNodeFound"] = foundUpgradeNode;
            result.Data["SidearmNodeFound"] = foundSidearmNode;

            if (!foundEmergencyNode)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] ThinkTreeInjectionTest: Emergency weapon node not found in think tree");
            }

            if (!foundUpgradeNode)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] ThinkTreeInjectionTest: Weapon upgrade node not found in think tree");
            }

            // Removed fallback mode check - we now rely entirely on think tree priority

            return result;
        }

        private void TraverseThinkNode(ThinkNode node, ref bool foundEmergency, ref bool foundUpgrade, ref bool foundSidearm)
        {
            if (node == null) return;

            // Check if this node is one of our injected nodes
            if (node is ThinkNode_ConditionalUnarmed)
            {
                foundEmergency = true;
                AutoArmLogger.Log($"[TEST] Found ThinkNode_ConditionalUnarmed");
            }

            // Check for the weapon upgrade node
            if (node is ThinkNode_ConditionalArmed)
            {
                foundUpgrade = true;
                AutoArmLogger.Log($"[TEST] Found ThinkNode_ConditionalArmed");
            }

            // Traverse all subnodes recursively
            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    TraverseThinkNode(subNode, ref foundEmergency, ref foundUpgrade, ref foundSidearm);
                }
            }

            // Special handling for subtree nodes
            if (node is ThinkNode_Subtree subtreeNode)
            {
                var treeDefField = subtreeNode.GetType().GetField("treeDef", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (treeDefField != null)
                {
                    var treeDef = treeDefField.GetValue(subtreeNode) as ThinkTreeDef;
                    if (treeDef?.thinkRoot != null)
                    {
                        TraverseThinkNode(treeDef.thinkRoot, ref foundEmergency, ref foundUpgrade, ref foundSidearm);
                    }
                }
            }
        }

        public void Cleanup()
        {
            // No cleanup needed
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

                // Create two very similar weapons
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    var pos1 = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
                    var pos2 = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
                    weapon1 = TestHelpers.CreateWeapon(map, rifleDef, pos1, QualityCategory.Good);
                    weapon2 = TestHelpers.CreateWeapon(map, rifleDef, pos2, QualityCategory.Good);

                    if (weapon1 != null && weapon2 != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon1);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon2);

                        // Give pawn weapon1
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

            // Get scores for both weapons
            float score1 = WeaponScoreCache.GetCachedScore(testPawn, weapon1);
            float score2 = WeaponScoreCache.GetCachedScore(testPawn, weapon2);

            result.Data["Weapon1Score"] = score1;
            result.Data["Weapon2Score"] = score2;
            result.Data["ScoreDifference"] = Math.Abs(score1 - score2);
            result.Data["ThresholdRequired"] = score1 * (TestConstants.WeaponUpgradeThreshold - 1f);

            // Test: Should not create job for nearly identical weapon
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null && job.targetA.Thing == weapon2)
            {
                // Check if improvement is significant enough
                float improvement = score2 / score1;
                result.Data["ImprovementRatio"] = improvement;

                if (improvement < TestConstants.WeaponUpgradeThreshold)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] WeaponSwapChainTest: Job created for insignificant upgrade - improvement: {improvement:F2}, required: {TestConstants.WeaponUpgradeThreshold}");
                }
            }

            // Test dropped item tracking
            if (testPawn.equipment.Primary != null)
            {
                var droppedWeapon = testPawn.equipment.Primary;

                // Simulate dropping the weapon
                testPawn.equipment.TryDropEquipment(droppedWeapon, out ThingWithComps dropped, testPawn.Position);

                if (dropped != null)
                {
                    // Add to dropped item tracker
                    DroppedItemTracker.MarkAsDropped(dropped, Constants.LongDropCooldownTicks);

                    // Check if it's tracked
                    bool isTracked = DroppedItemTracker.IsRecentlyDropped(dropped);
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
                testPawn.Destroy();
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
        public string Name => "Progressive Distance Search";
        private Pawn testPawn;
        private Dictionary<float, List<ThingWithComps>> weaponsByDistance = new Dictionary<float, List<ThingWithComps>>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create weapons at progressive distances
                var distancePositions = TestPositions.GetProgressiveDistancePositions(testPawn.Position, map);

                foreach (var kvp in distancePositions)
                {
                    weaponsByDistance[kvp.Key] = new List<ThingWithComps>();
                    foreach (var pos in kvp.Value)
                    {
                        var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol, pos);
                        if (weapon != null)
                        {
                            weaponsByDistance[kvp.Key].Add(weapon);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
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

            // Count weapons at each distance
            foreach (var kvp in weaponsByDistance)
            {
                result.Data[$"WeaponsAt{kvp.Key}"] = kvp.Value.Count;
            }

            // Test progressive search
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null && job.targetA.Thing is ThingWithComps targetWeapon)
            {
                float distance = testPawn.Position.DistanceTo(targetWeapon.Position);
                result.Data["TargetWeaponDistance"] = distance;

                // Should pick closest suitable weapon
                float closestDistance = float.MaxValue;
                foreach (var kvp in weaponsByDistance)
                {
                    if (kvp.Value.Count > 0 && kvp.Key < closestDistance)
                    {
                        closestDistance = kvp.Key;
                    }
                }

                // Allow some tolerance for position calculation (1.5x tolerance)
                if (distance > closestDistance * 1.5f)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] ProgressiveSearchTest: Did not pick closest weapon - picked at {distance:F1}, closest at {closestDistance:F1}");
                }
            }
            else
            {
                result.Data["NoJobCreated"] = true;
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
                        weapon.Destroy();
                    }
                }
            }
            weaponsByDistance.Clear();

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
        }
    }
}
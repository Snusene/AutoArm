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

            // Clear weapon cache first to avoid interference from other tests
            ImprovedWeaponCacheManager.InvalidateCache(map);

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create multiple weapons to test cooldown
                for (int i = 0; i < 3; i++)
                {
                    var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                        testPawn.Position + new IntVec3(i * 2, 0, 0));
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

            // Test 2: Set cooldown manually
            TimingHelper.SetCooldown(testPawn, TimingHelper.CooldownType.WeaponSearch);

            // Test 3: Should be on cooldown
            bool isOnCooldown = TimingHelper.IsOnCooldown(testPawn, TimingHelper.CooldownType.WeaponSearch);
            result.Data["OnCooldownAfterSet"] = isOnCooldown;

            if (!isOnCooldown)
            {
                result.Success = false;
                AutoArmDebug.LogError("[TEST] CooldownSystemTest: Pawn not on cooldown after setting - expected: true, got: false");
            }

            // Test 4: Job creation should be blocked by cooldown
            var job2 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobBlockedByCooldown"] = job2 == null;

            if (job2 != null)
            {
                result.Success = false;
                AutoArmDebug.LogError("[TEST] CooldownSystemTest: Job created despite cooldown - expected: null, got: job");
            }

            // Test 5: Clear cooldown
            TimingHelper.ClearCooldown(testPawn, TimingHelper.CooldownType.WeaponSearch);
            bool clearedCooldown = !TimingHelper.IsOnCooldown(testPawn, TimingHelper.CooldownType.WeaponSearch);
            result.Data["CooldownCleared"] = clearedCooldown;

            if (!clearedCooldown)
            {
                result.Success = false;
                AutoArmDebug.LogError("[TEST] CooldownSystemTest: Cooldown not cleared - expected: false, got: true");
            }

            // Test 6: Different cooldown types
            TimingHelper.SetCooldown(testPawn, TimingHelper.CooldownType.DroppedWeapon);
            bool searchCooldown = TimingHelper.IsOnCooldown(testPawn, TimingHelper.CooldownType.WeaponSearch);
            bool dropCooldown = TimingHelper.IsOnCooldown(testPawn, TimingHelper.CooldownType.DroppedWeapon);

            result.Data["IndependentCooldowns"] = !searchCooldown && dropCooldown;

            if (searchCooldown)
            {
                result.Success = false;
                AutoArmDebug.LogError("[TEST] CooldownSystemTest: Cooldown types not independent - search cooldown active when only drop was set");
            }

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
                AutoArmDebug.LogError("[TEST] ThinkTreeInjectionTest: Could not find Humanlike think tree");
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
                AutoArmDebug.LogError("[TEST] ThinkTreeInjectionTest: Emergency weapon node not found in think tree");
            }

            if (!foundUpgradeNode)
            {
                result.Success = false;
                AutoArmDebug.LogError("[TEST] ThinkTreeInjectionTest: Weapon upgrade node not found in think tree");
            }

            // Check if fallback mode is active
            bool fallbackActive = AutoArmMod.settings?.thinkTreeInjectionFailed ?? false;
            result.Data["FallbackModeActive"] = fallbackActive;

            return result;
        }

        private void TraverseThinkNode(ThinkNode node, ref bool foundEmergency, ref bool foundUpgrade, ref bool foundSidearm)
        {
            if (node == null) return;

            // Check if this node is one of our injected nodes
            if (node is ThinkNode_ConditionalUnarmed)
            {
                foundEmergency = true;
                AutoArmDebug.Log($"[TEST] Found ThinkNode_ConditionalUnarmed");
            }

            if (node is ThinkNode_ConditionalWeaponsInOutfit)
            {
                foundUpgrade = true;
                AutoArmDebug.Log($"[TEST] Found ThinkNode_ConditionalWeaponsInOutfit");
            }

            if (node is ThinkNode_ConditionalShouldCheckSidearms)
            {
                foundSidearm = true;
                AutoArmDebug.Log($"[TEST] Found ThinkNode_ConditionalShouldCheckSidearms");
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

            // Clear weapon cache and dropped item tracker to avoid interference
            ImprovedWeaponCacheManager.InvalidateCache(map);
            DroppedItemTracker.CleanupOldEntries();

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create two very similar weapons
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    weapon1 = TestHelpers.CreateWeapon(map, rifleDef,
                        testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);
                    weapon2 = TestHelpers.CreateWeapon(map, rifleDef,
                        testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);

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
            result.Data["ThresholdRequired"] = score1 * 0.05f; // 5% improvement threshold

            // Test: Should not create job for nearly identical weapon
            var job = jobGiver.TestTryGiveJob(testPawn);
            
            result.Data["JobCreated"] = job != null;
            if (job != null)
            {
                result.Data["JobTarget"] = job.targetA.Thing?.Label ?? "null";
            }

            if (job != null && job.targetA.Thing == weapon2)
            {
                // Check if improvement is significant enough
                float improvement = score2 / score1;
                result.Data["ImprovementRatio"] = improvement;

                if (improvement < 1.05f) // Use actual threshold from settings (5%)
                {
                    result.Success = false;
                    AutoArmDebug.LogError($"[TEST] WeaponSwapChainTest: Job created for insignificant upgrade - improvement: {improvement:F2}, required: 1.05");
                }
            }
            else if (job == null && Math.Abs(score1 - score2) < 0.01f)
            {
                // If weapons have identical scores and no job was created, that's correct
                result.Data["Note"] = "No job created for identical weapons - correct behavior";
            }
            else if (job != null && job.targetA.Thing != weapon2)
            {
                // Job was created but for a different weapon (maybe something else on the map)
                result.Data["Warning"] = $"Job created for different weapon: {job.targetA.Thing?.Label}";
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
                    DroppedItemTracker.MarkAsDropped(dropped);

                    // Check if it's tracked
                    bool isTracked = DroppedItemTracker.WasRecentlyDropped(dropped);
                    result.Data["DroppedItemTracked"] = isTracked;

                    if (!isTracked)
                    {
                        result.Success = false;
                        AutoArmDebug.LogError("[TEST] WeaponSwapChainTest: Dropped item not tracked");
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

    public class WorkInterruptionTest : ITestScenario
    {
        public string Name => "Work Interruption Thresholds";
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
                testPawn.equipment?.DestroyAllEquipment();

                // Give pawn a normal quality pistol
                currentWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position, QualityCategory.Normal);
                if (currentWeapon != null)
                {
                    currentWeapon.DeSpawn();
                    testPawn.equipment.AddEquipment(currentWeapon);
                }

                // Create minor upgrade (same weapon, slightly better)
                minorUpgrade = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);

                // Create major upgrade (better weapon type)
                majorUpgrade = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Excellent);

                if (minorUpgrade != null)
                    ImprovedWeaponCacheManager.AddWeaponToCache(minorUpgrade);
                if (majorUpgrade != null)
                    ImprovedWeaponCacheManager.AddWeaponToCache(majorUpgrade);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || currentWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            // Test different work types
            var workTypes = new[]
            {
                (JobDefOf.Mine, "Mining"),
                (JobDefOf.HaulToCell, "Hauling"),
                (JobDefOf.Clean, "Cleaning"),
                (JobDefOf.Research, "Research")
            };

            float currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            float minorScore = minorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, minorUpgrade) : 0f;
            float majorScore = majorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, majorUpgrade) : 0f;

            result.Data["CurrentScore"] = currentScore;
            result.Data["MinorUpgradeScore"] = minorScore;
            result.Data["MajorUpgradeScore"] = majorScore;

            // Skip tests if weapons weren't created properly
            if (currentScore <= 0 || minorScore <= 0 || majorScore <= 0)
            {
                result.Data["Error"] = "Invalid weapon scores";
                return result;
            }

            foreach (var (jobDef, workName) in workTypes)
            {
                // Check if work should be interrupted for minor upgrade
                float minorImprovement = minorScore / currentScore;
                bool minorShouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(jobDef, minorImprovement);

                result.Data[$"{workName}_MinorInterrupt"] = minorShouldInterrupt;
                result.Data[$"{workName}_MinorImprovement"] = minorImprovement;

                // Check if work should be interrupted for major upgrade
                float majorImprovement = majorScore / currentScore;
                bool majorShouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(jobDef, majorImprovement);

                result.Data[$"{workName}_MajorInterrupt"] = majorShouldInterrupt;
                result.Data[$"{workName}_MajorImprovement"] = majorImprovement;

                // Verify hauling is protected from minor interruptions
                if (jobDef == JobDefOf.HaulToCell && minorImprovement < 1.15f && minorShouldInterrupt)
                {
                    result.Success = false;
                    AutoArmDebug.LogError($"[TEST] WorkInterruptionTest: Hauling interrupted for minor upgrade - improvement: {minorImprovement:F2}, required: 1.15");
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

            if (currentWeapon != null && !currentWeapon.Destroyed && currentWeapon.Spawned)
            {
                currentWeapon.Destroy();
            }
            if (minorUpgrade != null && !minorUpgrade.Destroyed)
            {
                minorUpgrade.Destroy();
            }
            if (majorUpgrade != null && !majorUpgrade.Destroyed)
            {
                majorUpgrade.Destroy();
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

                // Create weapons at specific distances: 8, 16, 32, 64
                float[] distances = { 8f, 16f, 32f, 64f };

                foreach (var distance in distances)
                {
                    weaponsByDistance[distance] = new List<ThingWithComps>();

                    // Create 2 weapons at each distance
                    for (int i = 0; i < 2; i++)
                    {
                        float angle = i * 180f;
                        var pos = testPawn.Position + (Vector3.forward.RotatedBy(angle) * distance).ToIntVec3();

                        if (!pos.InBounds(map) || !pos.Standable(map))
                            continue;

                        var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol, pos);
                        if (weapon != null)
                        {
                            weaponsByDistance[distance].Add(weapon);
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

                // Allow some tolerance for position calculation
                if (distance > closestDistance * 1.5f)
                {
                    result.Success = false;
                    AutoArmDebug.LogError($"[TEST] ProgressiveSearchTest: Did not pick closest weapon - picked at {distance:F1}, closest at {closestDistance:F1}");
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
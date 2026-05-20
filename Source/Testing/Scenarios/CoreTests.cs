using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{

    internal sealed class ThinkTreeInjectionTest : ITestScenario
    {
        public string Name => "Think tree injected";

        public void Setup(Map map)
        {
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            var colonistThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (colonistThinkTree == null)
            {
                AutoArmLogger.Warn("[TEST] ThinkTreeInjectionTest: Could not find Humanlike think tree");
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
                AutoArmLogger.Warn("[TEST] ThinkTreeInjectionTest: WeaponStatus node not found in think tree");
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

    internal sealed class WeaponSwapChainTest : ITestScenario
    {
        public string Name => "No swap chains";
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
                        WeaponCache.AddWeaponToCache(weapon1);
                        WeaponCache.AddWeaponToCache(weapon2);

                        weapon1.DeSpawn();
                        testPawn.equipment.AddEquipment(weapon1);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapon1 == null || weapon2 == null)
                return TestResult.Failure("Test setup failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            float score1 = WeaponCache.GetCachedScore(testPawn, weapon1);
            float score2 = WeaponCache.GetCachedScore(testPawn, weapon2);

            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null && job.targetA.Thing == weapon2)
            {
                float improvement = score2 / score1;
                if (improvement < TestConstants.WeaponUpgradeThreshold)
                    return TestResult.Failure(
                        $"Job created for insignificant upgrade {improvement:F2}x (required {TestConstants.WeaponUpgradeThreshold}x)");
                return TestResult.Failure(
                    $"Equal-def weapons should not trigger swap (scores {score1:F1} vs {score2:F1})");
            }

            return TestResult.Pass()
                .WithData("Weapon1Score", score1)
                .WithData("Weapon2Score", score2);
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


    internal sealed class NewGameDefaultsTest : ITestScenario
    {
        public string Name => "New game defaults";
        private ApparelPolicy normalPolicy;
        private ApparelPolicy anythingPolicy;
        private ApparelPolicy slavePolicy;
        private ApparelPolicy nudistPolicy;
        private ThingCategoryDef weaponsRoot;
        private ThingDef personaDef;
        private ThingDef normalWeaponDef;

        public void Setup(Map map)
        {
            if (map == null) return;

            weaponsRoot = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons") ?? ThingCategoryDefOf.Weapons;
            normalWeaponDef = AutoArmDefOf.Gun_Autopistol
                ?? DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.IsWeapon);
            personaDef = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d =>
                d.IsWeapon && d.comps != null &&
                d.comps.Any(c => c.compClass == typeof(CompBladelinkWeapon)));

            normalPolicy = new ApparelPolicy(0, "Combat");
            anythingPolicy = new ApparelPolicy(1, "Anything");
            slavePolicy = new ApparelPolicy(2, "Slave");
            nudistPolicy = new ApparelPolicy(3, "Nudist Colony");

            foreach (var p in new[] { normalPolicy, anythingPolicy, slavePolicy, nudistPolicy })
            {
                p.filter.SetAllow(weaponsRoot, true);
                if (normalWeaponDef != null) p.filter.SetAllow(normalWeaponDef, true);
                if (personaDef != null) p.filter.SetAllow(personaDef, true);
            }
        }

        public TestResult Run()
        {
            if (weaponsRoot == null || normalWeaponDef == null)
                return TestResult.Failure("Weapons category or default weapon def not found");

            var componentType = typeof(AutoArmNewGameDefaultsComponent);
            var applyMethod = componentType.GetMethod("ApplyDefaultsInternal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (applyMethod == null)
                return TestResult.Failure("ApplyDefaultsInternal method not found");

            var component = (AutoArmNewGameDefaultsComponent)FormatterServices.GetUninitializedObject(componentType);
            var policies = new List<ApparelPolicy> { normalPolicy, anythingPolicy, slavePolicy, nudistPolicy };

            applyMethod.Invoke(component, new object[] { policies, weaponsRoot });

            if (!normalPolicy.filter.Allows(normalWeaponDef))
                return TestResult.Failure("Combat policy blocks normal weapons");
            if (personaDef != null && normalPolicy.filter.Allows(personaDef))
                return TestResult.Failure("Combat policy allows persona weapon (should disallow for non-Anything)");

            if (!anythingPolicy.filter.Allows(normalWeaponDef))
                return TestResult.Failure("Anything policy blocks normal weapons");
            if (personaDef != null && !anythingPolicy.filter.Allows(personaDef))
                return TestResult.Failure("Anything policy blocks persona weapon (should allow)");

            if (slavePolicy.filter.Allows(normalWeaponDef))
                return TestResult.Failure("Slave policy allows normal weapon (should disallow all)");
            if (personaDef != null && slavePolicy.filter.Allows(personaDef))
                return TestResult.Failure("Slave policy allows persona weapon (should disallow all)");

            if (!nudistPolicy.filter.Allows(normalWeaponDef))
                return TestResult.Failure("Nudist policy was modified (should be skipped)");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            normalPolicy = null;
            anythingPolicy = null;
            slavePolicy = null;
            nudistPolicy = null;
        }
    }

}

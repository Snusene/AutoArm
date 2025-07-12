using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using static AutoArm.JobGiver_PickUpBetterWeapon_Emergency;

namespace AutoArm
{
    [StaticConstructorOnStartup]
    public static class AutoArmInit
    {
        internal static int retryAttempts = 0;
        internal const int MaxRetryAttempts = 3;

        static AutoArmInit()
        {
            var harmony = new Harmony("Snues.AutoArm");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Message("<color=#4287f5>[AutoArm]</color> Initialized successfully");
        }
    }

    // Patch to inject think tree after game is fully loaded
    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class Game_FinalizeInit_InjectThinkTree_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            InjectIntoThinkTree();

            // Validate injection after a short delay
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (!ValidateThinkTreeInjection())
                {
                    AutoArmMod.settings.thinkTreeInjectionFailed = true;
                    Log.Warning("[AutoArm] Think tree injection validation failed - attempting retry...");
                    RetryThinkTreeInjection();
                }
                else
                {
                    AutoArmMod.settings.thinkTreeInjectionFailed = false;
                    AutoArmInit.retryAttempts = 0;
                }
            });
        }

        private static void RetryThinkTreeInjection()
        {
            if (!AutoArmMod.settings.thinkTreeInjectionFailed || AutoArmInit.retryAttempts >= AutoArmInit.MaxRetryAttempts)
                return;

            AutoArmInit.retryAttempts++;
            Log.Message($"[AutoArm] Attempting think tree injection retry {AutoArmInit.retryAttempts}/{AutoArmInit.MaxRetryAttempts}...");

            try
            {
                InjectIntoThinkTree();

                // Validate after a short delay
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (ValidateThinkTreeInjection())
                    {
                        AutoArmMod.settings.thinkTreeInjectionFailed = false;
                        Log.Message("[AutoArm] Think tree injection successful on retry!");
                        AutoArmInit.retryAttempts = 0;
                    }
                    else if (AutoArmInit.retryAttempts < AutoArmInit.MaxRetryAttempts)
                    {
                        // Schedule another retry
                        LongEventHandler.QueueLongEvent(() => RetryThinkTreeInjection(),
                            "AutoArm", false, null);
                    }
                    else
                    {
                        Log.Warning("[AutoArm] Think tree injection failed after all retries. Using fallback mode.");
                    }
                });
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Think tree injection retry {AutoArmInit.retryAttempts} failed: {e.Message}");

                if (AutoArmInit.retryAttempts >= AutoArmInit.MaxRetryAttempts)
                {
                    Log.Warning("[AutoArm] All retry attempts exhausted. Using fallback mode.");
                }
            }
        }

        private static void InjectIntoThinkTree()
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message("[AutoArm] Starting think tree injection...");
            }
            var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (humanlikeThinkTree?.thinkRoot == null)
            {
                Log.Error("[AutoArm] Could not find Humanlike think tree!");
                return;
            }
            // Find the main priority sorter
            ThinkNode_PrioritySorter mainSorter = null;
            FindMainPrioritySorter(humanlikeThinkTree.thinkRoot, ref mainSorter);
            if (mainSorter == null)
            {
                Log.Error("[AutoArm] Failed to find main priority sorter in think tree!");
                return;
            }
            // Create properly structured weapon nodes
            // 1. Emergency weapon node for unarmed pawns (high priority)
            var emergencyPriorityNode = new ThinkNode_PriorityAutoArm();
            emergencyPriorityNode.SetPriority(7f); // Use SetPriority method
            var emergencyWeaponNode = new ThinkNode_ConditionalUnarmed();
            var emergencyJobGiver = new JobGiver_PickUpBetterWeapon_Emergency();
            emergencyWeaponNode.subNodes = new List<ThinkNode> { emergencyJobGiver };
            emergencyPriorityNode.subNodes = new List<ThinkNode> { emergencyWeaponNode };

            // 2. Normal weapon upgrade node (lower priority)
            var upgradePriorityNode = new ThinkNode_PriorityAutoArm();
            upgradePriorityNode.SetPriority(5.5f); // Use SetPriority method
            var weaponUpgradeNode = new ThinkNode_ConditionalWeaponsInOutfit();
            var upgradeJobGiver = new JobGiver_PickUpBetterWeapon();
            weaponUpgradeNode.subNodes = new List<ThinkNode> { upgradeJobGiver };
            upgradePriorityNode.subNodes = new List<ThinkNode> { weaponUpgradeNode };

            // 3. Sidearm node (even lower priority)
            var sidearmPriorityNode = new ThinkNode_PriorityAutoArm();
            sidearmPriorityNode.SetPriority(5f); // Lower than weapon upgrades
            var sidearmConditionalNode = new ThinkNode_ConditionalShouldCheckSidearms();
            var sidearmJobGiver = new JobGiver_PickUpSidearm();
            sidearmConditionalNode.subNodes = new List<ThinkNode> { sidearmJobGiver };
            sidearmPriorityNode.subNodes = new List<ThinkNode> { sidearmConditionalNode };

            // Insert nodes at appropriate positions
            // Emergency node goes early (high priority)
            int emergencyIndex = Math.Min(3, mainSorter.subNodes.Count);
            mainSorter.subNodes.Insert(emergencyIndex, emergencyPriorityNode);

            // Upgrade and sidearm nodes go near the end (before last few nodes)
            int upgradeIndex = Math.Max(emergencyIndex + 1, mainSorter.subNodes.Count - 2);
            mainSorter.subNodes.Insert(upgradeIndex, upgradePriorityNode);

            int sidearmIndex = upgradeIndex + 1;
            mainSorter.subNodes.Insert(sidearmIndex, sidearmPriorityNode);

            // Only log if debug is enabled
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Injected emergency weapon check at index {emergencyIndex}, upgrade check at index {upgradeIndex}, and sidearm check at index {sidearmIndex}");
            }
        }

        private static bool IsWorkNode(ThinkNode node)
        {
            if (node is JobGiver_Work)
                return true;

            // Check sub-nodes
            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    if (IsWorkNode(subNode))
                        return true;
                }
            }

            return false;
        }

        private static void FindMainPrioritySorter(ThinkNode node, ref ThinkNode_PrioritySorter result, int depth = 0)
        {
            if (depth > 10) return;

            if (node is ThinkNode_PrioritySorter sorter && sorter.subNodes != null)
            {
                // Look for sorters with many sub-nodes that contain recognizable nodes
                if (sorter.subNodes.Count >= 5)
                {
                    bool hasRecognizableNodes = sorter.subNodes.Any(n =>
                        IsWorkNode(n) ||
                        n.GetType().Name.Contains("Satisfy") ||
                        n.GetType().Name.Contains("JobGiver") ||
                        n.GetType().Name.Contains("Priority"));

                    if (hasRecognizableNodes)
                    {
                        result = sorter;
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] Found priority sorter at depth {depth} with {sorter.subNodes.Count} nodes");
                        }
                        return;
                    }
                }

                // Keep the best candidate
                if (result == null || sorter.subNodes.Count > result.subNodes.Count)
                {
                    result = sorter;
                }
            }

            // Recursively search sub-nodes
            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    FindMainPrioritySorter(subNode, ref result, depth + 1);
                }
            }
        }

        private static bool ValidateThinkTreeInjection()
        {
            try
            {
                var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
                if (humanlikeThinkTree?.thinkRoot == null)
                    return false;

                bool foundEmergencyNode = false;
                bool foundUpgradeNode = false;

                ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundEmergencyNode, ref foundUpgradeNode);

                if (!foundEmergencyNode || !foundUpgradeNode)
                {
                    Log.Warning($"[AutoArm] Think tree validation failed - Emergency: {foundEmergencyNode}, Upgrade: {foundUpgradeNode}");
                    return false;
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message("[AutoArm] Think tree validation successful");
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error validating think tree: {e.Message}");
                return false;
            }
        }

        private static void ValidateThinkNode(ThinkNode node, ref bool foundEmergencyNode, ref bool foundUpgradeNode)
        {
            if (node == null) return;

            if (node is ThinkNode_ConditionalUnarmed)
                foundEmergencyNode = true;

            if (node is ThinkNode_ConditionalWeaponsInOutfit)
                foundUpgradeNode = true;

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    ValidateThinkNode(subNode, ref foundEmergencyNode, ref foundUpgradeNode);
                }
            }
        }
    }

    // Also inject on save game load
    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_InjectThinkTree_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Reset retry counter when loading a game
            AutoArmInit.retryAttempts = 0;
            Game_FinalizeInit_InjectThinkTree_Patch.Postfix();
        }
    }

    // Custom priority wrapper node
    public class ThinkNode_PriorityAutoArm : ThinkNode
    {
        // Remove the 'priority' field declaration - use the inherited one
        // public float priority = 1f;  <-- REMOVE THIS LINE

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            if (subNodes == null || subNodes.Count == 0)
                return ThinkResult.NoJob;

            return subNodes[0].TryIssueJobPackage(pawn, jobParams);
        }

        public override float GetPriority(Pawn pawn)
        {
            // Check if our conditional is satisfied
            if (subNodes != null && subNodes.Count > 0 && subNodes[0] is ThinkNode_Conditional conditional)
            {
                try
                {
                    // Use reflection to call the protected Satisfied method
                    var satisfiedMethod = typeof(ThinkNode_Conditional).GetMethod("Satisfied",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (satisfiedMethod != null)
                    {
                        bool satisfied = (bool)satisfiedMethod.Invoke(conditional, new object[] { pawn });
                        return satisfied ? GetBasePriority() : 0f;  // Use GetBasePriority() method
                    }
                }
                catch { }
            }
            return GetBasePriority();  // Use GetBasePriority() method
        }

        // Helper method to get the priority value
        private float GetBasePriority()
        {
            // Access the base class priority field via reflection if needed
            var priorityField = typeof(ThinkNode).GetField("priority", BindingFlags.NonPublic | BindingFlags.Instance);
            if (priorityField != null)
            {
                var value = priorityField.GetValue(this);
                if (value is float f)
                    return f;
            }
            return 1f; // Default
        }

        // Method to set priority during initialization
        public void SetPriority(float value)
        {
            var priorityField = typeof(ThinkNode).GetField("priority", BindingFlags.NonPublic | BindingFlags.Instance);
            if (priorityField != null)
            {
                priorityField.SetValue(this, value);
            }
        }
    }

    // Think tree nodes
    public class ThinkNode_ConditionalUnarmed : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            // Only trigger for unarmed colonists
            if (!pawn.IsColonist || pawn.Dead || pawn.Downed)
                return false;

            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            if (pawn.Drafted)
                return false;

            // The key check - are they WITHOUT A WEAPON (not just without equipment)?
            var primary = pawn.equipment?.Primary;
            return primary == null || !primary.def.IsWeapon;

        }
    }

    public class JobGiver_PickUpBetterWeapon_Emergency : JobGiver_PickUpBetterWeapon
    {
        // This class uses the same logic but is specifically for emergency situations
        // You could override methods here to be more aggressive about weapon selection

        protected override Job TryGiveJob(Pawn pawn)
        {
            var job = base.TryGiveJob(pawn);

            if (job != null)
            {
                // Emergency jobs should re-evaluate even more frequently
                job.expiryInterval = 100; // Re-check every ~1.7 seconds
                job.checkOverrideOnExpire = true;
            }
            else if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Emergency: {pawn.Name} is unarmed but found no weapon!");
            }

            return job;
        }
        // JobGiver for sidearm pickup
        public class JobGiver_PickUpSidearm : ThinkNode_JobGiver
        {
            protected override Job TryGiveJob(Pawn pawn)
            {
                // Skip if Simple Sidearms isn't loaded or sidearms disabled
                if (!SimpleSidearmsCompat.IsLoaded() ||
                    AutoArmMod.settings?.autoEquipSidearms != true)
                    return null;

                var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);

                if (job != null)
                {
                    // Make sidearm jobs re-evaluate frequently too
                    job.expiryInterval = 250; // Re-check every ~4 seconds
                    job.checkOverrideOnExpire = true;

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} got sidearm job with fast expiry");
                    }
                }

                return job;
            }
        }

        // Conditional to check if pawn should look for sidearms
        public class ThinkNode_ConditionalShouldCheckSidearms : ThinkNode_Conditional
        {
            protected override bool Satisfied(Pawn pawn)
            {
                // Check if sidearms are enabled and pawn is valid
                return SimpleSidearmsCompat.IsLoaded() &&
                       AutoArmMod.settings?.autoEquipSidearms == true &&
                       pawn.IsColonist &&
                       !pawn.Drafted &&
                       !pawn.Downed &&
                       pawn.Spawned;
            }
        }
    }
}
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

            // Let's log the entire think tree structure before injection
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message("[AutoArm] === THINK TREE STRUCTURE BEFORE INJECTION ===");
                LogThinkTreeStructure(humanlikeThinkTree.thinkRoot, 0);
            }

            // Find the main priority sorter (not root, but the one with actual job nodes)
            ThinkNode_PrioritySorter mainSorter = null;
            FindMainPrioritySorter(humanlikeThinkTree.thinkRoot, ref mainSorter);

            if (mainSorter == null)
            {
                Log.Error("[AutoArm] Failed to find main priority sorter in think tree!");
                return;
            }

            // Find where critical tasks end and normal tasks begin
            int criticalIndex = 0;
            for (int i = 0; i < mainSorter.subNodes.Count; i++)
            {
                var node = mainSorter.subNodes[i];
                var nodeName = node.GetType().Name;

                // These are typically critical and should stay higher priority
                if (nodeName.Contains("SelfDefense") ||
                    nodeName.Contains("AttackMelee") ||
                    nodeName.Contains("Fire") ||
                    nodeName.Contains("Flee") ||
                    nodeName.Contains("Medical"))
                {
                    criticalIndex = i + 1;
                }
            }

            // Insert right after critical tasks
            int insertIndex = Math.Min(criticalIndex + 1, mainSorter.subNodes.Count);

            // Create nodes
            var emergencyWeaponNode = new ThinkNode_ConditionalUnarmed();
            var emergencyJobGiver = new JobGiver_PickUpBetterWeapon_Emergency();
            emergencyWeaponNode.subNodes = new List<ThinkNode> { emergencyJobGiver };

            mainSorter.subNodes.Insert(insertIndex, emergencyWeaponNode);

            // Normal upgrades and sidearms go later
            var weaponUpgradeNode = new ThinkNode_ConditionalWeaponsInOutfit();
            var upgradeJobGiver = new JobGiver_PickUpBetterWeapon();
            weaponUpgradeNode.subNodes = new List<ThinkNode> { upgradeJobGiver };

            mainSorter.subNodes.Insert(insertIndex + 2, weaponUpgradeNode);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Injected emergency weapon check at index {insertIndex} (after critical tasks)");
            }

            // After injection, log again
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message("[AutoArm] === THINK TREE STRUCTURE AFTER INJECTION ===");
                LogThinkTreeStructure(humanlikeThinkTree.thinkRoot, 0);
            }
        }

        private static void LogThinkTreeStructure(ThinkNode node, int depth)
        {
            string indent = new string(' ', depth * 2);
            string nodeInfo = $"{indent}{node.GetType().Name}";

            if (node is ThinkNode_JobGiver jobGiver)
                nodeInfo += $" [{jobGiver.GetType().Name}]";

            if (node is ThinkNode_PrioritySorter sorter)
                nodeInfo += $" (priority)";

            Log.Message(nodeInfo);

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    LogThinkTreeStructure(subNode, depth + 1);
                }
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

        private static void FindMainPrioritySorter(ThinkNode node, ref ThinkNode_PrioritySorter result, int depth = 0, List<string> path = null)
        {
            if (depth > 20) return;

            if (path == null) path = new List<string>();
            path.Add(node.GetType().Name);

            // Skip nodes that are inside special conditions we don't want
            bool skipThisPath = path.Any(p =>
                p.Contains("JoinVoluntarilyJoinableLord") ||
                p.Contains("ConditionalHasLordDuty") ||
                p.Contains("ConditionalPawnKind") ||
                p.Contains("ConditionalPrisoner") ||
                p.Contains("ConditionalGuest"));

            if (skipThisPath)
            {
                path.RemoveAt(path.Count - 1);
                return;
            }

            if (node is ThinkNode_PrioritySorter sorter && sorter.subNodes != null)
            {
                // Look for the main colonist priority sorter
                if (sorter.subNodes.Count >= 10)
                {
                    // Check if this contains the key colonist behaviors
                    bool hasGetFood = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetFood");
                    bool hasGetRest = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetRest");
                    bool hasWork = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_Work");

                    // Look for the one that's under SubtreesByTag
                    bool isUnderSubtreesByTag = path.Contains("ThinkNode_SubtreesByTag");

                    if (hasGetFood && hasGetRest && hasWork && isUnderSubtreesByTag)
                    {
                        result = sorter;
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] Found main colonist priority sorter at depth {depth} with {sorter.subNodes.Count} nodes");
                            Log.Message($"[AutoArm] Path: {string.Join(" -> ", path)}");
                        }
                        path.RemoveAt(path.Count - 1);
                        return;
                    }
                }
            }

            // Recursively search sub-nodes
            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    FindMainPrioritySorter(subNode, ref result, depth + 1, new List<string>(path));
                    if (result != null)
                    {
                        path.RemoveAt(path.Count - 1);
                        return;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
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
                // Don't set expiry interval - let the pawn complete the job!
                // Only set expiry for jobs that might become invalid
                job.expiryInterval = -1; // Never expire
                job.checkOverrideOnExpire = false;
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

                // Don't set expiry - let them complete the job!
                // if (job != null)
                // {
                //     job.expiryInterval = 250;
                //     job.checkOverrideOnExpire = true;
                // }

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
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

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

            Log.Message("[AutoArm] Initialized successfully. Think tree injection will happen after game load.");
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
                    // Reset retry counter on success
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

            // Find the priority sorter that contains JobGiver_Work
            ThinkNode_PrioritySorter targetSorter = null;
            int workNodeIndex = -1;
            FindWorkNode(humanlikeThinkTree.thinkRoot, ref targetSorter, ref workNodeIndex);

            if (targetSorter == null || workNodeIndex < 0)
            {
                Log.Warning("[AutoArm] Could not find JobGiver_Work in think tree. Trying alternative injection method...");
                targetSorter = FindMainPrioritySorter(humanlikeThinkTree.thinkRoot);
                if (targetSorter != null)
                {
                    workNodeIndex = Math.Min(8, targetSorter.subNodes.Count);
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] Using alternative injection at index " + workNodeIndex);
                    }
                }
                else
                {
                    Log.Error("[AutoArm] Failed to find suitable injection point in think tree!");
                    return;
                }
            }

            // Create single weapon node structure
            var weaponConditional = new ThinkNode_ConditionalWeaponsInOutfit();
            var weaponJobGiver = new JobGiver_PickUpBetterWeapon();
            weaponConditional.subNodes = new List<ThinkNode> { weaponJobGiver };

            // Insert at a higher priority (but not emergency level)
            int insertIndex = Math.Max(0, workNodeIndex - 2); // Just before work
            targetSorter.subNodes.Insert(insertIndex, weaponConditional);

            Log.Message($"[AutoArm] Successfully injected weapon optimization into think tree at index {insertIndex}");
        }

        private static void FindMainPrioritySorter(ThinkNode node, ref ThinkNode_PrioritySorter result, int depth)
        {
            // The main sorter is usually at depth 1 or 2 and has many sub-nodes
            if (node is ThinkNode_PrioritySorter sorter && depth <= 2 && sorter.subNodes?.Count > 10)
            {
                result = sorter;
                return;
            }

            if (node.subNodes != null && result == null)
            {
                foreach (var subNode in node.subNodes)
                {
                    FindMainPrioritySorter(subNode, ref result, depth + 1);
                }
            }
        }

        private static void FindWorkNode(ThinkNode node, ref ThinkNode_PrioritySorter resultSorter, ref int resultIndex)
        {
            if (node is ThinkNode_PrioritySorter sorter && sorter.subNodes != null)
            {
                for (int i = 0; i < sorter.subNodes.Count; i++)
                {
                    if (sorter.subNodes[i] is JobGiver_Work)
                    {
                        resultSorter = sorter;
                        resultIndex = i;
                        return;
                    }
                }
            }

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    FindWorkNode(subNode, ref resultSorter, ref resultIndex);
                    if (resultSorter != null) return;
                }
            }
        }

        private static ThinkNode_PrioritySorter FindMainPrioritySorter(ThinkNode node)
        {
            // Look for the first major priority sorter
            if (node is ThinkNode_PrioritySorter sorter && sorter.subNodes?.Count > 5)
            {
                return sorter;
            }

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    var result = FindMainPrioritySorter(subNode);
                    if (result != null) return result;
                }
            }

            return null;
        }

        private static bool ValidateThinkTreeInjection()
        {
            try
            {
                var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
                if (humanlikeThinkTree?.thinkRoot == null)
                    return false;

                bool foundWeaponConditional = false;
                bool foundJobGiver = false;

                ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundWeaponConditional, ref foundJobGiver);

                if (!foundWeaponConditional || !foundJobGiver)
                {
                    Log.Warning("[AutoArm] Think tree validation failed - weapon nodes not found");
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

        private static void ValidateThinkNode(ThinkNode node, ref bool foundWeaponConditional, ref bool foundJobGiver)
        {
            if (node == null) return;

            if (node is ThinkNode_ConditionalWeaponsInOutfit)
                foundWeaponConditional = true;

            if (node is JobGiver_PickUpBetterWeapon)
                foundJobGiver = true;

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    ValidateThinkNode(subNode, ref foundWeaponConditional, ref foundJobGiver);
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
}
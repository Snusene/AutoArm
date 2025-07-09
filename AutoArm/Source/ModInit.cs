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
                    // Set a flag that TickRare should handle everything
                    AutoArmMod.settings.thinkTreeInjectionFailed = true;
                    Log.Warning("[AutoArm] Think tree injection validation failed - using fallback mode");
                }
            });
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

                // Alternative: Find the main priority sorter
                targetSorter = FindMainPrioritySorter(humanlikeThinkTree.thinkRoot);
                if (targetSorter != null)
                {
                    workNodeIndex = targetSorter.subNodes.Count; // Add at end
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] Using alternative injection at end of main priority sorter");
                    }
                }
                else
                {
                    Log.Error("[AutoArm] Failed to find suitable injection point in think tree!");
                    return;
                }
            }

            // Create weapon optimization nodes
            var weaponConditional = new ThinkNode_ConditionalWeaponsInOutfit();
            var unarmedConditional = new ThinkNode_ConditionalUnarmedOrPoorlyArmed();
            var emergencyJobGiver = new JobGiver_GetWeaponEmergency();
            var normalJobGiver = new JobGiver_PickUpBetterWeapon();

            // Build the weapon node structure
            unarmedConditional.subNodes = new List<ThinkNode> { emergencyJobGiver };
            weaponConditional.subNodes = new List<ThinkNode> { unarmedConditional, normalJobGiver };

            // Create sidearm nodes (only if Simple Sidearms is loaded)
            ThinkNode sidearmNode = null;
            if (SimpleSidearmsCompat.IsLoaded())
            {
                var noSidearmsConditional = new ThinkNode_ConditionalNoSidearms();
                var sidearmEmergencyJobGiver = new JobGiver_GetSidearmEmergency();

                // Build the sidearm node structure
                noSidearmsConditional.subNodes = new List<ThinkNode> { sidearmEmergencyJobGiver };
                sidearmNode = noSidearmsConditional;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message("[AutoArm] Simple Sidearms detected - adding emergency sidearm acquisition");
                }
            }

            // Insert into think tree - weapons first, then sidearms
            targetSorter.subNodes.Insert(workNodeIndex, weaponConditional);

            if (sidearmNode != null)
            {
                // Insert sidearms right after weapons (so it has similar priority)
                targetSorter.subNodes.Insert(workNodeIndex + 1, sidearmNode);
            }

            Log.Message($"[AutoArm] Successfully injected weapon optimization into think tree at index {workNodeIndex}");
            if (sidearmNode != null)
            {
                Log.Message($"[AutoArm] Successfully injected sidearm emergency acquisition at index {workNodeIndex + 1}");
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

                // Try to find our injected nodes
                bool foundWeaponConditional = false;
                bool foundJobGiver = false;
                bool foundSidearmConditional = false;
                bool foundSidearmJobGiver = false;

                ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundWeaponConditional, ref foundJobGiver, ref foundSidearmConditional, ref foundSidearmJobGiver);

                if (!foundWeaponConditional || !foundJobGiver)
                {
                    Log.Warning("[AutoArm] Think tree validation failed - our weapon nodes may have been overridden");
                    Log.Warning("[AutoArm] Falling back to TickRare-only mode");
                    return false;
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message("[AutoArm] Think tree validation successful - weapon nodes found");
                    if (SimpleSidearmsCompat.IsLoaded())
                    {
                        if (foundSidearmConditional && foundSidearmJobGiver)
                        {
                            Log.Message("[AutoArm] Sidearm emergency nodes found");
                        }
                        else
                        {
                            Log.Message("[AutoArm] Sidearm emergency nodes not found");
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error validating think tree: {e.Message}");
                return false;
            }
        }

        private static void ValidateThinkNode(ThinkNode node, ref bool foundWeaponConditional, ref bool foundJobGiver, ref bool foundSidearmConditional, ref bool foundSidearmJobGiver)
        {
            if (node == null)
                return;

            if (node is ThinkNode_ConditionalWeaponsInOutfit)
                foundWeaponConditional = true;

            if (node is JobGiver_PickUpBetterWeapon || node is JobGiver_GetWeaponEmergency)
                foundJobGiver = true;

            if (node is ThinkNode_ConditionalNoSidearms)
                foundSidearmConditional = true;

            if (node is JobGiver_GetSidearmEmergency)
                foundSidearmJobGiver = true;

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    ValidateThinkNode(subNode, ref foundWeaponConditional, ref foundJobGiver, ref foundSidearmConditional, ref foundSidearmJobGiver);
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
            Game_FinalizeInit_InjectThinkTree_Patch.Postfix();
        }
    }
}
// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Mod initialization, think tree injection, and retry logic
// Critical: Handles dual weapon check system (think tree + fallback TickRare)
// Uses: WeaponAutoEquip, AutoArmSettings, SimpleSidearmsCompat
// Note: Pre-injection validation ensures compatibility with other mods

using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using AutoArm.Jobs;
using AutoArm.Helpers;
using AutoArm.Caching;
using AutoArm.Logging;
using AutoArm.Weapons;
using AutoArm.Definitions;
using AutoArm.UI;

namespace AutoArm
{
    [StaticConstructorOnStartup]
    public static class AutoArmInit
    {
        internal static int retryAttempts = 0;
        internal const int MaxRetryAttempts = 3;
        internal static Harmony harmonyInstance;

        static AutoArmInit()
        {
            harmonyInstance = new Harmony("Snues.AutoArm");
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("<color=#4287f5>[AutoArm]</color> Initialized successfully");

            // Initialize debug logging if enabled
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("AutoArm mod initialized with debug logging enabled");
            }
        }
    }

    public static class ModInit
    {
        /// <summary>
        /// Returns true if the mod is running in fallback mode due to ThinkTree injection failure
        /// </summary>
        public static bool IsFallbackModeActive => AutoArmMod.settings?.thinkTreeInjectionFailed == true;
    }

    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class Game_FinalizeInit_InjectThinkTree_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Add GameComponent for save/load support
            if (Current.Game != null && !Current.Game.components.Any(c => c is AutoArmGameComponent))
            {
                Current.Game.components.Add(new AutoArmGameComponent(Current.Game));
            }

            // Apply SimpleSidearms patches if loaded
            if (SimpleSidearmsCompat.IsLoaded())
            {
                try
                {
                    SimpleSidearms_WeaponSwap_Patches.ApplyPatches(AutoArmInit.harmonyInstance);
                    SimpleSidearms_JobGiver_RetrieveWeapon_Patch.ApplyPatch(AutoArmInit.harmonyInstance);
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug("SimpleSidearms patches applied successfully");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[AutoArm] Failed to apply SimpleSidearms patches: {ex.Message}");
                }
            }

            // Apply Pick Up and Haul patches if both PUAH and SimpleSidearms are loaded
            if (PickUpAndHaulCompat.IsLoaded() && SimpleSidearmsCompat.IsLoaded())
            {
                AutoArmInit.harmonyInstance.Patch(
                    AccessTools.Method(typeof(Pawn_JobTracker), "EndCurrentJob"),
                    postfix: new HarmonyMethod(typeof(PickUpAndHaul_JobEnd_Patch), "Postfix"));
                    
                AutoArmInit.harmonyInstance.Patch(
                    AccessTools.Method(typeof(Pawn_JobTracker), "StartJob"),
                    postfix: new HarmonyMethod(typeof(PickUpAndHaul_JobStart_Patch), "Postfix"));
                    
                AutoArmInit.harmonyInstance.Patch(
                    AccessTools.Method(typeof(Pawn_InventoryTracker), "TryAddItemNotForSale"),
                    postfix: new HarmonyMethod(typeof(InventoryTracker_AddItem_Patch), "Postfix"));
                    
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Pick Up and Haul + SimpleSidearms patches applied");
                }
            }

            // Pre-injection validation
            if (!ValidatePreInjection())
            {
                Log.Warning("[AutoArm] Pre-injection validation failed. Disabling think tree injection to prevent crashes.");
                AutoArmMod.settings.thinkTreeInjectionFailed = true;
                AutoArmLogger.Warn("Think tree injection disabled due to pre-validation failure - using fallback mode");
                return;
            }

            try
            {
                InjectIntoThinkTree();

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
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug("[THINK TREE] Think tree injection VALIDATED successfully!");
                        }
                        AutoArmMod.settings.thinkTreeInjectionFailed = false;
                        AutoArmInit.retryAttempts = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Critical error during think tree injection: {ex.Message}");
                if (ex.InnerException != null)
                    Log.Error($"[AutoArm] Inner exception: {ex.InnerException.Message}");

                AutoArmMod.settings.thinkTreeInjectionFailed = true;
            }
        }

        private static bool ValidatePreInjection()
        {
            try
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Running pre-injection validation...");
                }

                // Test 1: Can we access weapon defs without crashing?
                var weaponDefs = WeaponValidation.AllWeapons;
                if (weaponDefs == null || weaponDefs.Count == 0)
                {
                    Log.Warning("[AutoArm] No weapon definitions found during pre-injection validation.");
                    return false;
                }

                // Test 2: Can we safely check weapon properties?
                int testedWeapons = 0;
                int failedWeapons = 0;

                foreach (var weaponDef in weaponDefs.Take(10)) // Test first 10 weapons
                {
                    try
                    {
                        testedWeapons++;

                        // Try to access properties that might crash with modded weapons
                        var isWeapon = weaponDef.IsWeapon;
                        var equipmentType = weaponDef.equipmentType;
                        var techLevel = weaponDef.techLevel;

                        // Check if this might be a problematic modded weapon
                        if (weaponDef.defName?.Contains("Kiiro") == true && AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"Found Kiiro Race weapon during validation: {weaponDef.defName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedWeapons++;
                        Log.Warning($"[AutoArm] Failed to validate weapon {weaponDef?.defName ?? "unknown"}: {ex.Message}");
                    }
                }

                // If more than half of tested weapons failed, something is wrong
                if (failedWeapons > testedWeapons / 2)
                {
                    Log.Warning($"[AutoArm] Too many weapon validation failures ({failedWeapons}/{testedWeapons}). Pre-injection validation failed.");
                    return false;
                }

                // Test 3: Can we build a minimal weapon cache?
                try
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug("Testing weapon cache build...");
                    }
                    var testCache = new Dictionary<ThingDef, float>();

                    foreach (var weaponDef in weaponDefs.Take(5)) // Test with first 5 weapons
                    {
                        try
                        {
                            // Simulate weapon scoring
                            if (WeaponValidation.IsProperWeapon(weaponDef))
                            {
                                testCache[weaponDef] = 1.0f;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[AutoArm] Failed to process weapon {weaponDef?.defName} in cache test: {ex.Message}");
                        }
                    }

                    if (testCache.Count == 0)
                    {
                        Log.Warning("[AutoArm] Could not build test weapon cache. Pre-injection validation failed.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[AutoArm] Weapon cache test failed: {ex.Message}");
                    return false;
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Pre-injection validation passed.");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Critical error during pre-injection validation: {ex.Message}");
                return false;
            }
        }

        private static void RetryThinkTreeInjection()
        {
            if (!AutoArmMod.settings.thinkTreeInjectionFailed || AutoArmInit.retryAttempts >= AutoArmInit.MaxRetryAttempts)
                return;

            AutoArmInit.retryAttempts++;
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Attempting think tree injection retry {AutoArmInit.retryAttempts}/{AutoArmInit.MaxRetryAttempts}...");
            }

            try
            {
                InjectIntoThinkTree();

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (ValidateThinkTreeInjection())
                    {
                    AutoArmMod.settings.thinkTreeInjectionFailed = false;
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug("[THINK TREE] Think tree injection successful on retry!");
                    }
                    AutoArmInit.retryAttempts = 0;
                    }
                    else if (AutoArmInit.retryAttempts < AutoArmInit.MaxRetryAttempts)
                    {
                        LongEventHandler.QueueLongEvent(() => RetryThinkTreeInjection(),
                            "AutoArm", false, null);
                    }
                    else
                    {
                        Log.Warning("[AutoArm] Think tree injection failed after all retries. Using fallback mode.");
                        AutoArmLogger.Warn("AutoArm is running in FALLBACK MODE - performance may be impacted. Consider reporting this issue.");
                        Messages.Message("AutoArm: Think tree injection failed - using fallback mode. Performance may be reduced.", MessageTypeDefOf.NegativeEvent, false);
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
                AutoArmLogger.Debug("Starting think tree injection...");
            }

            var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (humanlikeThinkTree?.thinkRoot == null)
            {
                Log.Error("[AutoArm] Could not find Humanlike think tree!");
                return;
            }

            ThinkNode_PrioritySorter mainSorter = null;
            FindMainPrioritySorter(humanlikeThinkTree.thinkRoot, ref mainSorter);

            if (mainSorter == null)
            {
                Log.Error("[AutoArm] Failed to find main priority sorter in think tree!");
                return;
            }

            int criticalIndex = 0;
            for (int i = 0; i < mainSorter.subNodes.Count; i++)
            {
                var node = mainSorter.subNodes[i];
                var nodeName = node.GetType().Name;

                if (nodeName.Contains("SelfDefense") ||
                    nodeName.Contains("AttackMelee") ||
                    nodeName.Contains("Fire") ||
                    nodeName.Contains("Flee") ||
                    nodeName.Contains("Medical"))
                {
                    criticalIndex = i + 1;
                }
            }

            int insertIndex = Math.Min(criticalIndex + 1, mainSorter.subNodes.Count);

            // Create nodes
            var emergencyWeaponNode = new ThinkNode_ConditionalUnarmed();
            var emergencyJobGiver = new JobGiver_PickUpBetterWeapon_Emergency();
            emergencyWeaponNode.subNodes = new List<ThinkNode> { emergencyJobGiver };

            mainSorter.subNodes.Insert(insertIndex, emergencyWeaponNode);
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[THINK TREE] Injected emergency weapon node at index {insertIndex}");
            }

            // Normal upgrades - insert at next position
            // ThinkNode_ConditionalWeaponsInOutfit is inside WeaponAutoEquip.cs
            var weaponUpgradeNode = new ThinkNode_ConditionalWeaponsInOutfit();
            var upgradeJobGiver = new JobGiver_PickUpBetterWeapon();
            weaponUpgradeNode.subNodes = new List<ThinkNode> { upgradeJobGiver };
            mainSorter.subNodes.Insert(insertIndex + 1, weaponUpgradeNode);

            // Sidearm pickup node - only inject if SimpleSidearms is loaded
            if (SimpleSidearmsCompat.IsLoaded())
            {
                var sidearmConditionalNode = new ThinkNode_ConditionalShouldCheckSidearms();
                var sidearmJobGiver = new JobGiver_PickUpSidearm();
                sidearmConditionalNode.subNodes = new List<ThinkNode> { sidearmJobGiver };
                mainSorter.subNodes.Insert(insertIndex + 2, sidearmConditionalNode);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[THINK TREE] Injected sidearm pickup check at index {insertIndex + 2}");
                }
            }
            else
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("SimpleSidearms not loaded - skipping sidearm node injection");
                }
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[THINK TREE] Injected emergency weapon check at index {insertIndex}");
                AutoArmLogger.Debug($"[THINK TREE] Injected weapon upgrade check at index {insertIndex + 1}");
            }
        }

        private static bool IsWorkNode(ThinkNode node)
        {
            if (node is JobGiver_Work)
                return true;

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
                if (sorter.subNodes.Count >= 10 && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"DEBUG: Found large PrioritySorter at depth {depth} with {sorter.subNodes.Count} nodes");
                    AutoArmLogger.Debug($"DEBUG: Path: {string.Join(" -> ", path)}");

                    var nodeTypes = sorter.subNodes.Take(5).Select(n => n.GetType().Name).ToList();
                    AutoArmLogger.Debug($"DEBUG: First 5 nodes: {string.Join(", ", nodeTypes)}");

                    bool hasGetFood = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetFood");
                    bool hasGetRest = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetRest");
                    bool hasWork = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_Work");

                    AutoArmLogger.Debug($"DEBUG: Has GetFood: {hasGetFood}, GetRest: {hasGetRest}, Work: {hasWork}");

                    bool isUnderSubtree = path.Contains("ThinkNode_Subtree") || path.Contains("ThinkNode_SubtreesByTag");

                    if (hasGetFood && hasGetRest && hasWork && isUnderSubtree)
                    {
                        result = sorter;
                        AutoArmLogger.Debug($"Found main colonist priority sorter at depth {depth} with {sorter.subNodes.Count} nodes");
                        AutoArmLogger.Debug($"Path: {string.Join(" -> ", path)}");
                        path.RemoveAt(path.Count - 1);
                        return;
                    }
                }
                else if (sorter.subNodes.Count >= 10)
                {
                    // Still check for the main sorter even if debug logging is off
                    bool hasGetFood = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetFood");
                    bool hasGetRest = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetRest");
                    bool hasWork = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_Work");
                    bool isUnderSubtree = path.Contains("ThinkNode_Subtree") || path.Contains("ThinkNode_SubtreesByTag");

                    if (hasGetFood && hasGetRest && hasWork && isUnderSubtree)
                    {
                        result = sorter;
                        path.RemoveAt(path.Count - 1);
                        return;
                    }
                }
            }

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
                bool foundSidearmNode = false;

                ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundEmergencyNode, ref foundUpgradeNode, ref foundSidearmNode);

                // Validate core nodes
                if (!foundEmergencyNode || !foundUpgradeNode)
                {
                    Log.Warning($"[AutoArm] Think tree validation failed - Emergency: {foundEmergencyNode}, Upgrade: {foundUpgradeNode}");
                    return false;
                }

                // Validate sidearm nodes only if SimpleSidearms is loaded
                if (SimpleSidearmsCompat.IsLoaded() && !foundSidearmNode)
                {
                    Log.Warning($"[AutoArm] SimpleSidearms loaded but sidearm node not found in think tree");
                    return false;
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("[THINK TREE] Think tree validation successful - Emergency: " + foundEmergencyNode + ", Upgrade: " + foundUpgradeNode + ", Sidearm: " + foundSidearmNode);
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error validating think tree: {e.Message}");
                return false;
            }
        }

        internal static void ValidateThinkNode(ThinkNode node, ref bool foundEmergencyNode, ref bool foundUpgradeNode, ref bool foundSidearmNode)
        {
            if (node == null) return;

            if (node is ThinkNode_ConditionalUnarmed)
                foundEmergencyNode = true;

            if (node is ThinkNode_ConditionalWeaponsInOutfit)
                foundUpgradeNode = true;

            if (node is ThinkNode_ConditionalShouldCheckSidearms)
                foundSidearmNode = true;

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    ValidateThinkNode(subNode, ref foundEmergencyNode, ref foundUpgradeNode, ref foundSidearmNode);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_InjectThinkTree_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            AutoArmInit.retryAttempts = 0;
            
            // Only inject think tree if it's not already injected
            // This prevents duplicate nodes when loading saves
            if (!ValidateThinkTreeInjection())
            {
                Game_FinalizeInit_InjectThinkTree_Patch.Postfix();
            }
            else
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("[THINK TREE] Think tree already injected, skipping re-injection on load");
                }
            }

            // Clean up any sidearm upgrade state that shouldn't persist across save/load
            SimpleSidearmsCompat.CleanupAfterLoad();
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
                bool foundSidearmNode = false;

                Game_FinalizeInit_InjectThinkTree_Patch.ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundEmergencyNode, ref foundUpgradeNode, ref foundSidearmNode);

                // If we found our nodes, injection already happened
                return foundEmergencyNode && foundUpgradeNode;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error validating think tree on load: {e.Message}");
                return false;
            }
        }
    }

    public class ThinkNode_PriorityAutoArm : ThinkNode
    {
        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            if (subNodes == null || subNodes.Count == 0)
                return ThinkResult.NoJob;

            return subNodes[0].TryIssueJobPackage(pawn, jobParams);
        }

        public override float GetPriority(Pawn pawn)
        {
            if (subNodes != null && subNodes.Count > 0 && subNodes[0] is ThinkNode_Conditional conditional)
            {
                try
                {
                    var satisfiedMethod = typeof(ThinkNode_Conditional).GetMethod("Satisfied",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (satisfiedMethod != null)
                    {
                        bool satisfied = (bool)satisfiedMethod.Invoke(conditional, new object[] { pawn });
                        return satisfied ? GetBasePriority() : 0f;
                    }
                }
                catch { }
            }
            return GetBasePriority();
        }

        private float GetBasePriority()
        {
            var priorityField = typeof(ThinkNode).GetField("priority", BindingFlags.NonPublic | BindingFlags.Instance);
            if (priorityField != null)
            {
                var value = priorityField.GetValue(this);
                if (value is float f)
                    return f;
            }
            return 1f;
        }

        public void SetPriority(float value)
        {
            var priorityField = typeof(ThinkNode).GetField("priority", BindingFlags.NonPublic | BindingFlags.Instance);
            if (priorityField != null)
            {
                priorityField.SetValue(this, value);
            }
        }
    }

    public class ThinkNode_ConditionalUnarmed : ThinkNode_Conditional
    {
        private static Dictionary<Pawn, int> pawnEvaluationFailures = new Dictionary<Pawn, int>();
        private static HashSet<Pawn> loggedCriticalPawns = new HashSet<Pawn>();

        protected override bool Satisfied(Pawn pawn)
        {
            try
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, "[UNARMED CHECK] Evaluating ThinkNode_ConditionalUnarmed.Satisfied");
                }
                
                // Check if this pawn has been failing repeatedly
                if (pawnEvaluationFailures.TryGetValue(pawn, out int failCount) && failCount > 10)
                {
                    // Too many failures for this pawn - skip to prevent crash loops
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, $"[UNARMED CHECK] Too many failures ({failCount}), skipping");
                    }
                    return false;
                }

                if (!JobGiverHelpers.SafeIsColonist(pawn) || pawn.Dead || pawn.Downed)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, $"[UNARMED CHECK] Failed basic checks - IsColonist: {JobGiverHelpers.SafeIsColonist(pawn)}, Dead: {pawn.Dead}, Downed: {pawn.Downed}");
                    }
                    return false;
                }

                // Safe check for violence capability
                try
                {
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                        return false;
                }
                catch
                {
                    // If we can't check, assume they can use weapons
                    // Better to offer weapons than to deny them due to an error
                }

                if (pawn.Drafted)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, "[UNARMED CHECK] Pawn is drafted, skipping");
                    }
                    return false;
                }

                var primary = pawn.equipment?.Primary;
                bool isUnarmed = primary == null || !primary.def.IsWeapon;
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, $"[UNARMED CHECK] Primary weapon: {primary?.Label ?? "none"}, IsUnarmed: {isUnarmed}");
                }

                // If unarmed, check if any weapons are allowed by outfit filter
                if (isUnarmed && pawn.outfits?.CurrentApparelPolicy?.filter != null)
                {
                    var filter = pawn.outfits.CurrentApparelPolicy.filter;
                    bool anyWeaponAllowed = WeaponValidation.AllWeapons.Any(td => filter.Allows(td));

                    if (!anyWeaponAllowed)
                    {
                    // No weapons allowed in outfit - don't try to pick up weapons
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, "[UNARMED CHECK] No weapons allowed by outfit filter");
                    }
                        return false;
                        }
                        else
                        {
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.LogPawn(pawn, "[UNARMED CHECK] Outfit allows weapons");
                            }
                        }
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, $"[UNARMED CHECK] Satisfied returning: {isUnarmed}");
                }
                return isUnarmed;
            }
            catch (Exception ex)
            {
                // Record failure for this pawn
                if (!pawnEvaluationFailures.ContainsKey(pawn))
                    pawnEvaluationFailures[pawn] = 0;
                pawnEvaluationFailures[pawn]++;

                // If too many failures, record a crash
                if (pawnEvaluationFailures[pawn] > 5 && !loggedCriticalPawns.Contains(pawn))
                {
                    Log.Error($"[AutoArm] Multiple evaluation failures for {pawn?.Name?.ToStringShort ?? "unknown"} - possible crash loop detected");
                    loggedCriticalPawns.Add(pawn);
                }

                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalUnarmed.Satisfied for {pawn?.Name?.ToStringShort ?? "unknown"}: {ex.Message}");
                return false; // Safe default - don't try to equip weapons if we can't check
            }
        }

        public override float GetPriority(Pawn pawn)
        {
            try
            {
                // Emergency priority - unarmed pawns need weapons ASAP
                bool satisfied = Satisfied(pawn);
                float priority = satisfied ? 6.9f : 0f;
                if (satisfied && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, $"[UNARMED CHECK] GetPriority returning {priority} (emergency priority)");
                }
                return priority;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalUnarmed.GetPriority: {ex.Message}");
                return 0f;
            }
        }
        
        /// <summary>
        /// Clean up evaluation failures for dead pawns
        /// </summary>
        public static void CleanupEvaluationFailures()
        {
            var deadPawns = pawnEvaluationFailures.Keys
                .Where(p => p == null || p.Destroyed || p.Dead)
                .ToList();
                
            foreach (var pawn in deadPawns)
            {
                pawnEvaluationFailures.Remove(pawn);
            }
            
            // Also clear pawns with excessive failures (they're probably permanently problematic)
            var problematicPawns = pawnEvaluationFailures
                .Where(kvp => kvp.Value > 50)
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var pawn in problematicPawns)
            {
                pawnEvaluationFailures.Remove(pawn);
                loggedCriticalPawns.Remove(pawn);
            }
            
            // Clean logged critical pawns for dead pawns
            loggedCriticalPawns.RemoveWhere(p => p == null || p.Destroyed || p.Dead);
        }
    }

    public class JobGiver_PickUpBetterWeapon_Emergency : JobGiver_PickUpBetterWeapon
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.LogPawn(pawn, "[EMERGENCY JOB] JobGiver_PickUpBetterWeapon_Emergency.TryGiveJob called");
            }
            
            // Check if mod is enabled first
            if (AutoArmMod.settings?.modEnabled != true)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, "[EMERGENCY JOB] Mod is disabled, returning null");
                }
                return null;
            }
                
            try
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, "[EMERGENCY JOB] Calling base.TryGiveJob");
                }
                // Note: The base JobGiver already handles emergency vs routine checks
                // This class exists mainly for debugging and to ensure emergency jobs don't expire
                var job = base.TryGiveJob(pawn);

                if (job != null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, $"[EMERGENCY JOB] Job created successfully: {job.def.defName} targeting {job.targetA.Thing?.Label ?? "unknown"}");
                    }
                    job.expiryInterval = -1; // Never expire emergency weapon pickups
                    job.checkOverrideOnExpire = false;
                }
                else if (pawn.equipment?.Primary == null && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, "[EMERGENCY JOB] CRITICAL: Pawn is unarmed but no job was created!");
                    // Only log emergency failures once per 10 seconds per pawn
                    TimingHelper.LogWithCooldown(pawn, "Emergency: is unarmed but found no weapon!",
                        TimingHelper.CooldownType.ForcedWeaponLog);
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, $"[EMERGENCY JOB] Returning job: {job?.def?.defName ?? "null"}");
                }
                return job;
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, $"[EMERGENCY JOB] ERROR: {ex.Message}");
                }
                Log.Error($"[AutoArm] Error in JobGiver_PickUpBetterWeapon_Emergency: {ex.Message}");
                if (ex.InnerException != null)
                    Log.Error($"[AutoArm] Inner: {ex.InnerException.Message}");
                return null;
            }
        }
        
        // Note: TestTryGiveJob is inherited from JobGiver_PickUpBetterWeapon
    }

    // Sidearm performance handled in SimpleSidearmsCompat:
    // - Think node evaluates every 2 seconds (120 ticks)
    // - Search cooldowns scale with colony size
    // - Failed searches tracked with 60+ second cooldowns
    // - Max 2 searches per tick, fewer weapons checked in large colonies
    public class JobGiver_PickUpSidearm : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check if mod is enabled first
            if (AutoArmMod.settings?.modEnabled != true)
                return null;
                
            if (!SimpleSidearmsCompat.IsLoaded() ||
                AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            // Check if raids are happening and setting is enabled - should be consistent with primary weapons
            if (AutoArmMod.settings?.disableDuringRaids == true)
            {
                // Check for active raids on ANY map (not just current)
                foreach (var checkMap in Find.Maps)
                {
                    if (JobGiver_PickUpBetterWeapon.IsRaidActive(checkMap) && AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, $"[SIDEARM JOB] Raid active on map {checkMap.uniqueID} and disableDuringRaids is true, skipping sidearm check");
                        return null;
                    }
                }
            }

            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);

            return job;
        }
        
        public Job TestTryGiveJob(Pawn pawn)
        {
            return TryGiveJob(pawn);
        }
    }

    // This ThinkNode controls whether sidearm checks run at all.
    // It must check forced weapon status to prevent sidearm upgrades when weapons are forced.
    public class ThinkNode_ConditionalShouldCheckSidearms : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            try
            {
                // Check if sidearms are enabled in settings
                if (AutoArmMod.settings?.autoEquipSidearms != true)
                {
                    return false;
                }

                if (!SimpleSidearmsCompat.IsLoaded() ||
                    !JobGiverHelpers.SafeIsColonist(pawn) ||
                    pawn.Drafted ||
                    pawn.Downed ||
                    !pawn.Spawned)
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalShouldCheckSidearms.Satisfied for {pawn?.Name?.ToStringShort ?? "unknown"}: {ex.Message}");
                return false;
            }
        }

        public override float GetPriority(Pawn pawn)
        {
            try
            {
                // Priority 5.4 - check before starting work (5.5)
                return Satisfied(pawn) ? 5.4f : 0f;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalShouldCheckSidearms.GetPriority: {ex.Message}");
                return 0f;
            }
        }
    }
}
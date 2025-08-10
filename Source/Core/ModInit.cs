// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Mod initialization, think tree injection, and retry logic
// Critical: Contains think tree nodes and injection logic
// Uses: AutoArmSettings, SimpleSidearmsCompat
// Note: Pre-injection validation ensures compatibility with other mods

using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Patches;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
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
        internal const int MaxRetryAttempts = Constants.MaxThinkTreeRetryAttempts;
        internal static Harmony harmonyInstance;

        static AutoArmInit()
        {
            harmonyInstance = new Harmony("Snues.AutoArm");

            // Apply patches with better error handling
            try
            {
                harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (HarmonyException hex)
            {
                // Log specific harmony exception but continue
                if (hex.Message.Contains("AllowedToAutomaticallyDrop"))
                {
                    // Expected in some versions - not critical
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] OutfitForcedHandler patch skipped (expected in some RimWorld versions)");
                    }
                }
                else
                {
                    Log.Warning($"[AutoArm] Some patches may have failed: {hex.Message}");
                }
            }
            catch (Exception ex)
            {
                // Log but continue - some patches might have succeeded
                Log.Warning($"[AutoArm] Some patches failed during initialization: {ex.Message}");
            }

            // Log initialization first
            Log.Message("<color=#4287f5>[AutoArm]</color> Initialized successfully");

            // Apply manual patches for child weapon restrictions
            try
            {
                AutoArm.Source.Patches.ChildWeaponPatches.ApplyPatches(harmonyInstance);
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Failed to apply child weapon patches: {ex.Message}");
            }

            // Outfit filter patches are now applied via HarmonyPatch attributes
            // No manual patching needed for the new vanilla-style approach
            Log.Message("<color=#4287f5>[AutoArm]</color> Apparel filter patched");

            // Count and report available weapons
            try
            {
                int weaponCount = WeaponValidation.AllWeapons.Count;
                Log.Message($"<color=#4287f5>[AutoArm]</color> {weaponCount} weapons ready for auto-equip");
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Failed to count weapons: {ex.Message}");
            }

            // Initialize debug logging if enabled
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("AutoArm mod initialized with debug logging enabled");
            }
        }
    }

    public static class ModInit
    {
        // Global raid detection for performance
        private static bool _isLargeRaidActive = false;

        private static int _lastRaidCheckTick = 0;
        private const int RaidCheckInterval = 300; // Check every 5 seconds (better performance)

        public static bool IsLargeRaidActive
        {
            get
            {
                // Return false if mod disabled or game not initialized
                if (AutoArmMod.settings?.modEnabled != true || Current.Game == null || Find.TickManager == null)
                    return false;

                int currentTick = Find.TickManager.TicksGame;

                // Only recheck every 15 seconds
                if (currentTick - _lastRaidCheckTick > RaidCheckInterval)
                {
                    _lastRaidCheckTick = currentTick;
                    _isLargeRaidActive = CheckForHighDanger();
                }

                return _isLargeRaidActive;
            }
        }

        private static bool CheckForHighDanger()
        {
            if (Current.Game?.Maps == null)
                return false;

            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome)
                    continue;

                // Use RimWorld's built-in danger assessment
                // This triggers for actual threatening raids, not random hostiles
                if (map.dangerWatcher.DangerRating == StoryDanger.High)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"High danger detected on map {map.uniqueID}. Disabling auto-equip for performance.");
                    }
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class Game_FinalizeInit_InjectThinkTree_Patch
    {
        private static void CheckModCompatibility()
        {
            // Check SimpleSidearms
            if (SimpleSidearmsCompat.IsLoaded())
            {
                // Force initialization to detect reflection failures early
                SimpleSidearmsCompat.EnsureInitialized();

                if (SimpleSidearmsCompat.ReflectionFailed)
                {
                    // Disable sidearm features if reflection failed
                    if (AutoArmMod.settings?.autoEquipSidearms == true)
                    {
                        AutoArmMod.settings.autoEquipSidearms = false;
                        Log.Warning("[AutoArm] SimpleSidearms reflection failed - disabling sidearm auto-equip");
                        AutoArmLogger.Warn("SimpleSidearms integration failed due to reflection errors. Sidearm features have been disabled.");
                    }
                }
                else
                {
                    Log.Message("<color=#4287f5>[AutoArm]</color> SimpleSidearms integration initialized successfully");
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug("SimpleSidearms integration initialized successfully");
                    }
                }
            }
            else if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("SimpleSidearms not detected");
            }

            // Check Combat Extended
            if (CECompat.IsLoaded())
            {
                Log.Message("<color=#4287f5>[AutoArm]</color> Combat Extended detected - ammo checks available");
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    bool ammoSystemEnabled = CECompat.IsAmmoSystemEnabled();
                    AutoArmLogger.Debug($"Combat Extended detected - Ammo system: {(ammoSystemEnabled ? "enabled" : "disabled")}");
                }
            }
            else if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("Combat Extended not detected");
            }

            // Check Infusion 2
            if (InfusionCompat.IsLoaded())
            {
                Log.Message("<color=#4287f5>[AutoArm]</color> Infusion 2 detected - weapon infusion bonuses enabled");
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Infusion 2 compatibility enabled");
                }
            }
            else if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("Infusion 2 not detected");
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            // Clear ALL caches to ensure fresh calculations
            ClearAllCachesOnStartup();

            // Add GameComponent for save/load support
            if (Current.Game != null && !Current.Game.components.Any(c => c is AutoArmGameComponent))
            {
                Current.Game.components.Add(new AutoArmGameComponent(Current.Game));
            }

            // SimpleSidearms integration is now handled through vanilla Equip jobs
            // No patches needed - SS intercepts vanilla jobs automatically

            // Initialize and check mod compatibility
            CheckModCompatibility();

            // No PUAH patches needed - PUAH just hauls, doesn't manage weapons

            // Pre-injection validation
            if (!ValidatePreInjection())
            {
                Log.Error("[AutoArm] Pre-injection validation failed. Think tree injection is required for AutoArm to function.");
                AutoArmLogger.Error("Think tree injection disabled due to pre-validation failure - AutoArm will not function properly!");
                return;
            }

            try
            {
                InjectIntoThinkTree();

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (!ValidateThinkTreeInjection())
                    {
                        Log.Warning("[AutoArm] Think tree injection validation failed - attempting retry...");
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            // Log potentially conflicting mods
                            LogPotentialConflicts();
                        }
                        RetryThinkTreeInjection();
                    }
                    else
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug("[THINK TREE] Think tree injection VALIDATED successfully!");
                        }
                        AutoArmInit.retryAttempts = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Critical error during think tree injection: {ex.Message}");
                if (ex.InnerException != null)
                    Log.Error($"[AutoArm] Inner exception: {ex.InnerException.Message}");

                Log.Error("[AutoArm] Think tree injection failed - AutoArm will not function properly!");
            }
        }

        private static bool ValidatePreInjection()
        {
            try
            {
                // Simplified validation - just check if we can access weapons
                var weaponDefs = WeaponValidation.AllWeapons;
                if (weaponDefs == null || weaponDefs.Count == 0)
                {
                    Log.Warning("[AutoArm] No weapon definitions found during pre-injection validation.");
                    return false;
                }

                // Quick test on a few weapons
                int tested = 0;
                foreach (var weaponDef in weaponDefs.Take(3))
                {
                    try
                    {
                        if (WeaponValidation.IsProperWeapon(weaponDef))
                            tested++;
                    }
                    catch
                    {
                        // Individual weapon failure is OK
                    }
                }

                return tested > 0; // At least one weapon validated
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Pre-injection validation error: {ex.Message}");
                return false;
            }
        }

        private static void RetryThinkTreeInjection()
        {
            if (AutoArmInit.retryAttempts >= AutoArmInit.MaxRetryAttempts)
                return;

            AutoArmInit.retryAttempts++;
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Attempting think tree injection retry {AutoArmInit.retryAttempts}/{AutoArmInit.MaxRetryAttempts}...");
            }

            try
            {
                InjectIntoThinkTree(AutoArmInit.retryAttempts);

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (ValidateThinkTreeInjection())
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[THINK TREE] Think tree injection successful on retry {AutoArmInit.retryAttempts}!");
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
                        Log.Error("[AutoArm] Think tree injection failed after all retries. AutoArm will not function properly!");
                        AutoArmLogger.Error("AutoArm think tree injection failed completely. The mod will not work correctly.");
                        Messages.Message("AutoArm: Think tree injection failed - the mod will not function properly. Please report this issue.", MessageTypeDefOf.NegativeEvent, false);
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

        private static void InjectIntoThinkTree(int attemptNumber = 1)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Starting think tree injection (attempt {attemptNumber})...");
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

            // Log current think tree structure for debugging
            if (AutoArmMod.settings?.debugLogging == true && attemptNumber > 1)
            {
                AutoArmLogger.Debug($"[THINK TREE] Current node count: {mainSorter.subNodes.Count}");
                for (int i = 0; i < Math.Min(10, mainSorter.subNodes.Count); i++)
                {
                    AutoArmLogger.Debug($"[THINK TREE] Node {i}: {mainSorter.subNodes[i].GetType().Name}");
                }
            }

            int insertIndex = DetermineInsertionIndex(mainSorter, attemptNumber);

            // Create nodes - both use the same JobGiver, ThinkNodes handle priority
            var emergencyWeaponNode = new ThinkNode_ConditionalUnarmed();
            var emergencyJobGiver = new JobGiver_PickUpBetterWeapon();  // Use base JobGiver
            emergencyWeaponNode.subNodes = new List<ThinkNode> { emergencyJobGiver };

            mainSorter.subNodes.Insert(insertIndex, emergencyWeaponNode);
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[THINK TREE] Injected emergency weapon node at index {insertIndex} (attempt {attemptNumber})");
            }

            // Normal upgrades - insert at next position
            var weaponUpgradeNode = new ThinkNode_ConditionalArmed();
            var upgradeJobGiver = new JobGiver_PickUpBetterWeapon();  // Same JobGiver
            weaponUpgradeNode.subNodes = new List<ThinkNode> { upgradeJobGiver };
            mainSorter.subNodes.Insert(insertIndex + 1, weaponUpgradeNode);

            // SimpleSidearms integration is now handled directly in JobGiver_PickUpBetterWeapon
            // No separate sidearm node needed - SS patches intercept vanilla Equip jobs

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[THINK TREE] Injected emergency weapon check at index {insertIndex}");
                AutoArmLogger.Debug($"[THINK TREE] Injected weapon upgrade check at index {insertIndex + 1}");
            }
        }

        private static int DetermineInsertionIndex(ThinkNode_PrioritySorter mainSorter, int attemptNumber)
        {
            // Simplified: Always use the same reliable strategy
            // Find first work node and insert before it
            for (int i = 0; i < mainSorter.subNodes.Count; i++)
            {
                if (IsWorkNode(mainSorter.subNodes[i]))
                {
                    return Math.Max(1, i - 1); // Insert before work, but not at index 0
                }
            }

            // If no work node found, insert after critical jobs (index 3-5 typically)
            return Math.Min(4, mainSorter.subNodes.Count);
        }

        private static int GetIndexAfterCriticalJobs(ThinkNode_PrioritySorter mainSorter)
        {
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

            return Math.Min(criticalIndex + 1, mainSorter.subNodes.Count);
        }

        private static int GetIndexBeforeWork(ThinkNode_PrioritySorter mainSorter)
        {
            // Try to insert before work nodes
            for (int i = 0; i < mainSorter.subNodes.Count; i++)
            {
                var node = mainSorter.subNodes[i];
                if (IsWorkNode(node))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[THINK TREE] Found work node at index {i}, inserting before it");
                    }
                    return Math.Max(1, i - 1); // Don't insert at index 0
                }
            }

            // If no work node found, use middle of the tree
            return mainSorter.subNodes.Count / 2;
        }

        private static int GetSafeInsertionIndex(ThinkNode_PrioritySorter mainSorter)
        {
            // Look for a good spot between non-critical nodes
            int bestIndex = -1;

            for (int i = 1; i < mainSorter.subNodes.Count - 1; i++)
            {
                var prevNode = mainSorter.subNodes[i - 1];
                var nextNode = mainSorter.subNodes[i];

                var prevName = prevNode.GetType().Name;
                var nextName = nextNode.GetType().Name;

                // Good spot: after basic needs, before work or leisure
                if ((prevName.Contains("GetFood") || prevName.Contains("GetRest") || prevName.Contains("GetJoy")) &&
                    (nextName.Contains("Work") || nextName.Contains("Joy") || nextName.Contains("Optimize")))
                {
                    bestIndex = i;
                    break;
                }

                // Also good: between two conditional nodes
                if (prevName.StartsWith("ThinkNode_Conditional") && nextName.StartsWith("ThinkNode_Conditional"))
                {
                    bestIndex = i;
                }
            }

            if (bestIndex > 0)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[THINK TREE] Found safe insertion point at index {bestIndex}");
                }
                return bestIndex;
            }

            // Last resort: just after the first few nodes
            return Math.Min(4, mainSorter.subNodes.Count);
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
            if (depth > Constants.MaxThinkTreeSearchDepth) return;

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
                if (sorter.subNodes.Count >= Constants.MinPrioritySorterNodes && AutoArmMod.settings?.debugLogging == true)
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
                else if (sorter.subNodes.Count >= Constants.MinPrioritySorterNodes)
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

                ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundEmergencyNode, ref foundUpgradeNode);

                // Validate core nodes
                if (!foundEmergencyNode || !foundUpgradeNode)
                {
                    Log.Warning($"[AutoArm] Think tree validation failed - Emergency: {foundEmergencyNode}, Upgrade: {foundUpgradeNode}");
                    return false;
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("[THINK TREE] Think tree validation successful - Emergency: " + foundEmergencyNode + ", Upgrade: " + foundUpgradeNode);
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error validating think tree: {e.Message}");
                return false;
            }
        }

        internal static void ValidateThinkNode(ThinkNode node, ref bool foundEmergencyNode, ref bool foundUpgradeNode)
        {
            if (node == null) return;

            if (node is ThinkNode_ConditionalUnarmed)
                foundEmergencyNode = true;

            if (node is ThinkNode_ConditionalArmed)
                foundUpgradeNode = true;

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    ValidateThinkNode(subNode, ref foundEmergencyNode, ref foundUpgradeNode);
                }
            }
        }

        private static void LogPotentialConflicts()
        {
            // Log mods that commonly modify think trees
            var knownThinkTreeMods = new List<string>
            {
                "Hospitality",
                "RunAndGun",
                "DualWield",
                "Yayo's Combat",
                "Combat Extended",
                "Better Infestations",
                "Zombieland",
                "Humanoid Alien Races",
                "Psychology",
                "RimThreaded",
                "Performance Optimizer"
            };

            var loadedConflicts = new List<string>();
            foreach (var modId in knownThinkTreeMods)
            {
                if (ModLister.GetActiveModWithIdentifier(modId) != null ||
                    ModLister.AllInstalledMods.Any(m => m.Active && m.Name.Contains(modId)))
                {
                    loadedConflicts.Add(modId);
                }
            }

            if (loadedConflicts.Any())
            {
                AutoArmLogger.Debug($"[THINK TREE] Potentially conflicting mods detected: {string.Join(", ", loadedConflicts)}");
            }

            // Also log any harmony patches on think tree related methods
            try
            {
                var patchedMethods = Harmony.GetAllPatchedMethods();
                var thinkTreePatches = patchedMethods.Where(m =>
                    m.DeclaringType?.Name.Contains("ThinkNode") == true ||
                    m.DeclaringType?.Name.Contains("ThinkTree") == true ||
                    m.Name.Contains("TryIssueJobPackage"));

                if (thinkTreePatches.Any())
                {
                    AutoArmLogger.Debug("[THINK TREE] Other mods have patched think tree methods:");
                    foreach (var method in thinkTreePatches.Take(10)) // Limit output
                    {
                        var patches = Harmony.GetPatchInfo(method);
                        if (patches != null)
                        {
                            var owners = new List<string>();
                            if (patches.Prefixes != null) owners.AddRange(patches.Prefixes.Select(p => p.owner));
                            if (patches.Postfixes != null) owners.AddRange(patches.Postfixes.Select(p => p.owner));
                            if (patches.Transpilers != null) owners.AddRange(patches.Transpilers.Select(p => p.owner));

                            owners = owners.Distinct().Where(o => o != "Snues.AutoArm").ToList();
                            if (owners.Any())
                            {
                                AutoArmLogger.Debug($"  {method.DeclaringType?.Name}.{method.Name} patched by: {string.Join(", ", owners)}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug($"[THINK TREE] Error checking harmony patches: {e.Message}");
            }
        }

        /// <summary>
        /// Clear all caches on game startup or save load
        /// </summary>
        internal static void ClearAllCachesOnStartup()
        {
            try
            {
                // Clear weapon scoring caches
                WeaponScoringHelper.ClearWeaponScoreCache();  // Base weapon scores
                WeaponScoreCache.ClearCache();                // Pawn-weapon combinations

                // Clear validation caches
                PawnValidationCache.ClearCache();             // Pawn validation & lord jobs
                ValidationHelper.ClearStorageTypeCaches();    // Storage type caches

                // Clear generic cache
                GenericCache.ClearAll();

                // Clear TEMPORARY tracking systems only
                DroppedItemTracker.ClearAll();                // Recently dropped (10-60 sec)
                ForcedWeaponTracker.Clear();                  // 1-second grace period tracker
                AutoEquipTracker.Cleanup();                   // Recent auto-equip jobs
                WeaponBlacklist.ClearAll();                   // 60-second blacklist

                // DO NOT CLEAR ForcedWeaponHelper - it stores player's manual choices!
                // ForcedWeaponHelper data is loaded from save and must persist

                // Mark weapon location caches as changed (they'll rebuild from world state)
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        ImprovedWeaponCacheManager.MarkCacheAsChanged(map);
                    }
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Cleared temporary caches on startup/load - preserved player forced weapons");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to clear caches on startup", e);
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

            // Clear ALL caches when loading a save
            Game_FinalizeInit_InjectThinkTree_Patch.ClearAllCachesOnStartup();

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

                Game_FinalizeInit_InjectThinkTree_Patch.ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundEmergencyNode, ref foundUpgradeNode);

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

    /// <summary>
    /// Unified ThinkNode for weapon status checks - handles both armed and unarmed colonists
    /// Priority: Configurable (6.9 for unarmed, 5.6 for armed)
    /// </summary>
    public class ThinkNode_ConditionalWeaponStatus : ThinkNode_Conditional
    {
        public bool requireArmed = false;  // Set when creating the node
        public new float priority = 5.6f;   // Set when creating the node - 'new' to hide base class field

        // Track evaluation failures for problematic pawns
        private static Dictionary<Pawn, int> pawnEvaluationFailures = new Dictionary<Pawn, int>();

        private static HashSet<Pawn> loggedCriticalPawns = new HashSet<Pawn>();

        protected override bool Satisfied(Pawn pawn)
        {
            // Keep try/catch for safety - this is too critical to risk
            try
            {
                // OPTIMIZATION: Cache settings & flags locally (reduces repeated property access)
                var settings = AutoArmMod.settings;
                if (settings == null || settings.modEnabled != true)
                    return false;

                bool debug = settings.debugLogging == true;
                bool disableDuringRaids = settings.disableDuringRaids == true;

                if (disableDuringRaids)
                {
                    // cache raid state in a local
                    bool raidActive = ModInit.IsLargeRaidActive;
                    if (raidActive)
                    {
                        if (debug && pawn.IsHashIntervalTick(600))
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Skipping - raid active");
                        }
                        return false;
                    }
                }

                // ALWAYS validate first (preserves original behavior)
                if (!CanConsiderWeapons(pawn))
                    return false;

                // OPTIMIZATION: Cache Primary reference (small but helps)
                var primary = pawn.equipment?.Primary;
                bool isArmed = primary?.def?.IsWeapon == true;  // Safe null handling

                bool matchesRequirement = requireArmed ? isArmed : !isArmed;

                // Debug logging only when needed
                if (debug && !isArmed && !requireArmed && pawn.IsHashIntervalTick(300))
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Detected as UNARMED");
                }
                return matchesRequirement;
            }
            catch (Exception ex)
            {
                // Track failure count
                pawnEvaluationFailures.TryGetValue(pawn, out int count);
                pawnEvaluationFailures[pawn] = count + 1;

                // Only log if this pawn has failed many times
                if (count + 1 > Constants.PawnEvaluationCriticalThreshold &&
                    !loggedCriticalPawns.Contains(pawn))
                {
                    // NOW INCLUDES: Exception type and message for debugging
                    Log.Error($"[AutoArm] Multiple failures for {pawn?.Name?.ToStringShort ?? "unknown"}: {ex.GetType().Name} - {ex.Message}");
                    loggedCriticalPawns.Add(pawn);

                    // Optional: Log stack trace only if debug mode is on
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Error($"[AutoArm] Stack trace: {ex.StackTrace}");
                    }
                }
                return false;
            }
        }

        private bool CanConsiderWeapons(Pawn pawn)
        {
            // Use the unified cached validation system
            // This handles all the same checks but with intelligent caching:
            // - Dynamic properties (dead/downed/drafted/jobs) are always checked
            // - Stable properties (race/capabilities/age) are cached
            // - Lord job participation uses O(1) HashSet lookup
            // - Automatic cache invalidation via Harmony patches

            // Check if this pawn has been failing repeatedly (keep this check here for safety)
            if (pawnEvaluationFailures.TryGetValue(pawn, out int failCount) &&
                failCount > Constants.PawnEvaluationFailureThreshold)
            {
                return false;
            }

            return PawnValidationCache.CanConsiderWeapons(pawn);
        }

        public override float GetPriority(Pawn pawn)
        {
            try
            {
                // Quick exit if mod disabled (cache locally)
                var settings = AutoArmMod.settings;
                if (settings == null || settings.modEnabled != true)
                    return 0f;

                return Satisfied(pawn) ? priority : Constants.DefaultThinkNodePriority;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalWeaponStatus.GetPriority: {ex.Message}");
                return 0f;
            }
        }

        // For testing
        public bool TestSatisfied(Pawn pawn)
        {
            return Satisfied(pawn);
        }

        /// <summary>
        /// Clean up tracking for dead pawns
        /// </summary>
        public static void CleanupDeadPawns()
        {
            // Clean up validation cache (includes lord job cache)
            PawnValidationCache.CleanupDeadPawns();

            // Clean up evaluation failures
            var deadPawns = pawnEvaluationFailures.Keys
                .Where(p => p == null || p.Destroyed || p.Dead)
                .ToList();

            foreach (var pawn in deadPawns)
            {
                pawnEvaluationFailures.Remove(pawn);
            }

            // Also clear pawns with excessive failures (they're probably permanently problematic)
            var problematicPawns = pawnEvaluationFailures
                .Where(kvp => kvp.Value > Constants.PawnEvaluationExcessiveThreshold)
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

    // Keep these as thin wrappers for backwards compatibility and easier debugging
    public class ThinkNode_ConditionalUnarmed : ThinkNode_ConditionalWeaponStatus
    {
        public ThinkNode_ConditionalUnarmed()
        {
            requireArmed = false;
            priority = Constants.EmergencyWeaponPriority; // 6.9
        }
    }

    public class ThinkNode_ConditionalArmed : ThinkNode_ConditionalWeaponStatus
    {
        public ThinkNode_ConditionalArmed()
        {
            requireArmed = true;
            priority = Constants.WeaponUpgradePriority; // 5.6
        }
    }

    // JobGiver_PickUpBetterWeapon_Emergency removed - base JobGiver handles both cases
}
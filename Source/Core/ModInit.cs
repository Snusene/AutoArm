
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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
            // Wire up handlers
            WireUpTickScheduler();

            harmonyInstance = new Harmony("Snues.AutoArm");

            Patches.ConditionalPatcher.Initialize(harmonyInstance);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                int patchCount = harmonyInstance.GetPatchedMethods().Count();
                AutoArmLogger.Debug(() => $"Patched {patchCount} methods");
            }


            AutoArmLogger.Info("Initializing...");
        }

        private static void WireUpTickScheduler()
        {
            TickScheduler.OnDroppedItemExpired = DroppedItems.OnItemExpiredEvent;
            TickScheduler.OnForcedWeaponGraceCheck = ForcedWeaponState.OnGraceCheckEvent;
            TickScheduler.OnSimpleSidearmsExpired = SimpleSidearmsCompat.OnValidationExpiredEvent;
            TickScheduler.OnMessageCacheExpired = JobGiver_PickUpBetterWeapon.OnMessageCacheExpiredEvent;
            // Per-map handling
        }
    }

    internal static class ModInit
    {
        public static bool IsLargeRaidActive => RaidChecker.ShouldSkipDuringRaid(Find.CurrentMap);
    }

    [HarmonyPatch(typeof(Root_Play), "Start")]
    [HarmonyPatchCategory(Patches.PatchCategories.Core)]
    internal static class Game_FinalizeInit_InjectThinkTree_Patch
    {
        private static bool thinkTreeInjected = false;


        private static void CheckModCompatibility()
        {
            JobGiver_PickUpBetterWeapon.InitializeModExtensionCache();

            if (SimpleSidearmsCompat.IsLoaded)
            {
                SimpleSidearmsCompat.EnsureInitialized();

                if (SimpleSidearmsCompat.ReflectionFailed)
                {
                    if (AutoArmMod.settings != null && AutoArmMod.settings.autoEquipSidearms)
                    {
                        AutoArmMod.settings.autoEquipSidearms = false;
                        AutoArmLogger.Warn("SimpleSidearms reflection failed, disabling sidearm auto equip");
                    }
                }
                else
                {
                    AutoArmLogger.Debug(() => "SimpleSidearms integration ready");
                }
            }
            else AutoArmLogger.Debug(() => "SimpleSidearms not detected");

            if (CECompat.IsLoaded)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    bool ammoSystemEnabled = CECompat.IsAmmoSystemEnabled();
                    AutoArmLogger.Debug(() => $"CombatExtended detected, ammo system {AutoArmLogger.FormatBool(ammoSystemEnabled)}");
                }
            }

        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            AutoArm.Helpers.ForcedWeaponLabelHelper.ResetFieldChecking();

            ClearAllCachesOnStartup(true);

            PreWarmCachesOnStartup();

            if (Current.Game != null && !GenCollection.Any(Current.Game.components, c => c is AutoArmGameComponent))
            {
                Current.Game.components.Add(new AutoArmGameComponent(Current.Game));
            }

            CheckModCompatibility();

            if (thinkTreeInjected)
            {
                AutoArmLogger.Debug(() => "Think tree already injected (static flag set), skipping injection");
                return;
            }

            if (ValidateThinkTreeInjection(logWarning: false))
            {
                AutoArmLogger.Debug(() => "Think tree already has AutoArm nodes, skipping injection");
                thinkTreeInjected = true;
                return;
            }

            if (!ValidatePreInjection())
            {
                AutoArmLogger.Error("Pre-injection validation failed, think tree injection skipped");
                return;
            }

            try
            {
                InjectIntoThinkTree();
                InjectIntoExtraThinkTrees();
                thinkTreeInjected = true;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (!ValidateThinkTreeInjection(logWarning: true))
                    {
                        AutoArmLogger.Warn("Think tree injection validation failed - attempting retry...");
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            LogPotentialConflicts();
                        }
                        RetryThinkTreeInjection();
                    }
                    else
                    {
                        AutoArmInit.retryAttempts = 0;

                        FinalizeInitialization();
                    }
                });
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("Critical error during think tree injection", ex);
                if (ex.InnerException != null)
                    AutoArmLogger.Error("Inner exception during think tree injection", ex.InnerException);

                AutoArmLogger.Error("Think tree injection failed");
            }
        }

        private static bool ValidatePreInjection()
        {
            try
            {
                var weaponDefs = Validation.AllWeapons;
                if (weaponDefs == null || weaponDefs.Count == 0)
                {
                    AutoArmLogger.Warn("No weapon defs found during pre-injection validation");
                    return false;
                }

                int tested = 0;
                int weaponCount = 0;
                foreach (var weaponDef in weaponDefs)
                {
                    if (weaponCount++ >= 3) break;
                    try
                    {
                        if (Validation.IsWeapon(weaponDef))
                            tested++;
                    }
                    catch (Exception ex)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug(() => $"IsWeapon probe failed for {weaponDef?.defName ?? "null"}: {ex.Message}");
                        }
                    }
                }

                return tested > 0;
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("Pre-injection validation error", ex);
                return false;
            }
        }


        private static void FinalizeInitialization()
        {
            try
            {
                int weaponCount = Validation.AllWeapons.Count;
                AutoArmLogger.Info($"{weaponCount} weapons ready for auto equip");
            }
            catch (Exception ex)
            {
                AutoArmLogger.Warn($"Failed to count weapons during finalization: {ex.GetType().Name}: {ex.Message}");
                AutoArmLogger.Info("Ready for auto equip");
            }
        }

        private static void RetryThinkTreeInjection()
        {
            if (AutoArmInit.retryAttempts >= AutoArmInit.MaxRetryAttempts)
                return;

            AutoArmInit.retryAttempts++;
            AutoArmLogger.Debug(() => $"Attempting think tree injection retry {AutoArmInit.retryAttempts}/{AutoArmInit.MaxRetryAttempts}...");

            try
            {
                InjectIntoThinkTree(AutoArmInit.retryAttempts);

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (ValidateThinkTreeInjection(logWarning: false))
                    {
                        AutoArmLogger.Debug(() => $"Think tree injection successful on retry {AutoArmInit.retryAttempts}!");
                        AutoArmInit.retryAttempts = 0;

                        FinalizeInitialization();
                    }
                    else if (AutoArmInit.retryAttempts < AutoArmInit.MaxRetryAttempts)
                    {
                        LongEventHandler.QueueLongEvent(() => RetryThinkTreeInjection(),
                            "AutoArm", false, null);
                    }
                    else
                    {
                        AutoArmLogger.Error("Think tree injection failed after all retries");
                        Messages.Message("AutoArm_ThinkTreeInjectionFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                    }
                });
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Think tree injection retry {AutoArmInit.retryAttempts} failed: {e.Message}");

                if (AutoArmInit.retryAttempts >= AutoArmInit.MaxRetryAttempts)
                {
                    AutoArmLogger.Warn("All retries exhausted, using fallback");
                }
            }
        }

        private static void InjectIntoThinkTree(int attemptNumber = 1)
        {
            AutoArmLogger.Debug(() => $"Starting injection (attempt {attemptNumber})");

            var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (humanlikeThinkTree?.thinkRoot == null)
            {
                AutoArmLogger.Error("Could not find Humanlike think tree!");
                return;
            }

            if (!InjectIntoTree(humanlikeThinkTree, attemptNumber))
                AutoArmLogger.Error("Failed to find main priority sorter in think tree!");
        }

        private static bool InjectIntoTree(ThinkTreeDef tree, int attemptNumber = 1)
        {
            if (tree?.thinkRoot == null)
                return false;

            bool alreadyHasNode = false;
            ValidateThinkNode(tree.thinkRoot, ref alreadyHasNode);
            if (alreadyHasNode)
                return true;

            ThinkNode_PrioritySorter mainSorter = null;
            FindMainPrioritySorter(tree.thinkRoot, ref mainSorter);

            if (mainSorter == null)
                return false;

            if (AutoArmMod.settings?.debugLogging == true && attemptNumber > 1)
            {
                AutoArmLogger.Debug(() => $"Current node count: {mainSorter.subNodes.Count}");
                for (int i = 0; i < Math.Min(10, mainSorter.subNodes.Count); i++)
                {
                    AutoArmLogger.Debug(() => $"Node {i}: {mainSorter.subNodes[i].GetType().Name}");
                }
            }

            int insertIndex = DetermineInsertionIndex(mainSorter, attemptNumber);

            var weaponManagementNode = new ThinkNode_ConditionalWeaponStatus();
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            weaponManagementNode.subNodes = new List<ThinkNode> { jobGiver };

            mainSorter.subNodes.Insert(insertIndex, weaponManagementNode);
            AutoArmLogger.Debug(() => $"Injected weapon management node into '{tree.defName}' at index {insertIndex}, priority 5.7 (attempt {attemptNumber})");

            return true;
        }

        private static void InjectIntoExtraThinkTrees()
        {
            var trees = DefDatabase<ThinkTreeDef>.AllDefsListForReading;
            int injected = 0;

            for (int i = 0; i < trees.Count; i++)
            {
                var tree = trees[i];
                if (tree == null || tree.defName == "Humanlike")
                    continue;

                try
                {
                    bool alreadyHasNode = false;
                    ValidateThinkNode(tree.thinkRoot, ref alreadyHasNode);
                    if (alreadyHasNode)
                        continue;

                    ThinkNode_PrioritySorter sorter = null;
                    FindMainPrioritySorter(tree.thinkRoot, ref sorter);
                    if (sorter == null)
                        continue;

                    if (InjectIntoTree(tree))
                        injected++;
                }
                catch (Exception e)
                {
                    AutoArmLogger.Warn($"Think tree injection skipped for '{tree.defName}': {e.Message}");
                }
            }

            if (injected > 0)
                AutoArmLogger.Debug(() => $"Extended weapon injection into {injected} additional think tree(s)");
        }

        private static int DetermineInsertionIndex(ThinkNode_PrioritySorter mainSorter, int attemptNumber)
        {
            for (int i = 0; i < mainSorter.subNodes.Count; i++)
            {
                if (IsWorkNode(mainSorter.subNodes[i]))
                {
                    return Math.Max(1, i - 1);
                }
            }

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

            if (IsIrrelevantPath(path))
            {
                path.RemoveAt(path.Count - 1);
                return;
            }

            if (node is ThinkNode_PrioritySorter sorter && IsMainColonistSorter(sorter, path))
            {
                result = sorter;
                LogSorterFound(sorter, depth, path);
                path.RemoveAt(path.Count - 1);
                return;
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

        private static bool IsIrrelevantPath(List<string> path)
        {
            return GenCollection.Any(path, p =>
                p.Contains("JoinVoluntarilyJoinableLord") ||
                p.Contains("ConditionalHasLordDuty") ||
                p.Contains("ConditionalPawnKind") ||
                p.Contains("ConditionalPrisoner") ||
                p.Contains("ConditionalGuest"));
        }

        private static bool IsMainColonistSorter(ThinkNode_PrioritySorter sorter, List<string> path)
        {
            if (sorter?.subNodes == null || sorter.subNodes.Count < Constants.MinPrioritySorterNodes)
                return false;

            bool isUnderSubtree = path.Contains("ThinkNode_Subtree") || path.Contains("ThinkNode_SubtreesByTag");
            if (!isUnderSubtree)
                return false;

            return HasEssentialColonistNodes(sorter);
        }

        private static bool HasEssentialColonistNodes(ThinkNode_PrioritySorter sorter)
        {
            bool hasGetFood = false;
            bool hasGetRest = false;
            bool hasWork = false;

            foreach (var node in sorter.subNodes)
            {
                var typeName = node.GetType().Name;
                if (typeName == "JobGiver_GetFood") hasGetFood = true;
                else if (typeName == "JobGiver_GetRest") hasGetRest = true;
                else if (typeName == "JobGiver_Work") hasWork = true;
            }

            return hasGetFood && hasGetRest && hasWork;
        }

        private static void LogSorterFound(ThinkNode_PrioritySorter sorter, int depth, List<string> path)
        {
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            AutoArmLogger.Debug(() => $"Found main colonist priority sorter at depth {depth} with {sorter.subNodes.Count} nodes");
            AutoArmLogger.Debug(() => $"Path: {string.Join(" / ", path)}");

            var nodeTypes = new List<string>();
            int count = 0;
            foreach (var n in sorter.subNodes)
            {
                if (count++ >= 5) break;
                nodeTypes.Add(n.GetType().Name);
            }
            AutoArmLogger.Debug(() => $"First 5 nodes: {string.Join(", ", nodeTypes)}");
        }

        public static bool ValidateThinkTreeInjection(bool logWarning = false)
        {
            try
            {
                var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
                if (humanlikeThinkTree?.thinkRoot == null)
                    return false;

                bool foundWeaponNode = false;

                ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundWeaponNode);

                if (!foundWeaponNode)
                {
                    if (logWarning)
                    {
                        AutoArmLogger.Warn("Think tree validation failed - no weapon node found");
                    }
                    return false;
                }

                AutoArmLogger.Debug(() => "ThinkTree validated");
                

                return true;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error validating think tree", e);
                return false;
            }
        }

        internal static void ValidateThinkNode(ThinkNode node, ref bool foundWeaponNode)
        {
            if (node == null) return;

            if (node is ThinkNode_ConditionalWeaponStatus)
                foundWeaponNode = true;

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    ValidateThinkNode(subNode, ref foundWeaponNode);
                }
            }
        }

        private static class HarmonyConflictCache
        {
            private static Dictionary<string, List<string>> cachedConflicts = null;
            private static int lastCacheUpdateTick = -1;
            private const int CacheDurationTicks = 3600;

            public static void LogConflicts()
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (cachedConflicts != null && currentTick - lastCacheUpdateTick < CacheDurationTicks)
                {
                    LogCachedConflicts();
                    return;
                }

                cachedConflicts = new Dictionary<string, List<string>>();

                CheckKnownMods();

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    CheckHarmonyPatches();
                }

                lastCacheUpdateTick = currentTick;
                LogCachedConflicts();
            }

            private static void CheckKnownMods()
            {
                var knownThinkTreeMods = new[] {
                    "Hospitality", "RunAndGun", "DualWield", "Yayo's Combat",
                    "Combat Extended", "Psychology", "RimThreaded", "Performance Optimizer"
                };

                var loaded = new List<string>();
                foreach (var modId in knownThinkTreeMods)
                {
                    if (ModLister.GetActiveModWithIdentifier(modId) != null ||
                        ModLister.AllInstalledMods.Any(m => m.Active && m.Name.Contains(modId)))
                    {
                        loaded.Add(modId);
                    }
                }

                if (loaded.Any())
                    cachedConflicts["LoadedMods"] = loaded;
            }

            private static void CheckHarmonyPatches()
            {
                try
                {
                    var thinkTreePatches = new List<string>();

                    foreach (var method in Harmony.GetAllPatchedMethods())
                    {
                        if (method.DeclaringType?.Name.Contains("ThinkNode") != true &&
                            method.DeclaringType?.Name.Contains("ThinkTree") != true &&
                            !method.Name.Contains("TryIssueJobPackage"))
                            continue;

                        var patches = Harmony.GetPatchInfo(method);
                        if (patches == null) continue;

                        var owners = new HashSet<string>();
                        if (patches.Prefixes != null)
                            foreach (var p in patches.Prefixes)
                                owners.Add(p.owner);
                        if (patches.Postfixes != null)
                            foreach (var p in patches.Postfixes)
                                owners.Add(p.owner);
                        if (patches.Transpilers != null)
                            foreach (var p in patches.Transpilers)
                                owners.Add(p.owner);

                        owners.Remove("Snues.AutoArm");

                        if (owners.Any())
                        {
                            thinkTreePatches.Add($"{method.DeclaringType?.Name}.{method.Name}: {string.Join(", ", owners)}");
                            if (thinkTreePatches.Count >= 10) break;
                        }
                    }

                    if (thinkTreePatches.Any())
                        cachedConflicts["ThinkTreePatches"] = thinkTreePatches;
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"Error checking patches: {e.Message}");
                }
            }

            private static void LogCachedConflicts()
            {
                foreach (var kvp in cachedConflicts)
                {
                    if (kvp.Key == "LoadedMods")
                        AutoArmLogger.Debug(() => $"Potentially conflicting mods: {string.Join(", ", kvp.Value)}");
                    else if (kvp.Key == "ThinkTreePatches")
                    {
                        AutoArmLogger.Debug(() => "Other mods have patched think tree methods:");
                        foreach (var p in kvp.Value)
                            AutoArmLogger.Debug(() => $"  {p}");
                    }
                }
            }
        }

        private static void LogPotentialConflicts()
        {
            HarmonyConflictCache.LogConflicts();
        }


        internal static void ClearAllCachesOnStartup(bool isNewGame = false)
        {
            try
            {
                Scoring.ClearWeaponScoreCache();

                WeaponCache.ClearAllCaches();

                Validation.ClearWeaponDefCache();

                PawnValidation.ClearCache();

                AutoArm.UI.StatusOverviewDataGatherer.ClearTopWeaponsCache();
                AutoArm.UI.DebugPanel.ResetState();

                DroppedItems.ClearAll();
                ForcedWeaponState.Clear();
                AutoEquipState.Cleanup(force: true);
                Blacklist.ClearAll();
                JobGiver_PickUpBetterWeapon.CleanupMessageCache();
                JobGiver_PickUpBetterWeapon.ClearMeleeDpsCache();


                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        WeaponCache.MarkCacheAsChanged(map);
                    }
                }

                AutoArmLogger.Debug(() => "Cleared temporary caches on startup/load");
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorCleanup(e, "ClearCachesOnStartup");
            }
        }


        internal static void PreWarmCachesOnStartup()
        {
            try
            {
                AutoArmLogger.Debug(() => "Pre-warming caches on startup");

                Scoring.PreCalcWeapons();

                AutoArmLogger.Debug(() => "Cache pre-warming complete");
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorCleanup(e, "PreWarmCachesOnStartup");
            }
        }
    }

    internal static class Game_LoadGame_InjectThinkTree_Patch
    {
        public static void Postfix()
        {
            AutoArmInit.retryAttempts = 0;

            AutoArm.Helpers.ForcedWeaponLabelHelper.ResetFieldChecking();

            Game_FinalizeInit_InjectThinkTree_Patch.ClearAllCachesOnStartup(false);

            if (!ValidateThinkTreeInjection())
            {
                Game_FinalizeInit_InjectThinkTree_Patch.Postfix();
            }
            else
            {
                AutoArmLogger.Debug(() => "Think tree already injected, skipping re-injection on load");
            }

            SimpleSidearmsCompat.CleanupAfterLoad();
        }

        private static bool ValidateThinkTreeInjection()
        {
            return Game_FinalizeInit_InjectThinkTree_Patch.ValidateThinkTreeInjection(logWarning: false);
        }
    }

    public class ThinkNode_ConditionalWeaponStatus : ThinkNode_Priority
    {
        private readonly WeaponStatusEvaluator evaluator = new WeaponStatusEvaluator();

        public override float GetPriority(Pawn pawn)
        {
            return evaluator.EvaluatePriority(pawn);
        }
    }


    internal class WeaponStatusEvaluator
    {
        public float EvaluatePriority(Pawn pawn)
        {
            if (!BasicPawnValidation.IsValid(pawn))
                return 0f;

            if (RaidChecker.ShouldSkipDuringRaid(pawn.Map))
                return 0f;

            if (!PawnValidation.CanConsiderWeapons(pawn))
            {
                return 0f;
            }

            return 5.7f;
        }
    }


    internal static class BasicPawnValidation
    {
        public static bool IsValid(Pawn pawn)
        {
            if (pawn?.Spawned != true) return false;
            if (AutoArmMod.settings?.modEnabled != true) return false;
            if (pawn.Dead || pawn.Downed) return false;
            return true;
        }
    }


    internal static class RaidChecker
    {
        public static bool ShouldSkipDuringRaid(Map map)
        {
            return AutoArmMod.settings?.disableDuringRaids == true &&
                   map?.dangerWatcher?.DangerRating == StoryDanger.High;
        }
    }


    [Obsolete("Use ThinkNode_ConditionalWeaponStatus instead - kept for backward compatibility")]
    public sealed class ThinkNode_ConditionalUnarmed : ThinkNode_ConditionalWeaponStatus
    {
        public ThinkNode_ConditionalUnarmed()
        {
        }
    }

    [Obsolete("Use ThinkNode_ConditionalWeaponStatus instead - kept for backward compatibility")]
    public sealed class ThinkNode_ConditionalArmed : ThinkNode_ConditionalWeaponStatus
    {
        public ThinkNode_ConditionalArmed()
        {
        }
    }
}


using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
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
            // Wire up unified tick scheduler event handlers
            WireUpTickScheduler();

            harmonyInstance = new Harmony("Snues.AutoArm");

            Patches.ConditionalPatcher.Initialize(harmonyInstance);

            try
            {

                if (Testing.TestRunner.IsRunningTests)
                {
                    harmonyInstance.PatchCategory(Patches.PatchCategories.Testing);
                    AutoArmLogger.Debug(() => "Applied Testing patches (test mode active)");
                }

                var appliedPatches = harmonyInstance.GetPatchedMethods();
                bool hasSpawnSetup = false;
                bool hasGenSpawn = false;

                foreach (var method in appliedPatches)
                {
                    if (method.Name == "SpawnSetup" && method.DeclaringType == typeof(Thing))
                        hasSpawnSetup = true;
                    if (method.Name == "Spawn" && method.DeclaringType == typeof(GenSpawn))
                        hasGenSpawn = true;
                }

                if (!hasSpawnSetup)
                {
                    AutoArmLogger.Warn("[CRITICAL] Thing.SpawnSetup patch not applied - attempting manual patch!");
                    try
                    {
                        var targetMethod = AccessTools.Method(typeof(Thing), "SpawnSetup", new[] { typeof(Map), typeof(bool) });
                        var postfix = AccessTools.Method(typeof(AutoArm.Thing_SpawnSetup_Patch), "Postfix");
                        if (targetMethod != null && postfix != null)
                        {
                            harmonyInstance.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                            AutoArmLogger.Debug(() => "Manually applied Thing.SpawnSetup patch");
                        }
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.ErrorPatch(e, "Thing.SpawnSetup_ManualPatch");
                    }
                }

                if (!hasGenSpawn)
                {
                    AutoArmLogger.Warn("[CRITICAL] GenSpawn.Spawn patch not applied - attempting manual patch!");
                    try
                    {
                        var targetMethod = AccessTools.Method(typeof(GenSpawn), "Spawn",
                            new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) });
                        var postfix = AccessTools.Method(typeof(AutoArm.GenSpawn_Spawn_Patch), "Postfix");
                        if (targetMethod != null && postfix != null)
                        {
                            harmonyInstance.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                            AutoArmLogger.Debug(() => "Manually applied GenSpawn.Spawn patch");
                        }
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.ErrorPatch(e, "GenSpawn.Spawn_ManualPatch");
                    }
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    int patchCount = appliedPatches.Count();
                    AutoArmLogger.Debug(() => $"Patched {patchCount} methods");
                }

            }
            catch (HarmonyException hex)
            {
                if (hex.Message.Contains("AllowedToAutomaticallyDrop"))
                {
                    AutoArmLogger.Debug(() => "OutfitForcedHandler patch skipped (expected in some RimWorld versions)");
                }
                else if (hex.Message.Contains("Patching exception in method null"))
                {
                    AutoArmLogger.Warn($"A patch tried to target a method that doesn't exist. This is often due to game version differences.");
                    AutoArmLogger.Warn($"Full error: {hex.Message}");
                    if (hex.InnerException != null)
                    {
                        AutoArmLogger.Warn($"Inner exception: {hex.InnerException.Message}");
                    }
                    if (AutoArmMod.settings?.debugLogging == true && hex.StackTrace != null)
                    {
                        AutoArmLogger.Debug(() => $"Stack trace: {hex.StackTrace}");
                    }
                }
                else
                {
                    AutoArmLogger.Warn($"Some patches may have failed: {hex.Message}");
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Warn($"Some patches failed during initialization: {ex.Message}");
                if (AutoArmMod.settings?.debugLogging == true && ex.StackTrace != null)
                {
                    AutoArmLogger.Debug(() => $"Stack trace: {ex.StackTrace}");
                }
            }


            Log.Message("<color=#4287f5>[AutoArm]</color> Initializing...");

            if (Patches.ConditionalPatcher.IsCategoryEnabled(Patches.PatchCategories.AgeRestrictions))
            {
                try
                {
                    AutoArm.Patches.ChildWeaponPatches.ApplyPatches(harmonyInstance);
                }
                catch (Exception ex)
                {
                    AutoArmLogger.Error("Failed to apply child weapon patches", ex);
                }
            }

        }

        /// <summary>
        /// Wire up unified tick scheduler - single dictionary lookup replaces 6+ per tick
        /// </summary>
        private static void WireUpTickScheduler()
        {
            TickScheduler.OnCooldownExpired = CooldownMetrics.OnCooldownExpiredEvent;
            TickScheduler.OnDroppedItemExpired = DroppedItemTracker.OnItemExpiredEvent;
            TickScheduler.OnBlacklistExpired = WeaponBlacklist.OnBlacklistExpiredEvent;
            TickScheduler.OnForcedWeaponGraceCheck = ForcedWeaponState.OnGraceCheckEvent;
            TickScheduler.OnSkillCacheExpired = WeaponScoringHelper.OnSkillCacheExpiredEvent;
            TickScheduler.OnSimpleSidearmsExpired = SimpleSidearmsCompat.OnValidationExpiredEvent;
            TickScheduler.OnMessageCacheExpired = JobGiver_PickUpBetterWeapon.OnMessageCacheExpiredEvent;
            // ReservationExpiry and TempBlacklistExpiry handled per-map via MapComponent
        }
    }

    public static class ModInit
    {
        private static readonly RaidChecker raidChecker = new RaidChecker();

        public static bool IsLargeRaidActive => raidChecker.ShouldSkipDuringRaid();
    }

    [HarmonyPatch(typeof(Root_Play), "Start")]
    [HarmonyPatchCategory(Patches.PatchCategories.Core)]
    public static class Game_FinalizeInit_InjectThinkTree_Patch
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
                        AutoArmLogger.Warn("SimpleSidearms reflection failed - disabling sidearm auto-equip");
                        AutoArmLogger.Warn("SimpleSidearms integration failed due to reflection errors. Sidearm features have been disabled.");
                    }
                }
                else
                {
                    AutoArmLogger.Debug(() => "SimpleSidearm integration successful");
                }
            }
            else AutoArmLogger.Debug(() => "SimpleSidearms not detected");

            if (CECompat.IsLoaded())
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

            AutoArm.ForcedWeaponLabelHelper.ResetFieldChecking();

            ClearAllCachesOnStartup(true);

            PreWarmCachesOnStartup();

            if (Current.Game != null && !GenCollection.Any(Current.Game.components, c => c is AutoArmGameComponent))
            {
                Current.Game.components.Add(new AutoArmGameComponent(Current.Game));
            }

            CheckModCompatibility();

            if (AutoArm.Compatibility.PocketSandCompat.Active)
            {
            }

            if (!ValidatePreInjection())
            {
                AutoArmLogger.Error("Pre-injection validation failed. Think tree injection is required for Auto Arm to function.");
                AutoArmLogger.Error("Think tree injection disabled due to pre-validation failure - Auto Arm will not function properly!");
                return;
            }

            try
            {
                InjectIntoThinkTree();
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

                AutoArmLogger.Error("Think tree injection failed - AutoArm will not function properly!");
            }
        }

        private static bool ValidatePreInjection()
        {
            try
            {
                var weaponDefs = WeaponValidation.AllWeapons;
                if (weaponDefs == null || weaponDefs.Count == 0)
                {
                    AutoArmLogger.Warn("No weapon definitions found during pre-injection validation.");
                    return false;
                }

                int tested = 0;
                int weaponCount = 0;
                foreach (var weaponDef in weaponDefs)
                {
                    if (weaponCount++ >= 3) break;
                    try
                    {
                        if (WeaponValidation.IsWeapon(weaponDef))
                            tested++;
                    }
                    catch
                    {
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
                int weaponCount = WeaponValidation.AllWeapons.Count;
                Log.Message($"<color=#4287f5>[AutoArm]</color> {weaponCount} weapons ready for auto equip");
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("Failed to count weapons during finalization", ex);
                Log.Message("<color=#4287f5>[AutoArm]</color> Ready for auto equip");
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
                        AutoArmLogger.Error("Think tree injection failed after all retries. AutoArm will not function properly!");
                        AutoArmLogger.Error("Auto Arm think tree injection failed completely. The mod will not work correctly.");
                        Messages.Message("AutoArm_ThinkTreeInjectionFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                    }
                });
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Think tree injection retry {AutoArmInit.retryAttempts} failed: {e.Message}");

                if (AutoArmInit.retryAttempts >= AutoArmInit.MaxRetryAttempts)
                {
                    AutoArmLogger.Warn("All retry attempts exhausted. Using fallback mode.");
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

            ThinkNode_PrioritySorter mainSorter = null;
            FindMainPrioritySorter(humanlikeThinkTree.thinkRoot, ref mainSorter);

            if (mainSorter == null)
            {
                AutoArmLogger.Error("Failed to find main priority sorter in think tree!");
                return;
            }

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
            AutoArmLogger.Debug(() => $"Injected weapon management node at index {insertIndex}, priority 5.7 (attempt {attemptNumber})");

            AutoArmLogger.Debug(() => $"Weapon management system active with priority 5.7");
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
            AutoArmLogger.Debug(() => $"Path: {string.Join(" -> ", path)}");

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
                bool foundLegacyEmergencyNode = false;
                bool foundLegacyUpgradeNode = false;

                ValidateThinkNode(humanlikeThinkTree.thinkRoot, ref foundWeaponNode, ref foundLegacyEmergencyNode, ref foundLegacyUpgradeNode);

                bool hasValidNodes = foundWeaponNode || (foundLegacyEmergencyNode && foundLegacyUpgradeNode);

                if (!hasValidNodes)
                {
                    if (logWarning)
                    {
                        AutoArmLogger.Warn($"Think tree validation failed - Unified: {foundWeaponNode}, Legacy Emergency: {foundLegacyEmergencyNode}, Legacy Upgrade: {foundLegacyUpgradeNode}");
                    }
                    return false;
                }

                
                    if (foundWeaponNode)
                        AutoArmLogger.Debug(() => "ThinkTree validation successful");
                    else
                        AutoArmLogger.Debug(() => "ThinkTree validation successful, using legacy separate nodes (backward compatibility)");
                

                return true;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error validating think tree", e);
                return false;
            }
        }

        internal static void ValidateThinkNode(ThinkNode node, ref bool foundWeaponNode, ref bool foundLegacyEmergencyNode, ref bool foundLegacyUpgradeNode)
        {
            if (node == null) return;

            if (node is ThinkNode_ConditionalWeaponStatus)
            {
                foundWeaponNode = true;
#pragma warning disable CS0618
                if (node is ThinkNode_ConditionalUnarmed)
                    foundLegacyEmergencyNode = true;
                else if (node is ThinkNode_ConditionalArmed)
                    foundLegacyUpgradeNode = true;
#pragma warning restore CS0618
            }

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    ValidateThinkNode(subNode, ref foundWeaponNode, ref foundLegacyEmergencyNode, ref foundLegacyUpgradeNode);
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
                WeaponScoringHelper.ClearWeaponScoreCache();

                if (isNewGame)
                {
                    WeaponCacheManager.ClearAllCaches();
                }

                PawnValidationCache.ClearCache();

                GenericCache.ClearAll();

                DroppedItemTracker.ClearAll();
                ForcedWeaponState.Clear();
                AutoEquipState.Cleanup();
                WeaponBlacklist.ClearAll();
                JobGiver_PickUpBetterWeapon.CleanupMessageCache();


                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        WeaponCacheManager.MarkCacheAsChanged(map);
                    }
                }

                AutoArmLogger.Debug(() => "Cleared temporary caches on startup/load - preserved player forced weapons");
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
                AutoArmLogger.Debug(() => "Pre-warming caches on startup...");

                WeaponScoringHelper.PreCalcWeapons();

                var activeOutfits = new HashSet<ApparelPolicy>();

                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        if (map?.mapPawns?.FreeColonists == null) continue;

                        foreach (var pawn in map.mapPawns.FreeColonists)
                        {
                            if (pawn?.outfits?.CurrentApparelPolicy != null)
                            {
                                activeOutfits.Add(pawn.outfits.CurrentApparelPolicy);
                            }
                        }
                    }
                }

                if (activeOutfits.Count == 0)
                {
                    var allOutfits = UI.PolicyDBHelper.GetAllPolicies();
                    if (allOutfits != null)
                    {
                        var firstOutfit = allOutfits.FirstOrDefault();
                        if (firstOutfit != null)
                        {
                            activeOutfits.Add(firstOutfit);
                        }
                    }
                }

                if (activeOutfits.Count > 0)
                {
                    var weaponDefs = WeaponValidation.AllWeapons;
                    int combinationsWarmed = 0;

                    foreach (var outfit in activeOutfits)
                    {
                        if (outfit?.filter == null) continue;

                        foreach (var weaponDef in weaponDefs)
                        {
                            if (weaponDef == null) continue;

                            WeaponCacheManager.PreWarmFilterCheck(weaponDef, outfit);
                            combinationsWarmed++;
                        }
                    }

                    AutoArmLogger.Debug(() => $"Pre-warmed {combinationsWarmed} outfit-weapon combinations for {activeOutfits.Count} active outfits");
                }

                if (Find.Maps != null)
                {
                    int colonistScoresWarmed = 0;

                    foreach (var map in Find.Maps)
                    {
                        if (map?.mapPawns?.FreeColonists != null)
                        {
                            foreach (var pawn in map.mapPawns.FreeColonists)
                            {
                                if (pawn == null || pawn.Dead || pawn.Downed) continue;

                                WeaponCacheManager.PreWarmColonistScore(pawn, true);
                                WeaponCacheManager.PreWarmColonistScore(pawn, false);
                                colonistScoresWarmed++;
                            }
                        }
                    }

                    if (AutoArmMod.settings?.debugLogging == true && colonistScoresWarmed > 0)
                    {
                        AutoArmLogger.Debug(() => $"Pre-warmed scores for {colonistScoresWarmed} colonists");
                    }
                }

                AutoArmLogger.Debug(() => "Cache pre-warming complete");
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorCleanup(e, "PreWarmCachesOnStartup");
            }
        }
    }

    public static class Game_LoadGame_InjectThinkTree_Patch
    {
        public static void Postfix()
        {
            AutoArmInit.retryAttempts = 0;

            AutoArm.ForcedWeaponLabelHelper.ResetFieldChecking();

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

    /// <summary>
    /// Weapon management
    /// Priority-based execution
    /// </summary>
    public class ThinkNode_ConditionalWeaponStatus : ThinkNode_Priority
    {
        private readonly WeaponStatusEvaluator evaluator = new WeaponStatusEvaluator();

        public override float GetPriority(Pawn pawn)
        {
            return evaluator.EvaluatePriority(pawn);
        }

        public static void CleanupDeadPawns()
        {
            PawnValidationCache.CleanupDeadPawns();
        }
    }


    internal class WeaponStatusEvaluator
    {
        private static readonly RaidChecker raidChecker = new RaidChecker();

        public float EvaluatePriority(Pawn pawn)
        {
            if (!BasicPawnValidation.IsValid(pawn))
                return 0f;

            if (raidChecker.ShouldSkipDuringRaid())
                return 0f;

            if (!PawnValidationCache.CanConsiderWeapons(pawn))
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


    internal class RaidChecker
    {
        private bool isLargeRaidActive = false;
        private int lastCheckTick = 0;
        private const int CheckInterval = 300;

        public bool ShouldSkipDuringRaid()
        {
            if (AutoArmMod.settings?.disableDuringRaids != true)
                return false;

            UpdateRaidStatus();
            return isLargeRaidActive;
        }

        private void UpdateRaidStatus()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - lastCheckTick > CheckInterval)
            {
                lastCheckTick = currentTick;
                isLargeRaidActive = CheckForHighDanger();

                if (AutoArmMod.settings?.debugLogging == true && isLargeRaidActive)
                {
                    AutoArmLogger.Debug(() => "High danger detected - throttling weapon checks");
                }
            }
        }

        private bool CheckForHighDanger()
        {
            if (Find.Maps == null) return false;

            foreach (var map in Find.Maps)
            {
                if (map.IsPlayerHome && map.dangerWatcher?.DangerRating == StoryDanger.High)
                    return true;
            }
            return false;
        }
    }


    [Obsolete("Use ThinkNode_ConditionalWeaponStatus instead - kept for backward compatibility")]
    public class ThinkNode_ConditionalUnarmed : ThinkNode_ConditionalWeaponStatus
    {
        public ThinkNode_ConditionalUnarmed()
        {
        }
    }

    [Obsolete("Use ThinkNode_ConditionalWeaponStatus instead - kept for backward compatibility")]
    public class ThinkNode_ConditionalArmed : ThinkNode_ConditionalWeaponStatus
    {
        public ThinkNode_ConditionalArmed()
        {
        }
    }
}

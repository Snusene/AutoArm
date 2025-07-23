using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using Verse.Steam;

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
            
            // Log version detection
            var version = VersionControl.CurrentVersionString;
            if (version.StartsWith("1.5"))
            {
                Log.Message("<color=#4287f5>[AutoArm]</color> Detected RimWorld 1.5 - Using performance-optimized timings");
            }
            else
            {
                Log.Message($"<color=#4287f5>[AutoArm]</color> Running on RimWorld {version} - Using standard timings");
            }
            
            // Initialize debug logging if enabled
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmDebugLogger.DebugLog("AutoArm mod initialized with debug logging enabled");
            }
        }

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
                Log.Message("[AutoArm] Added GameComponent for save/load support");
            }
            
            // Pre-injection validation
            if (!ValidatePreInjection())
            {
                Log.Warning("[AutoArm] Pre-injection validation failed. Disabling think tree injection to prevent crashes.");
                AutoArmMod.settings.thinkTreeInjectionFailed = true;
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
                Log.Message("[AutoArm] Running pre-injection validation...");
                
                // Test 1: Can we access weapon defs without crashing?
                var weaponDefs = WeaponThingFilterUtility.AllWeapons;
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
                        if (weaponDef.defName?.Contains("Kiiro") == true)
                        {
                            Log.Message($"[AutoArm] Found Kiiro Race weapon during validation: {weaponDef.defName}");
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
                    Log.Message("[AutoArm] Testing weapon cache build...");
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
                
                Log.Message("[AutoArm] Pre-injection validation passed.");
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
            Log.Message($"[AutoArm] Attempting think tree injection retry {AutoArmInit.retryAttempts}/{AutoArmInit.MaxRetryAttempts}...");

            try
            {
                InjectIntoThinkTree();

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
            AutoArmDebug.Log("Starting think tree injection...");

            var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (humanlikeThinkTree?.thinkRoot == null)
            {
                Log.Error("[AutoArm] Could not find Humanlike think tree!");
                AutoArmDebug.LogError("Could not find Humanlike think tree!");
                return;
            }

            ThinkNode_PrioritySorter mainSorter = null;
            FindMainPrioritySorter(humanlikeThinkTree.thinkRoot, ref mainSorter);

            if (mainSorter == null)
            {
                Log.Error("[AutoArm] Failed to find main priority sorter in think tree!");
                AutoArmDebug.LogError("Failed to find main priority sorter in think tree!");
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

            // Normal upgrades - insert at next position
            var weaponUpgradeNode = new ThinkNode_ConditionalWeaponsInOutfit();
            var upgradeJobGiver = new JobGiver_PickUpBetterWeapon();
            weaponUpgradeNode.subNodes = new List<ThinkNode> { upgradeJobGiver };
            mainSorter.subNodes.Insert(insertIndex + 1, weaponUpgradeNode);

            // Sidearm pickup node
            var sidearmConditionalNode = new ThinkNode_ConditionalShouldCheckSidearms();
            var sidearmJobGiver = new JobGiver_PickUpSidearm();
            sidearmConditionalNode.subNodes = new List<ThinkNode> { sidearmJobGiver };
            mainSorter.subNodes.Insert(insertIndex + 2, sidearmConditionalNode);

            AutoArmDebug.Log($"Injected emergency weapon check at index {insertIndex}");
            AutoArmDebug.Log($"Injected weapon upgrade check at index {insertIndex + 1}");
            AutoArmDebug.Log($"Injected sidearm check at index {insertIndex + 2}");
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
                if (sorter.subNodes.Count >= 10)
                {
                    AutoArmDebug.Log($"DEBUG: Found large PrioritySorter at depth {depth} with {sorter.subNodes.Count} nodes");
                    AutoArmDebug.Log($"DEBUG: Path: {string.Join(" -> ", path)}");

                    var nodeTypes = sorter.subNodes.Take(5).Select(n => n.GetType().Name).ToList();
                    AutoArmDebug.Log($"DEBUG: First 5 nodes: {string.Join(", ", nodeTypes)}");

                    bool hasGetFood = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetFood");
                    bool hasGetRest = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_GetRest");
                    bool hasWork = sorter.subNodes.Any(n => n.GetType().Name == "JobGiver_Work");

                    AutoArmDebug.Log($"DEBUG: Has GetFood: {hasGetFood}, GetRest: {hasGetRest}, Work: {hasWork}");

                    bool isUnderSubtree = path.Contains("ThinkNode_Subtree") || path.Contains("ThinkNode_SubtreesByTag");

                    if (hasGetFood && hasGetRest && hasWork && isUnderSubtree)
                    {
                        result = sorter;
                        AutoArmDebug.Log($"Found main colonist priority sorter at depth {depth} with {sorter.subNodes.Count} nodes");
                        AutoArmDebug.Log($"Path: {string.Join(" -> ", path)}");
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

                if (!foundEmergencyNode || !foundUpgradeNode)
                {
                    Log.Warning($"[AutoArm] Think tree validation failed - Emergency: {foundEmergencyNode}, Upgrade: {foundUpgradeNode}");
                    AutoArmDebug.LogError($"Think tree validation failed - Emergency: {foundEmergencyNode}, Upgrade: {foundUpgradeNode}");
                    return false;
                }

                AutoArmDebug.Log("Think tree validation successful");

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error validating think tree: {e.Message}");
                return false;
            }
        }

        private static void ValidateThinkNode(ThinkNode node, ref bool foundEmergencyNode, ref bool foundUpgradeNode, ref bool foundSidearmNode)
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
            Game_FinalizeInit_InjectThinkTree_Patch.Postfix();
            
            // Clean up any sidearm upgrade state that shouldn't persist across save/load
            SimpleSidearmsCompat.CleanupAfterLoad();
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
        private static DateTime lastCleanupTime = DateTime.Now;
        
        protected override bool Satisfied(Pawn pawn)
        {
            try
            {
                // Clean up old entries periodically
                if ((DateTime.Now - lastCleanupTime).TotalMinutes > 5)
                {
                    pawnEvaluationFailures.Clear();
                    lastCleanupTime = DateTime.Now;
                }
                
                // Check if this pawn has been failing repeatedly
                if (pawnEvaluationFailures.TryGetValue(pawn, out int failCount) && failCount > 10)
                {
                    // Too many failures for this pawn - skip to prevent crash loops
                    return false;
                }
                
                if (!JobGiverHelpers.SafeIsColonist(pawn) || pawn.Dead || pawn.Downed)
                    return false;

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
                    return false;

                var primary = pawn.equipment?.Primary;
                bool isUnarmed = primary == null || !primary.def.IsWeapon;
                
                return isUnarmed;
            }
            catch (Exception ex)
            {
                // Record failure for this pawn
                if (!pawnEvaluationFailures.ContainsKey(pawn))
                    pawnEvaluationFailures[pawn] = 0;
                pawnEvaluationFailures[pawn]++;
                
                // If too many failures, record a crash
                if (pawnEvaluationFailures[pawn] > 5)
                {
                    Log.Error($"[AutoArm] Multiple evaluation failures for {pawn?.Name?.ToStringShort ?? "unknown"} - possible crash loop detected");
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
                return Satisfied(pawn) ? 6.9f : 0f;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalUnarmed.GetPriority: {ex.Message}");
                return 0f;
            }
        }
    }

    public class JobGiver_PickUpBetterWeapon_Emergency : JobGiver_PickUpBetterWeapon
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            try
            {
                // Note: The base JobGiver already handles emergency vs routine checks
                // This class exists mainly for debugging and to ensure emergency jobs don't expire
                var job = base.TryGiveJob(pawn);

                if (job != null)
                {
                    job.expiryInterval = -1; // Never expire emergency weapon pickups
                    job.checkOverrideOnExpire = false;
                }
                else if (pawn.equipment?.Primary == null)
                {
                    // Only log emergency failures once per 10 seconds per pawn
                    TimingHelper.LogWithCooldown(pawn, "Emergency: is unarmed but found no weapon!", 
                        TimingHelper.CooldownType.ForcedWeaponLog);
                }

                return job;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in JobGiver_PickUpBetterWeapon_Emergency: {ex.Message}");
                if (ex.InnerException != null)
                    Log.Error($"[AutoArm] Inner: {ex.InnerException.Message}");
                return null;
            }
        }
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
            if (!SimpleSidearmsCompat.IsLoaded() ||
                AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);


            return job;
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
                if (!SimpleSidearmsCompat.IsLoaded() ||
                    AutoArmMod.settings?.autoEquipSidearms != true ||
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
                // Same priority as weapon upgrades - check before starting work
                return Satisfied(pawn) ? 5.0f : 0f;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalShouldCheckSidearms.GetPriority: {ex.Message}");
                return 0f;
            }
        }
    }
}

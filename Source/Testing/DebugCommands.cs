using LudeonTK;
using RimWorld;
using System.Collections;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using System;
using System.IO;

namespace AutoArm
{
    public static class AutoArmDebugCommands
    {
        private static bool ShouldShowDebugActions => AutoArmMod.settings?.debugLogging ?? false;

        [DebugAction("AutoArm", "Test Think Tree",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestThinkTree(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Think Tree for {pawn.Name} ===");

            var weaponsInOutfitNode = new ThinkNode_ConditionalWeaponsInOutfit();

            bool weaponsAllowed = weaponsInOutfitNode.TestSatisfied(pawn);

            Log.Message($"[AutoArm] Weapons in outfit allowed: {weaponsAllowed}");

            var currentWeapon = pawn.equipment?.Primary;
            if (currentWeapon != null)
            {
                Log.Message($"[AutoArm] Current weapon: {currentWeapon.Label} (Score: {new JobGiver_PickUpBetterWeapon().GetWeaponScore(pawn, currentWeapon)})");
            }
            else
            {
                Log.Message($"[AutoArm] Currently UNARMED");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(pawn);

            if (job != null)
            {
                var targetWeapon = job.targetA.Thing;
                Log.Message($"[AutoArm] Found better weapon: {targetWeapon.Label} at {targetWeapon.Position}");
                Log.Message($"[AutoArm] Distance: {targetWeapon.Position.DistanceTo(pawn.Position):F1} cells");
            }
            else
            {
                Log.Message($"[AutoArm] No better weapon found");
            }

            if (pawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;
                int allowedRanged = WeaponThingFilterUtility.RangedWeapons.Count(td => filter.Allows(td));
                int allowedMelee = WeaponThingFilterUtility.MeleeWeapons.Count(td => filter.Allows(td));
                Log.Message($"[AutoArm] Outfit '{pawn.outfits.CurrentApparelPolicy.label}' allows {allowedRanged} ranged and {allowedMelee} melee weapons");
            }
            else
            {
                Log.Message($"[AutoArm] No outfit policy set");
            }

            Log.Message($"[AutoArm] === End Test ===\n");
        }

        [DebugAction("AutoArm", "Test Force Weapon Check Now",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceWeaponCheck(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(pawn);

            if (job != null)
            {
                AutoEquipTracker.MarkAutoEquip(job, pawn);

                pawn.jobs.StartJob(job, JobCondition.InterruptForced);

                var targetWeapon = job.targetA.Thing;
                Log.Message($"[AutoArm] Forced {pawn.Name} to pick up {targetWeapon.Label}");
            }
            else
            {
                Log.Message($"[AutoArm] {pawn.Name} - No weapon to pick up");
            }
        }

        [DebugAction("AutoArm", "Test Weapon Drop Check",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestWeaponDropCheck(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Weapon Drop for {pawn.Name} ===");

            var currentWeapon = pawn.equipment?.Primary;
            if (currentWeapon == null)
            {
                Log.Message($"[AutoArm] No weapon equipped");
                return;
            }

            Log.Message($"[AutoArm] Current weapon: {currentWeapon.Label}");

            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter == null)
            {
                Log.Message($"[AutoArm] No outfit policy set");
                return;
            }

            Log.Message($"[AutoArm] Outfit policy: {pawn.outfits.CurrentApparelPolicy.label}");

            bool allowed = filter.Allows(currentWeapon.def);
            Log.Message($"[AutoArm] Weapon allowed by outfit: {allowed}");

            if (!allowed && !pawn.Drafted)
            {
                Log.Message($"[AutoArm] Would drop weapon (not allowed and not drafted)");
            }
            else if (!allowed && pawn.Drafted)
            {
                Log.Message($"[AutoArm] Would NOT drop weapon (drafted)");
            }
            else
            {
                Log.Message($"[AutoArm] Would NOT drop weapon (allowed by outfit)");
            }

            Log.Message($"[AutoArm] === End Test ===\n");
        }

        [DebugAction("AutoArm", "Test Simple Sidearms",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestSimpleSidearms(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Simple Sidearms for {pawn.Name} ===");
            Log.Message($"[AutoArm] Simple Sidearms loaded: {SimpleSidearmsCompat.IsLoaded()}");
            Log.Message($"[AutoArm] Auto-equip sidearms enabled: {AutoArmMod.settings?.autoEquipSidearms}");

            if (SimpleSidearmsCompat.IsLoaded())
            {
                var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);
                if (job != null)
                {
                    Log.Message($"[AutoArm] Found sidearm job: {job.def.defName} targeting {job.targetA.Thing?.Label}");

                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    Log.Message($"[AutoArm] Started sidearm job");
                }
                else
                {
                    Log.Message($"[AutoArm] No sidearm job found");
                }
            }

            Log.Message($"[AutoArm] === End Test ===\n");
        }

        [DebugAction("AutoArm", "Test Infusion Compatibility",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestInfusionCompat()
        {
            Log.Message("\n[AutoArm] === Testing Infusion 2 Compatibility ===");
            Log.Message($"[AutoArm] Infusion 2 loaded: {InfusionCompat.IsLoaded()}");

            if (!InfusionCompat.IsLoaded())
            {
                Log.Message("[AutoArm] Infusion 2 is not loaded");
                return;
            }

            InfusionCompat.DebugListInfusionTypes();

            var map = Find.CurrentMap;
            if (map == null) return;

            var infusedWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => InfusionCompat.HasInfusions(w))
                .ToList();

            Log.Message($"[AutoArm] Found {infusedWeapons.Count} infused weapons on map:");

            foreach (var weapon in infusedWeapons.Take(10))
            {
                float bonus = InfusionCompat.GetInfusionScoreBonus(weapon);
                Log.Message($"  - {weapon.Label}: +{bonus} score from infusions");
            }

            Log.Message("[AutoArm] === End Test ===\n");
        }

        [DebugAction("AutoArm", "Test List Simple Sidearms Types",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ListSimpleSidearmsTypes()
        {
            Log.Message("\n[AutoArm] === Simple Sidearms Types ===");

            var sidearmTypes = GenTypes.AllTypes
                .Where(t => t.Namespace?.Contains("SimpleSidearms") == true ||
                           t.FullName?.Contains("SimpleSidearms") == true ||
                           t.Name.Contains("Sidearm"))
                .OrderBy(t => t.FullName)
                .ToList();

            Log.Message($"[AutoArm] Found {sidearmTypes.Count} Simple Sidearms-related types:");

            foreach (var type in sidearmTypes)
            {
                Log.Message($"  - {type.FullName}");

                if (type.Name == "CompSidearmMemory")
                {
                    Log.Message($"    Found CompSidearmMemory! Methods:");
                    foreach (var method in type.GetMethods())
                    {
                        Log.Message($"      - {method.Name}");
                    }
                }
            }

            Log.Message($"[AutoArm] === End Types ===\n");
        }

        [DebugAction("AutoArm", "Test Force Sidearm Upgrade Check",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceSidearmUpgradeCheck(Pawn pawn)
        {
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);
            if (job != null)
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                Log.Message($"[AutoArm] Started sidearm upgrade job for {pawn.Name}");
            }
            else
            {
                Log.Message($"[AutoArm] No sidearm upgrade found for {pawn.Name}");
            }
        }

        [DebugAction("AutoArm", "Test Check Think Tree Status",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckThinkTreeStatus()
        {
            Log.Message($"[AutoArm] Think tree injection failed: {AutoArmMod.settings?.thinkTreeInjectionFailed}");

            var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (humanlikeThinkTree?.thinkRoot != null)
            {
                bool foundWeaponNode = false;
                CheckForWeaponNode(humanlikeThinkTree.thinkRoot, ref foundWeaponNode);
                Log.Message($"[AutoArm] Weapon nodes found in think tree: {foundWeaponNode}");
            }
        }

        private static void CheckForWeaponNode(ThinkNode node, ref bool found)
        {
            if (node is ThinkNode_ConditionalWeaponsInOutfit || node is JobGiver_PickUpBetterWeapon)
                found = true;

            if (node.subNodes != null)
                foreach (var sub in node.subNodes)
                    CheckForWeaponNode(sub, ref found);
        }

        [DebugAction("AutoArm", "Show Think Tree Structure",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ShowThinkTreeStructure()
        {
            var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (humanlikeThinkTree?.thinkRoot == null)
            {
                Log.Message("[AutoArm] No humanlike think tree found");
                return;
            }

            Log.Message("[AutoArm] === Think Tree Structure ===");
            PrintThinkNode(humanlikeThinkTree.thinkRoot, 0);
        }

        private static void PrintThinkNode(ThinkNode node, int depth)
        {
            string indent = new string(' ', depth * 2);
            string nodeInfo = $"{indent}{node.GetType().Name}";

            if (node is JobGiver_Work)
                nodeInfo += " <-- WORK IS HERE";
            if (node is ThinkNode_ConditionalWeaponsInOutfit || node is JobGiver_PickUpBetterWeapon)
                nodeInfo += " <-- OUR WEAPON NODE";

            Log.Message(nodeInfo);

            if (node.subNodes != null)
            {
                for (int i = 0; i < node.subNodes.Count; i++)
                {
                    Log.Message($"{indent}[{i}]:");
                    PrintThinkNode(node.subNodes[i], depth + 1);
                }
            }
        }

        [DebugAction("AutoArm", "Test Debug Simple Sidearms Data",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugSimpleSidearmsData(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Debugging Simple Sidearms Data for {pawn.Name} ===");
            Log.Message($"[AutoArm] Simple Sidearms loaded: {SimpleSidearmsCompat.IsLoaded()}");

            if (!SimpleSidearmsCompat.IsLoaded())
                return;

            var initMethod = typeof(SimpleSidearmsCompat).GetMethod("EnsureInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            initMethod?.Invoke(null, null);

            var compType = GenTypes.AllTypes.FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");
            if (compType == null)
            {
                Log.Message("[AutoArm] Could not find CompSidearmMemory type");
                return;
            }

            var comp = pawn.AllComps?.FirstOrDefault(c => c.GetType() == compType);
            if (comp == null)
            {
                Log.Message($"[AutoArm] {pawn.Name} has no sidearm memory component");
                return;
            }

            var rememberedWeaponsProperty = compType.GetProperty("RememberedWeapons", BindingFlags.Public | BindingFlags.Instance);
            if (rememberedWeaponsProperty != null)
            {
                var value = rememberedWeaponsProperty.GetValue(comp);
                Log.Message($"[AutoArm] RememberedWeapons type: {value?.GetType()?.FullName ?? "null"}");

                if (value is IEnumerable list)
                {
                    int count = 0;
                    var thingDefStuffDefPairType = GenTypes.AllTypes.FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.ThingDefStuffDefPair");

                    if (thingDefStuffDefPairType != null)
                    {
                        var thingField = thingDefStuffDefPairType.GetField("thing");
                        var stuffField = thingDefStuffDefPairType.GetField("stuff");

                        foreach (var item in list)
                        {
                            count++;
                            if (item != null && thingField != null)
                            {
                                var weaponDef = thingField.GetValue(item) as ThingDef;
                                var stuffDef = stuffField?.GetValue(item) as ThingDef;

                                Log.Message($"[AutoArm]   Sidearm {count}: {weaponDef?.label ?? "null"} (stuff: {stuffDef?.label ?? "none"})");
                            }
                        }
                    }

                    Log.Message($"[AutoArm] Total sidearms: {count}");
                }
            }

            if (pawn.equipment?.Primary != null)
            {
                Log.Message($"[AutoArm] Current primary weapon: {pawn.equipment.Primary.Label}");
            }
            else
            {
                Log.Message($"[AutoArm] No primary weapon equipped");
            }

            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);
            if (job != null)
            {
                Log.Message($"[AutoArm] Found sidearm job: {job.def.defName} targeting {job.targetA.Thing?.Label}");
            }
            else
            {
                Log.Message($"[AutoArm] No sidearm job found");
            }

            Log.Message($"[AutoArm] === End Debug ===\n");
        }
        
        [DebugAction("AutoArm", "Run Performance Test",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RunPerformanceTest()
        {
            Testing.PerformanceTestRunner.RunPerformanceTest();
        }
        
        [DebugAction("AutoArm", "Run All Tests",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RunAllTests()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[AutoArm] No map available for testing");
                return;
            }
            
            var results = Testing.TestRunner.RunAllTests(map);
            Testing.TestRunner.LogTestResults(results);
        }
        
        [DebugAction("AutoArm", "Diagnose Weapon Detection",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiagnoseWeaponDetection()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            
            Log.Message("\n[AutoArm] === WEAPON DETECTION DIAGNOSIS ===");
            
            // Check ThingRequestGroup.Weapon
            var weaponGroupItems = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToList();
            Log.Message($"[AutoArm] ThingRequestGroup.Weapon count: {weaponGroupItems.Count}");
            foreach (var item in weaponGroupItems.Take(5))
            {
                Log.Message($"  - {item.Label} ({item.def.defName})");
            }
            
            // Check all IsWeapon items
            var allWeaponItems = map.listerThings.AllThings.Where(t => t.def.IsWeapon).ToList();
            Log.Message($"\n[AutoArm] All things with IsWeapon=true: {allWeaponItems.Count}");
            
            // Group by def to see what types
            var weaponTypes = allWeaponItems.GroupBy(w => w.def).OrderByDescending(g => g.Count()).ToList();
            Log.Message($"[AutoArm] Weapon types found:");
            foreach (var group in weaponTypes.Take(10))
            {
                Log.Message($"  - {group.Key.label} ({group.Key.defName}): {group.Count()} items");
            }
            
            // Check ground weapons
            var groundWeapons = allWeaponItems.Where(w => w.Spawned && (w.ParentHolder == null || w.ParentHolder == map)).ToList();
            Log.Message($"\n[AutoArm] Ground weapons (not equipped/inventory): {groundWeapons.Count}");
            
            // Check equipped weapons
            var equippedWeapons = map.mapPawns.AllPawns.Where(p => p.equipment?.Primary != null).Select(p => p.equipment.Primary).ToList();
            Log.Message($"[AutoArm] Equipped weapons: {equippedWeapons.Count}");
            
            // Check inventory weapons
            var inventoryWeapons = map.mapPawns.AllPawns
                .Where(p => p.inventory?.innerContainer != null)
                .SelectMany(p => p.inventory.innerContainer.Where(t => t.def.IsWeapon))
                .ToList();
            Log.Message($"[AutoArm] Weapons in inventories: {inventoryWeapons.Count}");
            
            // Check weapon validation
            Log.Message($"\n[AutoArm] Validation check on first 5 ground weapons:");
            foreach (var weapon in groundWeapons.Take(5))
            {
                var thingWithComps = weapon as ThingWithComps;
                if (thingWithComps != null)
                {
                    var validationInfo = WeaponValidation.GetWeaponValidationInfo(weapon);
                    Log.Message($"  - {weapon.Label}: {validationInfo}");
                }
            }
            
            // Check cache status
            Log.Message($"\n[AutoArm] Current cache status:");
            var cacheField = typeof(ImprovedWeaponCacheManager).GetField("weaponCache", BindingFlags.NonPublic | BindingFlags.Static);
            if (cacheField != null)
            {
                var weaponCacheDict = cacheField.GetValue(null) as System.Collections.IDictionary;
                if (weaponCacheDict != null && weaponCacheDict.Contains(map))
                {
                    dynamic cache = weaponCacheDict[map];
                    Log.Message($"  - Cached weapons: {cache.Weapons.Count}");
                    Log.Message($"  - Spatial index cells: {cache.SpatialIndex.Count}");
                }
                else
                {
                    Log.Message($"  - No cache exists for this map");
                }
            }
            
            Log.Message("[AutoArm] === END DIAGNOSIS ===\n");
        }
        
        [DebugAction("AutoArm", "Run Weapon Categorization Diagnostic",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RunWeaponCategorizationDiagnostic()
        {
            var map = Find.CurrentMap;
            WeaponCategorizationDiagnostic.RunDiagnostic(map);
        }
        
        [DebugAction("AutoArm", "Check Forced Weapon Status",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckForcedWeaponStatus(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }
            
            Log.Message($"\n[AutoArm] === Forced Weapon Status for {pawn.Name} ===");
            
            // Log all forced weapon info
            ForcedWeaponTracker.DebugLogForcedWeapons(pawn);
            
            // Check current equipment
            if (pawn.equipment?.Primary != null)
            {
                var weapon = pawn.equipment.Primary;
                bool isForced = ForcedWeaponTracker.IsForced(pawn, weapon);
                bool defIsForced = ForcedWeaponTracker.IsAnyWeaponDefForced(pawn, weapon.def);
                
                Log.Message($"\n[AutoArm] Current primary: {weapon.Label} ({weapon.def.defName})");
                Log.Message($"  - IsForced: {isForced}");
                Log.Message($"  - Def is forced: {defIsForced}");
            }
            else
            {
                Log.Message($"\n[AutoArm] No primary weapon equipped");
            }
            
            // Check inventory
            if (pawn.inventory?.innerContainer != null)
            {
                var weapons = pawn.inventory.innerContainer.OfType<ThingWithComps>().Where(t => t.def.IsWeapon).ToList();
                if (weapons.Any())
                {
                    Log.Message($"\n[AutoArm] Inventory weapons:");
                    foreach (var weapon in weapons)
                    {
                        bool isForced = ForcedWeaponTracker.IsForced(pawn, weapon);
                        bool defIsForced = ForcedWeaponTracker.IsAnyWeaponDefForced(pawn, weapon.def);
                        bool isForcedSidearm = ForcedWeaponTracker.IsForcedSidearm(pawn, weapon.def);
                        
                        Log.Message($"  - {weapon.Label} ({weapon.def.defName})");
                        Log.Message($"    IsForced: {isForced}, Def forced: {defIsForced}, Forced sidearm: {isForcedSidearm}");
                    }
                }
                else
                {
                    Log.Message($"\n[AutoArm] No weapons in inventory");
                }
            }
            
            // Check SimpleSidearms status
            if (SimpleSidearmsCompat.IsLoaded())
            {
                bool isPrimarySidearm = SimpleSidearmsCompat.PrimaryIsRememberedSidearm(pawn);
                Log.Message($"\n[AutoArm] SimpleSidearms: Primary is remembered sidearm = {isPrimarySidearm}");
            }
            
            Log.Message($"[AutoArm] === End Status ===\n");
        }
        
        [DebugAction("AutoArm", "Force Primary Weapon",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForcePrimaryWeapon(Pawn pawn)
        {
            if (!pawn.IsColonist || pawn.equipment?.Primary == null)
            {
                Log.Message($"[AutoArm] {pawn.Name} has no primary weapon to force");
                return;
            }
            
            var weapon = pawn.equipment.Primary;
            ForcedWeaponTracker.SetForced(pawn, weapon);
            Log.Message($"[AutoArm] Forced {pawn.Name}'s primary weapon: {weapon.Label}");
        }
        
        [DebugAction("AutoArm", "Clear Forced Weapons",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearForcedWeapons(Pawn pawn)
        {
            ForcedWeaponTracker.ClearForced(pawn);
            
            // Clear all forced sidearms too
            var forcedSidearms = ForcedWeaponTracker.GetForcedSidearms(pawn).ToList();
            foreach (var weaponDef in forcedSidearms)
            {
                ForcedWeaponTracker.ClearForcedSidearm(pawn, weaponDef);
            }
            
            Log.Message($"[AutoArm] Cleared all forced weapons for {pawn.Name}");
        }
        
        [DebugAction("AutoArm", "Test Debug Logging",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestDebugLogging()
        {
            Log.Message($"[AutoArm] Current debug logging setting: {AutoArmMod.settings?.debugLogging}");
            
            // Force initialize the logger
            var initMethod = typeof(AutoArmDebugLogger).GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Static);
            initMethod?.Invoke(null, null);
            
            // Test logging
            AutoArmDebug.Log("TEST: This is a test debug log message");
            AutoArmDebug.LogFormat("TEST: Formatted message with number {0} and string {1}", 42, "test");
            AutoArmDebug.LogError("TEST: This is a test error message");
            
            // Force flush
            AutoArmDebugLogger.EnsureFlush();
            
            // Get log file path
            var pathField = typeof(AutoArmDebugLogger).GetField("logFilePath", BindingFlags.NonPublic | BindingFlags.Static);
            string logPath = pathField?.GetValue(null) as string;
            
            Log.Message($"[AutoArm] Debug log file should be at: {logPath}");
            
            if (File.Exists(logPath))
            {
                Log.Message($"[AutoArm] File exists! Size: {new FileInfo(logPath).Length} bytes");
            }
            else
            {
                Log.Error($"[AutoArm] File does not exist at expected location!");
            }
        }
    }
}

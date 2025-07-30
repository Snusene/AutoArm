using LudeonTK;
using RimWorld;
using System.Collections;
using System.Collections.Generic;
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
        
        private static void LogForcedWeaponsDebug(Pawn pawn)
        {
            if (pawn == null) return;
            
            Log.Message($"[AutoArm] Forced weapon info for {pawn.Name?.ToStringShort ?? "unknown"}:");
            
            // Log forced primary weapon
            var forcedPrimary = ForcedWeaponHelper.GetForcedPrimary(pawn);
            if (forcedPrimary != null)
            {
                Log.Message($"  - Forced primary weapon: {forcedPrimary.Label} ({forcedPrimary.ThingID})");
            }
            else
            {
                Log.Message($"  - No forced primary weapon");
            }
            
            // Log forced weapon defs
            var forcedDefs = ForcedWeaponHelper.GetForcedWeaponDefs(pawn);
            if (forcedDefs.Count > 0)
            {
                Log.Message($"  - Forced weapon types ({forcedDefs.Count}):");
                foreach (var def in forcedDefs)
                {
                    Log.Message($"    * {def.label} ({def.defName})");
                }
            }
            else
            {
                Log.Message($"  - No forced weapon types");
            }
            
            // Check if pawn has forced weapon
            bool hasForced = ForcedWeaponHelper.HasForcedWeapon(pawn);
            Log.Message($"  - Has any forced weapon: {hasForced}");
        }

        [DebugAction("AutoArm", "Test Think Tree",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestThinkTree(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Think Tree for {pawn.Name?.ToStringShort ?? "unknown"} ===");

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
                Log.Message($"[AutoArm] Forced {pawn.Name?.ToStringShort ?? "unknown"} to pick up {targetWeapon.Label}");
            }
            else
            {
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} - No weapon to pick up");
            }
        }

        [DebugAction("AutoArm", "Test Weapon Drop Check",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestWeaponDropCheck(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Weapon Drop for {pawn.Name?.ToStringShort ?? "unknown"} ===");

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
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Simple Sidearms for {pawn.Name?.ToStringShort ?? "unknown"} ===");
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
                Log.Message($"[AutoArm] Started sidearm upgrade job for {pawn.Name?.ToStringShort ?? "unknown"}");
            }
            else
            {
                Log.Message($"[AutoArm] No sidearm upgrade found for {pawn.Name?.ToStringShort ?? "unknown"}");
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
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Debugging Simple Sidearms Data for {pawn.Name?.ToStringShort ?? "unknown"} ===");
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
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} has no sidearm memory component");
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
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} is not a colonist");
                return;
            }
            
            Log.Message($"\n[AutoArm] === Forced Weapon Status for {pawn.Name?.ToStringShort ?? "unknown"} ===");
            
            // Log all forced weapon info
            LogForcedWeaponsDebug(pawn);
            
            // Check current equipment
            if (pawn.equipment?.Primary != null)
            {
                var weapon = pawn.equipment.Primary;
                bool isForced = ForcedWeaponHelper.IsForced(pawn, weapon);
                bool defIsForced = ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def);
                
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
                        bool isForced = ForcedWeaponHelper.IsForced(pawn, weapon);
                        bool defIsForced = ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def);
                        bool isForcedSidearm = ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def);
                        
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
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} has no primary weapon to force");
                return;
            }
            
            var weapon = pawn.equipment.Primary;
            ForcedWeaponHelper.SetForced(pawn, weapon);
            Log.Message($"[AutoArm] Forced {pawn.Name?.ToStringShort ?? "unknown"}'s primary weapon: {weapon.Label}");
        }
        
        [DebugAction("AutoArm", "Clear Forced Weapons",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearForcedWeapons(Pawn pawn)
        {
            ForcedWeaponHelper.ClearForced(pawn);
            
            // Clear all forced sidearms too
            var forcedSidearms = ForcedWeaponHelper.GetForcedWeaponDefs(pawn).ToList();
            foreach (var weaponDef in forcedSidearms)
            {
                ForcedWeaponHelper.RemoveForcedDef(pawn, weaponDef);
            }
            
            Log.Message($"[AutoArm] Cleared all forced weapons for {pawn.Name?.ToStringShort ?? "unknown"}");
        }
        
        [DebugAction("AutoArm", "Test SimpleSidearms Weight Limits",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestSimpleSidearmsWeightLimits()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
            {
                Log.Message("[AutoArm] SimpleSidearms not loaded");
                return;
            }
            
            Log.Message("\n[AutoArm] === SimpleSidearms Weight Limit Test ===");
            
            // Log current SimpleSidearms settings
            SimpleSidearmsCompat.LogSimpleSidearmsSettings();
            
            // Test specific weight and slot limit retrieval
            Log.Message("\n[AutoArm] Testing limit retrieval:");
            
            // Test weight limits
            var getWeightLimitMethod = typeof(SimpleSidearmsCompat).GetMethod("GetSimpleSidearmsWeightLimit", 
                BindingFlags.NonPublic | BindingFlags.Static);
            
            if (getWeightLimitMethod != null)
            {
                Log.Message("  Weight limits:");
                float generalLimit = (float)getWeightLimitMethod.Invoke(null, new object[] { false, false });
                Log.Message($"    General: {generalLimit}kg");
                
                float rangedLimit = (float)getWeightLimitMethod.Invoke(null, new object[] { true, false });
                Log.Message($"    Ranged: {rangedLimit}kg");
                
                float meleeLimit = (float)getWeightLimitMethod.Invoke(null, new object[] { false, true });
                Log.Message($"    Melee: {meleeLimit}kg");
            }
            
            // Test slot limits
            var getSlotLimitMethod = typeof(SimpleSidearmsCompat).GetMethod("GetSimpleSidearmsSlotLimit", 
                BindingFlags.NonPublic | BindingFlags.Static);
            
            if (getSlotLimitMethod != null)
            {
                Log.Message("  Slot limits:");
                int totalSlots = (int)getSlotLimitMethod.Invoke(null, new object[] { false, false, true });
                Log.Message($"    Total: {totalSlots} slots");
                
                int rangedSlots = (int)getSlotLimitMethod.Invoke(null, new object[] { true, false, false });
                Log.Message($"    Ranged: {rangedSlots} slots");
                
                int meleeSlots = (int)getSlotLimitMethod.Invoke(null, new object[] { false, true, false });
                Log.Message($"    Melee: {meleeSlots} slots");
            }
            
            // Find colonists with heavy weapons
            Log.Message("\n[AutoArm] Colonists with heavy weapons:");
            var colonists = Find.CurrentMap?.mapPawns?.FreeColonists;
            if (colonists != null)
            {
                foreach (var pawn in colonists)
                {
                    var weapons = new List<(ThingWithComps weapon, string location, float weight)>();
                    
                    // Check primary
                    if (pawn.equipment?.Primary != null)
                    {
                        var w = pawn.equipment.Primary;
                        weapons.Add((w, "Primary", w.GetStatValue(StatDefOf.Mass)));
                    }
                    
                    // Check inventory
                    if (pawn.inventory?.innerContainer != null)
                    {
                        foreach (var item in pawn.inventory.innerContainer)
                        {
                            if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                            {
                                weapons.Add((weapon, "Sidearm", weapon.GetStatValue(StatDefOf.Mass)));
                            }
                        }
                    }
                    
                    // Report heavy weapons and slot usage
                    var heavyWeapons = weapons.Where(w => w.weight > 2.7f).ToList();
                    if (heavyWeapons.Any() || weapons.Count > 2)
                    {
                        Log.Message($"\n  {pawn.Name?.ToStringShort ?? "unknown"}:");
                        
                        // Report weapon counts
                        int rangedCount = weapons.Count(w => w.weapon.def.IsRangedWeapon);
                        int meleeCount = weapons.Count(w => w.weapon.def.IsMeleeWeapon);
                        Log.Message($"    Total weapons: {weapons.Count} ({rangedCount} ranged, {meleeCount} melee)");
                        
                        // Report heavy weapons
                        if (heavyWeapons.Any())
                        {
                            Log.Message($"    Heavy weapons (>2.7kg):");
                            foreach (var (weapon, location, weight) in heavyWeapons)
                            {
                                Log.Message($"      - {location}: {weapon.Label} ({weight:F1}kg)");
                                
                                // Test if SimpleSidearms would allow this
                                string reason;
                                bool allowed = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pawn, out reason);
                                Log.Message($"        SimpleSidearms allows: {allowed} - {reason}");
                            }
                        }
                    }
                }
            }
            
            Log.Message("[AutoArm] === End Test ===");
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
        
        [DebugAction("AutoArm", "Check for Duplicate Weapons",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckForDuplicateWeapons()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            
            Log.Message("\n[AutoArm] === Checking for Duplicate Weapons ===");
            
            // Get all weapons on the map
            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .ToList();
                
            // Group by label and quality to find potential duplicates
            var weaponGroups = allWeapons
                .GroupBy(w => {
                    QualityCategory quality;
                    w.TryGetQuality(out quality);
                    return $"{w.def.defName}_{quality}";
                })
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .ToList();
                
            if (weaponGroups.Any())
            {
                Log.Message($"[AutoArm] Found {weaponGroups.Count} weapon types with multiple instances:");
                
                foreach (var group in weaponGroups)
                {
                    var weapons = group.ToList();
                    var first = weapons.First();
                    QualityCategory quality;
                    first.TryGetQuality(out quality);
                    
                    Log.Message($"\n  {first.Label} ({first.def.defName}, {quality}) - {weapons.Count} instances:");
                    
                    foreach (var weapon in weapons)
                    {
                        string location = "Unknown";
                        Pawn holder = null;
                        
                        if (weapon.ParentHolder is Pawn_EquipmentTracker equipment)
                        {
                            holder = equipment.pawn;
                            location = $"Equipped by {holder.Name?.ToStringShort ?? "unknown"}";
                        }
                        else if (weapon.ParentHolder is Pawn_InventoryTracker inventory)
                        {
                            holder = inventory.pawn;
                            location = $"In inventory of {holder.Name?.ToStringShort ?? "unknown"}";
                        }
                        else if (weapon.Map != null)
                        {
                            location = $"On ground at {weapon.Position}";
                        }
                        
                        bool isRecentlyDropped = DroppedItemTracker.IsRecentlyDropped(weapon);
                        bool isReserved = map.reservationManager.IsReservedByAnyoneOf(weapon, null);
                        
                        Log.Message($"    - {weapon.ThingID}: {location}");
                        if (isRecentlyDropped)
                            Log.Message($"      [Recently dropped]");
                        if (isReserved)
                        {
                            var reserver = map.reservationManager.FirstRespectedReserver(weapon, null);
                            Log.Message($"      [Reserved by {(reserver != null ? reserver.Name.ToString() : "unknown")}]");
                        }
                    }
                }
            }
            else
            {
                Log.Message("[AutoArm] No duplicate weapons found");
            }
            
            // Check recently dropped tracker
            var trackedCount = DroppedItemTracker.TrackedItemCount;
            Log.Message($"\n[AutoArm] Currently tracking {trackedCount} recently dropped items");
            
            Log.Message("[AutoArm] === End Check ===");
        }
        
        [DebugAction("AutoArm", "Clear Recently Dropped Tracker",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearRecentlyDroppedTracker()
        {
            int count = DroppedItemTracker.TrackedItemCount;
            DroppedItemTracker.ClearAll();
            Log.Message($"[AutoArm] Cleared {count} items from recently dropped tracker");
        }
        
        [DebugAction("AutoArm", "Show Weapon Blacklist Status",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ShowWeaponBlacklistStatus()
        {
            Log.Message(WeaponBlacklist.GetDebugInfo());
        }
        
        [DebugAction("AutoArm", "Clear Weapon Blacklist",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearWeaponBlacklist(Pawn pawn)
        {
            if (pawn == null || !pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn?.Name?.ToStringShort ?? "null"} is not a colonist");
                return;
            }
            
            WeaponBlacklist.ClearBlacklist(pawn);
            Log.Message($"[AutoArm] Cleared weapon blacklist for {pawn.Name?.ToStringShort ?? "unknown"}");
        }
        
        [DebugAction("AutoArm", "Check Pawns with Duplicate Weapons",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckPawnsWithDuplicateWeapons()
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            
            Log.Message("\n[AutoArm] === Checking for Pawns with Duplicate Weapons ===");
            
            int pawnsWithDuplicates = 0;
            
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                if (pawn == null || pawn.inventory?.innerContainer == null)
                    continue;
                    
                // Count weapons by type
                var weaponCounts = new Dictionary<ThingDef, int>();
                
                // Count primary
                if (pawn.equipment?.Primary != null)
                {
                    weaponCounts[pawn.equipment.Primary.def] = 1;
                }
                
                // Count inventory
                foreach (var item in pawn.inventory.innerContainer)
                {
                    if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                    {
                        if (weaponCounts.ContainsKey(weapon.def))
                            weaponCounts[weapon.def]++;
                        else
                            weaponCounts[weapon.def] = 1;
                    }
                }
                
                // Check for duplicates
                var duplicates = weaponCounts.Where(kvp => kvp.Value > 1).ToList();
                if (duplicates.Any())
                {
                    pawnsWithDuplicates++;
                    Log.Message($"\n{pawn.Name?.ToStringShort ?? "unknown"} has duplicate weapons:");
                    foreach (var dup in duplicates)
                    {
                        Log.Message($"  - {dup.Key.label}: {dup.Value} copies");
                        
                        // List each instance
                        if (pawn.equipment?.Primary?.def == dup.Key)
                        {
                            QualityCategory quality;
                            pawn.equipment.Primary.TryGetQuality(out quality);
                            Log.Message($"    * Equipped: {pawn.equipment.Primary.Label} ({quality})");
                        }
                        
                        foreach (var item in pawn.inventory.innerContainer)
                        {
                            if (item is ThingWithComps w && w.def == dup.Key)
                            {
                                QualityCategory quality;
                                w.TryGetQuality(out quality);
                                Log.Message($"    * Inventory: {w.Label} ({quality})");
                            }
                        }
                    }
                }
            }
            
            if (pawnsWithDuplicates == 0)
            {
                Log.Message("[AutoArm] No pawns with duplicate weapons found");
            }
            else
            {
                Log.Message($"\n[AutoArm] Total pawns with duplicates: {pawnsWithDuplicates}");
            }
            
            Log.Message("[AutoArm] === End Check ===");
        }
        
        [DebugAction("AutoArm", "Diagnose Pawn Weapon Issues",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiagnosePawnWeaponIssues(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} is not a colonist");
                return;
            }
            
            Log.Message($"\n[AutoArm] === Diagnosing Weapon Issues for {pawn.Name?.ToStringShort ?? "unknown"} ===");
            
            // Check pawn validity
            string pawnReason;
            bool pawnValid = ValidationHelper.IsValidPawn(pawn, out pawnReason);
            Log.Message($"[AutoArm] Pawn valid: {pawnValid} - {pawnReason}");
            
            // Check body size
            Log.Message($"[AutoArm] Pawn body size: {pawn.BodySize}");
            
            // Check current equipment
            if (pawn.equipment?.Primary != null)
            {
                Log.Message($"[AutoArm] Current weapon: {pawn.equipment.Primary.Label}");
            }
            else
            {
                Log.Message($"[AutoArm] Currently UNARMED");
            }
            
            // Find nearby weapons and check each one
            var nearbyWeapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => w.Position.DistanceTo(pawn.Position) <= 20f)
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .Take(10)
                .ToList();
                
            Log.Message($"\n[AutoArm] Found {nearbyWeapons.Count} weapons within 20 cells:");
            
            foreach (var weapon in nearbyWeapons)
            {
                string reason;
                bool canUse = ValidationHelper.CanPawnUseWeapon(pawn, weapon, out reason);
                
                Log.Message($"\n  {weapon.Label} at {weapon.Position}:");
                Log.Message($"    - Distance: {weapon.Position.DistanceTo(pawn.Position):F1}");
                Log.Message($"    - Can use: {canUse}");
                if (!canUse)
                {
                    Log.Message($"    - Reason: {reason}");
                }
                
                // Check specific things
                Log.Message($"    - Forbidden: {weapon.IsForbidden(pawn)}");
                Log.Message($"    - Can reach: {pawn.CanReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly)}");
                Log.Message($"    - Can reserve: {pawn.CanReserve(weapon)}");
                
                // Check outfit filter
                var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null)
                {
                    Log.Message($"    - Allowed by outfit: {filter.Allows(weapon.def)}");
                }
                
                // Check SimpleSidearms
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    string ssReason;
                    bool ssAllows = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pawn, out ssReason);
                    Log.Message($"    - SimpleSidearms allows: {ssAllows} - {ssReason}");
                }
            }
            
            Log.Message($"\n[AutoArm] === End Diagnosis ===");
        }
        
        [DebugAction("AutoArm", "Compare Weapon Scores",
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CompareWeaponScores(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name?.ToStringShort ?? "unknown"} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Weapon Score Comparison for {pawn.Name?.ToStringShort ?? "unknown"} ===");
            
            // Log pawn info
            Log.Message($"[AutoArm] Shooting skill: {pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0}");
            Log.Message($"[AutoArm] Melee skill: {pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0}");
            Log.Message($"[AutoArm] Traits: {string.Join(", ", pawn.story?.traits?.allTraits?.Select(t => t.def.label) ?? new string[0])}");
            Log.Message($"[AutoArm] Current weapon: {pawn.equipment?.Primary?.Label ?? "None"}");
            
            var map = pawn.Map;
            if (map == null) return;
            
            // Get all available weapons
            var availableWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => !w.IsForbidden(pawn) && w.def.IsWeapon)
                .OrderByDescending(w => WeaponScoreCache.GetCachedScore(pawn, w))
                .Take(20)
                .ToList();
                
            Log.Message($"\n[AutoArm] Top 20 weapons by score:");
            
            foreach (var weapon in availableWeapons)
            {
                float totalScore = WeaponScoreCache.GetCachedScore(pawn, weapon);
                var baseScores = WeaponScoreCache.GetBaseWeaponScore(weapon);
                
                Log.Message($"\n  {weapon.Label} ({weapon.def.defName}) - Total: {totalScore:F1}");
                
                if (baseScores != null)
                {
                    Log.Message($"    - Quality: {baseScores.QualityScore:F1}");
                    Log.Message($"    - Damage: {baseScores.DamageScore:F1}");
                    Log.Message($"    - Range: {baseScores.RangeScore:F1}");
                    Log.Message($"    - Mods: {baseScores.ModScore:F1}");
                }
                
                // Get pawn-specific scores
                float pawnScore = WeaponScoringHelper.GetTotalScore(pawn, weapon);
                Log.Message($"    - Pawn-specific: {pawnScore:F1}");
                
                // Show weapon stats
                if (weapon.def.IsRangedWeapon && weapon.def.Verbs?.Count > 0)
                {
                    var verb = weapon.def.Verbs[0];
                    if (verb.defaultProjectile?.projectile != null)
                    {
                        float damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon);
                        float warmup = verb.warmupTime;
                        float cooldown = weapon.def.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
                        Log.Message($"    - Stats: {damage} dmg x {verb.burstShotCount} burst, {verb.range} range");
                        Log.Message($"    - Timing: {warmup}s warmup, {cooldown}s cooldown");
                    }
                }
            }
            
            // Specifically compare autopistol vs chain shotgun if available
            var autopistol = availableWeapons.FirstOrDefault(w => w.def.defName == "Gun_Autopistol");
            var chainShotgun = availableWeapons.FirstOrDefault(w => w.def.defName == "Gun_ChainShotgun");
            
            if (autopistol != null && chainShotgun != null)
            {
                Log.Message($"\n[AutoArm] === Direct Comparison: Autopistol vs Chain Shotgun ===");
                Log.Message($"  Autopistol score: {WeaponScoreCache.GetCachedScore(pawn, autopistol):F1}");
                Log.Message($"  Chain Shotgun score: {WeaponScoreCache.GetCachedScore(pawn, chainShotgun):F1}");
            }
            
            Log.Message($"\n[AutoArm] === End Comparison ===");
        }
    }
}

using LudeonTK;
using RimWorld;
using System.Collections;  // Add this line!
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm
{
    public static class AutoArmDebugCommands
    {
        [DebugAction("AutoArm", "Test Think Tree", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestThinkTree(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Think Tree for {pawn.Name} ===");

            // Test conditionals
            var weaponsInOutfitNode = new ThinkNode_ConditionalWeaponsInOutfit();
            var unarmedNode = new ThinkNode_ConditionalUnarmedOrPoorlyArmed();

            bool weaponsAllowed = weaponsInOutfitNode.TestSatisfied(pawn);
            bool isUnarmedOrPoor = unarmedNode.TestSatisfied(pawn);

            Log.Message($"[AutoArm] Weapons in outfit allowed: {weaponsAllowed}");
            Log.Message($"[AutoArm] Is unarmed or poorly armed: {isUnarmedOrPoor}");

            // Test current equipment
            var currentWeapon = pawn.equipment?.Primary;
            if (currentWeapon != null)
            {
                Log.Message($"[AutoArm] Current weapon: {currentWeapon.Label} (Score: {new JobGiver_PickUpBetterWeapon().GetWeaponScore(pawn, currentWeapon)})");
            }
            else
            {
                Log.Message($"[AutoArm] Currently UNARMED");
            }

            // Test job giver
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

            // Check outfit filter
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

        [DebugAction("AutoArm", "Test Force Weapon Check Now", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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
                // Mark as auto-equip for notification
                AutoEquipTracker.MarkAutoEquip(job, pawn);

                // Force start the job
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);

                var targetWeapon = job.targetA.Thing;
                Log.Message($"[AutoArm] Forced {pawn.Name} to pick up {targetWeapon.Label}");
            }
            else
            {
                Log.Message($"[AutoArm] {pawn.Name} - No weapon to pick up");
            }
        }

        [DebugAction("AutoArm", "Test Weapon Drop Check", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestWeaponDropCheck(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Weapon Drop for {pawn.Name} ===");

            // Check current weapon
            var currentWeapon = pawn.equipment?.Primary;
            if (currentWeapon == null)
            {
                Log.Message($"[AutoArm] No weapon equipped");
                return;
            }

            Log.Message($"[AutoArm] Current weapon: {currentWeapon.Label}");

            // Check outfit policy
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter == null)
            {
                Log.Message($"[AutoArm] No outfit policy set");
                return;
            }

            Log.Message($"[AutoArm] Outfit policy: {pawn.outfits.CurrentApparelPolicy.label}");

            // Check if weapon is allowed
            bool allowed = filter.Allows(currentWeapon.def);
            Log.Message($"[AutoArm] Weapon allowed by outfit: {allowed}");

            // Test if we would drop it
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

        [DebugAction("AutoArm", "Test Simple Sidearms", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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

                    // Force start the job
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
        // Add this to DebugCommands.cs:

        // Add this to DebugCommands.cs:

        [DebugAction("AutoArm", "Test Emergency Sidearm", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestEmergencySidearm(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                Log.Message($"[AutoArm] {pawn.Name} is not a colonist");
                return;
            }

            Log.Message($"\n[AutoArm] === Testing Emergency Sidearm for {pawn.Name} ===");

            // Test conditional
            var noSidearmsNode = new ThinkNode_ConditionalNoSidearms();
            bool hasNoSidearms = noSidearmsNode.TestSatisfied(pawn);

            Log.Message($"[AutoArm] Has no sidearms: {hasNoSidearms}");
            Log.Message($"[AutoArm] Simple Sidearms loaded: {SimpleSidearmsCompat.IsLoaded()}");
            Log.Message($"[AutoArm] Auto-equip sidearms enabled: {AutoArmMod.settings?.autoEquipSidearms}");

            // Check inventory
            if (pawn.inventory?.innerContainer != null)
            {
                var weapons = pawn.inventory.innerContainer.Where(t => t.def.IsWeapon).ToList();
                Log.Message($"[AutoArm] Weapons in inventory: {weapons.Count}");
                foreach (var weapon in weapons)
                {
                    Log.Message($"[AutoArm]   - {weapon.Label}");
                }
            }
            else
            {
                Log.Message($"[AutoArm] No inventory");
            }

            // Test emergency job giver
            if (hasNoSidearms && SimpleSidearmsCompat.IsLoaded())
            {
                var jobGiver = new JobGiver_GetSidearmEmergency();
                var job = jobGiver.TestTryGiveJob(pawn);

                if (job != null)
                {
                    Log.Message($"[AutoArm] Emergency sidearm job found: {job.def.defName} targeting {job.targetA.Thing?.Label}");

                    // Force start the job
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    Log.Message($"[AutoArm] Started emergency sidearm job");
                }
                else
                {
                    Log.Message($"[AutoArm] No emergency sidearm job found");
                }
            }

            Log.Message($"[AutoArm] === End Test ===\n");
        }

        [DebugAction("AutoArm", "Test Infusion Compatibility", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestInfusionCompat()
        {
            Log.Message("\n[AutoArm] === Testing Infusion 2 Compatibility ===");
            Log.Message($"[AutoArm] Infusion 2 loaded: {InfusionCompat.IsLoaded()}");

            if (!InfusionCompat.IsLoaded())
            {
                Log.Message("[AutoArm] Infusion 2 is not loaded");
                return;
            }

            // List infusion types
            InfusionCompat.DebugListInfusionTypes();

            // Check all weapons on the map
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

        [DebugAction("AutoArm", "Test List Simple Sidearms Types", allowedGameStates = AllowedGameStates.PlayingOnMap)]
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

                // Look for specific types we're interested in
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

        [DebugAction("AutoArm", "Test Force Sidearm Upgrade Check", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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

        [DebugAction("AutoArm", "Test Debug Simple Sidearms Data", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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

            // Force initialization
            var initMethod = typeof(SimpleSidearmsCompat).GetMethod("EnsureInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            initMethod?.Invoke(null, null);

            // Find CompSidearmMemory
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

            // Get RememberedWeapons property
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

            // Check current weapon
            if (pawn.equipment?.Primary != null)
            {
                Log.Message($"[AutoArm] Current primary weapon: {pawn.equipment.Primary.Label}");
            }
            else
            {
                Log.Message($"[AutoArm] No primary weapon equipped");
            }

            // Test finding a new sidearm
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
    }  // <-- This closing brace ends the class
}
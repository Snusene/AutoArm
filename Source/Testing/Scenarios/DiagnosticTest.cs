using AutoArm.Caching;
using AutoArm.Jobs;
using AutoArm.Logging;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing
{
    public static class DiagnosticTest
    {
        public static void RunFullDiagnostics(Map map)
        {
            AutoArmLogger.Debug(() => "========== AUTOARM DIAGNOSTICS ==========");

            AutoArmLogger.Debug(() => $"[DIAG] Mod Enabled: {AutoArmMod.settings?.modEnabled}");
            AutoArmLogger.Debug(() => $"[DIAG] Debug Logging: {AutoArmMod.settings?.debugLogging}");
            AutoArmLogger.Debug(() => $"[DIAG] Disable During Raids: {AutoArmMod.settings?.disableDuringRaids}");
            AutoArmLogger.Debug(() => $"[DIAG] Allow Children: {AutoArmMod.settings?.allowChildrenToEquipWeapons}");
            AutoArmLogger.Debug(() => $"[DIAG] Allow Temp Colonists: {AutoArmMod.settings?.allowTemporaryColonists}");

            var humanlikeThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
            if (humanlikeThinkTree?.thinkRoot == null)
            {
                AutoArmLogger.Error("[DIAG] CRITICAL: Humanlike think tree not found!");
            }
            else
            {
                bool foundEmergency = false;
                bool foundUpgrade = false;
                CheckThinkNode(humanlikeThinkTree.thinkRoot, ref foundEmergency, ref foundUpgrade, 0);
                AutoArmLogger.Debug(() => $"[DIAG] Think Tree - Emergency Node Found: {foundEmergency}");
                AutoArmLogger.Debug(() => $"[DIAG] Think Tree - Upgrade Node Found: {foundUpgrade}");
            }

            var colonists = map.mapPawns.FreeColonists.ToList();
            AutoArmLogger.Debug(() => $"[DIAG] Total colonists: {colonists.Count}");

            var weapons = WeaponCacheManager.GetAllWeapons(map).ToList();
            AutoArmLogger.Debug(() => $"[DIAG] Available weapons on map: {weapons.Count}");
            foreach (var weapon in weapons.Take(5))
            {
                AutoArmLogger.Debug(() => $"[DIAG]   - {weapon.Label} at {weapon.Position}");
            }

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            foreach (var pawn in colonists.Take(3))
            {
                AutoArmLogger.Debug(() => $"\n[DIAG] Testing pawn: {pawn.LabelShort}");
                AutoArmLogger.Debug(() => $"[DIAG]   - Armed: {pawn.equipment?.Primary != null}");
                AutoArmLogger.Debug(() => $"[DIAG]   - Current weapon: {pawn.equipment?.Primary?.Label ?? "none"}");
                AutoArmLogger.Debug(() => $"[DIAG]   - Age: {pawn.ageTracker?.AgeBiologicalYears}");
                AutoArmLogger.Debug(() => $"[DIAG]   - Is colonist: {pawn.IsColonist}");
                AutoArmLogger.Debug(() => $"[DIAG]   - Faction: {pawn.Faction?.Name}");
                AutoArmLogger.Debug(() => $"[DIAG]   - Drafted: {pawn.Drafted}");
                AutoArmLogger.Debug(() => $"[DIAG]   - In mental state: {pawn.InMentalState}");
                AutoArmLogger.Debug(() => $"[DIAG]   - Can work: {pawn.workSettings?.EverWork}");

                bool canConsider = PawnValidationCache.CanConsiderWeapons(pawn);
                AutoArmLogger.Debug(() => $"[DIAG]   - CanConsiderWeapons: {canConsider}");

                var weaponStatusNode = new ThinkNode_ConditionalWeaponStatus();
                float nodePriority = weaponStatusNode.GetPriority(pawn);
                bool nodeActive = nodePriority > 0f;
                AutoArmLogger.Debug(() => $"[DIAG]   - WeaponStatus Node Priority: {nodePriority:F1}, Active: {nodeActive}");

                var job = jobGiver.TestTryGiveJob(pawn);
                if (job != null)
                {
                    AutoArmLogger.Debug(() => $"[DIAG]   - Job created: {job.def.defName}");
                    AutoArmLogger.Debug(() => $"[DIAG]   - Target weapon: {job.targetA.Thing?.Label}");
                }
                else
                {
                    AutoArmLogger.Debug(() => $"[DIAG]   - No job created");
                }
            }

            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            bool raidActive = ModInit.IsLargeRaidActive;
            AutoArmLogger.Debug(() => $"\n[DIAG] Raid Active: {raidActive}");

            AutoArmLogger.Debug(() => "========== END DIAGNOSTICS ==========\n");
        }

        private static void CheckThinkNode(ThinkNode node, ref bool foundEmergency, ref bool foundUpgrade, int depth)
        {
            if (node == null) return;

            string indent = new string(' ', depth * 2);

            if (node is ThinkNode_ConditionalWeaponStatus)
            {
                foundEmergency = true;
                foundUpgrade = true;
                AutoArmLogger.Debug(() => $"{indent}[DIAG] Found ThinkNode_ConditionalWeaponStatus at depth {depth}");
            }

            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    CheckThinkNode(subNode, ref foundEmergency, ref foundUpgrade, depth + 1);
                }
            }
        }
    }
}


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
            // Per-map handling
        }
    }

    internal static class ModInit
    {
        public static bool IsLargeRaidActive => RaidChecker.ShouldSkipDuringRaid(Find.CurrentMap);
    }

    [HarmonyPatch(typeof(Root_Play), "Start")]
    [HarmonyPatchCategory(Patches.PatchCategories.Core)]
    internal static class Root_Play_Start_Patch
    {
        private static bool treeChecked;

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

            CheckModCompatibility();

            if (treeChecked)
                return;
            treeChecked = true;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (ValidateThinkTreeInjection(logWarning: true))
                    FinalizeInitialization();
                else
                    Messages.Message("AutoArm_ThinkTreeInjectionFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
            });
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

    internal static class AfterLoad
    {
        public static void Run()
        {
            AutoArm.Helpers.ForcedWeaponLabelHelper.ResetFieldChecking();

            Root_Play_Start_Patch.ClearAllCachesOnStartup(false);

            SimpleSidearmsCompat.CleanupAfterLoad();
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

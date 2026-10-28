using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace AutoArm.UI
{
    internal static class PolicyDBHelper
    {
        public static IEnumerable<ApparelPolicy> GetAllPolicies()
        {
            var game = Current.Game;
            if (game == null) yield break;

            var field = AccessTools.Field(game.GetType(), "apparelPolicyDatabase")
                     ?? AccessTools.Field(game.GetType(), "outfitDatabase");
            var db = field?.GetValue(game);
            if (db == null) yield break;

            if (db is OutfitDatabase outfitDb)
            {
                foreach (var outfit in outfitDb.AllOutfits) yield return outfit;
            }
            else
            {
                var dbType = db.GetType();
                if (dbType.Name == "ApparelPolicyDatabase")
                {
                    var prop = AccessTools.Property(dbType, "AllApparelPolicies");
                    if (prop != null)
                    {
                        var policies = prop.GetValue(db) as IEnumerable;
                        if (policies != null)
                        {
                            foreach (var policy in policies)
                            {
                                if (policy is ApparelPolicy ap) yield return ap;
                            }
                        }
                    }
                }
            }
        }
    }

    [StaticConstructorOnStartup]
    internal static class ApparelWeaponsIntegration
    {
        private static bool _drawingPolicyDialog;

        private static ThingCategoryDef _weapons;
        private static ThingCategoryDef _apparel;

        private static AccessTools.FieldRef<Listing_TreeThingFilter, ThingFilter> _parentFilterRef;
        private static Action<Listing_TreeThingFilter, TreeNode_ThingCategory, int, int, Map, bool> _doCategory;

        static ApparelWeaponsIntegration()
        {
            var h = AutoArmInit.harmonyInstance ?? new Harmony("Snues.AutoArm");

            try
            {
                _weapons = AutoArmDefOf.Weapons ?? ThingCategoryDefOf.Weapons;
                _apparel = AutoArmDefOf.Apparel ?? ThingCategoryDefOf.Apparel;

                AutoArmLogger.Debug(() => $"Categories: Weapons={_weapons?.defName}, Apparel={_apparel?.defName}");
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorUI(ex, "WeaponTab", "DefInitialization");
            }

            try
            {
                var doCategory = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategory",
                    new Type[] { typeof(TreeNode_ThingCategory), typeof(int), typeof(int), typeof(Map), typeof(bool) });
                if (doCategory != null)
                {
                    _doCategory = (Action<Listing_TreeThingFilter, TreeNode_ThingCategory, int, int, Map, bool>)
                        Delegate.CreateDelegate(
                            typeof(Action<Listing_TreeThingFilter, TreeNode_ThingCategory, int, int, Map, bool>),
                            doCategory, throwOnBindFailure: false);
                }

                var parentFilterField = AccessTools.Field(typeof(Listing_TreeThingFilter), "parentFilter")
                    ?? typeof(Listing_TreeThingFilter).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                                     .FirstOrDefault(f => f.FieldType == typeof(ThingFilter));
                if (parentFilterField != null)
                {
                    _parentFilterRef = AccessTools.FieldRefAccess<Listing_TreeThingFilter, ThingFilter>(parentFilterField);
                }

                var listChildren = AccessTools.Method(typeof(Listing_TreeThingFilter), "ListCategoryChildren",
                    new Type[] { typeof(TreeNode_ThingCategory), typeof(int), typeof(Map), typeof(Rect) });
                var doContents = AccessTools.Method(typeof(Dialog_ManageApparelPolicies), "DoContentsRect",
                    new Type[] { typeof(Rect) });

                if (_doCategory == null || _parentFilterRef == null || listChildren == null || doContents == null)
                {
                    AutoArmLogger.Warn("WeaponTab: Listing_TreeThingFilter internals not resolved. Weapons category will not appear in apparel policies.");
                    return;
                }

                h.Patch(doContents,
                    prefix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(DoContentsRect_Prefix)),
                    finalizer: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(DoContentsRect_Finalizer)));
                h.Patch(listChildren,
                    postfix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(ListCategoryChildren_Postfix)));

                AutoArmLogger.Debug(() => "WeaponTab: draw-time weapons injection patched");
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorPatch(ex, "ApparelWeaponsIntegration");
            }
        }

        public static void DoContentsRect_Prefix()
        {
            _drawingPolicyDialog = true;
        }

        public static void DoContentsRect_Finalizer()
        {
            _drawingPolicyDialog = false;
        }

        public static void ListCategoryChildren_Postfix(Listing_TreeThingFilter __instance, TreeNode_ThingCategory node, int openMask, Map map)
        {
            if (!_drawingPolicyDialog)
                return;

            try
            {
                if (AutoArmMod.settings?.modEnabled != true)
                    return;

                if (_weapons?.treeNode == null || node?.catDef == null || !IsApparel(node.catDef))
                    return;

                ref ThingFilter parentFilter = ref _parentFilterRef(__instance);
                var saved = parentFilter;
                parentFilter = null;
                try
                {
                    _doCategory(__instance, _weapons.treeNode, 0, openMask, map, false);
                }
                finally
                {
                    parentFilter = saved;
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorPatch(ex, "ListCategoryChildren_Postfix");
            }
        }

        private static bool IsApparel(ThingCategoryDef cat)
        {
            if (cat == null) return false;
            if (cat == ThingCategoryDefOf.Apparel) return true;
            return ReferenceEquals(cat, AutoArmDefOf.Apparel) || ReferenceEquals(cat, ThingCategoryDefOf.Apparel);
        }

    }

    [StaticConstructorOnStartup]
    internal static class WeaponTab
    {
        static WeaponTab()
        {
            try
            {
                var harmony = AutoArmInit.harmonyInstance ?? new Harmony("Snues.AutoArm");

                var constructors = AccessTools.GetDeclaredConstructors(typeof(Dialog_ManageApparelPolicies));
                if (constructors != null && constructors.Count > 0)
                {
                    int patchedCount = 0;
                    foreach (var ctor in constructors)
                    {
                        if (ctor != null)
                        {
                            try
                            {
                                harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(Dialog_ManageApparelPolicies_Lifecycle), nameof(Dialog_ManageApparelPolicies_Lifecycle.ApparelDialog_Ctor_Postfix)));
                                patchedCount++;
                            }
                            catch (Exception ex)
                            {
                                AutoArmLogger.Warn($"WeaponTab: Failed to patch constructor: {ex.Message}");
                            }
                        }
                    }
                    if (patchedCount > 0)
                        AutoArmLogger.Debug(() => $"WeaponTab: Patched {patchedCount} Dialog_ManageApparelPolicies constructor(s)");
                }

                var postCloseMethod = AccessTools.Method(typeof(Window), "PostClose");
                if (postCloseMethod != null)
                {
                    try
                    {
                        harmony.Patch(postCloseMethod, postfix: new HarmonyMethod(typeof(Dialog_ManageApparelPolicies_Lifecycle), nameof(Dialog_ManageApparelPolicies_Lifecycle.Window_PostClose_Postfix)));
                        AutoArmLogger.Debug(() => "WeaponTab: Patched Window.PostClose");
                    }
                    catch (Exception ex)
                    {
                        AutoArmLogger.Warn($"WeaponTab: Failed to patch Window.PostClose: {ex.Message}");
                    }
                }
                else
                {
                    AutoArmLogger.Warn("WeaponTab: Window.PostClose method not found");
                }

                try
                {
                    int rangedCount = Validation.RangedWeapons.Count;
                    int meleeCount = Validation.MeleeWeapons.Count;
                    AutoArmLogger.Debug(() => $"WeaponTab: ranged={rangedCount}, melee={meleeCount}");
                }
                catch (Exception e)
                {
                    AutoArmLogger.ErrorUI(e, "WeaponTab", "DefCounting");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorUI(e, "WeaponTab", "Initialization");
            }
        }

    }

    internal static class Dialog_ManageApparelPolicies_Lifecycle
    {
        public static void ApparelDialog_Ctor_Postfix()
        {
            try { WeaponPolicyBatcher.Begin(); } catch (Exception e) { AutoArmLogger.ErrorUI(e, "WeaponTab", "LifecycleBeginBatch"); }
        }

        public static void Window_PostClose_Postfix(Window __instance)
        {
            if (!(__instance is Dialog_ManageApparelPolicies)) return;
            try { WeaponPolicyBatcher.Apply(); } catch (Exception e) { AutoArmLogger.ErrorUI(e, "WeaponTab", "LifecycleApplyBatch"); }
        }
    }

    [HarmonyPatch(typeof(ApparelPolicy), "ExposeData")]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    internal static class ApparelPolicy_ExposeData_Migration
    {
        [HarmonyPostfix]
        public static void Postfix(ApparelPolicy __instance)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                List<string> dummy = null;
                Scribe_Collections.Look(ref dummy, "autoArmAllowedWeapons", LookMode.Value);
                Scribe_Collections.Look(ref dummy, "autoArmDisallowedWeapons", LookMode.Value);
                Scribe_Collections.Look(ref dummy, "autoArmAllowedCategories", LookMode.Value);
                Scribe_Collections.Look(ref dummy, "autoArmDisallowedCategories", LookMode.Value);
                if (dummy != null)
                    AutoArmLogger.Debug(() => $"[MIGRATION] Consumed old save data for outfit '{__instance.label}' - now using vanilla save system");
            }
        }
    }

    internal static class WeaponPolicyBatcher
    {
        private static bool _batching;

        public static void Begin()
        {
            if (_batching) return;
            _batching = true;
            AutoArmLogger.Debug(() => "WeaponPolicyBatcher: BEGIN");
        }

        public static void Apply()
        {
            if (!_batching) return;
            _batching = false;

            try
            {
                foreach (var policy in PolicyDBHelper.GetAllPolicies())
                    if (policy != null)
                        Caching.WeaponCache.OnOutfitFilterChanged(policy);
            }
            catch (Exception ex) { AutoArmLogger.ErrorUI(ex, "WeaponPolicyBatcher", "Apply"); }

            AutoArmLogger.Debug(() => "WeaponPolicyBatcher: END");
        }
    }
}

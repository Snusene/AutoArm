using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm.UI
{
    public static class PolicyDBHelper
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
    public static class ApparelWeaponsIntegration
    {
        public static void MoveWeaponsForUI()
        {
            if (_depth == 0) Push();
        }

        public static void RestoreWeaponsPosition()
        {
            Pop();
        }

        public static void Push()
        {
            _depth++;
            if (_depth == 1)
            {
                AutoArmLogger.Debug(() => "Push() — moving Weapons under Apparel");
                TryRebuild(true);
            }
        }

        public static void Pop()
        {
            if (_depth <= 0)
            {
                AutoArmLogger.Debug(() => $"Pop() called but depth={_depth}, ignoring");
                _depth = 0;
                return;
            }
            _depth--;
            if (_depth == 0)
            {
                AutoArmLogger.Debug(() => "Pop() — restoring Weapons to original parent");
                TryRebuild(false);
            }
        }

        public static void OnApparelDialogClosed()
        {
            Pop();
            if (_depth > 0)
            {
                AutoArmLogger.Warn($"Depth still {_depth} after close — forcing restore");
                _depth = 1;
                Pop();
            }
        }

        public static int GetDepth() => _depth;

        private static int _depth;
        private static ThingCategoryDef _weapons;
        private static ThingCategoryDef _apparel;
        private static ThingCategoryDef _apparelMisc;
        private static ThingCategoryDef _origParent;
        private static int _origIndex = -1;

        private static MethodInfo _doSpec16;

        private static MethodInfo _doChildren16;
        private static FieldInfo _parentFilterField;

        static ApparelWeaponsIntegration()
        {
            var h = AutoArmInit.harmonyInstance ?? new Harmony("Snues.AutoArm");

            try
            {
                _weapons = AutoArmDefOf.Weapons ?? ThingCategoryDefOf.Weapons;
                _apparel = AutoArmDefOf.Apparel ?? ThingCategoryDefOf.Apparel;

                _apparelMisc = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("ApparelMisc");

                AutoArmLogger.Debug(() => $"Categories — Weapons={_weapons?.defName}, Apparel={_apparel?.defName}");
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorUI(ex, "WeaponTab", "DefInitialization");
            }

            int patchedUI = 0;

            try
            {
                var doCat16 = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategory",
                    new Type[] { typeof(TreeNode_ThingCategory), typeof(int), typeof(int), typeof(Map), typeof(bool) });
                if (doCat16 != null)
                {
                    h.Patch(doCat16, prefix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(Prefix16)));
                    patchedUI++;
                    AutoArmLogger.Debug(() => "Patched Listing_TreeThingFilter.DoCategory (node, 1.6)");
                }

                var doCat15 = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategory",
                    new Type[] { typeof(ThingCategoryDef), typeof(int), typeof(int) });
                if (doCat15 != null)
                {
                    h.Patch(doCat15, prefix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(Prefix15)));
                    patchedUI++;
                    AutoArmLogger.Debug(() => "Patched Listing_TreeThingFilter.DoCategory (def, 1.5)");
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorPatch(ex, "Listing_TreeThingFilter_DoCategory");
            }

            AutoArmLogger.Debug(() => $"Patch summary — UI={patchedUI}");
        }

        private static void TryRebuild(bool moveUnderApparel)
        {
            try
            {
                if (_weapons == null || _apparel == null) return;

                if (moveUnderApparel)
                {
                    if (_apparel.childCategories == null)
                        _apparel.childCategories = new List<ThingCategoryDef>();

                    if (_origParent == null)
                    {
                        _origParent = _weapons.parent;
                        _origIndex = (_origParent?.childCategories != null)
                                   ? _origParent.childCategories.IndexOf(_weapons) : -1;
                        AutoArmLogger.Debug(() => "captured original parent=" +
                                        (_origParent != null ? _origParent.defName : "null") + " index=" + _origIndex);
                    }

                    if (_weapons.parent?.childCategories != null)
                        _weapons.parent.childCategories.Remove(_weapons);

                    int insertAt = _apparel.childCategories.Count;
                    if (_apparelMisc != null)
                    {
                        int idx = _apparel.childCategories.IndexOf(_apparelMisc);
                        if (idx >= 0) insertAt = idx + 1;
                    }
                    if (insertAt > _apparel.childCategories.Count) insertAt = _apparel.childCategories.Count;

                    int current = _apparel.childCategories.IndexOf(_weapons);
                    if (current >= 0)
                    {
                        if (current != insertAt)
                        {
                            _apparel.childCategories.RemoveAt(current);
                            if (insertAt > _apparel.childCategories.Count) insertAt = _apparel.childCategories.Count;
                            _apparel.childCategories.Insert(insertAt, _weapons);
                        }
                    }
                    else
                    {
                        _apparel.childCategories.Insert(insertAt, _weapons);
                    }
                    _weapons.parent = _apparel;

                    AutoArmLogger.Debug(() => $"moved Weapons under Apparel at index {insertAt}");
                }
                else
                {
                    if (_apparel?.childCategories != null)
                        _apparel.childCategories.Remove(_weapons);

                    if (_origParent != null)
                    {
                        if (_origParent.childCategories == null)
                            _origParent.childCategories = new List<ThingCategoryDef>();

                        if (_origIndex >= 0 && _origIndex < _origParent.childCategories.Count)
                            _origParent.childCategories.Insert(_origIndex, _weapons);
                        else if (!_origParent.childCategories.Contains(_weapons))
                            _origParent.childCategories.Add(_weapons);

                        _weapons.parent = _origParent;
                        AutoArmLogger.Debug(() => "restored Weapons to " + _origParent.defName +
                                        " at index " + _origIndex);
                    }

                    _origParent = null;
                    _origIndex = -1;
                }

                ForceRebuildCategoryNodes();
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorUI(ex, "WeaponTab", "CategoryReparenting");
            }
        }

        private static void ForceRebuildCategoryNodes()
        {
            try
            {
                Type dbType = AccessTools.TypeByName("Verse.ThingCategoryNodeDatabase") ?? AccessTools.TypeByName("RimWorld.ThingCategoryNodeDatabase");
                if (dbType == null) { AutoArmLogger.Debug(() => "NodeDB type not found"); return; }

                MethodInfo rebuild = AccessTools.Method(dbType, "Rebuild") ?? AccessTools.Method(dbType, "Regenerate") ?? AccessTools.Method(dbType, "Reset");
                if (rebuild != null)
                {
                    rebuild.Invoke(null, null);
                    AutoArmLogger.Debug(() => $"Invoked {dbType.Name}.{rebuild.Name}()");
                    return;
                }

                FieldInfo rootField = AccessTools.Field(dbType, "rootNode");
                if (rootField != null) { rootField.SetValue(null, null); AutoArmLogger.Debug(() => "NodeDB.rootNode cleared"); }
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorUI(ex, "WeaponTab", "NodeDBRebuild");
            }
        }

        public static bool Prefix16(Listing_TreeThingFilter __instance, TreeNode_ThingCategory node, int indentLevel, int openMask, Map map, bool subtreeMatchedSearch)
        {
            try
            {
                var cat = node?.catDef;

                if (!IsApparel(cat) || _depth <= 0) return true;

                Cache16();

                try { _doSpec16?.Invoke(__instance, new object[] { node, indentLevel, map }); } catch { }

                object savedParentFilter = null;
                bool mustRestore = false;

                if (_parentFilterField != null)
                {
                    try
                    {
                        savedParentFilter = _parentFilterField.GetValue(__instance);
                        _parentFilterField.SetValue(__instance, null);
                        mustRestore = true;
                    }
                    catch (Exception ex)
                    {
                        AutoArmLogger.Debug(() => $"Failed to modify parent filter: {ex.Message}");
                    }
                }

                try
                {
                    _doChildren16?.Invoke(__instance, new object[] { node, indentLevel, openMask, map, subtreeMatchedSearch });
                }
                catch (Exception ex)
                {
                    AutoArmLogger.Debug(() => $"[WeaponTab] Error in DoCategoryChildren: {ex.Message}");
                }
                finally
                {
                    if (mustRestore && _parentFilterField != null)
                    {
                        try
                        {
                            _parentFilterField.SetValue(__instance, savedParentFilter);
                        }
                        catch (Exception ex)
                        {
                            AutoArmLogger.ErrorUI(ex, "WeaponTab", "RestoreParentFilter");
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorPatch(ex, "Prefix16");
                return true;
            }
        }

        private static void Cache16()
        {
            if (_doSpec16 == null)
                _doSpec16 = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoSpecialFilters",
                    new Type[] { typeof(TreeNode_ThingCategory), typeof(int), typeof(Map) });
            if (_doChildren16 == null)
                _doChildren16 = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategoryChildren",
                    new Type[] { typeof(TreeNode_ThingCategory), typeof(int), typeof(int), typeof(Map), typeof(bool) });
            if (_parentFilterField == null)
            {
                _parentFilterField = AccessTools.Field(typeof(Listing_TreeThingFilter), "parentFilter")
                    ?? typeof(Listing_TreeThingFilter).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                                     .FirstOrDefault(f => f.FieldType == typeof(ThingFilter));
            }
        }

        private static bool IsApparel(ThingCategoryDef cat)
        {
            if (cat == null) return false;
            if (cat == ThingCategoryDefOf.Apparel) return true;
            return ReferenceEquals(cat, AutoArmDefOf.Apparel) || ReferenceEquals(cat, ThingCategoryDefOf.Apparel);
        }

        public static bool Prefix15(Listing_TreeThingFilter __instance, ThingCategoryDef cat, int nestLevel, int openMask)
        {
            if (_depth <= 0) return true;
            return true;
        }
    }

    [StaticConstructorOnStartup]
    public static class WeaponTabInjector
    {
        static WeaponTabInjector()
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
                                AutoArmLogger.Warn($"WeaponTabInjector: Failed to patch constructor: {ex.Message}");
                            }
                        }
                    }
                    if (patchedCount > 0)
                        AutoArmLogger.Debug(() => $"WeaponTabInjector: Patched {patchedCount} Dialog_ManageApparelPolicies constructor(s)");
                }

                var closeMethod = AccessTools.Method(typeof(Window), "Close", new Type[] { typeof(bool) });
                if (closeMethod != null)
                {
                    try
                    {
                        harmony.Patch(closeMethod, prefix: new HarmonyMethod(typeof(Dialog_ManageApparelPolicies_Lifecycle), nameof(Dialog_ManageApparelPolicies_Lifecycle.Window_Close_Prefix)));
                        AutoArmLogger.Debug(() => "WeaponTabInjector: Patched Window.Close");
                    }
                    catch (Exception ex)
                    {
                        AutoArmLogger.Warn($"WeaponTabInjector: Failed to patch Window.Close: {ex.Message}");
                    }
                }
                else
                {
                    AutoArmLogger.Warn("WeaponTabInjector: Window.Close method not found");
                }

                try
                {
                    var baseType = typeof(Dialog_ManageApparelPolicies).BaseType;
                    if (baseType != null)
                    {
                        var doWindowMethod = AccessTools.Method(baseType, "DoWindowContents");
                        if (doWindowMethod != null)
                        {
                            harmony.Patch(doWindowMethod, postfix: new HarmonyMethod(typeof(Dialog_ManageApparelPolicies_Patches), nameof(Dialog_ManageApparelPolicies_Patches.DoWindowContents_Postfix)));
                            AutoArmLogger.Debug(() => $"WeaponTabInjector: Patched {baseType.Name}.DoWindowContents");
                        }
                    }
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"WeaponTabInjector: DoWindowContents patch skipped: {e.Message}");
                }

                try
                {
                    int rangedCount = WeaponValidation.RangedWeapons.Count;
                    int meleeCount = WeaponValidation.MeleeWeapons.Count;
                    AutoArmLogger.Debug(() => $"WeaponTabInjector: defs — ranged={rangedCount}, melee={meleeCount}");
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

    public static class Dialog_ManageApparelPolicies_Lifecycle
    {
        public static void ApparelDialog_Ctor_Postfix()
        {
            try { ApparelWeaponsIntegration.Push(); } catch (Exception e) { AutoArmLogger.ErrorUI(e, "WeaponTab", "LifecyclePush"); }
            try { WeaponPolicyBatcher.Begin(); } catch (Exception e) { AutoArmLogger.ErrorUI(e, "WeaponTab", "LifecycleBeginBatch"); }
            try { ThingFilter_Allows_Thing_Patch.EnableForDialog(); } catch (Exception e) { AutoArmLogger.ErrorPatch(e, "ThingFilter_Allows_Enable"); }
        }

        public static void Window_Close_Prefix(Window __instance, ref bool doCloseSound)
        {
            if (!(__instance is Dialog_ManageApparelPolicies)) return;
            try { ThingFilter_Allows_Thing_Patch.DisableForDialog(); } catch (Exception e) { AutoArmLogger.ErrorPatch(e, "ThingFilter_Allows_Disable"); }
            try { WeaponPolicyBatcher.Apply(); } catch (Exception e) { AutoArmLogger.ErrorUI(e, "WeaponTab", "LifecycleApplyBatch"); }
            try { ApparelWeaponsIntegration.OnApparelDialogClosed(); } catch (Exception e) { AutoArmLogger.ErrorUI(e, "WeaponTab", "LifecyclePop"); }
        }
    }

    public static class Dialog_ManageApparelPolicies_Patches
    {
        public static void DoWindowContents_Postfix()
        {
            if (!ThingFilter_Allows_Thing_Patch.IsPatchEnabled())
                ThingFilter_Allows_Thing_Patch.EnableForDialog();
        }
    }

    public static class ThingFilter_Allows_Thing_Patch
    {
        private static bool _patchEnabled = false;
        private static readonly HashSet<ThingFilter> _outfitFilters = new HashSet<ThingFilter>();
        private static FieldInfo _allowedDefsField;
        private static readonly Dictionary<ThingFilter, HashSet<ThingDef>> _allowedDefsCache = new Dictionary<ThingFilter, HashSet<ThingDef>>();
        private static Harmony _harmonyInstance;
        private static System.Reflection.MethodInfo _targetMethod;
        private static System.Reflection.MethodInfo _patchMethod;

        static ThingFilter_Allows_Thing_Patch()
        {
            _targetMethod = AccessTools.Method(typeof(ThingFilter), "Allows", new Type[] { typeof(Thing) });
            _patchMethod = AccessTools.Method(typeof(ThingFilter_Allows_Thing_Patch), nameof(Postfix));
        }

        public static void EnableForDialog()
        {
            if (_patchEnabled) return;

            _patchEnabled = true;

            try
            {
                if (_harmonyInstance == null)
                    _harmonyInstance = new Harmony("Snues.AutoArm.DynamicPatch");

                _harmonyInstance.Patch(_targetMethod, postfix: new HarmonyMethod(_patchMethod, Priority.Last));
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "ThingFilter_Allows_Apply");
            }

            RebuildOutfitFilterCache();
            _allowedDefsCache.Clear();

            _logCounter = 0;
        }

        public static bool IsPatchEnabled() => _patchEnabled;

        public static void DisableForDialog()
        {
            if (!_patchEnabled) return;

            _patchEnabled = false;

            try
            {
                if (_harmonyInstance != null)
                {
                    _harmonyInstance.Unpatch(_targetMethod, _patchMethod);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "ThingFilter_Allows_Remove");
            }

            _allowedDefsCache.Clear();
            _outfitFilters.Clear();
            _loggedExceptions.Clear();
            _hasLoggedPatchActive = false;
            _logCounter = 0;
        }

        private static int _logCounter = 0;
        private static bool _hasLoggedPatchActive = false;
        private static readonly HashSet<string> _loggedExceptions = new HashSet<string>();

        public static void Postfix(ThingFilter __instance, Thing t, ref bool __result)
        {
            if (!IsOutfitFilter(__instance))
            {
                return;
            }

            if (!_hasLoggedPatchActive)
            {
                _hasLoggedPatchActive = true;
                AutoArmLogger.Debug(() => "[ThingFilter.Allows] Patch is active and being called!");
            }

            if (__result) return;

            if (t?.def?.IsWeapon == true)
            {
                bool shouldLog = _logCounter++ < 20;

                if (shouldLog)
                    AutoArmLogger.Debug(() => $"[ThingFilter.Allows] Checking weapon {t.LabelCap}");

                bool filterAllowsDef = false;
                try
                {
                    filterAllowsDef = __instance.Allows(t.def);
                }
                catch (Exception ex)
                {
                    string exceptionKey = $"{__instance.GetType().Name}|{t.def.defName}|{ex.GetType().Name}";
                    if (_loggedExceptions.Add(exceptionKey))
                    {
                        AutoArmLogger.Warn($"[ThingFilter.Allows] Exception checking filter for weapon {t.def.defName}: {ex.GetType().Name} - {ex.Message}");
                    }
                }

                if (filterAllowsDef)
                {
                    if (t is ThingWithComps twc)
                    {
                        if (!CheckQualityFilter(__instance, twc))
                        {
                            if (shouldLog) AutoArmLogger.Debug(() => "  - Failed quality filter");
                            return;
                        }
                        if (!CheckHitPointsFilter(__instance, twc))
                        {
                            if (shouldLog) AutoArmLogger.Debug(() => "  - Failed HP filter");
                            return;
                        }
                    }
                    __result = true;
                    if (shouldLog) AutoArmLogger.Debug(() => "  - ALLOWED via filter.Allows(def)");
                    return;
                }

                var allowedDefs = GetAllowedDefs(__instance);
                if (allowedDefs != null && allowedDefs.Contains(t.def))
                {
                    if (t is ThingWithComps twc2)
                    {
                        if (!CheckQualityFilter(__instance, twc2))
                        {
                            if (shouldLog) AutoArmLogger.Debug(() => "  - Failed quality filter (specific def)");
                            return;
                        }
                        if (!CheckHitPointsFilter(__instance, twc2))
                        {
                            if (shouldLog) AutoArmLogger.Debug(() => "  - Failed HP filter (specific def)");
                            return;
                        }
                    }
                    __result = true;
                    if (shouldLog) AutoArmLogger.Debug(() => "  - ALLOWED via specific def (fallback)");
                }
                else if (shouldLog)
                {
                    AutoArmLogger.Debug(() => "  - Not allowed by filter");
                }
            }
        }

        private static HashSet<ThingDef> GetAllowedDefs(ThingFilter filter)
        {
            if (_allowedDefsCache.TryGetValue(filter, out var cached)) return cached;
            if (_allowedDefsField == null)
                _allowedDefsField = AccessTools.Field(typeof(ThingFilter), "allowedDefs");

            var allowedDefs = _allowedDefsField?.GetValue(filter) as HashSet<ThingDef>;
            if (allowedDefs != null)
                _allowedDefsCache[filter] = allowedDefs;
            return allowedDefs;
        }

        private static bool IsOutfitFilter(ThingFilter filter)
        {
            return filter != null && _outfitFilters.Contains(filter);
        }

        private static void RebuildOutfitFilterCache()
        {
            _outfitFilters.Clear();
            int policyCount = 0;
            foreach (var policy in PolicyDBHelper.GetAllPolicies())
            {
                if (policy?.filter != null)
                {
                    _outfitFilters.Add(policy.filter);
                    policyCount++;
                }
            }

            AutoArmLogger.Debug(() => $"[ThingFilter.Allows] Rebuilt outfit filter cache: found {policyCount} policies with filters");
        }

        public static void InvalidateCache()
        {
            _outfitFilters.Clear();
            _allowedDefsCache.Clear();
        }

        private static bool CheckQualityFilter(ThingFilter filter, ThingWithComps weapon)
        {
            var allowedQualities = filter.AllowedQualityLevels;
            if (allowedQualities != QualityRange.All)
                if (weapon.TryGetQuality(out var quality))
                    return allowedQualities.Includes(quality);
            return true;
        }

        private static bool CheckHitPointsFilter(ThingFilter filter, ThingWithComps weapon)
        {
            var allowedHitPointsPercents = filter.AllowedHitPointsPercents;
            if (allowedHitPointsPercents != FloatRange.ZeroToOne)
            {
                float hitPointsPercent = (float)weapon.HitPoints / weapon.MaxHitPoints;
                if (allowedHitPointsPercents.max < 1.0f)
                    return hitPointsPercent <= allowedHitPointsPercents.max;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ApparelPolicy), "ExposeData")]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    public static class ApparelPolicy_ExposeData_Migration
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

    [StaticConstructorOnStartup]
    public static class WeaponPolicyBatcher
    {
        private static readonly Harmony H = AutoArmInit.harmonyInstance ?? new Harmony("Snues.AutoArm");

        private static bool _batching;
        public static bool IsBatching => _batching;

        private static readonly Dictionary<ThingFilter, ApparelPolicy> _filterToPolicy = new Dictionary<ThingFilter, ApparelPolicy>(ReferenceEqualityComparer<ThingFilter>.Instance);
        private static readonly Dictionary<ThingFilter, HashSet<ThingDef>> _pendingDisallow = new Dictionary<ThingFilter, HashSet<ThingDef>>(ReferenceEqualityComparer<ThingFilter>.Instance);


        static WeaponPolicyBatcher()
        {
            try
            {
                var setAllowDef = AccessTools.Method(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingDef), typeof(bool) });
                if (setAllowDef != null)
                {
                    H.Patch(setAllowDef, prefix: new HarmonyMethod(typeof(WeaponPolicyBatcher), nameof(SetAllowDef_Prefix)));
                    H.Patch(setAllowDef, postfix: new HarmonyMethod(typeof(WeaponPolicyBatcher), nameof(SetAllowDef_Postfix)));
                }
                AutoArmLogger.Debug(() => "WeaponPolicyBatcher: patches installed");
            }
            catch (Exception ex) { AutoArmLogger.ErrorUI(ex, "WeaponPolicyBatcher", "Initialization"); }
        }

        public static void Begin()
        {
            if (_batching) return;
            _batching = true;
            _pendingDisallow.Clear();
            _filterToPolicy.Clear();

            foreach (var ap in PolicyDBHelper.GetAllPolicies())
                if (ap?.filter != null && !_filterToPolicy.ContainsKey(ap.filter))
                    _filterToPolicy.Add(ap.filter, ap);

            AutoArmLogger.Debug(() => "WeaponPolicyBatcher: BEGIN (mapped " + _filterToPolicy.Count + " filters)");
        }

        public static void Apply()
        {
            if (!_batching) return;
            try
            {
                var modifiedOutfits = new HashSet<ApparelPolicy>();
                foreach (var kvp in _pendingDisallow)
                {
                    if (_filterToPolicy.TryGetValue(kvp.Key, out var policy))
                        modifiedOutfits.Add(policy);
                }

                ApplyPending();

                foreach (var outfit in modifiedOutfits)
                {
                    bool handledIncremental = false;

                    if (_policyToDisallowedDefs.TryGetValue(outfit, out var disallowedDefs))
                    {
                        foreach (var weaponDef in disallowedDefs)
                        {
                            Caching.WeaponCacheManager.OnOutfitFilterChanged(outfit, weaponDef);
                            handledIncremental = true;
                        }
                    }

                    ThingFilter filterForOutfit = null;
                    foreach (var kvp in _filterToPolicy)
                    {
                        if (kvp.Value == outfit)
                        {
                            filterForOutfit = kvp.Key;
                            break;
                        }
                    }

                    if (filterForOutfit != null && _pendingAllow.TryGetValue(filterForOutfit, out var allowedDefs))
                    {
                        foreach (var weaponDef in allowedDefs)
                        {
                            Caching.WeaponCacheManager.OnOutfitFilterChanged(outfit, weaponDef);
                            handledIncremental = true;
                        }
                    }

                    if (!handledIncremental)
                    {
                        Caching.WeaponCacheManager.OnOutfitFilterChanged(outfit);
                    }
                }

                foreach (var outfit in PolicyDBHelper.GetAllPolicies())
                {
                    if (!modifiedOutfits.Contains(outfit))
                    {
                        Caching.WeaponCacheManager.OnOutfitFilterChanged(outfit);
                    }
                }
            }
            catch (Exception ex) { AutoArmLogger.ErrorUI(ex, "WeaponPolicyBatcher", "Apply"); }
            finally
            {
                _pendingDisallow.Clear();
                _pendingAllow.Clear();
                _policyToDisallowedDefs.Clear();
                _filterToPolicy.Clear();
                _batching = false;
                AutoArmLogger.Debug(() => "WeaponPolicyBatcher: END");
            }
        }

        private static Dictionary<ThingFilter, HashSet<ThingDef>> _pendingAllow = new Dictionary<ThingFilter, HashSet<ThingDef>>();

        private static Dictionary<ApparelPolicy, HashSet<ThingDef>> _policyToDisallowedDefs = new Dictionary<ApparelPolicy, HashSet<ThingDef>>();

        public static void SetAllowDef_Prefix(ThingFilter __instance, ThingDef thingDef, out bool __state)
            => __state = __instance.Allows(thingDef);

        public static void SetAllowDef_Postfix(ThingFilter __instance, ThingDef thingDef, bool __state)
        {
            if (!_batching || !IsWeaponDef(thingDef)) return;

            bool isNowAllowed = __instance.Allows(thingDef);

            if (__state && !isNowAllowed)
            {
                if (!_pendingDisallow.TryGetValue(__instance, out var set))
                    _pendingDisallow[__instance] = set = new HashSet<ThingDef>();
                set.Add(thingDef);
            }
            else if (!__state && isNowAllowed)
            {
                if (!_pendingAllow.TryGetValue(__instance, out var set))
                    _pendingAllow[__instance] = set = new HashSet<ThingDef>();
                set.Add(thingDef);
            }
        }

        private static void ApplyPending()
        {
            if (_pendingDisallow.Count == 0) return;
            int touchedPawns = 0, droppedEq = 0, droppedInv = 0;

            _policyToDisallowedDefs.Clear();
            foreach (var kv in _pendingDisallow)
                if (_filterToPolicy.TryGetValue(kv.Key, out var policy))
                    if (!_policyToDisallowedDefs.TryGetValue(policy, out var set)) _policyToDisallowedDefs[policy] = kv.Value; else set.UnionWith(kv.Value);
            if (_policyToDisallowedDefs.Count == 0) return;

            var pawnsByPolicy = Find.Maps.SelectMany(m => m.mapPawns.FreeColonistsAndPrisonersSpawned)
                .Where(p => p.outfits?.CurrentApparelPolicy != null)
                .GroupBy(p => p.outfits.CurrentApparelPolicy)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var policyDefsPair in _policyToDisallowedDefs)
            {
                var policy = policyDefsPair.Key;
                var disallowed = policyDefsPair.Value;

                if (!pawnsByPolicy.TryGetValue(policy, out var pawns)) continue;

                foreach (var p in pawns)
                {
                    if (p.Drafted || p.InMentalState || p.Downed || ValidationHelper.IsInRitual(p) || global::AutoArm.Jobs.Jobs.IsHaulingJob(p) || global::AutoArm.Jobs.Jobs.IsTemporary(p)) continue;

                    bool anyChange = false;
                    var eq = p.equipment?.Primary;
                    if (eq != null && disallowed.Contains(eq.def) && !ForcedWeapons.IsForced(p, eq))
                    {
                        DroppedItemTracker.MarkAsDropped(eq, 1200);
                        if (SimpleSidearmsCompat.IsLoaded) SimpleSidearmsCompat.InformOfDroppedWeapon(p, eq);
                        if (p.equipment.TryDropEquipment(eq, out var droppedWeapon, p.Position, false))
                        {
                            droppedEq++;
                            anyChange = true;
                            if (droppedWeapon != null)
                            {
                                var haulJob = Verse.AI.HaulAIUtility.HaulToStorageJob(p, droppedWeapon, false);
                                if (haulJob != null) p.jobs.TryTakeOrderedJob(haulJob, Verse.AI.JobTag.Misc, true);
                            }
                        }
                    }

                    var inv = p.inventory?.innerContainer;
                    if (inv != null && inv.Any)
                    {
                        List<Thing> itemsToDrop = null;
                        foreach (var t in inv.InnerListForReading)
                            if (t is ThingWithComps twc && IsWeaponDef(twc.def) && disallowed.Contains(twc.def) && !ForcedWeapons.IsForced(p, twc))
                                (itemsToDrop ?? (itemsToDrop = new List<Thing>())).Add(t);

                        if (itemsToDrop != null)
                        {
                            foreach (var t in itemsToDrop)
                            {
                                DroppedItemTracker.MarkAsDropped(t, 1800);
                                if (SimpleSidearmsCompat.IsLoaded) SimpleSidearmsCompat.InformOfDroppedWeapon(p, t as ThingWithComps);
                                if (inv.TryDrop(t, p.Position, p.Map, ThingPlaceMode.Near, out _))
                                {
                                    droppedInv++;
                                    anyChange = true;
                                }
                            }
                        }
                    }
                    if (anyChange) touchedPawns++;
                }
            }
            AutoArmLogger.Debug(() => $"WeaponPolicyBatcher: applied — pawns={touchedPawns}, droppedEq={droppedEq}, droppedInv={droppedInv}");
        }

        private static bool IsWeaponDef(ThingDef def)
        {
            if (def == null) return false;
            try { return WeaponValidation.AllWeapons.Contains(def); } catch { return def.IsWeapon; }
        }

        public static void RegisterFilterPolicy(ThingFilter filter, ApparelPolicy policy)
        {
            if (!_batching || filter == null || policy == null) return;
            if (!_filterToPolicy.ContainsKey(filter)) _filterToPolicy.Add(filter, policy);
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) => obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}

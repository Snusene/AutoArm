// AutoArm — ApparelWeaponsIntegration (rev3)
// RimWorld 1.5 & 1.6 (C# 7.3 / .NET 4.7.2)
// -----------------------------------------------------------------------------
// Fixes:
//  • Patch base Window.Close(Boolean) instead of Dialog_ManageApparelPolicies.Close
//    (RimWorld 1.6 throws when patching non-implemented overload). We now check
//    __instance is Dialog_ManageApparelPolicies before running Pop().
//  • Apply UI patches BEFORE patching Close so a failure there can't block them.
//  • Keep previous behavior: re-parent Weapons under Apparel on dialog open,
//    force node DB rebuild, hide the Apparel header but keep sliders/toggles
//    by calling DoSpecialFilters + DoCategoryChildren, with parentFilter bypass.
//
// Logs prefix: [AutoArmDbg] AWI r3:
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Reflection;
using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoArm.UI
{
    [StaticConstructorOnStartup]
    public static class ApparelWeaponsIntegration
    {
        // Public surface for legacy calls
        public static void MoveWeaponsForUI()
        { Push(); }

        public static void RestoreWeaponsPosition()
        { Pop(); }

        public static void Push()
        {
            _depth++;
            if (_depth == 1) TryReparentAndRebuild(true);
        }

        public static void Pop()
        {
            if (_depth <= 0) { _depth = 0; return; }
            _depth--;
            if (_depth == 0) TryReparentAndRebuild(false);
        }

        private static int _depth;
        private static ThingCategoryDef _weapons;
        private static ThingCategoryDef _apparel;
        private static ThingCategoryDef _apparelMisc;
        private static ThingCategoryDef _origParent;
        private static int _origIndex = -1;

        // Cached UI members
        private static MethodInfo _doSpec16;

        private static MethodInfo _doChildren16;
        private static FieldInfo _parentFilterField;

        static ApparelWeaponsIntegration()
        {
            var h = new Harmony("AutoArm.ApparelWeaponsIntegration");

            try
            {
                _weapons = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (_weapons == null) _weapons = ThingCategoryDefOf.Weapons;

                _apparel = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");
                if (_apparel == null) _apparel = ThingCategoryDefOf.Apparel;

                _apparelMisc = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("ApparelMisc");
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("[AutoArmDbg] AWI r3: def init error", ex);
            }

            int patchedUI = 0;
            try
            {
                // Patch UI FIRST so later errors don't block it.
                var doCat16 = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategory",
                    new Type[] { typeof(TreeNode_ThingCategory), typeof(int), typeof(int), typeof(Map), typeof(bool) });
                if (doCat16 != null)
                {
                    h.Patch(doCat16, prefix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(Prefix16)));
                    patchedUI++;
                }

                var doCat15 = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategory",
                    new Type[] { typeof(ThingCategoryDef), typeof(int), typeof(int) });
                if (doCat15 != null)
                {
                    h.Patch(doCat15, prefix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(Prefix15)));
                    patchedUI++;
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("[AutoArmDbg] AWI r3: UI patch error", ex);
            }

            // Dialog ctor
            try
            {
                var ctor = AccessTools.Constructor(typeof(Dialog_ManageApparelPolicies), new Type[0]);
                if (ctor != null)
                    h.Patch(ctor, postfix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(OnDialogCtor)));
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("[AutoArmDbg] AWI r3: ctor patch error", ex);
            }

            // Patch base Window.Close(bool) and filter by instance type at runtime
            try
            {
                var closeBase = AccessTools.Method(typeof(Window), "Close", new Type[] { typeof(bool) });
                if (closeBase != null)
                    h.Patch(closeBase, prefix: new HarmonyMethod(typeof(ApparelWeaponsIntegration), nameof(OnAnyWindowClose)));
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("[AutoArmDbg] AWI r3: close patch error", ex);
            }

            AutoArmLogger.Debug($"[AutoArmDbg] AWI r3: patched (UI={patchedUI}, ctor=1, close=1)");
        }

        // ---------------- Dialog hooks ----------------
        public static void OnDialogCtor(Dialog_ManageApparelPolicies __instance)
        { Push(); }

        // Runs for every Window.Close(bool). Only act for Apparel dialog.
        public static void OnAnyWindowClose(Window __instance, ref bool doCloseSound)
        {
            if (__instance is Dialog_ManageApparelPolicies) Pop();
        }

        // ---------------- Reparent + rebuild ----------------
        private static void TryReparentAndRebuild(bool moveUnderApparel)
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
                        _origIndex = (_origParent != null && _origParent.childCategories != null)
                                   ? _origParent.childCategories.IndexOf(_weapons) : -1;
                        AutoArmLogger.Debug("[AutoArmDbg] AWI r3: captured original parent=" +
                                    (_origParent != null ? _origParent.defName : "null") + " index=" + _origIndex);
                    }

                    if (_weapons.parent != null && _weapons.parent.childCategories != null)
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

                    AutoArmLogger.Debug($"[AutoArmDbg] AWI r3: moved Weapons under Apparel at index {insertAt}");
                }
                else
                {
                    if (_apparel != null && _apparel.childCategories != null)
                        _apparel.childCategories.Remove(_weapons);

                    if (_origParent != null)
                    {
                        if (_origParent.childCategories == null)
                            _origParent.childCategories = new List<ThingCategoryDef>();

                        if (_origIndex >= 0 && _origIndex <= _origParent.childCategories.Count)
                            _origParent.childCategories.Insert(_origIndex, _weapons);
                        else if (!_origParent.childCategories.Contains(_weapons))
                            _origParent.childCategories.Add(_weapons);

                        _weapons.parent = _origParent;
                        AutoArmLogger.Debug("[AutoArmDbg] AWI r3: restored Weapons to " + _origParent.defName +
                                    " at index " + _origIndex);
                    }

                    _origParent = null;
                    _origIndex = -1;
                }

                ForceRebuildCategoryNodes();
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("[AutoArmDbg] AWI r3: reparent error", ex);
            }
        }

        private static void ForceRebuildCategoryNodes()
        {
            try
            {
                Type dbType = AccessTools.TypeByName("Verse.ThingCategoryNodeDatabase");
                if (dbType == null) dbType = AccessTools.TypeByName("RimWorld.ThingCategoryNodeDatabase");
                if (dbType == null) { AutoArmLogger.Debug("[AutoArmDbg] AWI r3: NodeDB type not found"); return; }

                MethodInfo rebuild = AccessTools.Method(dbType, "Rebuild");
                MethodInfo regenerate = AccessTools.Method(dbType, "Regenerate");
                MethodInfo reset = AccessTools.Method(dbType, "Reset");

                if (rebuild != null) { rebuild.Invoke(null, null); AutoArmLogger.Debug("[AutoArmDbg] AWI r3: NodeDB.Rebuild()"); return; }
                if (regenerate != null) { regenerate.Invoke(null, null); AutoArmLogger.Debug("[AutoArmDbg] AWI r3: NodeDB.Regenerate()"); return; }
                if (reset != null) { reset.Invoke(null, null); AutoArmLogger.Debug("[AutoArmDbg] AWI r3: NodeDB.Reset()"); return; }

                FieldInfo rootField = AccessTools.Field(dbType, "rootNode");
                if (rootField != null) { rootField.SetValue(null, null); AutoArmLogger.Debug("[AutoArmDbg] AWI r3: NodeDB.rootNode cleared"); }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("[AutoArmDbg] AWI r3: NodeDB rebuild error", ex);
            }
        }

        // ---------------- UI (1.6 / node) ----------------
        public static bool Prefix16(Listing_TreeThingFilter __instance, TreeNode_ThingCategory node, int indentLevel, int openMask, Map map, bool subtreeMatchedSearch)
        {
            try
            {
                var cat = node != null ? node.catDef : null;
                if (!IsApparel(cat)) return true;

                Cache16();

                // Draw vanilla "starred" filters and the hp/quality bars
                try { _doSpec16?.Invoke(__instance, new object[] { node, indentLevel, map }); } catch { }

                // Temporarily disable parent gating so Weapons renders as a child
                ThingFilter saved = null;
                if (_parentFilterField != null)
                {
                    try { saved = _parentFilterField.GetValue(__instance) as ThingFilter; } catch { }
                    try { _parentFilterField.SetValue(__instance, null); } catch { }
                }

                // Draw children (Headgear/Armor/Utility/Noble/Misc/Weapons)
                _doChildren16?.Invoke(__instance, new object[] { node, indentLevel, openMask, map, subtreeMatchedSearch });

                // Restore gating
                if (_parentFilterField != null)
                {
                    try { _parentFilterField.SetValue(__instance, saved); } catch { }
                }

                // Hide only the "Apparel" header row
                return false;
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("[AutoArmDbg] AWI r3: Prefix16 error", ex);
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
                _parentFilterField = AccessTools.Field(typeof(Listing_TreeThingFilter), "parentFilter");
                if (_parentFilterField == null)
                {
                    var fs = typeof(Listing_TreeThingFilter).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < fs.Length; i++)
                        if (fs[i].FieldType == typeof(ThingFilter)) { _parentFilterField = fs[i]; break; }
                }
            }
        }

        private static bool IsApparel(ThingCategoryDef cat)
        {
            if (cat == null) return false;
            if (cat == ThingCategoryDefOf.Apparel) return true;
            return cat.defName != null && string.Equals(cat.defName, "Apparel", StringComparison.OrdinalIgnoreCase);
        }

        // ---------------- UI (1.5 / def) ----------------
        public static bool Prefix15(Listing_TreeThingFilter __instance, ThingCategoryDef cat, int nestLevel, int openMask)
        {
            // In 1.5+ the node path is usually the one in use; safest is to let 1.5 def path run unchanged.
            return true;
        }
    }
}
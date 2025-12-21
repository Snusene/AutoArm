
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Testing;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using ForcedWeapons = AutoArm.Helpers.ForcedWeapons;

namespace AutoArm
{
    /// <summary>
    /// Add "forced" label
    /// </summary>
    [HarmonyPatch(typeof(Thing), "Label", MethodType.Getter)]
    [HarmonyPatch(typeof(Thing), "LabelNoCount", MethodType.Getter)]
    [HarmonyPatch(typeof(Thing), "LabelCap", MethodType.Getter)]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    public static class Thing_LabelPatches
    {
        private static Dictionary<int, (string label, int tick)> cache = new Dictionary<int, (string, int)>();

        private static int cachedTickForQuickCheck = -1;

        private static bool quickCheckResult = false;

        [HarmonyPrefix]
        public static bool Prefix(Thing __instance, ref string __result)
        {
            // Skip non-weapons
            if (!__instance.def.IsWeapon)
                return true;

            // Tick-cached check
            var tick = Find.TickManager?.TicksGame ?? -1;
            if (cachedTickForQuickCheck != tick)
            {
                cachedTickForQuickCheck = tick;
                quickCheckResult = ForcedWeaponLabelHelper.ShouldProcessWeaponLabel();
            }

            if (!quickCheckResult)
                return true;

            if (!ForcedWeaponLabelHelper.IsWeaponOwnedBySelectedPawn(__instance.thingIDNumber))
                return true;

            if (cache.TryGetValue(__instance.thingIDNumber, out var cached) && cached.tick == tick)
            {
                __result = cached.label;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            if (!quickCheckResult)
                return;

            ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            cache[__instance.thingIDNumber] = (__result, currentTick);
        }

        /// <summary>
        /// Periodic cleanup of stale label cache entries
        /// Cleanup helper
        /// </summary>
        public static int CleanupLabelCache()
        {
            if (cache.Count == 0)
                return 0;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int removedCount = 0;

            var toRemove = ListPool<int>.Get(cache.Count / 4);
            foreach (var kvp in cache)
            {
                if (currentTick - kvp.Value.tick > 60)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                cache.Remove(key);
                removedCount++;
            }

            ListPool<int>.Return(toRemove);
            return removedCount;
        }
    }

    internal static class ForcedWeaponLabelHelper
    {
        private static int lastUICheckTick = -1;

        private static bool cachedIsGearTabOpen = false;
        private static Pawn cachedSelectedPawn = null;

        private static HashSet<int> cachedPawnWeaponIds = new HashSet<int>();

        private static FieldInfo inspectTabField = null;

        private static FieldInfo openTabTypeField = null;
        private static bool fieldSearchDone = false;

        /// <summary>
        /// Reset field detection - useful when reloading the mod
        /// </summary>
        public static void ResetFieldChecking()
        {
            fieldSearchDone = false;
            inspectTabField = null;
            openTabTypeField = null;
            cachedPawnWeaponIds.Clear();
        }


        private static void BuildWeaponIds(Pawn pawn)
        {
            cachedPawnWeaponIds.Clear();

            if (pawn.equipment?.Primary != null)
            {
                cachedPawnWeaponIds.Add(pawn.equipment.Primary.thingIDNumber);
            }

            if (pawn.inventory?.innerContainer != null)
            {
                foreach (var item in pawn.inventory.innerContainer)
                {
                    if (item.def.IsWeapon)
                    {
                        cachedPawnWeaponIds.Add(item.thingIDNumber);
                    }
                }
            }
        }

        /// <summary>
        /// Public accessor to check if a weapon belongs to the selected pawn.
        /// Encapsulates the internal cache for proper separation of concerns.
        /// </summary>
        public static bool IsWeaponOwnedBySelectedPawn(int weaponId)
        {
            return cachedPawnWeaponIds.Contains(weaponId);
        }


        internal static bool ShouldProcessWeaponLabel()
        {
            var tickManager = Find.TickManager;
            if (tickManager == null)
                return false;

            int currentTick = tickManager.TicksGame;

            if (lastUICheckTick == currentTick)
                return cachedIsGearTabOpen;

            lastUICheckTick = currentTick;

            cachedIsGearTabOpen = false;
            cachedSelectedPawn = null;

            if (AutoArmMod.settings?.modEnabled != true || AutoArmMod.settings?.showForcedLabels != true)
            {
                cachedPawnWeaponIds.Clear();
                return false;
            }

            if (Find.CurrentMap == null ||
                Find.Selector == null ||
                Find.MainTabsRoot == null ||
                Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect)
            {
                cachedPawnWeaponIds.Clear();
                return false;
            }

            Pawn selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null || !ValidationHelper.SafeIsColonist(selectedPawn))
            {
                cachedPawnWeaponIds.Clear();
                return false;
            }

            cachedIsGearTabOpen = true;
            cachedSelectedPawn = selectedPawn;
            BuildWeaponIds(selectedPawn);

            var inspectPane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            if (inspectPane == null)
                return cachedIsGearTabOpen;

            if (!fieldSearchDone)
            {
                inspectTabField = AccessTools.GetDeclaredFields(typeof(MainTabWindow_Inspect))
                    .FirstOrDefault(f => typeof(ITab).IsAssignableFrom(f.FieldType));

                if (inspectTabField == null)
                {
                    openTabTypeField = AccessTools.Field(typeof(MainTabWindow_Inspect), "openTabType");
                    if (openTabTypeField != null && openTabTypeField.FieldType != typeof(Type))
                    {
                        openTabTypeField = null;
                    }

                    if (openTabTypeField == null)
                    {
                        AutoArmLogger.Error("Auto-detection of inspect tab field failed! Could not find ITab field or openTabType field.");

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            var allFields = AccessTools.GetDeclaredFields(typeof(MainTabWindow_Inspect));
                            AutoArmLogger.Debug(() => $"Available fields in MainTabWindow_Inspect: {string.Join(", ", allFields.Select(f => $"{f.FieldType.Name} {f.Name}"))}");
                        }
                    }
                    else
                    {
                        AutoArmLogger.Debug(() => $"Successfully found and cached openTabType field (newer RimWorld version)");
                    }
                }
                else
                {
                    AutoArmLogger.Debug(() => $"Successfully found and cached inspect tab field: '{inspectTabField.Name}' (Type: {inspectTabField.FieldType.Name})");
                }

                fieldSearchDone = true;
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                if (inspectTabField != null)
                {
                    if (currentTick % 60 == 0)
                    {
                        var openTab = (ITab)inspectTabField.GetValue(inspectPane);
                        AutoArmLogger.Debug(() => $"Using cached field '{inspectTabField.Name}'. Open tab type: {openTab?.GetType()?.Name ?? "null"}, UI active: {cachedIsGearTabOpen}");
                    }
                }
                else if (openTabTypeField != null)
                {
                    if (currentTick % 6000 == 0)
                    {
                        var openTabType = (Type)openTabTypeField.GetValue(inspectPane);
                        AutoArmLogger.Debug(() => $"Using openTabType field. Open tab type: {openTabType?.Name ?? "null"}, UI active: {cachedIsGearTabOpen}");
                    }
                }
            }

            return cachedIsGearTabOpen;
        }

        private static int lastForcedWeaponCheckTick = -1;

        private static Pawn lastForcedWeaponCheckPawn = null;
        private static HashSet<int> cachedForcedWeaponIds = new HashSet<int>();

        private static Dictionary<int, string> labelCache = new Dictionary<int, string>();

        private const int MaxLabelCacheSize = 100;

        private const string ForcedSuffix = ", forced";


        internal static void AddForcedText(Thing thing, ref string label)
        {
            if (thing == null || !thing.def.IsWeapon || !(thing is ThingWithComps weapon))
                return;

            if (label.EndsWith(ForcedSuffix, StringComparison.Ordinal))
                return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (cachedSelectedPawn == null)
                return;

            bool shouldRebuildCache = lastForcedWeaponCheckPawn != cachedSelectedPawn ||
                                      lastForcedWeaponCheckTick != currentTick;

            if (shouldRebuildCache)
            {
                lastForcedWeaponCheckPawn = cachedSelectedPawn;
                lastForcedWeaponCheckTick = currentTick;
                BuildCache(cachedSelectedPawn);
            }

            bool isForced = false;
            if (cachedForcedWeaponIds.Contains(weapon.thingIDNumber))
            {
                if (weapon == cachedSelectedPawn.equipment?.Primary)
                {
                    isForced = true;
                }
                else if (cachedSelectedPawn.inventory?.innerContainer != null &&
                         cachedSelectedPawn.inventory.innerContainer.Contains(weapon))
                {
                    isForced = true;
                }
            }

            if (isForced)
            {
                label = label + ForcedSuffix;
            }
        }

        private static void BuildCache(Pawn pawn)
        {
            cachedForcedWeaponIds.Clear();


            if (pawn.equipment?.Primary != null)
            {
                var primary = pawn.equipment.Primary;
                if (ForcedWeapons.IsForced(pawn, primary))
                {
                    cachedForcedWeaponIds.Add(primary.thingIDNumber);
                }
            }

            if (pawn.inventory?.innerContainer != null)
            {
                foreach (var item in pawn.inventory.innerContainer)
                {
                    if (item is ThingWithComps invWeapon && invWeapon.def.IsWeapon)
                    {
                        if (ForcedWeapons.IsForced(pawn, invWeapon))
                        {
                            cachedForcedWeaponIds.Add(invWeapon.thingIDNumber);
                        }
                    }
                }
            }
        }

        private static void ClearCaches()
        {
            labelCache.Clear();
            cachedForcedWeaponIds.Clear();
            cachedPawnWeaponIds.Clear();
            lastForcedWeaponCheckPawn = null;
        }

        public static void CleanupDeadPawnCaches()
        {
            if (lastForcedWeaponCheckPawn != null &&
                (lastForcedWeaponCheckPawn.Dead || lastForcedWeaponCheckPawn.Destroyed))
            {
                ClearCaches();
            }
        }

        /// <summary>
        /// Event-driven removal when pawn dies/destroyed
        /// </summary>
        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;
            if (lastForcedWeaponCheckPawn == pawn)
                ClearCaches();
            if (cachedSelectedPawn == pawn)
            {
                cachedSelectedPawn = null;
                cachedPawnWeaponIds.Clear();
            }
        }

        private static bool CheckPawnHasForcedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            return ForcedWeapons.IsForced(pawn, weapon);
        }
    }

    /// <summary>
    /// Hide weapons category
    /// </summary>
    [HarmonyPatch(typeof(ThingCategoryDef), "DescendantThingDefs", MethodType.Getter)]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    public static class ThingCategoryDef_DescendantThingDefs_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingDef> __result)
        {
            if (TestRunner.IsRunningTests)
                return;

            if (AutoArmMod.settings?.modEnabled != true &&
                __instance?.defName == "Weapons" &&
                __instance?.parent?.defName == "Apparel")
            {
                __result = Enumerable.Empty<ThingDef>();
            }
        }
    }

    /// <summary>
    /// Hide child categories
    /// </summary>
    [HarmonyPatch(typeof(ThingCategoryDef), "ThisAndChildCategoryDefs", MethodType.Getter)]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    public static class ThingCategoryDef_ThisAndChildCategoryDefs_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingCategoryDef> __result)
        {
            if (TestRunner.IsRunningTests)
                return;

            if (AutoArmMod.settings?.modEnabled != true && __instance?.defName == "Apparel")
            {
                __result = __result.Where(cat => cat?.defName != "Weapons");
            }
        }
    }
}

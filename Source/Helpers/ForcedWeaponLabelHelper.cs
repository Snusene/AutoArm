
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using ForcedWeapons = AutoArm.ForcedWeapons;

namespace AutoArm.Helpers
{
    internal static class ForcedWeaponLabelHelper
    {
        private static int lastUICheckTick = -1;

        private static bool cachedIsGearTabOpen = false;
        private static Pawn cachedSelectedPawn = null;

        private static HashSet<int> cachedPawnWeaponIds = new HashSet<int>();

        private static FieldInfo inspectTabField = null;

        private static FieldInfo openTabTypeField = null;
        private static bool fieldSearchDone = false;

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
                        AutoArmLogger.Warn("Auto-detection of inspect tab field failed! Could not find ITab field or openTabType field.");

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            var allFields = AccessTools.GetDeclaredFields(typeof(MainTabWindow_Inspect));
                            AutoArmLogger.Debug(() => $"Available fields in MainTabWindow_Inspect: {string.Join(", ", allFields.Select(f => $"{f.FieldType.Name} {f.Name}"))}");
                        }
                    }
                    else
                    {
                        AutoArmLogger.Debug(() => $"Cached openTabType field (newer RimWorld)");
                    }
                }
                else
                {
                    AutoArmLogger.Debug(() => $"Cached inspect tab field '{inspectTabField.Name}' ({inspectTabField.FieldType.Name})");
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

        private static int lastForcedWeaponCheckPawnId = -1;
        private static HashSet<int> cachedForcedWeaponIds = new HashSet<int>();

        private static string _cachedForcedSuffix;
        private static string ForcedSuffix
            => _cachedForcedSuffix ?? (_cachedForcedSuffix = ", " + "ApparelForcedLower".Translate());

        public static string StripForcedSuffix(string label)
        {
            if (string.IsNullOrEmpty(label))
                return label;
            return label.EndsWith(ForcedSuffix, StringComparison.Ordinal)
                ? label.Substring(0, label.Length - ForcedSuffix.Length)
                : label;
        }


        internal static void AddForcedText(Thing thing, ref string label)
        {
            if (thing == null || !thing.def.IsWeapon || !(thing is ThingWithComps weapon))
                return;

            if (label.EndsWith(ForcedSuffix, StringComparison.Ordinal))
                return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (cachedSelectedPawn == null)
                return;

            int selectedId = cachedSelectedPawn.thingIDNumber;
            bool shouldRebuildCache = lastForcedWeaponCheckPawnId != selectedId ||
                                      lastForcedWeaponCheckTick != currentTick;

            if (shouldRebuildCache)
            {
                lastForcedWeaponCheckPawnId = selectedId;
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
            cachedForcedWeaponIds.Clear();
            cachedPawnWeaponIds.Clear();
            lastForcedWeaponCheckPawnId = -1;
        }

        public static void CleanupDeadPawnCaches()
        {
            if (cachedSelectedPawn != null &&
                (cachedSelectedPawn.Dead || cachedSelectedPawn.Destroyed || !cachedSelectedPawn.Spawned))
            {
                cachedSelectedPawn = null;
                ClearCaches();
            }
        }

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;
            if (lastForcedWeaponCheckPawnId == pawn.thingIDNumber)
                ClearCaches();
            if (cachedSelectedPawn == pawn)
            {
                cachedSelectedPawn = null;
                cachedPawnWeaponIds.Clear();
            }
        }

    }
}

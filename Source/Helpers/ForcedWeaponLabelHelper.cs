
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using ForcedWeapons = AutoArm.ForcedWeapons;

namespace AutoArm.Helpers
{
    internal static class ForcedWeaponLabelHelper
    {
        private static int lastUICheckFrame = -1;

        private static bool cachedIsGearTabOpen = false;
        private static Pawn cachedSelectedPawn = null;

        private static HashSet<int> cachedPawnWeaponIds = new HashSet<int>();

        internal static void ResetFrame()
        {
            lastUICheckFrame = -1;
            lastForcedWeaponCheckFrame = -1;
        }

        public static void ResetFieldChecking()
        {
            cachedPawnWeaponIds.Clear();
            _cachedForcedSuffix = null;
            _cachedSuffixLanguage = null;
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
            if (Find.TickManager == null)
                return false;

            int currentFrame = RealTime.frameCount;

            if (lastUICheckFrame == currentFrame)
                return cachedIsGearTabOpen;

            lastUICheckFrame = currentFrame;

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

            return cachedIsGearTabOpen;
        }

        private static int lastForcedWeaponCheckFrame = -1;

        private static int lastForcedWeaponCheckPawnId = -1;
        private static HashSet<int> cachedForcedWeaponIds = new HashSet<int>();

        private static string _cachedForcedSuffix;
        private static LoadedLanguage _cachedSuffixLanguage;
        private static string ForcedSuffix
        {
            get
            {
                var lang = LanguageDatabase.activeLanguage;
                if (_cachedForcedSuffix == null || !ReferenceEquals(_cachedSuffixLanguage, lang))
                {
                    _cachedSuffixLanguage = lang;
                    _cachedForcedSuffix = ", " + "ApparelForcedLower".Translate();
                }
                return _cachedForcedSuffix;
            }
        }

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

            if (cachedSelectedPawn == null)
                return;

            int currentFrame = RealTime.frameCount;
            int selectedId = cachedSelectedPawn.thingIDNumber;
            bool shouldRebuildCache = lastForcedWeaponCheckPawnId != selectedId ||
                                      lastForcedWeaponCheckFrame != currentFrame;

            if (shouldRebuildCache)
            {
                lastForcedWeaponCheckPawnId = selectedId;
                lastForcedWeaponCheckFrame = currentFrame;
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
    }
}

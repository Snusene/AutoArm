
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Patches
{
    internal static class ConditionalPatcher
    {
        private static Harmony harmony;
        private static HashSet<string> enabledCategories = new HashSet<string>();
        private static Dictionary<string, bool> modPresenceCache = new Dictionary<string, bool>();

        public static void Initialize(Harmony harmonyInstance)
        {
            harmony = harmonyInstance;

            CacheModPresence();

            ApplyConditionalPatches();
        }


        private static void CacheModPresence()
        {
            modPresenceCache["SimpleSidearms"] = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary") != null;
            modPresenceCache["CombatExtended"] = AccessTools.TypeByName("CombatExtended.CompAmmoUser") != null;
            modPresenceCache["PickUpAndHaul"] = DefDatabase<JobDef>.GetNamedSilentFail("HaulToInventory") != null;
            modPresenceCache["Infusion2"] = AccessTools.TypeByName("Infused.InfusionDef") != null
                                         || AccessTools.TypeByName("Infusion.InfusionDef") != null;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"Detected mods: " +
                    string.Join(", ", modPresenceCache.Where(kvp => kvp.Value).Select(kvp => kvp.Key)));
            }
        }


        private static void ApplyConditionalPatches()
        {
            EnableCategory(PatchCategories.Core);
            EnableCategory(PatchCategories.Performance);

            EnableCategory(PatchCategories.UI);

            if (modPresenceCache["SimpleSidearms"] ||
                modPresenceCache["CombatExtended"] ||
                modPresenceCache["PickUpAndHaul"] ||
                modPresenceCache["Infusion2"])
            {
                EnableCategory(PatchCategories.Compatibility);
            }

            AutoArmLogger.Debug(() => $"Patched categories: {string.Join(", ", enabledCategories)}");
        }

        public static void EnableCategory(string category)
        {
            if (enabledCategories.Add(category))
            {
                harmony?.PatchCategory(category);
            }
        }

        public static void DisableCategory(string category)
        {
            if (enabledCategories.Remove(category))
            {
                harmony?.UnpatchCategory(category);

                AutoArmLogger.Debug(() => $"Disabled patch category: {category}");
            }
        }

        public static void RefreshPatches()
        {
            if (harmony == null)
                return;

            CacheModPresence();

            var previousCategories = new HashSet<string>(enabledCategories);
            var newCategories = new HashSet<string>();

            newCategories.Add(PatchCategories.Core);
            newCategories.Add(PatchCategories.Performance);
            newCategories.Add(PatchCategories.UI);

            if (modPresenceCache["SimpleSidearms"] ||
                modPresenceCache["CombatExtended"] ||
                modPresenceCache["PickUpAndHaul"] ||
                modPresenceCache["Infusion2"])
            {
                newCategories.Add(PatchCategories.Compatibility);
            }

            foreach (var category in previousCategories)
            {
                if (!newCategories.Contains(category))
                {
                    DisableCategory(category);
                }
            }

            foreach (var category in newCategories)
            {
                if (!previousCategories.Contains(category))
                {
                    EnableCategory(category);
                }
            }
        }
    }
}

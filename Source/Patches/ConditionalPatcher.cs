
using HarmonyLib;
using System;
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
            modPresenceCache["ChildrenMods"] = HasChildrenMods();

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"Detected mods: " +
                    string.Join(", ", modPresenceCache.Where(kvp => kvp.Value).Select(kvp => kvp.Key)));
            }
        }


        private static bool HasChildrenMods()
        {
            try
            {
                foreach (var thingDef in DefDatabase<ThingDef>.AllDefs)
                {
                    var race = thingDef?.race;
                    if (race?.Humanlike != true)
                        continue;

                    var stages = race.lifeStageAges;
                    if (stages == null || stages.Count == 0)
                        continue;

                    foreach (var stage in stages)
                    {
                        if (stage?.def?.developmentalStage != null &&
                            stage.def.developmentalStage != DevelopmentalStage.Adult)
                        {
                            AutoArmLogger.Debug(() => $"Detected child life stage on {thingDef.label ?? thingDef.defName}: {stage.def.developmentalStage}");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                AutoArmLogger.Warn($"Error checking for children mods: {ex.Message}");
                return false;
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

            if (modPresenceCache["ChildrenMods"] || AutoArmMod.settings?.allowChildrenToEquipWeapons == true)
            {
                EnableCategory(PatchCategories.AgeRestrictions);
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

        public static bool IsCategoryEnabled(string category)
        {
            return enabledCategories.Contains(category);
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

            if (modPresenceCache["ChildrenMods"] || AutoArmMod.settings?.allowChildrenToEquipWeapons == true)
            {
                newCategories.Add(PatchCategories.AgeRestrictions);
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

            if (newCategories.Contains(PatchCategories.AgeRestrictions))
            {
                ChildWeapon.ApplyPatches(harmony);
            }
            else if (previousCategories.Contains(PatchCategories.AgeRestrictions))
            {
                ChildWeapon.UnpatchPatches(harmony);
            }
        }
    }
}


using AutoArm.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Patches
{
    /// <summary>
    /// Conditional patching
    /// </summary>
    public static class ConditionalPatcher
    {
        private static Harmony harmony;
        private static HashSet<string> enabledCategories = new HashSet<string>();
        private static Dictionary<string, bool> modPresenceCache = new Dictionary<string, bool>();

        /// <summary>
        /// Initialize the conditional patcher with the Harmony instance
        /// </summary>
        public static void Initialize(Harmony harmonyInstance)
        {
            harmony = harmonyInstance;

            CacheModPresence();

            ApplyConditionalPatches();
        }


        private static void CacheModPresence()
        {
            modPresenceCache["SimpleSidearms"] = ModLister.GetActiveModWithIdentifier("PeteTimesSix.SimpleSidearms") != null;
            modPresenceCache["CombatExtended"] = ModLister.GetActiveModWithIdentifier("CETeam.CombatExtended") != null;
            modPresenceCache["PickUpAndHaul"] = ModLister.GetActiveModWithIdentifier("Mehni.PickUpAndHaul") != null;
            modPresenceCache["Infusion2"] = ModLister.GetActiveModWithIdentifier("notfood.InfusionTwo") != null ||
                                            ModLister.GetActiveModWithIdentifier("notfood.Infusion2") != null;
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
                            AutoArmLogger.Debug(() => $"Detected child life stage: {thingDef.label ?? thingDef.defName} has {stage.def.label ?? stage.def.defName} ({stage.def.developmentalStage})");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error($"Error checking for children mods: {ex.Message}");
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
        }

        /// <summary>
        /// Enable a patch category
        /// </summary>
        public static void EnableCategory(string category)
        {
            if (enabledCategories.Add(category))
            {
                harmony?.PatchCategory(category);

                AutoArmLogger.Debug(() => $"Patched category: {category}");
            }
        }

        /// <summary>
        /// Disable a patch category (for debugging)
        /// </summary>
        public static void DisableCategory(string category)
        {
            if (enabledCategories.Remove(category))
            {
                harmony?.UnpatchCategory(category);

                AutoArmLogger.Debug(() => $"Disabled patch category: {category}");
            }
        }

        /// <summary>
        /// Category enabled
        /// </summary>
        public static bool IsCategoryEnabled(string category)
        {
            return enabledCategories.Contains(category);
        }

        /// <summary>
        /// Re-evaluate conditions and update patch categories
        /// Settings changed
        /// </summary>
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
        }
    }
}

using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    public class AutoArmNewGameDefaultsComponent : GameComponent
    {
        private const string SlaveOutfitLabel = "Slave";
        private const string AnythingOutfitLabel = "Anything";
        private const string EverythingOutfitLabel = "Everything";
        private const string NudistOutfitToken1 = "nudist";
        private const string NudistOutfitToken2 = "nude";
        private const string WeaponsCategoryDefName = "Weapons";

        private bool applied;

        public AutoArmNewGameDefaultsComponent(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref applied, "AutoArm_NewGameDefaultsApplied", false);
        }

        public override void StartedNewGame()
        {
            LongEventHandler.ExecuteWhenFinished(() => TryApplyDefaults());
        }

        private void TryApplyDefaults()
        {
            if (applied) return;

            var policies = GetAllPoliciesViaReflection();
            if (policies == null || policies.Count == 0) return;

            var weaponsRoot = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(WeaponsCategoryDefName) ?? ThingCategoryDefOf.Weapons;
            if (weaponsRoot == null) return;

            var allWeaponDefs = new List<ThingDef>();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def != null && def.IsWithinCategory(weaponsRoot))
                    allWeaponDefs.Add(def);
            }

            int outfitsModified = 0;
            int nudistOutfits = 0;
            int slaveOutfits = 0;

            foreach (var policyObj in policies)
            {
                var filter = GetFilter(policyObj);
                if (filter == null) continue;

                string label = GetLabel(policyObj) ?? string.Empty;
                bool isSlave = label.Equals(SlaveOutfitLabel, StringComparison.OrdinalIgnoreCase);
                bool isNudist = label.IndexOf(NudistOutfitToken1, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               label.IndexOf(NudistOutfitToken2, StringComparison.OrdinalIgnoreCase) >= 0;

                if (isNudist)
                {
                    nudistOutfits++;
                    continue;
                }

                if (isSlave)
                {
                    slaveOutfits++;
                    SetAllowOnTree(filter, weaponsRoot, allow: false);
                    for (int i = 0; i < allWeaponDefs.Count; i++)
                    {
                        filter.SetAllow(allWeaponDefs[i], false);
                    }
                }
                else
                {
                    outfitsModified++;
                    SetAllowOnTree(filter, weaponsRoot, allow: true);

                    bool isAnything = label.Equals(AnythingOutfitLabel, StringComparison.OrdinalIgnoreCase) ||
                                     label.Equals(EverythingOutfitLabel, StringComparison.OrdinalIgnoreCase);

                    int weaponsEnabled = 0;
                    for (int i = 0; i < allWeaponDefs.Count; i++)
                    {
                        var def = allWeaponDefs[i];
                        bool allow = isAnything || !IsPersona(def);
                        filter.SetAllow(def, allow);
                        if (allow) weaponsEnabled++;
                    }

                    AutoArmLogger.Debug(() => $"  - Outfit '{label}': enabled {weaponsEnabled}/{allWeaponDefs.Count} weapons");
                }

                var specials = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading;
                if (specials != null)
                {
                    for (int i = 0; i < specials.Count; i++)
                    {
                        var s = specials[i];
                        try
                        {
                            filter.SetAllow(s, true);
                        }
                        catch (Exception ex)
                        {
                            AutoArmLogger.Debug(() => $"[AutoArm] Suppressed exception while setting special filter '{s.defName}': {ex.Message}");
                        }
                    }
                }
            }

            applied = true;

            AutoArmLogger.Debug(() => $"Default outfits applied:");
            AutoArmLogger.Debug(() => $"  - Modified {outfitsModified} outfits");
            PreWarmColonistSkillCaches();
        }


        private void PreWarmColonistSkillCaches()
        {
            if (Find.Maps == null)
                return;

            int totalColonists = 0;
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonistsSpawned == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.skills?.skills != null)
                    {
                        Caching.WeaponCacheManager.PreWarmColonistScore(pawn, true);
                        Caching.WeaponCacheManager.PreWarmColonistScore(pawn, false);
                        totalColonists++;
                    }
                }
            }

            if (totalColonists > 0 && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"Pre-warmed skill caches for {totalColonists} colonists");
            }
        }

        private static List<object> GetAllPoliciesViaReflection()
        {
            var game = Current.Game;
            if (game == null) return new List<object>();

            var field = AccessTools.Field(game.GetType(), "apparelPolicyDatabase")
                     ?? AccessTools.Field(game.GetType(), "outfitDatabase");
            var db = field?.GetValue(game);
            if (db == null)
            {
                AutoArmLogger.Warn($"[AutoArm] Could not find 'apparelPolicyDatabase' or 'outfitDatabase' on Current.Game. New game defaults will not be applied.");
                return new List<object>();
            }

            var prop = AccessTools.Property(db.GetType(), "AllApparelPolicies")
                    ?? AccessTools.Property(db.GetType(), "AllOutfits");
            var enumerable = prop?.GetValue(db) as IEnumerable;
            if (enumerable == null)
            {
                AutoArmLogger.Warn($"[AutoArm] Could not find 'AllApparelPolicies' or 'AllOutfits' property on database object. New game defaults will not be applied.");
                return new List<object>();
            }

            var list = new List<object>();
            foreach (var o in enumerable) if (o != null) list.Add(o);
            return list;
        }

        private static ThingFilter GetFilter(object policyObj)
        {
            if (policyObj == null) return null;
            var t = policyObj.GetType();
            var p = AccessTools.Property(t, "filter");
            if (p != null) return p.GetValue(policyObj) as ThingFilter;
            var f = AccessTools.Field(t, "filter");
            if (f != null) return f.GetValue(policyObj) as ThingFilter;
            return null;
        }

        private static string GetLabel(object policyObj)
        {
            if (policyObj == null) return null;
            var t = policyObj.GetType();
            var p = AccessTools.Property(t, "label");
            if (p != null) return p.GetValue(policyObj) as string;
            var f = AccessTools.Field(t, "label");
            if (f != null) return f.GetValue(policyObj) as string;
            return null;
        }

        private static void SetAllowOnTree(ThingFilter filter, ThingCategoryDef root, bool allow)
        {
            if (root == null || filter == null) return;
            try { filter.SetAllow(root, allow); }
            catch (Exception ex)
            {
                AutoArmLogger.Debug(() => $"Suppressed exception while setting tree filter on '{root.defName}': {ex.Message}");
            }
            if (root.childCategories == null) return;
            for (int i = 0; i < root.childCategories.Count; i++)
            {
                var c = root.childCategories[i];
                if (c == null) continue;
                SetAllowOnTree(filter, c, allow);
            }
        }

        private static bool IsPersona(ThingDef def)
        {
            if (def?.comps == null) return false;
            for (int i = 0; i < def.comps.Count; i++)
            {
                var comp = def.comps[i];
                if (comp?.compClass == typeof(CompBladelinkWeapon)) return true;
            }
            return false;
        }
    }
}

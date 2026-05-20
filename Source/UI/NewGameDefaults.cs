using AutoArm.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    public sealed class AutoArmNewGameDefaultsComponent : GameComponent
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

        public override void LoadedGame()
        {
            if (!applied)
                applied = true;
        }

        private void TryApplyDefaults()
        {
            if (applied) return;

            var policies = new List<ApparelPolicy>();
            foreach (var p in AutoArm.UI.PolicyDBHelper.GetAllPolicies())
                if (p != null) policies.Add(p);
            if (policies.Count == 0) return;

            var weaponsRoot = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(WeaponsCategoryDefName) ?? ThingCategoryDefOf.Weapons;
            if (weaponsRoot == null) return;

            AutoArm.UI.WeaponPolicyBatcher.Begin();
            try
            {
                ApplyDefaultsInternal(policies, weaponsRoot);
            }
            finally
            {
                AutoArm.UI.WeaponPolicyBatcher.Apply();
            }
        }

        private void ApplyDefaultsInternal(List<ApparelPolicy> policies, ThingCategoryDef weaponsRoot)
        {

            var allWeaponDefs = new List<ThingDef>();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def != null && def.IsWithinCategory(weaponsRoot))
                    allWeaponDefs.Add(def);
            }

            int outfitsModified = 0;
            int nudistOutfits = 0;
            int slaveOutfits = 0;
            var outfitSummaries = ListPool<string>.Get();

            foreach (var policy in policies)
            {
                var filter = policy.filter;
                if (filter == null) continue;

                string label = policy.label ?? string.Empty;
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

                    outfitSummaries.Add($"'{label}' {weaponsEnabled}/{allWeaponDefs.Count}");
                }

                var specials = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading;
                if (specials != null)
                {
                    for (int i = 0; i < specials.Count; i++)
                    {
                        var s = specials[i];
                        if (!CategoryIsUnderRoot(s.parentCategory, weaponsRoot))
                            continue;
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

            string summary = outfitSummaries.Count > 0 ? string.Join(", ", outfitSummaries) : "none";
            AutoArmLogger.Debug(() => $"Applied default outfits: {outfitsModified} modified ({summary})");
            ListPool<string>.Return(outfitSummaries);
            PreWarmColonistSkillCaches();
        }


        private void PreWarmColonistSkillCaches()
        {
            var maps = Find.Maps;
            if (maps == null)
                return;

            int totalColonists = 0;
            var colonists = ListPool<Pawn>.Get();
            try
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    var map = maps[i];
                    var live = map?.mapPawns?.FreeColonistsSpawned;
                    if (live == null) continue;

                    colonists.Clear();
                    for (int j = 0; j < live.Count; j++)
                        colonists.Add(live[j]);

                    for (int j = 0; j < colonists.Count; j++)
                    {
                        var pawn = colonists[j];
                        if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.skills?.skills != null)
                        {
                            Caching.WeaponCache.PreWarmColonistScore(pawn, true);
                            Caching.WeaponCache.PreWarmColonistScore(pawn, false);
                            totalColonists++;
                        }
                    }
                }
            }
            finally
            {
                ListPool<Pawn>.Return(colonists);
            }

            if (totalColonists > 0 && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"Pre-warmed skill caches for {totalColonists} colonists");
            }
        }

        private static bool CategoryIsUnderRoot(ThingCategoryDef cat, ThingCategoryDef root)
        {
            if (cat == null || root == null) return false;
            for (var c = cat; c != null; c = c.parent)
                if (c == root) return true;
            return false;
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

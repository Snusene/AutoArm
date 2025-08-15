// AutoArm RimWorld 1.6+
// New-game only defaults: enable all weapons in every apparel policy,
// disable Persona weapons everywhere, and disable ALL weapons in the
// vanilla "Slave" apparel policy. Runs once per new game.
//
// This version avoids compile-time references to ApparelPolicyDatabase / Game.apparelPolicyDatabase
// and uses reflection so it works across builds that still use the older OutfitDatabase names.
//
// Place in your project. It is self-contained and safe to include alongside your other components.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoArm
{
    public class AutoArmNewGameDefaultsComponent : GameComponent
    {
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
            // Run after defs and the policy DB are ready.
            LongEventHandler.ExecuteWhenFinished(() => TryApplyDefaults());
        }

        private void TryApplyDefaults()
        {
            if (applied) return;
            var policies = GetAllPoliciesViaReflection();
            if (policies == null || policies.Count == 0) return;

            var weaponsRoot = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons") ?? ThingCategoryDefOf.Weapons;
            if (weaponsRoot == null) return;

            // Cache once
            var allWeaponDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d != null && d.IsWithinCategory(weaponsRoot))
                .ToList();

            foreach (var policyObj in policies)
            {
                var filter = GetFilter(policyObj);
                if (filter == null) continue;

                string label = GetLabel(policyObj) ?? string.Empty;
                bool isSlave = label.Equals("Slave", StringComparison.OrdinalIgnoreCase);

                // 1) Category-wide intent
                SetAllowOnTree(filter, weaponsRoot, allow: !isSlave);

                // 2) Per-def: persona OFF everywhere; slave OFF for everything
                for (int i = 0; i < allWeaponDefs.Count; i++)
                {
                    var def = allWeaponDefs[i];
                    filter.SetAllow(def, !isSlave && !IsPersona(def));
                }

                // 3) Starred special filters green (best effort)
                var specials = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading;
                if (specials != null)
                {
                    for (int i = 0; i < specials.Count; i++)
                    {
                        var s = specials[i];
                        try { filter.SetAllow(s, true); } catch { }
                    }
                }
            }

            applied = true;
        }

        // --- Reflection helpers (work on ApparelPolicyDatabase or legacy OutfitDatabase) ---
        private static List<object> GetAllPoliciesViaReflection()
        {
            var game = Current.Game;
            if (game == null) return new List<object>();

            // Try field "apparelPolicyDatabase", else legacy "outfitDatabase"
            var field = AccessTools.Field(game.GetType(), "apparelPolicyDatabase")
                     ?? AccessTools.Field(game.GetType(), "outfitDatabase");
            var db = field?.GetValue(game);
            if (db == null) return new List<object>();

            // Try property "AllApparelPolicies", else legacy "AllOutfits"
            var prop = AccessTools.Property(db.GetType(), "AllApparelPolicies")
                    ?? AccessTools.Property(db.GetType(), "AllOutfits");
            var enumerable = prop?.GetValue(db) as IEnumerable;
            if (enumerable == null) return new List<object>();

            var list = new List<object>();
            foreach (var o in enumerable) if (o != null) list.Add(o);
            return list;
        }

        private static ThingFilter GetFilter(object policyObj)
        {
            if (policyObj == null) return null;
            var t = policyObj.GetType();
            // property "filter"
            var p = AccessTools.Property(t, "filter");
            if (p != null) return p.GetValue(policyObj) as ThingFilter;
            // field "filter"
            var f = AccessTools.Field(t, "filter");
            if (f != null) return f.GetValue(policyObj) as ThingFilter;
            return null;
        }

        private static string GetLabel(object policyObj)
        {
            if (policyObj == null) return null;
            var t = policyObj.GetType();
            // property "label"
            var p = AccessTools.Property(t, "label");
            if (p != null) return p.GetValue(policyObj) as string;
            // field "label"
            var f = AccessTools.Field(t, "label");
            if (f != null) return f.GetValue(policyObj) as string;
            return null;
        }

        private static void SetAllowOnTree(ThingFilter filter, ThingCategoryDef root, bool allow)
        {
            if (root == null || filter == null) return;
            try { filter.SetAllow(root, allow); } catch { }
            if (root.childCategories == null) return;
            for (int i = 0; i < root.childCategories.Count; i++)
            {
                var c = root.childCategories[i];
                if (c == null) continue;
                try { filter.SetAllow(c, allow); } catch { }
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
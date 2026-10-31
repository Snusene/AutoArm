using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class BondedWeaponAutoForcedTest : ForcedSidearmScenario
    {
        public override string Name => "Bonded weapon auto forced";
        private ThingDef def;

        public override void Setup(Map map)
        {
            def = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_PersonaMonosword")
                ?? DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d =>
                    d.IsWeapon && d.comps != null && d.comps.Any(c => c.compClass == typeof(CompBladelinkWeapon)));
            MakePawn(map, null);
        }

        private ThingWithComps Bonded()
        {
            var weapon = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            var bladelink = weapon.GetComp<CompBladelinkWeapon>();
            bladelink.TraitsListForReading.RemoveAll(t => t.neverBond);
            bladelink.CodeFor(pawn);
            return weapon;
        }

        public override TestResult Run()
        {
            if (!ModsConfig.RoyaltyActive)
                return TestResult.Skip("Royalty not active");
            if (def == null)
                return TestResult.Skip("No persona weapon def");
            if (pawn == null)
                return TestResult.Failure("Setup incomplete");

            Settings.respectWeaponBonds = true;
            var weapon = Bonded();
            if (!EquipmentUtility.IsBondedTo(weapon, pawn))
                return TestResult.Failure("Bond not applied");
            pawn.equipment.AddEquipment(weapon);
            if (!ForcedWeapons.IsForced(pawn, weapon))
                return TestResult.Failure("Bonded weapon not forced with the setting on");

            ForcedWeapons.ClearForced(pawn);
            pawn.equipment.DestroyAllEquipment();

            Settings.respectWeaponBonds = false;
            weapon = Bonded();
            pawn.equipment.AddEquipment(weapon);
            if (ForcedWeapons.IsForced(pawn, weapon))
                return TestResult.Failure("Bonded weapon forced with the setting off");
            return TestResult.Pass();
        }
    }

    internal sealed class StorageOnlyTest : ForcedSidearmScenario
    {
        public override string Name => "Storage only setting";
        private Zone_Stockpile zone;
        private ThingWithComps stored;
        private ThingWithComps loose;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || !MakePawn(map, null)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);

            IntVec3 cell = IntVec3.Invalid;
            for (int i = 0; i < 20 && !cell.IsValid; i++)
            {
                var c = TestPositions.GetNearbyPosition(pawn.Position, 2f, 5f, map);
                if (c.IsValid && c.Standable(map) && map.zoneManager.ZoneAt(c) == null && c.GetSlotGroup(map) == null && c.GetFirstItem(map) == null)
                    cell = c;
            }
            if (!cell.IsValid) return;

            zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            zone.AddCell(cell);
            zone.settings.filter.SetAllowAll(null);
            stored = Track(TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle, cell, QualityCategory.Awful));
            loose = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (zone == null || stored == null || loose == null)
                return TestResult.Failure("Setup incomplete");
            if (stored.GetSlotGroup() == null)
                return TestResult.Failure("Stored rifle is not in the stockpile");
            if (loose.GetSlotGroup() != null)
                return TestResult.Failure("Loose rifle landed in storage");
            string blocked = Blocked(loose);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            Settings.onlyAutoEquipFromStorage = false;
            EquipEligibility.Clear();
            var job = GiveJob();
            if (job?.targetA.Thing != loose)
                return TestResult.Failure($"Setting off: expected the loose legendary rifle, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(loose);

            Settings.onlyAutoEquipFromStorage = true;
            EquipEligibility.Clear();
            JobGiver_PickUpBetterWeapon.InvalidatePawnValidationCache(pawn);
            job = GiveJob();
            if (job?.targetA.Thing != stored)
                return TestResult.Failure($"Setting on: expected the stored awful rifle, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(stored);
            return TestResult.Pass();
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (zone != null && zone.zoneManager.AllZones.Contains(zone))
                zone.Delete(false);
        }
    }

    internal sealed class UpgradeEquippedOffTest : ForcedSidearmScenario
    {
        public override string Name => "Upgrade equipped weapons off";
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Poor)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            better = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (better == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            Settings.upgradeEquippedWeapons = true;
            var job = GiveJob();
            if (job?.targetA.Thing != better)
                return TestResult.Failure($"Setting on: expected the legendary rifle, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(better);

            Settings.upgradeEquippedWeapons = false;
            JobGiver_PickUpBetterWeapon.InvalidatePawnValidationCache(pawn);
            job = GiveJob();
            if (job?.targetA.Thing == better)
                return TestResult.Failure($"Setting off: armed pawn still upgraded, got {Describe(job)}");
            return TestResult.Pass();
        }
    }

    internal sealed class UpgradeThresholdTest : ForcedSidearmScenario
    {
        public override string Name => "Upgrade threshold respected";

        private static readonly QualityCategory[] steps =
        {
            QualityCategory.Good, QualityCategory.Excellent, QualityCategory.Masterwork, QualityCategory.Legendary
        };

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Normal)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
        }

        public override TestResult Run()
        {
            var current = pawn?.equipment?.Primary;
            if (current == null)
                return TestResult.Failure("Setup incomplete");

            WeaponCache.ClearScoreCache();
            float currentScore = WeaponCache.GetCachedScore(pawn, current);
            float own = currentScore - Scoring.SharedBonus(pawn, current);
            if (own <= 0f)
                return TestResult.Failure($"Current rifle has no own value: score {currentScore:F1}");
            ThingWithComps candidate = null;
            float ratio = 0f;
            foreach (var quality in steps)
            {
                var weapon = Ground(AutoArmDefOf.Gun_AssaultRifle, quality);
                if (weapon == null)
                    return TestResult.Failure("Could not place a rifle");
                ratio = 1f + (WeaponCache.GetCachedScore(pawn, weapon) - currentScore) / own;
                if (ratio >= 1.03f && ratio <= 1.48f)
                {
                    candidate = weapon;
                    break;
                }
                TestHelpers.SafeDestroyWeapon(weapon);
                if (ratio > 1.48f)
                    break;
            }
            if (candidate == null)
                return TestResult.Skip($"No quality step gives a score ratio inside the slider range, last {ratio:F2}");
            string blocked = Blocked(candidate);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            Settings.weaponUpgradeThreshold = Mathf.Max(ratio - 0.02f, Constants.WeaponUpgradeThresholdMin);
            var job = GiveJob();
            if (job?.targetA.Thing != candidate)
                return TestResult.Failure($"Threshold {Settings.weaponUpgradeThreshold:F2} below ratio {ratio:F2}: expected the upgrade, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(candidate);

            Settings.weaponUpgradeThreshold = Mathf.Min(ratio + 0.02f, Constants.WeaponUpgradeThresholdMax);
            JobGiver_PickUpBetterWeapon.InvalidatePawnValidationCache(pawn);
            job = GiveJob();
            if (job?.targetA.Thing == candidate)
                return TestResult.Failure($"Threshold {Settings.weaponUpgradeThreshold:F2} above ratio {ratio:F2}: upgrade still taken");
            return TestResult.Pass();
        }
    }

    internal sealed class SkilledShooterQualityUpgradeTest : ForcedSidearmScenario
    {
        public override string Name => "Skilled shooter takes quality upgrade";
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Normal) || pawn.skills == null) return;
            pawn.skills.GetSkill(SkillDefOf.Shooting).Level = 15;
            pawn.skills.GetSkill(SkillDefOf.Melee).Level = 3;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            better = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Excellent);
        }

        public override TestResult Run()
        {
            if (better == null || pawn?.equipment?.Primary == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            Settings.weaponUpgradeThreshold = Constants.WeaponUpgradeThreshold;
            var job = GiveJob();
            if (job?.targetA.Thing != better)
                return TestResult.Failure($"Shooting 15 pawn skipped the excellent rifle, got {Describe(job)}");
            return TestResult.Pass();
        }
    }

    internal sealed class WeaponTypePreferenceTest : ForcedSidearmScenario
    {
        public override string Name => "Weapon type preference";
        private ThingWithComps rifle;
        private ThingWithComps mace;

        public override void Setup(Map map)
        {
            var meleeDef = TestDefOf.MeleeWeapon_Mace ?? TestDefOf.MeleeWeapon_Club;
            if (AutoArmDefOf.Gun_AssaultRifle == null || meleeDef == null) return;
            if (!MakePawn(map, null)) return;
            pawn.skills.GetSkill(SkillDefOf.Shooting).Level = 10;
            pawn.skills.GetSkill(SkillDefOf.Melee).Level = 10;
            if (pawn.workSettings?.EverWork == true)
                pawn.workSettings.Disable(WorkTypeDefOf.Hunting);
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, meleeDef);
            rifle = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Normal);
            mace = Ground(meleeDef, QualityCategory.Normal);
        }

        public override TestResult Run()
        {
            if (rifle == null || mace == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(rifle) ?? Blocked(mace);
            if (blocked != null)
                return TestResult.Skip($"Weapon blocked for this pawn by {blocked}");

            Settings.weaponTypePreference = 1f;
            var job = GiveJob();
            if (job?.targetA.Thing != rifle)
                return TestResult.Failure($"Full ranged preference: expected the rifle, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(rifle);

            Settings.weaponTypePreference = -1f;
            JobGiver_PickUpBetterWeapon.InvalidatePawnValidationCache(pawn);
            if (CECompat.IsLoaded)
            {
                WeaponCache.ClearScoreCache();
                float meleeRatio = WeaponCache.GetCachedScore(pawn, mace) / WeaponCache.GetCachedScore(pawn, rifle);
                Settings.weaponTypePreference = 1f;
                WeaponCache.ClearScoreCache();
                float rangedRatio = WeaponCache.GetCachedScore(pawn, mace) / WeaponCache.GetCachedScore(pawn, rifle);
                if (meleeRatio < rangedRatio * 2f)
                    return TestResult.Failure($"Melee preference barely moves the mace against the rifle under CE ({rangedRatio:F2} to {meleeRatio:F2})");
                return TestResult.Pass();
            }
            job = GiveJob();
            if (job?.targetA.Thing != mace)
            {
                WeaponCache.ClearScoreCache();
                float r = WeaponCache.GetCachedScore(pawn, rifle), m = WeaponCache.GetCachedScore(pawn, mace);
                Settings.weaponTypePreference = 1f;
                WeaponCache.ClearScoreCache();
                float r1 = WeaponCache.GetCachedScore(pawn, rifle), m1 = WeaponCache.GetCachedScore(pawn, mace);
                return TestResult.Failure($"Full melee preference: expected the {mace.def.label}, got {Describe(job)} (melee pref rifle={r:F0} mace={m:F0}, ranged pref rifle={r1:F0} mace={m1:F0})");
            }
            WeaponCache.ClearTemporaryReservation(mace);
            return TestResult.Pass();
        }
    }

    internal sealed class ForcedLabelTest : ForcedSidearmScenario
    {
        public override string Name => "Forced label shown";
        private ThingWithComps rifle;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle)) return;
            rifle = pawn.equipment.Primary;
            ForcedWeapons.SetForced(pawn, rifle, "test", log: false);
        }

        private string Label()
        {
            ForcedWeaponLabelHelper.ResetFrame();
            return rifle.LabelCap;
        }

        public override TestResult Run()
        {
            if (rifle == null)
                return TestResult.Failure("Setup incomplete");
            if (Find.Selector == null || Find.MainTabsRoot == null)
                return TestResult.Failure("No selector or main tabs");

            var selected = new List<object>(Find.Selector.SelectedObjects);
            var tab = Find.MainTabsRoot.OpenTab;
            try
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn, playSound: false);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);

                Settings.showForcedLabels = true;
                string shown = Label();
                string plain = ForcedWeaponLabelHelper.StripForcedSuffix(shown);
                if (shown == plain)
                {
                    ForcedWeaponLabelHelper.ResetFrame();
                    bool process = ForcedWeaponLabelHelper.ShouldProcessWeaponLabel();
                    return TestResult.Failure($"No forced suffix with the setting on: '{shown}' (tab={Find.MainTabsRoot.OpenTab?.defName}, selected={Find.Selector.SingleSelectedThing?.LabelShort}, process={process}, owned={ForcedWeaponLabelHelper.IsWeaponOwnedBySelectedPawn(rifle.thingIDNumber)}, forced={ForcedWeapons.IsForced(pawn, rifle)})");
                }

                Settings.showForcedLabels = false;
                if (Label() != plain)
                    return TestResult.Failure("Forced suffix shown with the setting off");

                Settings.showForcedLabels = true;
                ForcedWeapons.ClearForced(pawn);
                if (Label() != plain)
                    return TestResult.Failure("Forced suffix shown on an unforced weapon");
                return TestResult.Pass();
            }
            finally
            {
                Find.Selector.ClearSelection();
                foreach (var obj in selected)
                {
                    if (!(obj is Thing thing) || thing.Spawned)
                        Find.Selector.Select(obj, playSound: false);
                }
                if (tab == null)
                    Find.MainTabsRoot.EscapeCurrentTab(playSound: false);
                else
                    Find.MainTabsRoot.SetCurrentTab(tab, playSound: false);
                ForcedWeaponLabelHelper.ResetFrame();
            }
        }
    }
}

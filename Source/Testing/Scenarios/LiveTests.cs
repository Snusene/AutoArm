using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Jobs;
using RimWorld;
using System.Linq;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class LivePrimarySwapTest : ForcedSidearmScenario
    {
        public override string Name => "Primary swap job completes";
        private ThingWithComps old;
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Poor)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            old = pawn.equipment.Primary;
            better = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (old == null || better == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            var job = GiveJob();
            if (job?.def != AutoArmDefOf.AutoArmSwapPrimary || job.targetA.Thing != better)
                return TestResult.Failure($"Expected primary swap to the legendary rifle, got {Describe(job)}");

            using (Live())
            {
                if (!Sim.RunJob(pawn, job))
                    return TestResult.Failure($"Swap job still running after 2500 ticks ({pawn.CurJob?.def?.defName})");
            }

            if (pawn.equipment.Primary != better)
                return TestResult.Failure($"Primary is {pawn.equipment.Primary?.LabelShort ?? "nothing"} after the swap");
            if (!old.Spawned)
                return TestResult.Failure("Old rifle was not put on the ground");
            return TestResult.Pass();
        }
    }

    internal sealed class LiveForcedPrimaryUpgradeTest : ForcedSidearmScenario
    {
        public override string Name => "Forced primary upgrade stays forced";
        private ThingWithComps old;
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Poor)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            old = pawn.equipment.Primary;
            ForcedWeapons.SetForced(pawn, old, "test", log: false);
            better = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (old == null || better == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            var job = GiveJob(upgrades: true);
            if (job?.def != AutoArmDefOf.AutoArmSwapPrimary || job.targetA.Thing != better)
                return TestResult.Failure($"Expected forced primary upgrade, got {Describe(job)}");

            using (Live())
            {
                if (!Sim.RunJob(pawn, job))
                    return TestResult.Failure($"Swap job still running after 2500 ticks ({pawn.CurJob?.def?.defName})");
                Sim.Tick(600);
            }

            if (pawn.equipment.Primary != better)
                return TestResult.Failure($"Primary is {pawn.equipment.Primary?.LabelShort ?? "nothing"} after the swap");
            if (!ForcedWeapons.IsForced(pawn, better))
                return TestResult.Failure("Upgraded rifle did not inherit forced status");
            if (ForcedWeapons.IsForced(pawn, old))
                return TestResult.Failure("Dropped rifle still forced after the grace period");
            return TestResult.Pass();
        }
    }

    internal sealed class LiveSidearmSwapTest : ForcedSidearmScenario
    {
        public override string Name => "Sidearm swap job completes";
        private ThingWithComps forced;
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.Gun_Autopistol);
            forced = AddForcedSidearm(AutoArmDefOf.Gun_Autopistol, QualityCategory.Awful);
            better = Ground(AutoArmDefOf.Gun_Autopistol, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsManagingPawn(pawn))
                return TestResult.Skip("SimpleSidearms not managing pawn");
            if (forced == null || better == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Autopistol blocked for this pawn by {blocked}");

            var job = GiveJob();
            if (job?.def != AutoArmDefOf.AutoArmSwapSidearm || job.targetA.Thing != better || job.targetB.Thing != forced)
                return TestResult.Failure($"Expected sidearm swap of the forced autopistol, got {Describe(job)}");

            using (Live())
            {
                if (!Sim.RunJob(pawn, job))
                    return TestResult.Failure($"Swap job still running after 2500 ticks ({pawn.CurJob?.def?.defName})");
            }

            var inv = pawn.inventory.innerContainer;
            if (!inv.Contains(better))
                return TestResult.Failure("Legendary autopistol is not in the inventory after the swap");
            if (inv.Contains(forced) || !forced.Spawned)
                return TestResult.Failure("Old autopistol was not put on the ground");
            if (!ForcedWeapons.IsForced(pawn, better))
                return TestResult.Failure("New sidearm did not inherit forced status");
            if (!SimpleSidearmsCompat.IsRememberedSidearm(pawn, better))
                return TestResult.Failure("SimpleSidearms does not remember the new sidearm");
            return TestResult.Pass();
        }
    }

    internal sealed class LiveArmsIdleColonistTest : ForcedSidearmScenario
    {
        public override string Name => "Idle colonist arms itself";
        private ThingWithComps rifle;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, null)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            rifle = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Normal);
        }

        public override TestResult Run()
        {
            if (rifle == null)
                return TestResult.Failure("Setup incomplete");
            if (SimpleSidearmsCompat.WantsUnarmed(pawn))
                return TestResult.Skip("SimpleSidearms wants this pawn unarmed");
            string blocked = Blocked(rifle);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            using (Live())
            {
                if (!Sim.TickUntil(() => pawn.equipment.Primary == rifle, 2500))
                    return TestResult.Failure($"Still {pawn.equipment.Primary?.LabelShort ?? "unarmed"} after 2500 ticks, current job {pawn.CurJob?.def?.defName ?? "none"}");
            }
            return TestResult.Pass();
        }
    }

    internal sealed class LiveNoFlipFlopTest : ForcedSidearmScenario
    {
        public override string Name => "No weapon flip-flop over time";
        private ThingWithComps old;
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Poor)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            old = pawn.equipment.Primary;
            better = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (old == null || better == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            int changes = 0;
            using (Live())
            {
                if (!Sim.TickUntil(() => pawn.equipment.Primary == better, 2500))
                    return TestResult.Failure($"Never upgraded, still {pawn.equipment.Primary?.LabelShort ?? "unarmed"}, current job {pawn.CurJob?.def?.defName ?? "none"}");

                Sim.TickUntil(() =>
                {
                    if (pawn.equipment.Primary != better)
                        changes++;
                    return changes > 0;
                }, 3000);
            }

            if (changes > 0)
                return TestResult.Failure($"Primary changed again after the upgrade, now {pawn.equipment.Primary?.LabelShort ?? "nothing"}");
            return TestResult.Pass();
        }
    }

    internal sealed class LiveForcedGraceTest : ForcedSidearmScenario
    {
        public override string Name => "Forced status grace period";
        private ThingWithComps rifle;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle)) return;
            rifle = pawn.equipment.Primary;
            ForcedWeapons.SetForced(pawn, rifle, "test", log: false);
        }

        public override TestResult Run()
        {
            if (rifle == null)
                return TestResult.Failure("Setup incomplete");

            using (Live(actor: false))
            {
                if (!pawn.equipment.TryDropEquipment(rifle, out var dropped, pawn.Position))
                    return TestResult.Failure("Could not drop the forced rifle");
                Sim.Tick(60);
                dropped.DeSpawn();
                pawn.equipment.AddEquipment(dropped);
                Sim.Tick(600);
                if (!ForcedWeapons.IsForced(pawn, dropped))
                    return TestResult.Failure("Forced status lost although the rifle was re-equipped within the grace period");

                if (!pawn.equipment.TryDropEquipment(dropped, out var left, pawn.Position))
                    return TestResult.Failure("Could not drop the rifle the second time");
                Sim.Tick(600);
                if (ForcedWeapons.IsForced(pawn, left))
                    return TestResult.Failure("Forced status kept 600 ticks after the rifle was dropped");
            }
            return TestResult.Pass();
        }
    }

    internal sealed class LivePolicyReplaceTest : ForcedSidearmScenario
    {
        public override string Name => "Disallowed primary replaced, not picked up again";
        private ThingWithComps rifle;
        private ThingWithComps pistol;
        private ApparelPolicy policy;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary)) return;
            rifle = pawn.equipment.Primary;
            pistol = Ground(AutoArmDefOf.Gun_Autopistol, QualityCategory.Normal);

            policy = Current.Game.outfitDatabase.MakeNewOutfit();
            policy.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
            policy.filter.SetAllow(AutoArmDefOf.Gun_AssaultRifle, false);
            pawn.outfits.CurrentApparelPolicy = policy;
            WeaponCache.OnOutfitFilterChanged(policy);
        }

        public override TestResult Run()
        {
            if (rifle == null || pistol == null || policy == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(pistol);
            if (blocked != null)
                return TestResult.Skip($"Autopistol blocked for this pawn by {blocked}");

            using (Live())
            {
                if (!Sim.TickUntil(() => pawn.equipment.Primary == pistol, 2500))
                    return TestResult.Failure($"Not switched to the allowed autopistol, holding {pawn.equipment.Primary?.LabelShort ?? "nothing"}, current job {pawn.CurJob?.def?.defName ?? "none"}");
                Sim.Tick(1500);
            }

            if (pawn.equipment.Primary != pistol)
                return TestResult.Failure($"Primary changed to {pawn.equipment.Primary?.LabelShort ?? "nothing"} after the switch");
            if (pawn.inventory.innerContainer.Contains(rifle))
                return TestResult.Failure("Disallowed rifle was picked up as a sidearm");
            return TestResult.Pass();
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (policy != null)
                Current.Game?.outfitDatabase?.TryDelete(policy);
        }
    }

    internal sealed class LiveAreaTest : ForcedSidearmScenario
    {
        public override string Name => "Colonist arms inside its allowed area";
        private ThingWithComps pistol;
        private ThingWithComps rifle;
        private Area_Allowed area;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (!MakePawn(map, null)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.Gun_Autopistol);
            pistol = Ground(AutoArmDefOf.Gun_Autopistol, QualityCategory.Poor);
            rifle = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary, 9f, 12f);
        }

        public override TestResult Run()
        {
            if (pistol == null || rifle == null || pawn.playerSettings == null)
                return TestResult.Failure("Setup incomplete");
            if (SimpleSidearmsCompat.WantsUnarmed(pawn))
                return TestResult.Skip("SimpleSidearms wants this pawn unarmed");
            string blocked = Blocked(pistol) ?? Blocked(rifle);
            if (blocked != null)
                return TestResult.Skip($"Test weapon blocked for this pawn by {blocked}");
            if (!map.areaManager.TryMakeNewAllowed(out area))
                return TestResult.Skip("No free area slot");

            foreach (var cell in GenRadial.RadialCellsAround(pawn.Position, 5f, true))
            {
                if (cell.InBounds(map))
                    area[cell] = true;
            }
            if (area[rifle.Position] || !area[pistol.Position])
                return TestResult.Skip("Weapons did not land on the intended sides of the area");
            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;

            using (Live())
            {
                if (!Sim.TickUntil(() => pawn.equipment.Primary != null, 2500))
                    return TestResult.Failure($"Still unarmed after 2500 ticks, current job {pawn.CurJob?.def?.defName ?? "none"}");
            }

            if (pawn.equipment.Primary != pistol)
                return TestResult.Failure($"Equipped {pawn.equipment.Primary.LabelShort} instead of the autopistol inside the area");
            return TestResult.Pass();
        }

        public override void Cleanup()
        {
            if (pawn?.playerSettings != null)
                pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
            area?.Delete();
            base.Cleanup();
        }
    }

    internal sealed class LiveForcedPrimarySidearmTest : ForcedSidearmScenario
    {
        public override string Name => "Forced primary still picks up sidearm";
        private ThingWithComps rifle;
        private ThingWithComps knife;

        public override void Setup(Map map)
        {
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.MeleeWeapon_Knife == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle, AutoArmDefOf.MeleeWeapon_Knife);
            rifle = pawn.equipment.Primary;
            ForcedWeapons.SetForced(pawn, rifle, "test", log: false);
            knife = Ground(AutoArmDefOf.MeleeWeapon_Knife, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsManagingPawn(pawn))
                return TestResult.Skip("SimpleSidearms not managing pawn");
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return TestResult.Skip("Auto equip sidearms off");
            if (rifle == null || knife == null)
                return TestResult.Failure("Setup incomplete");
            if (!SimpleSidearmsCompat.CanPickupSidearm(knife, pawn, out string ssReason))
                return TestResult.Skip($"SimpleSidearms refuses the knife: {ssReason}");
            string blocked = Blocked(knife);
            if (blocked != null)
                return TestResult.Skip($"Knife blocked for this pawn by {blocked}");

            SetForcedUpgrades(false);
            using (Live())
            {
                if (!Sim.TickUntil(() => pawn.inventory.innerContainer.Contains(knife), 2500))
                    return TestResult.Failure($"Knife not picked up after 2500 ticks, current job {pawn.CurJob?.def?.defName ?? "none"}");
            }

            if (pawn.equipment.Primary != rifle)
                return TestResult.Failure($"Forced rifle was replaced by {pawn.equipment.Primary?.LabelShort ?? "nothing"}");
            return TestResult.Pass();
        }
    }

    internal sealed class LiveMinigunUpgradeTest : ForcedSidearmScenario
    {
        public override string Name => "Minigun holder still upgrades";
        private ThingDef minigun;
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            minigun = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Minigun");
            if (minigun == null) return;
            if (!MakePawn(map, minigun, QualityCategory.Awful)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, minigun);
            better = Ground(minigun, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (minigun == null)
                return TestResult.Skip("No minigun def");
            if (better == null)
                return TestResult.Failure("Setup incomplete");
            if (minigun.equippedStatOffsets.NullOrEmpty() || minigun.equippedStatOffsets.Any(m => m.value > 0f))
                return TestResult.Skip("Minigun no longer has only penalty stat offsets");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Minigun blocked for this pawn by {blocked}");

            using (Live())
            {
                if (!Sim.TickUntil(() => pawn.equipment.Primary == better, 2500))
                    return TestResult.Failure($"Still holding {pawn.equipment.Primary?.LabelShort ?? "nothing"} after 2500 ticks, current job {pawn.CurJob?.def?.defName ?? "none"}");
            }
            return TestResult.Pass();
        }
    }

    internal sealed class IdeologyDespisedWeaponTest : ForcedSidearmScenario
    {
        public override string Name => "Despised weapon ignored";
        private ThingWithComps rifle;

        public override void Setup(Map map)
        {
            if (!ModsConfig.IdeologyActive || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, null)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            rifle = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!ModsConfig.IdeologyActive)
                return TestResult.Skip("Ideology not active");
            if (rifle == null)
                return TestResult.Failure("Setup incomplete");
            var weaponClass = rifle.def.weaponClasses?.FirstOrDefault();
            if (weaponClass == null)
                return TestResult.Skip("Rifle has no weapon class");
            var ideo = pawn.Ideo;
            if (ideo == null)
                return TestResult.Skip("Pawn has no ideology");
            string blocked = Blocked(rifle);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            var job = GiveJob();
            if (job?.targetA.Thing != rifle)
                return TestResult.Skip($"Rifle not chosen even without the precept, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(rifle);

            var otherClass = DefDatabase<WeaponClassDef>.AllDefsListForReading.FirstOrDefault(d => !rifle.def.weaponClasses.Contains(d));
            if (otherClass == null)
                return TestResult.Skip("Rifle belongs to every weapon class");

            var precept = ideo.PreceptsListForReading.OfType<Precept_Weapon>().FirstOrDefault();
            Precept_Weapon added = null;
            if (precept == null)
            {
                added = (Precept_Weapon)PreceptMaker.MakePrecept(PreceptDefOf.NobleDespisedWeapons);
                ideo.AddPrecept(added);
                precept = added;
            }
            var old = precept.despised;
            var oldNoble = precept.noble;
            IdeoTestHelper.SetNoble(precept, otherClass);
            IdeoTestHelper.SetDespised(precept, weaponClass);
            try
            {
                JobGiver_PickUpBetterWeapon.InvalidatePawnValidationCache(pawn);
                job = GiveJob();
                if (job?.targetA.Thing == rifle)
                    return TestResult.Failure($"Rifle chosen although its class {weaponClass.defName} is despised");
            }
            finally
            {
                if (added != null)
                    ideo.RemovePrecept(added, replacing: true);
                else
                {
                    IdeoTestHelper.SetDespised(precept, old);
                    IdeoTestHelper.SetNoble(precept, oldNoble);
                }
                JobGiver_PickUpBetterWeapon.InvalidatePawnValidationCache(pawn);
            }
            return TestResult.Pass();
        }
    }
}

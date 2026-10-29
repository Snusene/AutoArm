using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    internal static class PocketSandTestHelper
    {
        public static JobDef EquipDef => DefDatabase<JobDef>.GetNamedSilentFail("PS_Equip");

        public static bool Pocket(Pawn pawn, ThingWithComps target)
        {
            var job = JobMaker.MakeJob(EquipDef, target, pawn.equipment.Primary);
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                return false;
            int id = job.loadID;
            return Sim.TickUntil(() => pawn.CurJob == null || pawn.CurJob.loadID != id, 2500);
        }
    }

    internal sealed class PocketSandSwapNotForcedTest : ForcedSidearmScenario
    {
        public override string Name => "Pocket Sand swap not forced";
        private ThingWithComps rifle;
        private ThingWithComps pistol;

        public override void Setup(Map map)
        {
            if (!PocketSandCompat.IsLoaded || AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle)) return;
            rifle = pawn.equipment.Primary;
            pistol = AddCarried(AutoArmDefOf.Gun_Autopistol, remembered: false);
        }

        public override TestResult Run()
        {
            if (!PocketSandCompat.IsLoaded || PocketSandTestHelper.EquipDef == null)
                return TestResult.Skip("Pocket Sand not loaded");
            if (rifle == null || pistol == null)
                return TestResult.Failure("Setup incomplete");

            using (Live())
            {
                if (!PocketSandTestHelper.Pocket(pawn, pistol))
                    return TestResult.Failure($"Pocket Sand swap did not finish ({pawn.CurJob?.def?.defName})");
                if (pawn.equipment.Primary != pistol)
                    return TestResult.Failure($"Primary is {pawn.equipment.Primary?.LabelShort ?? "nothing"} after the swap");
                if (ForcedWeapons.IsForced(pawn, pistol))
                    return TestResult.Failure("Pistol picked through the Pocket Sand gizmo was forced");

                if (!PocketSandTestHelper.Pocket(pawn, rifle))
                    return TestResult.Failure($"Swap back did not finish ({pawn.CurJob?.def?.defName})");
                if (pawn.equipment.Primary != rifle)
                    return TestResult.Failure($"Primary is {pawn.equipment.Primary?.LabelShort ?? "nothing"} after swapping back");
                if (ForcedWeapons.IsForced(pawn, rifle))
                    return TestResult.Failure("Rifle swapped back through the Pocket Sand gizmo was forced, so it would stop upgrading");
            }
            return TestResult.Pass();
        }
    }

    internal sealed class PocketSandUnarmedRespectedTest : ForcedSidearmScenario
    {
        public override string Name => "Pocket Sand unarmed kept";
        private ThingWithComps rifle;
        private ThingWithComps better;

        public override void Setup(Map map)
        {
            if (!PocketSandCompat.IsLoaded || AutoArmDefOf.Gun_AssaultRifle == null) return;
            if (!MakePawn(map, AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Normal)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_AssaultRifle);
            rifle = pawn.equipment.Primary;
            better = Ground(AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Legendary);
        }

        public override TestResult Run()
        {
            if (!PocketSandCompat.IsLoaded || PocketSandTestHelper.EquipDef == null)
                return TestResult.Skip("Pocket Sand not loaded");
            if (rifle == null || better == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(better);
            if (blocked != null)
                return TestResult.Skip($"Rifle blocked for this pawn by {blocked}");

            using (Live())
            {
                if (!PocketSandTestHelper.Pocket(pawn, null))
                    return TestResult.Failure($"Pocket Sand unarmed did not finish ({pawn.CurJob?.def?.defName})");
                if (pawn.equipment.Primary != null)
                    return TestResult.Failure($"Still holding {pawn.equipment.Primary.LabelShort} after Pocket Sand unarmed");
                if (!pawn.inventory.innerContainer.Contains(rifle))
                    return TestResult.Failure("Rifle not in the pocket after Pocket Sand unarmed");
                Sim.Tick(2500);
            }

            if (pawn.equipment.Primary != null)
                return TestResult.Failure($"AutoArm armed a pawn the player made unarmed through Pocket Sand ({pawn.equipment.Primary.LabelShort})");
            return TestResult.Pass();
        }
    }

    internal sealed class PocketSandBeerNotWeaponTest : ForcedSidearmScenario
    {
        public override string Name => "Beer in pocket doesn't block arming";
        private ThingWithComps pistol;
        private bool carrying;

        public override void Setup(Map map)
        {
            var beer = DefDatabase<ThingDef>.GetNamedSilentFail("Beer");
            if (!PocketSandCompat.IsLoaded || beer == null || AutoArmDefOf.Gun_Autopistol == null) return;
            if (!MakePawn(map, null)) return;
            ForcedWeaponUpgradeTestHelper.AllowWeaponInOutfit(pawn, AutoArmDefOf.Gun_Autopistol);
            carrying = pawn.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(beer));
            pistol = Ground(AutoArmDefOf.Gun_Autopistol, QualityCategory.Normal);
        }

        public override TestResult Run()
        {
            if (!PocketSandCompat.IsLoaded)
                return TestResult.Skip("Pocket Sand not loaded");
            if (!carrying || pistol == null)
                return TestResult.Failure("Setup incomplete");
            string blocked = Blocked(pistol);
            if (blocked != null)
                return TestResult.Skip($"Autopistol blocked for this pawn by {blocked}");

            var job = GiveJob();
            if (job?.targetA.Thing != pistol)
                return TestResult.Failure($"Unarmed pawn carrying beer was not armed, got {Describe(job)}");
            WeaponCache.ClearTemporaryReservation(pistol);
            return TestResult.Pass();
        }
    }
}


using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace AutoArm
{
    public sealed class JobDriver_SwapSidearm : JobDriver
    {
        private ThingWithComps NewWeapon => (ThingWithComps)job.targetA.Thing;
        private ThingWithComps OldWeapon => (ThingWithComps)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(NewWeapon, job, 1, -1, null, errorOnFailed);
        }

        public override string GetReport()
        {
            var newW = job.targetA.Thing?.Label ?? "weapon";
            var oldW = job.targetB.Thing?.Label ?? "sidearm";
            return $"AutoArm: swapping sidearm {oldW} for {newW}";
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A)
                .FailOn(() => pawn.Downed);

            var swapToil = ToilMaker.MakeToil("AutoArmSidearmSwap");
            swapToil.initAction = delegate
            {
                PerformAtomicSidearmSwap(pawn, NewWeapon, OldWeapon);
            };
            swapToil.defaultCompleteMode = ToilCompleteMode.Instant;

            swapToil.FailOn(() => pawn.Downed);
            swapToil.FailOn(() => NewWeapon == null || NewWeapon.Destroyed);
            swapToil.FailOn(() => OldWeapon == null || OldWeapon.Destroyed);

            yield return swapToil;
        }


        private void PerformAtomicSidearmSwap(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            int oldWeaponIndex = pawn.inventory.innerContainer.IndexOf(oldWeapon);
            if (oldWeaponIndex < 0)
            {
                AutoArmLogger.WarnFileOnly($"[{pawn.LabelShort}] Swap aborted - {oldWeapon.Label} not in inventory");
                AutoArm.Jobs.JobGiver_PickUpBetterWeapon.RecordFailedJob(pawn, newWeapon);
                return;
            }

            // Swap-aware validation
            if (SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed)
            {
                string validationReason;
                if (!SimpleSidearmsCompat.CanUseSidearmForSwap(newWeapon, oldWeapon, pawn, out validationReason))
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Swap aborted - SS rejected {newWeapon.Label}: {validationReason}");
                    return;
                }
            }


            ThingWithComps pickedUp;
            if (newWeapon.stackCount > 1)
            {
                pickedUp = (ThingWithComps)newWeapon.SplitOff(1);
            }
            else
            {
                if (newWeapon.Spawned) newWeapon.DeSpawn();
                pickedUp = newWeapon;
            }

            ThingWithComps oldWeaponToPlace = oldWeapon;
            IntVec3 dropPosition = pawn.Position;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                int invCount = pawn.inventory.innerContainer.Count;
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Atomic swap: dropping {oldWeapon.Label} (slot {oldWeaponIndex}), picking up {newWeapon.Label}");
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Current inventory count: {invCount} items");
            }

            if (oldWeaponToPlace.stackCount > 1)
            {
                oldWeaponToPlace = (ThingWithComps)oldWeapon.SplitOff(1);
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Split stack: keeping {oldWeapon.stackCount} in inventory, dropping 1");
            }
            else
            {
                pawn.inventory.innerContainer.Remove(oldWeapon);
            }

            if (!pawn.inventory.innerContainer.TryAdd(pickedUp))
            {
                AutoArmLogger.WarnFileOnly($"[{pawn.LabelShort}] Sidearm swap TryAdd failed for {pickedUp.Label}, recovering");

                if (!pawn.inventory.innerContainer.TryAdd(oldWeaponToPlace))
                {
                    GenPlace.TryPlaceThing(oldWeaponToPlace, dropPosition, pawn.MapHeld, ThingPlaceMode.Near);
                }

                if (!pickedUp.Spawned)
                {
                    GenPlace.TryPlaceThing(pickedUp, dropPosition, pawn.MapHeld, ThingPlaceMode.Near);
                }
                return;
            }

            if (pickedUp.def.soundInteract != null)
            {
                pickedUp.def.soundInteract.PlayOneShot(new TargetInfo(pawn.Position, pawn.MapHeld, false));
            }

            GenPlace.TryPlaceThing(oldWeaponToPlace, dropPosition, pawn.MapHeld, ThingPlaceMode.Near);

            if (oldWeaponToPlace != null && oldWeaponToPlace.Spawned)
            {
                oldWeaponToPlace.SetForbidden(false, false);
            }

            if (SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed)
            {
                SimpleSidearmsCompat.InformOfDroppedWeapon(pawn, oldWeaponToPlace);
                SimpleSidearmsCompat.InformOfAddedSidearm(pawn, pickedUp);
            }

            DroppedItems.MarkAsDropped(oldWeaponToPlace, Constants.LongDropCooldownTicks, pawn);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Atomic swap done: {oldWeapon.Label} replaced with {newWeapon.Label}");
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Dropped {oldWeaponToPlace.Label} at {dropPosition}");
            }

            if (AutoArmMod.settings?.showNotifications == true &&
                PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message("AutoArm_UpgradedSidearm".Translate(
                    pawn.LabelShort.CapitalizeFirst(),
                    oldWeapon.Label,
                    newWeapon.Label
                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
            }

            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Sidearm swap done");
        }
    }

    public sealed class JobDriver_SwapPrimary : JobDriver
    {
        private ThingWithComps NewWeapon => (ThingWithComps)job.targetA.Thing;
        private ThingWithComps OldWeapon => (ThingWithComps)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(NewWeapon, job, 1, -1, null, errorOnFailed);
        }

        public override string GetReport()
        {
            var newW = job.targetA.Thing?.Label ?? "weapon";
            var oldW = job.targetB.Thing?.Label ?? "weapon";
            return $"AutoArm: swapping primary {oldW} for {newW}";
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A)
                .FailOn(() => pawn.Downed);

            var swapToil = ToilMaker.MakeToil("AutoArmPrimarySwap");
            swapToil.initAction = delegate
            {
                PerformPrimaryWeaponSwap(pawn, NewWeapon, OldWeapon);
            };
            swapToil.defaultCompleteMode = ToilCompleteMode.Instant;

            swapToil.FailOn(() => pawn.Downed);
            swapToil.FailOn(() => NewWeapon == null || NewWeapon.Destroyed);
            swapToil.FailOn(() => OldWeapon == null || OldWeapon.Destroyed);

            yield return swapToil;
        }


        private void PerformPrimaryWeaponSwap(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            if (pawn.equipment?.Primary != oldWeapon)
            {
                AutoArmLogger.WarnFileOnly($"[{pawn.LabelShort}] Primary swap aborted - {oldWeapon.Label} is no longer primary");
                return;
            }

            IntVec3 dropPosition = pawn.Position;

            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Swapping primary from {oldWeapon.Label} to {newWeapon.Label}");


            ThingWithComps droppedWeapon;
            if (!pawn.equipment.TryDropEquipment(oldWeapon, out droppedWeapon, dropPosition))
            {
                AutoArmLogger.WarnFileOnly($"[{pawn.LabelShort}] Failed to drop primary weapon {oldWeapon.Label}");
                return;
            }

            if (droppedWeapon != null && droppedWeapon.Spawned)
            {
                droppedWeapon.SetForbidden(false, false);
            }

            ThingWithComps pickedUp;
            if (newWeapon.stackCount > 1)
            {
                pickedUp = (ThingWithComps)newWeapon.SplitOff(1);
            }
            else
            {
                if (newWeapon.Spawned) newWeapon.DeSpawn();
                pickedUp = newWeapon;
            }

            try
            {
                pawn.equipment.AddEquipment(pickedUp);
            }
            catch (System.Exception e)
            {
                AutoArmLogger.WarnFileOnly($"[{pawn.LabelShort}] Primary swap AddEquipment failed: {e.Message}");
                if (pickedUp != null && !pickedUp.Spawned)
                    GenPlace.TryPlaceThing(pickedUp, dropPosition, pawn.MapHeld, ThingPlaceMode.Near);
                return;
            }

            if (pickedUp.def.soundInteract != null)
            {
                pickedUp.def.soundInteract.PlayOneShot(new TargetInfo(pawn.Position, pawn.MapHeld, false));
            }

            if (SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed)
            {
                SimpleSidearmsCompat.InformOfDroppedWeapon(pawn, droppedWeapon);
                SimpleSidearmsCompat.InformOfAddedPrimary(pawn, pickedUp);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    SimpleSidearmsCompat.LogRememberedWeapons(pawn, "after primary swap");
                }
            }

            DroppedItems.MarkAsDropped(droppedWeapon, Constants.LongDropCooldownTicks, pawn);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Swapped primary from {oldWeapon.Label} to {newWeapon.Label}");
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Dropped weapon spawned={droppedWeapon?.Spawned}, pos={droppedWeapon?.Position}");
            }
        }
    }
}


using AutoArm.Compatibility;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace AutoArm
{
    /// <summary>
    /// Swap sidearms
    /// </summary>
    public class JobDriver_SwapSidearm : JobDriver
    {
        private ThingWithComps NewWeapon => (ThingWithComps)job.targetA.Thing;
        private ThingWithComps OldWeapon => (ThingWithComps)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(NewWeapon, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A)
                .FailOn(() => pawn.Downed)
                .FailOn(() => !pawn.CanReach(NewWeapon, PathEndMode.Touch, Danger.Deadly));

            var swapToil = new Toil
            {
                initAction = delegate
                {
                    if (NewWeapon == null || OldWeapon == null)
                    {
                        AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Swap cancelled - weapon no longer exists");
                        return;
                    }

                    PerformAtomicSidearmSwap(pawn, NewWeapon, OldWeapon);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

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
                AutoArmLogger.Warn($"[{pawn.LabelShort}] Swap aborted - {oldWeapon.Label} not in inventory");
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


            if (newWeapon.Spawned)
            {
                newWeapon.DeSpawn();
            }

            ThingWithComps oldWeaponToPlace = oldWeapon;
            IntVec3 dropPosition = pawn.Position;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                int invCount = pawn.inventory.innerContainer.Count;
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Performing atomic swap: {oldWeapon.Label} (slot {oldWeaponIndex}) -> {newWeapon.Label}");
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

            if (!pawn.inventory.innerContainer.TryAdd(newWeapon))
            {
                AutoArmLogger.Error($"[{pawn.LabelShort}] CRITICAL: Failed to add {newWeapon.Label} during atomic swap!");

                if (!pawn.inventory.innerContainer.TryAdd(oldWeaponToPlace))
                {
                    GenPlace.TryPlaceThing(oldWeaponToPlace, dropPosition, pawn.Map, ThingPlaceMode.Near);
                }

                if (!newWeapon.Spawned)
                {
                    GenPlace.TryPlaceThing(newWeapon, dropPosition, pawn.Map, ThingPlaceMode.Near);
                }
                return;
            }

            if (newWeapon.def.soundInteract != null)
            {
                newWeapon.def.soundInteract.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map, false));
            }

            GenPlace.TryPlaceThing(oldWeaponToPlace, dropPosition, pawn.Map, ThingPlaceMode.Near);

            if (oldWeaponToPlace != null && oldWeaponToPlace.Spawned)
            {
                oldWeaponToPlace.SetForbidden(false, false);
            }

            if (SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed)
            {
                SimpleSidearmsCompat.InformOfDroppedWeapon(pawn, oldWeaponToPlace);
                SimpleSidearmsCompat.InformOfAddedSidearm(pawn, newWeapon);
            }

            DroppedItemTracker.MarkAsDropped(oldWeaponToPlace, 600, pawn);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Atomic swap successful: {oldWeapon.Label} -> {newWeapon.Label}");
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

            JobGiver_PickUpBetterWeapon.RecordWeaponEquip(pawn);
            EquipCooldownTracker.Record(pawn);

            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Sidearm swap complete and reordered");
        }
    }

    /// <summary>
    /// Swap primary weapons
    /// </summary>
    public class JobDriver_SwapPrimary : JobDriver
    {
        private ThingWithComps NewWeapon => (ThingWithComps)job.targetA.Thing;
        private ThingWithComps OldWeapon => (ThingWithComps)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(NewWeapon, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A)
                .FailOn(() => pawn.Downed)
                .FailOn(() => !pawn.CanReach(NewWeapon, PathEndMode.Touch, Danger.Deadly));

            var swapToil = new Toil
            {
                initAction = delegate
                {
                    if (NewWeapon == null || OldWeapon == null)
                    {
                        AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Primary swap cancelled - weapon no longer exists");
                        return;
                    }

                    PerformPrimaryWeaponSwap(pawn, NewWeapon, OldWeapon);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            swapToil.FailOn(() => pawn.Downed);
            swapToil.FailOn(() => NewWeapon == null || NewWeapon.Destroyed);
            swapToil.FailOn(() => OldWeapon == null || OldWeapon.Destroyed);

            yield return swapToil;
        }


        private void PerformPrimaryWeaponSwap(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            if (pawn.equipment?.Primary != oldWeapon)
            {
                AutoArmLogger.Warn($"[{pawn.LabelShort}] Primary swap aborted - {oldWeapon.Label} is no longer primary");
                return;
            }

            IntVec3 dropPosition = pawn.Position;

            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Swapping primary: {oldWeapon.Label} -> {newWeapon.Label}");


            ThingWithComps droppedWeapon;
            if (!pawn.equipment.TryDropEquipment(oldWeapon, out droppedWeapon, dropPosition))
            {
                AutoArmLogger.Error($"[{pawn.LabelShort}] Failed to drop primary weapon {oldWeapon.Label}");
                return;
            }

            if (droppedWeapon != null && droppedWeapon.Spawned)
            {
                droppedWeapon.SetForbidden(false, false);
            }

            if (newWeapon.Spawned)
            {
                newWeapon.DeSpawn();
            }

            pawn.equipment.AddEquipment(newWeapon);

            if (newWeapon.def.soundInteract != null)
            {
                newWeapon.def.soundInteract.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map, false));
            }

            if (SimpleSidearmsCompat.IsLoaded)
            {
                SimpleSidearmsCompat.InformOfDroppedWeapon(pawn, droppedWeapon);

            }

            DroppedItemTracker.MarkAsDropped(droppedWeapon, 600, pawn);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Primary swap successful: {oldWeapon.Label} -> {newWeapon.Label}");
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Dropped {droppedWeapon.Label} at {dropPosition}");
            }

            JobGiver_PickUpBetterWeapon.RecordWeaponEquip(pawn);
            EquipCooldownTracker.Record(pawn);

            if (AutoArmMod.settings?.showNotifications == true &&
                PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message("AutoArm_UpgradedWeapon".Translate(
                    pawn.LabelShort.CapitalizeFirst(),
                    oldWeapon.Label,
                    newWeapon.Label
                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
            }
        }
    }
}

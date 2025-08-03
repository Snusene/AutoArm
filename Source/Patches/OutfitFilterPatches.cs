// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for outfit filter enforcement
// Drops weapons when they violate outfit policies

using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using AutoArm.Helpers; using AutoArm.Jobs; using AutoArm.Logging; using AutoArm.Patches;
using AutoArm.Weapons;

namespace AutoArm
{
    [HarmonyPatch(typeof(Pawn_OutfitTracker), "CurrentApparelPolicy", MethodType.Setter)]
    public static class Pawn_OutfitTracker_CurrentApparelPolicy_Setter_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn == null || AutoArmMod.settings?.modEnabled != true)
                return;

            if (!___pawn.IsColonist || !___pawn.Spawned || ___pawn.Dead || ___pawn.Drafted)
                return;

            // Don't interfere if pawn is hauling something
            if (JobGiverHelpers.IsHaulingJob(___pawn))
            {
                // Pawn is hauling
                return;
            }

            // Check primary weapon
            if (___pawn.equipment?.Primary != null && ___pawn.jobs != null)
            {
                // Don't interfere with sidearm upgrades in progress
                if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true &&
                    DroppedItemTracker.IsSimpleSidearmsSwapInProgress(___pawn))
                {
                    // Sidearm upgrade in progress
                    return;
                }

                // Don't force temporary colonists (quest pawns) to drop their weapons
                if (JobGiverHelpers.IsTemporaryColonist(___pawn))
                {
                    // Temporary colonist
                    return;
                }

                var filter = ___pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null)
                {
                    // Only check filter for proper weapons that appear in the UI
                    if (!WeaponValidation.IsProperWeapon(___pawn.equipment.Primary))
                    {
                        // Not a proper weapon (wood log, beer, etc.) - don't apply filter rules
                        // Not a proper weapon
                        return;
                    }

                    bool weaponAllowed = filter.Allows(___pawn.equipment.Primary);
                    // Removed spammy outfit filter check log

                    if (!weaponAllowed)
                    {
                        // Check if this weapon is forced - if so, keep it equipped
                        if (ForcedWeaponHelper.IsForced(___pawn, ___pawn.equipment.Primary))
                        {
                            // ALWAYS keep forced weapons regardless of outfit filter
                            // Keeping forced weapon
                            return; // Don't drop forced weapons
                        }

                        // Note: Bonded weapons are NOT exempt from outfit filters
                        // If the outfit says no plasma swords, even bonded ones get dropped

                        // Check if pawn is in a ritual - don't drop ritual items during ceremonies
                        if (ValidationHelper.IsInRitual(___pawn))
                        {
                            // Pawn in ritual
                            return; // Don't drop items during rituals
                        }

                        ForcedWeaponHelper.ClearForced(___pawn);

                        // Mark weapon as dropped to prevent immediate re-pickup
                        DroppedItemTracker.MarkAsDropped(___pawn.equipment.Primary, 600); // 10 second cooldown

                        var dropJob = new Job(JobDefOf.DropEquipment, ___pawn.equipment.Primary);
                        ___pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"{___pawn.LabelShort}: Outfit changed, dropping {___pawn.equipment.Primary.Label}");
                        }

                        if (PawnUtility.ShouldSendNotificationAbout(___pawn) && AutoArmMod.settings?.showNotifications == true)
                        {
                            Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                ___pawn.LabelShort.CapitalizeFirst(),
                                ___pawn.equipment.Primary.Label ?? ___pawn.equipment.Primary.def?.label ?? "weapon"
                            ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                        }
                    }
                }
            }

            // Also check sidearms when outfit changes
            CheckAndDropDisallowedSidearms(___pawn);

            // Schedule an additional check next tick to ensure all weapons are dropped
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (___pawn != null && ___pawn.Spawned && !___pawn.Dead)
                {
                    CheckAndDropDisallowedSidearms(___pawn);
                }
            });

            if (Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.ContainsKey(___pawn))
            {
                Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.Remove(___pawn);
            }
        }

        public static void CheckAndDropDisallowedSidearms(Pawn pawn)
        {
            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (!SimpleSidearmsCompat.IsLoaded() || pawn?.inventory?.innerContainer == null)
                return;

            // Also validate if we just finished a Pick Up and Haul job
            if (PickUpAndHaulCompat.IsLoaded())
            {
                PickUpAndHaulCompat.ValidateInventoryWeapons(pawn);
            }

            // Don't interfere if pawn is hauling something
            if (JobGiverHelpers.IsHaulingJob(pawn))
            {
                return;
            }

            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter == null)
                return;

            var weaponsToCheck = pawn.inventory.innerContainer
                .OfType<ThingWithComps>()
                .Where(t => t.def.IsWeapon)
                .ToList();

            foreach (var weapon in weaponsToCheck)
            {
                // Only check filter for proper weapons that appear in the UI
                if (!WeaponValidation.IsProperWeapon(weapon))
                {
                    continue; // Skip non-proper weapons like wood logs, beer bottles, etc.
                }

                if (!filter.Allows(weapon))
                {
                    // Check if this is a forced weapon - don't drop forced weapons
                    if (ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                    {
                        continue;
                    }

                    // Note: Bonded weapons are NOT exempt from outfit filters

                    // Drop the weapon
                    Thing droppedWeapon;
                    if (pawn.inventory.innerContainer.TryDrop(weapon, pawn.Position, pawn.Map,
                        ThingPlaceMode.Near, out droppedWeapon))
                    {
                        if (droppedWeapon != null)
                        {
                            // Mark it as dropped so it won't be immediately picked up again
                            SimpleSidearmsCompat.MarkWeaponAsRecentlyDropped(droppedWeapon as ThingWithComps);
                            // Additional marking with longer cooldown to prevent pickup loops
                            DroppedItemTracker.MarkAsDropped(droppedWeapon, 1200); // 20 seconds

                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"{pawn.LabelShort}: Dropped disallowed sidearm - {weapon.Label}");
                            }
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(ThingFilter), "SetDisallowAll")]
    public static class ThingFilter_SetDisallowAll_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance)
        {
            if (__instance == null || AutoArmMod.settings?.modEnabled != true)
                return;

            if (Current.Game == null || Find.Maps == null)
                return;

            var pawnsToDropWeapons = new List<(Pawn pawn, ThingWithComps weapon)>();

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn == null || pawn.Drafted || pawn.jobs == null)
                        continue;

                    // Don't interfere if pawn is hauling something
                    if (JobGiverHelpers.IsHaulingJob(pawn))
                    {
                        continue;
                    }

                    if (pawn.outfits?.CurrentApparelPolicy?.filter == __instance &&
                        pawn.equipment?.Primary != null &&
                        WeaponValidation.IsProperWeapon(pawn.equipment.Primary)) // Only drop proper weapons that appear in filter UI
                    {
                        pawnsToDropWeapons.Add((pawn, pawn.equipment.Primary));
                    }
                }
            }

            if (pawnsToDropWeapons.Count > 0)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    foreach (var (pawn, weapon) in pawnsToDropWeapons)
                    {
                        if (pawn?.equipment?.Primary == weapon && !pawn.Drafted && pawn.jobs != null)
                        {
                            // Don't interfere with sidearm upgrades in progress
                            if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true &&
                                DroppedItemTracker.IsSimpleSidearmsSwapInProgress(pawn))
                            {
                                // Sidearm upgrade in progress
                                continue;
                            }

                            // Don't force temporary colonists (quest pawns) to drop their weapons
                            if (JobGiverHelpers.IsTemporaryColonist(pawn))
                            {
                                // Temporary colonist
                                continue;
                            }

                            // Check if this weapon is forced - if so, keep it equipped
                            if (ForcedWeaponHelper.IsForced(pawn, weapon))
                            {
                                // Keeping forced weapon
                                continue; // Skip this pawn, don't drop forced weapons
                            }

                            // Note: Bonded weapons are NOT exempt from outfit filters
                            // If the outfit says no weapons, even bonded ones get dropped

                            // Check if pawn is in a ritual - don't drop ritual items during ceremonies
                            if (ValidationHelper.IsInRitual(pawn))
                            {
                                // Pawn in ritual
                                continue; // Don't drop items during rituals
                            }

                            ForcedWeaponHelper.ClearForced(pawn);

                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            // All items disallowed
                        }
                    }
                });
            }
        }
    }
}

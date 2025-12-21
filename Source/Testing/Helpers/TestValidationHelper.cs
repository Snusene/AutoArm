using AutoArm.Compatibility;
using AutoArm.Helpers;
using AutoArm.Logging;
using RimWorld;
using Verse;

namespace AutoArm.Testing.Helpers
{
    /// <summary>
    /// Helper class for validating test conditions
    /// </summary>
    public static class TestValidationHelper
    {
        /// <summary>
        /// Check if a pawn is valid for auto-equip
        /// </summary>
        public static bool IsValidPawn(Pawn pawn, out string reason)
        {
            reason = "Valid";

            if (pawn == null)
            {
                reason = "Pawn is null";
                return false;
            }

            if (!pawn.Spawned)
            {
                reason = "Pawn not spawned";
                return false;
            }

            if (pawn.Dead)
            {
                reason = "Pawn is dead";
                return false;
            }

            if (pawn.Downed)
            {
                reason = "Pawn is downed";
                return false;
            }

            if (pawn.IsPrisoner || pawn.IsPrisonerOfColony)
            {
                reason = "Is a prisoner";
                return false;
            }

            bool isColonistOrSlave = pawn.IsColonist || (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony);
            if (!isColonistOrSlave)
            {
                reason = "Not a colonist or slave";
                return false;
            }

            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                reason = "Cannot do violence";
                return false;
            }

            if (pawn.Drafted)
            {
                reason = "Pawn is drafted";
                return false;
            }

            if (pawn.InMentalState)
            {
                reason = "Pawn in mental state";
                return false;
            }

            if (pawn.equipment == null)
            {
                reason = "No equipment tracker";
                return false;
            }

            if (pawn.Map == null)
            {
                reason = "Pawn has no map";
                return false;
            }

            if (AutoArmMod.settings != null && !AutoArmMod.settings.modEnabled)
            {
                reason = "AutoArm mod is disabled";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Force refresh the weapon cache for testing
        /// </summary>
        public static void ForceWeaponCacheRefresh(Map map)
        {
            if (map == null) return;

            try
            {
                Caching.WeaponCacheManager.ClearAllCaches();

                var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
                foreach (var weapon in weapons)
                {
                    if (weapon is ThingWithComps twc && twc.Spawned && !twc.Destroyed)
                    {
                        Caching.WeaponCacheManager.AddWeaponToCache(twc);
                    }
                }

                AutoArmLogger.Debug(() => $"[TEST] Force refreshed weapon cache for map {map.uniqueID} - {weapons.Count} weapons");
            }
            catch (System.Exception e)
            {
                AutoArmLogger.Error("[TEST] Error force refreshing weapon cache", e);
            }
        }

        public static void EnsureWeaponRegistered(ThingWithComps weapon)
        {
            if (weapon == null || !weapon.Spawned || weapon.Destroyed) return;

            try
            {
                Caching.WeaponCacheManager.AddWeaponToCache(weapon);


                weapon.SetForbidden(false, false);

                AutoArmLogger.Debug(() => $"[TEST] Ensured weapon {weapon.Label} is registered for testing");
            }
            catch (System.Exception e)
            {
                AutoArmLogger.Error($"[TEST] Error registering weapon {weapon?.Label}", e);
            }
        }

        /// <summary>
        /// Check if SimpleSidearms reflection is working
        /// </summary>
        public static bool IsSimpleSidearmsWorking()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return false;

            if (SimpleSidearmsCompat.ReflectionFailed)
            {
                AutoArmLogger.Debug(() => "[TEST] SimpleSidearms is loaded but reflection failed");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Force mark a weapon as forced (bypassing SimpleSidearms if needed)
        /// </summary>
        public static void ForceMarkWeaponAsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null) return;

            try
            {
                ForcedWeapons.SetForced(pawn, weapon);

                if (SimpleSidearmsCompat.IsLoaded && SimpleSidearmsCompat.ReflectionFailed)
                {
                    AutoArmLogger.Debug(() => $"[TEST] Force marked weapon {weapon.Label} for {pawn.Name} using fallback tracking");
                }
            }
            catch (System.Exception e)
            {
                AutoArmLogger.Error($"[TEST] Error force marking weapon as forced", e);
            }
        }
    }
}

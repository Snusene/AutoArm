
using AutoArm.Logging;
using AutoArm.Weapons;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Track user-forced weapons
    /// </summary>
    public static class ForcedWeapons
    {
        private static Dictionary<Pawn, HashSet<int>> forcedWeaponIds = new Dictionary<Pawn, HashSet<int>>();

        private static Dictionary<Pawn, HashSet<ThingDef>> forcedWeaponDefs = new Dictionary<Pawn, HashSet<ThingDef>>();

        private static Dictionary<Pawn, ThingWithComps> forcedPrimaryWeapon = new Dictionary<Pawn, ThingWithComps>();

        public static Dictionary<Pawn, Thing> GetAllForcedWeapons()
        {
            var result = new Dictionary<Pawn, Thing>();
            foreach (var kvp in forcedPrimaryWeapon)
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }

        /// <summary>
        /// Mark a specific weapon instance as forced for a pawn
        /// </summary>
        public static void SetForced(Pawn pawn, ThingWithComps weapon, string reason = null, bool log = true)
        {
            if (pawn == null || weapon == null)
                return;

            if (!WeaponValidation.IsWeapon(weapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Ignoring force-equip for non-weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)}");
                }
                return;
            }

            forcedPrimaryWeapon[pawn] = weapon;

            if (!forcedWeaponIds.ContainsKey(pawn))
                forcedWeaponIds[pawn] = new HashSet<int>();
            forcedWeaponIds[pawn].Add(weapon.thingIDNumber);

            if (!forcedWeaponDefs.ContainsKey(pawn))
                forcedWeaponDefs[pawn] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawn].Add(weapon.def);

            if (log && AutoArmMod.settings?.debugLogging == true)
            {
                var pawnName = pawn.Name?.ToStringShort ?? "Unknown";
                var suffix = string.IsNullOrEmpty(reason) ? string.Empty : $" ({reason})";
                AutoArmLogger.Debug(() => $"[{pawnName}] Force-equipped weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)} (ID: {weapon.thingIDNumber}){suffix}");
            }
        }

        /// <summary>
        /// Clear only the forced primary weapon reference (not all forced weapons)
        /// </summary>
        public static void ClearForcedPrimary(Pawn pawn)
        {
            if (pawn == null)
                return;

            ThingWithComps weapon = null;
            ThingDef weaponDefToCheck = null;
            if (forcedPrimaryWeapon.TryGetValue(pawn, out var forcedWeapon) && forcedWeapon != null)
            {
                weapon = forcedWeapon;
                weaponDefToCheck = forcedWeapon.def;
            }

            forcedPrimaryWeapon.Remove(pawn);

            if (weapon != null && forcedWeaponIds.ContainsKey(pawn))
            {
                forcedWeaponIds[pawn].Remove(weapon.thingIDNumber);
                if (forcedWeaponIds[pawn].Count == 0)
                    forcedWeaponIds.Remove(pawn);
            }

            if (weaponDefToCheck != null && forcedWeaponDefs.ContainsKey(pawn))
            {
                bool stillHasWeaponOfType = false;

                if (pawn.equipment?.Primary?.def == weaponDefToCheck)
                {
                    stillHasWeaponOfType = true;
                }

                if (!stillHasWeaponOfType && pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps weaponInInventory && weaponInInventory.def == weaponDefToCheck)
                        {
                            stillHasWeaponOfType = true;
                            break;
                        }
                    }
                }

                if (!stillHasWeaponOfType)
                {
                    forcedWeaponDefs[pawn].Remove(weaponDefToCheck);
                    if (forcedWeaponDefs[pawn].Count == 0)
                    {
                        forcedWeaponDefs.Remove(pawn);
                    }
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Removed forced weapon type {AutoArmLogger.GetDefLabel(weaponDefToCheck)} (no longer has any)");
                    }
                }
            }
        }

        /// <summary>
        /// Clear forced weapons
        /// </summary>
        public static void ClearForced(Pawn pawn)
        {
            if (pawn == null)
                return;

            int count = 0;
            if (forcedWeaponIds.ContainsKey(pawn))
            {
                count = forcedWeaponIds[pawn].Count;
                forcedWeaponIds.Remove(pawn);
            }

            if (forcedWeaponDefs.ContainsKey(pawn))
            {
                forcedWeaponDefs.Remove(pawn);
            }

            forcedPrimaryWeapon.Remove(pawn);

            if (count > 0)
            {
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared {count} forced weapon{(count == 1 ? "" : "s")}");
            }
        }

        /// <summary>
        /// Event-driven removal when pawn dies/destroyed (no logging)
        /// </summary>
        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;
            forcedWeaponIds.Remove(pawn);
            forcedWeaponDefs.Remove(pawn);
            forcedPrimaryWeapon.Remove(pawn);
        }

        public static bool IsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            if (forcedWeaponIds.ContainsKey(pawn) && forcedWeaponIds[pawn].Contains(weapon.thingIDNumber))
                return true;

            if (forcedPrimaryWeapon.TryGetValue(pawn, out var forced) && forced == weapon)
                return true;

            return false;
        }

        public static ThingWithComps ForcedPrimary(Pawn pawn)
        {
            if (pawn == null)
                return null;

            forcedPrimaryWeapon.TryGetValue(pawn, out var weapon);
            return weapon;
        }

        public static HashSet<ThingDef> ForcedDefs(Pawn pawn)
        {
            if (pawn == null)
                return new HashSet<ThingDef>();

            return forcedWeaponDefs.ContainsKey(pawn) ?
                new HashSet<ThingDef>(forcedWeaponDefs[pawn]) :
                new HashSet<ThingDef>();
        }

        /// <summary>
        /// Force a specific sidearm instance
        /// </summary>
        public static void AddSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            if (!WeaponValidation.IsWeapon(weapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Ignoring force-equip sidearm for non-weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)}");
                }
                return;
            }

            if (!forcedWeaponIds.ContainsKey(pawn))
                forcedWeaponIds[pawn] = new HashSet<int>();
            forcedWeaponIds[pawn].Add(weapon.thingIDNumber);

            if (!forcedWeaponDefs.ContainsKey(pawn))
                forcedWeaponDefs[pawn] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawn].Add(weapon.def);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Force-equipped sidearm: {AutoArmLogger.GetWeaponLabelLower(weapon)} (ID: {weapon.thingIDNumber})");
            }
        }

        /// <summary>
        /// Remove a specific forced weapon instance
        /// </summary>
        public static void RemoveForcedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            HashSet<int> weaponIdSet;
            if (forcedWeaponIds.TryGetValue(pawn, out weaponIdSet))
            {
                weaponIdSet.Remove(weapon.thingIDNumber);
                if (weaponIdSet.Count == 0)
                {
                    forcedWeaponIds.Remove(pawn);
                    weaponIdSet = null;
                }
            }

            if (forcedPrimaryWeapon.TryGetValue(pawn, out var primary) && primary == weapon)
            {
                forcedPrimaryWeapon.Remove(pawn);
            }

            if (forcedWeaponDefs.TryGetValue(pawn, out var defSet))
            {
                bool hasOtherForcedOfSameType = false;

                if (weaponIdSet != null && weaponIdSet.Count > 0)
                {
                    var currentPrimary = pawn.equipment?.Primary;
                    if (currentPrimary != null && currentPrimary.def == weapon.def && weaponIdSet.Contains(currentPrimary.thingIDNumber))
                    {
                        hasOtherForcedOfSameType = true;
                    }

                    if (!hasOtherForcedOfSameType && pawn.inventory?.innerContainer != null)
                    {
                        var container = pawn.inventory.innerContainer;
                        for (int i = 0; i < container.Count; i++)
                        {
                            if (container[i] is ThingWithComps invWeapon && invWeapon.def == weapon.def && weaponIdSet.Contains(invWeapon.thingIDNumber))
                            {
                                hasOtherForcedOfSameType = true;
                                break;
                            }
                        }
                    }
                }

                if (!hasOtherForcedOfSameType && ForcedWeaponState.IsTrackingWeapon(pawn, weapon.def))
                {
                    hasOtherForcedOfSameType = true;
                }

                if (!hasOtherForcedOfSameType)
                {
                    defSet.Remove(weapon.def);
                    if (defSet.Count == 0)
                    {
                        forcedWeaponDefs.Remove(pawn);
                    }
                }
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Removed forced weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)} (ID: {weapon.thingIDNumber})");
            }
        }

        /// <summary>
        /// Cleanup invalid
        /// </summary>
        public static int Cleanup()
        {
            if (forcedWeaponIds.Count == 0 && forcedWeaponDefs.Count == 0 && forcedPrimaryWeapon.Count == 0)
                return 0;

            int removed = 0;

            var invalidIdPawns = ListPool<Pawn>.Get(forcedWeaponIds.Count);
            foreach (var pawn in forcedWeaponIds.Keys)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                    invalidIdPawns.Add(pawn);
            }

            foreach (var pawn in invalidIdPawns)
            {
                forcedWeaponIds.Remove(pawn);
                removed++;
            }
            ListPool<Pawn>.Return(invalidIdPawns);

            var invalidPawns = ListPool<Pawn>.Get(forcedWeaponDefs.Count);
            foreach (var pawn in forcedWeaponDefs.Keys)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                    invalidPawns.Add(pawn);
            }

            foreach (var pawn in invalidPawns)
            {
                forcedWeaponDefs.Remove(pawn);
                forcedPrimaryWeapon.Remove(pawn);
                removed++;
            }
            ListPool<Pawn>.Return(invalidPawns);

            var invalidWeaponPawns = ListPool<Pawn>.Get();
            foreach (var kvp in forcedPrimaryWeapon)
            {
                if (kvp.Value == null || kvp.Value.Destroyed)
                    invalidWeaponPawns.Add(kvp.Key);
            }

            foreach (var pawn in invalidWeaponPawns)
            {
                forcedPrimaryWeapon.Remove(pawn);
                removed++;
            }
            ListPool<Pawn>.Return(invalidWeaponPawns);

            var phantomForcedPawns = ListPool<Pawn>.Get();
            foreach (var kvp in forcedPrimaryWeapon)
            {
                var pawn = kvp.Key;
                var forcedWeapon = kvp.Value;

                if (pawn == null || pawn.Dead || !pawn.Spawned || forcedWeapon == null)
                    continue;

                bool hasWeaponAsPrimary = pawn.equipment?.Primary == forcedWeapon;
                bool hasWeaponInInventory = false;

                if (!hasWeaponAsPrimary && pawn.inventory?.innerContainer != null)
                {
                    hasWeaponInInventory = pawn.inventory.innerContainer.Contains(forcedWeapon);
                }

                if (!hasWeaponAsPrimary && !hasWeaponInInventory)
                {
                    phantomForcedPawns.Add(pawn);
                }
            }

            foreach (var pawn in phantomForcedPawns)
            {
                var phantomWeapon = forcedPrimaryWeapon[pawn];
                forcedPrimaryWeapon.Remove(pawn);

                if (phantomWeapon != null && forcedWeaponIds.ContainsKey(pawn))
                {
                    forcedWeaponIds[pawn].Remove(phantomWeapon.thingIDNumber);
                    if (forcedWeaponIds[pawn].Count == 0)
                    {
                        forcedWeaponIds.Remove(pawn);
                    }
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared phantom forced weapon (weapon not found on pawn)");
                }
                removed++;
            }
            ListPool<Pawn>.Return(phantomForcedPawns);

            return removed;
        }

        /// <summary>
        /// Transfer forced status between primary and sidearm
        /// </summary>
        public static void TransferForcedStatus(Pawn pawn, ThingWithComps fromWeapon, ThingWithComps toWeapon)
        {
            if (pawn == null || fromWeapon == null || toWeapon == null)
                return;

            if (!IsForced(pawn, fromWeapon))
                return;

            RemoveForcedWeapon(pawn, fromWeapon);
            SetForced(pawn, toWeapon);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Transferred forced status from {AutoArmLogger.GetWeaponLabelLower(fromWeapon)} to {AutoArmLogger.GetWeaponLabelLower(toWeapon)}");
            }
        }


        /// <summary>
        /// Get primary weapon save data (LEGACY FORMAT - NO LONGER SAVED)
        /// Backward compat with old saves
        /// New saves use GetForcedWeaponIds() instead (instance ID-based system).
        /// </summary>
        public static Dictionary<Pawn, ThingDef> GetSaveData()
        {
            PruneInvalidEntries();

            var result = new Dictionary<Pawn, ThingDef>();

            foreach (var kvp in forcedPrimaryWeapon)
            {
                var pawn = kvp.Key;
                var weapon = kvp.Value;

                if (!IsPawnValidForPersistence(pawn) || weapon == null || weapon.def == null)
                {
                    continue;
                }

                result[pawn] = weapon.def;
            }

            return result;
        }

        /// <summary>
        /// Forced IDs
        /// </summary>
        public static Dictionary<Pawn, List<int>> GetForcedWeaponIds()
        {
            PruneInvalidEntries();

            var result = new Dictionary<Pawn, List<int>>();

            foreach (var kvp in forcedWeaponIds)
            {
                var pawn = kvp.Key;
                var idSet = kvp.Value;

                if (!IsPawnValidForPersistence(pawn) || idSet == null || idSet.Count == 0)
                {
                    continue;
                }

                result[pawn] = idSet.Where(id => id != 0).ToList();
            }

            return result;
        }

        public static Dictionary<Pawn, List<ThingDef>> GetSidearmSaveData()
        {
            PruneInvalidEntries();

            var result = new Dictionary<Pawn, List<ThingDef>>();

            foreach (var kvp in forcedWeaponDefs)
            {
                var pawn = kvp.Key;
                var defs = kvp.Value;

                if (!IsPawnValidForPersistence(pawn) || defs == null || defs.Count == 0)
                {
                    continue;
                }

                var sanitized = defs.Where(def => def != null).ToList();
                if (sanitized.Count > 0)
                {
                    result[pawn] = sanitized;
                }
            }

            return result;
        }

        public static void LoadSaveData(Dictionary<Pawn, ThingDef> data)
        {
            if (data == null)
                return;

            forcedPrimaryWeapon.Clear();

            foreach (var kvp in data)
            {
                var pawn = kvp.Key;
                var def = kvp.Value;

                if (!IsPawnValidForPersistence(pawn) || def == null)
                {
                    continue;
                }

                if (!forcedWeaponDefs.ContainsKey(pawn))
                {
                    forcedWeaponDefs[pawn] = new HashSet<ThingDef>();
                }

                forcedWeaponDefs[pawn].Add(def);
            }

            PruneInvalidEntries();
        }

        /// <summary>
        /// Load forced weapon IDs from save
        /// </summary>
        public static void LoadForcedWeaponIds(Dictionary<Pawn, List<int>> data)
        {
            if (data == null)
                return;

            forcedWeaponIds.Clear();

            foreach (var kvp in data)
            {
                var pawn = kvp.Key;
                var ids = kvp.Value;

                if (!IsPawnValidForPersistence(pawn) || ids == null)
                {
                    continue;
                }

                var sanitized = new HashSet<int>();
                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    if (id != 0)
                    {
                        sanitized.Add(id);
                    }
                }

                if (sanitized.Count > 0)
                {
                    forcedWeaponIds[pawn] = sanitized;
                }
            }

            PruneInvalidEntries();
        }

        /// <summary>
        /// Add forced def
        /// </summary>
        public static void AddForcedWeaponDef(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return;
            if (!forcedWeaponDefs.ContainsKey(pawn))
                forcedWeaponDefs[pawn] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawn].Add(weaponDef);
        }

        /// <summary>
        /// Remove a forced weapon definition for a pawn (def-level sync)
        /// </summary>
        public static void RemoveForcedWeaponDef(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return;
            if (forcedWeaponDefs.ContainsKey(pawn))
            {
                forcedWeaponDefs[pawn].Remove(weaponDef);
                if (forcedWeaponDefs[pawn].Count == 0)
                    forcedWeaponDefs.Remove(pawn);
            }
        }

        public static void LoadSidearmSaveData(Dictionary<Pawn, HashSet<ThingDef>> data)
        {
            if (data == null)
                return;

            forcedWeaponDefs.Clear();

            foreach (var kvp in data)
            {
                var pawn = kvp.Key;
                var defs = kvp.Value;

                if (!IsPawnValidForPersistence(pawn) || defs == null || defs.Count == 0)
                {
                    continue;
                }

                var sanitized = new HashSet<ThingDef>(defs.Where(def => def != null));
                if (sanitized.Count > 0)
                {
                    forcedWeaponDefs[pawn] = sanitized;
                }
            }

            PruneInvalidEntries();
        }



        internal static bool HasForcedWeaponDef(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return false;

            return forcedWeaponDefs.ContainsKey(pawn) && forcedWeaponDefs[pawn].Contains(weaponDef);
        }

        private static void PruneInvalidEntries()
        {
            if (forcedPrimaryWeapon.Count == 0 && forcedWeaponDefs.Count == 0 && forcedWeaponIds.Count == 0)
            {
                return;
            }

            var invalidPawns = new HashSet<Pawn>();

            foreach (var kvp in forcedPrimaryWeapon)
            {
                if (!IsPawnValidForPersistence(kvp.Key) || kvp.Value == null || kvp.Value.Destroyed)
                {
                    invalidPawns.Add(kvp.Key);
                }
            }

            foreach (var kvp in forcedWeaponDefs)
            {
                if (!IsPawnValidForPersistence(kvp.Key) || kvp.Value == null)
                {
                    invalidPawns.Add(kvp.Key);
                    continue;
                }

                kvp.Value.RemoveWhere(def => def == null);
                if (kvp.Value.Count == 0)
                {
                    invalidPawns.Add(kvp.Key);
                }
            }

            foreach (var kvp in forcedWeaponIds)
            {
                if (!IsPawnValidForPersistence(kvp.Key) || kvp.Value == null)
                {
                    invalidPawns.Add(kvp.Key);
                    continue;
                }

                kvp.Value.RemoveWhere(id => id == 0);
                if (kvp.Value.Count == 0)
                {
                    invalidPawns.Add(kvp.Key);
                }
            }

            if (invalidPawns.Count == 0)
            {
                return;
            }

            foreach (var pawn in invalidPawns)
            {
                forcedPrimaryWeapon.Remove(pawn);
                forcedWeaponDefs.Remove(pawn);
                forcedWeaponIds.Remove(pawn);
            }
        }

        private static bool IsPawnValidForPersistence(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.Discarded || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            return true;
        }
    }
}

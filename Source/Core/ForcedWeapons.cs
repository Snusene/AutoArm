
using AutoArm.Helpers;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm
{
    internal static class ForcedWeapons
    {
        private static readonly Dictionary<int, HashSet<int>> forcedWeaponIds = new Dictionary<int, HashSet<int>>();

        private static readonly Dictionary<int, HashSet<ThingDef>> forcedWeaponDefs = new Dictionary<int, HashSet<ThingDef>>();

        private static readonly Dictionary<int, int> forcedPrimaryWeapon = new Dictionary<int, int>();

        public static Dictionary<Pawn, Thing> GetAllForcedWeapons()
        {
            var result = new Dictionary<Pawn, Thing>();
            if (forcedPrimaryWeapon.Count == 0)
                return result;

            var idToPawn = BuildPawnIdLookup();
            foreach (var kvp in forcedPrimaryWeapon)
            {
                if (!idToPawn.TryGetValue(kvp.Key, out var pawn) || pawn == null)
                    continue;

                int weaponId = kvp.Value;
                var primary = pawn.equipment?.Primary;
                if (primary != null && primary.thingIDNumber == weaponId)
                {
                    result[pawn] = primary;
                    continue;
                }

                var inv = pawn.inventory?.innerContainer;
                if (inv != null)
                {
                    for (int i = 0; i < inv.Count; i++)
                    {
                        if (inv[i] is ThingWithComps twc && twc.thingIDNumber == weaponId)
                        {
                            result[pawn] = twc;
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public static void SetForced(Pawn pawn, ThingWithComps weapon, string reason = null, bool log = true)
        {
            if (pawn == null || weapon == null)
                return;

            if (!Validation.IsWeapon(weapon))
            {
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Ignoring force-equip for non-weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)}");
                return;
            }

            int pawnId = pawn.thingIDNumber;
            int weaponId = weapon.thingIDNumber;

            if (forcedPrimaryWeapon.TryGetValue(pawnId, out var priorId)
                && priorId != weaponId
                && forcedWeaponIds.TryGetValue(pawnId, out var priorIds))
            {
                priorIds.Remove(priorId);
            }

            forcedPrimaryWeapon[pawnId] = weaponId;

            if (!forcedWeaponIds.ContainsKey(pawnId))
                forcedWeaponIds[pawnId] = new HashSet<int>();
            forcedWeaponIds[pawnId].Add(weaponId);

            if (!forcedWeaponDefs.ContainsKey(pawnId))
                forcedWeaponDefs[pawnId] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawnId].Add(weapon.def);

            if (log && AutoArmMod.settings?.debugLogging == true)
            {
                var pawnName = pawn.Name?.ToStringShort ?? "Unknown";
                var suffix = string.IsNullOrEmpty(reason) ? string.Empty : $" ({reason})";
                AutoArmLogger.Debug(() => $"[{pawnName}] Force-equipped weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)} (ID: {weaponId}){suffix}");
            }
        }

        public static void ClearForcedPrimary(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;

            int weaponId = 0;
            ThingDef weaponDefToCheck = null;
            bool hadEntry = forcedPrimaryWeapon.TryGetValue(pawnId, out weaponId);
            if (hadEntry)
            {
                var primary = pawn.equipment?.Primary;
                if (primary != null && primary.thingIDNumber == weaponId)
                {
                    weaponDefToCheck = primary.def;
                }
                else if (pawn.inventory?.innerContainer != null)
                {
                    var inv = pawn.inventory.innerContainer;
                    for (int i = 0; i < inv.Count; i++)
                    {
                        if (inv[i] is ThingWithComps twc && twc.thingIDNumber == weaponId)
                        {
                            weaponDefToCheck = twc.def;
                            break;
                        }
                    }
                }
            }

            forcedPrimaryWeapon.Remove(pawnId);

            if (hadEntry && forcedWeaponIds.ContainsKey(pawnId))
            {
                forcedWeaponIds[pawnId].Remove(weaponId);
                if (forcedWeaponIds[pawnId].Count == 0)
                    forcedWeaponIds.Remove(pawnId);
            }

            if (weaponDefToCheck != null && forcedWeaponDefs.ContainsKey(pawnId))
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
                    forcedWeaponDefs[pawnId].Remove(weaponDefToCheck);
                    if (forcedWeaponDefs[pawnId].Count == 0)
                    {
                        forcedWeaponDefs.Remove(pawnId);
                    }
                    AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Removed forced weapon type {AutoArmLogger.GetDefLabel(weaponDefToCheck)} (no longer has any)");
                }
            }
        }

        public static void ClearForced(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;

            int count = 0;
            if (forcedWeaponIds.ContainsKey(pawnId))
            {
                count = forcedWeaponIds[pawnId].Count;
                forcedWeaponIds.Remove(pawnId);
            }

            if (forcedWeaponDefs.ContainsKey(pawnId))
            {
                forcedWeaponDefs.Remove(pawnId);
            }

            forcedPrimaryWeapon.Remove(pawnId);

            if (count > 0)
            {
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared {count} forced weapon{(count == 1 ? "" : "s")}");
            }
        }

        public static bool IsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            int pawnId = pawn.thingIDNumber;
            int weaponId = weapon.thingIDNumber;

            if (forcedWeaponIds.TryGetValue(pawnId, out var ids) && ids.Contains(weaponId))
                return true;

            if (forcedPrimaryWeapon.TryGetValue(pawnId, out int forcedId) && forcedId == weaponId)
                return true;

            return false;
        }

        public static bool IsForcedPrimary(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            return forcedPrimaryWeapon.TryGetValue(pawn.thingIDNumber, out int forcedId)
                && forcedId == weapon.thingIDNumber;
        }

        public static bool HasAny(Pawn pawn)
        {
            if (pawn == null) return false;
            int pawnId = pawn.thingIDNumber;
            return forcedWeaponIds.ContainsKey(pawnId) || forcedPrimaryWeapon.ContainsKey(pawnId);
        }

        public static void SetForced(Pawn pawn, ThingWithComps weapon, bool forced)
        {
            if (forced) SetForced(pawn, weapon);
            else RemoveForcedWeapon(pawn, weapon);
        }

        public static bool AllowedToAutomaticallyDrop(Pawn pawn, ThingWithComps weapon)
            => !IsForced(pawn, weapon);

        public static bool SomethingIsForced(Pawn pawn) => HasAny(pawn);

        public static void AddSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            if (!Validation.IsWeapon(weapon))
            {
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Ignoring force-equip sidearm for non-weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)}");
                return;
            }

            int pawnId = pawn.thingIDNumber;
            int weaponId = weapon.thingIDNumber;

            if (!forcedWeaponIds.ContainsKey(pawnId))
                forcedWeaponIds[pawnId] = new HashSet<int>();
            forcedWeaponIds[pawnId].Add(weaponId);

            if (!forcedWeaponDefs.ContainsKey(pawnId))
                forcedWeaponDefs[pawnId] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawnId].Add(weapon.def);

            AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Force-equipped sidearm: {AutoArmLogger.GetWeaponLabelLower(weapon)} (ID: {weaponId})");
        }

        public static void RemoveForcedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            int pawnId = pawn.thingIDNumber;
            int weaponId = weapon.thingIDNumber;

            HashSet<int> weaponIdSet;
            if (forcedWeaponIds.TryGetValue(pawnId, out weaponIdSet))
            {
                weaponIdSet.Remove(weaponId);
                if (weaponIdSet.Count == 0)
                {
                    forcedWeaponIds.Remove(pawnId);
                    weaponIdSet = null;
                }
            }

            if (forcedPrimaryWeapon.TryGetValue(pawnId, out int primaryId) && primaryId == weaponId)
            {
                forcedPrimaryWeapon.Remove(pawnId);
            }

            if (forcedWeaponDefs.TryGetValue(pawnId, out var defSet))
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
                        forcedWeaponDefs.Remove(pawnId);
                    }
                }
            }

            AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Removed forced weapon: {AutoArmLogger.GetWeaponLabelLower(weapon)} (ID: {weaponId})");
        }

        public static int Cleanup()
        {
            if (forcedWeaponIds.Count == 0 && forcedWeaponDefs.Count == 0 && forcedPrimaryWeapon.Count == 0)
                return 0;

            int removed = 0;

            var idToPawn = pawnIdLookupScratch;
            idToPawn.Clear();
            var neededIds = HashSetPool<int>.Get();
            try
            {
                foreach (var pawnId in forcedWeaponIds.Keys)
                    neededIds.Add(pawnId);
                foreach (var pawnId in forcedWeaponDefs.Keys)
                    neededIds.Add(pawnId);
                foreach (var pawnId in forcedPrimaryWeapon.Keys)
                    neededIds.Add(pawnId);

                ResolvePawnsByIds(neededIds, idToPawn);
            }
            finally
            {
                HashSetPool<int>.Return(neededIds);
            }

            var invalidIds = ListPool<int>.Get(forcedWeaponIds.Count);
            try
            {
                foreach (var pawnId in forcedWeaponIds.Keys)
                {
                    if (!idToPawn.TryGetValue(pawnId, out var pawn) || pawn == null || pawn.Destroyed || pawn.Dead)
                        invalidIds.Add(pawnId);
                }

                foreach (var pawnId in invalidIds)
                {
                    forcedWeaponIds.Remove(pawnId);
                    removed++;
                }
            }
            finally
            {
                ListPool<int>.Return(invalidIds);
            }

            var invalidDefIds = ListPool<int>.Get(forcedWeaponDefs.Count);
            try
            {
                foreach (var pawnId in forcedWeaponDefs.Keys)
                {
                    if (!idToPawn.TryGetValue(pawnId, out var pawn) || pawn == null || pawn.Destroyed || pawn.Dead)
                        invalidDefIds.Add(pawnId);
                }

                foreach (var pawnId in invalidDefIds)
                {
                    forcedWeaponDefs.Remove(pawnId);
                    forcedPrimaryWeapon.Remove(pawnId);
                    removed++;
                }
            }
            finally
            {
                ListPool<int>.Return(invalidDefIds);
            }

            var invalidWeaponIds = ListPool<int>.Get();
            try
            {
                foreach (var kvp in forcedPrimaryWeapon)
                {
                    if (!idToPawn.TryGetValue(kvp.Key, out var pawn) || pawn == null)
                    {
                        invalidWeaponIds.Add(kvp.Key);
                        continue;
                    }

                    int weaponId = kvp.Value;
                    bool weaponExists = false;

                    if (pawn.equipment?.Primary?.thingIDNumber == weaponId)
                        weaponExists = true;
                    else if (pawn.inventory?.innerContainer != null)
                    {
                        var inv = pawn.inventory.innerContainer;
                        for (int i = 0; i < inv.Count; i++)
                        {
                            if (inv[i] is ThingWithComps twc && twc.thingIDNumber == weaponId)
                            {
                                weaponExists = true;
                                break;
                            }
                        }
                    }

                    if (!weaponExists)
                        invalidWeaponIds.Add(kvp.Key);
                }

                foreach (var pawnId in invalidWeaponIds)
                {
                    forcedPrimaryWeapon.Remove(pawnId);
                    removed++;
                }
            }
            finally
            {
                ListPool<int>.Return(invalidWeaponIds);
            }

            var phantomForcedIds = ListPool<int>.Get();
            try
            {
                foreach (var kvp in forcedPrimaryWeapon)
                {
                    int pawnId = kvp.Key;
                    int weaponId = kvp.Value;

                    if (!idToPawn.TryGetValue(pawnId, out var pawn) || pawn == null || pawn.Dead || !pawn.Spawned)
                        continue;

                    bool hasWeaponAsPrimary = pawn.equipment?.Primary?.thingIDNumber == weaponId;
                    bool hasWeaponInInventory = false;

                    if (!hasWeaponAsPrimary && pawn.inventory?.innerContainer != null)
                    {
                        var inv = pawn.inventory.innerContainer;
                        for (int i = 0; i < inv.Count; i++)
                        {
                            if (inv[i] is ThingWithComps twc && twc.thingIDNumber == weaponId)
                            {
                                hasWeaponInInventory = true;
                                break;
                            }
                        }
                    }

                    if (!hasWeaponAsPrimary && !hasWeaponInInventory)
                    {
                        phantomForcedIds.Add(pawnId);
                    }
                }

                foreach (var pawnId in phantomForcedIds)
                {
                    int phantomWeaponId = forcedPrimaryWeapon[pawnId];
                    forcedPrimaryWeapon.Remove(pawnId);

                    if (forcedWeaponIds.ContainsKey(pawnId))
                    {
                        forcedWeaponIds[pawnId].Remove(phantomWeaponId);
                        if (forcedWeaponIds[pawnId].Count == 0)
                        {
                            forcedWeaponIds.Remove(pawnId);
                        }
                    }

                    if (idToPawn.TryGetValue(pawnId, out var pawn) && pawn != null)
                    {
                        AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared phantom forced weapon (weapon not found on pawn)");
                    }
                    removed++;
                }
            }
            finally
            {
                ListPool<int>.Return(phantomForcedIds);
            }

            return removed;
        }

        public static Dictionary<Pawn, List<int>> GetForcedWeaponIds()
        {
            PruneInvalidEntries();

            var result = new Dictionary<Pawn, List<int>>();
            if (forcedWeaponIds.Count == 0)
                return result;

            var idToPawn = BuildPawnIdLookup();

            foreach (var kvp in forcedWeaponIds)
            {
                if (!idToPawn.TryGetValue(kvp.Key, out var pawn) || !IsPawnValidForPersistence(pawn))
                    continue;

                var idSet = kvp.Value;
                if (idSet == null || idSet.Count == 0)
                    continue;

                result[pawn] = idSet.Where(id => id != 0).ToList();
            }

            return result;
        }

        public static Dictionary<Pawn, List<ThingDef>> GetSidearmSaveData()
        {
            PruneInvalidEntries();

            var result = new Dictionary<Pawn, List<ThingDef>>();
            if (forcedWeaponDefs.Count == 0)
                return result;

            var idToPawn = BuildPawnIdLookup();

            foreach (var kvp in forcedWeaponDefs)
            {
                if (!idToPawn.TryGetValue(kvp.Key, out var pawn) || !IsPawnValidForPersistence(pawn))
                    continue;

                var defs = kvp.Value;
                if (defs == null || defs.Count == 0)
                    continue;

                var sanitized = defs.Where(def => def != null).ToList();
                if (sanitized.Count > 0)
                {
                    result[pawn] = sanitized;
                }
            }

            return result;
        }

        internal static ThingWithComps FindHeldWeaponById(Pawn pawn, int weaponId)
        {
            if (pawn == null || weaponId == 0)
                return null;

            var primary = pawn.equipment?.Primary;
            if (primary != null && primary.thingIDNumber == weaponId)
                return primary;

            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
            {
                for (int i = 0; i < inv.Count; i++)
                {
                    if (inv[i] is ThingWithComps twc && twc.thingIDNumber == weaponId)
                        return twc;
                }
            }

            var carried = pawn.carryTracker?.CarriedThing as ThingWithComps;
            if (carried != null && carried.thingIDNumber == weaponId)
                return carried;

            return null;
        }

        public static void Reset()
        {
            forcedPrimaryWeapon.Clear();
            forcedWeaponDefs.Clear();
            forcedWeaponIds.Clear();
        }

        internal sealed class Snapshot
        {
            public readonly Dictionary<int, HashSet<int>> weaponIds = new Dictionary<int, HashSet<int>>();
            public readonly Dictionary<int, HashSet<ThingDef>> weaponDefs = new Dictionary<int, HashSet<ThingDef>>();
            public readonly Dictionary<int, int> primaries = new Dictionary<int, int>();
        }

        internal static Snapshot Capture()
        {
            var snap = new Snapshot();
            foreach (var kvp in forcedWeaponIds)
                snap.weaponIds[kvp.Key] = new HashSet<int>(kvp.Value);
            foreach (var kvp in forcedWeaponDefs)
                snap.weaponDefs[kvp.Key] = new HashSet<ThingDef>(kvp.Value);
            foreach (var kvp in forcedPrimaryWeapon)
                snap.primaries[kvp.Key] = kvp.Value;
            return snap;
        }

        internal static void Restore(Snapshot snap)
        {
            if (snap == null)
                return;

            forcedWeaponIds.Clear();
            forcedWeaponDefs.Clear();
            forcedPrimaryWeapon.Clear();

            foreach (var kvp in snap.weaponIds)
                forcedWeaponIds[kvp.Key] = new HashSet<int>(kvp.Value);
            foreach (var kvp in snap.weaponDefs)
                forcedWeaponDefs[kvp.Key] = new HashSet<ThingDef>(kvp.Value);
            foreach (var kvp in snap.primaries)
                forcedPrimaryWeapon[kvp.Key] = kvp.Value;
        }

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
                    forcedWeaponIds[pawn.thingIDNumber] = sanitized;
                }
            }

            PruneInvalidEntries();
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
                    forcedWeaponDefs[pawn.thingIDNumber] = sanitized;
                }
            }

            PruneInvalidEntries();
        }

        private static void PruneInvalidEntries()
        {
            if (forcedPrimaryWeapon.Count == 0 && forcedWeaponDefs.Count == 0 && forcedWeaponIds.Count == 0)
            {
                return;
            }

            var liveIds = new HashSet<int>();
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawns == null) continue;
                    foreach (var p in map.mapPawns.AllPawns)
                    {
                        if (p == null || p.Dead || p.Destroyed || p.Discarded) continue;
                        liveIds.Add(p.thingIDNumber);
                    }
                }
            }
            if (Find.WorldPawns != null)
            {
                foreach (var p in Find.WorldPawns.AllPawnsAlive)
                {
                    if (p != null) liveIds.Add(p.thingIDNumber);
                }
            }

            var invalidIds = new HashSet<int>();

            foreach (var kvp in forcedPrimaryWeapon)
            {
                if (!liveIds.Contains(kvp.Key))
                {
                    invalidIds.Add(kvp.Key);
                }
            }

            foreach (var kvp in forcedWeaponDefs)
            {
                if (!liveIds.Contains(kvp.Key) || kvp.Value == null)
                {
                    invalidIds.Add(kvp.Key);
                    continue;
                }

                kvp.Value.RemoveWhere(def => def == null);
                if (kvp.Value.Count == 0)
                {
                    invalidIds.Add(kvp.Key);
                }
            }

            foreach (var kvp in forcedWeaponIds)
            {
                if (!liveIds.Contains(kvp.Key) || kvp.Value == null)
                {
                    invalidIds.Add(kvp.Key);
                    continue;
                }

                kvp.Value.RemoveWhere(id => id == 0);
                if (kvp.Value.Count == 0)
                {
                    invalidIds.Add(kvp.Key);
                }
            }

            if (invalidIds.Count == 0)
            {
                return;
            }

            foreach (var pawnId in invalidIds)
            {
                forcedPrimaryWeapon.Remove(pawnId);
                forcedWeaponDefs.Remove(pawnId);
                forcedWeaponIds.Remove(pawnId);
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

        private static readonly Func<List<Pawn>> mapsCaravansTransitGetter = BindMapsCaravansTransitGetter();

        private static Func<List<Pawn>> BindMapsCaravansTransitGetter()
        {
            try
            {
                var finder = typeof(PawnsFinder);
                var prop = finder.GetProperty("AllMapsCaravansAndTravellingTransporters_Alive",
                               BindingFlags.Public | BindingFlags.Static)
                           ?? finder.GetProperty("AllMapsCaravansAndTravelingTransportPods_Alive",
                               BindingFlags.Public | BindingFlags.Static);

                var getter = prop?.GetGetMethod();
                if (getter != null && typeof(List<Pawn>).IsAssignableFrom(getter.ReturnType))
                {
                    var bound = (Func<List<Pawn>>)Delegate.CreateDelegate(typeof(Func<List<Pawn>>), getter, throwOnBindFailure: false);
                    if (bound != null)
                    {
                        AutoArmLogger.Debug(() => $"Pawn enumeration bound to PawnsFinder.{prop.Name}");
                        return bound;
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"PawnsFinder bind failed, using manual pawn enumeration: {e.Message}");
            }
            return null;
        }

        private static void ResolvePawnsByIds(HashSet<int> ids, Dictionary<int, Pawn> outPawns)
        {
            if (ids.Count == 0)
                return;

            List<Pawn> combined = null;
            if (mapsCaravansTransitGetter != null)
            {
                try { combined = mapsCaravansTransitGetter(); }
                catch (Exception e) { AutoArmLogger.WarnFileOnly($"PawnsFinder enumeration failed: {e.Message}"); }
            }

            if (combined != null)
            {
                for (int i = 0; i < combined.Count; i++)
                {
                    var p = combined[i];
                    if (p != null && ids.Contains(p.thingIDNumber))
                    {
                        outPawns[p.thingIDNumber] = p;
                        if (outPawns.Count == ids.Count)
                            return;
                    }
                }
            }
            else
            {
                if (Find.Maps != null)
                {
                    foreach (var gameMap in Find.Maps)
                    {
                        var all = gameMap?.mapPawns?.AllPawns;
                        if (all == null) continue;
                        for (int i = 0; i < all.Count; i++)
                        {
                            var p = all[i];
                            if (p != null && ids.Contains(p.thingIDNumber))
                            {
                                outPawns[p.thingIDNumber] = p;
                                if (outPawns.Count == ids.Count)
                                    return;
                            }
                        }
                    }
                }

                if (Find.World != null && Find.WorldObjects != null)
                {
                    var caravans = Find.WorldObjects.Caravans;
                    for (int i = 0; i < caravans.Count; i++)
                    {
                        var pawns = caravans[i]?.PawnsListForReading;
                        if (pawns == null) continue;
                        for (int j = 0; j < pawns.Count; j++)
                        {
                            var p = pawns[j];
                            if (p != null && ids.Contains(p.thingIDNumber))
                            {
                                outPawns[p.thingIDNumber] = p;
                                if (outPawns.Count == ids.Count)
                                    return;
                            }
                        }
                    }
                }
            }

            var worldPawns = Find.World?.worldPawns;
            if (worldPawns != null)
            {
                foreach (var p in worldPawns.AllPawnsAliveOrDead)
                {
                    if (p != null && ids.Contains(p.thingIDNumber))
                    {
                        outPawns[p.thingIDNumber] = p;
                        if (outPawns.Count == ids.Count)
                            return;
                    }
                }
            }
        }

        internal static void CollectKnownPawns(List<Pawn> outPawns)
        {
            outPawns.Clear();
            var seen = HashSetPool<int>.Get();
            try
            {
                List<Pawn> combined = null;
                if (mapsCaravansTransitGetter != null)
                {
                    try { combined = mapsCaravansTransitGetter(); }
                    catch (Exception e) { AutoArmLogger.WarnFileOnly($"PawnsFinder enumeration failed: {e.Message}"); }
                }

                if (combined != null)
                {
                    for (int i = 0; i < combined.Count; i++)
                    {
                        var p = combined[i];
                        if (p != null && seen.Add(p.thingIDNumber))
                            outPawns.Add(p);
                    }
                }
                else
                {
                    if (Find.Maps != null)
                    {
                        foreach (var gameMap in Find.Maps)
                        {
                            var all = gameMap?.mapPawns?.AllPawns;
                            if (all == null) continue;
                            for (int i = 0; i < all.Count; i++)
                            {
                                var p = all[i];
                                if (p != null && seen.Add(p.thingIDNumber))
                                    outPawns.Add(p);
                            }
                        }
                    }

                    if (Find.World != null && Find.WorldObjects != null)
                    {
                        var caravans = Find.WorldObjects.Caravans;
                        for (int i = 0; i < caravans.Count; i++)
                        {
                            var pawns = caravans[i]?.PawnsListForReading;
                            if (pawns == null) continue;
                            for (int j = 0; j < pawns.Count; j++)
                            {
                                var p = pawns[j];
                                if (p != null && seen.Add(p.thingIDNumber))
                                    outPawns.Add(p);
                            }
                        }
                    }
                }

                var worldPawns = Find.World?.worldPawns;
                if (worldPawns != null)
                {
                    foreach (var p in worldPawns.AllPawnsAliveOrDead)
                    {
                        if (p != null && seen.Add(p.thingIDNumber))
                            outPawns.Add(p);
                    }
                }
            }
            finally
            {
                HashSetPool<int>.Return(seen);
            }
        }

        private static readonly Dictionary<int, Pawn> pawnIdLookupScratch = new Dictionary<int, Pawn>(64);

        private static Dictionary<int, Pawn> BuildPawnIdLookup()
        {
            pawnIdLookupScratch.Clear();

            var pawns = ListPool<Pawn>.Get(256);
            try
            {
                CollectKnownPawns(pawns);
                for (int i = 0; i < pawns.Count; i++)
                {
                    pawnIdLookupScratch[pawns[i].thingIDNumber] = pawns[i];
                }
            }
            finally
            {
                ListPool<Pawn>.Return(pawns);
            }

            return pawnIdLookupScratch;
        }
    }
}

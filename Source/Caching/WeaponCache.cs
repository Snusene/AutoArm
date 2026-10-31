
using AutoArm.Definitions;
using AutoArm.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Caching
{
    internal static class WeaponCache
    {
        public sealed class AutoArmWeaponMapComponent : MapComponent
        {
            public int lastChangeDetectedTick = 0;

            public int lastNonForbiddenCheckTick = -1;

            public bool lastNonForbiddenResult = false;
            public int lastNonForbiddenCount = 0;
            public int lastAllForbiddenLoggedTick = -1;

            private struct TempReservation
            {
                public int PawnId;
                public int ExpiryTick;
            }

            private readonly Dictionary<int, TempReservation> _tempReservations
                = new Dictionary<int, TempReservation>();

            private readonly Action<Thing> added;
            private readonly Action<Thing> removed;
            private bool listening;

            public AutoArmWeaponMapComponent(Map map) : base(map)
            {
                added = Added;
                removed = Removed;
            }

            internal void InvalidateNonForbiddenCache()
            {
                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;
            }

            public void Reset()
            {
                _tempReservations.Clear();
                InvalidateNonForbiddenCache();
            }

            internal void Listen()
            {
                if (listening)
                    return;
                listening = true;

                var callbacks = map.thingListChangedCallbacks;
                callbacks.onThingAdded = (Action<Thing>)Delegate.Combine(callbacks.onThingAdded, added);
                callbacks.onThingRemoved = (Action<Thing>)Delegate.Combine(callbacks.onThingRemoved, removed);
            }

            private void Unlisten()
            {
                if (!listening)
                    return;
                listening = false;

                var callbacks = map.thingListChangedCallbacks;
                callbacks.onThingAdded = (Action<Thing>)Delegate.Remove(callbacks.onThingAdded, added);
                callbacks.onThingRemoved = (Action<Thing>)Delegate.Remove(callbacks.onThingRemoved, removed);
            }

            private void Added(Thing thing)
            {
                if (!(thing is ThingWithComps) || !Validation.IsWeapon(thing.def))
                    return;

                if (Testing.TestRunner.IsRunningTests)
                    testWeapons.Add(thing.thingIDNumber);
                Changed();
            }

            private void Removed(Thing thing)
            {
                if (!(thing is ThingWithComps) || !Validation.IsWeapon(thing.def))
                    return;

                _tempReservations.Remove(thing.thingIDNumber);
                Changed();
            }

            private void Changed()
            {
                lastChangeDetectedTick = Find.TickManager.TicksGame;
                InvalidateNonForbiddenCache();
            }

            public override void FinalizeInit()
            {
                base.FinalizeInit();
                Listen();
            }

            public override void MapRemoved()
            {
                base.MapRemoved();

                try
                {
                    Unlisten();
                    Reset();

                    if (map?.mapPawns != null)
                    {
                        foreach (var pawn in map.mapPawns.AllPawns)
                        {
                            if (pawn != null)
                                scoreCache.Remove(pawn.thingIDNumber);
                        }
                    }

                    if (map != null)
                        trackedMapIds.Remove(map.uniqueID);

                    AutoArmLogger.Debug(() => $"Cleared weapon cache for destroyed map {map?.uniqueID}");
                }
                catch (Exception ex)
                {
                    AutoArmLogger.WarnCleanup(ex, "MapRemoved");
                }
            }

            public override void ExposeData()
            {
                base.ExposeData();
            }

            public override void MapComponentTick()
            {
                base.MapComponentTick();

                var now = Find.TickManager.TicksGame;
                if (now % TempReservationTicks == 0 && _tempReservations.Count > 0)
                {
                    var toRemove = ListPool<int>.Get();
                    foreach (var kvp in _tempReservations)
                    {
                        if (kvp.Value.ExpiryTick <= now)
                        {
                            toRemove.Add(kvp.Key);
                        }
                    }

                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        _tempReservations.Remove(toRemove[i]);
                    }

                    ListPool<int>.Return(toRemove);
                }
            }

            public bool HasTempReservation(ThingWithComps weapon, int askingPawnId, int now)
            {
                TempReservation res;
                if (weapon == null) return false;
                int weaponId = weapon.thingIDNumber;
                if (_tempReservations.TryGetValue(weaponId, out res))
                {
                    if (res.ExpiryTick <= now)
                    {
                        _tempReservations.Remove(weaponId);
                        return false;
                    }
                    return res.PawnId != askingPawnId;
                }
                return false;
            }

            public void SetTempReservation(ThingWithComps weapon, int pawnId, int expiry)
            {
                if (weapon == null) return;
                _tempReservations[weapon.thingIDNumber] = new TempReservation { PawnId = pawnId, ExpiryTick = expiry };
            }

            public void ClearTempReservation(ThingWithComps weapon)
            {
                if (weapon == null) return;
                _tempReservations.Remove(weapon.thingIDNumber);
            }
        }

        private static AutoArmWeaponMapComponent GetMapComponent(Map map)
        {
            if (map == null || map.components == null)
                return null;

            try
            {
                var component = map.GetComponent<AutoArmWeaponMapComponent>();
                if (component == null)
                {
                    component = new AutoArmWeaponMapComponent(map);
                    map.components.Add(component);
                    component.Listen();
                    trackedMapIds.Add(map.uniqueID);

                    AutoArmLogger.Debug(() => $"[Cache] Created new weapon cache component for map {map.uniqueID}");
                }
                return component;
            }
            catch (Exception ex)
            {
                AutoArmLogger.WarnCleanup(ex, "MapComponentCreation");
                return null;
            }
        }

        private static readonly HashSet<int> trackedMapIds = new HashSet<int>();

        private static readonly Dictionary<int, Dictionary<int, ScoreEntry>> scoreCache =
            new Dictionary<int, Dictionary<int, ScoreEntry>>();

        private struct ScoreEntry
        {
            public float Score;
            public int LastUpdateTick;
        }

        private const int ScoreCacheDuration = Constants.StandardCacheDuration;
        private const int ScoreCacheJitterRange = 300; // +/- 150 ticks to stagger expiry
        private const int TempReservationTicks = 60;
        private const int NonForbiddenCheckCacheTicks = 300;

        // Per-def cache (not per-instance)
        private struct OutfitDefKey : IEquatable<OutfitDefKey>
        {
            public readonly int OutfitId;
            public readonly int DefShortHash;

            public OutfitDefKey(int outfitId, ThingDef def)
            {
                OutfitId = outfitId;
                DefShortHash = def?.shortHash ?? 0;
            }

            public bool Equals(OutfitDefKey other) => OutfitId == other.OutfitId && DefShortHash == other.DefShortHash;
            public override bool Equals(object obj) => obj is OutfitDefKey k && Equals(k);
            public override int GetHashCode() => (OutfitId * 397) ^ DefShortHash;
        }

        private static readonly Dictionary<OutfitDefKey, bool> outfitFilterCache = new Dictionary<OutfitDefKey, bool>();

        private static int lastScoreAddedTick = -1;
        private static int deadPawnCleanupCounter = 0;
        private const int DeadPawnCleanupInterval = 10;


        public static void EnsureCacheExists(Map map)
        {
            GetMapComponent(map);
        }

        private static readonly HashSet<int> testWeapons = new HashSet<int>();

        private static List<Thing> Weapons(Map map) => map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

        private static bool Visible(Thing thing) => !Testing.TestRunner.IsRunningTests || testWeapons.Contains(thing.thingIDNumber);

        public static bool HasAnyNonForbiddenWeapons(Map map)
        {
            var component = GetMapComponent(map);
            if (component == null)
                return false;

            int now = Find.TickManager.TicksGame;
            if (component.lastNonForbiddenCheckTick >= 0 &&
                now - component.lastNonForbiddenCheckTick < NonForbiddenCheckCacheTicks)
            {
                return component.lastNonForbiddenResult;
            }

            RecalculateNonForbidden(component, now);
            return component.lastNonForbiddenResult;
        }

        private static void RecalculateNonForbidden(AutoArmWeaponMapComponent component, int now)
        {
            var playerFaction = Find.FactionManager?.OfPlayer;
            int count = 0;
            int totalWeapons = 0;

            var things = Weapons(component.map);
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is ThingWithComps weapon) || !Validation.IsWeapon(weapon.def) || !Visible(weapon))
                    continue;

                totalWeapons++;

                if (weapon.Position.Fogged(component.map) || (playerFaction != null && weapon.IsForbidden(playerFaction)))
                    continue;

                count++;
            }

            component.lastNonForbiddenCount = count;
            component.lastNonForbiddenResult = count > 0;
            component.lastNonForbiddenCheckTick = now;

            if (AutoArmMod.settings?.debugLogging == true &&
                totalWeapons > 0 &&
                count == 0 &&
                component.lastAllForbiddenLoggedTick != now)
            {
                component.lastAllForbiddenLoggedTick = now;
                AutoArmLogger.Debug(() => $"[Cache] All {totalWeapons} weapons are forbidden, colonists will see 'No weapons found'");
            }
        }

        public static int GetCacheWeaponCount(Map map)
        {
            if (map == null) return 0;

            int count = 0;
            var things = Weapons(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is ThingWithComps && Validation.IsWeapon(things[i].def) && Visible(things[i]))
                    count++;
            }
            PerfMetrics.ReportCacheStats(count);
            return count;
        }

        public static int GetLastCacheChangeTick(Map map)
        {
            var component = GetMapComponent(map);
            return component?.lastChangeDetectedTick ?? 0;
        }


        public static void MarkCacheAsChanged(Map map)
        {
            var component = GetMapComponent(map);
            if (component != null)
            {
                component.lastChangeDetectedTick = Find.TickManager.TicksGame;
            }
        }


        public static void Collect(Map map, List<ThingWithComps> into, ApparelPolicy outfit = null, bool storageOnly = false)
        {
            if (map == null)
                return;

            var playerFaction = Find.FactionManager?.OfPlayer;
            if (outfit?.filter == null)
                outfit = null;

            var things = Weapons(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is ThingWithComps weapon) || !Validation.IsWeapon(weapon.def) || !Visible(weapon))
                    continue;
                if (weapon.Position.Fogged(map) || (playerFaction != null && weapon.IsForbidden(playerFaction)))
                    continue;
                if (storageOnly && !IsInStorageZone(weapon))
                    continue;
                if (outfit != null && !AllowedByOutfit(weapon, outfit))
                    continue;
                into.Add(weapon);
            }
        }

        public static bool AnyWeaponForOutfit(Map map, ApparelPolicy outfit)
        {
            if (map == null)
                return false;

            var playerFaction = Find.FactionManager?.OfPlayer;

            var things = Weapons(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is ThingWithComps weapon) || !Validation.IsWeapon(weapon.def) || !Visible(weapon))
                    continue;
                if (weapon.Position.Fogged(map) || (playerFaction != null && weapon.IsForbidden(playerFaction)))
                    continue;
                if (AllowedByOutfit(weapon, outfit))
                    return true;
            }
            return false;
        }

        private static bool AllowedByOutfit(ThingWithComps weapon, ApparelPolicy outfit)
        {
            var key = new OutfitDefKey(outfit.id, weapon.def);
            if (!outfitFilterCache.TryGetValue(key, out bool allowed))
            {
                allowed = outfit.filter.Allows(weapon.def);
                outfitFilterCache[key] = allowed;
            }
            return allowed && CheckQualityRequirements(weapon, outfit);
        }

        private static bool CheckQualityRequirements(ThingWithComps weapon, ApparelPolicy outfit)
        {
            var filter = outfit.filter;

            if (filter.AllowedQualityLevels != QualityRange.All)
            {
                if (weapon.TryGetQuality(out QualityCategory quality))
                {
                    if (!filter.AllowedQualityLevels.Includes(quality))
                        return false;
                }
            }


            return true;
        }

        public static float GetCachedScore(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return 0f;

            int pawnId = pawn.thingIDNumber;
            int weaponId = weapon.thingIDNumber;

            if (!scoreCache.TryGetValue(pawnId, out var pawnCache))
            {
                pawnCache = new Dictionary<int, ScoreEntry>();
                scoreCache[pawnId] = pawnCache;
            }

            int currentTick = Find.TickManager.TicksGame;

            if (pawnCache.TryGetValue(weaponId, out var entry))
            {
                // Jitter TTL
                int jitter = (((pawnId * 31) ^ weaponId) & int.MaxValue) % ScoreCacheJitterRange - (ScoreCacheJitterRange / 2);
                if (currentTick - entry.LastUpdateTick < ScoreCacheDuration + jitter)
                {
                    PerfMetrics.ReportCacheHit();
                    return entry.Score;
                }
            }

            float score = Scoring.GetTotalScore(pawn, weapon);
            pawnCache[weaponId] = new ScoreEntry { Score = score, LastUpdateTick = currentTick };
            lastScoreAddedTick = currentTick;

            return score;
        }

        public static void MarkPawnSkillsChanged(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;
            if (scoreCache.ContainsKey(pawnId))
            {
                scoreCache[pawnId].Clear();
            }
        }

        public static void InvalidateWeaponScores(ThingWithComps weapon)
        {
            if (weapon == null)
                return;

            int weaponId = weapon.thingIDNumber;

            foreach (var pawnCache in scoreCache.Values)
            {
                pawnCache.Remove(weaponId);
            }
        }

        public static void RemovePawnFromScoreCache(int pawnId)
        {
            scoreCache.Remove(pawnId);
        }

        public static int CleanupScoreCache(bool forceDeadPawnCleanup = false)
        {
            if (scoreCache.Count == 0)
                return 0;

            int removedCount = 0;
            int currentTick = Find.TickManager.TicksGame;

            // Safety net for missed removals
            deadPawnCleanupCounter++;
            bool shouldCleanupDeadPawns = forceDeadPawnCleanup || deadPawnCleanupCounter >= 50;
            if (shouldCleanupDeadPawns && scoreCache.Count > 0)
            {
                deadPawnCleanupCounter = 0;

                var validPawnIds = HashSetPool<int>.Get();
                try
                {
                    foreach (var map in Find.Maps)
                    {
                        if (map?.mapPawns == null) continue;
                        foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                        {
                            if (!pawn.Destroyed && !pawn.Dead)
                                validPawnIds.Add(pawn.thingIDNumber);
                        }
                    }

                    // Include caravan pawns
                    if (Find.World != null)
                    {
                        var caravans = Find.WorldObjects.Caravans;
                        for (int i = 0; i < caravans.Count; i++)
                        {
                            var pawns = caravans[i].PawnsListForReading;
                            for (int j = 0; j < pawns.Count; j++)
                            {
                                var pawn = pawns[j];
                                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                                    validPawnIds.Add(pawn.thingIDNumber);
                            }
                        }

                        var transporters = Find.WorldObjects.TravellingTransporters;
                        for (int i = 0; i < transporters.Count; i++)
                        {
                            var pawns = transporters[i].Pawns;
                            foreach (var pawn in pawns)
                            {
                                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                                    validPawnIds.Add(pawn.thingIDNumber);
                            }
                        }
                    }

                    var idsToRemove = ListPool<int>.Get();
                    try
                    {
                        foreach (var pawnId in scoreCache.Keys)
                        {
                            if (!validPawnIds.Contains(pawnId))
                                idsToRemove.Add(pawnId);
                        }

                        foreach (var id in idsToRemove)
                        {
                            removedCount += scoreCache[id]?.Count ?? 0;
                            scoreCache.Remove(id);
                        }
                    }
                    finally
                    {
                        ListPool<int>.Return(idsToRemove);
                    }
                }
                finally
                {
                    HashSetPool<int>.Return(validPawnIds);
                }
            }

            // Skip if nothing added
            if (lastScoreAddedTick < 0)
                return removedCount;

            bool shouldCleanupExpired = currentTick - lastScoreAddedTick >= ScoreCacheDuration;
            if (!shouldCleanupExpired)
                return removedCount;

            var keysToRemoveLater = ListPool<int>.Get();
            foreach (var pawnEntry in scoreCache)
            {
                // Skip empty entries
                if (pawnEntry.Value == null || pawnEntry.Value.Count == 0)
                {
                    keysToRemoveLater.Add(pawnEntry.Key);
                    continue;
                }

                var weaponsToRemove = ListPool<int>.Get();

                foreach (var kvp in pawnEntry.Value)
                {
                    if (currentTick - kvp.Value.LastUpdateTick >= ScoreCacheDuration)
                    {
                        weaponsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var weaponId in weaponsToRemove)
                {
                    pawnEntry.Value.Remove(weaponId);
                    removedCount++;
                }

                ListPool<int>.Return(weaponsToRemove);

                if (pawnEntry.Value.Count == 0)
                {
                    keysToRemoveLater.Add(pawnEntry.Key);
                }
            }

            foreach (var key in keysToRemoveLater)
            {
                scoreCache.Remove(key);
            }
            ListPool<int>.Return(keysToRemoveLater);

            if (scoreCache.Count > 400)
            {
                const int targetCount = 300;
                int evictCount = scoreCache.Count - targetCount;

                var sorted = ListPool<KeyValuePair<int, int>>.Get(scoreCache.Count);
                foreach (var pawnEntry in scoreCache)
                {
                    int mostRecentTick = 0;
                    foreach (var scoreEntry in pawnEntry.Value.Values)
                    {
                        if (scoreEntry.LastUpdateTick > mostRecentTick)
                            mostRecentTick = scoreEntry.LastUpdateTick;
                    }
                    sorted.Add(new KeyValuePair<int, int>(pawnEntry.Key, mostRecentTick));
                }
                sorted.Sort((a, b) => a.Value.CompareTo(b.Value));

                int evictedCount = 0;
                for (int i = 0; i < evictCount && i < sorted.Count; i++)
                {
                    int pawnId = sorted[i].Key;
                    int weaponCount = scoreCache[pawnId]?.Count ?? 0;
                    scoreCache.Remove(pawnId);
                    removedCount += weaponCount;
                    evictedCount++;
                }
                ListPool<KeyValuePair<int, int>>.Return(sorted);

                if (AutoArmMod.settings?.debugLogging == true && evictedCount > 0)
                {
                    AutoArmLogger.Debug(() => $"Evicted {evictedCount} stale pawn caches (LRU), now {scoreCache.Count}");
                }
            }

            if (scoreCache.Count > 500)
            {
                AutoArmLogger.WarnFileOnly($"Score cache over 500 limit ({scoreCache.Count} entries), clearing");
                scoreCache.Clear();
            }

            return removedCount;
        }


        public static void OnOutfitFilterChanged(ApparelPolicy outfit)
        {
            // Clear outfit cache
            if (outfit != null)
            {
                int outfitId = outfit.id;
                var keysToRemove = ListPool<OutfitDefKey>.Get();
                foreach (var key in outfitFilterCache.Keys)
                {
                    if (key.OutfitId == outfitId)
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                    outfitFilterCache.Remove(key);
                ListPool<OutfitDefKey>.Return(keysToRemove);
            }

            UI.StatusOverviewDataGatherer.InvalidateOutfitWeaponCount(outfit);

            var colonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists;

            var affectedMaps = HashSetPool<Map>.Get();
            try
            {
                foreach (var pawn in colonists)
                {
                    if (pawn?.outfits?.CurrentApparelPolicy == outfit)
                    {
                        MarkPawnSkillsChanged(pawn);
                        Jobs.JobGiver_PickUpBetterWeapon.InvalidatePawnValidationCache(pawn);

                        if (pawn.Map != null)
                        {
                            affectedMaps.Add(pawn.Map);
                        }
                    }
                }

                foreach (var map in affectedMaps)
                {
                    MarkCacheAsChanged(map);
                }
            }
            finally
            {
                HashSetPool<Map>.Return(affectedMaps);
            }
        }

        public static bool HasTemporaryReservation(ThingWithComps weapon, Pawn askingPawn)
        {
            if (weapon == null || askingPawn == null) return false;
            var map = weapon.Map;
            if (map == null) return false;

            var component = GetMapComponent(map);
            if (component == null) return false;

            int now = Find.TickManager.TicksGame;
            return component.HasTempReservation(weapon, askingPawn.thingIDNumber, now);
        }

        public static void SetTemporaryReservation(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || pawn == null) return;
            var map = weapon.Map;
            if (map == null) return;

            var component = GetMapComponent(map);
            if (component == null) return;

            int now = Find.TickManager.TicksGame;
            component.SetTempReservation(weapon, pawn.thingIDNumber, now + TempReservationTicks);
        }

        public static void ClearTemporaryReservation(ThingWithComps weapon)
        {
            if (weapon == null) return;
            var map = weapon.Map;
            if (map == null) return;

            var component = GetMapComponent(map);
            component?.ClearTempReservation(weapon);
        }

        public static void NotifyForbiddenStatusChanged(Thing thing)
        {
            var weapon = thing as ThingWithComps;
            if (weapon == null || weapon.Map == null)
                return;

            var component = GetMapComponent(weapon.Map);
            if (component == null)
                return;

            component.InvalidateNonForbiddenCache();
            component.lastChangeDetectedTick = Find.TickManager.TicksGame;
        }

        private static bool IsInStorageZone(ThingWithComps weapon)
        {
            if (weapon == null || !weapon.Spawned)
                return false;

            var map = weapon.Map;
            if (map == null)
                return false;

            var slotGroup = weapon.GetSlotGroup();
            if (slotGroup?.parent == null)
                return false;

            var parent = slotGroup.parent;
            if (!IsPlayerOwnedStorageParent(parent, map))
                return false;

            if (parent is Zone_Stockpile)
                return true;

            if (parent is Building_Storage)
                return true;

            var parentTypeName = parent.GetType().Name;
            if (!string.IsNullOrEmpty(parentTypeName) &&
                parentTypeName.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static bool IsPlayerOwnedStorageParent(ISlotGroupParent parent, Map map)
        {
            if (parent == null || map == null)
                return false;

            var playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
                return false;

            if (parent is Zone_Stockpile zone)
            {
                var zoneMap = zone.Map ?? map;
                var zoneFaction = zoneMap.ParentFaction;
                if (zoneFaction != null)
                    return zoneFaction == playerFaction;

                return zoneMap.IsPlayerHome;
            }

            if (parent is Thing thingParent)
            {
                if (!thingParent.Spawned || thingParent.Map != map)
                    return false;

                if (thingParent.Faction != null)
                    return thingParent.Faction == playerFaction;

                var mapFaction = thingParent.Map.ParentFaction;
                if (mapFaction != null)
                    return mapFaction == playerFaction;

                return thingParent.Map.IsPlayerHome;
            }

            return false;
        }


        public static void CleanupDestroyedMaps()
        {
            try
            {
                var maps = Find.Maps;
                if (maps == null || maps.Count == 0)
                {
                    trackedMapIds.Clear();
                    scoreCache.Clear();
                    return;
                }

                if (trackedMapIds.Count == 0)
                    return;

                // O(1) lookup
                var currentMapIds = HashSetPool<int>.Get();
                try
                {
                    for (int i = 0; i < maps.Count; i++)
                    {
                        if (maps[i] != null)
                            currentMapIds.Add(maps[i].uniqueID);
                    }

                    var toRemove = ListPool<int>.Get();
                    try
                    {
                        foreach (var mapId in trackedMapIds)
                        {
                            if (!currentMapIds.Contains(mapId))
                                toRemove.Add(mapId);
                        }

                        foreach (var mapId in toRemove)
                        {
                            trackedMapIds.Remove(mapId);
                        }
                    }
                    finally
                    {
                        ListPool<int>.Return(toRemove);
                    }
                }
                finally
                {
                    HashSetPool<int>.Return(currentMapIds);
                }

                // Clean score cache for removed maps
                CleanupScoreCache();
            }
            catch (Exception ex)
            {
                AutoArmLogger.WarnCleanup(ex, "CleanupDestroyedMaps");
            }
        }

        public static void ClearAllCaches()
        {
            scoreCache.Clear();
            trackedMapIds.Clear();
            outfitFilterCache.Clear();
            testWeapons.Clear();
            lastScoreAddedTick = -1;
            deadPawnCleanupCounter = 0;

            var maps = Find.Maps;
            if (maps == null)
                return;

            for (int i = 0; i < maps.Count; i++)
            {
                GetMapComponent(maps[i])?.Reset();
            }
        }

        public static void ClearScoreCache()
        {
            scoreCache.Clear();
            lastScoreAddedTick = -1;
            deadPawnCleanupCounter = 0;
        }

    }

    [Obsolete("Feature removed - stub exists only for save compatibility")]
    public sealed class WeaponCacheManager
    {
        public sealed class AutoArmWeaponMapComponent : MapComponent
        {
            private bool initialized;
            private int lastChangeTick;
            private int highWaterMark;

            public AutoArmWeaponMapComponent(Map map) : base(map) { }

            public override void ExposeData()
            {
                base.ExposeData();
                Scribe_Values.Look(ref initialized, "initialized", false);
                Scribe_Values.Look(ref lastChangeTick, "lastChangeTick", 0);
                Scribe_Values.Look(ref highWaterMark, "highWaterMark", 0);
            }
        }
    }
}

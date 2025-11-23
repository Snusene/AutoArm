
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace AutoArm.Caching
{
    public static class WeaponCacheManager
    {
        public const float CANNOT_EQUIP = -1f;

        public const float SCORE_EXPIRED = -2f;

        private static readonly Func<IEnumerable<Pawn>> GetAllColonists;

        static WeaponCacheManager()
        {
            var pawnsFinderType = typeof(PawnsFinder);
            PropertyInfo colonistsProperty = null;

            colonistsProperty = pawnsFinderType.GetProperty(
                "AllMapsCaravansAndTravellingTransporters_Alive_Colonists",
                BindingFlags.Public | BindingFlags.Static);

            if (colonistsProperty == null)
            {
                colonistsProperty = pawnsFinderType.GetProperty(
                    "AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists",
                    BindingFlags.Public | BindingFlags.Static);
            }

            if (colonistsProperty != null)
            {
                GetAllColonists = () => (IEnumerable<Pawn>)colonistsProperty.GetValue(null);
            }
            else
            {
                GetAllColonists = () =>
                {
                    var colonists = new List<Pawn>();
                    if (Find.Maps != null)
                    {
                        foreach (Map map in Find.Maps)
                        {
                            if (map?.mapPawns != null)
                            {
                                colonists.AddRange(map.mapPawns.FreeColonists);
                            }
                        }
                    }
                    return colonists;
                };

                AutoArmLogger.Warn("[AutoArm] Could not find PawnsFinder colonist property via reflection. Using fallback method.");
            }
        }

        public class AutoArmWeaponMapComponent : MapComponent
        {
            public HashSet<ThingWithComps> weapons = new HashSet<ThingWithComps>();

            public int lastChangeDetectedTick = 0;

            public int lastNonForbiddenCheckTick = -1;

            public bool lastNonForbiddenResult = false;
            public int lastNonForbiddenCount = 0;
            public int lastAllForbiddenLoggedTick = -1;

            private bool initialized = false;

            [Unsaved(false)]
            private HashSet<ThingWithComps> cachedWeaponsForEvents = new HashSet<ThingWithComps>();

            private struct TempReservation
            {
                public int PawnId;
                public int ExpiryTick;
            }

            private readonly Dictionary<ThingWithComps, TempReservation> _tempReservations
                = new Dictionary<ThingWithComps, TempReservation>();

            [Unsaved(false)]
            private Dictionary<int, List<ThingWithComps>> reservationExpirySchedule =
                new Dictionary<int, List<ThingWithComps>>();

            public void ResetCache()
            {
                weapons.Clear();
                initialized = false;
                _tempReservations.Clear();
                reservationExpirySchedule.Clear();
                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;
                cachedWeaponsForEvents.Clear();
            }

            public void ForceReinitialize()
            {
                weapons.Clear();
                initialized = false;
                _tempReservations.Clear();
                reservationExpirySchedule.Clear();
                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;
                cachedWeaponsForEvents.Clear();
                InitializeCache();
            }

            public int cacheHighWaterMark = 0;

            public int lastCleanupTick = 0;

            public AutoArmWeaponMapComponent(Map map) : base(map)
            {
            }

            public override void FinalizeInit()
            {
                base.FinalizeInit();
                if (!initialized)
                {
                    InitializeCache();
                }
            }

            public override void ExposeData()
            {
                base.ExposeData();
                Scribe_Values.Look(ref initialized, "initialized", false);
                Scribe_Values.Look(ref lastChangeDetectedTick, "lastChangeTick", 0);
                Scribe_Values.Look(ref cacheHighWaterMark, "highWaterMark", 0);
            }

            public override void MapComponentTick()
            {
                base.MapComponentTick();

                if (!initialized && Find.TickManager.TicksGame > 10)
                {
                    InitializeCache();
                }

                if ((Find.TickManager.TicksGame % 10000) == 0)
                {
                    PerformCleanup();
                }

                var now = Find.TickManager.TicksGame;
                if (now % TempReservationTicks == 0 && _tempReservations.Count > 0)
                {
                    var toRemove = ListPool<ThingWithComps>.Get();
                    foreach (var kvp in _tempReservations)
                    {
                        if (kvp.Value.ExpiryTick <= now || kvp.Key == null || kvp.Key.Destroyed)
                        {
                            toRemove.Add(kvp.Key);
                        }
                    }

                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        _tempReservations.Remove(toRemove[i]);
                    }

                    ListPool<ThingWithComps>.Return(toRemove);
                }
            }

            public void InitializeCache()
            {
                if (initialized) return;

                initialized = true;

                if (weapons.Count > 0)
                {
                    AutoArmLogger.Debug(() => $"Clearing {weapons.Count} stale weapons from cache for map {map.uniqueID}");
                    weapons.Clear();
                }

                AutoArmLogger.Debug(() => $"Initializing weapon cache for map {map.uniqueID}");

                var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
                var playerFaction = Find.FactionManager?.OfPlayer;

                foreach (Thing thing in allWeapons)
                {
                    if (thing is ThingWithComps weapon && WeaponValidation.IsWeapon(weapon))
                    {
                        if (weapon.ParentHolder is Pawn_EquipmentTracker ||
                            weapon.ParentHolder is Pawn_InventoryTracker)
                        {
                            continue;
                        }

                        weapons.Add(weapon);
                        cachedWeaponsForEvents.Add(weapon);
                    }
                }

                cacheHighWaterMark = weapons.Count;
                lastChangeDetectedTick = Find.TickManager.TicksGame;

                AutoArmLogger.Debug(() => $"Initialized cache with {weapons.Count} weapons");

                if (weapons.Count > 0)
                {
                    AutoArmLogger.Debug(() => $"Warming up component cache for {weapons.Count} weapons...");

                    foreach (var weapon in weapons)
                    {
                        Components.WarmupWeapon(weapon);
                    }

                    AutoArmLogger.Debug(() => "Component cache warmup complete");
                }

                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;
            }


            private void PerformCleanup()
            {
                int removed = 0;
                var toRemove = ListPool<ThingWithComps>.Get();

                foreach (var weapon in cachedWeaponsForEvents)
                {
                    if (weapon == null || weapon.Destroyed || !weapon.Spawned || weapon.Map != map)
                    {
                        toRemove.Add(weapon);
                    }
                }

                foreach (var weapon in toRemove)
                {
                    OnWeaponRemoved(weapon);
                    removed++;
                }

                ListPool<ThingWithComps>.Return(toRemove);

                if (weapons.Count > cacheHighWaterMark)
                {
                    cacheHighWaterMark = weapons.Count;
                }

                lastCleanupTick = Find.TickManager.TicksGame;

                if (removed > 0 && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[WeaponCache] Legacy cleanup removed {removed} missed weapons (event-based system should handle most)");
                }
            }

            public void AddWeapon(ThingWithComps weapon)
            {
                if (weapon == null || weapons.Contains(weapon))
                    return;

                weapons.Add(weapon);
                cachedWeaponsForEvents.Add(weapon);
                lastChangeDetectedTick = Find.TickManager.TicksGame;
                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;

                Components.WarmupWeapon(weapon);
            }

            public void RemoveWeapon(ThingWithComps weapon)
            {
                if (weapon == null || !weapons.Remove(weapon))
                    return;

                cachedWeaponsForEvents.Remove(weapon);

                if (_tempReservations.ContainsKey(weapon))
                    _tempReservations.Remove(weapon);

                lastChangeDetectedTick = Find.TickManager.TicksGame;
                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;
            }

            /// <summary>
            /// Weapon destroyed
            /// O(1) removal
            /// </summary>
            public void OnWeaponRemoved(ThingWithComps weapon)
            {
                if (weapon == null || !cachedWeaponsForEvents.Contains(weapon))
                    return;

                weapons.Remove(weapon);
                cachedWeaponsForEvents.Remove(weapon);
                _tempReservations.Remove(weapon);

                lastChangeDetectedTick = Find.TickManager.TicksGame;
                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[WeaponCacheEvent] Removed {weapon.Label} from cache (destroyed/despawned)");
                }
            }

            /// <summary>
            /// Rebuild weapon set
            /// Rebuild on load from saved data
            /// </summary>
            public void RebuildCachedWeaponSet()
            {
                cachedWeaponsForEvents.Clear();

                foreach (var weapon in weapons)
                {
                    if (weapon?.Destroyed == false && weapon.Spawned)
                    {
                        cachedWeaponsForEvents.Add(weapon);
                    }
                }

                AutoArmLogger.Debug(() => $"WeaponCache rebuilt for map {map?.uniqueID ?? -1}: {cachedWeaponsForEvents.Count} weapons tracked");
            }

            /// <summary>
            /// Reset
            /// </summary>
            public void Reset()
            {
                weapons.Clear();
                cachedWeaponsForEvents.Clear();
                _tempReservations.Clear();
                reservationExpirySchedule.Clear();
                initialized = false;
                lastNonForbiddenCheckTick = -1;
                lastNonForbiddenResult = false;
                lastNonForbiddenCount = 0;
                lastAllForbiddenLoggedTick = -1;
                AutoArmLogger.Debug(() => $"WeaponCache reset for map {map?.uniqueID ?? -1}");
            }


            public bool HasTempReservation(ThingWithComps weapon, int askingPawnId, int now)
            {
                TempReservation res;
                if (weapon == null) return false;
                if (_tempReservations.TryGetValue(weapon, out res))
                {
                    if (res.ExpiryTick <= now)
                    {
                        _tempReservations.Remove(weapon);
                        return false;
                    }
                    return res.PawnId != askingPawnId;
                }
                return false;
            }

            /// <summary>
            /// Reserve temporarily
            /// </summary>
            public void SetTempReservation(ThingWithComps weapon, int pawnId, int expiry)
            {
                if (weapon == null) return;

                if (_tempReservations.TryGetValue(weapon, out var oldReservation))
                {
                    RemoveFromReservationSchedule(weapon, oldReservation.ExpiryTick);
                }

                var res = new TempReservation { PawnId = pawnId, ExpiryTick = expiry };
                _tempReservations[weapon] = res;

                if (!reservationExpirySchedule.TryGetValue(expiry, out var list))
                {
                    list = new List<ThingWithComps>();
                    reservationExpirySchedule[expiry] = list;
                }
                list.Add(weapon);
            }

            public void ClearTempReservation(ThingWithComps weapon)
            {
                if (weapon == null) return;
                if (_tempReservations.TryGetValue(weapon, out var reservation))
                {
                    _tempReservations.Remove(weapon);
                    RemoveFromReservationSchedule(weapon, reservation.ExpiryTick);
                }
            }

            /// <summary>
            /// Process expiring
            /// Tick update
            /// </summary>
            public void ProcessExpiredReservations(int tick)
            {
                if (!reservationExpirySchedule.TryGetValue(tick, out var expiredReservations))
                    return;

                foreach (var weapon in expiredReservations)
                {
                    if (weapon == null || weapon.Destroyed)
                        continue;

                    _tempReservations.Remove(weapon);
                }

                reservationExpirySchedule.Remove(tick);
            }


            private void RemoveFromReservationSchedule(ThingWithComps weapon, int? knownExpireTick = null)
            {
                if (weapon == null)
                    return;

                if (knownExpireTick.HasValue)
                {
                    if (reservationExpirySchedule.TryGetValue(knownExpireTick.Value, out var list))
                    {
                        list.Remove(weapon);
                        if (list.Count == 0)
                        {
                            reservationExpirySchedule.Remove(knownExpireTick.Value);
                        }
                    }
                }
                else
                {
                    int keyToRemove = -1;
                    foreach (var kvp in reservationExpirySchedule)
                    {
                        if (kvp.Value.Remove(weapon))
                        {
                            if (kvp.Value.Count == 0)
                            {
                                keyToRemove = kvp.Key;
                            }
                            break;
                        }
                    }
                    if (keyToRemove != -1)
                    {
                        reservationExpirySchedule.Remove(keyToRemove);
                    }
                }
            }

            /// <summary>
            /// Rebuild reservation schedule
            /// Rebuild on load
            /// </summary>
            public void RebuildReservationSchedule()
            {
                reservationExpirySchedule.Clear();

                int currentTick = Find.TickManager.TicksGame;

                foreach (var kvp in _tempReservations)
                {
                    var weapon = kvp.Key;
                    int expireTick = kvp.Value.ExpiryTick;

                    if (expireTick > currentTick && weapon?.Destroyed == false)
                    {
                        if (!reservationExpirySchedule.TryGetValue(expireTick, out var list))
                        {
                            list = new List<ThingWithComps>();
                            reservationExpirySchedule[expireTick] = list;
                        }
                        list.Add(weapon);
                    }
                }

                AutoArmLogger.Debug(() => $"ReservationSchedule rebuilt for map {map?.uniqueID ?? -1}: {_tempReservations.Count} reservations, " +
                                  $"{reservationExpirySchedule.Count} expiry ticks scheduled");
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
                    trackedMapIds.Add(map.uniqueID);

                    AutoArmLogger.Debug(() => $"[Cache] Created new weapon cache component for map {map.uniqueID}");
                }
                return component;
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorCleanup(ex, "MapComponentCreation");
                return null;
            }
        }

        private static readonly HashSet<int> trackedMapIds = new HashSet<int>();

        private static readonly Dictionary<int, Dictionary<int, ScoreEntry>> scoreCache =
            new Dictionary<int, Dictionary<int, ScoreEntry>>();

        private class ScoreEntry
        {
            public float Score { get; set; }
            public int LastUpdateTick { get; set; }
        }

        private const int ScoreCacheDuration = Constants.StandardCacheDuration;
        private const int TempReservationTicks = 60;
        private const int NonForbiddenCheckCacheTicks = 300;


        public static void EnsureCacheExists(Map map)
        {
            GetMapComponent(map);
        }

        public static void Initialize(Map map)
        {
            var component = GetMapComponent(map);
            component?.InitializeCache();
        }

        public static void ForceReinitialize(Map map)
        {
            var component = GetMapComponent(map);
            component?.ForceReinitialize();
        }

        public static void AddWeaponToCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null || !WeaponValidation.IsWeapon(weapon))
                return;

            if (!ShouldTrackWeapon(weapon))
                return;

            var component = GetMapComponent(weapon.Map);
            component?.AddWeapon(weapon);
        }

        public static void RemoveWeaponFromCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null)
                return;

            var component = GetMapComponent(weapon.Map);
            component?.RemoveWeapon(weapon);
        }


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

            foreach (var weapon in component.weapons)
            {
                if (weapon == null || weapon.Destroyed || !weapon.Spawned)
                    continue;

                totalWeapons++;

                if (playerFaction != null && weapon.IsForbidden(playerFaction))
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

        public static bool IsWeaponTracked(Map map, ThingWithComps weapon)
        {
            if (map == null || weapon == null)
                return false;

            var component = GetMapComponent(map);
            if (component == null)
                return false;

            return component.weapons.Contains(weapon);
        }

        public static int GetCacheWeaponCount(Map map)
        {
            var component = GetMapComponent(map);
            if (component == null) return 0;

            int count = component.weapons.Count;
            float estimatedMemoryMB = count * 0.0002f;
            AutoArmPerfOverlayWindow.ReportCacheStats(count, estimatedMemoryMB);
            return count;
        }

        public static int GetLastCacheChangeTick(Map map)
        {
            var component = GetMapComponent(map);
            return component?.lastChangeDetectedTick ?? 0;
        }


        private static int? GetLastRebuildTime(Map map)
        {
            var component = GetMapComponent(map);
            return component?.lastChangeDetectedTick > 0 ? component.lastChangeDetectedTick : (int?)null;
        }

        public static void MarkCacheAsChanged(Map map)
        {
            var component = GetMapComponent(map);
            if (component != null)
            {
                component.lastChangeDetectedTick = Find.TickManager.TicksGame;
            }
        }


        public static IEnumerable<ThingWithComps> GetAllWeapons(Map map)
        {
            var component = GetMapComponent(map);
            if (component == null)
                yield break;

            var playerFaction = Find.FactionManager?.OfPlayer;

            int forbiddenWeapons = 0;
            bool debugLogging = AutoArmMod.settings?.debugLogging == true;

            foreach (var weapon in component.weapons)
            {
                if (weapon != null && !weapon.Destroyed && weapon.Spawned)
                {
                    bool isForbidden = playerFaction != null && weapon.IsForbidden(playerFaction);
                    if (isForbidden)
                    {
                        forbiddenWeapons++;
                        if (debugLogging && forbiddenWeapons == 1)
                        {
                            var comp = weapon.compForbiddable;
                            bool compForbidden = comp?.Forbidden ?? false;
                            AutoArmLogger.Debug(() => $"[Cache] Skipping forbidden weapon: {AutoArmLogger.GetDefLabel(weapon.def)} at {weapon.Position} (flags: IsForbidden={AutoArmLogger.FormatBool(isForbidden)}, CompForbidden={AutoArmLogger.FormatBool(compForbidden)})");
                        }
                    }

                    if (!isForbidden)
                    {
                        yield return weapon;
                    }
                }
            }
        }

        public static IEnumerable<ThingWithComps> GetAllStorageWeapons(Map map)
        {
            foreach (var weapon in GetAllWeapons(map))
            {
                if (IsInStorageZone(weapon))
                {
                    yield return weapon;
                }
            }
        }

        public static IEnumerable<ThingWithComps> GetWeaponsForOutfit(Map map, ApparelPolicy outfit)
        {
            if (outfit?.filter == null)
            {
                foreach (var weapon in GetAllWeapons(map))
                {
                    yield return weapon;
                }
            }
            else
            {
                foreach (var weapon in GetAllWeapons(map))
                {
                    if (outfit.filter.Allows(weapon.def))
                    {
                        if (CheckQualityRequirements(weapon, outfit))
                        {
                            yield return weapon;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Storage weapons
        /// Avoid outfit filter redundancy
        /// </summary>
        public static IEnumerable<ThingWithComps> GetStorageWeapons(Map map, ApparelPolicy outfit)
        {
            if (outfit?.filter == null)
            {
                foreach (var weapon in GetAllStorageWeapons(map))
                {
                    yield return weapon;
                }
            }
            else
            {
                foreach (var weapon in GetAllWeapons(map))
                {
                    if (!IsInStorageZone(weapon))
                        continue;

                    if (outfit.filter.Allows(weapon.def))
                    {
                        if (CheckQualityRequirements(weapon, outfit))
                        {
                            yield return weapon;
                        }
                    }
                }
            }
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

        private static bool CheckInstanceRequirements(ThingWithComps weapon, ApparelPolicy outfit)
        {
            return CheckQualityRequirements(weapon, outfit);
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
                if (currentTick - entry.LastUpdateTick < ScoreCacheDuration)
                {
                    AutoArmPerfOverlayWindow.ReportCacheHit();
                    return entry.Score;
                }
            }

            AutoArmPerfOverlayWindow.ReportCacheMiss();
            float score = WeaponScoringHelper.GetTotalScore(pawn, weapon);

            if (entry == null)
            {
                entry = new ScoreEntry();
                pawnCache[weaponId] = entry;
            }

            entry.Score = score;
            entry.LastUpdateTick = currentTick;

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

        public static int CleanupScoreCache()
        {
            int removedCount = 0;
            int currentTick = Find.TickManager.TicksGame;

            var activePawnIds = new HashSet<int>();
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawns)
                {
                    if (!pawn.Destroyed && !pawn.Dead)
                        activePawnIds.Add(pawn.thingIDNumber);
                }
            }

            var idsToRemove = ListPool<int>.Get();
            foreach (var id in scoreCache.Keys)
            {
                if (!activePawnIds.Contains(id))
                    idsToRemove.Add(id);
            }

            foreach (var id in idsToRemove)
            {
                removedCount += scoreCache[id]?.Count ?? 0;
                scoreCache.Remove(id);
            }
            ListPool<int>.Return(idsToRemove);

            var keysToRemoveLater = ListPool<int>.Get();
            foreach (var pawnEntry in scoreCache)
            {
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
                int toRemove = scoreCache.Count - 400;

                var lruPawns = ListPool<(int pawnId, int lastUpdateTick)>.Get();
                foreach (var pawnEntry in scoreCache)
                {
                    int mostRecentTick = 0;
                    foreach (var scoreEntry in pawnEntry.Value.Values)
                    {
                        if (scoreEntry.LastUpdateTick > mostRecentTick)
                            mostRecentTick = scoreEntry.LastUpdateTick;
                    }
                    lruPawns.Add((pawnEntry.Key, mostRecentTick));
                }

                lruPawns.Sort((a, b) => a.lastUpdateTick.CompareTo(b.lastUpdateTick));

                int evictedCount = 0;
                for (int i = 0; i < toRemove && i < lruPawns.Count; i++)
                {
                    int pawnId = lruPawns[i].pawnId;
                    int weaponCount = scoreCache[pawnId]?.Count ?? 0;
                    scoreCache.Remove(pawnId);
                    removedCount += weaponCount;
                    evictedCount++;
                }

                if (AutoArmMod.settings?.debugLogging == true && evictedCount > 0)
                {
                    AutoArmLogger.Debug(() => $"Evicted {evictedCount} least-recently-used pawn caches (was {scoreCache.Count + evictedCount} pawns, now {scoreCache.Count})");
                }

                ListPool<(int pawnId, int lastUpdateTick)>.Return(lruPawns);
            }

            if (scoreCache.Count > 500)
            {
                AutoArmLogger.Warn($"Score cache exceeded hard limit (500 pawns) with {scoreCache.Count} entries - full clear (possible cleanup issue)");
                scoreCache.Clear();
            }

            return removedCount;
        }


        public static void OnOutfitFilterChanged(ApparelPolicy outfit, ThingDef specificWeaponChanged = null)
        {
            var colonists = GetAllColonists();
            if (colonists == null) return;

            var affectedMaps = new HashSet<Map>();

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

        public static void PredictBestCandidateForWeapon(ThingWithComps weapon)
        {
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

            component.lastNonForbiddenCheckTick = -1;
            component.lastNonForbiddenResult = false;
            component.lastNonForbiddenCount = 0;
            component.lastAllForbiddenLoggedTick = -1;

            component.lastChangeDetectedTick = Find.TickManager.TicksGame;
        }

        public static bool ShouldTrackWeapon(ThingWithComps weapon)
        {
            if (weapon == null || !weapon.Spawned)
                return false;

            if (weapon.ParentHolder is Pawn_EquipmentTracker ||
                weapon.ParentHolder is Pawn_InventoryTracker)
            {
                return false;
            }

            return true;
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

            if (parent is Building)
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

            var playerFaction = Faction.OfPlayer;
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


        private static void DebugRebuildCache(Map map)
        {
            if (!Prefs.DevMode)
            {
                AutoArmLogger.Debug(() => "DebugRebuildCache called outside dev mode - ignoring");
                return;
            }

            AutoArmLogger.Debug(() => $"[WARNING] Manual cache rebuild for map {map?.uniqueID ?? -1}");

            if (map == null)
                return;

            var component = GetMapComponent(map);
            if (component != null)
            {
                component.ResetCache();
                component.InitializeCache();
            }
        }

        public static void LogCacheStatistics()
        {
            if (!Prefs.DevMode)
                return;

            AutoArmLogger.Debug(() => "[CACHE STATISTICS]");

            foreach (var map in Find.Maps)
            {
                var component = GetMapComponent(map);
                if (component != null)
                {
                    AutoArmLogger.Debug(() => $"  Map {map.uniqueID}: {component.weapons.Count} weapons (peak: {component.cacheHighWaterMark})");
                }
            }

            AutoArmLogger.Debug(() => $"  Score cache: {scoreCache.Count} pawns");
        }

        private static string GetCacheStats()
        {
            int totalEntries = 0;
            foreach (var kvp in scoreCache)
                totalEntries += kvp.Value.Count;

            int colonistCount = 0;
            foreach (var pawn in GetAllColonists())
                colonistCount++;
            int pawnCount = scoreCache.Count;

            return $"WeaponScoreCache: {totalEntries} entries, {pawnCount} pawns, {colonistCount} colonists";
        }

        public static void ClearCacheForMap(Map map)
        {
            if (map == null)
                return;

            try
            {
                var component = map.GetComponent<AutoArmWeaponMapComponent>();
                component?.ResetCache();

                if (map.mapPawns != null)
                {
                    foreach (var pawn in map.mapPawns.AllPawns)
                    {
                        if (pawn != null)
                        {
                            scoreCache.Remove(pawn.thingIDNumber);
                        }
                    }
                }

                trackedMapIds.Remove(map.uniqueID);
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorCleanup(ex, "ClearMapCache");
            }
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

                var mapsCopy = maps.ToArray();

                var activeIds = new HashSet<int>();
                foreach (var map in mapsCopy)
                {
                    if (map != null)
                    {
                        activeIds.Add(map.uniqueID);
                    }
                }

                if (trackedMapIds.Count > 0)
                {
                    var toRemove = ListPool<int>.Get();
                    foreach (var mapId in trackedMapIds)
                    {
                        if (!activeIds.Contains(mapId))
                        {
                            toRemove.Add(mapId);
                        }
                    }

                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        trackedMapIds.Remove(toRemove[i]);
                    }

                    ListPool<int>.Return(toRemove);
                }

                CleanupScoreCache();
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorCleanup(ex, "CleanupDestroyedMaps");
            }
        }

        public static void PreWarmFilterCheck(ThingDef weaponDef, ApparelPolicy outfit)
        {
            if (weaponDef == null || outfit?.filter == null)
                return;

            if (!weaponDef.IsWeapon)
                return;

            outfit.filter.Allows(weaponDef);
        }

        /// <summary>
        /// Pre-warm skill caches for a colonist to prevent first-equip spikes
        /// </summary>
        public static void PreWarmColonistScore(Pawn pawn, bool isRanged)
        {
            if (pawn?.skills?.skills == null)
                return;


            var shootingSkill = pawn.skills.GetSkill(SkillDefOf.Shooting);
            var meleeSkill = pawn.skills.GetSkill(SkillDefOf.Melee);

            var weaponDef = isRanged ? AutoArmDefOf.Gun_BoltActionRifle : AutoArmDefOf.MeleeWeapon_Knife;
            if (weaponDef != null)
            {
                ThingWithComps dummyWeapon = null;
                try
                {
                    if (weaponDef.MadeFromStuff)
                    {
                        var stuff = GenStuff.DefaultStuffFor(weaponDef);
                        dummyWeapon = ThingMaker.MakeThing(weaponDef, stuff) as ThingWithComps;
                    }
                    else
                    {
                        dummyWeapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                    }
                }
                catch (Exception ex)
                {
                    AutoArmLogger.Debug(() => $"[Warmup] Failed to create test weapon '{AutoArmLogger.GetDefLabel(weaponDef)}': {ex.Message}");
                }

                if (dummyWeapon != null)
                {
                    try
                    {
                        WeaponScoringHelper.GetSkillScore(pawn, dummyWeapon, out _);
                        WeaponScoringHelper.GetWeaponPropertyScore(pawn, dummyWeapon);
                    }
                    finally
                    {
                        dummyWeapon.Destroy();
                    }
                }
            }
        }

        public static void PreWarmOutfitCachesForMap(Map map)
        { }

        public static bool CheckWeaponPassesOutfitFilter(ThingWithComps weapon, ApparelPolicy outfit)
        {
            if (outfit?.filter == null) return true;
            return outfit.filter.Allows(weapon.def) && CheckQualityRequirements(weapon, outfit);
        }

        internal static void DebugForceRebuildCacheForTestMode(Map map)
        {
            DebugRebuildCache(map);
        }

        private static int CountAvailable(Map map)
        {
            var component = GetMapComponent(map);
            if (component == null)
                return 0;

            int now = Find.TickManager.TicksGame;
            if (component.lastNonForbiddenCheckTick < 0 ||
                now - component.lastNonForbiddenCheckTick >= NonForbiddenCheckCacheTicks)
            {
                RecalculateNonForbidden(component, now);
            }

            return component.lastNonForbiddenCount;
        }

        private static int GetAvailableWeaponsQuickCount(Map map)
        {
            return CountAvailable(map);
        }

        private static bool GetForbiddenState(ThingWithComps weapon, out bool isForbidden)
        {
            isForbidden = false;
            if (weapon?.Map == null) return false;
            var playerFaction = Find.FactionManager?.OfPlayer;
            isForbidden = playerFaction != null && weapon.IsForbidden(playerFaction);
            return true;
        }

        public static void ClearAllCaches()
        {
            scoreCache.Clear();
            trackedMapIds.Clear();
            foreach (var map in Find.Maps)
            {
                var component = GetMapComponent(map);
                if (component != null)
                {
                    component.weapons.Clear();
                }
            }
        }

        public static void ClearScoreCache()
        {
            scoreCache.Clear();
        }

        public static void InvalidateCache(Map map = null)
        {
            if (map != null)
            {
                DebugRebuildCache(map);
            }
            else
            {
                foreach (var m in Find.Maps)
                {
                    DebugRebuildCache(m);
                }
            }
        }

        internal static void ValidateCacheIntegrity(Map map)
        {
            var component = GetMapComponent(map);
            if (component == null) return;

            var toRemove = new List<ThingWithComps>();
            foreach (var weapon in component.weapons)
            {
                if (weapon == null || weapon.Destroyed)
                {
                    toRemove.Add(weapon);
                }
            }

            foreach (var weapon in toRemove)
            {
                component.weapons.Remove(weapon);
            }
        }

        internal static void ForceRebuildAllOutfitCaches(Map map)
        {
            ClearScoreCache();
        }

        internal static void ClearOutfitCachesOnly()
        {
            ClearScoreCache();
        }

        public static void RebuildCache(Map map)
        {
            if (map == null)
                return;

            var component = GetMapComponent(map);
            if (component != null)
            {
                component.ResetCache();
                component.InitializeCache();
            }
        }
    }
}

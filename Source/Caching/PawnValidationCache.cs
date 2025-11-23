
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace AutoArm.Caching
{
    /// <summary>
    /// Validation cache with lord tracking
    /// </summary>
    public static class PawnValidationCache
    {

        private static Dictionary<Pawn, CachedPawnValidation> _cache = new Dictionary<Pawn, CachedPawnValidation>();

        private static Dictionary<Pawn, int> _lastValidationTick = new Dictionary<Pawn, int>();

        private const int RevalidationInterval = Constants.StandardCacheDuration;

        private static int _hits = 0;

        private static int _misses = 0;
        private static int _lastReportTick = 0;

        private static Dictionary<int, List<string>> _pendingInvalidationLogs = new Dictionary<int, List<string>>();
        private static int _invalidationBufferTick = -1;


        private static readonly HashSet<Pawn> pawnsInRestrictedLords = new HashSet<Pawn>();

        private static readonly Dictionary<Pawn, Lord> pawnLords = new Dictionary<Pawn, Lord>();

        private static readonly HashSet<string> restrictedLordJobTypes = new HashSet<string>
        {
            "LordJob_Joinable_Party",
            "LordJob_Joinable_MarriageCeremony",
            "LordJob_Ritual",
            "LordJob_Joinable_Speech",
            "LordJob_BestowingCeremony",
            "LordJob_Joinable_Concert",
            "LordJob_Joinable_Dance",
            "LordJob_TradeWithColony",
            "LordJob_FormAndSendCaravan",

            "LordJob_Joinable_Gathering",
            "LordJob_RitualDuel",
            "LordJob_Joinable_DateLead",

            "LordJob_VisitColony",
            "LordJob_HospitalityParty"
        };

        private static readonly HashSet<string> restrictedPatterns = new HashSet<string>
        {
            "Party",
            "Wedding",
            "Ritual",
            "Speech",
            "Ceremony",
            "Festival",
            "Celebration",
            "Gathering",
            "Concert",
            "Dance",
            "Funeral",
            "Date",
            "Bestowing"
        };


        private class CachedPawnValidation
        {
            public bool IsAnimal { get; set; }

            public bool IsMechanoid { get; set; }
            public bool IsToolUser { get; set; }
            public bool HasSufficientIntelligence { get; set; }

            public bool HasManipulation { get; set; }

            public bool CanDoViolence { get; set; }

            public bool IsChild { get; set; }

            public bool MeetsAgeRequirement { get; set; }

            public bool IsColonist { get; set; }

            public bool IsTemporaryColonist { get; set; }
            public bool IsPrisoner { get; set; }

            public int CachedAtTick { get; set; }

            public bool IsValidForWeapons { get; set; }
        }


        public static bool CanConsiderWeapons(Pawn pawn)
        {
            if (!CheckDynamicProperties(pawn))
                return false;

            return CheckCachedProperties(pawn);
        }

        /// <summary>
        /// Checks restricted lord job
        /// </summary>
        public static bool IsInRestrictedLord(Pawn pawn)
        {
            if (pawn == null) return false;
            return pawnsInRestrictedLords.Contains(pawn);
        }



        private static bool CheckDynamicProperties(Pawn pawn)
        {

            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed)
                return false;

            if (pawn.Drafted)
                return false;

            if (pawn.InMentalState)
                return false;


            if (IsInRestrictedLord(pawn))
                return false;

            if (CaravanCompat.IsCaravanMember(pawn) || pawn.carryTracker?.CarriedThing != null)
                return false;

            if (ValidationHelper.IsInRitual(pawn))
                return false;

            if (IsCurrentlyHauling(pawn))
                return false;

            return true;
        }


        private static bool IsCurrentlyHauling(Pawn pawn)
        {
            if (pawn.CurJob == null)
                return false;

            var jobDef = pawn.CurJob.def;
            return ValidationHelper.IsHaulingOrInventoryJob(jobDef);
        }



        private static bool CheckCachedProperties(Pawn pawn)
        {
            int currentTick = Find.TickManager.TicksGame;
            bool needsRevalidation = false;

            if (_lastValidationTick.TryGetValue(pawn, out int lastValidation))
            {
                needsRevalidation = (currentTick - lastValidation) > RevalidationInterval;
            }
            else
            {
                needsRevalidation = true;
            }

            if (!needsRevalidation && _cache.TryGetValue(pawn, out var cached))
            {
                _hits++;
                AutoArmPerfOverlayWindow.ReportCacheHit();
                AutoArmPerfOverlayWindow.ReportValidationCacheHit();
                ReportCacheStats();
                return cached.IsValidForWeapons;
            }

            _misses++;
            AutoArmPerfOverlayWindow.ReportCacheMiss();
            AutoArmPerfOverlayWindow.ReportValidationCacheMiss();
            var validation = BuildCacheEntry(pawn);

            _cache[pawn] = validation;
            _lastValidationTick[pawn] = currentTick;

            ReportCacheStats();
            return validation.IsValidForWeapons;
        }


        private static CachedPawnValidation BuildCacheEntry(Pawn pawn)
        {
            var entry = new CachedPawnValidation
            {
                CachedAtTick = Find.TickManager.TicksGame
            };

            if (pawn.health?.capacities == null)
            {
                entry.HasManipulation = false;
                entry.IsValidForWeapons = false;
                return entry;
            }

            entry.HasManipulation = pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
            if (!entry.HasManipulation)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }

            if (pawn.RaceProps == null)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }

            entry.IsAnimal = pawn.RaceProps.Animal;
            entry.IsMechanoid = pawn.RaceProps.IsMechanoid;
            entry.IsToolUser = pawn.RaceProps.ToolUser;
            entry.HasSufficientIntelligence = pawn.RaceProps.intelligence >= Intelligence.ToolUser;

            if (entry.IsAnimal || entry.IsMechanoid || !entry.IsToolUser || !entry.HasSufficientIntelligence)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }

            entry.IsColonist = global::AutoArm.Jobs.Jobs.SafeIsColonist(pawn);
            entry.IsTemporaryColonist = global::AutoArm.Jobs.Jobs.IsTemporary(pawn);
            entry.IsPrisoner = pawn.IsPrisoner;

            var playerFaction = Find.FactionManager?.OfPlayer;
            if (TestRunner.IsRunningTests && playerFaction != null && pawn.Faction == playerFaction)
            {
                AutoArmLogger.Debug(() => $"[TEST] PawnValidationCache: Allowing test pawn");
            }
            else if (!entry.IsColonist || entry.IsPrisoner)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }

            if (pawn.equipment?.Primary != null && Components.IsBiocodedTo(pawn.equipment.Primary, pawn))
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Blocked - has biocoded weapon (locked to this pawn)");
                entry.IsValidForWeapons = false;
                return entry;
            }

            if (entry.IsTemporaryColonist)
            {
                bool allowSetting = AutoArmMod.settings?.allowTemporaryColonists ?? false;
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Temporary colonist check - IsTemporary={AutoArmLogger.FormatBool(true)}, AllowSetting={AutoArmLogger.FormatBool(allowSetting)}");

                if (!allowSetting)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Blocked - temporary colonists not allowed by settings");
                    entry.IsValidForWeapons = false;
                    return entry;
                }
                else
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Allowed - temporary colonists enabled in settings");
                }
            }

            entry.CanDoViolence = !pawn.WorkTagIsDisabled(WorkTags.Violent);

            if (!entry.CanDoViolence)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }

            if (ModsConfig.BiotechActive)
            {
                bool childrenAllowed = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;

                if (!childrenAllowed)
                {
                    entry.IsChild = pawn.DevelopmentalStage < DevelopmentalStage.Adult;
                    entry.MeetsAgeRequirement = pawn.DevelopmentalStage >= DevelopmentalStage.Child;
                }
                else
                {
                    int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;
                    int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;

                    if (minAge <= 13)
                    {
                        entry.IsChild = pawn.DevelopmentalStage < DevelopmentalStage.Adult;
                        entry.MeetsAgeRequirement = (pawn.DevelopmentalStage >= DevelopmentalStage.Adult) || (age >= minAge);
                    }
                    else
                    {
                        entry.IsChild = age < minAge;
                        entry.MeetsAgeRequirement = age >= minAge;
                    }
                }
            }
            else
            {
                entry.IsChild = false;
                entry.MeetsAgeRequirement = true;
            }

            if (!entry.MeetsAgeRequirement)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }

            entry.IsValidForWeapons = true;
            return entry;
        }


        public static void UpdateLordCache(Pawn pawn, Lord newLord)
        {
            if (pawn == null) return;

            if (!pawn.IsColonist && !pawn.IsSlaveOfColony)
            {
                RemoveFromLordCache(pawn);
                return;
            }

            RemoveFromLordCache(pawn);

            if (newLord?.LordJob != null)
            {
                pawnLords[pawn] = newLord;

                var lordJobType = newLord.LordJob.GetType();
                var typeName = lordJobType.Name;

                if (restrictedLordJobTypes.Contains(typeName))
                {
                    pawnsInRestrictedLords.Add(pawn);

                    AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Joined restricted lord job: {typeName}");
                    return;
                }

                foreach (var pattern in restrictedPatterns)
                {
                    if (typeName.Contains(pattern))
                    {
                        pawnsInRestrictedLords.Add(pawn);

                        restrictedLordJobTypes.Add(typeName);

                        AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Joined restricted lord job (pattern match): {typeName}");
                        return;
                    }
                }
            }
        }


        private static void RemoveFromLordCache(Pawn pawn)
        {
            if (pawn == null) return;

            if (pawnLords.Remove(pawn))
            {
                if (pawnsInRestrictedLords.Remove(pawn) && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Left restricted lord job");
                }
            }
        }


        private static Dictionary<Pawn, int> _lastInvalidationLogTick = new Dictionary<Pawn, int>();

        private const int InvalidationLogCooldown = Constants.StandardCacheDuration;

        public static void InvalidatePawn(Pawn pawn)
        {
            if (pawn == null)
                return;

            // Skip non-tool users (animals, etc.)
            if (pawn.RaceProps?.ToolUser != true)
                return;

            _cache.Remove(pawn);
            _lastValidationTick.Remove(pawn);

            RemoveFromLordCache(pawn);

            if (AutoArmMod.settings?.debugLogging == true &&
                Find.TickManager != null &&
                Find.TickManager.TicksGame > 600 &&
                Current.ProgramState == ProgramState.Playing)
            {
                int currentTick = Find.TickManager.TicksGame;
                if (!_lastInvalidationLogTick.TryGetValue(pawn, out int lastLogTick) ||
                    (currentTick - lastLogTick) > InvalidationLogCooldown)
                {
                    if (_invalidationBufferTick < 0)
                        _invalidationBufferTick = currentTick;

                    if (!_pendingInvalidationLogs.TryGetValue(currentTick, out var names))
                    {
                        names = new List<string>();
                        _pendingInvalidationLogs[currentTick] = names;
                    }

                    var name = pawn.Name?.ToStringShort ?? pawn.LabelShort;
                    names.Add(name);
                    _lastInvalidationLogTick[pawn] = currentTick;
                }
            }
        }

        /// <summary>
        /// Flush pending messages
        /// Output invalidation logs
        /// </summary>
        public static void FlushPendingInvalidationLogs()
        {
            if (_pendingInvalidationLogs.Count == 0)
                return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (_invalidationBufferTick >= 0 && currentTick > _invalidationBufferTick)
            {
                foreach (var kvp in _pendingInvalidationLogs)
                {
                    var names = kvp.Value;
                    if (names.Count == 0)
                        continue;

                    string pawnList = string.Join(", ", names);
                    AutoArmLogger.Debug(() => $"Invalidated cache for {pawnList}");
                }

                _pendingInvalidationLogs.Clear();
                _invalidationBufferTick = -1;
            }
        }


        internal static void InvalidateIfManipulationChanged(Pawn pawn)
        {
            if (pawn == null) return;

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.Saving)
                return;

            if (!_cache.TryGetValue(pawn, out var cached))
            {
                return;
            }

            bool hasManipulation = pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) ?? false;

            if (cached.HasManipulation != hasManipulation)
            {
                InvalidatePawn(pawn);

                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Manipulation changed: {AutoArmLogger.FormatBool(cached.HasManipulation)} to {AutoArmLogger.FormatBool(hasManipulation)}, invalidating cache");
            }
        }

        /// <summary>
        /// Invalidate faction changes
        /// </summary>
        public static void InvalidateFactionChanges()
        {
            var pawnsToRevalidate = ListPool<Pawn>.Get();
            foreach (var pawn in _cache.Keys)
                pawnsToRevalidate.Add(pawn);

            foreach (var pawn in pawnsToRevalidate)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    _cache.Remove(pawn);
                    _lastValidationTick.Remove(pawn);
                }
                else
                {
                    _lastValidationTick[pawn] = 0;
                }
            }

            ListPool<Pawn>.Return(pawnsToRevalidate);
        }


        public static void CleanupDeadPawns()
        {
            var deadPawns = ListPool<Pawn>.Get();
            foreach (var pawn in _cache.Keys)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                    deadPawns.Add(pawn);
            }

            foreach (var pawn in deadPawns)
            {
                _cache.Remove(pawn);
                _lastValidationTick.Remove(pawn);
                _lastInvalidationLogTick.Remove(pawn);
            }

            var orphanedValidationPawns = ListPool<Pawn>.Get();
            foreach (var pawn in _lastValidationTick.Keys)
            {
                if ((pawn == null || pawn.Destroyed || pawn.Dead) && !_cache.ContainsKey(pawn))
                    orphanedValidationPawns.Add(pawn);
            }

            foreach (var pawn in orphanedValidationPawns)
            {
                _lastValidationTick.Remove(pawn);
                _lastInvalidationLogTick.Remove(pawn);
            }

            var orphanedLogPawns = ListPool<Pawn>.Get();
            foreach (var pawn in _lastInvalidationLogTick.Keys)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                    orphanedLogPawns.Add(pawn);
            }

            foreach (var pawn in orphanedLogPawns)
            {
                _lastInvalidationLogTick.Remove(pawn);
            }

            var deadLordPawns = ListPool<Pawn>.Get();
            foreach (var pawn in pawnLords.Keys)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                    deadLordPawns.Add(pawn);
            }

            foreach (var pawn in deadLordPawns)
            {
                RemoveFromLordCache(pawn);
            }

            pawnsInRestrictedLords.RemoveWhere(p => p == null || p.Destroyed || p.Dead);

            int deadCount = deadPawns.Count;
            int orphanedValidationCount = orphanedValidationPawns.Count;
            int orphanedLogCount = orphanedLogPawns.Count;
            int deadLordCount = deadLordPawns.Count;

            ListPool<Pawn>.Return(deadPawns);
            ListPool<Pawn>.Return(orphanedValidationPawns);
            ListPool<Pawn>.Return(orphanedLogPawns);
            ListPool<Pawn>.Return(deadLordPawns);

            if (AutoArmMod.settings?.debugLogging == true &&
                (deadCount > 0 || orphanedValidationCount > 0 || deadLordCount > 0))
            {
                AutoArmLogger.Debug(() => $"PawnValidationCache cleaned up {deadCount + orphanedValidationCount + deadLordCount} dead pawn entries");
            }
        }

        /// <summary>
        /// Clears entire cache (use sparingly)
        /// </summary>
        public static void ClearCache()
        {
            int count = _cache.Count;
            _cache.Clear();
            _lastValidationTick.Clear();
            _lastInvalidationLogTick.Clear();
            _hits = 0;
            _misses = 0;

            ClearLordCache();


        }

        public static void ClearLordCache()
        {
            pawnsInRestrictedLords.Clear();
            pawnLords.Clear();
        }



        private static void ReportCacheStats()
        {
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - _lastReportTick > 6000)
            {
                _lastReportTick = currentTick;

                int total = _hits + _misses;
                if (total > 0)
                {
                    float hitRate = (_hits / (float)total) * 100f;
                    AutoArmLogger.Debug(() => $"PawnValidationCache stats: {_cache.Count} entries, " +
                                      $"hits: {_hits}, misses: {_misses}, " +
                                      $"hit rate: {hitRate:F1}% | " +
                                      $"lord cache: {pawnsInRestrictedLords.Count} restricted, {pawnLords.Count} tracked");
                }

                _hits = 0;
                _misses = 0;
            }
        }

        /// <summary>
        /// Cache size
        /// </summary>
        public static int CacheSize => _cache.Count;

        /// <summary>
        /// Hit rate
        /// </summary>
        public static float GetHitRate()
        {
            int total = _hits + _misses;
            if (total == 0) return 0f;
            return (_hits / (float)total) * 100f;
        }

        /// <summary>
        /// Debug stats
        /// </summary>
        public static string GetDebugStats()
        {
            return $"PawnValidationCache: {_cache.Count} cached, LordCache: {pawnsInRestrictedLords.Count} in restricted lords, {pawnLords.Count} total tracked";
        }
    }

    /// <summary>
    /// Cache invalidation
    /// </summary>
    [HarmonyPatch]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    public static class PawnValidationCachePatches
    {

        [HarmonyPatch(typeof(Pawn), "SetFaction")]
        [HarmonyPostfix]
        public static void SetFaction_Postfix(Pawn __instance)
        {
            PawnValidationCache.InvalidatePawn(__instance);
        }

        [HarmonyPatch(typeof(Pawn_GuestTracker), "SetGuestStatus")]
        [HarmonyPostfix]
        public static void SetGuestStatus_Postfix(Pawn ___pawn)
        {
            PawnValidationCache.InvalidatePawn(___pawn);
        }

        [HarmonyPatch(typeof(PawnCapacitiesHandler), "Notify_CapacityLevelsDirty")]
        [HarmonyPostfix]
        public static void Notify_CapacityLevelsDirty_Postfix(PawnCapacitiesHandler __instance)
        {
            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null) return;


            PawnValidationCache.InvalidateIfManipulationChanged(pawn);
        }

        [HarmonyPatch(typeof(Pawn_WorkSettings), "Notify_DisabledWorkTypesChanged")]
        [HarmonyPostfix]
        public static void Notify_DisabledWorkTypesChanged_Postfix(Pawn ___pawn)
        {
            PawnValidationCache.InvalidatePawn(___pawn);
        }

        [HarmonyPatch(typeof(Pawn_AgeTracker), "BirthdayBiological")]
        [HarmonyPostfix]
        public static void BirthdayBiological_Postfix(Pawn ___pawn)
        {
            PawnValidationCache.InvalidatePawn(___pawn);
        }


        /// <summary>
        /// Track when pawns join lords
        /// </summary>
        [HarmonyPatch(typeof(Lord), "AddPawn")]
        public static class Lord_AddPawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Lord __instance, Pawn p)
            {
                if (p == null || __instance == null) return;

                if (AutoArmMod.settings?.modEnabled != true) return;

                PawnValidationCache.UpdateLordCache(p, __instance);
            }
        }

        /// <summary>
        /// Track when pawns leave lords
        /// </summary>
        [HarmonyPatch(typeof(Lord), "RemovePawn")]
        public static class Lord_RemovePawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn p)
            {
                if (p == null) return;

                if (AutoArmMod.settings?.modEnabled != true) return;

                PawnValidationCache.InvalidatePawn(p);
            }
        }

        /// <summary>
        /// Cleanup destroyed lord
        /// </summary>
        [HarmonyPatch(typeof(Lord), "Cleanup")]
        public static class Lord_Cleanup_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(Lord __instance)
            {
                if (__instance == null) return;

                if (AutoArmMod.settings?.modEnabled != true) return;

                if (__instance.ownedPawns != null)
                {
                    foreach (var pawn in __instance.ownedPawns)
                    {
                        PawnValidationCache.InvalidatePawn(pawn);
                    }
                }
            }
        }

        /// <summary>
        /// Cleanup destroyed pawn
        /// </summary>
        [HarmonyPatch(typeof(Pawn), "Destroy")]
        public static class Pawn_Destroy_Cache_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(Pawn __instance)
            {
                if (__instance == null) return;

                if (AutoArmMod.settings?.modEnabled != true) return;

                PawnValidationCache.InvalidatePawn(__instance);

                if (AutoArm.Compatibility.PocketSandCompat.Active)
                {
                    AutoArm.Compatibility.PocketSandCompat.ClearPending(__instance);
                }
            }
        }

        /// <summary>
        /// Cleanup dead pawn
        /// </summary>
        [HarmonyPatch(typeof(Pawn), "Kill")]
        public static class Pawn_Kill_Cache_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn __instance)
            {
                if (__instance == null) return;

                if (AutoArmMod.settings?.modEnabled != true) return;

                PawnValidationCache.InvalidatePawn(__instance);
            }
        }

        /// <summary>
        /// Rebuild cache after game load
        /// </summary>
        [HarmonyPatch(typeof(Map), "FinalizeLoading")]
        public static class Map_FinalizeLoading_Cache_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Map __instance)
            {
                // Null check first - defensive programming
                if (__instance == null) return;

                if (AutoArmMod.settings?.modEnabled != true) return;

                // Rebuild lord cache for all pawns on this map
                if (__instance.lordManager?.lords != null)
                {
                    foreach (var lord in __instance.lordManager.lords)
                    {
                        if (lord.ownedPawns != null)
                        {
                            foreach (var pawn in lord.ownedPawns)
                            {
                                if (pawn.IsColonist || pawn.IsSlaveOfColony)
                                {
                                    PawnValidationCache.UpdateLordCache(pawn, lord);
                                }
                            }
                        }
                    }
                }

                AutoArmLogger.Debug(() => $"Rebuilt PawnValidationCache for map {__instance.uniqueID}: {PawnValidationCache.GetDebugStats()}");
            }
        }
    }
}

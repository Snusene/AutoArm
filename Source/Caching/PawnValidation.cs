
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace AutoArm.Caching
{
    internal static class PawnValidation
    {

        private const int RevalidationInterval = Constants.StandardCacheDuration;
        private const int MaxCachedPawns = 1024;

        private static readonly TickExpiringLruCache<int, CachedPawnValidation> _cache =
            new TickExpiringLruCache<int, CachedPawnValidation>(64, RevalidationInterval, MaxCachedPawns);

        private static readonly Dictionary<int, List<string>> _pendingInvalidationLogs = new Dictionary<int, List<string>>();
        private static int _invalidationBufferTick = -1;


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

            public bool CanShoot { get; set; }

            public bool IsBrawler { get; set; }

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

        public static bool CanShoot(Pawn pawn)
        {
            if (pawn == null)
                return false;

            int currentTick = Find.TickManager.TicksGame;
            if (_cache.TryGet(pawn.thingIDNumber, currentTick, out var cached))
                return cached.CanShoot;

            var validation = BuildCacheEntry(pawn);
            _cache.Set(pawn.thingIDNumber, validation, currentTick);
            return validation.CanShoot;
        }

        public static bool IsBrawler(Pawn pawn)
        {
            if (pawn == null)
                return false;

            int currentTick = Find.TickManager.TicksGame;
            if (_cache.TryGet(pawn.thingIDNumber, currentTick, out var cached))
                return cached.IsBrawler;

            var validation = BuildCacheEntry(pawn);
            _cache.Set(pawn.thingIDNumber, validation, currentTick);
            return validation.IsBrawler;
        }

        public static bool IsInRestrictedLord(Pawn pawn)
        {
            var lord = pawn?.GetLord();
            return lord != null && IsRestrictedLordJob(lord.LordJob);
        }

        private static bool IsRestrictedLordJob(LordJob lordJob)
        {
            if (lordJob == null) return false;

            var typeName = lordJob.GetType().Name;
            if (restrictedLordJobTypes.Contains(typeName))
                return true;

            foreach (var pattern in restrictedPatterns)
            {
                if (typeName.Contains(pattern))
                {
                    restrictedLordJobTypes.Add(typeName);
                    return true;
                }
            }

            return false;
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

            if (pawn.IsCaravanMember() || pawn.carryTracker?.CarriedThing != null)
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

            if (_cache.TryGet(pawn.thingIDNumber, currentTick, out var cached))
            {
                PerfMetrics.ReportCacheHit();
                PerfMetrics.ReportValidationCacheHit();
                return cached.IsValidForWeapons;
            }

            PerfMetrics.ReportValidationCacheMiss();
            var validation = BuildCacheEntry(pawn);
            _cache.Set(pawn.thingIDNumber, validation, currentTick);
            return validation.IsValidForWeapons;
        }


        private static CachedPawnValidation BuildCacheEntry(Pawn pawn)
        {
            var entry = new CachedPawnValidation
            {
                CachedAtTick = Find.TickManager.TicksGame
            };

            entry.CanShoot = !pawn.WorkTagIsDisabled(WorkTags.Shooting);
            entry.IsBrawler = pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) ?? false;

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

            entry.IsColonist = ValidationHelper.SafeIsColonist(pawn);
            entry.IsTemporaryColonist = AutoArm.Jobs.JobHelper.IsTemporary(pawn);
            entry.IsPrisoner = pawn.IsPrisoner;

            var playerFaction = Faction.OfPlayerSilentFail;
            if (TestRunner.IsRunningTests && playerFaction != null && pawn.Faction == playerFaction)
            {
                AutoArmLogger.Debug(() => $"[TEST] PawnValidation: Allowing test pawn");
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
                if (!allowSetting)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Temporary colonist blocked (allowTemporaryColonists=false)");
                    entry.IsValidForWeapons = false;
                    return entry;
                }
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Temporary colonist allowed");
            }

            entry.CanDoViolence = !pawn.WorkTagIsDisabled(WorkTags.Violent);

            if (!entry.CanDoViolence)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }

            if (ModsConfig.BiotechActive)
            {
                bool isRaceAdult = pawn.ageTracker?.Adult == true;
                var devStage = pawn.DevelopmentalStage;
                bool sliderActive = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;

                if (!sliderActive)
                {
                    // Match vanilla: Child and Adult can equip, only Baby blocked
                    entry.MeetsAgeRequirement = devStage >= DevelopmentalStage.Child;
                }
                else
                {
                    // Slider active: apply minAge restriction
                    int minAge = AutoArmMod.settings?.childrenMinAge ?? Constants.ChildDefaultMinAge;
                    int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                    entry.MeetsAgeRequirement = isRaceAdult || age >= minAge;
                }
            }
            else
            {
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


        private static readonly Dictionary<int, int> _lastInvalidationLogTick = new Dictionary<int, int>();

        private const int InvalidationLogCooldown = Constants.StandardCacheDuration;

        public static void InvalidatePawn(Pawn pawn)
        {
            if (pawn == null)
                return;

            // Skip non-tool users
            if (pawn.RaceProps?.ToolUser != true)
                return;

            int pawnId = pawn.thingIDNumber;
            _cache.Remove(pawnId);

            if (AutoArmMod.settings?.debugLogging == true &&
                Find.TickManager != null &&
                Find.TickManager.TicksGame > 600 &&
                Current.ProgramState == ProgramState.Playing)
            {
                int currentTick = Find.TickManager.TicksGame;
                if (!_lastInvalidationLogTick.TryGetValue(pawnId, out int lastLogTick) ||
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
                    _lastInvalidationLogTick[pawnId] = currentTick;
                }
            }
        }

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            _cache.Remove(pawnId);
            _lastInvalidationLogTick.Remove(pawnId);
        }

        public static void FlushPendingInvalidationLogs()
        {
            if (_pendingInvalidationLogs.Count == 0)
                return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (_invalidationBufferTick >= 0 && currentTick > _invalidationBufferTick)
            {
                int totalCount = 0;
                foreach (var kvp in _pendingInvalidationLogs)
                {
                    totalCount += kvp.Value.Count;
                }

                if (totalCount > 0)
                {
                    AutoArmLogger.Debug(() => $"Invalidated pawn validation cache ({totalCount} pawn{(totalCount == 1 ? "" : "s")})");
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

            if (!_cache.TryGet(pawn.thingIDNumber, Find.TickManager.TicksGame, out var cached))
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

        public static void CleanupDeadPawns()
        {
            if (_cache.Count == 0 && _lastInvalidationLogTick.Count == 0)
                return;

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

            int deadCount = _cache.RemoveWhere((id, _) => !liveIds.Contains(id));

            var orphanedLogIds = ListPool<int>.Get();
            try
            {
                foreach (var id in _lastInvalidationLogTick.Keys)
                {
                    if (!liveIds.Contains(id)) orphanedLogIds.Add(id);
                }
                foreach (var id in orphanedLogIds)
                    _lastInvalidationLogTick.Remove(id);
            }
            finally
            {
                ListPool<int>.Return(orphanedLogIds);
            }

            if (AutoArmMod.settings?.debugLogging == true && deadCount > 0)
            {
                AutoArmLogger.Debug(() => $"PawnValidation cleaned up {deadCount} dead pawn entries");
            }
        }

        public static void ClearCache()
        {
            _cache.Clear();
            _lastInvalidationLogTick.Clear();
        }



        public static string GetDebugStats()
        {
            return $"PawnValidation: {_cache.Count} cached";
        }
    }

}

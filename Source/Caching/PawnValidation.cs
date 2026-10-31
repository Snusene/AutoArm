
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
            "LordJob_HospitalityParty",

            "LordJob_SlaveRebellion",

            "LordJob_LoadAndEnterTransporters",
            "LordJob_LoadAndEnterPortal",
            "LordJob_ExitOnShuttle"
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
            "Bestowing",
            "Rebellion",
            "LoadAndEnter"
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

            public string BlockReason { get; set; }
        }


        public const string LordReason = "In lord job";

        public static bool CanConsiderWeapons(Pawn pawn)
            => CanConsiderWeapons(pawn, out _, out _);

        public static bool CanConsiderWeapons(Pawn pawn, out string reason, out bool transient)
        {
            reason = DynamicBlock(pawn);
            transient = reason != null;
            if (transient)
                return false;

            return CheckCachedProperties(pawn, out reason);
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



        private static string DynamicBlock(Pawn pawn)
        {
            if (pawn?.Spawned != true)
                return "Not spawned";
            if (pawn.Dead)
                return "Dead";
            if (pawn.Downed)
                return "Downed";
            if (pawn.Drafted)
                return "Drafted";
            if (pawn.InMentalState)
                return "In mental state";
            if (IsInRestrictedLord(pawn))
                return LordReason;
            if (pawn.IsCaravanMember())
                return "In caravan";
            if (pawn.carryTracker?.CarriedThing != null)
                return "Carrying something";
            if (ValidationHelper.IsInRitual(pawn))
                return "In ritual or ceremony";
            if (IsCurrentlyHauling(pawn))
                return "Currently hauling";
            return null;
        }


        private static bool IsCurrentlyHauling(Pawn pawn)
        {
            if (pawn.CurJob == null)
                return false;

            var jobDef = pawn.CurJob.def;
            return ValidationHelper.IsHaulingOrInventoryJob(jobDef);
        }



        private static bool CheckCachedProperties(Pawn pawn, out string reason)
        {
            int currentTick = Find.TickManager.TicksGame;

            if (_cache.TryGet(pawn.thingIDNumber, currentTick, out var cached))
            {
                PerfMetrics.ReportCacheHit();
                PerfMetrics.ReportValidationCacheHit();
                reason = cached.BlockReason;
                return cached.IsValidForWeapons;
            }

            PerfMetrics.ReportValidationCacheMiss();
            var validation = BuildCacheEntry(pawn);
            _cache.Set(pawn.thingIDNumber, validation, currentTick);
            reason = validation.BlockReason;
            return validation.IsValidForWeapons;
        }


        private static CachedPawnValidation BuildCacheEntry(Pawn pawn)
        {
            var entry = new CachedPawnValidation
            {
                CachedAtTick = Find.TickManager.TicksGame
            };

            entry.CanShoot = (pawn.CombinedDisabledWorkTags & WorkTags.Shooting) == 0;
            entry.IsBrawler = pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) ?? false;

            if (pawn.health?.capacities == null)
                return Block(entry, "No health capacities");

            entry.HasManipulation = pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
            if (!entry.HasManipulation)
                return Block(entry, "Incapable of manipulation");

            if (pawn.RaceProps == null)
                return Block(entry, "No race properties");

            entry.IsAnimal = pawn.RaceProps.Animal;
            entry.IsMechanoid = pawn.RaceProps.IsMechanoid;
            entry.IsToolUser = pawn.RaceProps.ToolUser;
            entry.HasSufficientIntelligence = pawn.RaceProps.intelligence >= Intelligence.ToolUser;

            if (entry.IsAnimal)
                return Block(entry, "Is animal");
            if (entry.IsMechanoid)
                return Block(entry, "Is mechanoid");
            if (!entry.IsToolUser)
                return Block(entry, "Cannot use tools");
            if (!entry.HasSufficientIntelligence)
                return Block(entry, "Intelligence too low");

            entry.IsColonist = ValidationHelper.SafeIsColonist(pawn);
            entry.IsTemporaryColonist = AutoArm.Jobs.JobHelper.IsTemporary(pawn);
            entry.IsPrisoner = pawn.IsPrisoner;

            var playerFaction = Faction.OfPlayerSilentFail;
            if (TestRunner.IsRunningTests && playerFaction != null && pawn.Faction == playerFaction)
            {
                AutoArmLogger.Debug(() => $"[TEST] PawnValidation: Allowing test pawn");
            }
            else if (entry.IsPrisoner)
            {
                return Block(entry, "Is prisoner");
            }
            else if (!entry.IsColonist)
            {
                return Block(entry, "Not a colonist");
            }

            var primary = pawn.equipment?.Primary;
            if (primary != null && pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanUnequip(primary, pawn))
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Blocked - quest lodger with locked weapon");
                return Block(entry, "Quest lodger with locked equipment");
            }

            if (entry.IsTemporaryColonist)
            {
                bool allowSetting = AutoArmMod.settings?.allowTemporaryColonists ?? false;
                if (!allowSetting)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Temporary colonist blocked (allowTemporaryColonists=false)");
                    return Block(entry, "Temporary colonist (quest/borrowed) - not allowed");
                }
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Temporary colonist allowed");
            }

            entry.CanDoViolence = (pawn.CombinedDisabledWorkTags & WorkTags.Violent) == 0;
            if (!entry.CanDoViolence)
                return Block(entry, "Incapable of violence");

            entry.MeetsAgeRequirement = ValidationHelper.PassesAgeGate(pawn, out string ageReason);
            if (!entry.MeetsAgeRequirement)
                return Block(entry, ageReason);

            entry.IsValidForWeapons = true;
            return entry;
        }

        private static CachedPawnValidation Block(CachedPawnValidation entry, string reason)
        {
            entry.IsValidForWeapons = false;
            entry.BlockReason = reason;
            return entry;
        }


        public static void InvalidatePawn(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (pawn.RaceProps?.ToolUser != true)
                return;

            _cache.Remove(pawn.thingIDNumber);
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

        private static int deadPawnSweepCounter;

        public static void CleanupDeadPawns(bool force = false)
        {
            if (_cache.Count == 0)
                return;

            deadPawnSweepCounter++;
            if (!force && !TestRunner.IsRunningTests && deadPawnSweepCounter < 8)
                return;
            deadPawnSweepCounter = 0;

            var liveIds = HashSetPool<int>.Get();
            try
            {
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

                if (AutoArmMod.settings?.debugLogging == true && deadCount > 0)
                {
                    AutoArmLogger.Debug(() => $"PawnValidation cleaned up {deadCount} dead pawn entries");
                }
            }
            finally
            {
                HashSetPool<int>.Return(liveIds);
            }
        }

        public static void ClearCache()
        {
            _cache.Clear();
        }
    }

}


using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Testing;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        private static bool testModeEnabled = false;

        private static int globalLastKnownGameTick = -1;
        private static readonly Dictionary<int, int> mapLastProcessedTick = new Dictionary<int, int>();

        internal static void OnMapRemoved(int mapId)
        {
            mapLastProcessedTick.Remove(mapId);
        }

        private static readonly StatDef CachedMass = StatDefOf.Mass;

        private static float? _unarmedDpsThreshold;
        private static float UnarmedDpsThreshold
        {
            get
            {
                if (_unarmedDpsThreshold.HasValue)
                    return _unarmedDpsThreshold.Value;

                float dps = 0f;
                var tools = ThingDefOf.Human?.tools;
                if (tools != null)
                {
                    for (int i = 0; i < tools.Count; i++)
                    {
                        var t = tools[i];
                        if (t?.linkedBodyPartsGroup == BodyPartGroupDefOf.LeftHand
                            || t?.linkedBodyPartsGroup == BodyPartGroupDefOf.RightHand)
                        {
                            if (t.cooldownTime > 0f)
                                dps = t.power / t.cooldownTime;
                            break;
                        }
                    }
                }

                float threshold = (dps > 0f ? dps : 3f) + 2f;
                _unarmedDpsThreshold = threshold;
                return threshold;
            }
        }

        private static readonly HashSet<Thing> EmptyReservationSet = new HashSet<Thing>();
        private static readonly Dictionary<string, int> rejectionReasonsPool = new Dictionary<string, int>();

        private static readonly Comparison<(ThingWithComps weapon, float roughScore)> RoughQueueComparison =
            (a, b) =>
            {
                int cmp = b.roughScore.CompareTo(a.roughScore);
                return cmp != 0 ? cmp : a.weapon.thingIDNumber.CompareTo(b.weapon.thingIDNumber);
            };

        private static readonly Comparison<KeyValuePair<PawnWeaponKey, int>> ValidationCacheExpiryComparison =
            (a, b) => a.Value.CompareTo(b.Value);

        private class MessageDeduplicationInfo
        {
            public string LastContent;
            public int FirstLoggedTick;
            public int Count;
        }

        [Unsaved]
        private static readonly Dictionary<int, MessageDeduplicationInfo> messageCache =
            new Dictionary<int, MessageDeduplicationInfo>();

        private const int MESSAGE_SUPPRESSION_WINDOW = Constants.ShortCacheDuration;

        private static int _lastCacheLogMapId = -1;
        private static string _lastCacheLogOutfit;
        private static int _lastCacheLogCount = -1;

        private static bool ShouldLogCacheState(int mapId, string outfitLabel, int weaponCount)
        {
            if (_lastCacheLogMapId == mapId && _lastCacheLogOutfit == outfitLabel && _lastCacheLogCount == weaponCount)
                return false;
            _lastCacheLogMapId = mapId;
            _lastCacheLogOutfit = outfitLabel;
            _lastCacheLogCount = weaponCount;
            return true;
        }

        private const string MSG_TYPE_NO_WEAPON = "NoWeapon";
        private const string MSG_TYPE_FIND_START = "FindStart";
        private const string MSG_TYPE_OUTFIT_FILTER = "OutfitFilter";
        private const string MSG_TYPE_WEAPON_CACHE = "WeaponCache";
        private const string MSG_TYPE_SKIP = "Skip";
        private const string MSG_TYPE_EVAL = "Eval";

        // Encode key for TickScheduler
        private static int EncodeMessageKey(int pawnId, string messageType)
        {
            return (pawnId * 397) ^ (messageType?.GetHashCode() ?? 0);
        }


        private static bool ShouldLogDebugMessage(Pawn pawn, string messageType, string messageContent)
        {
            int currentTick = Find.TickManager.TicksGame;
            int encodedKey = EncodeMessageKey(pawn.thingIDNumber, messageType);

            if (messageCache.TryGetValue(encodedKey, out var cached))
            {
                if (cached.LastContent == messageContent &&
                    (currentTick - cached.FirstLoggedTick) < MESSAGE_SUPPRESSION_WINDOW)
                {
                    cached.Count++;
                    return false;
                }
                // Cancel old schedule
                TickScheduler.Cancel(TickScheduler.EventType.MessageCacheExpiry, encodedKey);
            }

            messageCache[encodedKey] = new MessageDeduplicationInfo
            {
                LastContent = messageContent,
                FirstLoggedTick = currentTick,
                Count = 1
            };

            // Schedule expiry
            int expireTick = currentTick + MESSAGE_SUPPRESSION_WINDOW;
            TickScheduler.Schedule(expireTick, TickScheduler.EventType.MessageCacheExpiry, encodedKey);

            return true;
        }

        public static void OnMessageCacheExpiredEvent(int encodedKey, int unused)
        {
            messageCache.Remove(encodedKey);
        }

        internal class PawnWeaponState
        {
            public int LastEquipTick = -1;
            public int LastEvaluationTick = -1;
            public int OutfitId = -1;
            public float ShootingSkill = 0f;
            public float MeleeSkill = 0f;
            public bool HasBrawler = false;

            public int LastAttemptedWeaponId = -1;
            public int LastAttemptTick = -1;
            public Dictionary<int, int> TemporarilyBlacklistedWeapons = new Dictionary<int, int>();

            public int IsTemporaryCheckedTick = -1;
            public bool IsTemporaryCached;

            public int HasShieldCheckedTick = -1;
            public bool HasShieldCached;
        }


        [Unsaved]
        private static int lastKnownGameTick = -1;

        private static bool IsFreshlyLoaded()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (lastKnownGameTick == currentTick)
                return false;

            bool noStates = true;
            if (Find.Maps != null)
            {
                foreach (var m in Find.Maps)
                {
                    var c = JobGiverMapComponent.GetComponent(m);
                    if (c != null && c.PawnStates.Count > 0)
                    {
                        noStates = false;
                        break;
                    }
                }
            }
            if (lastKnownGameTick > currentTick ||
                (noStates && currentTick > 600))
            {
                return true;
            }
            return false;
        }



        internal readonly struct PawnWeaponKey : IEquatable<PawnWeaponKey>
        {
            public readonly int PawnId;
            public readonly int WeaponId;

            public PawnWeaponKey(int pawnId, int weaponId)
            {
                PawnId = pawnId;
                WeaponId = weaponId;
            }

            public override int GetHashCode() => (PawnId * 397) ^ WeaponId;

            public override bool Equals(object obj) => obj is PawnWeaponKey key && Equals(key);

            public bool Equals(PawnWeaponKey other) => PawnId == other.PawnId && WeaponId == other.WeaponId;
        }

        internal struct ValidationEntry
        {
            public bool IsValid;
            public int ExpiryTick;
            public bool HadOwner;
        }


        private const int VALIDATION_CACHE_DURATION = Constants.ShortCacheDuration;

        private const int VALIDATION_CACHE_DURATION_FAILED = 3600;



        private const int PROPER_WEAPON_CACHE_DURATION = 3600;


        private const int FAILED_JOB_MEMORY_TICKS = 1200;

        private const int CACHE_JITTER_RANGE = 120;

        private static void CacheInvalid(JobGiverMapComponent comp, PawnWeaponKey key, ThingWithComps weapon, ThingOwner weaponHolder, int currentTick, int multiplier = 1)
        {
            comp.ValidationCache[key] = new ValidationEntry
            {
                IsValid = false,
                ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED * multiplier + GetCacheJitter(weapon),
                HadOwner = weaponHolder != null
            };
        }

        private static bool ResolveProperWeapon(JobGiverMapComponent comp, ThingWithComps weapon, int currentTick)
        {
            if (comp.ProperWeaponCache.TryGetValue(weapon.thingIDNumber, out var cached)
                && currentTick <= cached.expiryTick)
            {
                return cached.isProper;
            }

            bool result = Validation.IsWeapon(weapon);
            comp.ProperWeaponCache[weapon.thingIDNumber] = (result, currentTick + PROPER_WEAPON_CACHE_DURATION);
            return result;
        }


        private static int GetCacheJitter(ThingWithComps weapon)
        {
            return (weapon.thingIDNumber % CACHE_JITTER_RANGE) - (CACHE_JITTER_RANGE / 2);
        }

        public static bool IsWeaponCached(ThingWithComps weapon, Map map)
        {
            if (weapon == null) return false;
            if (map == null) return Validation.IsWeapon(weapon);

            var comp = JobGiverMapComponent.GetComponent(map);
            if (comp == null) return Validation.IsWeapon(weapon);

            return ResolveProperWeapon(comp, weapon, Find.TickManager.TicksGame);
        }

        public static void ResetForTesting()
        {
            JobGiverMapComponent.ClearAllState();
        }

        public static void EnableTestMode(bool enable)
        {
            testModeEnabled = enable;
            if (enable)
            {
                ResetForTesting();
            }
        }

        public static TestModeScope EnterTestMode() => new TestModeScope(true);

        public sealed class TestModeScope : System.IDisposable
        {
            private readonly bool wasEnabled;
            public TestModeScope(bool enterNow)
            {
                wasEnabled = testModeEnabled;
                if (enterNow) EnableTestMode(true);
            }
            public void Dispose() => testModeEnabled = wasEnabled;
        }

        public static void CleanupMessageCache()
        {
            // TickScheduler handles expiry
            if (messageCache.Count > 400)
            {
                messageCache.Clear();
            }
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return TestTryGiveJob(pawn);
        }

        public Job TestTryGiveJob(Pawn pawn)
        {
            bool timingStarted = false;

            if (pawn == null)
            {
                return null;
            }
            if (AutoArmMod.settings?.modEnabled != true)
            {
                return null;
            }

            if (pawn.Map == null || !pawn.Spawned)
            {
                return null;
            }

            if (pawn.Drafted || pawn.InMentalState || pawn.Downed)
            {
                return null;
            }

            if (pawn.GetRegion() == null)
            {
                return null;
            }

            if (pawn.carryTracker?.CarriedThing != null)
            {
                return null;
            }

            int currentTick = Find.TickManager.TicksGame;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp != null && comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var failureState))
            {
                if (failureState.LastAttemptedWeaponId != -1 && failureState.LastAttemptTick != -1)
                {
                    int ticksSinceAttempt = currentTick - failureState.LastAttemptTick;

                    if (ticksSinceAttempt < 250 &&
                        (pawn.jobs?.curJob == null || pawn.jobs.curJob.def != JobDefOf.Equip) &&
                        pawn.equipment?.Primary?.thingIDNumber != failureState.LastAttemptedWeaponId)
                    {
                        int expireTick = currentTick + 250;
                        int blacklistedId = failureState.LastAttemptedWeaponId;
                        failureState.TemporarilyBlacklistedWeapons[blacklistedId] = expireTick;

                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Job failed for weapon ID {blacklistedId}, blacklisting temporarily (expires at tick {expireTick})");

                        failureState.LastAttemptedWeaponId = -1;
                        failureState.LastAttemptTick = -1;
                    }
                    else if (ticksSinceAttempt > 60)
                    {
                        failureState.LastAttemptedWeaponId = -1;
                        failureState.LastAttemptTick = -1;
                    }
                }

            }

            if (!testModeEnabled)
            {
                if (ShouldSkipEvaluation(pawn, currentTick))
                {
                    return null;
                }
            }

            if (!testModeEnabled && !TestRunner.IsRunningTests)
            {
                if (currentTick < globalLastKnownGameTick ||
                    currentTick - globalLastKnownGameTick > 10000)
                {
                    mapLastProcessedTick.Clear();
                }
                globalLastKnownGameTick = currentTick;

                int mapId = pawn.Map.uniqueID;
                if (mapLastProcessedTick.TryGetValue(mapId, out int lastMapTick) && lastMapTick == currentTick)
                {
                    return null;
                }

                mapLastProcessedTick[mapId] = currentTick;
            }

            if (testModeEnabled || TestRunner.IsRunningTests)
            {
                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] TryGiveJob: {AutoArmLogger.GetPawnName(pawn)}");
                }

                if (!PawnValidation.CanConsiderWeapons(pawn))
                {
                    if (TestRunner.IsRunningTests)
                    {
                        AutoArmLogger.Debug(() => $"[TEST] TryGiveJob: {AutoArmLogger.GetPawnName(pawn)} failed CanConsiderWeapons validation");
                    }
                    return null;
                }

                if (TestRunner.IsRunningTests && pawn.DevelopmentalStage < DevelopmentalStage.Adult)
                {
                    bool allowChildren = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
                    int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;
                    int childAge = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                    AutoArmLogger.Debug(() => $"[TEST] Child pawn validation: Age={childAge}, AllowChildren={allowChildren}, MinAge={minAge}, Passes={(allowChildren && childAge >= minAge)}");
                }
            }


            PerfMetrics.StartTiming();
            timingStarted = true;

            bool isEmergency = pawn.equipment?.Primary == null;

            if (AutoArmMod.settings?.debugLogging == true && isEmergency && pawn.IsHashIntervalTick(600))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] EMERGENCY: {AutoArmLogger.GetPawnName(pawn)} is unarmed!");
            }

            if (!isEmergency)
            {
                if (!WeaponCache.HasAnyNonForbiddenWeapons(pawn.Map))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        string msg = "all weapons forbidden or none on map";
                        if (ShouldLogDebugMessage(pawn, MSG_TYPE_SKIP, msg))
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Skipped: {msg}");
                    }
                    if (timingStarted) PerfMetrics.EndTiming();
                    return null;
                }

                var outfit = pawn.outfits?.CurrentApparelPolicy;
                if (outfit?.filter != null)
                {
                    bool hasMatchingWeapon = false;
                    foreach (var weapon in WeaponCache.GetWeaponsForOutfit(pawn.Map, outfit))
                    {
                        hasMatchingWeapon = true;
                        break;
                    }

                    if (!hasMatchingWeapon)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            string msg = $"outfit '{outfit.label}' allows no weapons on map";
                            if (ShouldLogDebugMessage(pawn, MSG_TYPE_SKIP, msg))
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Skipped: {msg}");
                        }
                        if (timingStarted) PerfMetrics.EndTiming();
                        return null;
                    }
                }
            }

            bool justLoaded = IsFreshlyLoaded();
            if (justLoaded)
            {
                JobGiverMapComponent.ClearAllState();
                lastKnownGameTick = currentTick;
                AutoArmLogger.Debug(() => "Detected fresh load - rebuilding pawn states");
            }

            if (comp == null)
            {
                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }
            if (!comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var pawnState))
            {
                pawnState = new PawnWeaponState();
                comp.PawnStates[pawn.thingIDNumber] = pawnState;

                if (justLoaded)
                {
                    pawnState.LastEvaluationTick = currentTick - (pawn.thingIDNumber % 60);
                }
            }

            if (!testModeEnabled && !TestRunner.IsRunningTests && !justLoaded)
            {
                int ticksSinceEquip = currentTick - pawnState.LastEquipTick;
                if (pawnState.LastEquipTick >= 0 && ticksSinceEquip < Constants.WeaponEquipCooldownTicks)
                {
                    if (timingStarted) PerfMetrics.EndTiming();
                    return null;
                }
            }


            lastKnownGameTick = currentTick;

            PerfMetrics.ReportTickProcessing(1);

            var currentWeapon = pawn.equipment?.Primary;
            var weaponRestriction = GetWeaponRestriction(pawn, currentWeapon);

            if (weaponRestriction.blockSearch)
            {
                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] {AutoArmLogger.GetPawnName(pawn)}: Search blocked - restrictToType={weaponRestriction.restrictToType?.defName}, wasForced={weaponRestriction.wasForced}");
                }
                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }

            float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : 0f;

            string failureReason;
            ThingWithComps bestWeapon;
            bestWeapon = FindBestWeapon(pawn, currentScore, weaponRestriction.restrictToType,
                weaponRestriction.wasForced && weaponRestriction.restrictToType != null, out failureReason);

            if (bestWeapon == null && !string.IsNullOrEmpty(failureReason))
            {
                PerfMetrics.ReportFailureReason(failureReason);
            }


            if (SimpleSidearmsCompat.IsLoaded && pawn.equipment?.Primary != null &&
                bestWeapon == null && AutoArmMod.settings?.autoEquipSidearms == true)
            {
                string sidearmFailureReason;
                ThingWithComps potentialSidearm = FindBestSidearm(pawn, out sidearmFailureReason);

                if (potentialSidearm != null)
                {
                    if (!pawn.CanReach(potentialSidearm, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cannot reach sidearm {potentialSidearm.def.defName} - recording as failed");
                        RecordFailedJob(pawn, potentialSidearm);
                    }
                    else
                    {
                        Job sidearmJob = SimpleSidearmsCompat.TryGetWeaponJob(pawn, potentialSidearm);
                        if (sidearmJob != null)
                        {
                            AutoEquipState.MarkAsAutoEquip(sidearmJob, pawn);

                            pawnState.LastEvaluationTick = currentTick - Constants.MaxSkipEvaluationTicks - 1;

                            return sidearmJob;
                        }
                    }
                }
                else if (AutoArmMod.settings?.allowSidearmUpgrades == true &&
                         pawn.inventory?.innerContainer != null)
                {
                    // Pawn at SS limit
                    // Swap doesn't change count
                    var upgradeJob = TryFindSidearmUpgrade(pawn);
                    if (upgradeJob != null)
                    {
                        AutoEquipState.MarkAsAutoEquip(upgradeJob, pawn);
                        pawnState.LastEvaluationTick = currentTick - Constants.MaxSkipEvaluationTicks - 1;
                        return upgradeJob;
                    }
                }
            }

            if (bestWeapon == null)
            {
                if (isEmergency)
                {
                    string messageContent = $"No weapon found: {failureReason}";
                    if (ShouldLogDebugMessage(pawn, MSG_TYPE_NO_WEAPON, messageContent))
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] {messageContent}");
                    }
                    else
                    {
                        if (timingStarted) PerfMetrics.EndTiming();
                        return null;
                    }
                }
                else
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        string msg = $"keeping {currentWeapon?.def?.defName} (score={currentScore:F1}): {failureReason}";
                        if (ShouldLogDebugMessage(pawn, MSG_TYPE_EVAL, msg))
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] {msg}");
                    }
                }

                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] {AutoArmLogger.GetPawnName(pawn)}: No weapon found, current={currentWeapon?.def?.defName ?? "none"} (score={currentScore:F1}), reason: {failureReason}");
                }

                pawnState.LastEvaluationTick = currentTick;

                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }


            if (!pawn.Map.reservationManager.CanReserve(pawn, bestWeapon, 1))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cannot reserve {bestWeapon.def.defName} - already reserved by another pawn");
                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }

            if (bestWeapon.IsForbidden(pawn))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Weapon {bestWeapon.def.defName} is forbidden");
                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }

            if (!CanTakeOrderedJob(pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true || TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cannot take ordered job (game state prevents it)");
                }
                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }

            var curJob = pawn.jobs?.curJob;
            if (curJob != null && curJob.targetA.Thing == bestWeapon)
            {
                bool isWeaponJob = curJob.def == JobDefOf.Equip ||
                                   curJob.def == AutoArmDefOf.AutoArmSwapPrimary ||
                                   curJob.def == AutoArmDefOf.AutoArmSwapSidearm ||
                                   curJob.def == AutoArmDefOf.EquipSecondary;

                if (isWeaponJob)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Already equipping {bestWeapon.def.defName} (job: {curJob.def.defName})");
                    if (timingStarted) PerfMetrics.EndTiming();
                    return null;
                }
            }

            // Throttle repeated attempts at same weapon
            if (pawnState.LastAttemptedWeaponId == bestWeapon.thingIDNumber &&
                currentTick - pawnState.LastAttemptTick < 180)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Recently attempted {bestWeapon.def.defName}, waiting...");
                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }

            if (!pawn.CanReach(bestWeapon, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cannot reach {bestWeapon.def.defName} - recording as failed");
                RecordFailedJob(pawn, bestWeapon);
                if (timingStarted) PerfMetrics.EndTiming();
                return null;
            }

            Job job = JobHelper.CreateEquipJob(bestWeapon, isSidearm: false, pawn: pawn);
            if (job != null)
            {
                WeaponCache.SetTemporaryReservation(bestWeapon, pawn);

                PerfMetrics.ReportJobCreated();
                ConfigureAutoEquipJob(job, pawn, currentWeapon, bestWeapon, weaponRestriction.wasForced);

                pawnState.LastAttemptedWeaponId = bestWeapon.thingIDNumber;
                pawnState.LastAttemptTick = currentTick;

                if (isEmergency)
                {
                    job.expiryInterval = Constants.EmergencyJobExpiry;
                    job.checkOverrideOnExpire = false;
                }


                pawnState.LastEvaluationTick = currentTick - Constants.MaxSkipEvaluationTicks - 1;
            }

            if (timingStarted)
            {
                PerfMetrics.EndTiming();
            }


            return job;
        }

        public static void RecordWeaponEquip(Pawn pawn)
        {
            if (pawn == null) return;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp == null) return;
            if (!comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var state))
            {
                state = new PawnWeaponState();
                comp.PawnStates[pawn.thingIDNumber] = state;
            }

            state.LastEquipTick = Find.TickManager.TicksGame;

            CooldownMetrics.OnPawnEquippedWeapon(pawn);
        }

        public static void ClearWeaponCooldown(Pawn pawn)
        {
            if (pawn == null) return;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp != null && comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var state))
            {
                state.LastEquipTick = -1;
            }
        }

        public static int GetRemainingCooldown(Pawn pawn)
        {
            if (pawn == null) return 0;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp != null && comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var state) && state.LastEquipTick >= 0)
            {
                int currentTick = Find.TickManager.TicksGame;
                int ticksSinceEquip = currentTick - state.LastEquipTick;

                if (ticksSinceEquip < Constants.WeaponEquipCooldownTicks)
                {
                    return Constants.WeaponEquipCooldownTicks - ticksSinceEquip;
                }
            }

            return 0;
        }


        private (ThingDef restrictToType, bool blockSearch, bool wasForced) GetWeaponRestriction(Pawn pawn, ThingWithComps currentWeapon)
        {
            if (!ForcedWeapons.SomethingIsForced(pawn))
                return (null, false, false);

            if (currentWeapon != null)
            {
                bool isForced = ForcedWeapons.IsForced(pawn, currentWeapon);
                if (isForced)
                {
                    if (AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Blocking weapon search: primary is force-equipped and upgrades are disabled");
                        return (null, true, true);
                    }
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Restricting search to {AutoArmLogger.GetDefLabel(currentWeapon.def)}: primary is force-equipped with upgrades enabled");
                    return (currentWeapon.def, false, true);
                }
            }

            if (pawn.inventory?.innerContainer != null && AutoArmMod.settings?.allowForcedWeaponUpgrades == true)
            {
                foreach (Thing thing in pawn.inventory.innerContainer)
                {
                    var sidearm = thing as ThingWithComps;
                    if (sidearm == null || !sidearm.def.IsWeapon)
                        continue;

                    if (ForcedWeapons.IsForced(pawn, sidearm))
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Found force-equipped sidearm {AutoArmLogger.GetDefLabel(sidearm.def)}, searching for upgrades");
                        return (sidearm.def, false, true);
                    }
                }
            }

            return (null, false, false);
        }

        private static bool IsPawnTemporaryCached(Pawn pawn, JobGiverMapComponent comp, int currentTick)
        {
            if (comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var state) && state.IsTemporaryCheckedTick == currentTick)
                return state.IsTemporaryCached;

            bool isTemp = JobHelper.IsTemporary(pawn);

            if (state == null)
            {
                state = new PawnWeaponState();
                comp.PawnStates[pawn.thingIDNumber] = state;
            }
            state.IsTemporaryCheckedTick = currentTick;
            state.IsTemporaryCached = isTemp;
            return isTemp;
        }

        private static bool HasRangedBlockingShieldCached(Pawn pawn, JobGiverMapComponent comp, int currentTick)
        {
            if (comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var state) && state.HasShieldCheckedTick == currentTick)
                return state.HasShieldCached;

            bool hasShield = HasRangedBlockingShield(pawn);

            if (state == null)
            {
                state = new PawnWeaponState();
                comp.PawnStates[pawn.thingIDNumber] = state;
            }
            state.HasShieldCheckedTick = currentTick;
            state.HasShieldCached = hasShield;
            return hasShield;
        }

        public bool ShouldConsiderWeapon(Pawn pawn, ThingWithComps weapon, ThingWithComps currentWeapon, bool isForcedUpgrade = false, List<ThingWithComps> inventoryWeapons = null, JobGiverMapComponent mapComp = null)
        {
            if (isForcedUpgrade && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade: validating {weapon.Label}");
            }


            bool isUnarmed = pawn.equipment?.Primary == null;

            bool shouldLogRejection = isUnarmed && AutoArmMod.settings?.debugLogging == true;

            if (TestRunner.IsRunningTests && AutoArmMod.settings?.debugLogging == true &&
                pawn.DevelopmentalStage < DevelopmentalStage.Adult)
            {
                AutoArmLogger.Debug(() => $"[TEST] ShouldConsiderWeapon for child {AutoArmLogger.GetPawnName(pawn)}: weapon={weapon.def.defName}");
            }


            int currentTick = Find.TickManager.TicksGame;
            var comp = mapComp ?? JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp == null) return false;

            if (weapon.Destroyed)
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: destroyed");
                return false;
            }

            if (weapon.Map != pawn.Map)
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: on different map");
                return false;
            }

            if (!weapon.Position.IsValid || !weapon.Position.InBounds(pawn.Map))
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: invalid position");
                return false;
            }

            var weaponHolder = weapon.holdingOwner;

            if (weaponHolder != null)
            {
                var holder = weaponHolder.Owner;

                if (holder is Pawn otherPawn && otherPawn != pawn)
                {
                    if (otherPawn.equipment?.Primary == weapon)
                    {
                        if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: equipped by someone else");
                        return false;
                    }

                    if (otherPawn.inventory?.innerContainer?.Contains(weapon) == true)
                    {
                        if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: in someone's inventory");
                        return false;
                    }

                    if (otherPawn.carryTracker?.CarriedThing == weapon)
                    {
                        if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: being carried");
                        return false;
                    }
                }
            }

            var cacheKey = new PawnWeaponKey(pawn.thingIDNumber, weapon.thingIDNumber);

            bool bypassCache = isForcedUpgrade;
            if (bypassCache)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade: skipping validation cache for {weapon.Label}");
            }
            else if (comp.ValidationCache.TryGetValue(cacheKey, out var cached))
            {
                if (currentTick < cached.ExpiryTick)
                {
                    bool currentlyHasOwner = weaponHolder != null;
                    bool biocodeChanged = cached.IsValid && Caching.Components.IsBiocodedToOther(weapon, pawn);

                    if (weapon.Destroyed || cached.HadOwner != currentlyHasOwner || biocodeChanged)
                    {
                        comp.ValidationCache.Remove(cacheKey);
                    }
                    else
                    {
                        if (cached.IsValid)
                        {
                            if (weapon.IsBurning())
                                return false;
                            if (CECompat.IsLoaded && CECompat.ShouldSkipWeaponForCE(weapon, pawn))
                                return false;

                            var forbiddenHit = pawn.genes?.Xenotype?.forbiddenWeaponClasses;
                            if (forbiddenHit != null && !weapon.def.weaponClasses.NullOrEmpty())
                            {
                                for (int i = 0; i < forbiddenHit.Count; i++)
                                {
                                    if (weapon.def.weaponClasses.Contains(forbiddenHit[i]))
                                        return false;
                                }
                            }

                            if (EquipmentUtility.AlreadyBondedToWeapon(weapon, pawn))
                                return false;

                            if (weapon.def.IsRangedWeapon)
                            {
                                if (!Caching.PawnValidation.CanShoot(pawn))
                                    return false;
                                if (Caching.PawnValidation.IsBrawler(pawn))
                                    return false;
                                if (HasRangedBlockingShieldCached(pawn, comp, currentTick))
                                    return false;
                            }
                        }
                        PerfMetrics.ReportCacheHit();
                        return cached.IsValid;
                    }
                }
            }

            bool isProperWeapon = ResolveProperWeapon(comp, weapon, currentTick);
            if (!isProperWeapon)
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: not a valid weapon");
                return false;
            }

            if (weapon.IsBurning())
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: on fire");
                return false;
            }

            if (Caching.Components.IsBiocodedToOther(weapon, pawn))
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: biocoded to another pawn");
                return false;
            }

            if (EquipmentUtility.AlreadyBondedToWeapon(weapon, pawn))
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: already bonded to different bladelink");
                return false;
            }

            var forbiddenClasses = pawn.genes?.Xenotype?.forbiddenWeaponClasses;
            if (forbiddenClasses != null && !weapon.def.weaponClasses.NullOrEmpty())
            {
                for (int i = 0; i < forbiddenClasses.Count; i++)
                {
                    if (weapon.def.weaponClasses.Contains(forbiddenClasses[i]))
                    {
                        if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: forbidden by xenotype ({pawn.genes.Xenotype.LabelCap})");
                        return false;
                    }
                }
            }

            if (weapon.def.IsRangedWeapon && !Caching.PawnValidation.CanShoot(pawn))
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: shooting disabled");
                return false;
            }

            if (CECompat.ShouldSkipWeaponForCE(weapon, pawn))
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: no CE ammo available");
                return false;
            }

            if (weapon.def.IsMeleeWeapon)
            {
                float meleeDps = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS, applyPostProcess: true, cacheStaleAfterTicks: 2500);
                if (meleeDps < UnarmedDpsThreshold)
                {
                    if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: DPS {meleeDps:F1} below unarmed threshold {UnarmedDpsThreshold:F1}");
                    return false;
                }
            }



            if (DroppedItems.IsDropped(weapon))
            {
                if (shouldLogRejection)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: just dropped it recently");
                return false;
            }

            var pawnLastDrop = DroppedItems.GetLastDropped(pawn);
            if (pawnLastDrop != null && pawnLastDrop == weapon)
            {
                if (shouldLogRejection)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: own recent drop");
                return false;
            }

            if (comp.PawnStates.TryGetValue(pawn.thingIDNumber, out var blacklistState) &&
                blacklistState.TemporarilyBlacklistedWeapons.TryGetValue(weapon.thingIDNumber, out var blacklistExpireTick))
            {
                if (currentTick >= blacklistExpireTick)
                {
                    blacklistState.TemporarilyBlacklistedWeapons.Remove(weapon.thingIDNumber);
                }
                else
                {
                    if (shouldLogRejection || AutoArmMod.settings?.debugLogging == true)
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: temporarily blacklisted (failed recently)");
                    return false;
                }
            }

            if (weapon.questTags != null && weapon.questTags.Count > 0)
            {
                if (shouldLogRejection)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: quest item");
                return false;
            }

            if (currentWeapon != null && currentWeapon.def == weapon.def && weapon != currentWeapon)
            {
                QualityCategory curQ = QualityCategory.Normal;
                QualityCategory newQ = QualityCategory.Normal;
                Caching.Components.TryGetWeaponQuality(currentWeapon, out curQ);
                Caching.Components.TryGetWeaponQuality(weapon, out newQ);

                if (newQ <= curQ)
                {
                    CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                    return false;
                }
            }


            if (!IsValidSize(pawn, weapon))
            {
                Blacklist.AddToBlacklist(weapon.def, pawn, "Body size requirement not met");

                CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                return false;
            }

            if (Blacklist.IsBlacklisted(weapon.def, pawn))
            {
                CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                return false;
            }


            if (weapon.def.IsRangedWeapon && Caching.PawnValidation.IsBrawler(pawn))
            {
                CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                return false;
            }

            if (weapon.def.IsRangedWeapon && HasRangedBlockingShieldCached(pawn, comp, currentTick))
            {
                CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                return false;
            }

            string cantEquipReason;
            if (!EquipEligibility.CanEquip(pawn, weapon, out cantEquipReason, checkBonded: true))
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't equip {AutoArmLogger.GetWeaponLabelLower(weapon)}: {cantEquipReason}");

                CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                return false;
            }

            if (ModsConfig.IdeologyActive)
            {
                string ideologyReason;
                if (ValidationHelper.TryGetIdeologyWeaponBlock(weapon, pawn, out ideologyReason, out _))
                {
                    if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: {ideologyReason ?? "ideology forbids it"}");
                    }

                    CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                    return false;
                }
            }



            ThingWithComps existingSidearm = null;

            if (inventoryWeapons != null)
            {
                foreach (ThingWithComps invWeapon in inventoryWeapons)
                {
                    if (invWeapon.def == weapon.def)
                    {
                        existingSidearm = invWeapon;
                        break;
                    }
                }
            }
            else if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing item in pawn.inventory.innerContainer)
                {
                    if (item is ThingWithComps invWeapon && invWeapon.def == weapon.def)
                    {
                        existingSidearm = invWeapon;
                        break;
                    }
                }
            }

            if (existingSidearm != null)
            {
                if (isForcedUpgrade)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade: allowing duplicate {weapon.Label} for quality check");
                }
                else
                {
                    QualityCategory existingQuality = QualityCategory.Normal;
                    QualityCategory newQuality = QualityCategory.Normal;
                    Caching.Components.TryGetWeaponQuality(existingSidearm, out existingQuality);
                    Caching.Components.TryGetWeaponQuality(weapon, out newQuality);

                    bool isForcedWeapon = ForcedWeapons.IsForced(pawn, existingSidearm);
                    bool allowForcedUpgrades = AutoArmMod.settings?.allowForcedWeaponUpgrades == true;

                    if (isForcedWeapon && !allowForcedUpgrades)
                    {
                        if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: existing sidearm is forced and forced-weapon upgrades are disabled");

                        CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                        return false;
                    }

                    if (isForcedWeapon)
                    {
                        if (newQuality < existingQuality)
                        {
                            if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: forced sidearm has better quality");

                            CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                            return false;
                        }
                    }
                    else
                    {
                        if (newQuality <= existingQuality)
                        {
                            if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: already have same or better quality sidearm");

                            CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                            return false;
                        }
                    }

                    float existingScore = GetWeaponScore(pawn, existingSidearm);
                    float newScore = GetWeaponScore(pawn, weapon);
                    float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;

                    if (isForcedWeapon && allowForcedUpgrades)
                    {
                        threshold = Math.Min(threshold, 1.01f);
                    }

                    if (newScore <= existingScore * threshold)
                    {
                        if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: not enough of a sidearm upgrade (need {existingScore * threshold:F1}, got {newScore:F1})");

                        CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                        return false;
                    }
                }
            }

            if (IsPawnTemporaryCached(pawn, comp, currentTick) && !(AutoArmMod.settings?.allowTemporaryColonists ?? false))
            {
                CacheInvalid(comp, cacheKey, weapon, weaponHolder, currentTick);
                return false;
            }

            comp.ValidationCache[cacheKey] = new ValidationEntry
            {
                IsValid = true,
                ExpiryTick = currentTick + VALIDATION_CACHE_DURATION + GetCacheJitter(weapon),
                HadOwner = weaponHolder != null
            };


            return true;
        }


        private void ConfigureAutoEquipJob(Job job, Pawn pawn, ThingWithComps currentWeapon, ThingWithComps newWeapon, bool wasForced)
        {
            AutoEquipState.MarkAsAutoEquip(job, pawn);

            string previousLabel = null;
            if (AutoArmMod.settings?.showNotifications == true && currentWeapon != null)
            {
                previousLabel = currentWeapon.LabelCap;
            }
            AutoEquipState.SetPreviousWeapon(pawn, previousLabel);

            if (wasForced && AutoArmMod.settings?.allowForcedWeaponUpgrades == true)
            {
                AutoEquipState.SetWeaponToForce(pawn, newWeapon);
            }
        }


        public ThingWithComps FindBestWeapon(Pawn pawn, float currentScore, ThingDef restrictToType, bool isForcedUpgrade, out string primaryFailureReason)
        {
            primaryFailureReason = "No weapons found";


            if (isForcedUpgrade && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade search: currentScore={currentScore:F1}, restrictToType={restrictToType?.defName}");
            }

            bool isUnarmed = pawn.equipment?.Primary == null;
            bool storageOnly = AutoArmMod.settings?.onlyAutoEquipFromStorage == true;
            float userThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
            float minAcceptableScore = isUnarmed ? 0.01f : currentScore * userThreshold;
            if (TestRunner.IsRunningTests)
            {
                AutoArmLogger.Debug(() => $"[TEST] FindBestWeapon for {AutoArmLogger.GetPawnName(pawn)}: isUnarmed={isUnarmed}, currentScore={currentScore}, restrictToType={restrictToType?.defName ?? "none"}");
            }

            var roughQueue = ListPool<(ThingWithComps weapon, float roughScore)>.Get(256);
            List<ThingWithComps> filteredSearchSet = null;
            List<ThingWithComps> pawnInventoryWeapons = null;

            int totalWeaponsAvailable = 0;
            int totalWeaponsSearched = 0;
            rejectionReasonsPool.Clear();
            var rejectionReasons = rejectionReasonsPool;
            ThingWithComps bestWeapon = null;
            float bestScore = 0f;

            try
            {


                List<ThingWithComps> searchSet = null;
                if (storageOnly)
                {
                    WeaponCache.EnsureCacheExists(pawn.Map);
                    var storageWeapons = WeaponCache.GetAllStorageWeapons(pawn.Map);

                    if (restrictToType != null)
                    {
                        filteredSearchSet = ListPool<ThingWithComps>.Get();
                        foreach (var weapon in storageWeapons)
                        {
                            if (weapon.def == restrictToType)
                                filteredSearchSet.Add(weapon);
                        }
                        searchSet = filteredSearchSet;
                        totalWeaponsAvailable = filteredSearchSet.Count;
                    }
                    else
                    {
                        totalWeaponsAvailable = storageWeapons.Count;
                        searchSet = storageWeapons;
                    }
                }
                else
                {
                    WeaponCache.EnsureCacheExists(pawn.Map);

                    List<ThingWithComps> allWeapons;
                    if (isForcedUpgrade && restrictToType != null)
                    {
                        allWeapons = WeaponCache.GetAllWeapons(pawn.Map);
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade: skipping outfit filter for {restrictToType.defName}");
                    }
                    else if (pawn.outfits?.CurrentApparelPolicy != null)
                    {
                        allWeapons = WeaponCache.GetWeaponsForOutfit(pawn.Map, pawn.outfits.CurrentApparelPolicy);
                    }
                    else
                    {
                        allWeapons = WeaponCache.GetAllWeapons(pawn.Map);
                    }

                    if (AutoArmMod.settings?.debugLogging == true && isUnarmed)
                    {
                        var weaponCount = WeaponCache.GetCacheWeaponCount(pawn.Map);
                        var outfitLabel = pawn.outfits?.CurrentApparelPolicy?.label;
                        if (ShouldLogCacheState(pawn.Map.uniqueID, outfitLabel, weaponCount))
                        {
                            string outfitInfo = outfitLabel != null ? $" (outfit '{outfitLabel}')" : string.Empty;
                            string messageContent = weaponCount == 0
                                ? $"No weapons in cache{outfitInfo}"
                                : $"{weaponCount} weapons in cache{outfitInfo}";
                            AutoArmLogger.Debug(() => messageContent);
                        }
                    }


                    if (restrictToType != null)
                    {
                        // Type-restricted search
                        filteredSearchSet = ListPool<ThingWithComps>.Get();
                        foreach (var w in allWeapons)
                        {
                            if (!IsValidSize(pawn, w))
                                continue;

                            if (w.def == restrictToType)
                                filteredSearchSet.Add(w);
                        }
                        searchSet = filteredSearchSet;
                        totalWeaponsAvailable = filteredSearchSet.Count;

                        if (isForcedUpgrade && AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Found {totalWeaponsAvailable} {restrictToType.defName} weapons for forced upgrade evaluation");
                        }
                    }
                    else
                    {
                        // No pre-filtering
                        searchSet = allWeapons;
                        totalWeaponsAvailable = -1; // Unknown until we iterate
                    }
                }


                int currentTick = Find.TickManager.TicksGame;
                int evaluatedWeapons = 0;

                // Cached per-tick
                var mapComp = JobGiverMapComponent.GetComponent(pawn.Map);
                var reservedThings = mapComp?.GetReservedThings(currentTick) ?? EmptyReservationSet;

                if (pawn.inventory?.innerContainer != null)
                {
                    pawnInventoryWeapons = ListPool<ThingWithComps>.Get(pawn.inventory.innerContainer.Count);
                    foreach (Thing item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps invWeapon && invWeapon.def.IsWeapon)
                        {
                            pawnInventoryWeapons.Add(invWeapon);
                        }
                    }
                }


                // Cap rough candidates
                int maxRoughCandidates = isUnarmed ? 80 : 60;

                // Hard iteration cap
                int maxIterations = isUnarmed ? 300 : 200;

                if (searchSet != null)
                {
                    foreach (var weapon in searchSet)
                    {
                        totalWeaponsSearched++;

                        if (weapon == null)
                            continue;

                        // Size check
                        if (!IsValidSize(pawn, weapon))
                            continue;

                        bool shouldConsider = ShouldConsiderWeapon(pawn, weapon, pawn.equipment?.Primary, isForcedUpgrade, pawnInventoryWeapons, mapComp);

                        if (!shouldConsider)
                        {
                            string reason = "Unknown rejection";
                            var weaponOwner = weapon.holdingOwner;
                            if (weaponOwner != null)
                            {
                                var holder = weaponOwner.Owner;
                                if (holder is Pawn otherPawn && otherPawn != pawn)
                                {
                                    reason = "Already owned";
                                }
                                else
                                {
                                    if (DroppedItems.IsDropped(weapon)) reason = "Recently dropped";
                                    else if (Blacklist.IsBlacklisted(weapon.def, pawn)) reason = "Blacklisted";
                                    else reason = "Failed validation";
                                }
                            }
                            else if (DroppedItems.IsDropped(weapon)) reason = "Recently dropped";
                            else if (Blacklist.IsBlacklisted(weapon.def, pawn)) reason = "Blacklisted";

                            if (isForcedUpgrade && restrictToType != null && weapon.def == restrictToType &&
                                AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade: rejected {weapon.Label} ({reason})");
                            }

                            rejectionReasons.TryGetValue(reason, out int n);
                            rejectionReasons[reason] = n + 1;
                            continue;
                        }

                        var failKey = new PawnWeaponKey(pawn.thingIDNumber, weapon.thingIDNumber);
                        if (mapComp != null && mapComp.FailedJobHistory.TryGetValue(failKey, out int lastFailTick))
                        {
                            if (currentTick - lastFailTick < FAILED_JOB_MEMORY_TICKS)
                            {
                                string reason = "Recently unreachable";
                                rejectionReasons.TryGetValue(reason, out int n);
                                rejectionReasons[reason] = n + 1;
                                continue;
                            }
                            else
                            {
                                mapComp.FailedJobHistory.Remove(failKey);
                            }
                        }

                        if (WeaponCache.HasTemporaryReservation(weapon, pawn))
                        {
                            string reason = "Reserved for another";
                            rejectionReasons.TryGetValue(reason, out int n);
                            rejectionReasons[reason] = n + 1;
                            continue;
                        }

                        if (reservedThings.Contains(weapon))
                        {
                            if (!pawn.CanReserve(weapon))
                            {
                                string reason = "Reserved";
                                rejectionReasons.TryGetValue(reason, out int n);
                                rejectionReasons[reason] = n + 1;
                                continue;
                            }
                        }

                        float rough = Scoring.GetWeaponPropertyScore(pawn, weapon);
                        roughQueue.Add((weapon, rough));

                        // Stop at cap
                        if (roughQueue.Count >= maxRoughCandidates || totalWeaponsSearched >= maxIterations)
                            break;
                    }

                    if (roughQueue.Count > 1)
                    {
                        roughQueue.Sort(RoughQueueComparison);
                    }

                    var currentWeapon = pawn.equipment?.Primary;

                    float topRoughScore = roughQueue.Count > 0 ? roughQueue[0].roughScore : 0f;

                    foreach (var item in roughQueue)
                    {
                        var weapon = item.weapon;

                        if (!isUnarmed && currentWeapon != null
                            && !isForcedUpgrade
                            && weapon.def.IsRangedWeapon != currentWeapon.def.IsRangedWeapon
                            && SimpleSidearmsCompat.IsManagingPawn(pawn)
                            && !SimpleSidearmsCompat.CanPickupSidearm(weapon, pawn, out _))
                        {
                            string skipReason = "Cross-type swap blocked by SS";
                            rejectionReasons.TryGetValue(skipReason, out int n);
                            rejectionReasons[skipReason] = n + 1;
                            continue;
                        }

                        float newScore = GetWeaponScore(pawn, weapon);

                        if (isForcedUpgrade && restrictToType != null && !isUnarmed && weapon.def == restrictToType)
                        {
                            ThingWithComps forcedWeaponToCompare = null;

                            if (pawn.equipment?.Primary != null && pawn.equipment.Primary.def == restrictToType)
                            {
                                forcedWeaponToCompare = pawn.equipment.Primary;
                            }
                            else if (pawn.inventory?.innerContainer != null)
                            {
                                foreach (Thing invItem in pawn.inventory.innerContainer)
                                {
                                    if (invItem is ThingWithComps sidearm && sidearm.def == restrictToType &&
                                        ForcedWeapons.IsForced(pawn, sidearm))
                                    {
                                        forcedWeaponToCompare = sidearm;
                                        break;
                                    }
                                }
                            }

                            if (forcedWeaponToCompare != null)
                            {
                                QualityCategory currentQuality = QualityCategory.Normal;
                                QualityCategory newQuality = QualityCategory.Normal;
                                Caching.Components.TryGetWeaponQuality(forcedWeaponToCompare, out currentQuality);
                                Caching.Components.TryGetWeaponQuality(weapon, out newQuality);

                                float forcedCurrentScore = GetWeaponScore(pawn, forcedWeaponToCompare);

                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade compare: {weapon.Label} ({newQuality}, {newScore:F1}) against {forcedWeaponToCompare.Label} ({currentQuality}, {forcedCurrentScore:F1})");

                                if (newQuality < currentQuality)
                                {
                                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Rejecting forced upgrade: worse quality ({newQuality} < {currentQuality})");
                                    continue;
                                }

                                if (newScore <= forcedCurrentScore * 1.01f)
                                {
                                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Rejecting forced upgrade: insufficient score improvement ({newScore:F1} <= {forcedCurrentScore * 1.01f:F1})");
                                    continue;
                                }

                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade passed");
                            }
                            else
                            {
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade check: current weapon not found for type {restrictToType?.defName}");
                            }
                        }
                        else if (newScore <= minAcceptableScore)
                        {
                            continue;
                        }

                        bool canReserve = !reservedThings.Contains(weapon) || pawn.CanReserve(weapon);

                        if (canReserve && newScore > bestScore)
                        {
                            bestWeapon = weapon;
                            bestScore = newScore;
                        }

                        evaluatedWeapons++;

                        // Cap full evaluations
                        // Unarmed get more
                        int maxEvaluations = isUnarmed ? 40 : 25;
                        if (evaluatedWeapons >= maxEvaluations && bestWeapon != null)
                        {
                            break;
                        }

                        if (bestWeapon != null && item.roughScore < topRoughScore * 0.5f)
                        {
                            break;
                        }
                    }
                }

                if (TestRunner.IsRunningTests && bestWeapon == null)
                {
                    AutoArmLogger.Debug(() => $"[TEST] FindBestWeapon: no result, searched {totalWeaponsSearched}/{totalWeaponsAvailable}, evaluated {evaluatedWeapons}");
                    if (rejectionReasons.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        bool first = true;
                        foreach (var kvp in rejectionReasons)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(kvp.Key).Append('=').Append(kvp.Value);
                            first = false;
                        }
                        AutoArmLogger.Debug(() => $"[TEST] Rejection reasons: {sb}");
                    }
                }

                PerfMetrics.ReportSearchStats(totalWeaponsSearched);

                if (bestWeapon == null && rejectionReasons.Count > 0)
                {
                    KeyValuePair<string, int> topReason = default(KeyValuePair<string, int>);
                    foreach (var kvp in rejectionReasons)
                    {
                        if (topReason.Key == null || kvp.Value > topReason.Value)
                            topReason = kvp;
                    }
                    if (topReason.Key != null)
                    {
                        primaryFailureReason = topReason.Key;
                    }
                }

                if (bestWeapon != null)
                {
                    AutoArmLogger.Debug(() => isUnarmed
                            ? $"[{AutoArmLogger.GetPawnName(pawn)}] Best weapon found: {AutoArmLogger.GetWeaponLabelLower(bestWeapon)} (score {bestScore:F1})"
                            : $"[{AutoArmLogger.GetPawnName(pawn)}] Best weapon: {bestWeapon.def.defName} (score {bestScore:F1})");
                }

                if (bestWeapon != null)
                {
                    return bestWeapon;
                }

                if (isForcedUpgrade && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade: no weapon found");
                    AutoArmLogger.Debug(() => $"  - Total weapons searched: {totalWeaponsSearched}");
                    AutoArmLogger.Debug(() => $"  - Total weapons available: {totalWeaponsAvailable}");
                    if (rejectionReasons.Count > 0)
                    {
                        AutoArmLogger.Debug(() => $"  - Rejection reasons:");
                        foreach (var kvp in rejectionReasons)
                        {
                            AutoArmLogger.Debug(() => $"    - {kvp.Key}: {kvp.Value}");
                        }
                    }
                }

                return null;
            }
            finally
            {
                Blacklist.FlushPendingLogs();

                ListPool<(ThingWithComps weapon, float roughScore)>.Return(roughQueue);
                if (filteredSearchSet != null)
                    ListPool<ThingWithComps>.Return(filteredSearchSet);
                if (pawnInventoryWeapons != null)
                    ListPool<ThingWithComps>.Return(pawnInventoryWeapons);

            }
        }

        public ThingWithComps FindBestSidearm(Pawn pawn, out string failureReason)
            => FindSidearmInternal(pawn, upgradeOnly: false, out failureReason);

        private Job TryFindSidearmUpgrade(Pawn pawn)
        {
            var weapon = FindSidearmInternal(pawn, upgradeOnly: true, out _);
            return weapon != null ? SimpleSidearmsCompat.TryGetWeaponJob(pawn, weapon) : null;
        }

        private ThingWithComps FindSidearmInternal(Pawn pawn, bool upgradeOnly, out string failureReason)
        {
            failureReason = "No suitable sidearms found";

            if (pawn?.Map == null)
                return null;

            if (upgradeOnly)
            {
                if (pawn.inventory?.innerContainer == null)
                    return null;
            }
            else
            {
                if (pawn.equipment?.Primary == null)
                {
                    failureReason = "No primary weapon";
                    return null;
                }
                if (!SimpleSidearmsCompat.IsReady)
                {
                    failureReason = "SimpleSidearms not ready";
                    return null;
                }
            }

            var existingDefs = ListPool<ThingDef>.Get(4);
            var inventoryWeapons = ListPool<ThingWithComps>.Get(4);

            if (!upgradeOnly)
            {
                var primaryDef = pawn.equipment.Primary?.def;
                if (primaryDef != null)
                    existingDefs.Add(primaryDef);
            }

            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
            {
                foreach (var item in inv)
                {
                    if (item is ThingWithComps w && Validation.IsWeapon(w.def))
                    {
                        inventoryWeapons.Add(w);
                        if (!existingDefs.Contains(w.def))
                            existingDefs.Add(w.def);
                    }
                }
            }

            if (upgradeOnly && existingDefs.Count == 0)
            {
                ListPool<ThingDef>.Return(existingDefs);
                ListPool<ThingWithComps>.Return(inventoryWeapons);
                return null;
            }

            bool storageOnly = AutoArmMod.settings?.onlyAutoEquipFromStorage == true;
            var outfit = pawn.outfits?.CurrentApparelPolicy;

            List<ThingWithComps> candidateWeapons;
            if (storageOnly && outfit != null)
                candidateWeapons = WeaponCache.GetStorageWeapons(pawn.Map, outfit);
            else if (storageOnly)
                candidateWeapons = WeaponCache.GetAllStorageWeapons(pawn.Map);
            else if (outfit != null)
                candidateWeapons = WeaponCache.GetWeaponsForOutfit(pawn.Map, outfit);
            else
                candidateWeapons = WeaponCache.GetAllWeapons(pawn.Map);

            ThingWithComps bestSidearm = null;
            float bestScore = 0f;

            int iterations = 0;
            const int maxIterations = 150;

            foreach (var weapon in candidateWeapons)
            {
                if (++iterations > maxIterations)
                    break;

                if (weapon == null || weapon.Destroyed || weapon.Map != pawn.Map)
                    continue;

                if (weapon == pawn.equipment?.Primary)
                    continue;

                bool hasDef = existingDefs.Contains(weapon.def);
                if (upgradeOnly ? !hasDef : hasDef)
                    continue;

                if (inv?.Contains(weapon) == true)
                    continue;

                if (!ShouldConsiderWeapon(pawn, weapon, pawn.equipment?.Primary, false, inventoryWeapons))
                    continue;

                if (!upgradeOnly)
                {
                    string reason;
                    if (!SimpleSidearmsCompat.CanPickupSidearm(weapon, pawn, out reason))
                        continue;
                }

                if (!pawn.CanReserve(weapon))
                    continue;

                float score = GetWeaponScore(pawn, weapon);

                if (!upgradeOnly)
                {
                    bool primaryIsRanged = pawn.equipment.Primary.def.IsRangedWeapon;
                    bool sidearmIsRanged = weapon.def.IsRangedWeapon;
                    if (primaryIsRanged != sidearmIsRanged)
                        score *= 1.5f;
                }

                if (score > bestScore)
                {
                    if (upgradeOnly && SimpleSidearmsCompat.TryGetWeaponJob(pawn, weapon) == null)
                        continue;

                    bestScore = score;
                    bestSidearm = weapon;
                }
            }

            ListPool<ThingDef>.Return(existingDefs);
            ListPool<ThingWithComps>.Return(inventoryWeapons);

            if (!upgradeOnly)
            {
                if (bestSidearm == null)
                    failureReason = "No valid sidearms available";
                Blacklist.FlushPendingLogs();
            }

            return bestSidearm;
        }


        private bool CanTakeOrderedJob(Pawn pawn)
        {
            if (pawn?.jobs == null)
                return false;

            var curJob = pawn.jobs.curJob;
            if (curJob != null)
            {
                if (curJob.def == JobDefOf.Rescue ||
                    curJob.def == JobDefOf.TendPatient ||
                    curJob.def == JobDefOf.ExtinguishSelf ||
                    curJob.def == JobDefOf.BeatFire)
                {
                    return false;
                }
            }

            return true;
        }

        public float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn == null)
                return 0f;

            return WeaponCache.GetCachedScore(pawn, weapon);
        }


        [Unsaved]
        private static readonly Dictionary<ThingDef, float> weaponBodySizeCache = new Dictionary<ThingDef, float>();

        [Unsaved]
        private static readonly Dictionary<ThingDef, int> weaponBodySizeCacheAccessTicks = new Dictionary<ThingDef, int>();

        private const int MaxWeaponBodySizeCacheSize = 500;

        [Unsaved]
        private static readonly Dictionary<ThingDef, float> modExtensionBodySizeCache = new Dictionary<ThingDef, float>();

        private static bool modExtensionCacheInitialized = false;

        private static bool HasRangedBlockingShield(Pawn pawn)
        {
            var worn = pawn?.apparel?.WornApparel;
            if (worn == null)
                return false;

            for (int i = 0; i < worn.Count; i++)
            {
                var shield = worn[i].GetComp<CompShield>();
                if (shield?.Props?.blocksRangedWeapons == true)
                    return true;
            }
            return false;
        }

        public bool IsValidSize(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return true;

            if (ValidationHelper.PassesAgeGate(pawn, out _))
                return true;

            if (!weaponBodySizeCache.TryGetValue(weapon.def, out float requiredSize))
            {
                requiredSize = DetermineBodySizeRequirement(weapon.def);

                if (weaponBodySizeCache.Count >= MaxWeaponBodySizeCacheSize)
                {
                    if (weaponBodySizeCacheAccessTicks.Count > 0)
                    {
                        KeyValuePair<ThingDef, int> oldestEntry = default;
                        int oldestTick = int.MaxValue;
                        bool found = false;

                        foreach (var kvp in weaponBodySizeCacheAccessTicks)
                        {
                            if (kvp.Value < oldestTick)
                            {
                                oldestTick = kvp.Value;
                                oldestEntry = kvp;
                                found = true;
                            }
                        }

                        if (found)
                        {
                            weaponBodySizeCache.Remove(oldestEntry.Key);
                            weaponBodySizeCacheAccessTicks.Remove(oldestEntry.Key);

                            AutoArmLogger.Debug(() => $"Weapon body size cache at limit ({MaxWeaponBodySizeCacheSize}), evicted LRU entry: {oldestEntry.Key.defName}");
                        }
                    }
                    else
                    {
                        weaponBodySizeCache.Clear();
                        AutoArmLogger.Debug(() => $"Weapon body size cache exceeded {MaxWeaponBodySizeCacheSize} entries, full clear (tracking desync)");
                    }
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                weaponBodySizeCache[weapon.def] = requiredSize;
                weaponBodySizeCacheAccessTicks[weapon.def] = currentTick;
            }
            else
            {
                weaponBodySizeCacheAccessTicks[weapon.def] = Find.TickManager?.TicksGame ?? 0;
            }

            bool canUse = pawn.BodySize >= requiredSize;


            return canUse;
        }

        public static void InitializeModExtensionCache()
        {
            if (modExtensionCacheInitialized) return;
            modExtensionCacheInitialized = true;

            AutoArmLogger.Debug(() => "InitializeModExtensionCache called");

            try
            {
                int weaponsChecked = 0;
                int extensionsFound = 0;

                var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int defIdx = 0; defIdx < allDefs.Count; defIdx++)
                {
                    var weaponDef = allDefs[defIdx];
                    if (!weaponDef.IsWeapon)
                        continue;
                    weaponsChecked++;
                    if (weaponDef.modExtensions != null)
                    {
                        foreach (var extension in weaponDef.modExtensions)
                        {
                            if (extension != null)
                            {
                                var type = extension.GetType();
                                var typeName = type.Name;
                                extensionsFound++;

                                if (AutoArmMod.settings?.debugLogging == true && weaponDef.defName.Contains("DMS_"))
                                {
                                    AutoArmLogger.Debug(() => $"Weapon {weaponDef.defName} has extension: {typeName} (full: {type.FullName})");
                                }

                                var bodySizeField = FindBodySizeField(type,
                                    "requiredBodySize", "minBodySize", "maxBodySize", "bodySize", "minimumBodySize",
                                    "RequiredBodySize", "MinBodySize", "BodySize",
                                    "minBodySizeToEquip", "MinBodySizeToEquip",
                                    "minimumBodySizeToEquip", "MinimumBodySizeToEquip",
                                    "bodySizeRequirement", "BodySizeRequirement",
                                    "bodySizeMin", "BodySizeMin",
                                    "min", "Min", "minimum", "Minimum");

                                if (bodySizeField != null)
                                {
                                    var requiredSize = bodySizeField.GetValue(extension);
                                    if (requiredSize is float minSize && minSize > 0)
                                    {
                                        modExtensionBodySizeCache[weaponDef] = minSize;
                                        AutoArmLogger.Debug(() => $"Found body size {minSize:F1} for {weaponDef.defName} in field {bodySizeField.Name}");
                                        break;
                                    }
                                }
                                else if (typeName.Contains("Heavy") || typeName.Contains("Equippable"))
                                {
                                    var equippableDefField = AccessTools.Field(type, "EquippableDef");
                                    if (equippableDefField != null)
                                    {
                                        var equippableDef = equippableDefField.GetValue(extension);
                                        if (equippableDef != null && equippableDef is Def def)
                                        {
                                            var statBasesField = AccessTools.Field(def.GetType(), "statBases");
                                            if (statBasesField != null)
                                            {
                                                var statBases = statBasesField.GetValue(def) as System.Collections.IList;
                                                if (statBases != null)
                                                {
                                                    foreach (var statModifier in statBases)
                                                    {
                                                        if (statModifier != null)
                                                        {
                                                            var statField = AccessTools.Field(statModifier.GetType(), "stat");
                                                            var valueField = AccessTools.Field(statModifier.GetType(), "value");
                                                            if (statField != null && valueField != null)
                                                            {
                                                                var stat = statField.GetValue(statModifier);
                                                                if (stat != null && stat.ToString().Contains("BodySize"))
                                                                {
                                                                    var value = valueField.GetValue(statModifier);
                                                                    if (value is float bodySize && bodySize > 0)
                                                                    {
                                                                        modExtensionBodySizeCache[weaponDef] = bodySize;
                                                                        AutoArmLogger.Debug(() => $"Found body size {bodySize:F1} for {weaponDef.defName} via EquippableDef.statBases");
                                                                        break;
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            if (!modExtensionBodySizeCache.ContainsKey(weaponDef) && AutoArmMod.settings?.debugLogging == true && weaponDef.defName.Contains("DMS_"))
                                            {
                                                AutoArmLogger.Debug(() => $"  EquippableDef type: {def.GetType().Name}, defName: {def.defName}");
                                                var defFields = def.GetType().GetFields().Select(f => f.Name).ToArray();
                                                if (defFields.Length > 0)
                                                {
                                                    AutoArmLogger.Debug(() => $"    Fields: {string.Join(", ", defFields)}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    foreach (var kvp in modExtensionBodySizeCache.Where(x => x.Key.defName.Contains("DMS_")))
                    {
                        AutoArmLogger.Debug(() => $"  - {kvp.Key.defName}: {kvp.Value:F1} body size requirement");
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Failed to initialize mod extension body size cache: {e.GetType().Name}: {e.Message}");
            }
        }


        private float DetermineBodySizeRequirement(ThingDef weaponDef)
        {
            if (modExtensionBodySizeCache.TryGetValue(weaponDef, out float cachedSize))
            {
                return cachedSize;
            }

            string defName = weaponDef.defName.ToLowerInvariant();

            if (defName.Contains("mech") || defName.Contains("inferno") || defName.Contains("cannon"))
            {
                return 1.5f;
            }

            float mass = weaponDef.GetStatValueAbstract(CachedMass);

            if (mass > 10f)
                return 1.5f;
            if (mass > 5f)
                return 1.0f;
            if (mass > 3f)
                return 0.75f;

            return 0f;
        }


        private static FieldInfo FindBodySizeField(Type type, params string[] fieldNames)
        {
            foreach (var name in fieldNames)
            {
                var field = AccessTools.Field(type, name);
                if (field != null) return field;
            }
            return null;
        }

        public static void CleanupCaches()
        {
            if (Find.Maps == null || Find.Maps.Count == 0)
                return;

            Blacklist.CleanupOldEntries();

            int currentTick = Find.TickManager.TicksGame;

            foreach (var map in Find.Maps)
            {
                var comp = JobGiverMapComponent.GetComponent(map);
                if (comp == null) continue;

                // Skip if caches empty
                if (comp.ValidationCache.Count == 0 &&
                    comp.FailedJobHistory.Count == 0 &&
                    comp.PawnStates.Count == 0 &&
                    comp.ProperWeaponCache.Count == 0)
                    continue;

                // ValidationCache cleanup
                if (comp.ValidationCache.Count > 0)
                {
                    var expiredKeys = ListPool<PawnWeaponKey>.Get(32);
                    foreach (var kvp in comp.ValidationCache)
                    {
                        if (currentTick >= kvp.Value.ExpiryTick)
                        {
                            expiredKeys.Add(kvp.Key);
                        }
                    }

                    foreach (var key in expiredKeys)
                    {
                        comp.ValidationCache.Remove(key);
                    }
                    ListPool<PawnWeaponKey>.Return(expiredKeys);

                    int colonistCount = map.mapPawns?.FreeColonistsSpawned?.Count ?? 10;
                    int maxCacheSize = Math.Min(50000, Math.Max(5000, colonistCount * 200));

                    if (comp.ValidationCache.Count > maxCacheSize)
                    {
                        int entriesToRemove = maxCacheSize / 4;
                        int previousCacheCount = comp.ValidationCache.Count;

                        var sorted = ListPool<KeyValuePair<PawnWeaponKey, int>>.Get(comp.ValidationCache.Count);
                        foreach (var kvp in comp.ValidationCache)
                        {
                            sorted.Add(new KeyValuePair<PawnWeaponKey, int>(kvp.Key, kvp.Value.ExpiryTick));
                        }
                        sorted.Sort(ValidationCacheExpiryComparison);

                        int take = Math.Min(entriesToRemove, sorted.Count);
                        for (int i = 0; i < take; i++)
                        {
                            comp.ValidationCache.Remove(sorted[i].Key);
                        }
                        ListPool<KeyValuePair<PawnWeaponKey, int>>.Return(sorted);

                        AutoArmLogger.Debug(() => $"Validation cache LRU eviction: removed {take} oldest entries (cache was {previousCacheCount}, max {maxCacheSize})");
                    }
                }

                // FailedJobHistory cleanup
                if (comp.FailedJobHistory.Count > 0)
                {
                    var expiredFailures = ListPool<PawnWeaponKey>.Get(16);
                    foreach (var kvp in comp.FailedJobHistory)
                    {
                        if (currentTick - kvp.Value > FAILED_JOB_MEMORY_TICKS)
                        {
                            expiredFailures.Add(kvp.Key);
                        }
                    }

                    foreach (var key in expiredFailures)
                    {
                        comp.FailedJobHistory.Remove(key);
                    }
                    ListPool<PawnWeaponKey>.Return(expiredFailures);
                }

                // PawnStates cleanup
                if (comp.PawnStates.Count > 0)
                {
                    var validIds = new HashSet<int>();
                    if (comp.map?.mapPawns?.AllPawns != null)
                    {
                        foreach (var p in comp.map.mapPawns.AllPawns)
                        {
                            if (p == null || p.Destroyed || p.Dead || p.Discarded || !p.Spawned) continue;
                            validIds.Add(p.thingIDNumber);
                        }
                    }

                    var deadIds = ListPool<int>.Get(8);
                    int equipCooldownThreshold = Constants.WeaponEquipCooldownTicks * 2;
                    foreach (var kvp in comp.PawnStates)
                    {
                        if (!validIds.Contains(kvp.Key))
                        {
                            deadIds.Add(kvp.Key);
                        }
                        else if (kvp.Value.LastEquipTick >= 0 &&
                            (currentTick - kvp.Value.LastEquipTick) > equipCooldownThreshold)
                        {
                            kvp.Value.LastEquipTick = -1;
                        }
                    }

                    foreach (var id in deadIds)
                    {
                        comp.PawnStates.Remove(id);
                    }
                    ListPool<int>.Return(deadIds);
                }

                // ProperWeaponCache cleanup
                if (comp.ProperWeaponCache.Count > 0)
                {
                    var expiredProperties = ListPool<int>.Get(16);
                    foreach (var kvp in comp.ProperWeaponCache)
                    {
                        if (currentTick > kvp.Value.expiryTick)
                        {
                            expiredProperties.Add(kvp.Key);
                        }
                    }

                    foreach (var key in expiredProperties)
                    {
                        comp.ProperWeaponCache.Remove(key);
                    }
                    ListPool<int>.Return(expiredProperties);
                }
            }
        }

        public static void InvalidatePawnValidationCache(Pawn pawn)
        {
            if (pawn != null)
            {
                var keysToRemove = ListPool<PawnWeaponKey>.Get();
                var comp = JobGiverMapComponent.GetComponent(pawn.Map);
                if (comp == null)
                {
                    ListPool<PawnWeaponKey>.Return(keysToRemove);
                    return;
                }
                foreach (var key in comp.ValidationCache.Keys)
                {
                    if (key.PawnId == pawn.thingIDNumber)
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                {
                    comp.ValidationCache.Remove(key);
                }
                ListPool<PawnWeaponKey>.Return(keysToRemove);
            }
        }

        public static void RecordFailedJob(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null) return;

            var key = new PawnWeaponKey(pawn.thingIDNumber, weapon.thingIDNumber);
            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp != null)
                comp.FailedJobHistory[key] = Find.TickManager.TicksGame;
        }


        private static bool ShouldSkipEvaluation(Pawn pawn, int currentTick)
        {
            var primary = pawn.equipment?.Primary as ThingWithComps;
            if (primary != null && ForcedWeapons.IsForced(pawn, primary) &&
                AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
            {
                if (AutoArmMod.settings?.debugLogging == true && pawn.IsHashIntervalTick(600))
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Skipping evaluation - forced primary {primary.def.defName}");
                }
                return true;
            }

            if (primary != null && primary.def?.equippedStatOffsets != null && primary.def.equippedStatOffsets.Count > 0)
            {
                if (AutoArmMod.settings?.debugLogging == true && pawn.IsHashIntervalTick(600))
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Skipping evaluation - holding tool {primary.def.defName} (has equippedStatOffsets)");
                }
                return true;
            }

            if (pawn?.Map == null) return false;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            JobGiver_PickUpBetterWeapon.PawnWeaponState pawnState;
            if (comp == null || !comp.PawnStates.TryGetValue(pawn.thingIDNumber, out pawnState))
            {
                return false;
            }

            int lastEvalTick = pawnState.LastEvaluationTick;
            if (lastEvalTick < 0)
            {
                return false;
            }

            int ticksSinceEval = currentTick - lastEvalTick;

            if (ticksSinceEval >= Constants.MaxSkipEvaluationTicks)
            {
                return false;
            }

            if (pawn.equipment?.Primary == null)
            {
                return false;
            }

            int cacheLastChangeTick = WeaponCache.GetLastCacheChangeTick(pawn.Map);
            if (cacheLastChangeTick > lastEvalTick)
            {
                return false;
            }

            if (HasPawnStateChanged(pawn))
            {
                return false;
            }

            return true;
        }


        private static bool HasPawnStateChanged(Pawn pawn)
        {
            int currentOutfitId = pawn.outfits?.CurrentApparelPolicy?.id ?? -1;
            float currentShootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
            float currentMeleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            bool currentHasBrawler = pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) ?? false;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp == null) return true;
            JobGiver_PickUpBetterWeapon.PawnWeaponState pawnState;
            if (!comp.PawnStates.TryGetValue(pawn.thingIDNumber, out pawnState))
            {
                pawnState = new PawnWeaponState();
                comp.PawnStates[pawn.thingIDNumber] = pawnState;
                pawnState.OutfitId = currentOutfitId;
                pawnState.ShootingSkill = currentShootingSkill;
                pawnState.MeleeSkill = currentMeleeSkill;
                pawnState.HasBrawler = currentHasBrawler;
                return true;
            }

            bool changed = false;

            if (pawnState.OutfitId != currentOutfitId)
            {
                changed = true;
            }

            if (Math.Abs(pawnState.ShootingSkill - currentShootingSkill) >= 2f ||
                Math.Abs(pawnState.MeleeSkill - currentMeleeSkill) >= 2f)
            {
                changed = true;
            }

            if (pawnState.HasBrawler != currentHasBrawler)
            {
                changed = true;
            }

            if (changed)
            {
                pawnState.OutfitId = currentOutfitId;
                pawnState.ShootingSkill = currentShootingSkill;
                pawnState.MeleeSkill = currentMeleeSkill;
                pawnState.HasBrawler = currentHasBrawler;
            }

            return changed;
        }
    }

    public class JobGiverMapComponent : MapComponent
    {
        internal readonly Dictionary<int, JobGiver_PickUpBetterWeapon.PawnWeaponState> PawnStates =
            new Dictionary<int, JobGiver_PickUpBetterWeapon.PawnWeaponState>();

        internal readonly Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, JobGiver_PickUpBetterWeapon.ValidationEntry> ValidationCache =
            new Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, JobGiver_PickUpBetterWeapon.ValidationEntry>(512);

        internal readonly Dictionary<int, (bool isProper, int expiryTick)> ProperWeaponCache =
            new Dictionary<int, (bool isProper, int expiryTick)>();

        internal readonly Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, int> FailedJobHistory =
            new Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, int>();

        // Cached per-tick
        [Unsaved]
        private HashSet<Thing> _cachedReservedThings = new HashSet<Thing>();
        [Unsaved]
        private int _reservationCacheTick = -1;

        public HashSet<Thing> GetReservedThings(int currentTick)
        {
            if (_reservationCacheTick == currentTick)
                return _cachedReservedThings;

            _cachedReservedThings.Clear();
            var reservations = map?.reservationManager?.ReservationsReadOnly;
            if (reservations != null)
            {
                foreach (var reservation in reservations)
                {
                    if (reservation.Target.HasThing)
                        _cachedReservedThings.Add(reservation.Target.Thing);
                }
            }
            _reservationCacheTick = currentTick;
            return _cachedReservedThings;
        }

        public JobGiverMapComponent(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ClearAllCaches();
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            if (map != null)
                JobGiver_PickUpBetterWeapon.OnMapRemoved(map.uniqueID);
            ClearAllCaches();
        }

        public void ClearAllCaches()
        {
            PawnStates.Clear();
            ValidationCache.Clear();
            ProperWeaponCache.Clear();
            FailedJobHistory.Clear();
        }

        public static void ClearAllState()
        {
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    var comp = GetComponent(map);
                    comp?.ClearAllCaches();
                }
            }
        }

        public static JobGiverMapComponent GetComponent(Map map)
        {
            if (map == null) return null;
            return map.GetComponent<JobGiverMapComponent>();
        }


    }
}


using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.Weapons;
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
    /// <summary>
    /// Weapon pickup
    /// </summary>
    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        private static bool testModeEnabled = false;

        private static int globalLastProcessedTick = -1;
        private static int globalLastKnownGameTick = -1;

        private static readonly StatDef CachedMass = StatDefOf.Mass;

        private static readonly HashSet<Thing> reservationSetPool = new HashSet<Thing>();

        private class MessageDeduplicationInfo
        {
            public string LastContent;
            public int FirstLoggedTick;
            public int Count;
        }

        [Unsaved]
        private static readonly Dictionary<(int pawnId, string messageType), MessageDeduplicationInfo> messageCache =
            new Dictionary<(int pawnId, string messageType), MessageDeduplicationInfo>();

        private const int MESSAGE_SUPPRESSION_WINDOW = Constants.ShortCacheDuration;

        private const string MSG_TYPE_NO_WEAPON = "NoWeapon";
        private const string MSG_TYPE_FIND_START = "FindStart";
        private const string MSG_TYPE_OUTFIT_FILTER = "OutfitFilter";
        private const string MSG_TYPE_WEAPON_CACHE = "WeaponCache";


        private static bool ShouldLogDebugMessage(Pawn pawn, string messageType, string messageContent)
        {
            int pawnId = pawn.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;
            var key = (pawnId, messageType);

            if (messageCache.TryGetValue(key, out var cached))
            {
                if (cached.LastContent == messageContent &&
                    (currentTick - cached.FirstLoggedTick) < MESSAGE_SUPPRESSION_WINDOW)
                {
                    cached.Count++;
                    return false;
                }
            }

            messageCache[key] = new MessageDeduplicationInfo
            {
                LastContent = messageContent,
                FirstLoggedTick = currentTick,
                Count = 1
            };
            return true;
        }

        internal class PawnWeaponState
        {
            public int LastEquipTick = -1;
            public int LastEvaluationTick = -1;
            public int OutfitId = -1;
            public float ShootingSkill = 0f;
            public float MeleeSkill = 0f;
            public bool HasBrawler = false;
            public ThingDef LastEquippedDef = null;

            public int LastAttemptedWeaponId = -1;
            public int LastAttemptTick = -1;
            public HashSet<int> TemporarilyBlacklistedWeapons = new HashSet<int>();

            public int TempBlacklistExpiry = -1;
        }


        [Unsaved]
        private static int lastKnownGameTick = -1;

        private static bool IsFreshlyLoaded()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
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
            public bool WasForbidden;
            public bool HadOwner;
        }


        private const int VALIDATION_CACHE_DURATION = Constants.ShortCacheDuration;

        private const int VALIDATION_CACHE_DURATION_FAILED = 3600;



        private const int PROPER_WEAPON_CACHE_DURATION = 3600;


        private const int FAILED_JOB_MEMORY_TICKS = 150;

        private const int CACHE_JITTER_RANGE = 120;


        private static int GetCacheJitter(ThingWithComps weapon)
        {
            return (weapon.thingIDNumber % CACHE_JITTER_RANGE) - (CACHE_JITTER_RANGE / 2);
        }

        /// <summary>
        /// Reset state (tests)
        /// </summary>
        public static void ResetForTesting()
        {
            JobGiverMapComponent.ClearAllState();
        }

        /// <summary>
        /// Toggle test mode (disables throttling)
        /// </summary>
        public static void EnableTestMode(bool enable)
        {
            testModeEnabled = enable;
            if (enable)
            {
                ResetForTesting();
            }
        }

        /// <summary>
        /// Remove expired message cache entries
        /// </summary>
        public static void CleanupMessageCache()
        {
            if (messageCache.Count == 0)
                return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var toRemove = ListPool<(int pawnId, string messageType)>.Get();

            foreach (var kvp in messageCache)
            {
                var info = kvp.Value;

                if (currentTick - info.FirstLoggedTick > MESSAGE_SUPPRESSION_WINDOW)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                messageCache.Remove(key);
            }
            ListPool<(int pawnId, string messageType)>.Return(toRemove);

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
            AutoArmPerfOverlayWindow.IncrementOperations();

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

            int currentTick = Find.TickManager.TicksGame;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp != null && comp.PawnStates.TryGetValue(pawn, out var failureState))
            {
                if (failureState.LastAttemptedWeaponId != -1 && failureState.LastAttemptTick != -1)
                {
                    int ticksSinceAttempt = currentTick - failureState.LastAttemptTick;

                    if (ticksSinceAttempt < 250 &&
                        (pawn.jobs?.curJob == null || pawn.jobs.curJob.def != JobDefOf.Equip) &&
                        pawn.equipment?.Primary?.thingIDNumber != failureState.LastAttemptedWeaponId)
                    {
                        failureState.TemporarilyBlacklistedWeapons.Add(failureState.LastAttemptedWeaponId);

                        if (failureState.TempBlacklistExpiry != -1)
                        {
                            comp.RemoveFromTempBlacklistSchedule(pawn, failureState.TempBlacklistExpiry);
                        }

                        int expireTick = currentTick + 250;
                        failureState.TempBlacklistExpiry = expireTick;

                        if (!comp.tempBlacklistExpirySchedule.TryGetValue(expireTick, out var list))
                        {
                            list = new List<Pawn>();
                            comp.tempBlacklistExpirySchedule[expireTick] = list;
                        }
                        list.Add(pawn);

                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Job failed for weapon ID {failureState.LastAttemptedWeaponId}, blacklisting temporarily (expires at tick {expireTick})");

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

            if (!testModeEnabled && !TestRunner.IsRunningTests)
            {
                if (currentTick < globalLastKnownGameTick ||
                    currentTick - globalLastKnownGameTick > 10000)
                {
                    globalLastProcessedTick = -1;
                }
                globalLastKnownGameTick = currentTick;

                if (globalLastProcessedTick == currentTick)
                {
                    return null;
                }

                globalLastProcessedTick = currentTick;
            }

            if (!testModeEnabled)
            {
                if (ShouldSkipEvaluation(pawn, currentTick))
                {
                    return null;
                }
            }

            if (testModeEnabled || TestRunner.IsRunningTests)
            {
                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] TryGiveJob called for {AutoArmLogger.GetPawnName(pawn)}, testModeEnabled={testModeEnabled}, TestRunner.IsRunningTests={TestRunner.IsRunningTests}");
                }

                if (!PawnValidationCache.CanConsiderWeapons(pawn))
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


            AutoArmPerfOverlayWindow.StartTiming();
            timingStarted = true;

            bool isEmergency = pawn.equipment?.Primary == null;

            if (AutoArmMod.settings?.debugLogging == true && isEmergency && pawn.IsHashIntervalTick(600))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] EMERGENCY: {AutoArmLogger.GetPawnName(pawn)} is unarmed!");
            }

            if (!isEmergency)
            {
                if (!WeaponCacheManager.HasAnyNonForbiddenWeapons(pawn.Map))
                {
                    if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                    return null;
                }

                var outfit = pawn.outfits?.CurrentApparelPolicy;
                if (outfit?.filter != null)
                {
                    bool hasMatchingWeapon = false;
                    foreach (var weapon in WeaponCacheManager.GetWeaponsForOutfit(pawn.Map, outfit))
                    {
                        hasMatchingWeapon = true;
                        break;
                    }

                    if (!hasMatchingWeapon)
                    {
                        if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
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
                if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                return null;
            }
            if (!comp.PawnStates.TryGetValue(pawn, out var pawnState))
            {
                pawnState = new PawnWeaponState();
                comp.PawnStates[pawn] = pawnState;

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
                    if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                    return null;
                }
            }


            lastKnownGameTick = currentTick;

            AutoArmPerfOverlayWindow.ReportTickProcessing(1, 0, currentTick);

            AutoArmPerfOverlayWindow.ReportActiveCooldowns(AutoArmGameComponent.GetActiveCooldowns());

            var currentWeapon = pawn.equipment?.Primary;
            var weaponRestriction = GetWeaponRestriction(pawn, currentWeapon);

            if (AutoArmMod.settings?.debugLogging == true && weaponRestriction.wasForced)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] TryGiveJob: Forced weapon search - restrictToType={weaponRestriction.restrictToType?.defName}, wasForced={weaponRestriction.wasForced}, blockSearch={weaponRestriction.blockSearch}");
            }

            if (weaponRestriction.blockSearch)
            {
                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] {AutoArmLogger.GetPawnName(pawn)}: Search blocked - restrictToType={weaponRestriction.restrictToType?.defName}, wasForced={weaponRestriction.wasForced}");
                }
                if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                return null;
            }

            float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : 0f;

            string failureReason;
            ThingWithComps bestWeapon;
            bestWeapon = FindBestWeapon(pawn, currentScore, weaponRestriction.restrictToType,
                weaponRestriction.wasForced && weaponRestriction.restrictToType != null, out failureReason);

            if (bestWeapon == null && !string.IsNullOrEmpty(failureReason))
            {
                AutoArmPerfOverlayWindow.ReportFailureReason(failureReason);
            }


            if (SimpleSidearmsCompat.IsLoaded && pawn.equipment?.Primary != null &&
                bestWeapon == null && AutoArmMod.settings?.autoEquipSidearms == true)
            {
                string sidearmFailureReason;
                ThingWithComps potentialSidearm = FindBestSidearm(pawn, out sidearmFailureReason);

                if (potentialSidearm != null)
                {
                    Job sidearmJob = SimpleSidearmsCompat.TryGetWeaponJob(pawn, potentialSidearm);
                    if (sidearmJob != null)
                    {
                        AutoEquipState.MarkAsAutoEquip(sidearmJob, pawn);

                        pawnState.LastEvaluationTick = currentTick;

                        return sidearmJob;
                    }
                }
            }

            if (bestWeapon == null)
            {
                if (isEmergency)
                {
                    string messageContent = $"Couldn't find a weapon. Reason: {failureReason}";
                    if (ShouldLogDebugMessage(pawn, MSG_TYPE_NO_WEAPON, messageContent))
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] {messageContent}");
                        LogWeaponRejectionReasons(pawn);
                    }
                    else
                    {
                        if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                        return null;
                    }
                }
                else
                {
                    LogDebugSummary(pawn, $"found no suitable weapon: {failureReason}");
                }

                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] {AutoArmLogger.GetPawnName(pawn)}: No weapon found, current={currentWeapon?.def?.defName ?? "none"} (score={currentScore:F1}), reason: {failureReason}");
                }

                pawnState.LastEvaluationTick = currentTick;

                if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                return null;
            }


            if (!pawn.Map.reservationManager.CanReserve(pawn, bestWeapon, 1))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cannot reserve {bestWeapon.def.defName} - already reserved by another pawn");
                if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                return null;
            }

            /*
            if (!pawn.CanReserveAndReach(bestWeapon, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, false))
            {
                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] {AutoArmLogger.GetPawnName(pawn)}: Cannot reserve/reach {bestWeapon.def.defName} at {bestWeapon.Position}");
                }
                return null;
            }
            */

            if (bestWeapon.IsForbidden(pawn))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Weapon {bestWeapon.def.defName} is forbidden");
                if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                return null;
            }

            if (!CanTakeOrderedJob(pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true || TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cannot take ordered job (game state prevents it)");
                }
                if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
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
                    if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                    return null;
                }
            }

            // Throttle repeated attempts at same weapon
            if (pawnState.LastAttemptedWeaponId == bestWeapon.thingIDNumber &&
                currentTick - pawnState.LastAttemptTick < 180)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Recently attempted {bestWeapon.def.defName}, waiting...");
                if (timingStarted) AutoArmPerfOverlayWindow.EndTiming();
                return null;
            }

            Job job = Jobs.CreateEquipJob(bestWeapon, isSidearm: false, pawn: pawn);
            if (job != null)
            {

                AutoArmPerfOverlayWindow.ReportJobCreated();
                ConfigureAutoEquipJob(job, pawn, currentWeapon, bestWeapon, weaponRestriction.wasForced);

                pawnState.LastAttemptedWeaponId = bestWeapon.thingIDNumber;
                pawnState.LastAttemptTick = currentTick;

                if (isEmergency)
                {
                    job.expiryInterval = Constants.EmergencyJobExpiry;
                    job.checkOverrideOnExpire = false;
                }


                pawnState.LastEvaluationTick = currentTick;
            }

            if (timingStarted)
            {
                AutoArmPerfOverlayWindow.EndTiming();
            }


            return job;
        }

        /// <summary>
        /// Record that a pawn has equipped a weapon to start cooldown
        /// </summary>
        public static void RecordWeaponEquip(Pawn pawn)
        {
            if (pawn == null) return;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp == null) return;
            if (!comp.PawnStates.TryGetValue(pawn, out var state))
            {
                state = new PawnWeaponState();
                comp.PawnStates[pawn] = state;
            }

            state.LastEquipTick = Find.TickManager.TicksGame;
            if (pawn.equipment?.Primary != null)
            {
                state.LastEquippedDef = pawn.equipment.Primary.def;
            }

            CooldownMetrics.OnPawnEquippedWeapon(pawn);
        }

        /// <summary>
        /// Clear cooldown for a specific pawn (e.g., when forced by player)
        /// </summary>
        public static void ClearWeaponCooldown(Pawn pawn)
        {
            if (pawn == null) return;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp != null && comp.PawnStates.TryGetValue(pawn, out var state))
            {
                state.LastEquipTick = -1;
            }
        }

        /// <summary>
        /// Cooldown time
        /// </summary>
        public static int GetRemainingCooldown(Pawn pawn)
        {
            if (pawn == null) return 0;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp != null && comp.PawnStates.TryGetValue(pawn, out var state) && state.LastEquipTick >= 0)
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

        /// <summary>
        /// Should consider weapon
        /// NOTE: Made internal to allow UI to use the same validation logic
        /// </summary>
        /// <param name="inventoryWeapons">Pre-filtered list of weapons in pawn's inventory (optimization to avoid repeated scans)</param>
        /// <param name="ideologyRejectedWeapons">Optional list to track ideology-rejected weapons for consolidated logging</param>
        public bool ShouldConsiderWeapon(Pawn pawn, ThingWithComps weapon, ThingWithComps currentWeapon, bool isForcedUpgrade = false, List<ThingWithComps> inventoryWeapons = null, List<string> ideologyRejectedWeapons = null)
        {
            if (isForcedUpgrade && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FORCED UPGRADE VALIDATION: Checking {weapon.Label} (isForcedUpgrade={isForcedUpgrade})");
            }


            bool isUnarmed = pawn.equipment?.Primary == null;

            bool shouldLogRejection = isUnarmed && AutoArmMod.settings?.debugLogging == true;

            if (TestRunner.IsRunningTests && AutoArmMod.settings?.debugLogging == true &&
                pawn.DevelopmentalStage < DevelopmentalStage.Adult)
            {
                AutoArmLogger.Debug(() => $"[TEST] ShouldConsiderWeapon for child {AutoArmLogger.GetPawnName(pawn)}: weapon={weapon.def.defName}");
            }


            int currentTick = Find.TickManager.TicksGame;
            bool isProperWeapon = false;
            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            if (comp == null) return false;

            if (!comp.ProperWeaponCache.TryGetValue(weapon.thingIDNumber, out var properCached) ||
                currentTick > properCached.expiryTick)
            {
                isProperWeapon = WeaponValidation.IsWeapon(weapon);
                comp.ProperWeaponCache[weapon.thingIDNumber] = (isProperWeapon, currentTick + PROPER_WEAPON_CACHE_DURATION);
            }
            else
            {
                isProperWeapon = properCached.isProper;
            }

            if (!isProperWeapon)
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: not a valid weapon");
                return false;
            }


            if (weapon.Destroyed)
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: destroyed");
                return false;
            }

            if (weapon.IsBurning())
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: on fire");
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
            bool cachedHasOwner = weaponHolder != null;

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
            bool isForbidden = false;

            bool bypassCache = isForcedUpgrade;
            if (bypassCache)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FORCED UPGRADE: Bypassing validation cache for {weapon.Label}");
            }
            else if (comp.ValidationCache.TryGetValue(cacheKey, out var cached))
            {
                if (currentTick < cached.ExpiryTick)
                {
                    bool currentlyHasOwner = weaponHolder != null;

                    if (weapon.Destroyed || cached.HadOwner != currentlyHasOwner || cached.WasForbidden != isForbidden)
                    {
                        comp.ValidationCache.Remove(cacheKey);
                        AutoArmPerfOverlayWindow.ReportCacheMiss();
                    }
                    else
                    {
                        AutoArmPerfOverlayWindow.ReportCacheHit();
                        return cached.IsValid;
                    }
                }
                else
                {
                    AutoArmPerfOverlayWindow.ReportCacheMiss();
                }
            }
            else
            {
                AutoArmPerfOverlayWindow.ReportCacheMiss();
            }



            if (DroppedItemTracker.IsDropped(weapon))
            {
                if (shouldLogRejection)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: just dropped it recently");
                return false;
            }

            if (comp.PawnStates.TryGetValue(pawn, out var blacklistState) &&
                blacklistState.TemporarilyBlacklistedWeapons.Contains(weapon.thingIDNumber))
            {
                if (blacklistState.TempBlacklistExpiry != -1 && currentTick >= blacklistState.TempBlacklistExpiry)
                {
                    blacklistState.TemporarilyBlacklistedWeapons.Clear();
                    blacklistState.TempBlacklistExpiry = -1;

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cleared expired blacklist (TTL fallback) for {AutoArmLogger.GetWeaponLabelLower(weapon)}");
                    }
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

            bool isDuplicateType = (currentWeapon?.def == weapon.def && weapon != currentWeapon);


            if (!IsValidSize(pawn, weapon))
            {
                WeaponBlacklist.AddToBlacklist(weapon.def, pawn, "Body size requirement not met");

                comp.ValidationCache[cacheKey] = new ValidationEntry
                {
                    IsValid = false,
                    ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                    WasForbidden = isForbidden,
                    HadOwner = weaponHolder != null
                };
                return false;
            }

            if (WeaponBlacklist.IsBlacklisted(weapon.def, pawn))
            {
                comp.ValidationCache[cacheKey] = new ValidationEntry
                {
                    IsValid = false,
                    ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                    WasForbidden = isForbidden,
                    HadOwner = weaponHolder != null
                };
                return false;
            }


            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
            {
                comp.ValidationCache[cacheKey] = new ValidationEntry
                {
                    IsValid = false,
                    ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                    WasForbidden = isForbidden,
                    HadOwner = weaponHolder != null
                };
                return false;
            }

            if (Caching.Components.IsBiocodedToOther(weapon, pawn))
            {
                comp.ValidationCache[cacheKey] = new ValidationEntry
                {
                    IsValid = false,
                    ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED * 2 + GetCacheJitter(weapon),
                    WasForbidden = isForbidden,
                    HadOwner = weaponHolder != null
                };
                return false;
            }

            string cantEquipReason;
            if (!EquipEligibilityCache.CanEquip(pawn, weapon, out cantEquipReason, checkBonded: true))
            {
                if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't equip {AutoArmLogger.GetWeaponLabelLower(weapon)}: {cantEquipReason}");

                comp.ValidationCache[cacheKey] = new ValidationEntry
                {
                    IsValid = false,
                    ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                    WasForbidden = isForbidden,
                    HadOwner = weaponHolder != null
                };
                return false;
            }

            if (ModsConfig.IdeologyActive)
            {
                string ideologyReason;
                if (ValidationHelper.TryGetIdeologyWeaponBlock(weapon, pawn, out ideologyReason, out _))
                {
                    if (ideologyRejectedWeapons != null)
                    {
                        ideologyRejectedWeapons.Add(AutoArmLogger.GetWeaponLabelLower(weapon));
                    }
                    else if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: {ideologyReason ?? "ideology forbids it"}");
                    }

                    comp.ValidationCache[cacheKey] = new ValidationEntry
                    {
                        IsValid = false,
                        ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                        WasForbidden = isForbidden,
                        HadOwner = weaponHolder != null
                    };
                    return false;
                }
            }



            bool hasDuplicate = false;
            ThingWithComps existingWeapon = null;

            if (currentWeapon != null && currentWeapon.def == weapon.def)
            {
                hasDuplicate = true;
                existingWeapon = currentWeapon;
            }

            if (!hasDuplicate)
            {
                if (inventoryWeapons != null)
                {
                    foreach (ThingWithComps invWeapon in inventoryWeapons)
                    {
                        if (invWeapon.def == weapon.def)
                        {
                            hasDuplicate = true;
                            existingWeapon = invWeapon;
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
                            hasDuplicate = true;
                            existingWeapon = invWeapon;
                            break;
                        }
                    }
                }
            }

            if (hasDuplicate && existingWeapon != null)
            {
                if (isForcedUpgrade)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FORCED UPGRADE: Allowing duplicate {weapon.Label} through for quality comparison");
                }
                else
                {
                    QualityCategory existingQuality = QualityCategory.Normal;
                    QualityCategory newQuality = QualityCategory.Normal;
                    Caching.Components.TryGetWeaponQuality(existingWeapon, out existingQuality);
                    Caching.Components.TryGetWeaponQuality(weapon, out newQuality);

                    bool isForcedWeapon = ForcedWeapons.IsForced(pawn, existingWeapon);
                    bool allowForcedUpgrades = AutoArmMod.settings?.allowForcedWeaponUpgrades == true;

                    if (isForcedWeapon && allowForcedUpgrades)
                    {
                        if (newQuality < existingQuality)
                        {
                            if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: forced weapon has better quality");

                            comp.ValidationCache[cacheKey] = new ValidationEntry
                            {
                                IsValid = false,
                                ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                                WasForbidden = isForbidden,
                                HadOwner = weaponHolder != null
                            };
                            return false;
                        }
                    }
                    else
                    {
                        if (newQuality <= existingQuality)
                        {
                            if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: already have same or better quality");

                            comp.ValidationCache[cacheKey] = new ValidationEntry
                            {
                                IsValid = false,
                                ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                                WasForbidden = isForbidden,
                                HadOwner = weaponHolder != null
                            };
                            return false;
                        }
                    }

                    float existingScore = GetWeaponScore(pawn, existingWeapon);
                    float newScore = GetWeaponScore(pawn, weapon);
                    float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;

                    if (isForcedWeapon && allowForcedUpgrades)
                    {
                        threshold = Math.Min(threshold, 1.01f);
                    }

                    if (newScore <= existingScore * threshold)
                    {
                        if (shouldLogRejection && AutoArmMod.settings?.debugLogging == true)
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Can't use {AutoArmLogger.GetWeaponLabelLower(weapon)}: not enough of an upgrade (need {existingScore * threshold:F1}, got {newScore:F1})");

                        comp.ValidationCache[cacheKey] = new ValidationEntry
                        {
                            IsValid = false,
                            ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                            WasForbidden = isForbidden,
                            HadOwner = weaponHolder != null
                        };
                        return false;
                    }
                }
            }

            if (isDuplicateType)
            {
                comp.ValidationCache[cacheKey] = new ValidationEntry
                {
                    IsValid = false,
                    ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                    WasForbidden = isForbidden,
                    HadOwner = weaponHolder != null
                };
                return false;
            }

            if (Jobs.IsTemporary(pawn) && !(AutoArmMod.settings?.allowTemporaryColonists ?? false))
            {
                comp.ValidationCache[cacheKey] = new ValidationEntry
                {
                    IsValid = false,
                    ExpiryTick = currentTick + VALIDATION_CACHE_DURATION_FAILED + GetCacheJitter(weapon),
                    WasForbidden = isForbidden,
                    HadOwner = weaponHolder != null
                };
                return false;
            }

            comp.ValidationCache[cacheKey] = new ValidationEntry
            {
                IsValid = true,
                ExpiryTick = currentTick + VALIDATION_CACHE_DURATION + GetCacheJitter(weapon),
                WasForbidden = isForbidden,
                HadOwner = weaponHolder != null
            };


            return true;
        }


        private bool ShouldPickupWeaponType(Pawn pawn, ThingWithComps newWeapon, ThingWithComps currentWeapon)
        {
            if (currentWeapon != null && currentWeapon.def == newWeapon.def)
                return true;

            if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing item in pawn.inventory.innerContainer)
                {
                    if (item is ThingWithComps invWeapon && invWeapon.def == newWeapon.def)
                        return false;
                }
            }

            return true;
        }


        private void ConfigureAutoEquipJob(Job job, Pawn pawn, ThingWithComps currentWeapon, ThingWithComps newWeapon, bool wasForced)
        {
            AutoEquipState.MarkAsAutoEquip(job, pawn);
            AutoEquipState.SetPreviousWeapon(pawn, currentWeapon?.LabelCap);

            if (wasForced && AutoArmMod.settings?.allowForcedWeaponUpgrades == true)
            {
                AutoEquipState.SetWeaponToForce(pawn, newWeapon);
            }
        }


        private class WeaponSearchContext
        {
            public Pawn Pawn { get; set; }
            public float CurrentScore { get; set; }
            public ThingDef RestrictToType { get; set; }
            public bool IsForcedUpgrade { get; set; }
            public bool IsUnarmed { get; set; }
            public bool StorageOnly { get; set; }

            public float MinAcceptableScore { get; set; }

            public float AmazingThreshold { get; set; }
            public float GreatThreshold { get; set; }
            public float GoodThreshold { get; set; }

            public int TotalWeaponsSearched { get; set; }

            public int TotalWeaponsAvailable { get; set; }

            public List<string> IdeologyRejectedWeapons { get; set; }

            public ThingWithComps AmazingWeapon { get; set; }

            public float AmazingScore { get; set; }
            public ThingWithComps GreatWeapon { get; set; }
            public float GreatScore { get; set; }
            public ThingWithComps GoodWeapon { get; set; }
            public float GoodScore { get; set; }
        }


        private WeaponSearchContext InitializeWeaponSearch(Pawn pawn, float currentScore, ThingDef restrictToType, bool isForcedUpgrade)
        {
            var context = new WeaponSearchContext
            {
                Pawn = pawn,
                CurrentScore = currentScore,
                RestrictToType = restrictToType,
                IsForcedUpgrade = isForcedUpgrade,
                IsUnarmed = pawn.equipment?.Primary == null,
                StorageOnly = AutoArmMod.settings?.onlyAutoEquipFromStorage == true
            };

            float userThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
            context.MinAcceptableScore = context.IsUnarmed ? 0.01f : currentScore * userThreshold;

            context.AmazingThreshold = context.IsUnarmed ? float.MaxValue : currentScore * 2.0f;
            context.GreatThreshold = context.IsUnarmed ? float.MaxValue : currentScore * 1.5f;
            context.GoodThreshold = context.MinAcceptableScore;

            context.IdeologyRejectedWeapons = ListPool<string>.Get();

            if (TestRunner.IsRunningTests)
            {
                AutoArmLogger.Debug(() => $"[TEST] FindBestWeapon START for {AutoArmLogger.GetPawnName(pawn)}: isUnarmed={context.IsUnarmed}, currentScore={currentScore}, restrictToType={restrictToType?.defName ?? "none"}");
            }

            return context;
        }


        private IEnumerable<Thing> GetWeaponSearchSet(WeaponSearchContext context)
        {
            if (context.StorageOnly)
            {
                return WeaponCacheManager.GetAllStorageWeapons(context.Pawn.Map);
            }
            return WeaponCacheManager.GetAllWeapons(context.Pawn.Map);
        }


        private bool ProcessWeaponCandidate(WeaponSearchContext context, ThingWithComps weapon,
            List<(ThingWithComps weapon, float roughScore)> roughQueue)
        {
            context.TotalWeaponsSearched++;

            if (context.RestrictToType != null && weapon.def != context.RestrictToType)
            {
                return false;
            }

            if (!ShouldConsiderWeapon(context.Pawn, weapon, context.Pawn.equipment?.Primary, context.IsForcedUpgrade))
            {
                return false;
            }

            float rough = WeaponScoringHelper.GetWeaponPropertyScore(context.Pawn, weapon);
            roughQueue.Add((weapon, rough));
            return true;
        }


        private ThingWithComps SelectBestWeaponByTier(WeaponSearchContext context)
        {
            ThingWithComps bestWeapon = null;
            float bestScore = 0f;

            if (context.AmazingWeapon != null && context.Pawn.CanReserve(context.AmazingWeapon))
            {
                bestWeapon = context.AmazingWeapon;
                bestScore = context.AmazingScore;
                WeaponCacheManager.SetTemporaryReservation(context.AmazingWeapon, context.Pawn);
                AutoArmLogger.Debug(() => $"[{context.Pawn.ThingID}] Using AMAZING weapon: {context.AmazingWeapon.def.defName} (score {context.AmazingScore:F1})");
            }
            else if (context.GreatWeapon != null && context.Pawn.CanReserve(context.GreatWeapon))
            {
                bestWeapon = context.GreatWeapon;
                bestScore = context.GreatScore;
                WeaponCacheManager.SetTemporaryReservation(context.GreatWeapon, context.Pawn);
                AutoArmLogger.Debug(() => $"[{context.Pawn.ThingID}] Using GREAT weapon: {context.GreatWeapon.def.defName} (score {context.GreatScore:F1})");
            }
            else if (context.GoodWeapon != null && context.Pawn.CanReserve(context.GoodWeapon))
            {
                bestWeapon = context.GoodWeapon;
                bestScore = context.GoodScore;
                WeaponCacheManager.SetTemporaryReservation(context.GoodWeapon, context.Pawn);
                AutoArmLogger.Debug(() => $"[{context.Pawn.ThingID}] Using GOOD weapon: {context.GoodWeapon.def.defName} (score {context.GoodScore:F1})");
            }

            if (bestWeapon != null)
            {
                float distance = (bestWeapon.Position - context.Pawn.Position).LengthHorizontal;
                AutoArmPerfOverlayWindow.ReportWeaponDistance(distance);
            }

            return bestWeapon;
        }

        /// <summary>
        /// Aggressive tiered search
        /// Tier thresholds:
        /// - AMAZING: 2x better than current (immediate grab)
        /// - GREAT: 1.5x better than current (immediate grab)
        /// - GOOD: User configurable threshold (default 1.05x)
        /// This prevents multiple pawns competing for the same "best" weapon.
        /// </summary>
        public ThingWithComps FindBestWeapon(Pawn pawn, float currentScore, ThingDef restrictToType, bool isForcedUpgrade, out string primaryFailureReason)
        {
            primaryFailureReason = "No weapons found";


            if (isForcedUpgrade && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FindBestWeapon ENTRY: currentScore={currentScore:F1}, restrictToType={restrictToType?.defName}, isForcedUpgrade={isForcedUpgrade}");
            }

            var context = InitializeWeaponSearch(pawn, currentScore, restrictToType, isForcedUpgrade);


            var roughQueue = ListPool<(ThingWithComps weapon, float roughScore)>.Get(256);
            List<Thing> filteredSearchSet = null;
            List<ThingWithComps> pawnInventoryWeapons = null;

            int totalWeaponsAvailable = 0;
            int totalWeaponsSearched = 0;
            var rejectionReasons = new Dictionary<string, int>();
            ThingWithComps bestWeapon = null;
            float bestScore = 0f;
            bool isUnarmed = context.IsUnarmed;
            float minAcceptableScore = context.MinAcceptableScore;

            try
            {
                bool storageOnly = context.StorageOnly;


                IEnumerable<Thing> searchSet = null;
                if (storageOnly)
                {
                    WeaponCacheManager.EnsureCacheExists(pawn.Map);
                    var storageWeapons = WeaponCacheManager.GetAllStorageWeapons(pawn.Map);

                    if (restrictToType != null)
                    {
                        filteredSearchSet = ListPool<Thing>.Get();
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
                        int count = 0;
                        foreach (var _ in storageWeapons) count++;
                        totalWeaponsAvailable = count;
                        searchSet = storageWeapons;
                    }
                }
                else
                {
                    WeaponCacheManager.EnsureCacheExists(pawn.Map);

                    IEnumerable<ThingWithComps> allWeapons;
                    if (isForcedUpgrade && restrictToType != null)
                    {
                        allWeapons = WeaponCacheManager.GetAllWeapons(pawn.Map);
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FORCED UPGRADE: Bypassing outfit filter for {restrictToType.defName}");
                    }
                    else if (pawn.outfits?.CurrentApparelPolicy != null)
                    {
                        allWeapons = WeaponCacheManager.GetWeaponsForOutfit(pawn.Map, pawn.outfits.CurrentApparelPolicy);
                    }
                    else
                    {
                        allWeapons = WeaponCacheManager.GetAllWeapons(pawn.Map);
                    }

                    if (AutoArmMod.settings?.debugLogging == true && isUnarmed)
                    {
                        var weaponCount = WeaponCacheManager.GetCacheWeaponCount(pawn.Map);

                        string outfitInfo = pawn.outfits?.CurrentApparelPolicy != null
                            ? $", filtering by outfit '{pawn.outfits.CurrentApparelPolicy.label}'"
                            : string.Empty;

                        string messageContent = $"Found {weaponCount} weapons in cache{outfitInfo}";
                        if (ShouldLogDebugMessage(pawn, MSG_TYPE_WEAPON_CACHE, messageContent))
                        {
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] {messageContent}");
                            if (weaponCount == 0)
                            {
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] WARNING: Weapon cache is empty!");
                            }
                        }
                    }


                    if (restrictToType != null)
                    {
                        filteredSearchSet = ListPool<Thing>.Get();
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
                            if (totalWeaponsAvailable == 0)
                            {
                                int totalInCache = 0;
                                foreach (var w in WeaponCacheManager.GetAllWeapons(pawn.Map))
                                {
                                    totalInCache++;
                                    if (totalInCache <= 10)
                                    {
                                        AutoArmLogger.Debug(() => $"  - Cache contains: {w.Label} ({w.def.defName})");
                                    }
                                }
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Total weapons in cache: {totalInCache}");
                            }
                        }
                    }
                    else
                    {
                        filteredSearchSet = ListPool<Thing>.Get();
                        foreach (var w in allWeapons)
                        {
                            if (!IsValidSize(pawn, w))
                                continue;

                            filteredSearchSet.Add(w);
                        }
                        searchSet = filteredSearchSet;
                        totalWeaponsAvailable = filteredSearchSet.Count;
                    }
                }


                int currentTick = Find.TickManager.TicksGame;
                int evaluatedWeapons = 0;

                var reservedThings = reservationSetPool;
                reservedThings.Clear();
                if (pawn?.Map?.reservationManager?.ReservationsReadOnly != null)
                {
                    foreach (var reservation in pawn.Map.reservationManager.ReservationsReadOnly)
                    {
                        if (reservation.Target.HasThing)
                        {
                            reservedThings.Add(reservation.Target.Thing);
                        }
                    }
                }

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


                if (searchSet != null)
                {
                    foreach (Thing thing in searchSet)
                    {
                        totalWeaponsSearched++;
                        AutoArmPerfOverlayWindow.IncrementOperations();

                        var weapon = thing as ThingWithComps;
                        if (weapon == null)
                            continue;

                        bool shouldConsider = ShouldConsiderWeapon(pawn, weapon, pawn.equipment?.Primary, isForcedUpgrade, pawnInventoryWeapons, context.IdeologyRejectedWeapons);

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
                                    if (DroppedItemTracker.IsDropped(weapon)) reason = "Recently dropped";
                                    else if (WeaponBlacklist.IsBlacklisted(weapon.def, pawn)) reason = "Blacklisted";
                                    else reason = "Failed validation";
                                }
                            }
                            else if (DroppedItemTracker.IsDropped(weapon)) reason = "Recently dropped";
                            else if (WeaponBlacklist.IsBlacklisted(weapon.def, pawn)) reason = "Blacklisted";

                            if (isForcedUpgrade && restrictToType != null && weapon.def == restrictToType &&
                                AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FORCED UPGRADE: Rejecting {weapon.Label} - reason: {reason}");
                            }

                            if (!rejectionReasons.ContainsKey(reason)) rejectionReasons[reason] = 0;
                            rejectionReasons[reason]++;
                            continue;
                        }

                        var failKey = new PawnWeaponKey(pawn.thingIDNumber, weapon.thingIDNumber);
                        var comp2 = JobGiverMapComponent.GetComponent(pawn.Map);
                        if (comp2 != null && comp2.FailedJobHistory.TryGetValue(failKey, out int lastFailTick))
                        {
                            if (currentTick - lastFailTick < FAILED_JOB_MEMORY_TICKS)
                            {
                                string reason = "Recently unreachable";
                                if (!rejectionReasons.ContainsKey(reason)) rejectionReasons[reason] = 0;
                                rejectionReasons[reason]++;
                                continue;
                            }
                            else
                            {
                                comp2.FailedJobHistory.Remove(failKey);
                            }
                        }

                        if (WeaponCacheManager.HasTemporaryReservation(weapon, pawn))
                        {
                            string reason = "Reserved for another";
                            if (!rejectionReasons.ContainsKey(reason)) rejectionReasons[reason] = 0;
                            rejectionReasons[reason]++;
                            continue;
                        }

                        if (reservedThings.Contains(weapon))
                        {
                            if (!pawn.CanReserve(weapon))
                            {
                                string reason = "Reserved";
                                if (!rejectionReasons.ContainsKey(reason)) rejectionReasons[reason] = 0;
                                rejectionReasons[reason]++;
                                continue;
                            }
                        }

                        float rough = WeaponScoringHelper.GetWeaponPropertyScore(pawn, weapon);
                        roughQueue.Add((weapon, rough));
                    }

                    if (roughQueue.Count > 1)
                    {
                        roughQueue.Sort((a, b) => b.roughScore.CompareTo(a.roughScore));
                    }

                    var currentWeapon = pawn.equipment?.Primary;
                    float roughScore = 0f;
                    if (!isUnarmed && currentWeapon != null)
                    {
                        roughScore = WeaponScoringHelper.GetWeaponPropertyScore(pawn, currentWeapon);
                    }

                    float topRoughScore = roughQueue.Count > 0 ? roughQueue[0].roughScore : 0f;

                    foreach (var item in roughQueue)
                    {
                        var weapon = item.weapon;

                        if (!isUnarmed && currentWeapon != null)
                        {
                            float candidateScore = item.roughScore;

                            float scoreThreshold = (weapon.def == currentWeapon.def) ?
                                Constants.RoughScoreSameTypeThreshold :
                                Constants.RoughScoreDifferentTypeThreshold;

                            if (candidateScore < roughScore * scoreThreshold)
                            {
                                string skipReason = weapon.def == currentWeapon.def ?
                                    "Score too low (same type)" :
                                    "Score too low (different type)";

                                if (!rejectionReasons.ContainsKey(skipReason)) rejectionReasons[skipReason] = 0;
                                rejectionReasons[skipReason]++;

                                continue;
                            }
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

                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FORCED UPGRADE CHECK: {weapon.Label} (Q:{newQuality}, Score:{newScore:F1}) vs current {forcedWeaponToCompare.Label} (Q:{currentQuality}, Score:{forcedCurrentScore:F1})");

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

                                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Forced upgrade PASSES checks - proceeding to tier evaluation");
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

                        if (bestWeapon != null && item.roughScore < topRoughScore * 0.5f)
                        {
                            break;
                        }
                    }
                }

                if (TestRunner.IsRunningTests)
                {
                    if (bestWeapon != null)
                    {
                        AutoArmLogger.Debug(() => $"[TEST] FindBestWeapon FOUND: {bestWeapon.def.defName} (score={bestScore:F1}), searched {totalWeaponsSearched}/{totalWeaponsAvailable} weapons, evaluated {evaluatedWeapons} full scores");
                    }
                    else
                    {
                        AutoArmLogger.Debug(() => $"[TEST] FindBestWeapon FAILED: No weapon found, searched {totalWeaponsSearched}/{totalWeaponsAvailable} weapons, evaluated {evaluatedWeapons} full scores");
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
                }

                AutoArmPerfOverlayWindow.ReportSearchStats(totalWeaponsSearched, totalWeaponsAvailable);

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
                    WeaponCacheManager.SetTemporaryReservation(bestWeapon, pawn);
                    AutoArmLogger.Debug(() => isUnarmed
                            ? $"[{AutoArmLogger.GetPawnName(pawn)}] Best weapon found: {AutoArmLogger.GetWeaponLabelLower(bestWeapon)} (score {bestScore:F1})"
                            : $"[{AutoArmLogger.GetPawnName(pawn)}] Best weapon: {bestWeapon.def.defName} (score {bestScore:F1})");
                }

                if (bestWeapon != null)
                {
                    float distance = (bestWeapon.Position - pawn.Position).LengthHorizontal;
                    AutoArmPerfOverlayWindow.ReportWeaponDistance(distance);
                    return bestWeapon;
                }

                if (isForcedUpgrade && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] FORCED UPGRADE FAILED: No weapon found");
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
                WeaponBlacklist.FlushPendingLogs();

                ListPool<(ThingWithComps weapon, float roughScore)>.Return(roughQueue);
                if (filteredSearchSet != null)
                    ListPool<Thing>.Return(filteredSearchSet);
                if (pawnInventoryWeapons != null)
                    ListPool<ThingWithComps>.Return(pawnInventoryWeapons);
                if (context?.IdeologyRejectedWeapons != null)
                    ListPool<string>.Return(context.IdeologyRejectedWeapons);

                reservationSetPool.Clear();

            }
        }

        /// <summary>
        /// Find best available sidearm for the pawn
        /// Unlike FindBestWeapon, this looks for ANY suitable sidearm, not just upgrades
        /// </summary>
        public ThingWithComps FindBestSidearm(Pawn pawn, out string failureReason)
        {
            failureReason = "No suitable sidearms found";

            if (pawn?.Map == null || pawn.equipment?.Primary == null)
            {
                failureReason = "No primary weapon";
                return null;
            }

            ThingWithComps bestSidearm = null;
            float bestScore = 0f;

            bool storageOnly = AutoArmMod.settings?.onlyAutoEquipFromStorage == true;
            var outfit = pawn.outfits?.CurrentApparelPolicy;

            IEnumerable<ThingWithComps> candidateWeapons;
            if (storageOnly && outfit != null)
            {
                candidateWeapons = WeaponCacheManager.GetStorageWeapons(pawn.Map, outfit);
            }
            else if (storageOnly)
            {
                candidateWeapons = WeaponCacheManager.GetAllStorageWeapons(pawn.Map);
            }
            else if (outfit != null)
            {
                candidateWeapons = WeaponCacheManager.GetWeaponsForOutfit(pawn.Map, outfit);
            }
            else
            {
                candidateWeapons = WeaponCacheManager.GetAllWeapons(pawn.Map);
            }

            foreach (var weapon in candidateWeapons)
            {
                if (weapon == null || weapon.Destroyed || weapon.Map != pawn.Map)
                    continue;


                if (weapon == pawn.equipment.Primary)
                    continue;

                if (pawn.inventory?.innerContainer?.Contains(weapon) == true)
                    continue;

                if (!ShouldConsiderWeapon(pawn, weapon, pawn.equipment.Primary))
                    continue;

                string reason;
                if (!SimpleSidearmsCompat.CanPickupSidearm(weapon, pawn, out reason))
                    continue;

                if (!pawn.CanReserve(weapon))
                    continue;

                float score = GetWeaponScore(pawn, weapon);

                bool primaryIsRanged = pawn.equipment.Primary.def.IsRangedWeapon;
                bool sidearmIsRanged = weapon.def.IsRangedWeapon;
                if (primaryIsRanged != sidearmIsRanged)
                {
                    score *= 1.5f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestSidearm = weapon;
                }
            }

            if (bestSidearm == null)
            {
                failureReason = "No valid sidearms available";
            }

            WeaponBlacklist.FlushPendingLogs();

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

        /// <summary>
        /// Cached score
        /// </summary>
        public float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn == null)
                return 0f;

            return WeaponCacheManager.GetCachedScore(pawn, weapon);
        }


        private void LogDebugSummary(Pawn pawn, string result)
        {
            if (AutoArmMod.settings?.debugLogging != true || pawn.equipment?.Primary != null)
                return;

            if (pawn.IsHashIntervalTick(600))
            {
                AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] UNARMED: {result}");
            }
        }


        private void LogWeaponRejectionReasons(Pawn pawn)
        {
            return;
        }

        [Unsaved]
        private static readonly Dictionary<ThingDef, float> weaponBodySizeCache = new Dictionary<ThingDef, float>();

        [Unsaved]
        private static readonly Dictionary<ThingDef, int> weaponBodySizeCacheAccessTicks = new Dictionary<ThingDef, int>();

        private const int MaxWeaponBodySizeCacheSize = 500;

        [Unsaved]
        private static readonly Dictionary<ThingDef, float> modExtensionBodySizeCache = new Dictionary<ThingDef, float>();

        private static bool modExtensionCacheInitialized = false;

        /// <summary>
        /// Body size check
        /// </summary>
        public bool IsValidSize(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return true;

            if (AutoArmMod.settings?.allowChildrenToEquipWeapons == true)
            {
                int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;
                int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;

                bool isAllowedChild = (pawn.DevelopmentalStage < DevelopmentalStage.Adult && age >= minAge) ||
                                     (minAge > 13 && age >= minAge && age < 18);

                if (isAllowedChild)
                {
                    return true;
                }
            }

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

        /// <summary>
        /// Initialize mod extension body size cache once at startup
        /// </summary>
        public static void InitializeModExtensionCache()
        {
            if (modExtensionCacheInitialized) return;
            modExtensionCacheInitialized = true;

            AutoArmLogger.Debug(() => "InitializeModExtensionCache called");

            try
            {
                int weaponsChecked = 0;
                int extensionsFound = 0;

                foreach (ThingDef weaponDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon))
                {
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
                AutoArmLogger.Error($"Failed to initialize mod extension body size cache: {e.Message}", e);
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

        /// <summary>
        /// Cleanup static caches
        /// </summary>
        public static void CleanupCaches()
        {
            if (Find.Maps == null || Find.Maps.Count == 0)
                return;

            WeaponBlacklist.CleanupOldEntries();

            int currentTick = Find.TickManager.TicksGame;

            foreach (var map in Find.Maps)
            {
                var comp = JobGiverMapComponent.GetComponent(map);
                if (comp == null) continue;

                comp.ClearColonistCache();

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

                    var candidates = ListPool<KeyValuePair<PawnWeaponKey, ValidationEntry>>.Get(comp.ValidationCache.Count);
                    foreach (var kvp in comp.ValidationCache)
                    {
                        candidates.Add(kvp);
                    }

                    candidates.SortBy(kvp => kvp.Value.ExpiryTick);

                    var oldestEntries = ListPool<PawnWeaponKey>.Get(Math.Min(entriesToRemove, candidates.Count));
                    for (int i = 0; i < Math.Min(entriesToRemove, candidates.Count); i++)
                    {
                        oldestEntries.Add(candidates[i].Key);
                    }

                    foreach (var key in oldestEntries)
                    {
                        comp.ValidationCache.Remove(key);
                    }

                    int removedCount = oldestEntries.Count;
                    int previousCacheCount = comp.ValidationCache.Count + removedCount;

                    ListPool<KeyValuePair<PawnWeaponKey, ValidationEntry>>.Return(candidates);
                    ListPool<PawnWeaponKey>.Return(oldestEntries);

                    AutoArmLogger.Debug(() => $"Validation cache LRU eviction: removed {removedCount} oldest entries (cache was {previousCacheCount}, max {maxCacheSize})");
                }

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

                var deadPawns = ListPool<Pawn>.Get(8);
                foreach (var pawn in comp.PawnStates.Keys)
                {
                    if (Cleanup.IsPawnInvalid(pawn))
                        deadPawns.Add(pawn);
                }

                foreach (var pawn in deadPawns)
                {
                    comp.PawnStates.Remove(pawn);
                }
                ListPool<Pawn>.Return(deadPawns);

                foreach (var kvp in comp.PawnStates)
                {
                    if (kvp.Value.LastEquipTick >= 0 &&
                        (currentTick - kvp.Value.LastEquipTick) > Constants.WeaponEquipCooldownTicks * 2)
                    {
                        kvp.Value.LastEquipTick = -1;
                    }
                }

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

        /// <summary>
        /// Clear validation cache for a specific pawn (call when outfit changes, etc.)
        /// </summary>
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

        /// <summary>
        /// Record a failed job attempt for smart reachability
        /// </summary>
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
            if (pawn?.Map == null) return false;

            var comp = JobGiverMapComponent.GetComponent(pawn.Map);
            JobGiver_PickUpBetterWeapon.PawnWeaponState pawnState;
            if (comp == null || !comp.PawnStates.TryGetValue(pawn, out pawnState))
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

            int cacheLastChangeTick = WeaponCacheManager.GetLastCacheChangeTick(pawn.Map);
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
            if (!comp.PawnStates.TryGetValue(pawn, out pawnState))
            {
                pawnState = new PawnWeaponState();
                comp.PawnStates[pawn] = pawnState;
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

    /// <summary>
    /// Per-map JobGiver data
    /// Save/load handling
    /// </summary>
    public class JobGiverMapComponent : MapComponent
    {
        public int lastCacheCheckTick = -1;
        public int lastProcessedTick = -1;
        private List<Pawn> cachedColonistList = new List<Pawn>();
        private int cachedColonistListTick = -1;

        internal readonly Dictionary<Pawn, JobGiver_PickUpBetterWeapon.PawnWeaponState> PawnStates =
            new Dictionary<Pawn, JobGiver_PickUpBetterWeapon.PawnWeaponState>();

        internal readonly Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, JobGiver_PickUpBetterWeapon.ValidationEntry> ValidationCache =
            new Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, JobGiver_PickUpBetterWeapon.ValidationEntry>(512);

        internal readonly Dictionary<int, (bool isProper, int expiryTick)> ProperWeaponCache =
            new Dictionary<int, (bool isProper, int expiryTick)>();

        internal readonly Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, int> FailedJobHistory =
            new Dictionary<JobGiver_PickUpBetterWeapon.PawnWeaponKey, int>();

        [Unsaved]
        internal Dictionary<int, List<Pawn>> tempBlacklistExpirySchedule = new Dictionary<int, List<Pawn>>();

        public JobGiverMapComponent(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastCacheCheckTick, "lastCacheCheckTick", -1);
            Scribe_Values.Look(ref lastProcessedTick, "lastProcessedTick", -1);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ClearAllCaches();
        }

        public void ClearAllCaches()
        {
            PawnStates.Clear();
            ValidationCache.Clear();
            ProperWeaponCache.Clear();
            FailedJobHistory.Clear();
            tempBlacklistExpirySchedule.Clear();
            ClearColonistCache();
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

        /// <summary>
        /// MapComponent
        /// </summary>
        public static JobGiverMapComponent GetComponent(Map map)
        {
            if (map == null) return null;
            return map.GetComponent<JobGiverMapComponent>();
        }

        /// <summary>
        /// Update last cache check tick for a map
        /// </summary>
        public static void UpdateCacheCheckTick(Map map)
        {
            var comp = GetComponent(map);
            if (comp != null)
                comp.lastCacheCheckTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Last check tick
        /// </summary>
        public static int GetLastCacheCheckTick(Map map)
        {
            var comp = GetComponent(map);
            return comp?.lastCacheCheckTick ?? -1;
        }

        /// <summary>
        /// Update colonist cache if needed
        /// </summary>
        public void UpdateColonistCache(int currentTick)
        {
            if (cachedColonistListTick != currentTick)
            {
                cachedColonistList.Clear();
                cachedColonistList.AddRange(map.mapPawns.FreeColonistsSpawned);
                cachedColonistListTick = currentTick;
            }
        }

        /// <summary>
        /// Cached colonists
        /// </summary>
        public List<Pawn> GetCachedColonists()
        {
            return cachedColonistList;
        }

        /// <summary>
        /// Clear colonist cache
        /// </summary>
        public void ClearColonistCache()
        {
            cachedColonistList.Clear();
            cachedColonistListTick = -1;
        }


        /// <summary>
        /// EVENT-BASED PHASE 8: Process temp blacklists expiring at this tick
        /// Tick update
        /// </summary>
        public void ProcessExpiredTempBlacklists(int tick)
        {
            if (tempBlacklistExpirySchedule.TryGetValue(tick, out var expiredPawns))
            {
                foreach (var pawn in expiredPawns)
                {
                    if (pawn == null)
                        continue;

                    if (PawnStates.TryGetValue(pawn, out var state))
                    {
                        state.TemporarilyBlacklistedWeapons.Clear();
                        state.TempBlacklistExpiry = -1;

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Cleared blacklist (event-based expiry)");
                        }
                    }
                }

                tempBlacklistExpirySchedule.Remove(tick);
            }
        }

        /// <summary>
        /// EVENT-BASED PHASE 8: Rebuild temp blacklist schedule from saved state
        /// Rebuild on load from TempBlacklistExpiry fields
        /// </summary>
        public void RebuildTempBlacklistSchedule()
        {
            tempBlacklistExpirySchedule.Clear();

            int currentTick = Find.TickManager.TicksGame;

            foreach (var kvp in PawnStates)
            {
                var pawn = kvp.Key;
                var state = kvp.Value;

                if (pawn?.Destroyed != false || pawn.Dead)
                    continue;

                if (state.TempBlacklistExpiry > currentTick)
                {
                    if (!tempBlacklistExpirySchedule.TryGetValue(state.TempBlacklistExpiry, out var list))
                    {
                        list = new List<Pawn>();
                        tempBlacklistExpirySchedule[state.TempBlacklistExpiry] = list;
                    }
                    list.Add(pawn);
                }
            }
        }

        /// <summary>
        /// EVENT-BASED PHASE 8: Reset temp blacklist schedule for new game
        /// </summary>
        public void ResetTempBlacklistSchedule()
        {
            tempBlacklistExpirySchedule.Clear();
        }

        /// <summary>
        /// EVENT-BASED PHASE 8: Remove pawn from temp blacklist schedule
        /// Helper method for when blacklist is re-scheduled or manually cleared
        /// </summary>
        public void RemoveFromTempBlacklistSchedule(Pawn pawn, int expireTick)
        {
            if (tempBlacklistExpirySchedule.TryGetValue(expireTick, out var list))
            {
                list.Remove(pawn);
                if (list.Count == 0)
                {
                    tempBlacklistExpirySchedule.Remove(expireTick);
                }
            }
        }
    }
}

using AutoArm.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoArm
{
    public class AutoArmPerfOverlayWindow : Window
    {

        public static void OpenOrBringToFront()
        {
            if (Find.WindowStack == null)
                return;

            var existingWindow = Find.WindowStack.Windows.OfType<AutoArmPerfOverlayWindow>().FirstOrDefault();
            if (existingWindow != null)
            {
                existingWindow.BringToFront();
            }
            else
            {
                Find.WindowStack.Add(new AutoArmPerfOverlayWindow());
            }
        }

        private void BringToFront()
        {
            if (Find.WindowStack == null)
                return;

            Find.WindowStack.TryRemove(this, doCloseSound: false);
            Find.WindowStack.Add(this);
        }

        private float _dynamicHeight = 460f;

        private const float MIN_HEIGHT = 200f;
        private const float MAX_HEIGHT = 650f;
        public override Vector2 InitialSize => new Vector2(320f, _dynamicHeight);

        private readonly Queue<(float time, long count)> _jobsCreatedHistory = new Queue<(float, long)>();

        private readonly Queue<(float time, long count)> _weaponsEvaluatedHistory = new Queue<(float, long)>();
        private readonly Queue<(float time, long count)> _cacheHitsHistory = new Queue<(float, long)>();
        private float _jobsPerMinute = 0f;
        private float _evaluationsPerMinute = 0f;
        private static float _cacheSavesPerMinute = 0f;

        private static long _jobsCreated = 0;
        private static long _weaponsEvaluated = 0;
        private static long _cacheHits = 0;
        private static long _cacheMisses = 0;
        private static long _validationCalls = 0;
        private static long _validationPassed = 0;
        private static long _sidearmChecks = 0;
        private static long _sidearmAdds = 0;

        private static long _rangedWeaponsEvaluated = 0;

        private static long _meleeWeaponsEvaluated = 0;

        private static long _propertyCacheHits = 0;
        private static long _propertyCacheMisses = 0;
        private static long _validationCacheHits = 0;
        private static long _validationCacheMisses = 0;
        private static long _skillCacheHits = 0;
        private static long _skillCacheMisses = 0;
        private static long _eligibilityCacheHits = 0;
        private static long _eligibilityCacheMisses = 0;

        private static int _lastGen0Count = 0;

        private static int _lastGen1Count = 0;
        private static float _gen0PerMinute = 0f;
        private static float _gen1PerMinute = 0f;
        private static float _lastGCCheckTime = 0f;
        private static readonly Queue<(float time, int size)> _cacheSizeHistory = new Queue<(float, int)>();
        private static float _cacheGrowthRate = 0f;
        private static float _totalCacheMemoryMB = 0f;

        private static Dictionary<string, int> _failureReasons = new Dictionary<string, int>();

        private static long _weaponsSearched = 0;
        private static long _weaponsAvailable = 0;
        private static long _totalSearchDistance = 0;
        private static int _searchCount = 0;
        private static int _activeCooldowns = 0;
        private static int _cacheSize = 0;
        private static float _cacheMemoryMB = 0f;

        private static int _totalPawnsProcessed = 0;

        private static float _pawnsPerMinute = 0f;

        private static int _lastProcessTick = -1;

        private static int _lastTickTracked = -1;

        private static float _autoArmTimeThisTick = 0f;
        private static Queue<float> _recentTickTimes = new Queue<float>();
        private static float _recentTickTimesSum = 0f;
        private const int MAX_TICK_HISTORY = 5;

        private static float _tickStartTime = 0f;

        private static float _totalGameTickTime = 0f;
        private static Queue<float> _recentTotalTickTimes = new Queue<float>();

        private static float _thinkTreeTimeThisTick = 0f;

        private static Queue<float> _recentThinkTreeTimes = new Queue<float>();
        private static float _avgThinkTreeTime = 0f;
        private static float _avgOtherTime = 0f;

        private static readonly Queue<(int tick, float realTime)> _tpsHistory = new Queue<(int, float)>();

        private const int TPS_SAMPLE_SIZE = 60;
        private static float _actualTPS = 0f;

        private static Queue<(float time, int count)> _pawnsProcessedHistory = new Queue<(float, int)>();

        private static int _pawnsThisTick = 0;

        private static bool _isWindowOpen = false;

        /// <summary>
        /// Overlay open
        /// </summary>
        public static bool IsWindowOpen() => _isWindowOpen;

        private static float _peakTimePerTick = 0f;

        private static float _peakTickPercent = 0f;
        private static float _peakPawnsPerMinute = 0f;
        private static float _peakJobsPerMinute = 0f;
        private static int _peakWeaponsSearched = 0;
        private static float _peakSearchDistance = 0f;


        private float _lastUpdateTime = 0f;

        private float _currentUpdateInterval = 0.5f;
        private float _targetUpdateInterval = 0.5f;
        private float _lastIntervalChangeTime = 0f;
        private const float INTERVAL_CHANGE_COOLDOWN = 2.0f;

        private const float MIN_UPDATE_INTERVAL = 0.25f;

        private const float NORMAL_UPDATE_INTERVAL = 0.5f;
        private const float SLOW_UPDATE_INTERVAL = 1.0f;
        private const float EMERGENCY_UPDATE_INTERVAL = 2.0f;
        private const float STARTUP_SLOW_INTERVAL = 1.0f;

        private float _windowOpenTime = 0f;

        private const float STARTUP_DELAY = 0.5f;

        private TimeSpeed _lastTimeSpeed = TimeSpeed.Paused;


        private static bool ShouldCollectMetrics()
        {
            if (!_isWindowOpen)
                return false;

            var tickManager = Find.TickManager;
            if (tickManager == null || tickManager.Paused)
                return false;

            if (AutoArm.UI.StatusOverviewRenderer.isGatheringDebugData)
                return false;

            return true;
        }

        public static void ReportJobCreated()
        {
            if (ShouldCollectMetrics())
            {
                _jobsCreated++;
            }
        }

        public static void ReportWeaponEvaluated()
        {
            if (ShouldCollectMetrics())
            {
                _weaponsEvaluated++;
            }
        }

        public static void ReportWeaponEvaluatedWithType(bool isRanged)
        {
            if (ShouldCollectMetrics())
            {
                _weaponsEvaluated++;

                if (isRanged)
                {
                    _rangedWeaponsEvaluated++;
                }
                else
                {
                    _meleeWeaponsEvaluated++;
                }
            }
        }

        public static void ReportCacheHit()
        {
            if (ShouldCollectMetrics()) _cacheHits++;
        }

        public static void ReportCacheMiss()
        {
            if (ShouldCollectMetrics()) _cacheMisses++;
        }

        public static void ReportValidation(bool passed)
        {
            if (ShouldCollectMetrics())
            {
                _validationCalls++;
                if (passed) _validationPassed++;
            }
        }

        public static void ReportSidearmCheck()
        {
            if (ShouldCollectMetrics()) _sidearmChecks++;
        }

        public static void ReportSidearmAdd()
        {
            if (ShouldCollectMetrics()) _sidearmAdds++;
        }

        public static void ReportTickProcessing(int pawnsProcessed, int pawnsThrottled, int tick)
        {
            if (ShouldCollectMetrics())
            {
                _lastProcessTick = tick;
                _totalPawnsProcessed += pawnsProcessed;
                _pawnsThisTick += pawnsProcessed;

                float now = Time.realtimeSinceStartup;
                _pawnsProcessedHistory.Enqueue((now, pawnsProcessed));

                while (_pawnsProcessedHistory.Count > 0 &&
                       now - _pawnsProcessedHistory.Peek().time > 60f)
                    _pawnsProcessedHistory.Dequeue();
            }
        }

        public static void ReportFailureReason(string reason)
        {
            if (ShouldCollectMetrics() && !string.IsNullOrEmpty(reason))
            {
                if (!_failureReasons.ContainsKey(reason))
                    _failureReasons[reason] = 0;
                _failureReasons[reason]++;
                _failureReasonsDirty = true;
            }
        }

        public static void ReportSearchStats(int weaponsSearched, int weaponsAvailable)
        {
            if (ShouldCollectMetrics())
            {
                _weaponsSearched += weaponsSearched;
                _weaponsAvailable += weaponsAvailable;
                _searchCount++;
            }
        }

        public static void ReportWeaponDistance(float distance)
        {
            if (ShouldCollectMetrics())
            {
                _totalSearchDistance += (long)distance;
            }
        }

        public static void ReportActiveCooldowns(int count)
        {
            if (ShouldCollectMetrics())
            {
                _activeCooldowns = count;
            }
        }

        public static void ReportCacheStats(int size, float memoryMB)
        {
            if (ShouldCollectMetrics())
            {
                _cacheSize = size;
                _cacheMemoryMB = memoryMB;
            }
        }

        public static void ReportPropertyCacheHit()
        {
            if (ShouldCollectMetrics()) _propertyCacheHits++;
        }

        public static void ReportPropertyCacheMiss()
        {
            if (ShouldCollectMetrics()) _propertyCacheMisses++;
        }

        public static void ReportValidationCacheHit()
        {
            if (ShouldCollectMetrics()) _validationCacheHits++;
        }

        public static void ReportValidationCacheMiss()
        {
            if (ShouldCollectMetrics()) _validationCacheMisses++;
        }

        public static void ReportSkillCacheHit()
        {
            if (ShouldCollectMetrics()) _skillCacheHits++;
        }

        public static void ReportSkillCacheMiss()
        {
            if (ShouldCollectMetrics()) _skillCacheMisses++;
        }

        public static void ReportEligibilityCacheHit()
        {
            if (ShouldCollectMetrics()) _eligibilityCacheHits++;
        }

        public static void ReportEligibilityCacheMiss()
        {
            if (ShouldCollectMetrics()) _eligibilityCacheMisses++;
        }

        public static void ReportScoreTime(long ticks)
        {
        }

        private static float _operationStartTime = 0f;

        private static int _operationCount = 0;

        private static List<KeyValuePair<string, int>> _sortedFailureReasons = new List<KeyValuePair<string, int>>();

        private static bool _failureReasonsDirty = true;

        public static void StartTiming()
        {
            if (!_isWindowOpen)
                return;

            UnityEngine.Profiling.Profiler.BeginSample("AutoArm.JobGiver");

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick != _lastTickTracked)
            {
                if (_lastTickTracked >= 0)
                {
                    _recentTickTimes.Enqueue(_autoArmTimeThisTick);
                    _recentTickTimesSum += _autoArmTimeThisTick;
                    while (_recentTickTimes.Count > MAX_TICK_HISTORY)
                    {
                        float removed = _recentTickTimes.Dequeue();
                        _recentTickTimesSum -= removed;
                    }

                    _recentThinkTreeTimes.Enqueue(_thinkTreeTimeThisTick);
                    while (_recentThinkTreeTimes.Count > MAX_TICK_HISTORY)
                        _recentThinkTreeTimes.Dequeue();

                    if (_recentThinkTreeTimes.Count > 0)
                    {
                        float thinkSum = 0f;
                        foreach (float time in _recentThinkTreeTimes)
                            thinkSum += time;
                        _avgThinkTreeTime = thinkSum / _recentThinkTreeTimes.Count;

                        float totalAvg = _recentTickTimes.Count > 0 ? _recentTickTimesSum / _recentTickTimes.Count : 0f;
                        _avgOtherTime = totalAvg - _avgThinkTreeTime;
                        if (_avgOtherTime < 0) _avgOtherTime = 0f;
                    }
                }

                _lastTickTracked = currentTick;
                _autoArmTimeThisTick = 0f;
                _thinkTreeTimeThisTick = 0f;
                _pawnsThisTick = 0;
            }

            _operationStartTime = Time.realtimeSinceStartup;
        }

        public static void EndTiming()
        {
            if (!_isWindowOpen)
                return;

            UnityEngine.Profiling.Profiler.EndSample();

            if (_operationStartTime > 0)
            {
                float elapsed = (Time.realtimeSinceStartup - _operationStartTime) * 1000f;

                if (elapsed > 0)
                {
                    _autoArmTimeThisTick += elapsed;
                    _thinkTreeTimeThisTick += elapsed;
                    _operationCount++;

                }

                _operationStartTime = 0f;
            }
        }

        public static void IncrementOperations()
        {
        }

        public AutoArmPerfOverlayWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            closeOnCancel = false;
            closeOnAccept = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = false;
            preventCameraMotion = false;
            onlyOneOfTypeAllowed = true;
            doWindowBackground = true;
            drawShadow = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            _isWindowOpen = true;
            _windowOpenTime = Time.realtimeSinceStartup;

            _lastTimeSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Paused;

            ResetCounters();
            ResetPeakValues();
            _totalPawnsProcessed = 0;
            _jobsCreatedHistory.Clear();
            _weaponsEvaluatedHistory.Clear();
            _cacheHitsHistory.Clear();
        }

        protected override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            windowRect = new Rect(12f, 120f, size.x, size.y);
            resizeable = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                float currentTime = Time.realtimeSinceStartup;

                bool pastStartupDelay = (currentTime - _windowOpenTime) >= STARTUP_DELAY;

                UpdateTickMetrics();

                float interval = GetDynamicUpdateInterval(currentTime);
                bool shouldUpdate = pastStartupDelay && (currentTime - _lastUpdateTime >= interval);

                if (shouldUpdate)
                {
                    UpdateMetrics();
                    _lastUpdateTime = currentTime;
                }

                if (!pastStartupDelay)
                {
                    var startupListing = new Listing_Standard();
                    startupListing.Begin(inRect);
                    startupListing.Label($"Initializing... ({STARTUP_DELAY - (currentTime - _windowOpenTime):F1}s)");
                    startupListing.End();
                    return;
                }

                var listing = new Listing_Standard();
                listing.Begin(inRect);

                var tickManager = Find.TickManager;
                bool isPaused = tickManager?.Paused ?? true;

                if (tickManager != null)
                {
                    TimeSpeed currentSpeed = tickManager.CurTimeSpeed;
                    if (currentSpeed != _lastTimeSpeed)
                    {
                        bool isPausing = currentSpeed == TimeSpeed.Paused;
                        bool isUnpausing = _lastTimeSpeed == TimeSpeed.Paused && currentSpeed != TimeSpeed.Paused;
                        bool isSpeedChange = !isPausing && !isUnpausing;

                        if (isPausing)
                        {
                        }
                        else if (isUnpausing)
                        {
                            ResetCounters();
                            ResetPeakValues();
                            _jobsCreatedHistory.Clear();
                            _weaponsEvaluatedHistory.Clear();
                            _cacheHitsHistory.Clear();
                        }
                        else if (isSpeedChange)
                        {
                            ResetCounters();
                            ResetPeakValues();
                            _jobsCreatedHistory.Clear();
                            _weaponsEvaluatedHistory.Clear();
                            _cacheHitsHistory.Clear();
                        }

                        _lastTimeSpeed = currentSpeed;
                    }
                }

                TimeSpeed speed = tickManager?.CurTimeSpeed ?? TimeSpeed.Paused;
                string speedLabel = speed == TimeSpeed.Paused ? "PAUSED" :
                                   speed == TimeSpeed.Normal ? "1x" :
                                   speed == TimeSpeed.Fast ? "2x" :
                                   speed == TimeSpeed.Superfast ? "3x" : "4x";

                bool raidActive = ModInit.IsLargeRaidActive;
                bool disableDuringRaids = AutoArmMod.settings?.disableDuringRaids == true;

                if (raidActive)
                {
                    string raidStatus = disableDuringRaids
                        ? "RAID ACTIVE (AutoArm disabled)"
                        : "RAID ACTIVE";
                    Color raidColor = disableDuringRaids
                        ? new Color(1f, 0.35f, 0.35f)
                        : new Color(1f, 0.75f, 0.3f);
                    LabelPair(listing, "Status", raidStatus, raidColor);
                }
                else if (isPaused)
                {
                    LabelPair(listing, "Status", "PAUSED", Color.yellow);
                }

                if (!isPaused)
                {
                    if (tickManager != null && _actualTPS > 0)
                    {
                        float targetTPS = tickManager.TickRateMultiplier * 60f;

                        float actualTPS = _actualTPS;

                        Color tpsColor = actualTPS >= targetTPS * 0.95f ? Color.green : Color.yellow;

                        LabelPair(listing, "Speed", $"{speedLabel} ({actualTPS:F0}/{targetTPS:F0} TPS)", tpsColor);
                    }
                    else
                    {
                        LabelPair(listing, "Speed", speedLabel, Color.white);
                    }
                }
                listing.Gap(6f);

                var oldColor = GUI.color;
                GUI.color = new Color(0.7f, 0.9f, 1f);
                listing.Label("ACTIVITY");
                GUI.color = oldColor;

                string pawnsText = _pawnsPerMinute.ToString("F0");
                if (_peakPawnsPerMinute > _pawnsPerMinute)
                    pawnsText += $" (pk:{_peakPawnsPerMinute:F0})";
                LabelPair(listing, "Pawns/min", pawnsText,
                    _pawnsPerMinute > 0 ? Color.green : Color.gray);

                string jobsText = _jobsPerMinute.ToString("F1");
                if (_peakJobsPerMinute > _jobsPerMinute)
                    jobsText += $" (pk:{_peakJobsPerMinute:F1})";
                LabelPair(listing, "Jobs/min", jobsText,
                    _jobsPerMinute > 0 ? Color.green : Color.gray);
                if (_evaluationsPerMinute > 0)
                {
                    LabelPair(listing, "Evals/min", _evaluationsPerMinute.ToString("F0"), Color.gray);
                }

                float jobsPerPawn = _totalPawnsProcessed > 0 ? (float)_jobsCreated / _totalPawnsProcessed : 0f;
                Color jobsPerPawnColor = _totalPawnsProcessed == 0 ? Color.gray :
                                         jobsPerPawn > 0.1f ? Color.green :
                                         jobsPerPawn > 0.05f ? Color.yellow : Color.gray;
                LabelPair(listing, "Jobs/pawn", $"{jobsPerPawn:F2}", jobsPerPawnColor);

                listing.Gap(6f);

                GUI.color = new Color(0.85f, 1f, 0.85f);
                listing.Label("CACHE");
                GUI.color = oldColor;

                LabelPair(listing, "Weapons cached", _cacheSize.ToString(),
                    _cacheSize > 0 ? Color.green : Color.gray);

                string cacheSavesText = _cacheSavesPerMinute.ToString("F0");
                Color cacheSavesColor;

                if (_cacheSavesPerMinute > 1000)
                {
                    cacheSavesColor = Color.green;
                }
                else if (_cacheSavesPerMinute > 100)
                {
                    cacheSavesColor = Color.yellow;
                }
                else if (_cacheSavesPerMinute > 0)
                {
                    cacheSavesColor = Color.white;
                }
                else
                {
                    cacheSavesColor = Color.gray;
                    cacheSavesText = "0";
                }

                LabelPair(listing, "Cache saves/min", cacheSavesText, cacheSavesColor);

                if (_searchCount > 0)
                {
                    float avgSearched = (float)_weaponsSearched / _searchCount;
                    string searchedText = avgSearched.ToString("F0");
                    if (_peakWeaponsSearched > avgSearched)
                        searchedText += $" (pk:{_peakWeaponsSearched})";
                    LabelPair(listing, "Avg searched", searchedText,
                        avgSearched < 50 ? Color.green : Color.yellow);
                }
                else
                {
                    LabelPair(listing, "Avg searched", "0", Color.gray);
                }

                if (_propertyCacheHits + _propertyCacheMisses > 0)
                {
                    float hitRate = (_propertyCacheHits * 100f) / (_propertyCacheHits + _propertyCacheMisses);
                    string propertyText = $"{hitRate:F0}% ({_propertyCacheHits} hits)";
                    Color propertyColor = hitRate > 95f ? Color.green : Color.yellow;
                    LabelPair(listing, "Property", propertyText, propertyColor);
                }
                else
                {
                    LabelPair(listing, "Property", "0% (0 hits)", Color.gray);
                }

                if (_validationCacheHits + _validationCacheMisses > 0)
                {
                    float hitRate = (_validationCacheHits * 100f) / (_validationCacheHits + _validationCacheMisses);
                    string validationText = $"{hitRate:F0}% ({_validationCacheHits} hits)";
                    Color validationColor = hitRate > 85f ? Color.green : Color.yellow;
                    LabelPair(listing, "Validation", validationText, validationColor);
                }
                else
                {
                    LabelPair(listing, "Validation", "0% (0 hits)", Color.gray);
                }

                if (_skillCacheHits + _skillCacheMisses > 0)
                {
                    float hitRate = (_skillCacheHits * 100f) / (_skillCacheHits + _skillCacheMisses);
                    string skillText = $"{hitRate:F0}% ({_skillCacheHits} hits)";
                    Color skillColor = hitRate > 90f ? Color.green : Color.yellow;
                    LabelPair(listing, "Skill", skillText, skillColor);
                }
                else
                {
                    LabelPair(listing, "Skill", "0% (0 hits)", Color.gray);
                }

                if (_eligibilityCacheHits + _eligibilityCacheMisses > 0)
                {
                    float hitRate = (_eligibilityCacheHits * 100f) / (_eligibilityCacheHits + _eligibilityCacheMisses);
                    string eligibilityText = $"{hitRate:F0}% ({_eligibilityCacheHits} hits)";
                    Color eligibilityColor = hitRate > 80f ? Color.green : Color.yellow;
                    LabelPair(listing, "Eligibility", eligibilityText, eligibilityColor);
                }
                else
                {
                    LabelPair(listing, "Eligibility", "0% (0 hits)", Color.gray);
                }

                listing.Gap(6f);


                if (_sortedFailureReasons.Count > 0)
                {
                    GUI.color = new Color(1f, 0.7f, 0.7f);
                    listing.Label("BLOCKERS");
                    GUI.color = oldColor;


                    var consolidatedReasons = new Dictionary<string, int>();
                    foreach (var reason in _sortedFailureReasons)
                    {
                        string shortReason = reason.Key;

                        if (shortReason.Contains("Reserved"))
                            shortReason = "Reserved";
                        else if (shortReason.Contains("Already owned") || shortReason.Contains("Equipped by someone") || shortReason.Contains("someone's inventory"))
                            shortReason = "Owned";
                        else if (shortReason.Contains("ideology") || shortReason.Contains("Ideology"))
                            shortReason = "Ideology";
                        else if (shortReason.Contains("outfit") || shortReason.Contains("Outfit"))
                            shortReason = "Outfit filter";
                        else if (shortReason.Contains("Quest") || shortReason.Contains("Temporary colonist"))
                            shortReason = "Quest pawn";
                        else if (shortReason.Contains("unreachable") || shortReason.Contains("Cannot reach"))
                            shortReason = "Unreachable";
                        else if (shortReason.Contains("No ammo") && shortReason.Contains("Combat Extended"))
                            shortReason = "No ammo (CE)";
                        else if (shortReason.Contains("Brawler"))
                            shortReason = "Brawler trait";
                        else if (shortReason.Contains("skill too low") || shortReason.Contains("shooting skill"))
                            shortReason = "Skill too low";
                        else if (shortReason.Contains("mental state"))
                            shortReason = "Mental state";
                        else if (shortReason.Contains("Currently hauling"))
                            shortReason = "Hauling";
                        else if (shortReason.Contains("ritual") || shortReason.Contains("ceremony"))
                            shortReason = "In ritual";
                        else if (shortReason.Contains("Forbidden"))
                            shortReason = "Forbidden";
                        else if (shortReason.Contains("Too young"))
                            shortReason = "Too young";
                        else if (shortReason.Contains("blacklisted"))
                            shortReason = "Blacklisted";
                        else if (shortReason.Contains("dropped"))
                            shortReason = "Recently dropped";
                        else if (shortReason.Contains("SimpleSidearms"))
                            shortReason = "SimpleSidearms";
                        else if (shortReason.Contains("Score too low"))
                            shortReason = "Score too low";
                        else if (shortReason.Contains("No weapons found"))
                            shortReason = "No weapons found";
                        else if (shortReason.Contains("Failed validation"))
                            shortReason = "Failed validation";
                        else if (shortReason.Length > 20)
                            shortReason = shortReason.Substring(0, 20);

                        if (!consolidatedReasons.ContainsKey(shortReason))
                            consolidatedReasons[shortReason] = 0;
                        consolidatedReasons[shortReason] += reason.Value;
                    }

                    var sortedConsolidated = consolidatedReasons.OrderByDescending(kvp => kvp.Value);

                    foreach (var reason in sortedConsolidated)
                    {
                        LabelPair(listing, reason.Key, reason.Value.ToString(), Color.gray);
                    }
                }

                listing.End();

                float calculatedHeight = 0f;
                calculatedHeight += 24f;
                calculatedHeight += 8f;
                calculatedHeight += 22f + 22f * 3;
                calculatedHeight += 8f;

                int cacheItems = 7;
                calculatedHeight += 22f + 22f * cacheItems;
                calculatedHeight += 8f;

                int consolidatedBlockersCount = 0;
                if (_sortedFailureReasons.Count > 0)
                {
                    var tempConsolidated = new HashSet<string>();
                    foreach (var reason in _sortedFailureReasons)
                    {
                        string shortReason = reason.Key;

                        if (shortReason.Contains("Reserved"))
                            shortReason = "Reserved";
                        else if (shortReason.Contains("Already owned") || shortReason.Contains("Equipped by someone") || shortReason.Contains("someone's inventory"))
                            shortReason = "Owned";
                        else if (shortReason.Contains("ideology") || shortReason.Contains("Ideology"))
                            shortReason = "Ideology";
                        else if (shortReason.Contains("outfit") || shortReason.Contains("Outfit"))
                            shortReason = "Outfit filter";
                        else if (shortReason.Contains("Quest") || shortReason.Contains("Temporary colonist"))
                            shortReason = "Quest pawn";
                        else if (shortReason.Contains("unreachable") || shortReason.Contains("Cannot reach"))
                            shortReason = "Unreachable";
                        else if (shortReason.Contains("No ammo") && shortReason.Contains("Combat Extended"))
                            shortReason = "No ammo (CE)";
                        else if (shortReason.Contains("Brawler"))
                            shortReason = "Brawler trait";
                        else if (shortReason.Contains("skill too low") || shortReason.Contains("shooting skill"))
                            shortReason = "Skill too low";
                        else if (shortReason.Contains("mental state"))
                            shortReason = "Mental state";
                        else if (shortReason.Contains("Currently hauling"))
                            shortReason = "Hauling";
                        else if (shortReason.Contains("ritual") || shortReason.Contains("ceremony"))
                            shortReason = "In ritual";
                        else if (shortReason.Contains("Forbidden"))
                            shortReason = "Forbidden";
                        else if (shortReason.Contains("Too young"))
                            shortReason = "Too young";
                        else if (shortReason.Contains("blacklisted"))
                            shortReason = "Blacklisted";
                        else if (shortReason.Contains("dropped"))
                            shortReason = "Recently dropped";
                        else if (shortReason.Contains("SimpleSidearms"))
                            shortReason = "SimpleSidearms";
                        else if (shortReason.Contains("Score too low"))
                            shortReason = "Score too low";
                        else if (shortReason.Contains("No weapons found"))
                            shortReason = "No weapons found";
                        else if (shortReason.Contains("Failed validation"))
                            shortReason = "Failed validation";
                        else if (shortReason.Length > 20)
                            shortReason = shortReason.Substring(0, 20);

                        tempConsolidated.Add(shortReason);
                    }
                    consolidatedBlockersCount = tempConsolidated.Count;
                    calculatedHeight += 22f + 22f * consolidatedBlockersCount;
                }
                calculatedHeight += 50f;

                if (Math.Abs(calculatedHeight - _dynamicHeight) > 5f)
                {
                    _dynamicHeight = Mathf.Clamp(calculatedHeight, MIN_HEIGHT, MAX_HEIGHT);
                    windowRect.height = _dynamicHeight;
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorUI(ex, "PerfOverlay", "DoWindowContents");
                Widgets.Label(inRect, "Error displaying overlay");
            }
        }


        private void UpdateTickMetrics()
        {

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (currentTick != _lastTickTracked && _lastTickTracked >= 0)
            {
                float realTime = Time.realtimeSinceStartup;
                if (_tickStartTime > 0)
                {
                    float totalTickTime = (realTime - _tickStartTime) * 1000f;
                    _recentTotalTickTimes.Enqueue(totalTickTime);
                    while (_recentTotalTickTimes.Count > MAX_TICK_HISTORY)
                        _recentTotalTickTimes.Dequeue();

                    if (_recentTotalTickTimes.Count > 0)
                    {
                        float sum = 0f;
                        foreach (float time in _recentTotalTickTimes)
                            sum += time;
                        _totalGameTickTime = sum / _recentTotalTickTimes.Count;
                    }
                }
                _tickStartTime = realTime;

                _recentTickTimes.Enqueue(_autoArmTimeThisTick);
                _recentTickTimesSum += _autoArmTimeThisTick;

                int skippedTicks = currentTick - _lastTickTracked - 1;
                if (skippedTicks > 0 && skippedTicks < 100)
                {
                    for (int i = 0; i < skippedTicks; i++)
                    {
                        _recentTickTimes.Enqueue(0f);
                    }
                }

                while (_recentTickTimes.Count > MAX_TICK_HISTORY)
                {
                    float removed = _recentTickTimes.Dequeue();
                    _recentTickTimesSum -= removed;
                }

                _tpsHistory.Enqueue((currentTick, realTime));

                while (_tpsHistory.Count > TPS_SAMPLE_SIZE)
                    _tpsHistory.Dequeue();

                if (_tpsHistory.Count >= 10)
                {
                    var oldest = _tpsHistory.Peek();
                    var newest = (currentTick, realTime);
                    int tickDelta = newest.Item1 - oldest.tick;
                    float timeDelta = newest.Item2 - oldest.realTime;

                    if (timeDelta > 0.1f)
                    {
                        _actualTPS = tickDelta / timeDelta;
                    }
                }

                _autoArmTimeThisTick = 0f;
            }

            _lastTickTracked = currentTick;

            if (_recentTickTimes.Count > 0)
            {
                float avgTickTime = _recentTickTimesSum / _recentTickTimes.Count;

                if (avgTickTime > _peakTimePerTick)
                    _peakTimePerTick = avgTickTime;

                TimeSpeed speed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Normal;
                float expectedTickMs = speed == TimeSpeed.Normal ? 16.67f :
                                       speed == TimeSpeed.Fast ? 8.33f :
                                       speed == TimeSpeed.Superfast ? 5.56f : 4.17f;

                float percentOfTick = (avgTickTime / expectedTickMs) * 100f;
                if (percentOfTick > _peakTickPercent)
                    _peakTickPercent = percentOfTick;
            }
        }

        private void UpdateMetrics()
        {
            float currentTime = Time.realtimeSinceStartup;
            bool pastWarmup = (currentTime - _windowOpenTime) >= STARTUP_DELAY;

            TimeSpeed gameSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Paused;
            if (gameSpeed == TimeSpeed.Paused)
                return;

            float now = Time.realtimeSinceStartup;

            _jobsCreatedHistory.Enqueue((now, _jobsCreated));
            while (_jobsCreatedHistory.Count > 0 &&
                   (now - _jobsCreatedHistory.Peek().time) > 60f)
                _jobsCreatedHistory.Dequeue();

            if (_jobsCreatedHistory.Count > 1)
            {
                var oldest = _jobsCreatedHistory.Peek();
                float timeDiffSeconds = now - oldest.time;
                float timeDiffMinutes = timeDiffSeconds / 60f;
                if (timeDiffMinutes > 0.083f)
                {
                    _jobsPerMinute = (_jobsCreated - oldest.count) / timeDiffMinutes;
                    if (pastWarmup && timeDiffMinutes > 0.15f && _jobsPerMinute > _peakJobsPerMinute)
                        _peakJobsPerMinute = _jobsPerMinute;
                }
            }

            _weaponsEvaluatedHistory.Enqueue((now, _weaponsEvaluated));
            while (_weaponsEvaluatedHistory.Count > 0 &&
                   (now - _weaponsEvaluatedHistory.Peek().time) > 60f)
                _weaponsEvaluatedHistory.Dequeue();

            if (_weaponsEvaluatedHistory.Count > 1)
            {
                var oldest = _weaponsEvaluatedHistory.Peek();
                float timeDiffMinutes = (now - oldest.time) / 60f;
                if (timeDiffMinutes > 0.083f)
                    _evaluationsPerMinute = (_weaponsEvaluated - oldest.count) / timeDiffMinutes;
            }

            _cacheHitsHistory.Enqueue((now, _cacheHits));
            while (_cacheHitsHistory.Count > 0 &&
                   (now - _cacheHitsHistory.Peek().time) > 60f)
                _cacheHitsHistory.Dequeue();

            if (_cacheHitsHistory.Count > 1)
            {
                var oldest = _cacheHitsHistory.Peek();
                float timeDiffMinutes = (now - oldest.time) / 60f;
                if (timeDiffMinutes > 0.083f)
                {
                    _cacheSavesPerMinute = (_cacheHits - oldest.count) / timeDiffMinutes;
                }
            }

            if (_pawnsProcessedHistory.Count > 0)
            {
                float totalPawns = 0;
                foreach (var entry in _pawnsProcessedHistory)
                    totalPawns += entry.count;

                float timeSpan = Time.realtimeSinceStartup - _pawnsProcessedHistory.Peek().time;
                if (timeSpan > 2f)
                {
                    _pawnsPerMinute = (totalPawns / timeSpan) * 60f;
                    if (pastWarmup && timeSpan > 5f && _pawnsPerMinute > _peakPawnsPerMinute)
                        _peakPawnsPerMinute = _pawnsPerMinute;
                }
            }

            if (_recentTickTimes.Count > 0)
            {
                float avgTickTime = _recentTickTimesSum / _recentTickTimes.Count;

                if (avgTickTime > _peakTimePerTick)
                {
                    _peakTimePerTick = avgTickTime;
                }

                TimeSpeed currentSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Normal;
                float expectedTickMs = currentSpeed == TimeSpeed.Normal ? 16.67f :
                                     currentSpeed == TimeSpeed.Fast ? 8.33f :
                                     currentSpeed == TimeSpeed.Superfast ? 5.56f : 4.17f;
                float percentOfTick = (avgTickTime / expectedTickMs) * 100f;

                if (percentOfTick > _peakTickPercent)
                {
                    _peakTickPercent = percentOfTick;
                }

            }

            if (pastWarmup && _searchCount > 0)
            {
                float avgSearched = (float)_weaponsSearched / _searchCount;
                if (avgSearched > _peakWeaponsSearched)
                    _peakWeaponsSearched = (int)avgSearched;

                float avgDistance = _totalSearchDistance / (float)_searchCount;
                if (avgDistance > _peakSearchDistance)
                    _peakSearchDistance = avgDistance;
            }

            if (_failureReasonsDirty && _failureReasons.Count > 0)
            {
                _sortedFailureReasons.Clear();
                _sortedFailureReasons.AddRange(_failureReasons);
                _sortedFailureReasons.Sort((a, b) => b.Value.CompareTo(a.Value));
                _failureReasonsDirty = false;
            }

            if (Find.CurrentMap != null)
            {
                _cacheSize = Caching.WeaponCacheManager.GetCacheWeaponCount(Find.CurrentMap);
            }

            if (_lastGCCheckTime > 0f)
            {
                int currentGen0 = GC.CollectionCount(0);
                int currentGen1 = GC.CollectionCount(1);

                float timeDiffMinutes = (now - _lastGCCheckTime) / 60f;
                if (timeDiffMinutes > 0.083f)
                {
                    _gen0PerMinute = (currentGen0 - _lastGen0Count) / timeDiffMinutes;
                    _gen1PerMinute = (currentGen1 - _lastGen1Count) / timeDiffMinutes;

                    _lastGen0Count = currentGen0;
                    _lastGen1Count = currentGen1;
                    _lastGCCheckTime = now;
                }
            }
            else
            {
                _lastGen0Count = GC.CollectionCount(0);
                _lastGen1Count = GC.CollectionCount(1);
                _lastGCCheckTime = now;
            }

            _cacheSizeHistory.Enqueue((currentTime, _cacheSize));

            while (_cacheSizeHistory.Count > 0 &&
                   currentTime - _cacheSizeHistory.Peek().time > 60f)
                _cacheSizeHistory.Dequeue();

            if (_cacheSizeHistory.Count > 1)
            {
                var oldest = _cacheSizeHistory.Peek();
                var latest = (currentTime, _cacheSize);
                float timeSpan = latest.Item1 - oldest.time;
                if (timeSpan > 5f)
                {
                    float entriesChange = latest.Item2 - oldest.size;
                    _cacheGrowthRate = (entriesChange / timeSpan) * 60f;
                }
            }

            _totalCacheMemoryMB = (_cacheSize * 100f) / (1024f * 1024f);
        }

        private void LabelPair(Listing_Standard listing, string label, string value, Color? valueColor = null, float labelWidthPct = 0.45f)
        {
            var rect = listing.GetRect(20f);
            var labelRect = new Rect(rect.x, rect.y, rect.width * labelWidthPct, rect.height);
            var valueRect = new Rect(rect.x + rect.width * labelWidthPct, rect.y, rect.width * (1f - labelWidthPct), rect.height);

            Widgets.Label(labelRect, label);

            var oldColor = GUI.color;
            if (valueColor.HasValue)
                GUI.color = valueColor.Value;
            Widgets.Label(valueRect, value);
            GUI.color = oldColor;
        }

        private Color GetHealthColor(float value, float goodThreshold, float warnThreshold, bool higherIsBetter)
        {
            if (higherIsBetter)
            {
                if (value >= goodThreshold) return Color.green;
                if (value >= warnThreshold) return Color.yellow;
                return Color.yellow;
            }
            else
            {
                if (value <= warnThreshold) return Color.green;
                if (value <= goodThreshold) return Color.yellow;
                return Color.yellow;
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            _isWindowOpen = false;
            ResetCounters();
            ResetPeakValues();
            _jobsCreatedHistory.Clear();
            _weaponsEvaluatedHistory.Clear();
            _cacheHitsHistory.Clear();
        }

        private static void ResetPeakValues()
        {
            _peakTimePerTick = 0f;
            _peakTickPercent = 0f;
            _peakPawnsPerMinute = 0f;
            _peakJobsPerMinute = 0f;
            _peakWeaponsSearched = 0;
            _peakSearchDistance = 0f;
        }

        private static void ResetCounters()
        {
            _jobsCreated = 0;
            _weaponsEvaluated = 0;
            _cacheHits = 0;
            _cacheMisses = 0;
            _validationCalls = 0;
            _validationPassed = 0;
            _sidearmChecks = 0;
            _sidearmAdds = 0;
            _failureReasons.Clear();
            _weaponsSearched = 0;
            _weaponsAvailable = 0;
            _totalSearchDistance = 0;
            _searchCount = 0;
            _activeCooldowns = 0;
            _totalPawnsProcessed = 0;
            _pawnsPerMinute = 0f;
            _cacheSavesPerMinute = 0f;
            _pawnsThisTick = 0;

            _operationStartTime = 0f;
            _operationCount = 0;
            _lastTickTracked = -1;
            _autoArmTimeThisTick = 0f;
            _recentTickTimes.Clear();

            _tpsHistory.Clear();
            _actualTPS = 0f;

            _tickStartTime = 0f;
            _totalGameTickTime = 0f;
            _recentTotalTickTimes.Clear();

            _thinkTreeTimeThisTick = 0f;
            _recentThinkTreeTimes.Clear();
            _avgThinkTreeTime = 0f;
            _avgOtherTime = 0f;

            _pawnsProcessedHistory.Clear();

            _rangedWeaponsEvaluated = 0;
            _meleeWeaponsEvaluated = 0;

            _propertyCacheHits = 0;
            _propertyCacheMisses = 0;
            _validationCacheHits = 0;
            _validationCacheMisses = 0;
            _skillCacheHits = 0;
            _skillCacheMisses = 0;
            _eligibilityCacheHits = 0;
            _eligibilityCacheMisses = 0;

            _lastGen0Count = GC.CollectionCount(0);
            _lastGen1Count = GC.CollectionCount(1);
            _lastGCCheckTime = Time.realtimeSinceStartup;
            _gen0PerMinute = 0f;
            _gen1PerMinute = 0f;
            _cacheSizeHistory.Clear();
            _cacheGrowthRate = 0f;

            _sortedFailureReasons.Clear();
            _failureReasonsDirty = true;

        }


        private float GetDynamicUpdateInterval(float currentTime)
        {
            if (currentTime - _windowOpenTime < 5f)
            {
                return STARTUP_SLOW_INTERVAL;
            }

            if (Find.TickManager?.Paused == true)
            {
                return NORMAL_UPDATE_INTERVAL;
            }

            float targetInterval = NORMAL_UPDATE_INTERVAL;

            if (_recentTickTimes.Count > 0)
            {
                float avgTickTime = _recentTickTimesSum / _recentTickTimes.Count;

                TimeSpeed currentSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Normal;
                float expectedTickMs = currentSpeed == TimeSpeed.Normal ? 16.67f :
                                     currentSpeed == TimeSpeed.Fast ? 8.33f :
                                     currentSpeed == TimeSpeed.Superfast ? 5.56f : 4.17f;
                float tickPercent = (avgTickTime / expectedTickMs) * 100f;

                if (tickPercent > 20f)
                {
                    targetInterval = EMERGENCY_UPDATE_INTERVAL;
                }
                else if (tickPercent > 10f)
                {
                    targetInterval = SLOW_UPDATE_INTERVAL;
                }
                else if (tickPercent > 5f)
                {
                    targetInterval = NORMAL_UPDATE_INTERVAL;
                }
                else if (_pawnsPerMinute > 100f)
                {
                    targetInterval = NORMAL_UPDATE_INTERVAL;
                }
                else
                {
                    targetInterval = MIN_UPDATE_INTERVAL;
                }
            }

            if (_pawnsPerMinute > 200f || _jobsPerMinute > 50f)
            {
                targetInterval = Math.Max(targetInterval, SLOW_UPDATE_INTERVAL);
            }

            if (currentTime - _lastIntervalChangeTime > INTERVAL_CHANGE_COOLDOWN)
            {
                if (Math.Abs(targetInterval - _currentUpdateInterval) > 0.1f)
                {
                    _lastIntervalChangeTime = currentTime;
                    _targetUpdateInterval = targetInterval;

                    _currentUpdateInterval = _currentUpdateInterval * 0.5f + targetInterval * 0.5f;
                }
            }

            return _currentUpdateInterval;
        }

        private static string FormatMilliseconds(float ms)
        {
            return $"{ms:F2}ms";
        }

        private static string FormatPercentage(float percent)
        {
            if (percent < 0.01f)
                return "0.00%";
            else if (percent < 1f)
                return $"{percent:F2}%";
            else if (percent < 10f)
                return $"{percent:F1}%";
            else
                return $"{percent:F0}%";
        }
    }
}

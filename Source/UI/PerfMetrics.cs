using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AutoArm
{
    internal static class PerfMetrics
    {
        public static bool WindowOpen { get; set; }

        public static long JobsCreated;
        public static long CacheHits;

        public static long PropertyCacheHits, PropertyCacheMisses;
        public static long ValidationCacheHits, ValidationCacheMisses;
        public static long SkillCacheHits, SkillCacheMisses;
        public static long EligibilityCacheHits, EligibilityCacheMisses;

        public static long WeaponsSearched;
        public static long SearchCount;
        public static int CacheSize;
        public static long TotalPawnsProcessed;

        public static float JobsPerMinute;
        public static float CacheSavesPerMinute;
        public static float PawnsPerMinute;
        public static float ActualTps;

        public static float PeakPawnsPerMinute;
        public static float PeakJobsPerMinute;
        public static int PeakWeaponsSearched;

        public static readonly Dictionary<string, int> FailureReasons = new Dictionary<string, int>();
        public static readonly List<KeyValuePair<string, int>> SortedBlockers = new List<KeyValuePair<string, int>>();
        private static readonly Dictionary<string, int> _blockerScratch = new Dictionary<string, int>();
        private static bool _failureReasonsDirty = true;

        private static readonly Queue<(float time, long count)> _jobsCreatedHistory = new Queue<(float, long)>();
        private static readonly Queue<(float time, long count)> _cacheHitsHistory = new Queue<(float, long)>();
        private static readonly Queue<(int tick, float realTime)> _tpsHistory = new Queue<(int, float)>();
        private static readonly Queue<(float time, int count)> _pawnsProcessedHistory = new Queue<(float, int)>();

        private static int _lastTickTracked = -1;
        private static int _lastMapId = -1;

        private const float ROLLING_WINDOW_SECONDS = 60f;
        private const float MIN_ROLLING_MINUTES = 0.083f;
        private const float PEAK_ELIGIBLE_MINUTES = 0.15f;
        private const float MIN_PAWN_SPAN_SECONDS = 2f;
        private const float PEAK_PAWN_SPAN_SECONDS = 5f;
        private const int TPS_SAMPLE_SIZE = 60;
        private const int TPS_MIN_SAMPLES = 10;
        private const float TPS_MIN_TIME_DELTA = 0.1f;
        private const int MAX_FAILURE_REASON_KEYS = 200;

        public static bool ShouldCollect()
        {
            if (!WindowOpen) return false;
            var tickManager = Find.TickManager;
            if (tickManager == null || tickManager.Paused) return false;
            if (AutoArm.UI.DebugPanel.isGatheringDebugData) return false;
            return true;
        }

        public static void ReportJobCreated() { if (ShouldCollect()) JobsCreated++; }
        public static void ReportCacheHit() { if (ShouldCollect()) CacheHits++; }
        public static void ReportPropertyCacheHit() { if (ShouldCollect()) PropertyCacheHits++; }
        public static void ReportPropertyCacheMiss() { if (ShouldCollect()) PropertyCacheMisses++; }
        public static void ReportValidationCacheHit() { if (ShouldCollect()) ValidationCacheHits++; }
        public static void ReportValidationCacheMiss() { if (ShouldCollect()) ValidationCacheMisses++; }
        public static void ReportSkillCacheHit() { if (ShouldCollect()) SkillCacheHits++; }
        public static void ReportSkillCacheMiss() { if (ShouldCollect()) SkillCacheMisses++; }
        public static void ReportEligibilityCacheHit() { if (ShouldCollect()) EligibilityCacheHits++; }
        public static void ReportEligibilityCacheMiss() { if (ShouldCollect()) EligibilityCacheMisses++; }

        public static void ReportTickProcessing(int pawnsProcessed)
        {
            if (!ShouldCollect()) return;
            TotalPawnsProcessed += pawnsProcessed;
            float now = Time.realtimeSinceStartup;
            _pawnsProcessedHistory.Enqueue((now, pawnsProcessed));
            while (_pawnsProcessedHistory.Count > 0 && now - _pawnsProcessedHistory.Peek().time > ROLLING_WINDOW_SECONDS)
                _pawnsProcessedHistory.Dequeue();
        }

        public static void ReportFailureReason(string reason)
        {
            if (!ShouldCollect() || string.IsNullOrEmpty(reason)) return;
            if (!FailureReasons.ContainsKey(reason))
            {
                if (FailureReasons.Count >= MAX_FAILURE_REASON_KEYS) return;
                FailureReasons[reason] = 0;
            }
            FailureReasons[reason]++;
            _failureReasonsDirty = true;
        }

        public static void ReportSearchStats(int weaponsSearchedDelta)
        {
            if (!ShouldCollect()) return;
            WeaponsSearched += weaponsSearchedDelta;
            SearchCount++;
        }

        public static void ReportCacheStats(int size)
        {
            if (ShouldCollect()) CacheSize = size;
        }

        public static void StartTiming()
        {
            if (WindowOpen) UnityEngine.Profiling.Profiler.BeginSample("AutoArm.JobGiver");
        }

        public static void EndTiming()
        {
            if (WindowOpen) UnityEngine.Profiling.Profiler.EndSample();
        }

        public static void UpdateTps()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (_lastTickTracked < 0 || currentTick == _lastTickTracked)
            {
                _lastTickTracked = currentTick;
                return;
            }

            float realTime = Time.realtimeSinceStartup;
            _tpsHistory.Enqueue((currentTick, realTime));
            while (_tpsHistory.Count > TPS_SAMPLE_SIZE) _tpsHistory.Dequeue();

            if (_tpsHistory.Count >= TPS_MIN_SAMPLES)
            {
                var oldest = _tpsHistory.Peek();
                int tickDelta = currentTick - oldest.tick;
                float timeDelta = realTime - oldest.realTime;
                if (timeDelta > TPS_MIN_TIME_DELTA)
                    ActualTps = tickDelta / timeDelta;
            }
            _lastTickTracked = currentTick;
        }

        public static void CheckMapChange()
        {
            int currentMapId = Find.CurrentMap?.uniqueID ?? -1;
            if (_lastMapId != -1 && currentMapId != _lastMapId)
            {
                ResetCounters();
                ResetPeakValues();
            }
            _lastMapId = currentMapId;
        }

        public static void UpdateRollingMetrics(bool pastWarmup)
        {
            var tm = Find.TickManager;
            if (tm == null || tm.CurTimeSpeed == TimeSpeed.Paused) return;

            float now = Time.realtimeSinceStartup;

            _jobsCreatedHistory.Enqueue((now, JobsCreated));
            while (_jobsCreatedHistory.Count > 0 && now - _jobsCreatedHistory.Peek().time > ROLLING_WINDOW_SECONDS)
                _jobsCreatedHistory.Dequeue();
            if (_jobsCreatedHistory.Count > 1)
            {
                var oldest = _jobsCreatedHistory.Peek();
                float minutes = (now - oldest.time) / 60f;
                if (minutes > MIN_ROLLING_MINUTES)
                {
                    JobsPerMinute = (JobsCreated - oldest.count) / minutes;
                    if (pastWarmup && minutes > PEAK_ELIGIBLE_MINUTES && JobsPerMinute > PeakJobsPerMinute)
                        PeakJobsPerMinute = JobsPerMinute;
                }
            }

            _cacheHitsHistory.Enqueue((now, CacheHits));
            while (_cacheHitsHistory.Count > 0 && now - _cacheHitsHistory.Peek().time > ROLLING_WINDOW_SECONDS)
                _cacheHitsHistory.Dequeue();
            if (_cacheHitsHistory.Count > 1)
            {
                var oldest = _cacheHitsHistory.Peek();
                float minutes = (now - oldest.time) / 60f;
                if (minutes > MIN_ROLLING_MINUTES)
                    CacheSavesPerMinute = (CacheHits - oldest.count) / minutes;
            }

            if (_pawnsProcessedHistory.Count > 0)
            {
                float total = 0;
                foreach (var e in _pawnsProcessedHistory) total += e.count;
                float span = now - _pawnsProcessedHistory.Peek().time;
                if (span > MIN_PAWN_SPAN_SECONDS)
                {
                    PawnsPerMinute = total / span * 60f;
                    if (pastWarmup && span > PEAK_PAWN_SPAN_SECONDS && PawnsPerMinute > PeakPawnsPerMinute)
                        PeakPawnsPerMinute = PawnsPerMinute;
                }
            }

            if (pastWarmup && SearchCount > 0)
            {
                float avg = (float)WeaponsSearched / SearchCount;
                if (avg > PeakWeaponsSearched) PeakWeaponsSearched = (int)avg;
            }

            if (_failureReasonsDirty)
            {
                SortedBlockers.Clear();
                _blockerScratch.Clear();

                foreach (var kvp in FailureReasons)
                {
                    string bucket = AutoArm.UI.BlockerClassifier.Classify(kvp.Key);
                    if (!_blockerScratch.ContainsKey(bucket))
                        _blockerScratch[bucket] = 0;
                    _blockerScratch[bucket] += kvp.Value;
                }

                SortedBlockers.AddRange(_blockerScratch);
                SortedBlockers.Sort((a, b) => b.Value.CompareTo(a.Value));
                _failureReasonsDirty = false;
            }

            if (Find.CurrentMap != null)
                CacheSize = Caching.WeaponCache.GetCacheWeaponCount(Find.CurrentMap);
        }

        public static void ResetCounters()
        {
            JobsCreated = 0;
            CacheHits = 0;
            FailureReasons.Clear();
            SortedBlockers.Clear();
            _blockerScratch.Clear();
            _failureReasonsDirty = true;

            WeaponsSearched = 0;
            SearchCount = 0;
            TotalPawnsProcessed = 0;

            JobsPerMinute = 0;
            CacheSavesPerMinute = 0;
            PawnsPerMinute = 0;
            ActualTps = 0;

            _jobsCreatedHistory.Clear();
            _cacheHitsHistory.Clear();
            _pawnsProcessedHistory.Clear();
            _tpsHistory.Clear();
            _lastTickTracked = -1;

            PropertyCacheHits = PropertyCacheMisses = 0;
            ValidationCacheHits = ValidationCacheMisses = 0;
            SkillCacheHits = SkillCacheMisses = 0;
            EligibilityCacheHits = EligibilityCacheMisses = 0;
        }

        public static void ResetPeakValues()
        {
            PeakPawnsPerMinute = 0;
            PeakJobsPerMinute = 0;
            PeakWeaponsSearched = 0;
        }
    }
}

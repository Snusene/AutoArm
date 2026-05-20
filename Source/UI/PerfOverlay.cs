using System;
using System.Linq;
using AutoArm.UI;
using UnityEngine;
using Verse;

namespace AutoArm
{
    public sealed class PerfOverlay : Window
    {
        private const float WINDOW_WIDTH = 320f;
        private const float MIN_HEIGHT = 200f;
        private const float MAX_HEIGHT = 700f;
        private const float HEIGHT_CHANGE_THRESHOLD = 5f;

        private const float ROW_HEIGHT = 22f;
        private const float HEADER_HEIGHT = 24f;
        private const float SECTION_GAP = 6f;
        private const float CHROME_HEIGHT = 50f;

        private const int ACTIVITY_ROWS = 3;
        private const int CACHE_ROWS = 7;

        private const float STARTUP_DELAY = 0.5f;
        private const float MIN_UPDATE_INTERVAL = 0.25f;
        private const float NORMAL_UPDATE_INTERVAL = 0.5f;
        private const float SLOW_UPDATE_INTERVAL = 1.0f;
        private const float STARTUP_SLOW_INTERVAL = 1.0f;
        private const float INTERVAL_CHANGE_COOLDOWN = 2.0f;
        private const float STARTUP_GRACE_SECONDS = 5f;

        private const float DYNAMIC_PAWNS_NORMAL = 100f;
        private const float DYNAMIC_PAWNS_SLOW = 200f;
        private const float DYNAMIC_JOBS_SLOW = 50f;

        private const float HIT_RATE_GREEN_PROPERTY = 95f;
        private const float HIT_RATE_GREEN_VALIDATION = 85f;
        private const float HIT_RATE_GREEN_SKILL = 90f;
        private const float HIT_RATE_GREEN_ELIGIBILITY = 80f;

        private const float AVG_SEARCHED_GREEN = 50f;
        private const float CACHE_SAVES_GREEN = 1000f;
        private const float CACHE_SAVES_YELLOW = 100f;
        private const float JOBS_PER_PAWN_GREEN = 0.1f;
        private const float JOBS_PER_PAWN_YELLOW = 0.05f;
        private const float TPS_GREEN_RATIO = 0.95f;

        private static readonly Color RaidColor = new Color(1f, 0.75f, 0.3f);

        private float _dynamicHeight = 520f;
        private float _lastUpdateTime;
        private float _windowOpenTime;
        private float _currentUpdateInterval = NORMAL_UPDATE_INTERVAL;
        private float _lastIntervalChangeTime;
        private TimeSpeed _lastTimeSpeed = TimeSpeed.Paused;

        public override Vector2 InitialSize => new Vector2(WINDOW_WIDTH, _dynamicHeight);

        public static bool IsWindowOpen() => PerfMetrics.WindowOpen;

        public static void OpenOrBringToFront()
        {
            if (Find.WindowStack == null) return;
            var existing = Find.WindowStack.Windows.OfType<PerfOverlay>().FirstOrDefault();
            if (existing != null)
            {
                Find.WindowStack.TryRemove(existing, doCloseSound: false);
                Find.WindowStack.Add(existing);
            }
            else
            {
                Find.WindowStack.Add(new PerfOverlay());
            }
        }

        public PerfOverlay()
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
            PerfMetrics.WindowOpen = true;
            _windowOpenTime = Time.realtimeSinceStartup;
            _lastTimeSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Paused;
            PerfMetrics.ResetCounters();
            PerfMetrics.ResetPeakValues();
        }

        public override void PostClose()
        {
            base.PostClose();
            PerfMetrics.WindowOpen = false;
            PerfMetrics.ResetCounters();
            PerfMetrics.ResetPeakValues();
        }

        protected override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            windowRect = new Rect(12f, 120f, size.x, size.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                float currentTime = Time.realtimeSinceStartup;
                bool pastStartupDelay = (currentTime - _windowOpenTime) >= STARTUP_DELAY;

                PerfMetrics.UpdateTps();
                PerfMetrics.CheckMapChange();
                HandleSpeedChange();

                float interval = GetDynamicUpdateInterval(currentTime);
                if (pastStartupDelay && (currentTime - _lastUpdateTime >= interval))
                {
                    PerfMetrics.UpdateRollingMetrics(pastWarmup: pastStartupDelay);
                    _lastUpdateTime = currentTime;
                }

                int blockerCount = PerfMetrics.SortedBlockers.Count;
                float targetHeight = ComputeTargetHeight(blockerCount);
                if (Math.Abs(targetHeight - _dynamicHeight) > HEIGHT_CHANGE_THRESHOLD)
                {
                    _dynamicHeight = Mathf.Clamp(targetHeight, MIN_HEIGHT, MAX_HEIGHT);
                    windowRect.height = _dynamicHeight;
                }

                var listing = new Listing_Standard();
                listing.Begin(inRect);

                DrawStatusRow(listing);
                listing.Gap(SECTION_GAP);

                DrawSectionHeader(listing, "ACTIVITY", UIColors.AccentHeader);
                DrawActivity(listing);
                listing.Gap(SECTION_GAP);

                DrawSectionHeader(listing, "CACHE", UIColors.AccentHeader);
                DrawCache(listing);

                if (blockerCount > 0)
                {
                    listing.Gap(SECTION_GAP);
                    DrawSectionHeader(listing, "BLOCKERS", UIColors.AccentHeader);
                    foreach (var r in PerfMetrics.SortedBlockers)
                        LabelPair(listing, r.Key, r.Value.ToString(), UIColors.Dim);
                }

                listing.End();
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorUI(ex, "PerfOverlay", "DoWindowContents");
                Widgets.Label(inRect, "AutoArm_PerfOverlayError".Translate());
            }
        }

        private void HandleSpeedChange()
        {
            var tm = Find.TickManager;
            if (tm == null) return;
            var current = tm.CurTimeSpeed;
            if (current == _lastTimeSpeed) return;

            bool involvesPause = current == TimeSpeed.Paused || _lastTimeSpeed == TimeSpeed.Paused;
            if (!involvesPause)
            {
                PerfMetrics.ResetCounters();
            }
            _lastTimeSpeed = current;
        }

        private float ComputeTargetHeight(int blockerCount)
        {
            float h = ROW_HEIGHT;
            h += SECTION_GAP + HEADER_HEIGHT + ACTIVITY_ROWS * ROW_HEIGHT;
            h += SECTION_GAP + HEADER_HEIGHT + CACHE_ROWS * ROW_HEIGHT;
            if (blockerCount > 0)
                h += SECTION_GAP + HEADER_HEIGHT + blockerCount * ROW_HEIGHT;
            return h + CHROME_HEIGHT;
        }

        private void DrawStatusRow(Listing_Standard listing)
        {
            var tm = Find.TickManager;
            bool raidActive = ModInit.IsLargeRaidActive;
            bool disableDuringRaids = AutoArmMod.settings?.disableDuringRaids == true;

            string status;
            Color color;
            if (raidActive && disableDuringRaids) { status = "RAID (AutoArm off)"; color = UIColors.Fail; }
            else if (raidActive) { status = "RAID"; color = RaidColor; }
            else if (tm?.Paused ?? true) { status = "PAUSED"; color = UIColors.Dim; }
            else { status = FormatSpeedWithTps(tm); color = TpsColor(tm); }

            LabelPair(listing, "Status", status, color);
        }

        private string FormatSpeedWithTps(TickManager tm)
        {
            string speed = SpeedLabel(tm.CurTimeSpeed);
            if (PerfMetrics.ActualTps <= 0) return speed;
            float target = tm.TickRateMultiplier * 60f;
            return $"{speed} ({PerfMetrics.ActualTps:F0}/{target:F0} TPS)";
        }

        private Color TpsColor(TickManager tm)
        {
            if (PerfMetrics.ActualTps <= 0) return Color.white;
            float target = tm.TickRateMultiplier * 60f;
            return PerfMetrics.ActualTps >= target * TPS_GREEN_RATIO ? UIColors.Pass : UIColors.Skip;
        }

        private string SpeedLabel(TimeSpeed s)
        {
            switch (s)
            {
                case TimeSpeed.Paused: return "PAUSED";
                case TimeSpeed.Normal: return "1x";
                case TimeSpeed.Fast: return "2x";
                case TimeSpeed.Superfast: return "3x";
                default: return "4x";
            }
        }

        private void DrawActivity(Listing_Standard listing)
        {
            DrawRateRow(listing, "Pawns/min", PerfMetrics.PawnsPerMinute, PerfMetrics.PeakPawnsPerMinute, "N0");
            DrawRateRow(listing, "Jobs/min", PerfMetrics.JobsPerMinute, PerfMetrics.PeakJobsPerMinute, "N1");

            float jobsPerPawn = PerfMetrics.TotalPawnsProcessed > 0
                ? (float)PerfMetrics.JobsCreated / PerfMetrics.TotalPawnsProcessed
                : 0f;
            Color jobsPerPawnColor;
            if (PerfMetrics.TotalPawnsProcessed == 0) jobsPerPawnColor = UIColors.Dim;
            else if (jobsPerPawn > JOBS_PER_PAWN_GREEN) jobsPerPawnColor = UIColors.Pass;
            else if (jobsPerPawn > JOBS_PER_PAWN_YELLOW) jobsPerPawnColor = UIColors.Skip;
            else jobsPerPawnColor = UIColors.Dim;
            LabelPair(listing, "Jobs/pawn", $"{jobsPerPawn:F2}", jobsPerPawnColor);
        }

        private void DrawRateRow(Listing_Standard listing, string label, float value, float peak, string format)
        {
            string text = value.ToString(format);
            string suffix = peak > value ? $"(pk:{peak.ToString(format)})" : null;
            LabelPair(listing, label, text, value > 0 ? UIColors.Pass : UIColors.Dim, dimSuffix: suffix);
        }

        private void DrawCache(Listing_Standard listing)
        {
            LabelPair(listing, "Weapons cached", PerfMetrics.CacheSize.ToString(),
                PerfMetrics.CacheSize > 0 ? UIColors.Pass : UIColors.Dim);

            Color savesColor;
            if (PerfMetrics.CacheSavesPerMinute > CACHE_SAVES_GREEN) savesColor = UIColors.Pass;
            else if (PerfMetrics.CacheSavesPerMinute > CACHE_SAVES_YELLOW) savesColor = UIColors.Skip;
            else if (PerfMetrics.CacheSavesPerMinute > 0) savesColor = UIColors.LabelMuted;
            else savesColor = UIColors.Dim;
            LabelPair(listing, "Cache saves/min", PerfMetrics.CacheSavesPerMinute.ToString("N0"), savesColor);

            if (PerfMetrics.SearchCount > 0)
            {
                float avg = (float)PerfMetrics.WeaponsSearched / PerfMetrics.SearchCount;
                string text = avg.ToString("N0");
                string suffix = PerfMetrics.PeakWeaponsSearched > avg
                    ? $"(pk:{PerfMetrics.PeakWeaponsSearched.ToString("N0")})"
                    : null;
                LabelPair(listing, "Avg searched", text, avg < AVG_SEARCHED_GREEN ? UIColors.Pass : UIColors.Skip, dimSuffix: suffix);
            }
            else LabelPair(listing, "Avg searched", "0", UIColors.Dim);

            DrawHitRateRow(listing, "Property", PerfMetrics.PropertyCacheHits, PerfMetrics.PropertyCacheMisses, HIT_RATE_GREEN_PROPERTY);
            DrawHitRateRow(listing, "Validation", PerfMetrics.ValidationCacheHits, PerfMetrics.ValidationCacheMisses, HIT_RATE_GREEN_VALIDATION);
            DrawHitRateRow(listing, "Skill", PerfMetrics.SkillCacheHits, PerfMetrics.SkillCacheMisses, HIT_RATE_GREEN_SKILL);
            DrawHitRateRow(listing, "Eligibility", PerfMetrics.EligibilityCacheHits, PerfMetrics.EligibilityCacheMisses, HIT_RATE_GREEN_ELIGIBILITY);
        }

        private void DrawHitRateRow(Listing_Standard listing, string label, long hits, long misses, float greenThreshold)
        {
            long total = hits + misses;
            if (total == 0)
            {
                LabelPair(listing, label, "0% (0 hits)", UIColors.Dim);
                return;
            }
            float rate = hits * 100f / total;
            Color color;
            if (rate > greenThreshold) color = UIColors.Pass;
            else if (rate > 50f) color = UIColors.Skip;
            else color = UIColors.Dim;
            LabelPair(listing, label, $"{rate:F0}%", color, dimSuffix: $"({hits.ToString("N0")} hits)");
        }

        private void DrawSectionHeader(Listing_Standard listing, string label, Color color)
        {
            using (new TextBlock(color))
                listing.Label(label);
        }

        private void LabelPair(Listing_Standard listing, string label, string value, Color? valueColor = null, string dimSuffix = null, float labelWidthPct = 0.45f)
        {
            var rect = listing.GetRect(20f);
            var labelRect = new Rect(rect.x, rect.y, rect.width * labelWidthPct, rect.height);
            var valueRect = new Rect(rect.x + rect.width * labelWidthPct, rect.y, rect.width * (1f - labelWidthPct), rect.height);
            using (new TextBlock(UIColors.LabelMuted))
                Widgets.Label(labelRect, label);
            using (new TextBlock(valueColor ?? GUI.color))
                Widgets.Label(valueRect, value);
            if (!string.IsNullOrEmpty(dimSuffix))
            {
                float valueWidth = Text.CalcSize(value).x;
                var suffixRect = new Rect(valueRect.x + valueWidth + 4f, rect.y, valueRect.width - valueWidth - 4f, rect.height);
                using (new TextBlock(UIColors.Dim))
                    Widgets.Label(suffixRect, dimSuffix);
            }
        }

        private float GetDynamicUpdateInterval(float currentTime)
        {
            if (currentTime - _windowOpenTime < STARTUP_GRACE_SECONDS) return STARTUP_SLOW_INTERVAL;
            if (Find.TickManager?.Paused == true) return NORMAL_UPDATE_INTERVAL;

            float target = PerfMetrics.PawnsPerMinute > DYNAMIC_PAWNS_NORMAL
                ? NORMAL_UPDATE_INTERVAL : MIN_UPDATE_INTERVAL;
            if (PerfMetrics.PawnsPerMinute > DYNAMIC_PAWNS_SLOW || PerfMetrics.JobsPerMinute > DYNAMIC_JOBS_SLOW)
                target = Math.Max(target, SLOW_UPDATE_INTERVAL);

            if (currentTime - _lastIntervalChangeTime > INTERVAL_CHANGE_COOLDOWN &&
                Math.Abs(target - _currentUpdateInterval) > 0.1f)
            {
                _lastIntervalChangeTime = currentTime;
                _currentUpdateInterval = _currentUpdateInterval * 0.5f + target * 0.5f;
            }
            return _currentUpdateInterval;
        }
    }
}

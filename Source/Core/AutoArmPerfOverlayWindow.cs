// Source/Core/AutoArmPerfOverlayWindow.cs
// Drop-in debug overlay + lightweight telemetry for AutoArm.
// Hook from your Debug button: AutoArmPerfOverlayWindow.OpenOrBringToFront();

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;            // <-- Needed for Job

// Use your real namespaces/types, do not shadow them.
using AutoArm.Jobs;       // JobGiverHelpers
using AutoArm.Caching;    // WeaponScoreCache
using AutoArm.Weapons;    // WeaponScoringHelper, WeaponBlacklist

namespace AutoArm
{
    public class AutoArmPerfOverlayWindow : Window
    {
        private static AutoArmPerfOverlayWindow _instance;

        public static void OpenOrBringToFront()
        {
            if (_instance == null)
            {
                _instance = new AutoArmPerfOverlayWindow();
                Find.WindowStack.Add(_instance);
            }
            else
            {
                _instance.forcePauseSampling = false;
                _instance.BringToFront();
            }
        }

        private void BringToFront()
        {
            // Re-add to top of stack
            Find.WindowStack.TryRemove(this, doCloseSound: false);
            Find.WindowStack.Add(this);
        }

        public override Vector2 InitialSize => new Vector2(380f, 520f);

        public AutoArmPerfOverlayWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = false;
            preventCameraMotion = false;
            onlyOneOfTypeAllowed = false;

            AutoArmPerfCounters.InstallPatches();
            AutoArmPerfCounters.Enabled = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            windowRect = new Rect(12f, 120f, size.x, size.y);
        }

        private bool forcePauseSampling;
        private int sampleIntervalTicks = 60; // ~1s
        private int lastSampleTick = -99999;

        private float emaFps;
        private const float EmaAlpha = 0.15f;

        private AutoArmPerfCounters.Snapshot snap = AutoArmPerfCounters.Snapshot.Empty;

        private int colonistsTotal, colonistsDrafted, colonistsValid;
        private int weaponsTotal, weaponsGround, weaponsEquipped, weaponsInventory;
        private bool ssLoaded, ceLoaded;
        private double memoryMB;

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeight), "AutoArm - Performance Overlay");
            Text.Font = GameFont.Small;

            var listing = new Listing_Standard();
            listing.Begin(new Rect(inRect.x, inRect.y + Text.LineHeight + 6f, inRect.width, inRect.height - (Text.LineHeight + 6f)));

            listing.CheckboxLabeled("Pause sampling", ref forcePauseSampling, "Stops updating live metrics.");
            DrawIntSlider(listing, "Sampling interval (ticks)", ref sampleIntervalTicks, 30, 600, "Lower = more real-time; higher = less overhead.");
            listing.GapLine();

            TrySample();

            SectionHeader(listing, "Runtime");
            LabelPair(listing, "FPS (smoothed)", emaFps > 0 ? emaFps.ToString("F1") : "n/a", "Frames per second (Unity smooth delta time).");
            LabelPair(listing, "Game ticks", Find.TickManager.TicksGame.ToString("N0"), "Total game ticks.");
            LabelPair(listing, "Managed memory", $"{memoryMB:F2} MB", "GC-reported managed memory.");
            listing.GapLine();

            SectionHeader(listing, "Scoring & Cache (last window)");
            LabelPair(listing, "GetCachedScore calls", snap.GetCachedScoreCalls.ToString("N0"), "How often cache was queried.");
            LabelPair(listing, "Cache misses", snap.CacheMisses.ToString("N0"), "Miss = CalculateWeaponScore executed.");
            var hits = Math.Max(0, snap.GetCachedScoreCalls - snap.CacheMisses);
            var hitRate = snap.GetCachedScoreCalls > 0 ? (100.0 * hits / snap.GetCachedScoreCalls) : 0.0;
            LabelPair(listing, "Cache hits", $"{hits:N0}  ({hitRate:F1}%)", "Hit = served from cache.");
            LabelPair(listing, "Score calls (GetTotalScore)", snap.ScoreCalls.ToString("N0"), "Total scoring invocations.");
            LabelPair(listing, "Avg score time (µs)", snap.AvgScoreUs > 0 ? snap.AvgScoreUs.ToString("F1") : "n/a", "Mean microseconds per score.");
            LabelPair(listing, "Active score calcs (peak)", snap.ActiveScoreCalcsPeak.ToString("N0"), "Max concurrent scoring observed.");
            listing.GapLine();

            SectionHeader(listing, "Flow (last window)");
            LabelPair(listing, "Jobs per tick (score-calcs)", snap.ScoreCalcsPerTick.ToString("F2"), "Calculate/score work density.");
            LabelPair(listing, "Equip jobs tried", snap.EquipTryCalls.ToString("N0"), "JobGiver_PickUpBetterWeapon.TryGiveJob calls.");
            LabelPair(listing, "Equip jobs given", snap.EquipJobsGiven.ToString("N0"), "Returned a Job.");
            listing.GapLine();

            SectionHeader(listing, "Maintenance (last window)");
            LabelPair(listing, "Blacklist adds", snap.BlacklistAdds.ToString("N0"), "Weapons blacklisted due to restrictions.");
            LabelPair(listing, "Cache entries cleaned", snap.CacheCleaned.ToString("N0"), "Removed by WeaponScoreCache.CleanupCache.");
            listing.GapLine();

            SectionHeader(listing, "Compatibility");
            LabelPair(listing, "Simple Sidearms", ssLoaded ? "Loaded" : "Not found", "Detected once per sample.");
            LabelPair(listing, "Combat Extended", ceLoaded ? "Loaded" : "Not found", "Detected once per sample.");

            listing.End();
        }

        private void TrySample()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            float instFps = 1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f);
            emaFps = emaFps <= 0f ? instFps : Mathf.Lerp(emaFps, instFps, EmaAlpha);

            if (forcePauseSampling) return;

            int ticks = Find.TickManager.TicksGame;
            if (ticks - lastSampleTick < sampleIntervalTicks) return;
            int elapsedTicks = Math.Max(1, ticks - lastSampleTick);
            lastSampleTick = ticks;

            snap = AutoArmPerfCounters.Sample(elapsedTicks);

            memoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            var colonists = map.mapPawns?.FreeColonists?.ToList() ?? new List<Pawn>();
            colonistsTotal = colonists.Count;
            colonistsDrafted = colonists.Count(p => p.Drafted);
            colonistsValid = 0;
            foreach (var p in colonists)
            {
                string reason;
                bool valid = true;
                try { valid = JobGiverHelpers.IsValidPawnForAutoEquip(p, out reason); }
                catch { valid = true; }
                if (valid) colonistsValid++;
            }

            var allWeapons = map.listerThings?.ThingsInGroup(ThingRequestGroup.Weapon)?.OfType<ThingWithComps>()?.ToList()
                             ?? new List<ThingWithComps>();
            weaponsTotal = allWeapons.Count;
            weaponsGround = allWeapons.Count(w => w.Spawned);
            weaponsEquipped = allWeapons.Count(w => w.ParentHolder is Pawn_EquipmentTracker);
            weaponsInventory = allWeapons.Count(w => w.ParentHolder is Pawn_InventoryTracker);

            try { ssLoaded = SimpleSidearmsCompat.IsLoaded(); } catch { ssLoaded = false; }
            try { ceLoaded = CECompat.IsLoaded(); } catch { ceLoaded = false; }
        }

        private void SectionHeader(Listing_Standard listing, string label)
        {
            Text.Font = GameFont.Medium;
            var r = listing.GetRect(Text.LineHeight);
            Widgets.Label(r, label);
            Text.Font = GameFont.Small;
        }

        private void LabelPair(Listing_Standard listing, string left, string right, string tooltip = null)
        {
            Rect row = listing.GetRect(Text.LineHeight);
            float split = row.width * 0.56f;
            Rect l = new Rect(row.x, row.y, split - 6f, row.height);
            Rect r = new Rect(row.x + split, row.y, row.width - split, row.height);
            Widgets.Label(l, left);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(r, right);
            Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tooltip)) TooltipHandler.TipRegion(row, tooltip);
        }

        private void DrawIntSlider(Listing_Standard listing, string label, ref int value, int min, int max, string tooltip = null)
        {
            Widgets.Label(listing.GetRect(Text.LineHeight), label);
            Rect row = listing.GetRect(Text.LineHeight);
            float newVal = Widgets.HorizontalSlider(row, value, min, max, true);
            value = Mathf.Clamp(Mathf.RoundToInt(newVal), min, max);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(row, $"{value} ticks");
            Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(new Rect(row.x, row.y - Text.LineHeight, row.width, row.height + Text.LineHeight), tooltip);
        }

        public override void PostClose()
        {
            base.PostClose();
            _instance = null;
            AutoArmPerfCounters.Enabled = false;
        }
    }

    [StaticConstructorOnStartup]
    public static class AutoArmPerfCounters
    {
        public static volatile bool Enabled;
        private static bool _patched;

        private static long _getCachedScoreCalls;
        private static long _cacheMisses;           // CalculateWeaponScore calls
        private static long _scoreCalls;            // GetTotalScore calls
        private static long _scoreElapsedTicks;     // sum Stopwatch ticks for GetTotalScore
        private static int _activeScoreCalcs;
        private static int _activeScoreCalcsPeak;

        private static long _equipTryCalls;         // TryGiveJob calls
        private static long _equipJobsGiven;        // returned Job != null

        private static long _blacklistAdds;         // WeaponBlacklist.AddToBlacklist
        private static long _cacheCleaned;          // sum of CleanupCache() returns

        private static int _lastTick;
        private static long _scoreCalcsThisTick;
        private static readonly object _tickLock = new object();

        public static void InstallPatches()
        {
            if (_patched) return;
            _patched = true;

            var h = new Harmony("AutoArm.PerfOverlay.Telemetry");

            // Cache layer
            var tCache = typeof(WeaponScoreCache);
            var mGetCached = AccessTools.Method(tCache, "GetCachedScore", new Type[] { typeof(Pawn), typeof(ThingWithComps) });
            if (mGetCached != null) h.Patch(mGetCached, prefix: new HarmonyMethod(typeof(AutoArmPerfCounters), nameof(Prefix_GetCachedScore)));

            var mCalc = AccessTools.Method(tCache, "CalculateWeaponScore", new Type[] { typeof(Pawn), typeof(ThingWithComps) });
            if (mCalc != null) h.Patch(mCalc, prefix: new HarmonyMethod(typeof(AutoArmPerfCounters), nameof(Prefix_CalculateWeaponScore)));

            var mCleanup = AccessTools.Method(tCache, "CleanupCache", Type.EmptyTypes);
            if (mCleanup != null) h.Patch(mCleanup, postfix: new HarmonyMethod(typeof(AutoArmPerfCounters), nameof(Postfix_CleanupCache)));

            // Scoring
            var tScore = typeof(WeaponScoringHelper);
            var mScore = AccessTools.Method(tScore, "GetTotalScore", new Type[] { typeof(Pawn), typeof(ThingWithComps) });
            if (mScore != null)
            {
                h.Patch(mScore,
                    prefix: new HarmonyMethod(typeof(AutoArmPerfCounters), nameof(Prefix_GetTotalScore)),
                    postfix: new HarmonyMethod(typeof(AutoArmPerfCounters), nameof(Postfix_GetTotalScore)));
            }

            // Equip flow
            var tJG = typeof(JobGiver_PickUpBetterWeapon);
            var mTry = AccessTools.Method(tJG, "TryGiveJob", new Type[] { typeof(Pawn) });
            if (mTry != null) h.Patch(mTry, postfix: new HarmonyMethod(typeof(AutoArmPerfCounters), nameof(Postfix_TryGiveJob)));

            // Blacklist
            var tBL = typeof(WeaponBlacklist);
            var mAdd = AccessTools.Method(tBL, "AddToBlacklist", new Type[] { typeof(ThingDef), typeof(Pawn), typeof(string) });
            if (mAdd != null) h.Patch(mAdd, postfix: new HarmonyMethod(typeof(AutoArmPerfCounters), nameof(Postfix_AddToBlacklist)));
        }

        public static void Prefix_GetCachedScore(Pawn pawn, ThingWithComps weapon)
        {
            if (!Enabled) return;
            Interlocked.Increment(ref _getCachedScoreCalls);
        }

        public static void Prefix_CalculateWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (!Enabled) return;
            Interlocked.Increment(ref _cacheMisses);
            lock (_tickLock)
            {
                int t = Find.TickManager.TicksGame;
                if (t != _lastTick) { _lastTick = t; _scoreCalcsThisTick = 0; }
                _scoreCalcsThisTick++;
            }
        }

        public struct ScoreTimer
        { public long startTicks; } // public for Harmony accessibility

        public static void Prefix_GetTotalScore(Pawn pawn, ThingWithComps weapon, ref ScoreTimer __state)
        {
            if (!Enabled) return;
            __state.startTicks = Stopwatch.GetTimestamp();
            int now = Interlocked.Increment(ref _activeScoreCalcs);
            int curPeak = _activeScoreCalcsPeak;
            while (now > curPeak)
            {
                int prev = Interlocked.CompareExchange(ref _activeScoreCalcsPeak, now, curPeak);
                if (prev == curPeak) break;
                curPeak = prev;
            }
            Interlocked.Increment(ref _scoreCalls);
        }

        public static void Postfix_GetTotalScore(Pawn pawn, ThingWithComps weapon, ref ScoreTimer __state)
        {
            if (!Enabled) return;
            long end = Stopwatch.GetTimestamp();
            Interlocked.Add(ref _scoreElapsedTicks, end - __state.startTicks);
            Interlocked.Decrement(ref _activeScoreCalcs);
        }

        public static void Postfix_TryGiveJob(Pawn pawn, ref Job __result)
        {
            if (!Enabled) return;
            Interlocked.Increment(ref _equipTryCalls);
            if (__result != null) Interlocked.Increment(ref _equipJobsGiven);
        }

        public static void Postfix_AddToBlacklist(ThingDef weaponDef, Pawn pawn, string reason)
        {
            if (!Enabled) return;
            Interlocked.Increment(ref _blacklistAdds);
        }

        public static void Postfix_CleanupCache(ref int __result)
        {
            if (!Enabled) return;
            Interlocked.Add(ref _cacheCleaned, Math.Max(0, __result));
        }

        public struct Snapshot
        {
            public long GetCachedScoreCalls;
            public long CacheMisses;
            public long ScoreCalls;
            public float AvgScoreUs;
            public int ActiveScoreCalcsPeak;
            public double ScoreCalcsPerTick;
            public long EquipTryCalls;
            public long EquipJobsGiven;
            public long BlacklistAdds;
            public long CacheCleaned;

            public static readonly Snapshot Empty = new Snapshot();
        }

        private static double _ticksToUs = 1_000_000.0 / Stopwatch.Frequency;

        private static long p_getCached, p_miss, p_score, p_ticks, p_equipTry, p_equipGiven, p_blAdds, p_cleaned;

        public static Snapshot Sample(int elapsedTicks)
        {
            int peak = Interlocked.Exchange(ref _activeScoreCalcsPeak, 0);

            long getCached = Interlocked.Read(ref _getCachedScoreCalls);
            long miss = Interlocked.Read(ref _cacheMisses);
            long score = Interlocked.Read(ref _scoreCalls);
            long elapsed = Interlocked.Read(ref _scoreElapsedTicks);
            long equipTry = Interlocked.Read(ref _equipTryCalls);
            long equipGive = Interlocked.Read(ref _equipJobsGiven);
            long blAdds = Interlocked.Read(ref _blacklistAdds);
            long cleaned = Interlocked.Read(ref _cacheCleaned);

            long d_getCached = getCached - p_getCached;
            long d_miss = miss - p_miss;
            long d_score = score - p_score;
            long d_elapsed = elapsed - p_ticks;
            long d_try = equipTry - p_equipTry;
            long d_give = equipGive - p_equipGiven;
            long d_blAdds = blAdds - p_blAdds;
            long d_cleaned = cleaned - p_cleaned;

            p_getCached = getCached;
            p_miss = miss;
            p_score = score;
            p_ticks = elapsed;
            p_equipTry = equipTry;
            p_equipGiven = equipGive;
            p_blAdds = blAdds;
            p_cleaned = cleaned;

            float avgUs = d_score > 0 ? (float)(d_elapsed * _ticksToUs / d_score) : 0f;

            double perTickDensity;
            lock (_tickLock)
            {
                perTickDensity = elapsedTicks > 0 ? (double)d_miss / elapsedTicks : 0.0;
            }

            return new Snapshot
            {
                GetCachedScoreCalls = Math.Max(0, d_getCached),
                CacheMisses = Math.Max(0, d_miss),
                ScoreCalls = Math.Max(0, d_score),
                AvgScoreUs = avgUs,
                ActiveScoreCalcsPeak = peak,
                ScoreCalcsPerTick = perTickDensity,
                EquipTryCalls = Math.Max(0, d_try),
                EquipJobsGiven = Math.Max(0, d_give),
                BlacklistAdds = Math.Max(0, d_blAdds),
                CacheCleaned = Math.Max(0, d_cleaned)
            };
        }
    }
}
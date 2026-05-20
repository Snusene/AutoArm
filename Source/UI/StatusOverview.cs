
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoArm.UI
{
    internal sealed class StatusOverviewData
    {
        public ColonistListsInfo colonistLists;
    }

    internal sealed class TopWeaponInfo
    {
        public ThingWithComps weapon;
        public float baseScore;
        public float pawnScore;
        public bool isRanged;
        public bool isForbidden;
    }

    internal sealed class ConsolidatedWeaponInfo
    {
        public ThingDef weaponDef;
        public QualityCategory quality;
        public int minQualityPercent;
        public int maxQualityPercent;
        public int count;
        public ThingWithComps representativeWeapon;
        public float averageScore;
        public bool isRanged;
        public bool isForbidden;
    }

    internal sealed class ColonistListsInfo
    {
        public List<ColonistInfo> validActive;
        public List<ColonistInfo> validBusy;
        public List<ColonistInfo> invalid;
    }

    internal sealed class ColonistInfo
    {
        public Pawn pawn;
        public bool isValid;
        public string invalidReason;
        public int shootingSkill;
        public int meleeSkill;
        public ThingWithComps primaryWeapon;
        public float weaponScore;
        public float weaponBaseScore;
        public bool weaponBonded;
        public int sidearmCount;
        public string outfitName;
        public int outfitAllowedWeapons;
        public bool isHunter;
        public bool isBrawler;
        public bool isTemp;
        public bool hasNoRanged;
        public bool hasNoMelee;
        public int age;
        public int minAge;
        public string weaponText;
        public string tagString;
    }

    internal static class StatusOverviewDataGatherer
    {
        private const int CACHE_DURATION = 60;

        private static int lastColonistListCacheTick = -1;
        private static ColonistListsInfo cachedColonistLists = null;

        private static Dictionary<ApparelPolicy, int> outfitWeaponCountCache = new Dictionary<ApparelPolicy, int>();

        private struct TopWeaponsKey : IEquatable<TopWeaponsKey>
        {
            public readonly int MapId;
            public readonly int PawnId;
            public readonly int Limit;

            public TopWeaponsKey(int mapId, int pawnId, int limit)
            {
                MapId = mapId; PawnId = pawnId; Limit = limit;
            }

            public bool Equals(TopWeaponsKey other)
                => MapId == other.MapId && PawnId == other.PawnId && Limit == other.Limit;

            public override bool Equals(object obj) => obj is TopWeaponsKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked { return ((MapId * 397) ^ PawnId) * 31 ^ Limit; }
            }
        }

        private struct TopWeaponsEntry
        {
            public List<TopWeaponInfo> weapons;
            public int hash;
        }

        private static readonly TickExpiringLruCache<TopWeaponsKey, TopWeaponsEntry> topWeaponsCache =
            new TickExpiringLruCache<TopWeaponsKey, TopWeaponsEntry>(64, 60, 500);

        public static void ClearTopWeaponsCache() => topWeaponsCache.Clear();

        public static void InvalidateOutfitWeaponCount(ApparelPolicy outfit)
        {
            if (outfit == null) outfitWeaponCountCache.Clear();
            else outfitWeaponCountCache.Remove(outfit);
        }

        public static int CleanupTopWeaponsCache()
            => topWeaponsCache.CleanupExpired(Find.TickManager?.TicksGame ?? 0);

        public static StatusOverviewData GatherData(Map map)
        {
            if (map == null) return null;

            var data = new StatusOverviewData();

            Pawn selectedPawn = null;
            if (Find.Selector?.SingleSelectedThing is Pawn pawn && pawn.IsColonist)
            {
                selectedPawn = pawn;
            }

            data.colonistLists = GatherColonistLists(map);

            return data;
        }

        public static List<TopWeaponInfo> GetTopWeapons(Map map, Pawn pawn, int limit = 10)
        {
            if (map == null || pawn == null) return new List<TopWeaponInfo>();

            var key = new TopWeaponsKey(map.uniqueID, pawn.thingIDNumber, limit);
            int now = Find.TickManager.TicksGame;
            int currentHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.Weapon);

            if (topWeaponsCache.TryGet(key, now, out var cached) && cached.hash == currentHash)
                return cached.weapons;

            var computed = ComputeTopWeapons(map, pawn, limit);
            topWeaponsCache.Set(key, new TopWeaponsEntry { weapons = computed, hash = currentHash }, now);
            return computed;
        }


        private static List<TopWeaponInfo> ComputeTopWeapons(Map map, Pawn pawn, int limit)
        {
            var groundWeapons = new List<ThingWithComps>(256);
            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

            for (int i = 0; i < allWeapons.Count; i++)
            {
                if (allWeapons[i] is ThingWithComps weapon &&
                    weapon.Spawned &&
                    !weapon.ParentHolder.IsEnclosingContainer() &&
                    Validation.IsWeapon(weapon) &&
                    !CompBiocodable.IsBiocoded(weapon))
                {
                    groundWeapons.Add(weapon);
                }
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var currentWeapon = pawn.equipment?.Primary;
            var filteredWeapons = new List<ThingWithComps>(groundWeapons.Count);

            for (int i = 0; i < groundWeapons.Count; i++)
            {
                if (jobGiver.ShouldConsiderWeapon(pawn, groundWeapons[i], currentWeapon, false))
                {
                    filteredWeapons.Add(groundWeapons[i]);
                }
            }

            var weaponScores = new List<(ThingWithComps weapon, float baseScore, float pawnScore)>(filteredWeapons.Count);
            foreach (var weapon in filteredWeapons)
            {
                float baseScore = Scoring.GetWeaponPropertyScore(null, weapon);
                float pawnScore = WeaponCache.GetCachedScore(pawn, weapon);
                weaponScores.Add((weapon, baseScore, pawnScore));
            }

            weaponScores.Sort((a, b) => b.pawnScore.CompareTo(a.pawnScore));

            var playerFaction = Find.FactionManager?.OfPlayer;
            int count = weaponScores.Count < limit ? weaponScores.Count : limit;
            var result = new List<TopWeaponInfo>(count);

            for (int i = 0; i < count; i++)
            {
                var w = weaponScores[i];
                result.Add(new TopWeaponInfo
                {
                    weapon = w.weapon,
                    baseScore = w.baseScore,
                    pawnScore = w.pawnScore,
                    isRanged = w.weapon.def.IsRangedWeapon,
                    isForbidden = playerFaction != null && w.weapon.IsForbidden(playerFaction)
                });
            }

            return result;
        }

        private static ColonistListsInfo GatherColonistLists(Map map)
        {
            int currentTick = Find.TickManager.TicksGame;
            if (cachedColonistLists != null && currentTick - lastColonistListCacheTick < CACHE_DURATION)
            {
                return cachedColonistLists;
            }

            var src = map.mapPawns.FreeColonistsSpawned;
            var tagged = new List<(string name, Pawn pawn)>(src.Count);
            foreach (var p in src)
                tagged.Add((p.Name?.ToStringShort ?? "Unknown", p));
            tagged.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            var colonists = new List<Pawn>(tagged.Count);
            foreach (var t in tagged)
                colonists.Add(t.pawn);

            var validActive = new List<ColonistInfo>();
            var validBusy = new List<ColonistInfo>();
            var invalid = new List<ColonistInfo>();

            foreach (var pawn in colonists)
            {
                var info = CreateColonistInfo(pawn);

                if (info.isValid)
                {
                    validActive.Add(info);
                }
                else if (info.invalidReason != null &&
                        (info.invalidReason.Contains("hauling") ||
                         info.invalidReason.Contains("bed") ||
                         info.invalidReason.Contains("In bed")))
                {
                    validBusy.Add(info);
                }
                else
                {
                    invalid.Add(info);
                }
            }

            var result = new ColonistListsInfo
            {
                validActive = validActive,
                validBusy = validBusy,
                invalid = invalid
            };

            cachedColonistLists = result;
            lastColonistListCacheTick = currentTick;

            return result;
        }

        private static ColonistInfo CreateColonistInfo(Pawn pawn)
        {
            string reason;
            bool isValid = ValidationHelper.IsValidPawn(pawn, out reason);

            bool raidActive = ModInit.IsLargeRaidActive && (AutoArmMod.settings?.disableDuringRaids ?? false);
            if (raidActive)
            {
                isValid = false;
                reason = "AutoArm_ReasonRaidActive".Translate();
            }

            var info = new ColonistInfo
            {
                pawn = pawn,
                isValid = isValid,
                invalidReason = reason,
                shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0,
                meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0
            };

            if (pawn.equipment?.Primary != null)
            {
                info.primaryWeapon = pawn.equipment.Primary;
                info.weaponBaseScore = Scoring.GetWeaponPropertyScore(null, info.primaryWeapon);
                info.weaponScore = Scoring.GetTotalScore(pawn, info.primaryWeapon);
                info.weaponBonded = ValidationHelper.IsWeaponBondedToPawn(info.primaryWeapon, pawn);
            }

            if (SimpleSidearmsCompat.IsLoaded && pawn.inventory?.innerContainer != null)
            {
                int sidearmCount = 0;
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    var thing = pawn.inventory.innerContainer[i];
                    if (thing is ThingWithComps && Validation.IsWeapon(thing.def))
                    {
                        sidearmCount++;
                    }
                }
                info.sidearmCount = sidearmCount;
            }

            if (pawn.outfits?.CurrentApparelPolicy != null)
            {
                info.outfitName = pawn.outfits.CurrentApparelPolicy.label;
                info.outfitAllowedWeapons = GetCachedOutfitWeaponCount(pawn.outfits.CurrentApparelPolicy);
            }

            info.isHunter = pawn.workSettings != null &&
                           pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hunting) &&
                           !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting);
            info.isBrawler = pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true;
            info.isTemp = AutoArm.Jobs.JobHelper.IsTemporary(pawn);

            if (ModsConfig.IdeologyActive && pawn.Ideo != null)
            {
                var role = pawn.Ideo.GetRole(pawn);
                var effects = role?.def?.roleEffects;
                if (effects != null)
                {
                    for (int i = 0; i < effects.Count; i++)
                    {
                        var effect = effects[i];
                        if (effect is RoleEffect_NoRangedWeapons) info.hasNoRanged = true;
                        else if (effect is RoleEffect_NoMeleeWeapons) info.hasNoMelee = true;
                    }
                }
            }

            if (ModsConfig.BiotechActive && pawn.ageTracker != null)
            {
                bool childrenAllowed = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
                info.minAge = childrenAllowed ?
                    (AutoArmMod.settings?.childrenMinAge ?? Constants.ChildDefaultMinAge) : 13;
                info.age = (int)pawn.ageTracker.AgeBiologicalYears;
            }

            info.weaponText = BuildWeaponText(info);
            info.tagString = BuildTagString(info);

            return info;
        }

        private static string BuildWeaponText(ColonistInfo info)
        {
            if (info.primaryWeapon == null)
                return "None";

            var sb = new System.Text.StringBuilder();
            sb.Append(info.primaryWeapon.Label);
            sb.Append(" (");
            sb.Append(info.weaponScore.ToString("F0"));
            sb.Append(')');
            if (info.weaponBonded) sb.Append(" bonded");
            if (info.sidearmCount > 0) { sb.Append(" +"); sb.Append(info.sidearmCount); }
            return sb.ToString();
        }

        private static string BuildTagString(ColonistInfo info)
        {
            bool any = info.isHunter || info.isBrawler || info.isTemp ||
                       info.hasNoRanged || info.hasNoMelee ||
                       (info.age > 0 && info.age < info.minAge);
            if (!any) return "";

            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            bool first = true;
            void Add(string tag)
            {
                if (!first) sb.Append("] [");
                sb.Append(tag);
                first = false;
            }
            if (info.isHunter) Add("hunter");
            if (info.isBrawler) Add("brawler");
            if (info.isTemp) Add("temp");
            if (info.hasNoRanged) Add("no ranged");
            if (info.hasNoMelee) Add("no melee");
            if (info.age > 0 && info.age < info.minAge) Add($"age: {info.age}/{info.minAge}");
            sb.Append(']');
            return sb.ToString();
        }


        private static int GetCachedOutfitWeaponCount(ApparelPolicy outfit)
        {
            if (outfit == null) return 0;

            if (outfitWeaponCountCache.TryGetValue(outfit, out int count))
            {
                return count;
            }

            int weaponCount = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.IsWeapon && outfit.filter.Allows(def))
                {
                    weaponCount++;
                }
            }

            outfitWeaponCountCache[outfit] = weaponCount;
            return weaponCount;
        }

        public static void ClearCaches()
        {
            cachedColonistLists = null;
            lastColonistListCacheTick = -1;
            topWeaponsCache.Clear();
            outfitWeaponCountCache.Clear();
        }
    }

    internal static class DebugPanel
    {
        public static bool isGatheringDebugData = false;

        private static int lastUpdateTick = -1;
        private const int UPDATE_INTERVAL = 15;
        private static StatusOverviewData cachedData = null;

        private static Vector2 scrollPosition = Vector2.zero;
        private const float SECTION_GAP = 15f;
        private const float LINE_HEIGHT = 24f;
        private static Pawn expandedPawn = null;
        private static Pawn cameraFollowTarget = null;
        private static float expandedPawnTargetScreenY = -1f;
        private static Rect lastScrollViewRect = Rect.zero;
        private static bool userIsManuallyScrolling = false;
        private static Vector2 lastAutoScrollPosition = Vector2.zero;
        private enum ViewMode { StatusOverview, WeaponScores, TestResults }
        private static ViewMode currentView = ViewMode.StatusOverview;
        private static AutoArm.Testing.TestResults testResults;
        private static System.TimeSpan testsDuration;
        private static bool testsRunning;

        private static System.Reflection.FieldInfo cachedRootPosField = null;
        private static System.Reflection.FieldInfo cachedVelocityField = null;

        private struct ConsolidatedKey : IEquatable<ConsolidatedKey>
        {
            public readonly int MapId;
            public readonly int PawnId;

            public ConsolidatedKey(int mapId, int pawnId)
            {
                MapId = mapId;
                PawnId = pawnId;
            }

            public bool Equals(ConsolidatedKey other) => MapId == other.MapId && PawnId == other.PawnId;
            public override bool Equals(object obj) => obj is ConsolidatedKey k && Equals(k);
            public override int GetHashCode() => unchecked((MapId * 397) ^ PawnId);
        }

        private static readonly Caching.TickExpiringLruCache<ConsolidatedKey, List<ConsolidatedWeaponInfo>> consolidatedCache =
            new Caching.TickExpiringLruCache<ConsolidatedKey, List<ConsolidatedWeaponInfo>>(32, 60, 200);

        private struct WeaponGroupInfo
        {
            public string label;
            public int count;
            public bool isRanged;
            public bool isForbidden;
            public float baseScore;
            public ThingWithComps firstWeapon;
        }
        private static List<WeaponGroupInfo> cachedWeaponGroups = null;
        private static int lastWeaponGroupCacheTick = -1;
        private static int cachedWeaponGroupHash = -1;
        private const int WEAPON_CACHE_DURATION = 60;

        // Must call on close - stops background camera/cache
        public static void ResetState()
        {
            expandedPawn = null;
            cameraFollowTarget = null;
            expandedPawnTargetScreenY = -1f;
            scrollPosition = Vector2.zero;
            lastScrollViewRect = Rect.zero;
            userIsManuallyScrolling = false;
            lastAutoScrollPosition = Vector2.zero;

            cachedWeaponGroups = null;
            lastWeaponGroupCacheTick = -1;
            cachedWeaponGroupHash = -1;

            cachedData = null;
            lastUpdateTick = -1;

            StatusOverviewDataGatherer.ClearCaches();

        }

        public static void Draw(Rect rect)
        {
            isGatheringDebugData = true;

            var map = Find.CurrentMap;
            if (map == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "AutoArm_NoActiveMap".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                isGatheringDebugData = false;
                return;
            }

            const float tabHeight = 32f;
            const float tabGap = 4f;
            float tabWidth = (rect.width - tabGap * 4f) / 3f;
            float tabY = rect.y + 4f;
            Rect statusTab = new Rect(rect.x + tabGap, tabY, tabWidth, tabHeight);
            Rect weaponsTab = new Rect(statusTab.xMax + tabGap, tabY, tabWidth, tabHeight);
            Rect testsTab = new Rect(weaponsTab.xMax + tabGap, tabY, tabWidth, tabHeight);

            rect.y += tabHeight + 8f;
            rect.height -= tabHeight + 8f;

            if (Widgets.ButtonTextSubtle(statusTab, "AutoArm_StatusOverview".Translate(), currentView == ViewMode.StatusOverview ? 1f : 0f))
                currentView = ViewMode.StatusOverview;
            if (Widgets.ButtonTextSubtle(weaponsTab, "AutoArm_WeaponScoresTab".Translate(), currentView == ViewMode.WeaponScores ? 1f : 0f))
                currentView = ViewMode.WeaponScores;
            if (Widgets.ButtonTextSubtle(testsTab, "Run All Tests", currentView == ViewMode.TestResults ? 1f : 0f))
                StartTestRun(map);

            if (cameraFollowTarget != null)
            {
                bool userInput = Event.current.type == EventType.MouseDown ||
                                 Event.current.type == EventType.KeyDown ||
                                 KeyBindingDefOf.MapDolly_Left.IsDownEvent ||
                                 KeyBindingDefOf.MapDolly_Right.IsDownEvent ||
                                 KeyBindingDefOf.MapDolly_Up.IsDownEvent ||
                                 KeyBindingDefOf.MapDolly_Down.IsDownEvent;

                if (userInput)
                {
                    cameraFollowTarget = null;
                }
                else if (cameraFollowTarget.Spawned && Find.CurrentMap == cameraFollowTarget.Map)
                {
                    if (Event.current.type == EventType.Repaint)
                    {
                        var targetPos = cameraFollowTarget.DrawPos;

                        if (cachedRootPosField == null)
                        {
                            cachedRootPosField = typeof(CameraDriver).GetField("rootPos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            cachedVelocityField = typeof(CameraDriver).GetField("velocity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        }

                        var cameraDriver = Find.CameraDriver;
                        if (cachedRootPosField != null)
                        {
                            var currentPos = (Vector3)cachedRootPosField.GetValue(cameraDriver);
                            targetPos.y = currentPos.y;

                            cachedRootPosField.SetValue(cameraDriver, targetPos);

                            if (cachedVelocityField != null)
                                cachedVelocityField.SetValue(cameraDriver, Vector3.zero);
                        }
                        else
                        {
                            CameraJumper.TryJump(cameraFollowTarget);
                        }
                    }
                }
                else
                {
                    cameraFollowTarget = null;
                }
            }

            int currentTick = Find.TickManager.TicksGame;
            if (cachedData == null || currentTick - lastUpdateTick >= UPDATE_INTERVAL)
            {
                cachedData = StatusOverviewDataGatherer.GatherData(map);
                lastUpdateTick = currentTick;
            }

            var data = cachedData;
            if (data == null)
            {
                isGatheringDebugData = false;
                return;
            }

            float contentHeight = CalculateContentHeight(data);

            if (expandedPawn != null && expandedPawnTargetScreenY >= 0f && !userIsManuallyScrolling)
            {
                float expandedPawnContentY = CalculateExpandedPawnContentY(data);
                if (expandedPawnContentY >= 0f)
                {
                    float desiredScrollY = expandedPawnContentY - expandedPawnTargetScreenY;

                    float maxScrollY = Mathf.Max(0f, contentHeight - rect.height);
                    scrollPosition.y = Mathf.Clamp(desiredScrollY, 0f, maxScrollY);
                    lastAutoScrollPosition = scrollPosition;
                }
            }

            Rect viewRect = new Rect(0, 0, rect.width - 20f, contentHeight);
            lastScrollViewRect = rect;
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);

            if (expandedPawn != null && scrollPosition != lastAutoScrollPosition)
            {
                userIsManuallyScrolling = true;
            }

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            if (currentView == ViewMode.WeaponScores)
            {
                DrawWeaponScoresSection(listing, map);
            }
            else if (currentView == ViewMode.TestResults)
            {
                DrawTestResultsSection(listing);
            }
            else
            {
                DrawColonistListsSection(listing, data.colonistLists, map);
            }

            listing.End();
            Widgets.EndScrollView();

            isGatheringDebugData = false;
        }

        private static float GetExpandedHeight(Pawn pawn)
        {
            var map = Find.CurrentMap;
            if (map == null) return LINE_HEIGHT;

            int weaponCount = 0;
            foreach (var w in StatusOverviewDataGatherer.GetTopWeapons(map, pawn, 10))
            {
                if (!w.isForbidden && ++weaponCount >= 3) break;
            }

            if (weaponCount == 0)
                return LINE_HEIGHT;

            float headerHeight = LINE_HEIGHT * 0.8f;
            float weaponLinesHeight = weaponCount * (LINE_HEIGHT * 0.8f);
            float padding = LINE_HEIGHT * 0.7f;
            return headerHeight + weaponLinesHeight + padding;
        }

        private static float CalculateContentHeight(StatusOverviewData data)
        {
            float height = 0f;

            if (currentView == ViewMode.WeaponScores)
            {
                var map = Find.CurrentMap;
                if (map != null)
                {
                    int weaponCount = WeaponCache.GetCacheWeaponCount(map);

                    height = 40f + (weaponCount * LINE_HEIGHT * 0.8f) + 100f;
                }
                return height;
            }

            if (currentView == ViewMode.TestResults)
            {
                return CalculateTestResultsHeight();
            }

            if (data.colonistLists.validActive.Any())
            {
                height += 40f;
                foreach (var colonist in data.colonistLists.validActive)
                {
                    height += LINE_HEIGHT * 1.4f;
                    if (expandedPawn == colonist.pawn)
                        height += GetExpandedHeight(colonist.pawn);
                }
                height += SECTION_GAP;
            }

            if (data.colonistLists.validBusy.Any())
            {
                height += 40f;
                foreach (var colonist in data.colonistLists.validBusy)
                {
                    height += LINE_HEIGHT * 1.4f;
                    if (expandedPawn == colonist.pawn)
                        height += GetExpandedHeight(colonist.pawn);
                }
                height += SECTION_GAP;
            }

            if (data.colonistLists.invalid.Any())
            {
                height += 40f;
                foreach (var colonist in data.colonistLists.invalid)
                {
                    height += LINE_HEIGHT * 1.4f;
                    if (expandedPawn == colonist.pawn)
                        height += GetExpandedHeight(colonist.pawn);
                }
            }

            height += 100f;

            return height;
        }


        private static float CalculateExpandedPawnContentY(StatusOverviewData data)
        {
            if (expandedPawn == null)
                return -1f;

            float currentY = 0f;

            if (data.colonistLists.validActive.Any())
            {
                currentY += 40f;
                foreach (var colonist in data.colonistLists.validActive)
                {
                    if (colonist.pawn == expandedPawn)
                        return currentY;

                    currentY += LINE_HEIGHT * 1.4f;
                    if (expandedPawn == colonist.pawn)
                        currentY += (LINE_HEIGHT * 3) + 30f;
                }
                currentY += SECTION_GAP;
            }

            if (data.colonistLists.validBusy.Any())
            {
                currentY += 40f;
                foreach (var colonist in data.colonistLists.validBusy)
                {
                    if (colonist.pawn == expandedPawn)
                        return currentY;

                    currentY += LINE_HEIGHT * 1.4f;
                    if (expandedPawn == colonist.pawn)
                        currentY += (LINE_HEIGHT * 3) + 30f;
                }
                currentY += SECTION_GAP;
            }

            if (data.colonistLists.invalid.Any())
            {
                currentY += 40f;
                foreach (var colonist in data.colonistLists.invalid)
                {
                    if (colonist.pawn == expandedPawn)
                        return currentY;

                    currentY += LINE_HEIGHT * 1.4f;
                    if (expandedPawn == colonist.pawn)
                        currentY += (LINE_HEIGHT * 3) + 30f;
                }
            }

            return -1f;
        }


        private static void DrawColonistListsSection(Listing_Standard listing, ColonistListsInfo lists, Map map)
        {
            if (lists.validActive.Any())
            {
                DrawColonistList(listing, "AutoArm_ColonistsValidActive".Translate(), lists.validActive, UIColors.Active, map,
                    "AutoArm_ColonistsValidActiveTooltip".Translate());
                listing.Gap(SECTION_GAP);
            }

            if (lists.validBusy.Any())
            {
                DrawColonistList(listing, "AutoArm_ColonistsValidBusy".Translate(), lists.validBusy, UIColors.Busy, map,
                    "AutoArm_ColonistsValidBusyTooltip".Translate());
                listing.Gap(SECTION_GAP);
            }

            if (lists.invalid.Any())
            {
                DrawColonistList(listing, "AutoArm_ColonistsInvalid".Translate(), lists.invalid, UIColors.Invalid, map,
                    "AutoArm_ColonistsInvalidTooltip".Translate());
            }
        }

        private static void DrawColonistList(Listing_Standard listing, string title, List<ColonistInfo> colonists, Color titleColor, Map map, string tooltip = null)
        {
            var headerRect = listing.GetRect(22f);
            using (new TextBlock(titleColor))
                Widgets.Label(headerRect, $"{title} ({colonists.Count})");
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(headerRect, tooltip);

            Widgets.DrawLineHorizontal(headerRect.x, headerRect.yMax, headerRect.width);
            listing.Gap(2f);

            foreach (var colonist in colonists)
            {
                DrawColonistLine(listing, colonist, map);
            }
        }

        private static void DrawColonistLine(Listing_Standard listing, ColonistInfo colonist, Map map)
        {
            bool isExpanded = expandedPawn == colonist.pawn;
            float baseHeight = LINE_HEIGHT * 1.4f;

            float expandedWeaponsHeight = 0f;
            if (isExpanded)
            {
                int weaponCount = 0;
                foreach (var w in StatusOverviewDataGatherer.GetTopWeapons(map, colonist.pawn, 10))
                {
                    if (!w.isForbidden && ++weaponCount >= 3) break;
                }

                if (weaponCount == 0)
                {
                    expandedWeaponsHeight = LINE_HEIGHT;
                }
                else
                {
                    float headerHeight = LINE_HEIGHT * 0.8f;
                    float weaponLinesHeight = weaponCount * (LINE_HEIGHT * 0.8f);
                    float padding = LINE_HEIGHT * 0.7f;
                    expandedWeaponsHeight = headerHeight + weaponLinesHeight + padding;
                }
            }

            var lineRect = listing.GetRect(baseHeight + expandedWeaponsHeight);
            var clickRect = new Rect(lineRect.x, lineRect.y, lineRect.width, baseHeight);

            if (Widgets.ButtonInvisible(clickRect))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(colonist.pawn);
                cameraFollowTarget = colonist.pawn;

                if (expandedPawn == colonist.pawn)
                {
                    expandedPawn = null;
                }
                else
                {
                    expandedPawn = colonist.pawn;
                    expandedPawnTargetScreenY = lineRect.y - scrollPosition.y;
                    userIsManuallyScrolling = false;
                    lastAutoScrollPosition = scrollPosition;
                }
            }

            if (Mouse.IsOver(clickRect))
            {
                Widgets.DrawHighlight(clickRect);
            }

            float y = lineRect.y;
            var line1Rect = new Rect(lineRect.x, y, lineRect.width, LINE_HEIGHT);

            string statusIcon = colonist.isValid ? "✓" : "✗";
            Color statusColor = colonist.isValid ? UIColors.Active : UIColors.Invalid;
            using (new TextBlock(statusColor))
                Widgets.Label(new Rect(line1Rect.x, line1Rect.y, 20f, line1Rect.height), statusIcon);

            float nameX = line1Rect.x + 22f;
            string name = colonist.pawn.Name?.ToStringShort ?? "Unknown";

            string tags = colonist.tagString;
            float reservedTagWidth = 0f;
            if (!string.IsNullOrEmpty(tags))
            {
                using (new TextBlock(GameFont.Tiny))
                    reservedTagWidth = Math.Min(Text.CalcSize(tags).x + 4f, 150f) + 2f;
            }

            const float minNameGap = 6f;
            const float weaponMinSpace = 60f;
            float nameBudget = Math.Max(40f, line1Rect.xMax - nameX - reservedTagWidth - minNameGap - weaponMinSpace);
            string truncatedName = name.Truncate(nameBudget);
            float nameWidth = Math.Min(Text.CalcSize(truncatedName).x, nameBudget);
            Widgets.Label(new Rect(nameX, line1Rect.y, nameWidth, line1Rect.height), truncatedName);

            float tagsEndX = nameX + nameWidth;
            if (!string.IsNullOrEmpty(tags))
            {
                using (new TextBlock(GameFont.Tiny))
                using (new TextBlock(UIColors.Dim))
                {
                    float tagX = tagsEndX + 2f;
                    float tagWidth = Math.Min(Text.CalcSize(tags).x + 4f, 150f);
                    Widgets.Label(new Rect(tagX, line1Rect.y, tagWidth, line1Rect.height), tags);
                    tagsEndX = tagX + tagWidth;
                }
            }

            string weaponText = colonist.weaponText ?? "None";
            Color weaponColor = colonist.primaryWeapon != null ? UIColors.LabelMuted : UIColors.NoWeapon;

            float weaponMaxSpace = Math.Max(weaponMinSpace, line1Rect.xMax - (tagsEndX + minNameGap));
            string truncatedWeapon = weaponText.Truncate(weaponMaxSpace);
            float weaponWidth = Math.Min(Text.CalcSize(truncatedWeapon).x + 2f, weaponMaxSpace);
            var weaponRect = new Rect(line1Rect.xMax - weaponWidth, line1Rect.y, weaponWidth, line1Rect.height);
            using (new TextBlock(weaponColor))
            using (new TextBlock(TextAnchor.MiddleRight))
                Widgets.Label(weaponRect, truncatedWeapon);

            y += LINE_HEIGHT * 0.7f;

            var line2Rect = new Rect(nameX, y, lineRect.width - (nameX - lineRect.x), LINE_HEIGHT * 0.7f);
            using (new TextBlock(GameFont.Tiny))
            {
                var line2Parts = ListPool<string>.Get();
                Color line2Color = UIColors.Dim;

                if (!colonist.isValid && !string.IsNullOrEmpty(colonist.invalidReason))
                {
                    line2Color = UIColors.InvalidReason;
                    line2Parts.Add("AutoArm_StatusReason".Translate(colonist.invalidReason));
                }

                line2Parts.Add("AutoArm_StatusSkillsShort".Translate(colonist.shootingSkill, colonist.meleeSkill));

                if (colonist.primaryWeapon != null)
                    line2Parts.Add("AutoArm_StatusBaseScore".Translate(colonist.weaponBaseScore.ToString("F0")));

                if (!string.IsNullOrEmpty(colonist.outfitName))
                    line2Parts.Add("AutoArm_StatusOutfitWeapons".Translate(colonist.outfitName, colonist.outfitAllowedWeapons));

                string line2Text = string.Join(", ", line2Parts);
                ListPool<string>.Return(line2Parts);
                using (new TextBlock(line2Color))
                    Widgets.Label(line2Rect, line2Text);
            }

            TooltipHandler.TipRegion(new Rect(lineRect.x, lineRect.y, lineRect.width, baseHeight), BuildColonistTooltip(colonist));

            if (isExpanded)
            {
                y += LINE_HEIGHT * 0.7f;
                DrawInlineTopWeapons(listing, colonist.pawn, map, y, lineRect.width);
            }
        }

        private static List<ConsolidatedWeaponInfo> GetOrBuildConsolidatedWeapons(Map map, Pawn pawn)
        {
            var key = new ConsolidatedKey(map.uniqueID, pawn.thingIDNumber);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (consolidatedCache.TryGet(key, now, out var cachedList))
                return cachedList;

            var topWeapons = StatusOverviewDataGatherer.GetTopWeapons(map, pawn, 10);
            var result = new List<ConsolidatedWeaponInfo>(topWeapons.Count);
            var groupIndex = new Dictionary<int, int>(topWeapons.Count);

            for (int i = 0; i < topWeapons.Count; i++)
            {
                var weaponInfo = topWeapons[i];
                if (weaponInfo.isForbidden)
                    continue;

                QualityCategory quality = QualityCategory.Normal;
                int qualityPercent = 0;
                if (weaponInfo.weapon.TryGetQuality(out quality))
                {
                    int maxHp = weaponInfo.weapon.MaxHitPoints;
                    if (maxHp > 0)
                        qualityPercent = (int)((float)weaponInfo.weapon.HitPoints / maxHp * 100f);
                }

                int groupKey = unchecked((weaponInfo.weapon.def.shortHash * 397) ^ (int)quality);

                if (groupIndex.TryGetValue(groupKey, out int existingIdx))
                {
                    var existing = result[existingIdx];
                    existing.count++;
                    existing.minQualityPercent = Math.Min(existing.minQualityPercent, qualityPercent);
                    existing.maxQualityPercent = Math.Max(existing.maxQualityPercent, qualityPercent);
                    existing.averageScore = Math.Max(existing.averageScore, weaponInfo.pawnScore);
                }
                else
                {
                    result.Add(new ConsolidatedWeaponInfo
                    {
                        weaponDef = weaponInfo.weapon.def,
                        quality = quality,
                        minQualityPercent = qualityPercent,
                        maxQualityPercent = qualityPercent,
                        count = 1,
                        representativeWeapon = weaponInfo.weapon,
                        averageScore = weaponInfo.pawnScore,
                        isRanged = weaponInfo.isRanged,
                        isForbidden = weaponInfo.isForbidden
                    });
                    groupIndex[groupKey] = result.Count - 1;
                }
            }

            consolidatedCache.Set(key, result, now);
            return result;
        }

        private static void DrawInlineTopWeapons(Listing_Standard listing, Pawn pawn, Map map, float startY, float width)
        {
            var consolidatedWeapons = GetOrBuildConsolidatedWeapons(map, pawn);
            if (consolidatedWeapons.Count == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = UIColors.Dim;
                var emptyRect = new Rect(25f, startY, width - 25f, LINE_HEIGHT);
                Widgets.Label(emptyRect, "AutoArm_NoValidWeapons".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }

            Text.Font = GameFont.Tiny;
            var headerRect = new Rect(25f, startY, width - 25f, LINE_HEIGHT * 0.7f);

            Widgets.DrawBoxSolid(headerRect, new Color(0.2f, 0.25f, 0.3f, 0.3f));

            GUI.color = UIColors.AccentHeader;
            Widgets.Label(new Rect(headerRect.x + 4f, headerRect.y + 2f, headerRect.width - 4f, headerRect.height),
                $"TOP 3 WEAPONS FOR {pawn.Name.ToStringShort.ToUpperInvariant()}");
            GUI.color = Color.white;

            Text.Font = GameFont.Small;

            float y = startY + LINE_HEIGHT * 0.8f;

            int rank = 1;
            foreach (var consolidated in consolidatedWeapons.Take(3))
            {
                var weaponLineRect = new Rect(35f, y, width - 35f, LINE_HEIGHT * 0.8f);

                string icon = consolidated.isRanged ? "⚡" : "⚔";
                Color iconColor = consolidated.isRanged ? UIColors.Ranged : UIColors.Melee;

                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(weaponLineRect.x, weaponLineRect.y, 15f, weaponLineRect.height), $"{rank}.");

                GUI.color = iconColor;
                Widgets.Label(new Rect(weaponLineRect.x + 18f, weaponLineRect.y, 20f, weaponLineRect.height), icon);
                GUI.color = Color.white;

                string weaponLabel = consolidated.weaponDef.label;
                if (consolidated.quality != QualityCategory.Normal || consolidated.maxQualityPercent > 0)
                {
                    if (consolidated.count > 1 && consolidated.minQualityPercent != consolidated.maxQualityPercent)
                        weaponLabel += $" ({consolidated.quality.GetLabel()} {consolidated.minQualityPercent}-{consolidated.maxQualityPercent}%)";
                    else if (consolidated.maxQualityPercent > 0)
                        weaponLabel += $" ({consolidated.quality.GetLabel()} {consolidated.maxQualityPercent}%)";
                    else
                        weaponLabel += $" ({consolidated.quality.GetLabel()})";
                }

                if (consolidated.count > 1)
                    weaponLabel += $" [{consolidated.count}x]";

                if (consolidated.isForbidden)
                {
                    GUI.color = UIColors.Dim;
                    weaponLabel += " [FORBIDDEN]";
                }

                Widgets.Label(new Rect(weaponLineRect.x + 40f, weaponLineRect.y, 250f, weaponLineRect.height), weaponLabel);
                GUI.color = Color.white;

                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(weaponLineRect.x + 300f, weaponLineRect.y, 80f, weaponLineRect.height), $"{consolidated.averageScore:F0}");
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(weaponLineRect))
                {
                    CameraJumper.TryJump(consolidated.representativeWeapon.Position, map);
                    Messages.Message("AutoArm_JumpedTo".Translate(consolidated.representativeWeapon.Label),
                        new LookTargets(consolidated.representativeWeapon), MessageTypeDefOf.NeutralEvent, false);
                }

                string tooltip = $"{weaponLabel}\n";
                if (consolidated.count > 1)
                    tooltip += "AutoArm_VariantsFound".Translate(consolidated.count) + "\n";
                if (consolidated.representativeWeapon != null && !consolidated.representativeWeapon.Destroyed)
                {
                    tooltip += "AutoArm_TooltipPosition".Translate(consolidated.representativeWeapon.Position) + "\n";

                    if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                    {
                        var breakdown = AutoArm.Scoring.GetScoreBreakdown(pawn, consolidated.representativeWeapon);
                        tooltip += "\n" + BuildScoreBreakdownText(breakdown, consolidated.isRanged);
                    }
                }

                tooltip += "\nClick to jump to weapon";
                TooltipHandler.TipRegion(weaponLineRect, tooltip);

                Text.Font = GameFont.Small;
                y += LINE_HEIGHT * 0.8f;

                rank++;
            }
        }

        private static void DrawWeaponScoresSection(Listing_Standard listing, Map map)
        {
            int currentTick = Find.TickManager.TicksGame;
            int currentHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.Weapon);
            List<WeaponGroupInfo> weaponGroups;

            if (cachedWeaponGroups != null
                && cachedWeaponGroupHash == currentHash
                && currentTick - lastWeaponGroupCacheTick < WEAPON_CACHE_DURATION)
            {
                weaponGroups = cachedWeaponGroups;
            }
            else
            {
                var weapons = new List<ThingWithComps>();
                var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
                var playerFaction = Faction.OfPlayerSilentFail;

                for (int i = 0; i < allWeapons.Count; i++)
                {
                    if (allWeapons[i] is ThingWithComps weapon &&
                        weapon.Spawned &&
                        !weapon.ParentHolder.IsEnclosingContainer() &&
                        Validation.IsWeapon(weapon) &&
                        !CompBiocodable.IsBiocoded(weapon))
                    {
                        weapons.Add(weapon);
                    }
                }

                var groupMap = new Dictionary<(string, bool, bool), WeaponGroupInfo>();
                for (int i = 0; i < weapons.Count; i++)
                {
                    var w = weapons[i];
                    var key = (w.Label, playerFaction != null && w.IsForbidden(playerFaction), w.def.IsRangedWeapon);
                    if (groupMap.TryGetValue(key, out var existing))
                    {
                        existing.count++;
                        groupMap[key] = existing;
                    }
                    else
                    {
                        groupMap[key] = new WeaponGroupInfo
                        {
                            label = key.Item1,
                            count = 1,
                            isRanged = key.Item3,
                            isForbidden = key.Item2,
                            baseScore = Scoring.GetWeaponPropertyScore(null, w),
                            firstWeapon = w
                        };
                    }
                }

                var tempGroups = new List<WeaponGroupInfo>(groupMap.Values);
                tempGroups.Sort((a, b) => b.baseScore.CompareTo(a.baseScore));

                cachedWeaponGroups = tempGroups;
                cachedWeaponGroupHash = currentHash;
                lastWeaponGroupCacheTick = currentTick;
                weaponGroups = tempGroups;
            }

            int totalWeapons = 0;
            for (int i = 0; i < weaponGroups.Count; i++)
            {
                totalWeapons += weaponGroups[i].count;
            }
            var headerRect = listing.GetRect(22f);
            Widgets.Label(headerRect, "AutoArm_AllWeaponsOnMap".Translate(totalWeapons));
            TooltipHandler.TipRegion(headerRect, "AutoArm_AllWeaponsTooltip".Translate());
            using (new TextBlock(UIColors.Dim))
            using (new TextBlock(TextAnchor.MiddleRight))
                Widgets.Label(headerRect, "AutoArm_EffectivenessScore".Translate());
            Widgets.DrawLineHorizontal(headerRect.x, headerRect.yMax, headerRect.width);
            listing.Gap(2f);

            for (int i = 0; i < weaponGroups.Count; i++)
            {
                var weaponInfo = weaponGroups[i];
                var lineRect = listing.GetRect(LINE_HEIGHT);

                if (Widgets.ButtonInvisible(lineRect))
                {
                    CameraJumper.TryJump(weaponInfo.firstWeapon.Position, map);
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(weaponInfo.firstWeapon);
                    Messages.Message("AutoArm_JumpedTo".Translate(weaponInfo.label),
                        new LookTargets(weaponInfo.firstWeapon), MessageTypeDefOf.NeutralEvent, false);
                }

                if (Mouse.IsOver(lineRect))
                {
                    Widgets.DrawHighlight(lineRect);
                }

                string weaponLabel = weaponInfo.label;
                if (weaponInfo.count > 1)
                    weaponLabel = $"{weaponLabel} [{weaponInfo.count}]";

                QualityCategory quality = QualityCategory.Normal;
                bool hasQuality = weaponInfo.firstWeapon != null && weaponInfo.firstWeapon.TryGetQuality(out quality);
                Color labelColor = weaponInfo.isForbidden
                    ? UIColors.Forbidden
                    : (hasQuality ? UIColors.QualityColor(quality) : Color.white);

                float nameWidth = Text.CalcSize(weaponLabel).x;
                using (new TextBlock(labelColor))
                    Widgets.Label(new Rect(lineRect.x, lineRect.y, nameWidth, lineRect.height), weaponLabel);

                float currentX = lineRect.x + nameWidth + 4f;

                if (weaponInfo.isForbidden)
                    DrawPillTag(lineRect, ref currentX, "AutoArm_PillForbidden".Translate(), UIColors.PillForbiddenBg, Color.white);
                DrawPillTag(lineRect, ref currentX,
                    weaponInfo.isRanged ? "AutoArm_PillRanged".Translate() : "AutoArm_PillMelee".Translate(),
                    weaponInfo.isRanged ? UIColors.PillRangedBg : UIColors.PillMeleeBg,
                    Color.white);

                string scoreText = "AutoArm_StatusScore".Translate(weaponInfo.baseScore.ToString("F0"));
                using (new TextBlock(TextAnchor.MiddleRight))
                    Widgets.Label(new Rect(lineRect.x, lineRect.y, lineRect.width, lineRect.height), scoreText);

                if (Mouse.IsOver(lineRect))
                {
                    TooltipHandler.TipRegion(lineRect, new TipSignal(
                        () => $"{weaponInfo.label}\n\n" + AutoArm.Scoring.GetBaseScoreBreakdownText(weaponInfo.firstWeapon) + "\n" + "AutoArm_ClickToJump".Translate(),
                        weaponInfo.firstWeapon.thingIDNumber * 7919));
                }
            }
        }

        private static void DrawTestResultsSection(Listing_Standard listing)
        {
            if (testsRunning || testResults == null)
            {
                using (new TextBlock(UIColors.Dim))
                    Widgets.Label(listing.GetRect(LINE_HEIGHT), "Running tests...");
                return;
            }

            DrawStatsLine(listing);
            listing.Gap(6f);

            if (testResults.FailedTests > 0)
            {
                DrawSectionHeader(listing, "FAILED", testResults.FailedTests, UIColors.Fail,
                    "Tests that ran and asserted a failure. Reason shown below each.");
                foreach (var kvp in testResults.GetFailedTests())
                    DrawFailRow(listing, kvp.Key, kvp.Value);
                listing.Gap(6f);
            }

            if (testResults.PassedTests > 0)
            {
                DrawSectionHeader(listing, "PASSED", testResults.PassedTests, UIColors.Pass,
                    "Tests that ran and met their assertions.");
                foreach (var kvp in testResults.GetPassedTests())
                    DrawPassRow(listing, kvp.Key);
                listing.Gap(6f);
            }

            if (testResults.SkippedTests > 0)
            {
                DrawSectionHeader(listing, "SKIPPED", testResults.SkippedTests, UIColors.Skip,
                    "Tests whose preconditions were not met (e.g. required DLC or mod missing).");
                foreach (var kvp in testResults.GetSkippedTests())
                    DrawSkipRow(listing, kvp.Key, kvp.Value);
            }
        }

        private static void DrawStatsLine(Listing_Standard listing)
        {
            var r = listing.GetRect(LINE_HEIGHT);
            float x = r.x;

            x = DrawColoredSegment(r, x, $"{testResults.PassedTests} passed", UIColors.Pass);
            x = DrawColoredSegment(r, x, "   ", Color.white);
            x = DrawColoredSegment(r, x, $"{testResults.FailedTests} failed",
                testResults.FailedTests > 0 ? UIColors.Fail : UIColors.Dim);
            x = DrawColoredSegment(r, x, "   ", Color.white);
            x = DrawColoredSegment(r, x, $"{testResults.SkippedTests} skipped",
                testResults.SkippedTests > 0 ? UIColors.Skip : UIColors.Dim);

            string durationText = $"{testsDuration.TotalMilliseconds:F0}ms";
            float durationWidth = Text.CalcSize(durationText).x;
            var durationRect = new Rect(r.xMax - durationWidth, r.y, durationWidth, r.height);
            using (new TextBlock(UIColors.Dim))
                Widgets.Label(durationRect, durationText);
        }

        private static float DrawColoredSegment(Rect lineRect, float x, string text, Color color)
        {
            float width = Text.CalcSize(text).x;
            using (new TextBlock(color))
                Widgets.Label(new Rect(x, lineRect.y, width, lineRect.height), text);
            return x + width;
        }

        private static void DrawSectionHeader(Listing_Standard listing, string label, int count, Color color, string tooltip = null)
        {
            var r = listing.GetRect(LINE_HEIGHT);
            using (new TextBlock(color))
                Widgets.Label(r, $"{label} ({count})");
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(r, tooltip);
        }

        private static void DrawFailRow(Listing_Standard listing, string name, AutoArm.Testing.TestResult result)
        {
            var nameRect = listing.GetRect(LINE_HEIGHT);
            using (new TextBlock(UIColors.Fail))
                Widgets.Label(new Rect(nameRect.x + 12f, nameRect.y, nameRect.width - 12f, nameRect.height), name);
            DrawTimingRightAligned(nameRect, name);

            string reason = string.IsNullOrEmpty(result.FailureReason) ? "(no reason given)" : result.FailureReason;
            using (new TextBlock(GameFont.Tiny))
            {
                float reasonHeight = Text.CalcHeight(reason, listing.ColumnWidth - 36f);
                var reasonRect = listing.GetRect(reasonHeight);
                using (new TextBlock(UIColors.Dim))
                    Widgets.Label(new Rect(reasonRect.x + 36f, reasonRect.y, reasonRect.width - 36f, reasonHeight), reason);
            }
        }

        private static void DrawSkipRow(Listing_Standard listing, string name, AutoArm.Testing.TestResult result)
        {
            var r = listing.GetRect(LINE_HEIGHT);
            using (new TextBlock(UIColors.LabelMuted))
                Widgets.Label(new Rect(r.x + 12f, r.y, r.width - 12f, r.height), name);
        }

        private static void DrawPassRow(Listing_Standard listing, string name)
        {
            var r = listing.GetRect(LINE_HEIGHT);
            using (new TextBlock(UIColors.LabelMuted))
                Widgets.Label(new Rect(r.x + 12f, r.y, r.width - 12f, r.height), name);
            DrawTimingRightAligned(r, name);
        }

        private static void DrawTimingRightAligned(Rect lineRect, string testName)
        {
            var timing = testResults.GetTiming(testName);
            if (!timing.HasValue) return;

            string text = $"{timing.Value.TotalMilliseconds:F0}ms";
            using (new TextBlock(GameFont.Tiny))
            {
                float w = Text.CalcSize(text).x;
                using (new TextBlock(UIColors.Dim))
                    Widgets.Label(new Rect(lineRect.xMax - w, lineRect.y + 3f, w, lineRect.height), text);
            }
        }

        private static void StartTestRun(Map map)
        {
            testResults = null;
            testsRunning = true;
            currentView = ViewMode.TestResults;

            var priorSpeed = Find.TickManager.CurTimeSpeed;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var results = AutoArm.Testing.TestRunner.RunAllTests(map);
                    sw.Stop();
                    testResults = results;
                    testsDuration = sw.Elapsed;
                }
                finally
                {
                    testsRunning = false;
                    Find.TickManager.CurTimeSpeed = priorSpeed;
                }
            }, "", false, null);
        }

        private static void DrawPillTag(Rect rowRect, ref float x, string text, Color bgColor, Color textColor)
        {
            using (new TextBlock(GameFont.Tiny))
            {
                float textWidth = Text.CalcSize(text).x;
                float pillWidth = textWidth + 8f;
                var pill = new Rect(x, rowRect.y + (rowRect.height - 16f) / 2f, pillWidth, 16f);
                Widgets.DrawBoxSolid(pill, bgColor);
                using (new TextBlock(textColor))
                using (new TextBlock(TextAnchor.MiddleCenter))
                    Widgets.Label(pill, text);
                x += pillWidth + 3f;
            }
        }

        private static float CalculateTestResultsHeight()
        {
            if (testsRunning || testResults == null)
                return LINE_HEIGHT * 2f;

            float h = LINE_HEIGHT + 6f;

            if (testResults.FailedTests > 0)
            {
                h += LINE_HEIGHT;
                using (new TextBlock(GameFont.Tiny))
                {
                    foreach (var kvp in testResults.GetFailedTests())
                    {
                        h += LINE_HEIGHT;
                        string reason = string.IsNullOrEmpty(kvp.Value.FailureReason) ? "(no reason given)" : kvp.Value.FailureReason;
                        h += Text.CalcHeight(reason, 300f);
                    }
                }
                h += 6f;
            }

            if (testResults.PassedTests > 0)
                h += LINE_HEIGHT * (1 + testResults.PassedTests);

            if (testResults.SkippedTests > 0)
                h += LINE_HEIGHT * (1 + testResults.SkippedTests) + 6f;

            return h + 20f;
        }

        private static string BuildScoreBreakdownText(AutoArm.Scoring.ScoreBreakdown breakdown, bool isRanged)
        {
            string totalLine = "AutoArm_ScoreTotalLine".Translate(breakdown.totalScore.ToString("F0"));

            if (breakdown.isForced)
                return "AutoArm_ScoreForcedLocked".Translate() + "\n" + totalLine + "\n";
            if (breakdown.isForbidden)
                return "AutoArm_ScoreBlocked".Translate() + "\n" + totalLine + "\n";

            string fmtVal(float v) => v == 0f ? "—" : v.ToString("F0");
            string fmtMult(float m) => m == 1.0f ? "×1.00" : $"×{m:F2}";

            string typeLabel = isRanged ? "AutoArm_TypeRanged".Translate() : "AutoArm_TypeMelee".Translate();
            string outfitVal = breakdown.outfitPolicyScore < 0 ? breakdown.outfitPolicyScore.ToString("F0") : "AutoArm_StatusOK".Translate().ToString();

            string s = "AutoArm_ScoreType".Translate(typeLabel) + "\n";
            s += "\n" + "AutoArm_ScoreComponents".Translate() + "\n";
            s += "AutoArm_ScoreBase".Translate(fmtVal(breakdown.baseWeaponScore)) + "\n";
            s += "AutoArm_ScoreSkillBonus".Translate(fmtVal(breakdown.skillScore)) + "\n";
            s += "AutoArm_ScoreHunterBonus".Translate(fmtVal(breakdown.hunterScore)) + "\n";
            s += "AutoArm_ScoreOutfitPolicy".Translate(outfitVal) + "\n";
            s += "\n" + "AutoArm_ScoreMultipliers".Translate() + "\n";
            s += "AutoArm_ScoreSkillMatch".Translate(fmtMult(breakdown.skillMismatchMultiplier)) + "\n";
            s += "AutoArm_ScorePersona".Translate(fmtMult(breakdown.personaMultiplier)) + "\n";
            s += "AutoArm_ScoreCEAmmo".Translate(fmtMult(breakdown.ceAmmoModifier)) + "\n";
            s += "\n" + totalLine + "\n";
            return s;
        }

        private static string BuildColonistTooltip(ColonistInfo colonist)
        {
            string name = colonist.pawn.Name?.ToStringFull ?? "AutoArm_PawnUnknown".Translate();
            string tooltip = name + "\n\n";

            tooltip += "AutoArm_TooltipSkills".Translate(colonist.shootingSkill, colonist.meleeSkill) + "\n";

            if (colonist.primaryWeapon != null && !colonist.primaryWeapon.Destroyed
                && colonist.pawn != null && !colonist.pawn.Destroyed && !colonist.pawn.Dead)
            {
                tooltip += "\n" + "AutoArm_TooltipWeapon".Translate(colonist.primaryWeapon.Label) + "\n";

                var breakdown = AutoArm.Scoring.GetScoreBreakdown(colonist.pawn, colonist.primaryWeapon);
                tooltip += BuildScoreBreakdownText(breakdown, colonist.primaryWeapon.def.IsRangedWeapon);

                if (colonist.weaponBonded)
                    tooltip += "AutoArm_TooltipBonded".Translate() + "\n";
            }
            else
            {
                tooltip += "\n" + "AutoArm_TooltipWeaponNone".Translate() + "\n";
            }

            if (colonist.sidearmCount > 0)
            {
                tooltip += "AutoArm_TooltipSidearms".Translate(colonist.sidearmCount) + "\n";
            }

            if (!string.IsNullOrEmpty(colonist.outfitName))
            {
                tooltip += "\n" + "AutoArm_TooltipOutfit".Translate(colonist.outfitName) + "\n";
                tooltip += "AutoArm_TooltipAllows".Translate(colonist.outfitAllowedWeapons) + "\n";
            }

            if (!colonist.isValid && !string.IsNullOrEmpty(colonist.invalidReason))
            {
                tooltip += "\n" + "AutoArm_TooltipInvalid".Translate(colonist.invalidReason) + "\n";
            }

            return tooltip;
        }
    }
}

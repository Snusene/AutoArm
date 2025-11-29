
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoArm.UI
{
    /// <summary>
    /// Status overview data
    /// </summary>
    public class StatusOverviewData
    {
        public List<TopWeaponInfo> topWeapons;
        public ColonistListsInfo colonistLists;
        public Pawn contextPawn;
    }

    public class TopWeaponInfo
    {
        public ThingWithComps weapon;
        public float baseScore;
        public float pawnScore;
        public bool isRanged;
        public bool isForbidden;
    }

    public class ConsolidatedWeaponInfo
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

    public class ColonistListsInfo
    {
        public List<ColonistInfo> validActive;
        public List<ColonistInfo> validBusy;
        public List<ColonistInfo> invalid;
    }

    public class ColonistInfo
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
    }

    /// <summary>
    /// Gather status data
    /// </summary>
    public static class StatusOverviewDataGatherer
    {
        private static int lastGatherTick = -1;
        private static List<TopWeaponInfo> cachedTopWeapons = null;
        private static Pawn cachedForPawn = null;
        private const int CACHE_DURATION = 60;

        private static int lastColonistListCacheTick = -1;
        private static ColonistListsInfo cachedColonistLists = null;

        private static Dictionary<ApparelPolicy, int> outfitWeaponCountCache = new Dictionary<ApparelPolicy, int>();

        public static StatusOverviewData GatherData(Map map)
        {
            if (map == null) return null;

            var data = new StatusOverviewData();

            Pawn selectedPawn = null;
            if (Find.Selector?.SingleSelectedThing is Pawn pawn && pawn.IsColonist)
            {
                selectedPawn = pawn;
            }

            data.topWeapons = GatherTopWeapons(map, selectedPawn);
            data.contextPawn = selectedPawn;

            data.colonistLists = GatherColonistLists(map);

            return data;
        }

        private static List<TopWeaponInfo> GatherTopWeapons(Map map, Pawn forPawn = null)
        {
            int currentTick = Find.TickManager.TicksGame;

            if (cachedTopWeapons != null &&
                currentTick - lastGatherTick < CACHE_DURATION &&
                cachedForPawn == forPawn)
            {
                return cachedTopWeapons;
            }

            var groundWeapons = new List<ThingWithComps>(256);
            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

            for (int i = 0; i < allWeapons.Count; i++)
            {
                if (allWeapons[i] is ThingWithComps weapon &&
                    weapon.Spawned &&
                    !weapon.ParentHolder.IsEnclosingContainer() &&
                    WeaponValidation.IsWeapon(weapon) &&
                    !CompBiocodable.IsBiocoded(weapon))
                {
                    groundWeapons.Add(weapon);
                }
            }

            if (forPawn != null)
            {
                var jobGiver = new JobGiver_PickUpBetterWeapon();
                var currentWeapon = forPawn.equipment?.Primary;

                var filteredWeapons = new List<ThingWithComps>(groundWeapons.Count);
                for (int i = 0; i < groundWeapons.Count; i++)
                {
                    if (jobGiver.ShouldConsiderWeapon(forPawn, groundWeapons[i], currentWeapon, false))
                    {
                        filteredWeapons.Add(groundWeapons[i]);
                    }
                }
                groundWeapons = filteredWeapons;
            }

            var weaponScores = new List<(ThingWithComps weapon, float baseScore, float pawnScore)>();
            foreach (var weapon in groundWeapons)
            {
                float baseScore = WeaponScoringHelper.GetWeaponPropertyScore(null, weapon);
                float pawnScore = forPawn != null
                    ? WeaponCacheManager.GetCachedScore(forPawn, weapon)
                    : baseScore;
                weaponScores.Add((weapon, baseScore, pawnScore));
            }

            weaponScores.Sort((a, b) =>
            {
                float scoreA = forPawn != null ? a.pawnScore : a.baseScore;
                float scoreB = forPawn != null ? b.pawnScore : b.baseScore;
                return scoreB.CompareTo(scoreA);
            });

            int takeCount = Math.Min(5, weaponScores.Count);
            var result = new List<TopWeaponInfo>(takeCount);
            for (int i = 0; i < takeCount; i++)
            {
                var w = weaponScores[i];
                result.Add(new TopWeaponInfo
                {
                    weapon = w.weapon,
                    baseScore = w.baseScore,
                    pawnScore = w.pawnScore,
                    isRanged = w.weapon.def.IsRangedWeapon,
                    isForbidden = Find.FactionManager?.OfPlayer != null && w.weapon.IsForbidden(Faction.OfPlayer)
                });
            }

            cachedTopWeapons = result;
            cachedForPawn = forPawn;
            lastGatherTick = currentTick;

            return result;
        }

        /// <summary>
        /// Top weapons for pawn
        /// 60-tick TTL cache
        /// </summary>
        public static List<TopWeaponInfo> GetTopWeapons(Map map, Pawn pawn, int limit = 10)
        {
            if (map == null || pawn == null) return new List<TopWeaponInfo>();

            string cacheKey = $"TopWeapons_{map.uniqueID}_{pawn.thingIDNumber}_{limit}";

            return GenericCache.GetCached(cacheKey, () =>
            {
                return ComputeTopWeapons(map, pawn, limit);
            }, cacheDuration: 60);
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
                    WeaponValidation.IsWeapon(weapon) &&
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

            var weaponScores = new List<(ThingWithComps weapon, float baseScore, float pawnScore)>();
            foreach (var weapon in filteredWeapons)
            {
                float baseScore = WeaponScoringHelper.GetWeaponPropertyScore(null, weapon);
                float pawnScore = WeaponCacheManager.GetCachedScore(pawn, weapon);
                weaponScores.Add((weapon, baseScore, pawnScore));
            }

            return weaponScores
                .OrderByDescending(w => w.pawnScore)
                .Take(limit)
                .Select(w => new TopWeaponInfo
                {
                    weapon = w.weapon,
                    baseScore = w.baseScore,
                    pawnScore = w.pawnScore,
                    isRanged = w.weapon.def.IsRangedWeapon,
                    isForbidden = Find.FactionManager?.OfPlayer != null && w.weapon.IsForbidden(Faction.OfPlayer)
                })
                .ToList();
        }

        private static ColonistListsInfo GatherColonistLists(Map map)
        {
            int currentTick = Find.TickManager.TicksGame;
            if (cachedColonistLists != null && currentTick - lastColonistListCacheTick < CACHE_DURATION)
            {
                return cachedColonistLists;
            }

            var colonists = new List<Pawn>(map.mapPawns.FreeColonists);
            colonists.Sort((a, b) =>
            {
                string nameA = a.Name?.ToStringShort ?? "Unknown";
                string nameB = b.Name?.ToStringShort ?? "Unknown";
                return nameA.CompareTo(nameB);
            });

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
            bool isValid = global::AutoArm.Jobs.Jobs.IsValidPawn(pawn, out reason);

            bool raidActive = ModInit.IsLargeRaidActive && (AutoArmMod.settings?.disableDuringRaids ?? false);
            if (raidActive)
            {
                isValid = false;
                reason = "Raid active";
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
                info.weaponBaseScore = WeaponScoringHelper.GetWeaponPropertyScore(null, info.primaryWeapon);
                info.weaponScore = WeaponScoringHelper.GetTotalScore(pawn, info.primaryWeapon);
                info.weaponBonded = ValidationHelper.IsWeaponBondedToPawn(info.primaryWeapon, pawn);
            }

            if (SimpleSidearmsCompat.IsLoaded && pawn.inventory?.innerContainer != null)
            {
                int sidearmCount = 0;
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    var thing = pawn.inventory.innerContainer[i];
                    if (thing is ThingWithComps && thing.def.IsWeapon)
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
            info.isTemp = global::AutoArm.Jobs.Jobs.IsTemporary(pawn);

            if (ModsConfig.IdeologyActive && pawn.Ideo != null)
            {
                var role = pawn.Ideo.GetRole(pawn);
                if (role?.apparelRequirements != null)
                {
                    foreach (var req in role.apparelRequirements)
                    {
                        if (req == null) continue;
                        var reqTypeName = req.GetType().Name;
                        if (reqTypeName.Contains("NoRanged")) info.hasNoRanged = true;
                        if (reqTypeName.Contains("NoMelee")) info.hasNoMelee = true;
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

            return info;
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
        }
    }

    public static class StatusOverviewRenderer
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
        private static string testResultsText = "";

        private static System.Reflection.FieldInfo cachedRootPosField = null;

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
        private const int WEAPON_CACHE_DURATION = 60;

        public static void OnWindowOpened()
        {
        }

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

            cachedData = null;
            lastUpdateTick = -1;

            StatusOverviewDataGatherer.ClearCaches();

        }

        public static void DrawStatusOverview(Rect rect)
        {
            isGatheringDebugData = true;

            var map = Find.CurrentMap;
            if (map == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "Error: No active map");
                Text.Anchor = TextAnchor.UpperLeft;
                isGatheringDebugData = false;
                return;
            }

            float buttonHeight = 30f;
            Rect statusOverviewButtonRect = new Rect(rect.x + 5f, rect.y + 2f, 130f, 26f);
            Rect weaponScoresButtonRect = new Rect(rect.x + 140f, rect.y + 2f, 130f, 26f);
            Rect runTestsButtonRect = new Rect(rect.x + 275f, rect.y + 2f, 130f, 26f);

            if (Widgets.ButtonText(statusOverviewButtonRect, "Status Overview", currentView == ViewMode.StatusOverview))
            {
                currentView = ViewMode.StatusOverview;
            }
            if (Widgets.ButtonText(weaponScoresButtonRect, "Weapon Scores", currentView == ViewMode.WeaponScores))
            {
                currentView = ViewMode.WeaponScores;
            }
            if (Widgets.ButtonText(runTestsButtonRect, "Run All Tests", currentView == ViewMode.TestResults))
            {
                testResultsText = "Running tests...\n";
                currentView = ViewMode.TestResults;

                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                LongEventHandler.QueueLongEvent(() =>
                {
                    var results = AutoArm.Testing.TestRunner.RunAllTests(map);
                    testResultsText = FormatTestResults(results);
                }, "", false, null);
            }

            rect.y += buttonHeight + 5f;
            rect.height -= buttonHeight + 5f;

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
                    var targetPos = cameraFollowTarget.Position.ToVector3Shifted();

                    if (cachedRootPosField == null)
                    {
                        cachedRootPosField = typeof(CameraDriver).GetField("rootPos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    }

                    var cameraDriver = Find.CameraDriver;
                    if (cachedRootPosField != null)
                    {
                        var currentPos = (Vector3)cachedRootPosField.GetValue(cameraDriver);
                        targetPos.y = currentPos.y;

                        float distance = Vector3.Distance(currentPos, targetPos);
                        float lerpSpeed = Mathf.Lerp(0.15f, 0.35f, Mathf.Clamp01(distance / 20f));

                        var smoothPos = Vector3.Lerp(currentPos, targetPos, lerpSpeed);
                        cachedRootPosField.SetValue(cameraDriver, smoothPos);
                    }
                    else
                    {
                        CameraJumper.TryJump(cameraFollowTarget);
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

        private static float CalculateContentHeight(StatusOverviewData data)
        {
            float height = 0f;

            if (currentView == ViewMode.WeaponScores)
            {
                var map = Find.CurrentMap;
                if (map != null)
                {
                    int weaponCount = WeaponCacheManager.GetCacheWeaponCount(map);

                    height = 40f + (weaponCount * LINE_HEIGHT * 0.8f) + 100f;
                }
                return height;
            }

            if (currentView == ViewMode.TestResults)
            {
                int lineCount = testResultsText.Split('\n').Length;
                return 40f + (lineCount * LINE_HEIGHT * 0.6f) + 100f;
            }

            var allColonists = new List<ColonistInfo>();
            allColonists.AddRange(data.colonistLists.validActive);
            allColonists.AddRange(data.colonistLists.validBusy);
            allColonists.AddRange(data.colonistLists.invalid);

            if (data.colonistLists.validActive.Any())
            {
                height += 40f;
                foreach (var colonist in data.colonistLists.validActive)
                {
                    height += LINE_HEIGHT * 1.4f;
                    if (expandedPawn == colonist.pawn)
                        height += (LINE_HEIGHT * 3) + 30f;
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
                        height += (LINE_HEIGHT * 3) + 30f;
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
                        height += (LINE_HEIGHT * 3) + 30f;
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


        private static void DrawTopWeaponsSection(Listing_Standard listing, List<TopWeaponInfo> topWeapons, Pawn contextPawn = null)
        {
            if (!topWeapons.Any())
            {
                var emptyRect = listing.GetRect(30f);
                string emptyMessage = contextPawn != null
                    ? $"No valid weapons found for {contextPawn.Name?.ToStringShort ?? "this colonist"}"
                    : "No weapons on ground or storage";
                Widgets.Label(emptyRect, emptyMessage);
                return;
            }

            GUI.color = new Color(1f, 1f, 0.8f);
            var headerRect = listing.GetRect(24f);
            string headerText = contextPawn != null
                ? $"TOP 3 WEAPONS FOR {contextPawn.Name?.ToStringShort?.ToUpper() ?? "COLONIST"} (by pawn score)"
                : "TOP 3 AVAILABLE WEAPONS (by base score)";
            Widgets.Label(headerRect, headerText);
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(headerRect.x, headerRect.yMax, headerRect.width);
            listing.Gap(4f);

            var consolidatedWeapons = new List<ConsolidatedWeaponInfo>();
            var weaponGroups = new Dictionary<string, ConsolidatedWeaponInfo>();

            foreach (var weaponInfo in topWeapons)
            {
                QualityCategory quality = QualityCategory.Normal;
                int qualityPercent = 0;
                if (weaponInfo.weapon.TryGetQuality(out quality))
                {
                    string label = weaponInfo.weapon.Label;
                    int percentIndex = label.IndexOf('%');
                    if (percentIndex > 0)
                    {
                        int startIndex = percentIndex - 1;
                        while (startIndex > 0 && char.IsDigit(label[startIndex - 1]))
                            startIndex--;
                        if (int.TryParse(label.Substring(startIndex, percentIndex - startIndex), out int parsed))
                            qualityPercent = parsed;
                    }
                }

                string groupKey = $"{weaponInfo.weapon.def.defName}_{quality}";

                if (!weaponGroups.ContainsKey(groupKey))
                {
                    var consolidatedInfo = new ConsolidatedWeaponInfo
                    {
                        weaponDef = weaponInfo.weapon.def,
                        quality = quality,
                        minQualityPercent = qualityPercent,
                        maxQualityPercent = qualityPercent,
                        count = 1,
                        representativeWeapon = weaponInfo.weapon,
                        averageScore = contextPawn != null ? weaponInfo.pawnScore : weaponInfo.baseScore,
                        isRanged = weaponInfo.isRanged,
                        isForbidden = weaponInfo.isForbidden
                    };
                    weaponGroups[groupKey] = consolidatedInfo;
                    consolidatedWeapons.Add(consolidatedInfo);
                }
                else
                {
                    var existing = weaponGroups[groupKey];
                    existing.count++;
                    existing.minQualityPercent = Math.Min(existing.minQualityPercent, qualityPercent);
                    existing.maxQualityPercent = Math.Max(existing.maxQualityPercent, qualityPercent);
                    float newScore = contextPawn != null ? weaponInfo.pawnScore : weaponInfo.baseScore;
                    existing.averageScore = Math.Max(existing.averageScore, newScore);
                }
            }

            int rank = 1;
            foreach (var consolidated in consolidatedWeapons.Take(3))
            {
                var lineRect = listing.GetRect(LINE_HEIGHT);

                var rankRect = new Rect(lineRect.x, lineRect.y, 15f, lineRect.height);
                Widgets.Label(rankRect, $"{rank}.");

                string icon = consolidated.isRanged ? "⚡" : "⚔";
                Color iconColor = consolidated.isRanged ? new Color(1f, 0.7f, 0.7f) : new Color(0.7f, 0.7f, 1f);
                GUI.color = iconColor;
                var iconRect = new Rect(lineRect.x + 18f, lineRect.y, 25f, lineRect.height);
                Widgets.Label(iconRect, icon);
                GUI.color = Color.white;

                var nameRect = new Rect(lineRect.x + 38f, lineRect.y, 272f, lineRect.height);
                string weaponLabel = consolidated.weaponDef.label;

                if (consolidated.quality != QualityCategory.Normal || consolidated.maxQualityPercent > 0)
                {
                    if (consolidated.count > 1 && consolidated.minQualityPercent != consolidated.maxQualityPercent)
                    {
                        weaponLabel += $" ({consolidated.quality.GetLabel()} {consolidated.minQualityPercent}-{consolidated.maxQualityPercent}%)";
                    }
                    else if (consolidated.maxQualityPercent > 0)
                    {
                        weaponLabel += $" ({consolidated.quality.GetLabel()} {consolidated.maxQualityPercent}%)";
                    }
                    else
                    {
                        weaponLabel += $" ({consolidated.quality.GetLabel()})";
                    }
                }

                if (consolidated.count > 1)
                {
                    weaponLabel += $" [{consolidated.count}x]";
                }

                if (consolidated.isForbidden)
                {
                    GUI.color = Color.gray;
                    weaponLabel += " [FORBIDDEN]";
                }
                Widgets.Label(nameRect, weaponLabel);
                GUI.color = Color.white;

                var scoreRect = new Rect(lineRect.x + 320f, lineRect.y, 100f, lineRect.height);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(scoreRect, $"{consolidated.averageScore:F0}");
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(lineRect))
                {
                    CameraJumper.TryJump(consolidated.representativeWeapon.Position, consolidated.representativeWeapon.Map);
                    Messages.Message($"Jumped to {consolidated.representativeWeapon.Label}",
                        new LookTargets(consolidated.representativeWeapon), MessageTypeDefOf.NeutralEvent, false);
                }

                string tooltip = $"{weaponLabel}\n";
                if (consolidated.count > 1)
                {
                    tooltip += $"{consolidated.count} variants found\n";
                }
                tooltip += $"Position: {consolidated.representativeWeapon.Position}\n";
                tooltip += $"\nScore: {consolidated.averageScore:F1}\n";

                if (contextPawn != null)
                {
                    var breakdown = AutoArm.Weapons.WeaponScoringHelper.GetScoreBreakdown(contextPawn, consolidated.representativeWeapon);
                    tooltip += "\nBreakdown:\n";
                    tooltip += $"  Base weapon: {breakdown.baseWeaponScore:F0}\n";
                    if (breakdown.skillScore != 0)
                        tooltip += $"  Skill bonus: {breakdown.skillScore:F0}\n";
                    if (breakdown.hunterScore != 0)
                        tooltip += $"  Hunter bonus: {breakdown.hunterScore:F0}\n";
                    if (breakdown.personaScore != 0)
                        tooltip += $"  Persona bonus: {breakdown.personaScore:F0}\n";
                    if (breakdown.skillMismatchMultiplier != 1.0f)
                        tooltip += $"  Skill multiplier: x{breakdown.skillMismatchMultiplier:F2}\n";
                    if (breakdown.ceAmmoModifier != 1.0f)
                        tooltip += $"  CE ammo modifier: x{breakdown.ceAmmoModifier:F2}\n";
                }

                tooltip += "\nClick to jump to weapon";
                TooltipHandler.TipRegion(lineRect, tooltip);

                rank++;
            }
        }

        private static void DrawColonistListsSection(Listing_Standard listing, ColonistListsInfo lists, Map map)
        {
            if (lists.validActive.Any())
            {
                DrawColonistList(listing, "Valid & active", lists.validActive, new Color(0.7f, 1f, 0.7f), map);
                listing.Gap(SECTION_GAP);
            }

            if (lists.validBusy.Any())
            {
                DrawColonistList(listing, "Valid but busy", lists.validBusy, new Color(0.8f, 0.8f, 1f), map);
                listing.Gap(SECTION_GAP);
            }

            if (lists.invalid.Any())
            {
                DrawColonistList(listing, "Invalid", lists.invalid, new Color(1f, 0.8f, 0.8f), map);
            }
        }

        private static void DrawColonistList(Listing_Standard listing, string title, List<ColonistInfo> colonists, Color titleColor, Map map)
        {
            GUI.color = titleColor;
            var headerRect = listing.GetRect(22f);
            Widgets.Label(headerRect, $"{title} ({colonists.Count})");
            GUI.color = Color.white;

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
                var topWeapons = StatusOverviewDataGatherer.GetTopWeapons(map, colonist.pawn, 10);
                int weaponCount = Math.Min(topWeapons.Count(), 3);

                if (weaponCount == 0)
                {
                    expandedWeaponsHeight = LINE_HEIGHT * 1.5f;
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

            if (Widgets.ButtonInvisible(new Rect(lineRect.x, lineRect.y, lineRect.width, baseHeight)))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(colonist.pawn);
                cameraFollowTarget = colonist.pawn;

                if (expandedPawn != colonist.pawn)
                {
                    expandedPawn = colonist.pawn;

                    expandedPawnTargetScreenY = lineRect.y - scrollPosition.y;

                    userIsManuallyScrolling = false;
                    lastAutoScrollPosition = scrollPosition;
                }
            }

            if (Mouse.IsOver(new Rect(lineRect.x, lineRect.y, lineRect.width, baseHeight)))
            {
                Widgets.DrawHighlight(new Rect(lineRect.x, lineRect.y, lineRect.width, baseHeight));
            }

            float y = lineRect.y;

            var line1Rect = new Rect(lineRect.x, y, lineRect.width, LINE_HEIGHT);

            string statusIcon = colonist.isValid ? "✓" : "✗";
            Color statusColor = colonist.isValid ? new Color(0.7f, 1f, 0.7f) : new Color(1f, 0.7f, 0.7f);
            GUI.color = statusColor;
            Widgets.Label(new Rect(line1Rect.x, line1Rect.y, 20f, line1Rect.height), statusIcon);
            GUI.color = Color.white;

            string name = colonist.pawn.Name?.ToStringShort ?? "Unknown";
            float nameWidth = Text.CalcSize(name).x;
            Widgets.Label(new Rect(line1Rect.x + 25f, line1Rect.y, nameWidth, line1Rect.height), name);

            string tags = BuildTagString(colonist);
            if (!string.IsNullOrEmpty(tags))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                float tagX = line1Rect.x + 25f + nameWidth + 2f;
                Widgets.Label(new Rect(tagX, line1Rect.y, 150f, line1Rect.height), tags);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            string weaponText = "None";
            if (colonist.primaryWeapon != null)
            {
                weaponText = $"{colonist.primaryWeapon.Label} ({colonist.weaponScore:F0})";
                if (colonist.weaponBonded)
                {
                    weaponText += " bonded";
                }
                if (colonist.sidearmCount > 0)
                {
                    weaponText += $" +{colonist.sidearmCount}";
                }
            }
            else
            {
                GUI.color = new Color(1f, 0.7f, 0.5f);
            }

            var weaponRect = new Rect(line1Rect.x + 235f, line1Rect.y, 420f, line1Rect.height);
            Widgets.Label(weaponRect, weaponText);
            GUI.color = Color.white;

            y += LINE_HEIGHT * 0.7f;

            var line2Rect = new Rect(lineRect.x + 25f, y, lineRect.width - 25f, LINE_HEIGHT * 0.7f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;

            var line2Parts = new List<string>();

            if (!colonist.isValid && !string.IsNullOrEmpty(colonist.invalidReason))
            {
                GUI.color = new Color(1f, 0.8f, 0.5f);
                line2Parts.Add($"Reason: {colonist.invalidReason}");
            }

            line2Parts.Add($"S:{colonist.shootingSkill} M:{colonist.meleeSkill}");

            if (colonist.primaryWeapon != null)
            {
                line2Parts.Add($"Base: {colonist.weaponBaseScore:F0}");
            }

            if (!string.IsNullOrEmpty(colonist.outfitName))
            {
                line2Parts.Add($"Outfit: {colonist.outfitName} ({colonist.outfitAllowedWeapons} weapons)");
            }

            string line2Text = string.Join(", ", line2Parts);
            Widgets.Label(new Rect(line2Rect.x, line2Rect.y, line2Rect.width, line2Rect.height), line2Text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(new Rect(lineRect.x, lineRect.y, lineRect.width, baseHeight), BuildColonistTooltip(colonist));

            if (isExpanded)
            {
                y += LINE_HEIGHT * 0.7f;
                DrawInlineTopWeapons(listing, colonist.pawn, map, y, lineRect.width);
            }
        }

        private static void DrawInlineTopWeapons(Listing_Standard listing, Pawn pawn, Map map, float startY, float width)
        {
            var topWeapons = StatusOverviewDataGatherer.GetTopWeapons(map, pawn, 10)
                .Where(w => !w.isForbidden)
                .ToList();
            if (!topWeapons.Any())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                var emptyRect = new Rect(25f, startY, width - 25f, LINE_HEIGHT);
                Widgets.Label(emptyRect, "  No valid weapons available on map");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }

            var consolidatedWeapons = new List<ConsolidatedWeaponInfo>();
            var weaponGroups = new Dictionary<string, ConsolidatedWeaponInfo>();

            foreach (var weaponInfo in topWeapons)
            {
                QualityCategory quality = QualityCategory.Normal;
                int qualityPercent = 0;
                if (weaponInfo.weapon.TryGetQuality(out quality))
                {
                    string label = weaponInfo.weapon.Label;
                    int percentIndex = label.IndexOf('%');
                    if (percentIndex > 0)
                    {
                        int startIndex = percentIndex - 1;
                        while (startIndex > 0 && char.IsDigit(label[startIndex - 1]))
                            startIndex--;
                        if (int.TryParse(label.Substring(startIndex, percentIndex - startIndex), out int parsed))
                            qualityPercent = parsed;
                    }
                }

                string groupKey = $"{weaponInfo.weapon.def.defName}_{quality}";

                if (!weaponGroups.ContainsKey(groupKey))
                {
                    var consolidatedInfo = new ConsolidatedWeaponInfo
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
                    };
                    weaponGroups[groupKey] = consolidatedInfo;
                    consolidatedWeapons.Add(consolidatedInfo);
                }
                else
                {
                    var existing = weaponGroups[groupKey];
                    existing.count++;
                    existing.minQualityPercent = Math.Min(existing.minQualityPercent, qualityPercent);
                    existing.maxQualityPercent = Math.Max(existing.maxQualityPercent, qualityPercent);
                    existing.averageScore = Math.Max(existing.averageScore, weaponInfo.pawnScore);
                }
            }

            Text.Font = GameFont.Tiny;
            var headerRect = new Rect(25f, startY, width - 25f, LINE_HEIGHT * 0.7f);

            Widgets.DrawBoxSolid(headerRect, new Color(0.2f, 0.25f, 0.3f, 0.3f));

            GUI.color = new Color(0.8f, 0.9f, 1f);
            Widgets.Label(new Rect(headerRect.x + 4f, headerRect.y + 2f, headerRect.width - 4f, headerRect.height),
                $"TOP 3 WEAPONS FOR {pawn.Name.ToStringShort.ToUpper()}");
            GUI.color = Color.white;

            Text.Font = GameFont.Small;

            float y = startY + LINE_HEIGHT * 0.8f;

            int rank = 1;
            foreach (var consolidated in consolidatedWeapons.Take(3))
            {
                var weaponLineRect = new Rect(35f, y, width - 35f, LINE_HEIGHT * 0.8f);

                string icon = consolidated.isRanged ? "⚡" : "⚔";
                Color iconColor = consolidated.isRanged ? new Color(1f, 0.7f, 0.7f) : new Color(0.7f, 0.7f, 1f);

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
                    GUI.color = Color.gray;
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
                    Messages.Message($"Jumped to {consolidated.representativeWeapon.Label}",
                        new LookTargets(consolidated.representativeWeapon), MessageTypeDefOf.NeutralEvent, false);
                }

                string tooltip = $"{weaponLabel}\n";
                if (consolidated.count > 1)
                    tooltip += $"{consolidated.count} variants found\n";
                tooltip += $"Position: {consolidated.representativeWeapon.Position}\n";
                tooltip += $"\nScore: {consolidated.averageScore:F1}\n";

                var breakdown = AutoArm.Weapons.WeaponScoringHelper.GetScoreBreakdown(pawn, consolidated.representativeWeapon);
                tooltip += "\nBreakdown:\n";
                tooltip += $"  Base weapon: {breakdown.baseWeaponScore:F0}\n";
                if (breakdown.skillScore != 0)
                    tooltip += $"  Skill bonus: {breakdown.skillScore:F0}\n";
                if (breakdown.hunterScore != 0)
                    tooltip += $"  Hunter bonus: {breakdown.hunterScore:F0}\n";
                if (breakdown.personaScore != 0)
                    tooltip += $"  Persona bonus: {breakdown.personaScore:F0}\n";
                if (breakdown.skillMismatchMultiplier != 1.0f)
                    tooltip += $"  Skill multiplier: x{breakdown.skillMismatchMultiplier:F2}\n";
                if (breakdown.ceAmmoModifier != 1.0f)
                    tooltip += $"  CE ammo modifier: x{breakdown.ceAmmoModifier:F2}\n";

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
            List<WeaponGroupInfo> weaponGroups;

            if (cachedWeaponGroups != null && currentTick - lastWeaponGroupCacheTick < WEAPON_CACHE_DURATION)
            {
                weaponGroups = cachedWeaponGroups;
            }
            else
            {
                var weapons = new List<ThingWithComps>();
                var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

                for (int i = 0; i < allWeapons.Count; i++)
                {
                    if (allWeapons[i] is ThingWithComps weapon &&
                        weapon.Spawned &&
                        !weapon.ParentHolder.IsEnclosingContainer() &&
                        WeaponValidation.IsWeapon(weapon) &&
                        !CompBiocodable.IsBiocoded(weapon))
                    {
                        weapons.Add(weapon);
                    }
                }

                var tempGroups = weapons
                    .GroupBy(w => new
                    {
                        label = w.Label,
                        isForbidden = w.IsForbidden(Faction.OfPlayer),
                        isRanged = w.def.IsRangedWeapon
                    })
                    .Select(g => new WeaponGroupInfo
                    {
                        label = g.Key.label,
                        count = g.Count(),
                        isRanged = g.Key.isRanged,
                        isForbidden = g.Key.isForbidden,
                        baseScore = WeaponScoringHelper.GetWeaponPropertyScore(null, g.First()),
                        firstWeapon = g.First()
                    })
                    .OrderByDescending(g => g.baseScore)
                    .ToList();

                cachedWeaponGroups = tempGroups;
                lastWeaponGroupCacheTick = currentTick;
                weaponGroups = tempGroups;
            }

            int totalWeapons = 0;
            for (int i = 0; i < weaponGroups.Count; i++)
            {
                totalWeapons += weaponGroups[i].count;
            }
            var headerRect = listing.GetRect(22f);
            Widgets.Label(headerRect, $"All Weapons on Map ({totalWeapons})");
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
                    Messages.Message($"Jumped to {weaponInfo.label}",
                        new LookTargets(weaponInfo.firstWeapon), MessageTypeDefOf.NeutralEvent, false);
                }

                if (Mouse.IsOver(lineRect))
                {
                    Widgets.DrawHighlight(lineRect);
                }

                GUI.color = weaponInfo.isForbidden ? new Color(0.6f, 0.6f, 0.6f) : Color.white;

                string weaponLabel = weaponInfo.label;
                if (weaponInfo.count > 1)
                {
                    weaponLabel = $"{weaponLabel} [{weaponInfo.count}]";
                }

                float nameWidth = Text.CalcSize(weaponLabel).x;
                Widgets.Label(new Rect(lineRect.x, lineRect.y, nameWidth, lineRect.height), weaponLabel);

                float currentX = lineRect.x + nameWidth + 2f;

                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;

                if (weaponInfo.isForbidden)
                {
                    Widgets.Label(new Rect(currentX, lineRect.y, 100f, lineRect.height), "[forbidden]");
                    currentX += Text.CalcSize("[forbidden]").x + 2f;
                }

                string weaponType = weaponInfo.isRanged ? "[ranged]" : "[melee]";
                Widgets.Label(new Rect(currentX, lineRect.y, 100f, lineRect.height), weaponType);

                Text.Font = GameFont.Small;

                GUI.color = Color.white;
                string scoreText = $"Score: {weaponInfo.baseScore:F0}";
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(lineRect.x, lineRect.y, lineRect.width, lineRect.height), scoreText);
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private static void DrawTestResultsSection(Listing_Standard listing)
        {
            var headerRect = listing.GetRect(22f);
            Widgets.Label(headerRect, "Test Results");
            Widgets.DrawLineHorizontal(headerRect.x, headerRect.yMax, headerRect.width);
            listing.Gap(2f);

            Text.Font = GameFont.Tiny;
            var textRect = listing.GetRect(testResultsText.Split('\n').Length * LINE_HEIGHT * 0.6f);
            Widgets.Label(textRect, testResultsText);
            Text.Font = GameFont.Small;
        }

        private static string FormatTestResults(AutoArm.Testing.TestResults results)
        {
            if (results == null)
                return "No test results available.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Note: All weapons on the map are destroyed during tests");
            sb.AppendLine();
            sb.AppendLine($"Total Tests: {results.TotalTests}");
            sb.AppendLine($"Passed: {results.PassedTests}");
            sb.AppendLine($"Failed: {results.FailedTests}");
            sb.AppendLine($"Success Rate: {results.SuccessRate:P}");
            sb.AppendLine();

            if (results.FailedTests > 0)
            {
                sb.AppendLine("Failed Tests:");
                sb.AppendLine("-------------");
                var failedTests = results.GetFailedTests();
                foreach (var kvp in failedTests)
                {
                    sb.AppendLine($"  X {kvp.Key}");
                    if (!string.IsNullOrEmpty(kvp.Value.FailureReason))
                    {
                        sb.AppendLine($"    Reason: {kvp.Value.FailureReason}");
                    }
                }
                sb.AppendLine();
            }

            if (results.PassedTests > 0)
            {
                sb.AppendLine("Passed Tests:");
                sb.AppendLine("-------------");
                var passedTests = results.GetPassedTests();
                foreach (var kvp in passedTests)
                {
                    sb.AppendLine($"  ✓ {kvp.Key}");
                }
            }

            return sb.ToString();
        }

        private static string BuildTagString(ColonistInfo colonist)
        {
            var tags = new List<string>();

            if (colonist.isHunter) tags.Add("hunter");
            if (colonist.isBrawler) tags.Add("brawler");
            if (colonist.isTemp) tags.Add("temp");
            if (colonist.hasNoRanged) tags.Add("no ranged");
            if (colonist.hasNoMelee) tags.Add("no melee");
            if (colonist.age > 0 && colonist.age < colonist.minAge)
                tags.Add($"age: {colonist.age}/{colonist.minAge}");

            return tags.Any() ? $"[{string.Join("] [", tags)}]" : "";
        }

        private static string BuildColonistTooltip(ColonistInfo colonist)
        {
            string tooltip = $"{colonist.pawn.Name?.ToStringFull ?? "Unknown"}\n\n";

            tooltip += $"Skills: Shooting {colonist.shootingSkill}, Melee {colonist.meleeSkill}\n";

            if (colonist.primaryWeapon != null)
            {
                tooltip += $"\nWeapon: {colonist.primaryWeapon.Label}\n";

                var breakdown = AutoArm.Weapons.WeaponScoringHelper.GetScoreBreakdown(colonist.pawn, colonist.primaryWeapon);

                if (breakdown.isForced)
                {
                    tooltip += "  Forced weapon (locked)\n";
                }

                tooltip += $"  Total Score: {breakdown.totalScore:F0}\n";
                tooltip += "\n  Breakdown:\n";
                tooltip += $"    Base weapon: {breakdown.baseWeaponScore:F0}\n";

                if (breakdown.skillScore != 0)
                    tooltip += $"    Skill bonus: {breakdown.skillScore:F0}\n";
                if (breakdown.hunterScore != 0)
                    tooltip += $"    Hunter bonus: {breakdown.hunterScore:F0}\n";
                if (breakdown.personaScore != 0)
                    tooltip += $"    Persona bonus: {breakdown.personaScore:F0}\n";
                if (breakdown.skillMismatchMultiplier != 1.0f)
                    tooltip += $"    Skill multiplier: x{breakdown.skillMismatchMultiplier:F2}\n";
                if (breakdown.ceAmmoModifier != 1.0f)
                    tooltip += $"    CE ammo modifier: x{breakdown.ceAmmoModifier:F2}\n";
                if (breakdown.outfitPolicyScore < 0)
                    tooltip += $"    Outfit penalty: {breakdown.outfitPolicyScore:F0}\n";

                if (colonist.weaponBonded)
                    tooltip += "\n  Bonded to this pawn\n";
            }
            else
            {
                tooltip += "\nWeapon: None (unarmed)\n";
            }

            if (colonist.sidearmCount > 0)
            {
                tooltip += $"Sidearms: {colonist.sidearmCount}\n";
            }

            if (!string.IsNullOrEmpty(colonist.outfitName))
            {
                tooltip += $"\nOutfit: {colonist.outfitName}\n";
                tooltip += $"Allows {colonist.outfitAllowedWeapons} weapon types\n";
            }

            if (!colonist.isValid && !string.IsNullOrEmpty(colonist.invalidReason))
            {
                tooltip += $"\nInvalid: {colonist.invalidReason}\n";
            }

            return tooltip;
        }
    }
}

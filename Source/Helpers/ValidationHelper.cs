
using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AutoArm.Helpers
{
    internal static class ValidationHelper
    {
        private static readonly System.Collections.Generic.Dictionary<System.Type, bool> ritualLordTypeCache = new System.Collections.Generic.Dictionary<System.Type, bool>();

        private static readonly System.Collections.Generic.Dictionary<JobDef, bool> ritualJobDefCache = new System.Collections.Generic.Dictionary<JobDef, bool>();
        private static readonly System.Collections.Generic.Dictionary<JobDef, bool> haulingJobDefCache = new System.Collections.Generic.Dictionary<JobDef, bool>();

        private const int MaxRitualLordTypeCacheSize = 500;

        private const int MaxJobDefCacheSize = 1000;

        private static readonly string[] RitualLordJobKeywords =
        {
            "Ritual", "Gathering", "Party", "Ceremony", "Speech",
            "Festival", "Celebration", "Skylantern", "Marriage", "Funeral",
            "Date", "Lovin", "Bestowing", "Advent", "Trial",
            "Dance", "Drum", "Sacrifice", "Concert", "Venerate",
            "Chant", "Reimplant", "Sanguophage"
        };

        private static readonly string[] RitualJobDefKeywords =
        {
            "ritual", "spectate", "ceremony", "attendparty", "gatheringparticipate",
            "standandbesociallyactive", "hold", "carry", "deliver", "bring",
            "dance", "drum", "chant", "pray"
        };

        private static readonly string[] RitualCarriedDefKeywords =
        {
            "WoodLog", "Lantern", "RitualItem", "Skylantern", "Effigy", "Pyre"
        };

        private static void EvictOldest<TKey>(System.Collections.Generic.Dictionary<TKey, bool> cache, int count)
        {
            if (cache.Count == 0 || count <= 0) return;
            var toRemove = ListPool<TKey>.Get(count);
            int taken = 0;
            foreach (var key in cache.Keys)
            {
                if (taken >= count) break;
                toRemove.Add(key);
                taken++;
            }
            for (int i = 0; i < toRemove.Count; i++)
                cache.Remove(toRemove[i]);
            ListPool<TKey>.Return(toRemove);
        }

        private static bool MatchesAny(string name, string[] keywords, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (ignoreCase
                    ? name.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0
                    : name.Contains(keywords[i]))
                    return true;
            }
            return false;
        }

        public static bool PassesAgeGate(Pawn pawn, out string reason)
        {
            reason = null;
            if (!ModsConfig.BiotechActive) return true;

            bool isRaceAdult = pawn.ageTracker?.Adult == true;
            if (isRaceAdult) return true;

            bool sliderActive = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
            var devStage = pawn.DevelopmentalStage;

            if (!sliderActive)
            {
                if (devStage < DevelopmentalStage.Child)
                {
                    reason = "Too young to equip weapons";
                    return false;
                }
                return true;
            }

            int minAge = AutoArmMod.settings?.childrenMinAge ?? Constants.ChildDefaultMinAge;
            int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
            if (age < minAge)
            {
                reason = $"Too young ({age} < {minAge})";
                return false;
            }
            return true;
        }

        internal static bool IsHaulingOrInventoryJob(JobDef jobDef)
        {
            if (jobDef == null) return false;

            if (haulingJobDefCache.TryGetValue(jobDef, out var cached))
                return cached;

            bool isHaul = false;

            var dc = jobDef.driverClass;
            if (dc != null)
            {
                if (typeof(JobDriver_HaulToCell).IsAssignableFrom(dc) ||
                    typeof(JobDriver_HaulToContainer).IsAssignableFrom(dc) ||
                    typeof(JobDriver_HaulToTransporter).IsAssignableFrom(dc))
                {
                    isHaul = true;
                }
            }

            if (!isHaul)
            {
                if (jobDef == AutoArmDefOf.HaulToInventory ||
                    jobDef == AutoArmDefOf.UnloadYourHauledInventory ||
                    jobDef == AutoArmDefOf.UnloadYourInventory)
                {
                    isHaul = true;
                }
            }

            if (!isHaul && jobDef.defName != null)
            {
                var name = jobDef.defName;
                if (name.IndexOf("Haul", System.StringComparison.Ordinal) >= 0 ||
                    name.IndexOf("Inventory", System.StringComparison.Ordinal) >= 0)
                {
                    isHaul = true;
                }
            }

            if (haulingJobDefCache.Count >= MaxJobDefCacheSize)
            {
                AutoArmLogger.Debug(() => $"Hauling job cache exceeded {MaxJobDefCacheSize} entries, clearing");
                haulingJobDefCache.Clear();
            }

            haulingJobDefCache[jobDef] = isHaul;
            return isHaul;
        }

        public static bool TryGetIdeologyWeaponBlock(ThingWithComps weapon, Pawn pawn, out string reason, out IdeoWeaponDisposition? disposition)
        {
            reason = null;
            disposition = null;

            if (weapon?.def == null || pawn == null)
                return false;
            if (!ModsConfig.IdeologyActive)
                return false;

            var ideo = pawn.Ideo;
            if (ideo != null)
            {
                var weaponDisposition = ideo.GetDispositionForWeapon(weapon.def);
                disposition = weaponDisposition;

                if (weaponDisposition == IdeoWeaponDisposition.Despised)
                {
                    reason = "Despised by ideology";
                    return true;
                }
            }

            return false;
        }


        public static bool SafeIsColonist(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (pawn.IsColonist)
                return true;

            if (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony)
                return true;

            return false;
        }

        public static bool IsInRitual(Pawn pawn)
        {
            if (pawn?.Map?.lordManager == null)
                return false;

            var lord = pawn.GetLord();
            if (lord?.LordJob != null)
            {
                var lordJobType = lord.LordJob.GetType();
                if (ritualLordTypeCache.TryGetValue(lordJobType, out var isRitualLord))
                {
                    if (isRitualLord) return true;
                }
                else
                {
                    bool determined = false;
                    var currentType = lordJobType;
                    while (currentType != null && currentType != typeof(object))
                    {
                        if (MatchesAny(currentType.Name, RitualLordJobKeywords, ignoreCase: false))
                        {
                            determined = true;
                            break;
                        }
                        currentType = currentType.BaseType;
                    }
                    if (ritualLordTypeCache.Count >= MaxRitualLordTypeCacheSize)
                    {
                        EvictOldest(ritualLordTypeCache, MaxRitualLordTypeCacheSize / 10);
                    }

                    ritualLordTypeCache[lordJobType] = determined;
                    if (determined) return true;
                }
            }

            if (pawn.CurJobDef != null)
            {
                if (ritualJobDefCache.TryGetValue(pawn.CurJobDef, out var isRitualJob))
                {
                    if (isRitualJob) return true;
                }
                else
                {
                    bool jobMatches = MatchesAny(pawn.CurJobDef.defName, RitualJobDefKeywords, ignoreCase: true);
                    if (ritualJobDefCache.Count >= MaxJobDefCacheSize)
                    {
                        EvictOldest(ritualJobDefCache, MaxJobDefCacheSize / 10);
                    }

                    ritualJobDefCache[pawn.CurJobDef] = jobMatches;
                    if (jobMatches) return true;
                }
            }

            if (pawn.carryTracker?.CarriedThing != null &&
                MatchesAny(pawn.carryTracker.CarriedThing.def?.defName, RitualCarriedDefKeywords, ignoreCase: false))
            {
                return true;
            }

            return false;
        }

    }
}

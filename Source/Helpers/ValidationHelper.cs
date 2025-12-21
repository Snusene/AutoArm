
using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Logging;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse;
using Verse.AI;

namespace AutoArm.Helpers
{
    public static class ValidationHelper
    {
        private static readonly System.Collections.Generic.Dictionary<System.Type, bool> ritualLordTypeCache = new System.Collections.Generic.Dictionary<System.Type, bool>();

        private static readonly System.Collections.Generic.Dictionary<JobDef, bool> ritualJobDefCache = new System.Collections.Generic.Dictionary<JobDef, bool>();
        private static readonly System.Collections.Generic.Dictionary<JobDef, bool> haulingJobDefCache = new System.Collections.Generic.Dictionary<JobDef, bool>();

        // Per-def ideology cache
        private static readonly System.Collections.Generic.Dictionary<(int ideoId, int defHash), IdeoWeaponDisposition> ideoDispositionCache =
            new System.Collections.Generic.Dictionary<(int, int), IdeoWeaponDisposition>();

        private const int MaxRitualLordTypeCacheSize = 500;

        private const int MaxJobDefCacheSize = 1000;
        private const int MaxIdeoDispositionCacheSize = 500;

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
                    name.IndexOf("Carry", System.StringComparison.Ordinal) >= 0 ||
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

        /// <summary>
        /// Ideology despised
        /// </summary>
        public static bool IsDespisedByIdeology(ThingWithComps weapon, Pawn pawn)
        {
            string ideologyReason;
            IdeoWeaponDisposition? disposition;
            return TryGetIdeologyWeaponBlock(weapon, pawn, out ideologyReason, out disposition)
                && disposition.HasValue && disposition.Value == IdeoWeaponDisposition.Despised;
        }

        /// <summary>
        /// Ideology blocks
        /// </summary>
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
                try
                {
                    // Cache per (ideoId, defHash)
                    var cacheKey = (ideo.id, weapon.def.shortHash);
                    IdeoWeaponDisposition weaponDisposition;

                    if (!ideoDispositionCache.TryGetValue(cacheKey, out weaponDisposition))
                    {
                        weaponDisposition = ideo.GetDispositionForWeapon(weapon.def);

                        if (ideoDispositionCache.Count >= MaxIdeoDispositionCacheSize)
                            ideoDispositionCache.Clear();

                        ideoDispositionCache[cacheKey] = weaponDisposition;
                    }

                    disposition = weaponDisposition;

                    if (weaponDisposition == IdeoWeaponDisposition.Despised)
                    {
                        reason = "Despised by ideology";
                        return true;
                    }
                }
                catch (Exception ex) when (ex is MissingMethodException || ex is TypeLoadException || ex is MissingFieldException)
                {
                    AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Ideology weapon disposition check failed: {ex.GetType().Name} - {ex.Message}");
                    disposition = null;
                }
            }

            return false;
        }

        /// <summary>
        /// Clear ideology cache
        /// </summary>
        public static void ClearIdeologyDispositionCache()
        {
            ideoDispositionCache.Clear();
        }

        /// <summary>
        /// Full pawn validation
        /// </summary>
        public static bool IsValidPawn(Pawn pawn, out string reason, bool checkForWeapons = true, bool fromJobGiver = false)
        {
            reason = "";

            if (pawn == null)
            {
                reason = "Pawn is null";
                return false;
            }

            if (pawn.Map == null)
            {
                reason = "Map is null";
                return false;
            }

            if (!pawn.Position.IsValid || !pawn.Position.InBounds(pawn.Map))
            {
                reason = "Invalid position";
                return false;
            }

            if (!fromJobGiver)
            {
                if (pawn.RaceProps == null)
                {
                    reason = "No race properties";
                    return false;
                }

                if (pawn.RaceProps.Animal)
                {
                    reason = "Is animal";
                    return false;
                }

                if (pawn.RaceProps.IsMechanoid)
                {
                    reason = "Is mechanoid";
                    return false;
                }

                if (!pawn.RaceProps.ToolUser)
                {
                    reason = "Cannot use tools";
                    return false;
                }

                if (pawn.RaceProps.intelligence < Intelligence.ToolUser)
                {
                    reason = "Intelligence too low";
                    return false;
                }

                if (pawn.health?.capacities == null)
                {
                    reason = "No health capacities";
                    return false;
                }

                if (!pawn.Spawned)
                {
                    reason = "Not spawned";
                    return false;
                }

                if (pawn.Dead || pawn.Destroyed)
                {
                    reason = "Dead or destroyed";
                    return false;
                }

                if (pawn.Downed)
                {
                    reason = "Downed";
                    return false;
                }

                if (pawn.Drafted)
                {
                    reason = "Drafted";
                    return false;
                }

                if (pawn.InMentalState)
                {
                    reason = "In mental state";
                    return false;
                }

                if (pawn.InBed())
                {
                    reason = "In bed";
                    return false;
                }

                if (pawn.IsPrisoner)
                {
                    reason = "Is prisoner";
                    return false;
                }

                if (!SafeIsColonist(pawn))
                {
                    if (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony)
                    {
                    }
                    else
                    {
                        reason = "Not a colonist";
                        return false;
                    }
                }

                if (pawn.outfits == null)
                {
                    reason = "Temporary faction member (no outfit policy)";
                    return false;
                }

                if (ModsConfig.RoyaltyActive && pawn.IsQuestLodger())
                {
                    reason = "Quest lodger with locked equipment";
                    return false;
                }

                if (global::AutoArm.Jobs.Jobs.IsTemporary(pawn))
                {
                    if (QuestUtility.IsReservedByQuestOrQuestBeingGenerated(pawn))
                    {
                        reason = "Quest-reserved pawn";
                        return false;
                    }

                    if (!(AutoArmMod.settings?.allowTemporaryColonists ?? false))
                    {
                        reason = "Temporary colonist (quest/borrowed) - not allowed";
                        return false;
                    }
                }

                if (checkForWeapons && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    reason = "Incapable of violence";
                    return false;
                }

                if (pawn.Map?.lordManager != null)
                {
                    var lord = pawn.Map.lordManager.LordOf(pawn);
                    if (lord != null)
                    {
                        if (!(lord.LordJob is LordJob_DefendBase ||
                              lord.LordJob is LordJob_AssistColony))
                        {
                            reason = $"In lord job: {lord.LordJob.GetType().Name}";
                            return false;
                        }
                    }
                }

                if (IsInRitual(pawn))
                {
                    reason = "In ritual or ceremony";
                    return false;
                }

                if (pawn.CurJob != null && IsHaulingOrInventoryJob(pawn.CurJob.def))
                {
                    reason = "Currently hauling";
                    return false;
                }

                if (pawn.IsCaravanMember())
                {
                    reason = "In caravan";
                    return false;
                }


                if (ModsConfig.BiotechActive)
                {
                    bool isRaceAdult = pawn.ageTracker?.Adult == true;
                    var devStage = pawn.DevelopmentalStage;
                    bool sliderActive = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;

                    if (!sliderActive)
                    {
                        // Match vanilla: Child and Adult can equip, only Baby blocked
                        if (devStage < DevelopmentalStage.Child)
                        {
                            reason = "Too young to equip weapons";
                            return false;
                        }
                    }
                    else
                    {
                        // Slider active: apply minAge restriction
                        int minAge = AutoArmMod.settings?.childrenMinAge ?? Constants.ChildDefaultMinAge;
                        int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                        if (!isRaceAdult && age < minAge)
                        {
                            reason = $"Too young ({age} < {minAge})";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Colonist or slave of colony?
        /// </summary>
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

        /// <summary>
        /// Bonded check
        /// </summary>
        public static bool IsWeaponBondedToPawn(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || pawn == null)
                return false;

            if (pawn.equipment?.bondedWeapon == weapon)
                return true;

            if (CompBiocodable.IsBiocodedFor(weapon, pawn))
            {
                var bladelinkComp = Caching.Components.GetBladelink(weapon);
                if (bladelinkComp != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Pawn in ritual or ceremony?
        /// </summary>
        public static bool IsInRitual(Pawn pawn)
        {
            if (pawn?.Map?.lordManager == null)
                return false;

            var lord = pawn.Map.lordManager.LordOf(pawn);
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
                        var typeName = currentType.Name;
                        if (typeName.Contains("Ritual") ||
                            typeName.Contains("Gathering") ||
                            typeName.Contains("Party") ||
                            typeName.Contains("Ceremony") ||
                            typeName.Contains("Speech") ||
                            typeName.Contains("Festival") ||
                            typeName.Contains("Celebration") ||
                            typeName.Contains("Skylantern") ||
                            typeName.Contains("Marriage") ||
                            typeName.Contains("Funeral") ||
                            typeName.Contains("Date") ||
                            typeName.Contains("Lovin") ||
                            typeName.Contains("Bestowing") ||
                            typeName.Contains("Advent") ||
                            typeName.Contains("Trial") ||
                            typeName.Contains("Dance") ||
                            typeName.Contains("Drum") ||
                            typeName.Contains("Sacrifice"))
                        {
                            determined = true;
                            break;
                        }
                        currentType = currentType.BaseType;
                    }
                    if (ritualLordTypeCache.Count >= MaxRitualLordTypeCacheSize)
                    {
                        AutoArmLogger.Debug(() => $"Ritual lord type cache exceeded {MaxRitualLordTypeCacheSize} entries, clearing");
                        ritualLordTypeCache.Clear();
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
                    bool jobMatches = false;
                    var name = pawn.CurJobDef.defName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        jobMatches = name.IndexOf("ritual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("spectate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("ceremony", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("attendparty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("gatheringparticipate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("standandbesociallyactive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("carry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("deliver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("bring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("dance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("drum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("chant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("pray", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    if (ritualJobDefCache.Count >= MaxJobDefCacheSize)
                    {
                        AutoArmLogger.Debug(() => $"Ritual job def cache exceeded {MaxJobDefCacheSize} entries, clearing");
                        ritualJobDefCache.Clear();
                    }

                    ritualJobDefCache[pawn.CurJobDef] = jobMatches;
                    if (jobMatches) return true;
                }
            }

            if (pawn.carryTracker?.CarriedThing != null)
            {
                var carried = pawn.carryTracker.CarriedThing;
                var defName = carried.def?.defName;
                if (!string.IsNullOrEmpty(defName))
                {
                    if (defName.Contains("WoodLog") ||
                        defName.Contains("Lantern") ||
                        defName.Contains("RitualItem") ||
                        defName.Contains("Skylantern") ||
                        defName.Contains("Effigy") ||
                        defName.Contains("Pyre"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Pawn prefers melee
        /// </summary>
        public static bool PrefersMelee(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                return true;

            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;

            return meleeSkill > shootingSkill + 3f;
        }
    }
}

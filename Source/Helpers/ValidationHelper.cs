using RimWorld;
using System;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld.Planet;

namespace AutoArm
{
    /// <summary>
    /// Centralized validation logic to eliminate redundancy
    /// </summary>
    public static class ValidationHelper
    {
        // Cache for storage type checks (fixes #23)
        private static readonly System.Collections.Generic.HashSet<Type> knownStorageTypes = new System.Collections.Generic.HashSet<Type>();
        private static readonly System.Collections.Generic.HashSet<Type> knownNonStorageTypes = new System.Collections.Generic.HashSet<Type>();

        /// <summary>
        /// Consolidated weapon validation (fixes #1, #15)
        /// </summary>
        public static bool IsValidWeapon(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";

            // Basic validation
            if (weapon == null)
            {
                reason = "Weapon is null";
                return false;
            }

            if (weapon.def == null)
            {
                reason = "Weapon def is null";
                return false;
            }

            if (!WeaponValidation.IsProperWeapon(weapon))
            {
                reason = "Not a proper weapon";
                return false;
            }

            if (weapon.Destroyed)
            {
                reason = "Weapon destroyed";
                return false;
            }

            if (weapon.Map == null || weapon.Map != pawn.Map)
            {
                reason = "Wrong map";
                return false;
            }

            // Outfit filter check
            if (pawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;
                if (!filter.Allows(weapon.def))
                {
                    reason = "Not allowed by outfit";
                    return false;
                }
            }

            // Forbidden check
            if (weapon.IsForbidden(pawn))
            {
                reason = "Forbidden";
                return false;
            }

            // Biocode check
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
            {
                reason = "Biocoded to another pawn";
                return false;
            }

            // Quest item check
            if (weapon.questTags != null && weapon.questTags.Count > 0)
            {
                reason = "Quest item";
                return false;
            }

            // Reservation check
            var reservationManager = pawn.Map?.reservationManager;
            if (reservationManager != null)
            {
                if (reservationManager.IsReservedByAnyoneOf(weapon, pawn.Faction))
                {
                    if (!reservationManager.CanReserve(pawn, weapon, 1, -1, null, false))
                    {
                        reason = "Reserved by someone else";
                        return false;
                    }
                }
            }

            // Reachability check
            if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, false))
            {
                reason = "Cannot reach";
                return false;
            }

            // Container check
            if (weapon.ParentHolder != null)
            {
                if (weapon.ParentHolder is Pawn_EquipmentTracker equipTracker)
                {
                    var equipperPawn = equipTracker.pawn;
                    if (equipperPawn != null && equipperPawn != pawn)
                    {
                        reason = "Equipped by someone else";
                        return false;
                    }
                }
                else if (weapon.ParentHolder is Pawn_InventoryTracker)
                {
                    reason = "In someone's inventory";
                    return false;
                }
                else if (weapon.ParentHolder is MinifiedThing)
                {
                    reason = "Minified";
                    return false;
                }
                else if (!(weapon.ParentHolder is Map) && !IsStorageContainer(weapon.ParentHolder))
                {
                    reason = "In non-storage container";
                    return false;
                }
            }

            // Status checks
            if (weapon.IsBurning())
            {
                reason = "On fire";
                return false;
            }

            // Research check
            if (weapon.def.researchPrerequisites != null)
            {
                foreach (var research in weapon.def.researchPrerequisites)
                {
                    if (research != null && !research.IsFinished)
                    {
                        reason = $"Research not complete: {research.label}";
                        return false;
                    }
                }
            }

            // SimpleSidearms compatibility checks
            if (SimpleSidearmsCompat.IsLoaded())
            {
                if (SimpleSidearmsCompat.ShouldSkipDangerousWeapons() && JobGiverHelpers.IsExplosiveWeapon(weapon.def))
                {
                    reason = "Dangerous weapon (SimpleSidearms setting)";
                    return false;
                }
                
                if (SimpleSidearmsCompat.ShouldSkipEMPWeapons() && JobGiverHelpers.IsEMPWeapon(weapon.def))
                {
                    reason = "EMP weapon (SimpleSidearms setting)";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Consolidated pawn validation (fixes #3, #16, #24)
        /// </summary>
        public static bool IsValidPawn(Pawn pawn, out string reason, bool checkForWeapons = true)
        {
            reason = "";

            // Basic null and state checks
            if (pawn == null)
            {
                reason = "Pawn is null";
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

            if (pawn.Map == null)
            {
                reason = "No map";
                return false;
            }

            if (!pawn.Position.IsValid || !pawn.Position.InBounds(pawn.Map))
            {
                reason = "Invalid position";
                return false;
            }

            // Race checks
            if (!pawn.RaceProps.Humanlike)
            {
                reason = "Not humanlike";
                return false;
            }

            if (pawn.RaceProps.IsMechanoid)
            {
                reason = "Is mechanoid";
                return false;
            }

            // Status checks
            if (pawn.Downed)
            {
                reason = "Downed";
                return false;
            }

            if (pawn.InMentalState)
            {
                reason = "In mental state";
                return false;
            }

            if (pawn.IsPrisoner)
            {
                reason = "Is prisoner";
                return false;
            }

            if (pawn.Drafted)
            {
                reason = "Drafted";
                return false;
            }

            if (pawn.InBed())
            {
                reason = "In bed";
                TimingHelper.LogWithCooldown(pawn, "Invalid pawn: In bed", TimingHelper.CooldownType.InBed);
                return false;
            }

            // Faction and colonist checks
            if (!SafeIsColonist(pawn))
            {
                reason = "Not a colonist";
                return false;
            }

            // Violence capability check (only for weapon-related validation)
            if (checkForWeapons && pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                reason = "Incapable of violence";
                TimingHelper.LogWithCooldown(pawn, "Invalid pawn: Incapable of violence", TimingHelper.CooldownType.IncapableOfViolence);
                return false;
            }

            // Health checks
            if (pawn.health?.capacities == null)
            {
                reason = "No health capacities";
                return false;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                reason = "Cannot manipulate";
                return false;
            }

            // Lord job checks
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

            // Caravan check
            if (pawn.IsCaravanMember())
            {
                reason = "In caravan";
                return false;
            }

            // Temporary colonist check
            if (JobGiverHelpers.IsTemporaryColonist(pawn))
            {
                if (!(AutoArmMod.settings?.allowTemporaryColonists ?? false))
                {
                    reason = "Temporary colonist (quest/borrowed)";
                    return false;
                }
            }

            // Age check for Biotech
            if (ModsConfig.BiotechActive && pawn.ageTracker != null)
            {
                if (pawn.ageTracker.AgeBiologicalYears < 18)
                {
                    if (!(AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false))
                    {
                        reason = $"Children not allowed to equip weapons ({pawn.ageTracker.AgeBiologicalYears} years old)";
                        return false;
                    }
                    
                    int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;
                    if (pawn.ageTracker.AgeBiologicalYears < minAge)
                    {
                        reason = $"Too young ({pawn.ageTracker.AgeBiologicalYears} < {minAge})";
                        return false;
                    }
                }
            }

            // Conceited noble check - prevent weapon switching if they already have one
            if (checkForWeapons && (AutoArmMod.settings?.respectConceitedNobles ?? true))
            {
                if (IsConceited(pawn) && pawn.equipment?.Primary != null)
                {
                    reason = "Conceited noble won't switch weapons";
                    AutoArmDebug.LogPawn(pawn, "Conceited noble/trait - keeping current weapon");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Consolidated colonist check (fixes #2)
        /// </summary>
        public static bool SafeIsColonist(Pawn pawn)
        {
            if (pawn == null)
                return false;
                
            try
            {
                return pawn.IsColonist;
            }
            catch (Exception ex)
            {
                // Some modded pawns might throw when checking IsColonist
                Log.Warning($"[AutoArm] Error checking IsColonist for {pawn?.Label ?? "unknown pawn"}: {ex.Message}");
                
                // Fallback checks
                try
                {
                    return pawn.Faction == Faction.OfPlayer && pawn.IsFreeColonist;
                }
                catch
                {
                    try
                    {
                        return pawn.Faction == Faction.OfPlayer && !pawn.IsPrisoner;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Check if pawn can use a specific weapon
        /// </summary>
        public static bool CanPawnUseWeapon(Pawn pawn, ThingWithComps weapon, out string reason)
        {
            reason = "";

            if (!IsValidPawn(pawn, out reason))
                return false;

            if (!IsValidWeapon(weapon, pawn, out reason))
                return false;

            // Trait checks
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
            {
                reason = "Brawler won't use ranged weapon";
                return false;
            }

            // Hunter explosive check
            if (pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hunting) == true &&
                weapon.def.IsRangedWeapon && JobGiverHelpers.IsExplosiveWeapon(weapon.def))
            {
                reason = "Hunter won't use explosive weapon";
                return false;
            }

            // Skill requirement check
            if (weapon.def.equippedStatOffsets != null)
            {
                foreach (var statOffset in weapon.def.equippedStatOffsets)
                {
                    if (statOffset.stat == StatDefOf.ShootingAccuracyPawn && statOffset.value < 0)
                    {
                        float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
                        if (shootingSkill < 5f && Math.Abs(statOffset.value) > 0.2f)
                        {
                            reason = "Pawn shooting skill too low for weapon";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Check if a pawn is conceited (has conceited title or traits)
        /// </summary>
        public static bool IsConceited(Pawn pawn)
        {
            if (pawn == null)
                return false;

            // Check for conceited royal titles (Royalty DLC)
            if (ModsConfig.RoyaltyActive && pawn.royalty != null)
            {
                foreach (var title in pawn.royalty.AllTitlesInEffectForReading)
                {
                    if (title.conceited)
                        return true;
                }
            }

            // Check for conceited traits
            if (pawn.story?.traits != null)
            {
                // Greedy trait
                var greedyTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Greedy");
                if (greedyTrait != null && pawn.story.traits.HasTrait(greedyTrait))
                    return true;

                // Jealous trait  
                var jealousTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Jealous");
                if (jealousTrait != null && pawn.story.traits.HasTrait(jealousTrait))
                    return true;

                // Abrasive trait
                var abrasiveTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Abrasive");  
                if (abrasiveTrait != null && pawn.story.traits.HasTrait(abrasiveTrait))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Consolidated storage container check (fixes #23)
        /// </summary>
        private static bool IsStorageContainer(IThingHolder holder)
        {
            if (holder == null) return false;

            var holderType = holder.GetType();

            // Check cache first
            if (knownStorageTypes.Contains(holderType))
                return true;
            if (knownNonStorageTypes.Contains(holderType))
                return false;

            bool isStorage = false;

            // Check if it's a building with storage
            if (holder is Building building)
            {
                if (building.def.building?.fixedStorageSettings != null || 
                    building.GetSlotGroup() != null)
                {
                    isStorage = true;
                }
                else if (building.def.thingClass?.Name == "Building_Storage" ||
                         building.def.defName.ToLower().Contains("shelf") ||
                         building.def.defName.ToLower().Contains("rack"))
                {
                    isStorage = true;
                }
                else
                {
                    // Check for deep storage comp
                    foreach (var comp in building.AllComps)
                    {
                        if (comp?.GetType().Name == "CompDeepStorage")
                        {
                            isStorage = true;
                            break;
                        }
                    }
                }
            }

            // Check if it's a stockpile
            if (holder is Zone_Stockpile)
            {
                isStorage = true;
            }

            // Check base types
            if (!isStorage)
            {
                var baseType = holderType.BaseType;
                while (baseType != null && baseType != typeof(object))
                {
                    if (baseType.Name == "Building_Storage")
                    {
                        isStorage = true;
                        break;
                    }
                    baseType = baseType.BaseType;
                }
            }

            // Check type name patterns
            if (!isStorage)
            {
                var holderName = holderType.Name.ToLower();
                var holderNamespace = holderType.Namespace?.ToLower() ?? "";

                isStorage = holderName.Contains("storage") ||
                           holderName.Contains("rack") ||
                           holderName.Contains("locker") ||
                           holderName.Contains("cabinet") ||
                           holderName.Contains("armory") ||
                           holderName.Contains("shelf") ||
                           holderNamespace.Contains("storage") ||
                           holderNamespace.Contains("weaponstorage");
            }

            // Cache the result
            if (isStorage)
                knownStorageTypes.Add(holderType);
            else
                knownNonStorageTypes.Add(holderType);

            return isStorage;
        }
    }
}

using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AutoArm
{
    public static class JobGiverHelpers
    {
        // HashSets for job categorization
        private static readonly HashSet<JobDef> AlwaysCriticalJobs = new HashSet<JobDef>
        {
            // Medical/Emergency
            JobDefOf.TendPatient,
            JobDefOf.Rescue,
            JobDefOf.ExtinguishSelf,
            JobDefOf.BeatFire,
            JobDefOf.GotoSafeTemperature,
            
            // Combat
            JobDefOf.AttackMelee,
            JobDefOf.AttackStatic,
            JobDefOf.Hunt,
            JobDefOf.ManTurret,
            JobDefOf.Wait_Combat,
            JobDefOf.FleeAndCower,
            JobDefOf.Reload,
            
            // Critical personal needs
            JobDefOf.Vomit,
            JobDefOf.LayDown,
            JobDefOf.Lovin,
            JobDefOf.Ingest,
            
            // Transportation
            JobDefOf.EnterTransporter,
            JobDefOf.EnterCryptosleepCasket,
            
            // Prison/Security
            JobDefOf.Arrest,
            JobDefOf.Capture,
            JobDefOf.EscortPrisonerToBed,
            JobDefOf.TakeWoundedPrisonerToBed,
            JobDefOf.ReleasePrisoner,
            JobDefOf.Kidnap,
            JobDefOf.CarryDownedPawnToExit,
            JobDefOf.CarryToCryptosleepCasket,
            
            // Trading
            JobDefOf.TradeWithPawn,
            
            // Communications
            JobDefOf.UseCommsConsole,
            
            // Equipment handling
            JobDefOf.DropEquipment
        };

        private static readonly HashSet<JobDef> ConditionalCriticalJobs = new HashSet<JobDef>
        {
            JobDefOf.Sow,
            JobDefOf.Harvest,
            JobDefOf.HaulToCell,
            JobDefOf.HaulToContainer,
            JobDefOf.DoBill,
            JobDefOf.FinishFrame,
            JobDefOf.SmoothFloor,
            JobDefOf.Mine,
            JobDefOf.Refuel,
            JobDefOf.Research
        };

        private static readonly HashSet<JobDef> AlwaysSafeToInterrupt = new HashSet<JobDef>
        {
            JobDefOf.Wait,
            JobDefOf.Wait_Wander,
            JobDefOf.GotoWander
        };

        // Storage type caching
        private static readonly HashSet<Type> knownStorageTypes = new HashSet<Type>();
        private static readonly HashSet<Type> knownNonStorageTypes = new HashSet<Type>();

        // Existing methods
        public static bool IsCriticalJob(Pawn pawn, bool hasNoSidearms = false)
        {
            if (pawn.CurJob == null)
                return false;

            var job = pawn.CurJob.def;

            // Always check player-forced
            if (pawn.CurJob.playerForced)
                return true;

            // Check always-critical jobs
            if (AlwaysCriticalJobs.Contains(job))
                return true;

            // Check conditional jobs (always critical now since we removed emergency system)
            if (ConditionalCriticalJobs.Contains(job))
                return true;

            // Check string-based patterns for DLC/modded content
            var defName = job.defName;
            if (defName.Contains("Ritual") ||
                defName.Contains("Surgery") ||
                defName.Contains("Operate") ||
                defName.Contains("Prisoner") ||
                defName.Contains("Mental") ||
                defName.Contains("PrepareCaravan"))
                return true;

            return false;
        }

        public static bool IsSafeToInterrupt(JobDef jobDef)
        {
            if (AlwaysSafeToInterrupt.Contains(jobDef))
                return true;

            var defName = jobDef.defName;
            return defName.StartsWith("Joy") ||
                   defName.Contains("Social");
        }

        // NEW: Pawn validation with detailed reason
        public static bool IsValidPawnForAutoEquip(Pawn pawn, out string reason)
        {
            reason = "";

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

            if (pawn.Dead)
            {
                reason = "Dead";
                return false;
            }

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

            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                reason = "Incapable of violence";
                return false;
            }

            // Null check for health capacities
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

            if (pawn.InBed())
            {
                reason = "In bed";
                return false;
            }

            // More specific lord checks
            var lord = pawn.GetLord();
            if (lord != null)
            {
                // Allow certain lord types
                if (lord.LordJob is LordJob_DefendBase ||
                    lord.LordJob is LordJob_DefendPoint ||
                    lord.LordJob is LordJob_AssistColony)
                {
                    // These are OK
                }
                else
                {
                    reason = $"In lord job: {lord.LordJob.GetType().Name}";
                    return false;
                }
            }

            if (pawn.IsCaravanMember())
            {
                reason = "In caravan";
                return false;
            }

            // Biotech age check with null safety
            if (ModsConfig.BiotechActive && pawn.ageTracker != null)
            {
                int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;
                if (!(AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false) &&
                    pawn.ageTracker.AgeBiologicalYears < minAge)
                {
                    reason = $"Too young ({pawn.ageTracker.AgeBiologicalYears} < {minAge})";
                    return false;
                }
            }

            // Royalty conceited check with null safety
            if (ModsConfig.RoyaltyActive && (AutoArmMod.settings?.respectConceitedNobles ?? true))
            {
                if (pawn.royalty?.AllTitlesInEffectForReading?.Any(t => t.conceited) == true)
                {
                    if (pawn.equipment?.Primary != null)
                    {
                        reason = "Conceited noble already has weapon";
                        return false;
                    }
                }
            }

            return true;
        }

        // Storage container helper method
        // Replace the IsStorageContainer method with this version:
        private static bool IsStorageContainer(IThingHolder holder)
        {
            if (holder == null) return false;

            var holderType = holder.GetType();

            // Check cache first
            if (knownStorageTypes.Contains(holderType))
                return true;
            if (knownNonStorageTypes.Contains(holderType))
                return false;

            // Check if it's a Building with storage
            if (holder is Building building)
            {
                // Check if it has storage settings or a storage group
                if (building.def.building?.fixedStorageSettings != null || // Vanilla storage
                    building.GetSlotGroup() != null) // Has storage slots
                {
                    knownStorageTypes.Add(holderType);
                    return true;
                }

                // Check for shelves specifically
                if (building.def.thingClass?.Name == "Building_Storage" ||
                    building.def.defName.ToLower().Contains("shelf") ||
                    building.def.defName.ToLower().Contains("rack"))
                {
                    knownStorageTypes.Add(holderType);
                    return true;
                }

                // Check for deep storage comp by name (mod compatibility without reference)
                var comps = building.AllComps;
                if (comps != null)
                {
                    foreach (var comp in comps)
                    {
                        if (comp != null && comp.GetType().Name == "CompDeepStorage")
                        {
                            knownStorageTypes.Add(holderType);
                            return true;
                        }
                    }
                }
            }

            // Check if it's a vanilla storage zone/stockpile
            if (holder is Zone_Stockpile)
            {
                knownStorageTypes.Add(holderType);
                return true;
            }

            // For shelves and other vanilla storage, check the base type
            var baseType = holderType.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                if (baseType.Name == "Building_Storage")
                {
                    knownStorageTypes.Add(holderType);
                    return true;
                }
                baseType = baseType.BaseType;
            }

            // Fallback to name checking (only once per type)
            var holderName = holderType.Name.ToLower();
            var holderNamespace = holderType.Namespace?.ToLower() ?? "";

            bool isStorage = holderName.Contains("storage") ||
                             holderName.Contains("rack") ||
                             holderName.Contains("locker") ||
                             holderName.Contains("cabinet") ||
                             holderName.Contains("armory") ||
                             holderName.Contains("shelf") ||
                             holderNamespace.Contains("storage") ||
                             holderNamespace.Contains("weaponstorage");

            // Cache the result
            if (isStorage)
                knownStorageTypes.Add(holderType);
            else
                knownNonStorageTypes.Add(holderType);

            return isStorage;
        }

        // NEW: Weapon validation with detailed reason
        public static bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";

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

            if (!weapon.def.IsWeapon)
            {
                reason = "Not a weapon";
                return false;
            }

            if (weapon.def.IsApparel)
            {
                reason = "Is apparel";
                return false;
            }

            // Check for heavy weapon extension (handle null)
            if (weapon.def.modExtensions != null)
            {
                foreach (var extension in weapon.def.modExtensions)
                {
                    if (extension?.GetType().Name == "HeavyWeapon")
                    {
                        reason = "Heavy weapon";
                        return false;
                    }
                }
            }

            // Outfit filter check with null safety
            if (pawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;

                // Special vanilla animal weapons bypass
                if (weapon.def.defName == "ElephantTusk" || weapon.def.defName == "ThrumboHorn")
                {
                    // Allow these
                }
                else if (!filter.Allows(weapon.def))
                {
                    reason = "Not allowed by outfit";
                    return false;
                }
            }

            if (weapon.IsForbidden(pawn))
            {
                reason = "Forbidden";
                return false;
            }

            // Biocodable check with null safety
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true)
            {
                if (biocomp.CodedPawn != pawn)
                {
                    reason = "Biocoded to another pawn";
                    return false;
                }
            }

            // Quest item check
            if (weapon.questTags != null && weapon.questTags.Count > 0)
            {
                reason = "Quest item";
                return false;
            }

            // Reservation check with null safety
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

            // Parent holder check with storage compatibility
            if (weapon.ParentHolder != null)
            {
                // Debug log what the parent holder actually is
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {weapon.Label} parent holder type: {weapon.ParentHolder.GetType().FullName}");
                    Log.Message($"[AutoArm] Spawned: {weapon.Spawned}, Position: {weapon.Position}");
                }

                // Quick checks for common cases first
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
                // Check if it's a map (weapons on ground)
                else if (weapon.ParentHolder is Map)
                {
                    // Weapons on the ground have Map as parent holder - this is fine!
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {weapon.Label} is on the ground (Map parent)");
                    }
                }
                // Check if it's in storage
                else if (!IsStorageContainer(weapon.ParentHolder))
                {
                    reason = "In non-storage container";
                    return false;
                }
            }

            if (weapon.IsBurning())
            {
                reason = "On fire";
                return false;
            }

            // Research check with null safety
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

            return true;
        }

        // NEW: Safe weapon DPS calculation
        public static float GetRangedWeaponDPS(ThingDef weaponDef, ThingWithComps weapon = null)
        {
            if (weaponDef?.Verbs == null || weaponDef.Verbs.Count == 0)
                return 0f;

            var verb = weaponDef.Verbs[0];
            if (verb == null)
                return 0f;

            // Get damage amount
            float damage = 0f;
            if (verb.defaultProjectile?.projectile != null)
            {
                // GetDamageAmount returns int, so just cast it
                damage = (float)verb.defaultProjectile.projectile.GetDamageAmount(weapon);
            }

            float warmup = Math.Max(0.1f, verb.warmupTime); // Prevent divide by zero
            float cooldown = weaponDef.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
            float burstShots = Math.Max(1, verb.burstShotCount);

            // Calculate time per salvo
            float timePerSalvo = warmup + cooldown + 0.1f; // Add small buffer

            return (damage * burstShots) / timePerSalvo;
        }

        // NEW: Helper method to check if a weapon is explosive
        public static bool IsExplosiveWeapon(ThingDef weaponDef)
        {
            if (weaponDef?.Verbs == null || weaponDef.Verbs.Count == 0)
                return false;

            var verb = weaponDef.Verbs[0];
            if (verb?.defaultProjectile?.projectile == null)
                return false;

            return verb.defaultProjectile.projectile.explosionRadius > 0f;
        }

        // NEW: Check if pawn can use weapon based on their current state
        public static bool CanPawnUseWeapon(Pawn pawn, ThingWithComps weapon, out string reason)
        {
            reason = "";

            // First check if pawn is valid
            if (!IsValidPawnForAutoEquip(pawn, out reason))
                return false;

            // Then check if weapon is valid
            if (!IsValidWeaponCandidate(weapon, pawn, out reason))
                return false;

            // Additional specific checks

            // Brawler check
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
            {
                reason = "Brawler won't use ranged weapon";
                return false;
            }

            // Hunter explosive check
            if (pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hunting) == true &&
                weapon.def.IsRangedWeapon && IsExplosiveWeapon(weapon.def))
            {
                reason = "Hunter won't use explosive weapon";
                return false;
            }

            // Skill requirements (if any)
            if (weapon.def.equippedStatOffsets != null)
            {
                foreach (var statOffset in weapon.def.equippedStatOffsets)
                {
                    if (statOffset.stat == StatDefOf.ShootingAccuracyPawn && statOffset.value < 0)
                    {
                        // Weapon has accuracy penalty - check if pawn has minimum skill
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

        // NEW: Get a descriptive reason why a pawn won't pick up a weapon
        public static string GetWeaponRejectionReason(Pawn pawn, ThingWithComps weapon)
        {
            string reason;

            if (!IsValidPawnForAutoEquip(pawn, out reason))
                return $"Pawn: {reason}";

            if (!IsValidWeaponCandidate(weapon, pawn, out reason))
                return $"Weapon: {reason}";

            if (!CanPawnUseWeapon(pawn, weapon, out reason))
                return $"Compatibility: {reason}";

            return "Unknown reason";
        }
    }
}
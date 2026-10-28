
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class ShieldBeltBlocksRangedTest : ITestScenario
    {
        public string Name => "Shield belt blocks ranged";
        private Pawn testPawn;
        private ThingWithComps rangedWeapon;
        private Apparel shieldBelt;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var shieldDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_ShieldBelt");
            if (shieldDef != null)
            {
                shieldBelt = ThingMaker.MakeThing(shieldDef) as Apparel;
                if (shieldBelt != null && testPawn.apparel != null)
                {
                    testPawn.apparel.Wear(shieldBelt, dropReplacedApparel: false);
                }
            }

            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (rifleDef != null)
            {
                rangedWeapon = TestHelpers.CreateWeapon(map, rifleDef, testPawn.Position + new IntVec3(2, 0, 0));
                if (rangedWeapon != null)
                    WeaponCache.AddWeaponToCache(rangedWeapon);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Pawn setup failed");
            if (shieldBelt == null)
                return TestResult.Failure("Shield belt def not present (vanilla Royalty required)");
            if (rangedWeapon == null)
                return TestResult.Failure("Ranged weapon setup failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            bool accepted = jobGiver.ShouldConsiderWeapon(testPawn, rangedWeapon, null);

            if (accepted)
                return TestResult.Failure("Shield-belt pawn accepted ranged weapon (C12 regression)");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(rangedWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class ClearForcedPrimaryPreservesSidearmsTest : ITestScenario
    {
        public string Name => "Sidearm pins survive primary unforce";
        private Pawn testPawn;
        private ThingWithComps primary;
        private ThingWithComps sidearm;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var primaryDef = AutoArmDefOf.Gun_AssaultRifle;
            var sidearmDef = AutoArmDefOf.Gun_Autopistol;

            if (primaryDef != null)
            {
                primary = ThingMaker.MakeThing(primaryDef) as ThingWithComps;
                if (primary != null)
                    testPawn.equipment.AddEquipment(primary);
            }

            if (sidearmDef != null)
            {
                sidearm = ThingMaker.MakeThing(sidearmDef) as ThingWithComps;
                if (sidearm != null && testPawn.inventory?.innerContainer != null)
                    testPawn.inventory.innerContainer.TryAdd(sidearm);
            }

            if (primary != null)
                ForcedWeapons.SetForced(testPawn, primary, "test", log: false);
            if (sidearm != null)
                ForcedWeapons.AddSidearm(testPawn, sidearm);
        }

        public TestResult Run()
        {
            if (testPawn == null || primary == null || sidearm == null)
                return TestResult.Failure("Setup incomplete");

            if (!ForcedWeapons.IsForced(testPawn, primary))
                return TestResult.Failure("Primary not forced after setup");
            if (!ForcedWeapons.IsForced(testPawn, sidearm))
                return TestResult.Failure("Sidearm not forced after setup");

            ForcedWeapons.ClearForcedPrimary(testPawn);

            if (ForcedWeapons.IsForced(testPawn, primary))
                return TestResult.Failure("Primary pin still present after ClearForcedPrimary");
            if (!ForcedWeapons.IsForced(testPawn, sidearm))
                return TestResult.Failure("Sidearm pin wiped (EMP-regression from Equipment.cs fix)");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            if (testPawn != null)
                ForcedWeapons.ClearForced(testPawn);
            TestHelpers.SafeDestroyWeapon(primary);
            TestHelpers.SafeDestroyWeapon(sidearm);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class PistolDetectedByRangedLightTest : ITestScenario
    {
        public string Name => "Pistol via RangedLight tag";
        private Pawn testPawn;
        private ThingWithComps pistol;
        private ThingWithComps rifle;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            var pistolDef = AutoArmDefOf.Gun_Autopistol;
            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;

            if (pistolDef != null)
            {
                pistol = TestHelpers.CreateWeapon(map, pistolDef, testPawn.Position + new IntVec3(1, 0, 0));
                if (pistol != null) WeaponCache.AddWeaponToCache(pistol);
            }
            if (rifleDef != null)
            {
                rifle = TestHelpers.CreateWeapon(map, rifleDef, testPawn.Position + new IntVec3(-1, 0, 0));
                if (rifle != null) WeaponCache.AddWeaponToCache(rifle);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || pistol == null || rifle == null)
                return TestResult.Failure("Setup incomplete");

            var rangedLight = DefDatabase<WeaponClassDef>.GetNamedSilentFail("RangedLight");
            if (rangedLight == null)
                return TestResult.Failure("RangedLight weaponClass not present");

            bool pistolHasClass = pistol.def.weaponClasses != null && pistol.def.weaponClasses.Contains(rangedLight);
            bool rifleHasClass = rifle.def.weaponClasses != null && rifle.def.weaponClasses.Contains(rangedLight);

            if (!pistolHasClass)
                return TestResult.Failure("Autopistol missing RangedLight class");
            if (rifleHasClass)
                return TestResult.Failure("AssaultRifle has RangedLight class (unexpected)");

            float pistolScore = Scoring.GetTotalScore(testPawn, pistol);
            float rifleScore = Scoring.GetTotalScore(testPawn, rifle);

            if (rifleScore <= pistolScore)
                return TestResult.Failure($"Rifle should outscore pistol after 0.75x modifier (rifle={rifleScore:F1}, pistol={pistolScore:F1})");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(pistol);
            TestHelpers.SafeDestroyWeapon(rifle);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class OrderedEquipMarksForcedTest : ITestScenario
    {
        public string Name => "Ordered equip marks forced";
        private Pawn testPawn;
        private ThingWithComps weapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (rifleDef != null)
            {
                weapon = TestHelpers.CreateWeapon(map, rifleDef, testPawn.Position + new IntVec3(2, 0, 0));
                if (weapon != null)
                    WeaponCache.AddWeaponToCache(weapon);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapon == null)
                return TestResult.Failure("Setup incomplete");

            if (ForcedWeapons.IsForced(testPawn, weapon))
                return TestResult.Failure("Weapon forced before order");

            var job = JobMaker.MakeJob(JobDefOf.Equip, weapon);
            bool taken = testPawn.jobs?.TryTakeOrderedJob(job, JobTag.Misc) ?? false;

            if (!taken)
                return TestResult.Failure("TryTakeOrderedJob rejected the equip order");

            if (ForcedWeapons.IsForced(testPawn, weapon))
                return TestResult.Failure("Order marked weapon forced before it was equipped");

            if (testPawn.CurJob != job)
                return TestResult.Skip("Ordered job did not start");

            weapon.DeSpawn();
            testPawn.equipment.AddEquipment(weapon);

            if (!ForcedWeapons.IsForced(testPawn, weapon))
                return TestResult.Failure("Equipping the ordered weapon did not mark it forced");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            if (testPawn != null)
            {
                testPawn.jobs?.ClearQueuedJobs();
                testPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                ForcedWeapons.ClearForced(testPawn);
                testPawn.equipment?.DestroyAllEquipment();
            }
            TestHelpers.SafeDestroyWeapon(weapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class UnfinishedOrderKeepsForcedPrimaryTest : ITestScenario
    {
        public string Name => "Unfinished order keeps forced primary";
        private Pawn testPawn;
        private ThingWithComps held;
        private ThingWithComps ordered;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = AutoArmDefOf.Gun_Autopistol;
            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (pistolDef == null || rifleDef == null) return;

            held = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
            if (held != null)
            {
                testPawn.equipment.AddEquipment(held);
                ForcedWeapons.SetForced(testPawn, held, "test");
            }

            ordered = TestHelpers.CreateWeapon(map, rifleDef, testPawn.Position + new IntVec3(4, 0, 0));
            if (ordered != null)
                WeaponCache.AddWeaponToCache(ordered);
        }

        public TestResult Run()
        {
            if (testPawn == null || held == null || ordered == null)
                return TestResult.Failure("Setup incomplete");

            var job = JobMaker.MakeJob(JobDefOf.Equip, ordered);
            bool taken = testPawn.jobs?.TryTakeOrderedJob(job, JobTag.Misc) ?? false;
            if (!taken)
                return TestResult.Failure("TryTakeOrderedJob rejected the equip order");

            testPawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            testPawn.jobs.ClearQueuedJobs();

            if (ForcedWeapons.IsForced(testPawn, ordered))
                return TestResult.Failure("Cancelled order left the ordered weapon forced");

            if (!ForcedWeapons.IsForced(testPawn, held))
                return TestResult.Failure("Cancelled order removed forced status from the held weapon");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            if (testPawn != null)
            {
                testPawn.jobs?.ClearQueuedJobs();
                testPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                ForcedWeapons.ClearForced(testPawn);
                testPawn.equipment?.DestroyAllEquipment();
            }
            TestHelpers.SafeDestroyWeapon(ordered);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class UnarmedWithSameDefSidearmTest : ITestScenario
    {
        public string Name => "Skip duplicate ground sidearm";
        private Pawn testPawn;
        private ThingWithComps inventorySidearm;
        private ThingWithComps groundSameDef;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (!SimpleSidearmsCompat.IsLoaded) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = AutoArmDefOf.Gun_Autopistol;
            if (pistolDef == null) return;

            inventorySidearm = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
            if (inventorySidearm != null && testPawn.inventory?.innerContainer != null)
                testPawn.inventory.innerContainer.TryAdd(inventorySidearm);

            groundSameDef = TestHelpers.CreateWeapon(map, pistolDef, testPawn.Position + new IntVec3(2, 0, 0));
            if (groundSameDef != null)
                WeaponCache.AddWeaponToCache(groundSameDef);
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");

            if (testPawn == null || inventorySidearm == null || groundSameDef == null)
                return TestResult.Failure("Setup incomplete");

            var job = JobHelper.CreateEquipJob(groundSameDef, isSidearm: false, pawn: testPawn);

            if (job != null && job.def == JobDefOf.Equip && job.targetA.Thing == groundSameDef)
                return TestResult.Failure("Unarmed pawn walked to same-def ground weapon when sidearm of same def in inventory (JobHelper regression)");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(groundSameDef);
            TestHelpers.SafeDestroyWeapon(inventorySidearm);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class PrimaryBlocksSameDefSidearmTest : ITestScenario
    {
        public string Name => "Primary blocks same-def sidearm pickup";
        private Pawn testPawn;
        private ThingWithComps primary;
        private ThingWithComps groundSameDef;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (!SimpleSidearmsCompat.IsLoaded) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (rifleDef == null) return;

            primary = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
            if (primary != null)
            {
                primary.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                testPawn.equipment.AddEquipment(primary);
            }

            groundSameDef = TestHelpers.CreateWeapon(map, rifleDef, testPawn.Position + new IntVec3(2, 0, 0));
            if (groundSameDef != null)
            {
                groundSameDef.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);
                WeaponCache.AddWeaponToCache(groundSameDef);
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");

            if (testPawn == null || primary == null || groundSameDef == null)
                return TestResult.Failure("Setup incomplete");

            bool oldAutoEquip = AutoArmMod.settings.autoEquipSidearms;
            AutoArmMod.settings.autoEquipSidearms = true;
            try
            {
                var job = SimpleSidearmsCompat.TryGetWeaponJob(testPawn, groundSameDef, bypassCooldown: true);
                if (job != null)
                    return TestResult.Failure($"SS path created {job.def.defName} for same-def weapon while primary is {primary.def.defName}");
            }
            finally
            {
                AutoArmMod.settings.autoEquipSidearms = oldAutoEquip;
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(groundSameDef);
            if (testPawn != null)
                testPawn.equipment?.DestroyAllEquipment();
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class SidearmBlocksSameDefPrimaryUpgradeTest : ITestScenario
    {
        public string Name => "Sidearm blocks same-def primary upgrade";
        private Pawn testPawn;
        private ThingWithComps primary;
        private ThingWithComps sidearm;
        private ThingWithComps groundUpgrade;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (!SimpleSidearmsCompat.IsLoaded) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = AutoArmDefOf.Gun_Autopistol;
            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (pistolDef == null || rifleDef == null) return;

            primary = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
            if (primary != null)
                testPawn.equipment.AddEquipment(primary);

            sidearm = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
            if (sidearm != null && testPawn.inventory?.innerContainer != null)
            {
                sidearm.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                testPawn.inventory.innerContainer.TryAdd(sidearm);
            }

            groundUpgrade = TestHelpers.CreateWeapon(map, rifleDef, testPawn.Position + new IntVec3(2, 0, 0));
            if (groundUpgrade != null)
            {
                groundUpgrade.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                WeaponCache.AddWeaponToCache(groundUpgrade);
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");

            if (testPawn == null || primary == null || sidearm == null || groundUpgrade == null)
                return TestResult.Failure("Setup incomplete");

            var job = SimpleSidearmsCompat.TryGetWeaponJob(testPawn, groundUpgrade, bypassCooldown: true);

            if (job != null && (job.def == AutoArmDefOf.AutoArmSwapPrimary || job.def == JobDefOf.Equip))
                return TestResult.Failure($"Primary upgrade created ({job.def.defName}) for a def already carried as sidearm");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(groundUpgrade);
            TestHelpers.SafeDestroyWeapon(sidearm);
            if (testPawn != null)
                testPawn.equipment?.DestroyAllEquipment();
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class RetrievalDupeBlockSelfHealsTest : ITestScenario
    {
        public string Name => "Blocked retrieval heals SS memory";
        private Pawn testPawn;
        private ThingWithComps primary;
        private ThingWithComps groundCopy;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (!SimpleSidearmsCompat.IsLoaded) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = AutoArmDefOf.Gun_Autopistol;
            if (pistolDef == null) return;

            primary = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
            if (primary != null)
            {
                testPawn.equipment.AddEquipment(primary);
                SimpleSidearmsCompat.InformOfAddedPrimary(testPawn, primary);
                SimpleSidearmsCompat.InformOfAddedSidearm(testPawn, primary);
            }

            groundCopy = TestHelpers.CreateWeapon(map, pistolDef, testPawn.Position + new IntVec3(2, 0, 0));
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return TestResult.Skip("SimpleSidearms not loaded");

            if (testPawn == null || primary == null || groundCopy == null)
                return TestResult.Failure("Setup incomplete");

            int before = SimpleSidearmsCompat.CountRemembered(testPawn, primary.def);
            if (before < 2)
                return TestResult.Failure($"Memory pollution setup failed (remembered {before}, expected 2+)");

            var job = JobMaker.MakeJob(JobDefOf.Equip, groundCopy);
            JobGiver_RetrieveWeapon_TryGiveJob_Patch.Postfix(testPawn, ref job);

            if (job != null)
                return TestResult.Failure("Retrieval of same-def copy was not blocked");

            int after = SimpleSidearmsCompat.CountRemembered(testPawn, primary.def);
            if (after != before - 1)
                return TestResult.Failure($"Memory did not self-heal (remembered {before} then {after})");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(groundCopy);
            if (testPawn != null)
                testPawn.equipment?.DestroyAllEquipment();
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class PolicyDisallowedPrimaryDropsTest : ITestScenario
    {
        public string Name => "Disallowed primary dropped lazily";
        private Pawn testPawn;
        private ThingWithComps primary;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (rifleDef == null) return;

            primary = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
            if (primary != null)
                testPawn.equipment.AddEquipment(primary);
        }

        public TestResult Run()
        {
            if (testPawn == null || primary == null)
                return TestResult.Failure("Setup incomplete");

            var policy = testPawn.outfits?.CurrentApparelPolicy;
            if (policy?.filter == null)
                return TestResult.Failure("Pawn has no apparel policy");

            var savedFilter = new ThingFilter();
            savedFilter.CopyAllowancesFrom(policy.filter);

            try
            {
                policy.filter.SetAllow(ThingCategoryDefOf.Weapons, false);
                WeaponCache.OnOutfitFilterChanged(policy);

                var jobGiver = new JobGiver_PickUpBetterWeapon();
                var job = jobGiver.TestTryGiveJob(testPawn);

                if (job == null || job.def != JobDefOf.DropEquipment)
                    return TestResult.Failure($"Expected DropEquipment job for disallowed primary, got {job?.def?.defName ?? "null"}");

                if (job.targetA.Thing != primary)
                    return TestResult.Failure("DropEquipment job targets wrong weapon");

                return TestResult.Pass();
            }
            finally
            {
                policy.filter.CopyAllowancesFrom(savedFilter);
                WeaponCache.OnOutfitFilterChanged(policy);
            }
        }

        public void Cleanup()
        {
            if (testPawn != null)
                testPawn.equipment?.DestroyAllEquipment();
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class FactionChangeInvalidatesScoreCacheTest : ITestScenario
    {
        public string Name => "Faction change invalidates score cache";
        private Pawn testPawn;
        private ThingWithComps weapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (rifleDef != null)
            {
                weapon = TestHelpers.CreateWeapon(map, rifleDef, testPawn.Position + new IntVec3(2, 0, 0));
                if (weapon != null) WeaponCache.AddWeaponToCache(weapon);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapon == null)
                return TestResult.Failure("Setup incomplete");

            WeaponCache.GetCachedScore(testPawn, weapon);

            var scoreCacheField = typeof(WeaponCache).GetField("scoreCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (scoreCacheField == null)
                return TestResult.Failure("WeaponCache.scoreCache field not found");

            var outerDict = scoreCacheField.GetValue(null) as System.Collections.IDictionary;
            if (outerDict == null)
                return TestResult.Failure("scoreCache not a dictionary");

            if (!outerDict.Contains(testPawn.thingIDNumber))
                return TestResult.Failure("Cache entry not populated before SetFaction");

            var originalFaction = testPawn.Faction;
            var otherFaction = Find.FactionManager.AllFactions.FirstOrDefault(f =>
                f != originalFaction && f.def.humanlikeFaction && !f.def.isPlayer);
            if (otherFaction == null)
                return TestResult.Skip("No alternate humanlike faction available");

            testPawn.SetFaction(otherFaction);

            bool cleared = !outerDict.Contains(testPawn.thingIDNumber);
            testPawn.SetFaction(originalFaction);

            if (!cleared)
                return TestResult.Failure("Score cache entry not removed by SetFaction_Postfix");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(weapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }
}

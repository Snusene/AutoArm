using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class OutfitFilterPenaltyTest : ITestScenario
    {
        public string Name => "Outfit filter penalty";
        private Pawn testPawn;
        private ThingWithComps rangedWeapon;
        private ApparelPolicy originalPolicy;
        private ApparelPolicy restrictivePolicy;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    rangedWeapon = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                }

                originalPolicy = testPawn.outfits?.CurrentApparelPolicy;

                restrictivePolicy = new ApparelPolicy(testPawn.Map.uniqueID, "Test - Melee Only");

                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat != null)
                {
                    restrictivePolicy.filter.SetAllow(weaponsCat, true);
                }

                foreach (var weaponDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon && d.IsRangedWeapon))
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, false);
                }

                foreach (var weaponDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon && d.IsMeleeWeapon))
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, true);
                }

                Current.Game.outfitDatabase.AllOutfits.Add(restrictivePolicy);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || rangedWeapon == null)
                return TestResult.Failure("Test setup failed");

            if (testPawn.outfits != null)
                testPawn.outfits.CurrentApparelPolicy = restrictivePolicy;

            float disallowedScore = Scoring.GetTotalScore(testPawn, rangedWeapon);

            if (disallowedScore > Constants.OutfitFilterDisallowedPenalty + 1f)
                return TestResult.Failure(
                    $"Disallowed weapon scored {disallowedScore:F1}, expected <= {Constants.OutfitFilterDisallowedPenalty} (outfit filter not applied)");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            if (testPawn?.outfits != null && originalPolicy != null)
            {
                testPawn.outfits.CurrentApparelPolicy = originalPolicy;
            }

            if (restrictivePolicy != null && Current.Game?.outfitDatabase?.AllOutfits != null)
            {
                Current.Game.outfitDatabase.AllOutfits.Remove(restrictivePolicy);
            }

            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(rangedWeapon);
        }
    }

    internal sealed class WeaponBlacklistBasicTest : ITestScenario
    {
        public string Name => "Blacklist basics";
        private Pawn testPawn;
        private ThingDef testWeaponDef;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);
            testWeaponDef = AutoArmDefOf.Gun_BoltActionRifle;
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            Blacklist.AddToBlacklist(testWeaponDef, testPawn, "Test restriction");
            result.Data["AddedToBlacklist"] = Blacklist.IsBlacklisted(testWeaponDef, testPawn);

            if (!Blacklist.IsBlacklisted(testWeaponDef, testPawn))
            {
                result.Success = false;
                result.Data["Error"] = "Weapon not blacklisted after adding";
                AutoArmLogger.Warn($"[TEST] WeaponBlacklistBasicTest: Weapon not blacklisted after adding - expected: true, got: false (weapon: {testWeaponDef.defName})");
            }

            Blacklist.RemoveFromBlacklist(testWeaponDef, testPawn);
            result.Data["RemovedFromBlacklist"] = !Blacklist.IsBlacklisted(testWeaponDef, testPawn);

            if (Blacklist.IsBlacklisted(testWeaponDef, testPawn))
            {
                result.Success = false;
                result.Data["Error2"] = "Weapon still blacklisted after removing";
                AutoArmLogger.Warn($"[TEST] WeaponBlacklistBasicTest: Weapon still blacklisted after removing - expected: false, got: true (weapon: {testWeaponDef.defName})");
            }

            return result;
        }

        public void Cleanup()
        {
            Blacklist.ClearBlacklist(testPawn);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class WeaponBlacklistIntegrationTest : ITestScenario
    {
        public string Name => "Blacklist blocks jobs";
        private Pawn testPawn;
        private ThingWithComps blacklistedWeapon;
        private ThingWithComps normalWeapon;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                blacklistedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_BoltActionRifle,
                    testPawn.Position + new IntVec3(2, 0, 0));

                normalWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(-2, 0, 0));

                if (blacklistedWeapon != null)
                {
                    WeaponCache.AddWeaponToCache(blacklistedWeapon);
                    Blacklist.AddToBlacklist(blacklistedWeapon.def, testPawn, "Integration test");
                }

                if (normalWeapon != null)
                {
                    WeaponCache.AddWeaponToCache(normalWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || blacklistedWeapon == null || normalWeapon == null)
                return TestResult.Failure("Test setup failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
                return TestResult.Failure("No job created; expected equip of normal weapon");

            var target = job.targetA.Thing;
            if (target == blacklistedWeapon)
                return TestResult.Failure($"Job targets blacklisted weapon {blacklistedWeapon.Label}");
            if (target != normalWeapon)
                return TestResult.Failure($"Job targets {target?.Label ?? "null"}, expected {normalWeapon.Label}");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            Blacklist.ClearBlacklist(testPawn);

            TestHelpers.SafeDestroyWeapon(blacklistedWeapon);
            TestHelpers.SafeDestroyWeapon(normalWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class PersonaWeaponBonusTest : ITestScenario
    {
        public string Name => "Persona weapon owner bonus";
        private Pawn owner;
        private Pawn stranger;
        private ThingWithComps personaWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            owner = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig { Name = "PersonaOwner" });
            stranger = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "Stranger",
                SpawnPosition = owner?.Position + new IntVec3(3, 0, 0)
            });

            var personaDef = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d =>
                d.IsWeapon && d.comps != null &&
                d.comps.Any(c => c.compClass == typeof(CompBladelinkWeapon)));
            if (personaDef == null) return;

            personaWeapon = ThingMaker.MakeThing(personaDef) as ThingWithComps;
            if (personaWeapon != null && owner != null)
            {
                var bladelink = personaWeapon.GetComp<CompBladelinkWeapon>();
                bladelink?.CodeFor(owner);
                var biocodable = personaWeapon.GetComp<CompBiocodable>();
                biocodable?.CodeFor(owner);
            }
        }

        public TestResult Run()
        {
            if (owner == null || stranger == null)
                return TestResult.Failure("Pawn setup failed");
            if (personaWeapon == null)
                return TestResult.Skip("No persona weapon def available");

            if (!Caching.Components.IsPersonaWeapon(personaWeapon))
                return TestResult.Failure("Weapon not recognized as persona");
            if (!CompBiocodable.IsBiocodedFor(personaWeapon, owner))
                return TestResult.Failure("Biocode not applied to owner");
            if (CompBiocodable.IsBiocodedFor(personaWeapon, stranger))
                return TestResult.Failure("Biocode leaked to stranger");

            var ownerBreakdown = Scoring.GetScoreBreakdown(owner, personaWeapon);
            var strangerBreakdown = Scoring.GetScoreBreakdown(stranger, personaWeapon);

            if (Math.Abs(ownerBreakdown.personaMultiplier - Constants.PersonaWeaponMultiplier) > 0.01f)
                return TestResult.Failure(
                    $"Owner personaMultiplier {ownerBreakdown.personaMultiplier:F2}, expected {Constants.PersonaWeaponMultiplier}");
            if (Math.Abs(strangerBreakdown.personaMultiplier - 1.0f) > 0.01f)
                return TestResult.Failure(
                    $"Stranger personaMultiplier {strangerBreakdown.personaMultiplier:F2}, expected 1.0");

            return TestResult.Pass()
                .WithData("OwnerPersonaMultiplier", ownerBreakdown.personaMultiplier)
                .WithData("StrangerPersonaMultiplier", strangerBreakdown.personaMultiplier);
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(personaWeapon);
            TestHelpers.SafeDestroyPawn(owner);
            TestHelpers.SafeDestroyPawn(stranger);
        }
    }

    internal sealed class GrenadeHandlingTest : ITestScenario
    {
        public string Name => "Grenades and thrown weapons";
        private Pawn testPawn;
        private ThingWithComps grenade;
        private ThingWithComps normalWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            var filter = testPawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null)
                filter.AllowedQualityLevels = QualityRange.All;

            var grenadeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeFrag");
            if (grenadeDef != null)
            {
                grenade = TestHelpers.CreateWeapon(map, grenadeDef,
                    testPawn.Position + new IntVec3(1, 0, 0));
                if (grenade != null && filter != null)
                    filter.SetAllow(grenade.def, true);
            }

            var weaponDef = AutoArmDefOf.Gun_AssaultRifle
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle")
                ?? AutoArmDefOf.Gun_Autopistol;
            if (weaponDef != null)
            {
                normalWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                    testPawn.Position + new IntVec3(2, 0, 0));
                if (normalWeapon != null && filter != null)
                    filter.SetAllow(normalWeapon.def, true);
            }

            testPawn.equipment?.DestroyAllEquipment();
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test pawn not created");
            if (grenade == null)
                return TestResult.Skip("Weapon_GrenadeFrag def not present");
            if (normalWeapon == null)
                return TestResult.Failure("Normal weapon setup failed");

            float grenadeScore = Scoring.GetTotalScore(testPawn, grenade);
            float normalScore = Scoring.GetTotalScore(testPawn, normalWeapon);

            if (grenadeScore >= normalScore)
                return TestResult.Failure($"Grenade not de-prioritized: grenade={grenadeScore:F1}, rifle={normalScore:F1} (SituationalWeaponModifier regression)");

            return TestResult.Pass()
                .WithData("GrenadeScore", grenadeScore)
                .WithData("NormalScore", normalScore);
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(grenade);
            TestHelpers.SafeDestroyWeapon(normalWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class UnarmedOutfitBypassTest : ITestScenario
    {
        public string Name => "Unarmed respects filter and forbidden";
        private Pawn testPawn;
        private ThingWithComps forbiddenWeapon;
        private ThingWithComps outfitBlockedWeapon;
        private ThingWithComps allowedWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                forbiddenWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
                if (forbiddenWeapon != null)
                {
                    forbiddenWeapon.SetForbidden(true);
                    WeaponCache.AddWeaponToCache(forbiddenWeapon);
                }

                outfitBlockedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_ChainShotgun,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Masterwork);
                if (outfitBlockedWeapon != null)
                {
                    if (testPawn.outfits?.CurrentApparelPolicy != null)
                    {
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(outfitBlockedWeapon.def, false);
                        WeaponCache.OnOutfitFilterChanged(testPawn.outfits.CurrentApparelPolicy);
                        WeaponCache.ClearScoreCache();
                    }
                    WeaponCache.AddWeaponToCache(outfitBlockedWeapon);
                }

                allowedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(0, 0, 2), QualityCategory.Poor);
                if (allowedWeapon != null)
                    WeaponCache.AddWeaponToCache(allowedWeapon);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
            {
                var target = job.targetA.Thing;
                if (target == forbiddenWeapon)
                    return TestResult.Failure("Unarmed pawn picked up forbidden weapon");
                if (target == outfitBlockedWeapon)
                    return TestResult.Failure("Unarmed pawn picked outfit-blocked weapon");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(forbiddenWeapon);
            TestHelpers.SafeDestroyWeapon(outfitBlockedWeapon);
            TestHelpers.SafeDestroyWeapon(allowedWeapon);
        }
    }

    internal sealed class ForbiddenWeaponHandlingTest : ITestScenario
    {
        public string Name => "Forbidden and claimed weapons";
        private Pawn testPawn;
        private Pawn otherPawn;
        private ThingWithComps forbiddenWeapon;
        private ThingWithComps allowedWeapon;
        private ThingWithComps claimedWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            forbiddenWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
            if (forbiddenWeapon != null)
            {
                forbiddenWeapon.SetForbidden(true);
                WeaponCache.AddWeaponToCache(forbiddenWeapon);
            }

            allowedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);
            if (allowedWeapon != null)
            {
                allowedWeapon.SetForbidden(false);
                WeaponCache.AddWeaponToCache(allowedWeapon);
            }

            claimedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_ChainShotgun,
                testPawn.Position + new IntVec3(0, 0, 2), QualityCategory.Masterwork);
            if (claimedWeapon != null)
            {
                WeaponCache.AddWeaponToCache(claimedWeapon);
                otherPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = "ClaimerPawn",
                    SpawnPosition = testPawn.Position + new IntVec3(5, 0, 0)
                });
                if (otherPawn != null)
                {
                    var job = JobMaker.MakeJob(JobDefOf.Equip, claimedWeapon);
                    otherPawn.Reserve(claimedWeapon, job);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null && job.targetA.Thing == forbiddenWeapon)
                return TestResult.Failure("Unarmed pawn tried to pick up forbidden weapon");

            if (claimedWeapon != null && !claimedWeapon.Destroyed && testPawn.CanReserve(claimedWeapon))
                return TestResult.Failure("Can reserve weapon already claimed by another pawn");

            if (testPawn.outfits?.CurrentApparelPolicy != null && forbiddenWeapon != null)
            {
                forbiddenWeapon.SetForbidden(false);
                testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(forbiddenWeapon.def, false);
                WeaponCache.OnOutfitFilterChanged(testPawn.outfits.CurrentApparelPolicy);
                WeaponCache.ClearScoreCache();

                var job2 = jobGiver.TestTryGiveJob(testPawn);
                if (job2 != null && job2.targetA.Thing == forbiddenWeapon)
                    return TestResult.Failure("Picks up weapon not allowed by outfit");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            if (claimedWeapon != null && otherPawn != null && otherPawn.Map != null)
                otherPawn.Map.reservationManager?.ReleaseAllClaimedBy(otherPawn);

            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyPawn(otherPawn);
            TestHelpers.SafeDestroyWeapon(forbiddenWeapon);
            TestHelpers.SafeDestroyWeapon(allowedWeapon);
            TestHelpers.SafeDestroyWeapon(claimedWeapon);
        }
    }
}

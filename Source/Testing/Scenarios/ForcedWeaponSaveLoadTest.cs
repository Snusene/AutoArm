using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.IO;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class ForcedWeaponSaveLoadTest : ITestScenario
    {
        public string Name => "Forced weapons survive save/load";

        private Pawn testPawn;
        private ThingWithComps forcedPrimary;
        private ThingWithComps forcedSidearm;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (AutoArmDefOf.Gun_AssaultRifle == null || AutoArmDefOf.Gun_Autopistol == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            forcedPrimary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (forcedPrimary != null)
            {
                testPawn.equipment.AddEquipment(forcedPrimary);
                ForcedWeapons.SetForced(testPawn, forcedPrimary, "test", log: false);
            }

            forcedSidearm = ThingMaker.MakeThing(AutoArmDefOf.Gun_Autopistol) as ThingWithComps;
            if (forcedSidearm != null && testPawn.inventory?.innerContainer != null)
            {
                if (testPawn.inventory.innerContainer.TryAdd(forcedSidearm))
                    ForcedWeapons.AddSidearm(testPawn, forcedSidearm);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedPrimary == null || forcedSidearm == null)
                return TestResult.Failure("Setup incomplete");

            var component = Current.Game?.GetComponent<AutoArmGameComponent>();
            if (component == null)
                return TestResult.Failure("AutoArmGameComponent not present in game");

            if (!ForcedWeapons.IsForced(testPawn, forcedPrimary))
                return TestResult.Failure("Setup failed - primary not forced");
            if (!ForcedWeapons.IsForced(testPawn, forcedSidearm))
                return TestResult.Failure("Setup failed - sidearm not forced");

            string tempPath = Path.Combine(GenFilePaths.TempFolderPath, "autoarm_forced_roundtrip.xml");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

            try
            {
                Scribe.saver.InitSaving(tempPath, "AutoArmTest");
                try { component.ExposeData(); }
                finally { Scribe.saver.FinalizeSaving(); }

                ForcedWeapons.Reset();

                if (ForcedWeapons.IsForced(testPawn, forcedPrimary) || ForcedWeapons.IsForced(testPawn, forcedSidearm))
                    return TestResult.Failure("Reset did not clear forced state");

                Scribe.loader.InitLoading(tempPath);
                try { component.ExposeData(); }
                finally
                {
                    try { Scribe.loader.FinalizeLoading(); }
                    catch (Exception e) { AutoArmLogger.Debug(() => $"FinalizeLoading non-fatal: {e.Message}"); }
                }

                Scribe.mode = LoadSaveMode.PostLoadInit;
                try { component.ExposeData(); }
                finally { Scribe.mode = LoadSaveMode.Inactive; }

                try { component.LoadedGame(); }
                catch (Exception e) { AutoArmLogger.Debug(() => $"LoadedGame non-fatal: {e.Message}"); }

                bool primaryRestored = ForcedWeapons.IsForced(testPawn, forcedPrimary);
                bool sidearmRestored = ForcedWeapons.IsForced(testPawn, forcedSidearm);

                var result = new TestResult { Success = true };
                result.Data["PrimaryRestored"] = primaryRestored;
                result.Data["SidearmRestored"] = sidearmRestored;

                if (!primaryRestored && !sidearmRestored)
                {
                    result.Success = false;
                    result.FailureReason = "Neither primary nor sidearm forced state restored after save/load";
                }
                else if (!primaryRestored)
                {
                    result.Success = false;
                    result.FailureReason = "Forced primary not restored after save/load";
                }
                else if (!sidearmRestored)
                {
                    result.Success = false;
                    result.FailureReason = "Forced sidearm not restored after save/load";
                }

                return result;
            }
            catch (Exception ex)
            {
                return TestResult.Failure($"Save/load round-trip threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        public void Cleanup()
        {
            if (testPawn != null)
                ForcedWeapons.ClearForced(testPawn);

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }
        }
    }
}

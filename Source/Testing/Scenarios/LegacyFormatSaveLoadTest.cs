using AutoArm.Definitions;
using AutoArm.Testing.Helpers;
using System;
using System.IO;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class LegacyFormatSaveLoadTest : ITestScenario
    {
        public string Name => "Legacy save format still restores forced weapons";

        private Pawn testPawn;
        private ThingWithComps forcedPrimary;

        public void Setup(Map map)
        {
            if (map == null) return;
            if (AutoArmDefOf.Gun_AssaultRifle == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            forcedPrimary = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
            if (forcedPrimary != null)
            {
                testPawn.equipment.AddEquipment(forcedPrimary);
                ForcedWeapons.SetForced(testPawn, forcedPrimary, "test", log: false);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedPrimary == null)
                return TestResult.Failure("Setup incomplete");

            var component = Current.Game?.GetComponent<AutoArmGameComponent>();
            if (component == null)
                return TestResult.Failure("AutoArmGameComponent not present in game");

            if (!ForcedWeapons.IsForced(testPawn, forcedPrimary))
                return TestResult.Failure("Setup failed - primary not forced");

            string tempPath = Path.Combine(GenFilePaths.TempFolderPath, "autoarm_legacy_roundtrip.xml");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

            var snapshot = ForcedWeapons.Capture();

            try
            {
                AutoArmGameComponent.suppressRecordWriteForTest = true;
                try
                {
                    Scribe.saver.InitSaving(tempPath, "AutoArmTest");
                    try { component.ExposeData(); }
                    finally { Scribe.saver.FinalizeSaving(); }
                }
                finally
                {
                    AutoArmGameComponent.suppressRecordWriteForTest = false;
                }

                ForcedWeapons.Reset();

                if (ForcedWeapons.IsForced(testPawn, forcedPrimary))
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

                var result = new TestResult { Success = true };
                result.Data["PrimaryRestoredViaLegacyKeys"] = primaryRestored;

                if (!primaryRestored)
                {
                    result.Success = false;
                    result.FailureReason = "Forced primary not restored through the legacy key path";
                }

                return result;
            }
            catch (Exception ex)
            {
                return TestResult.Failure($"Legacy round-trip threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                ForcedWeapons.Restore(snapshot);
                AutoArmGameComponent.suppressRecordWriteForTest = false;
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

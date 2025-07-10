using UnityEngine;
using Verse;
using RimWorld;
using System.Linq;
using System.Collections.Generic;
using AutoArm.Testing;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        public bool modEnabled = true;
        public bool debugLogging = false;
        public bool showNotifications = true;
        public bool thinkTreeInjectionFailed = false;
        public bool autoEquipSidearms = true;
        public bool checkCEAmmo = true;

        // Advanced settings (for future use)
        public float weaponUpgradeThreshold = 1.05f;
        public int childrenMinAge = 13;
        public bool allowChildrenToEquipWeapons = false;
        public bool respectConceitedNobles = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, "modEnabled", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref showNotifications, "showNotifications", true);
            Scribe_Values.Look(ref thinkTreeInjectionFailed, "thinkTreeInjectionFailed", false);
            Scribe_Values.Look(ref autoEquipSidearms, "autoEquipSidearms", true);
            Scribe_Values.Look(ref checkCEAmmo, "checkCEAmmo", true);
            Scribe_Values.Look(ref weaponUpgradeThreshold, "weaponUpgradeThreshold", 1.05f);
            Scribe_Values.Look(ref childrenMinAge, "childrenMinAge", 13);
            Scribe_Values.Look(ref allowChildrenToEquipWeapons, "allowChildrenToEquipWeapons", false);
            Scribe_Values.Look(ref respectConceitedNobles, "respectConceitedNobles", true);
            base.ExposeData();
        }

        public void ResetToDefaults()
        {
            modEnabled = true;
            debugLogging = false;
            showNotifications = true;
            autoEquipSidearms = true;
            checkCEAmmo = true;
            weaponUpgradeThreshold = 1.05f;
            childrenMinAge = 13;
            allowChildrenToEquipWeapons = false;
            respectConceitedNobles = true;
        }
    }

    public class AutoArmMod : Mod
    {
        public static AutoArmSettings settings;
        private Vector2 scrollPosition;
        private string testResultsText = "";
        private TestResults lastTestResults;
        private bool showTestResults = false;

        public AutoArmMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AutoArmSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();

            // Reserve space for test results if shown
            float mainHeight = showTestResults ? inRect.height * 0.6f : inRect.height;
            Rect mainRect = new Rect(inRect.x, inRect.y, inRect.width, mainHeight);

            listingStandard.Begin(mainRect);

            // Main enable/disable
            listingStandard.CheckboxLabeled("AutoArm_EnableMod".Translate(), ref settings.modEnabled,
                "AutoArm_EnableModDesc".Translate());

            listingStandard.Gap(12f);

            // Notifications setting
            listingStandard.CheckboxLabeled("AutoArm_ShowNotifications".Translate(), ref settings.showNotifications,
                "AutoArm_ShowNotificationsDesc".Translate());

            // Show Simple Sidearms option if it's loaded
            if (SimpleSidearmsCompat.IsLoaded())
            {
                listingStandard.Gap(12f);
                listingStandard.CheckboxLabeled("AutoArm_EnableSidearmAutoEquip".Translate(), ref settings.autoEquipSidearms,
                    "AutoArm_EnableSidearmAutoEquipDesc".Translate());
            }

            // Show Combat Extended option if it's loaded
            if (CECompat.IsLoaded())
            {
                listingStandard.Gap(12f);
                listingStandard.CheckboxLabeled("AutoArm_CheckCEAmmo".Translate(), ref settings.checkCEAmmo,
                    "AutoArm_CheckCEAmmoDesc".Translate());
            }

            listingStandard.Gap(20f);

            // Detected mods section
            listingStandard.Label("AutoArm_DetectedMods".Translate() + ":");
            string detectedMods = GetDetectedModsList();
            listingStandard.Label(detectedMods);

            listingStandard.Gap(20f);

            // Reset button
            if (listingStandard.ButtonText("AutoArm_ResetToDefaults".Translate()))
            {
                settings.ResetToDefaults();
                Messages.Message("AutoArm_SettingsReset".Translate(), MessageTypeDefOf.TaskCompletion);
            }

            listingStandard.Gap(20f);

            // Debug section
            listingStandard.CheckboxLabeled("AutoArm_EnableDebugLogging".Translate(), ref settings.debugLogging,
                "AutoArm_EnableDebugLoggingDesc".Translate());

            // Debug options when enabled
            if (settings.debugLogging)
            {
                DrawDebugSection(listingStandard);
            }

            listingStandard.End();

            // Draw test results if shown
            if (showTestResults && lastTestResults != null)
            {
                DrawTestResults(new Rect(inRect.x, mainRect.yMax + 10f, inRect.width, inRect.height - mainHeight - 10f));
            }

            base.DoSettingsWindowContents(inRect);
        }

        private void DrawDebugSection(Listing_Standard listing)
        {
            listing.Gap(12f);

            // Think tree status
            listing.Label("Debug Info:");
            listing.Label($"  Think tree injection: {(settings.thinkTreeInjectionFailed ? "FAILED - using fallback" : "OK")}");

            // Show what Simple Sidearms mods are installed
            var sidearmsMods = ModLister.AllInstalledMods.Where(m =>
                m.PackageIdPlayerFacing.ToLower().Contains("sidearms") ||
                m.Name.ToLower().Contains("sidearm")).ToList();
            if (sidearmsMods.Any())
            {
                listing.Label($"  Found {sidearmsMods.Count} sidearms-related mod(s):");
                foreach (var mod in sidearmsMods.Take(3))
                {
                    listing.Label($"    - {mod.Name} ({mod.PackageIdPlayerFacing})");
                }
            }

            // Test runner button - only show if in game
            if (Current.Game != null && Find.CurrentMap != null)
            {
                listing.Gap(12f);
                listing.Label("Testing Tools:");

                // Simple test buttons
                if (listing.ButtonText("Test Weapon Detection"))
                {
                    TestWeaponDetection();
                }

                if (listing.ButtonText("Test Pawn Validation"))
                {
                    TestPawnValidation();
                }

                // Advanced test runner
                listing.Gap(6f);

                if (listing.ButtonText("Run All AutoArm Tests"))
                {
                    RunAllTests();
                }

                if (listing.ButtonText("Run Specific Test"))
                {
                    ShowTestMenu();
                }

                if (listing.ButtonText("Export Debug Logs"))
                {
                    ExportDebugLogs();
                }

                // Toggle test results display
                if (lastTestResults != null)
                {
                    string buttonText = showTestResults ? "Hide Test Results" : "Show Test Results";
                    if (listing.ButtonText(buttonText))
                    {
                        showTestResults = !showTestResults;
                    }
                }
            }
            else
            {
                listing.Gap(12f);
                listing.Label("<i>Test runner requires an active game</i>");
            }
        }

        private void RunAllTests()
        {
            testResultsText = "Running tests...\n";
            showTestResults = true;

            try
            {
                // Initialize test runner
                var results = new TestResults();
                var tests = GetAllTests();

                foreach (var test in tests)
                {
                    try
                    {
                        Log.Message($"[AutoArm] Running test: {test.Name}");
                        test.Setup(Find.CurrentMap);
                        var result = test.Run();
                        results.AddResult(test.Name, result);
                        test.Cleanup();

                        testResultsText += $"{test.Name}: {(result.Success ? "PASSED" : "FAILED")}\n";
                        if (!result.Success)
                        {
                            testResultsText += $"  Reason: {result.FailureReason}\n";
                        }
                    }
                    catch (System.Exception e)
                    {
                        results.AddResult(test.Name, TestResult.Failure($"Exception: {e.Message}"));
                        testResultsText += $"{test.Name}: FAILED (Exception)\n";
                        Log.Error($"[AutoArm] Test {test.Name} threw exception: {e}");
                    }
                }

                lastTestResults = results;
                testResultsText += $"\n=== Summary ===\n";
                testResultsText += $"Total: {results.TotalTests}\n";
                testResultsText += $"Passed: {results.PassedTests}\n";
                testResultsText += $"Failed: {results.FailedTests}\n";
                testResultsText += $"Success Rate: {results.SuccessRate:P0}\n";

                // Show completion message
                string msg = $"Tests complete: {results.PassedTests}/{results.TotalTests} passed";
                MessageTypeDefOf messageType = results.FailedTests > 0 ?
                    MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.PositiveEvent;
                Messages.Message(msg, messageType);
            }
            catch (System.Exception e)
            {
                testResultsText += $"\nTest runner error: {e.Message}";
                Log.Error($"[AutoArm] Test runner error: {e}");
            }
        }

        private void ShowTestMenu()
        {
            var options = new List<FloatMenuOption>();
            var tests = GetAllTests();

            foreach (var test in tests)
            {
                options.Add(new FloatMenuOption(test.Name, () => RunSingleTest(test)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void RunSingleTest(ITestScenario test)
        {
            try
            {
                test.Setup(Find.CurrentMap);
                var result = test.Run();
                test.Cleanup();

                string message = $"Test {test.Name}: {(result.Success ? "PASSED" : "FAILED")}";
                if (!result.Success)
                {
                    message += $"\nReason: {result.FailureReason}";
                }

                // Add data if present
                if (result.Data != null && result.Data.Count > 0)
                {
                    message += "\nData:";
                    foreach (var kvp in result.Data)
                    {
                        message += $"\n  {kvp.Key}: {kvp.Value}";
                    }
                }

                Messages.Message(message, result.Success ?
                    MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
            }
            catch (System.Exception e)
            {
                Messages.Message($"Test failed with exception: {e.Message}",
                    MessageTypeDefOf.RejectInput);
                Log.Error($"[AutoArm] Test {test.Name} exception: {e}");
            }
        }

        private void ExportDebugLogs()
        {
            string logs = AutoArmLogger.ExportRecentLogs();
            GUIUtility.systemCopyBuffer = logs;
            Messages.Message("Debug logs copied to clipboard", MessageTypeDefOf.TaskCompletion);
        }

        private void DrawTestResults(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));
            rect = rect.ContractedBy(5f);

            // Title
            Rect titleRect = new Rect(rect.x, rect.y, rect.width, 20f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "Test Results");
            Text.Font = GameFont.Small;

            // Results text
            Rect textRect = new Rect(rect.x, rect.y + 25f, rect.width, rect.height - 25f);
            Widgets.BeginScrollView(textRect, ref scrollPosition,
                new Rect(0, 0, rect.width - 20f, Text.CalcHeight(testResultsText, rect.width - 20f)));

            Widgets.Label(new Rect(0, 0, rect.width - 20f, 9999f), testResultsText);

            Widgets.EndScrollView();
        }

        // Simple test methods that don't require the full testing framework
        private void TestWeaponDetection()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Take(10)
                .ToList();

            Log.Message($"[AutoArm] Found {weapons.Count} weapons on map:");
            foreach (var weapon in weapons)
            {
                Log.Message($"  - {weapon.Label} ({weapon.def.defName})");
            }

            Messages.Message($"Found {weapons.Count} weapons. Check log for details.", MessageTypeDefOf.NeutralEvent);
        }

        private void TestPawnValidation()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            var colonists = map.mapPawns.FreeColonists.ToList();
            int validCount = 0;

            Log.Message($"[AutoArm] Testing {colonists.Count} colonists:");
            foreach (var pawn in colonists)
            {
                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(pawn, out reason);

                if (isValid)
                {
                    validCount++;
                    Log.Message($"  - {pawn.Name}: Valid");
                }
                else
                {
                    Log.Message($"  - {pawn.Name}: Invalid - {reason}");
                }
            }

            Messages.Message($"{validCount}/{colonists.Count} colonists valid for auto-equip. Check log for details.",
                MessageTypeDefOf.NeutralEvent);
        }

        private List<ITestScenario> GetAllTests()
        {
            return new List<ITestScenario>
            {
                new UnarmedPawnTest(),
                new BrawlerTest(),
                new HunterTest(),
                new WeaponUpgradeTest(),
                new OutfitFilterTest(),
                new ForcedWeaponTest(),
                new CombatExtendedAmmoTest(),
                new SimpleSidearmsIntegrationTest(),
                new ChildColonistTest(),
                new NobilityTest(),
                new MapTransitionTest(),
                new SaveLoadTest(),
                new PerformanceTest(),
                new EdgeCaseTest()
            };
        }

        private string GetDetectedModsList()
        {
            string detectedMods = "";

            if (SimpleSidearmsCompat.IsLoaded())
                detectedMods += "• Simple Sidearms\n";
            if (CECompat.IsLoaded())
                detectedMods += "• Combat Extended\n";
            if (InfusionCompat.IsLoaded())
                detectedMods += "• Infusion 2\n";

            if (string.IsNullOrEmpty(detectedMods))
                detectedMods = "AutoArm_NoCompatModsDetected".Translate();

            return detectedMods;
        }

        public override string SettingsCategory()
        {
            return "AutoArm_SettingsCategory".Translate();
        }
    }
}
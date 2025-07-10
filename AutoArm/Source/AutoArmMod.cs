using UnityEngine;
using Verse;
using RimWorld;
using System.Linq;
using System.Collections.Generic;
using AutoArm.Testing;

namespace AutoArm
{
    public class AutoArmMod : Mod
    {
        public static AutoArmSettings settings;
        private Vector2 scrollPosition;
        private string testResultsText = "";
        private TestResults lastTestResults;
        private bool showTestResults = false;

        // Tab tracking
        private enum SettingsTab
        {
            General,
            Compatibility,
            Advanced,
            Debug
        }
        private SettingsTab currentTab = SettingsTab.General;

        public AutoArmMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AutoArmSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Draw header
            DrawHeader(ref inRect);

            // Draw tabs
            DrawTabs(ref inRect);

            // Draw tab content
            switch (currentTab)
            {
                case SettingsTab.General:
                    DrawGeneralTab(inRect);
                    break;
                case SettingsTab.Compatibility:
                    DrawCompatibilityTab(inRect);
                    break;
                case SettingsTab.Advanced:
                    DrawAdvancedTab(inRect);
                    break;
                case SettingsTab.Debug:
                    DrawDebugTab(inRect);
                    break;
            }

            base.DoSettingsWindowContents(inRect);
        }

        private void DrawHeader(ref Rect inRect)
        {
            // Draw mod title
            Text.Font = GameFont.Medium;
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 40f);
            Widgets.Label(titleRect, "AutoArm Settings");

            // Draw version info
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            var versionRect = new Rect(inRect.x, inRect.y, inRect.width - 5f, 20f);
            Widgets.Label(versionRect, "v1.0.0");
            Text.Anchor = TextAnchor.UpperLeft;

            inRect.y += 45f;
            inRect.height -= 45f;

            // Draw separator
            Widgets.DrawLineHorizontal(inRect.x, inRect.y, inRect.width);
            inRect.y += 10f;
            inRect.height -= 10f;
        }

        private void DrawTabs(ref Rect inRect)
        {
            Text.Font = GameFont.Small;
            var tabRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            var tabWidth = tabRect.width / 4f;

            // General tab
            var generalRect = new Rect(tabRect.x, tabRect.y, tabWidth - 5f, tabRect.height);
            if (Widgets.ButtonText(generalRect, "General"))
            {
                currentTab = SettingsTab.General;
            }
            if (currentTab == SettingsTab.General)
            {
                Widgets.DrawHighlight(generalRect);
            }

            // Compatibility tab
            var compatRect = new Rect(tabRect.x + tabWidth, tabRect.y, tabWidth - 5f, tabRect.height);
            if (Widgets.ButtonText(compatRect, "Compatibility"))
            {
                currentTab = SettingsTab.Compatibility;
            }
            if (currentTab == SettingsTab.Compatibility)
            {
                Widgets.DrawHighlight(compatRect);
            }

            // Advanced tab
            var advancedRect = new Rect(tabRect.x + tabWidth * 2, tabRect.y, tabWidth - 5f, tabRect.height);
            if (Widgets.ButtonText(advancedRect, "Advanced"))
            {
                currentTab = SettingsTab.Advanced;
            }
            if (currentTab == SettingsTab.Advanced)
            {
                Widgets.DrawHighlight(advancedRect);
            }

            // Debug tab
            var debugRect = new Rect(tabRect.x + tabWidth * 3, tabRect.y, tabWidth - 5f, tabRect.height);
            if (Widgets.ButtonText(debugRect, "Debug"))
            {
                currentTab = SettingsTab.Debug;
            }
            if (currentTab == SettingsTab.Debug)
            {
                Widgets.DrawHighlight(debugRect);
            }

            inRect.y += 35f;
            inRect.height -= 35f;
        }

        private void DrawGeneralTab(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // Main settings section
            DrawSectionHeader(listing, "Main Settings");

            // Enable mod with better checkbox
            DrawCheckboxRow(listing, "Enable AutoArm", "When enabled, colonists will automatically equip better weapons based on their outfit policy.",
                ref settings.modEnabled);

            listing.Gap(12f);

            // Notifications
            DrawCheckboxRow(listing, "Show notifications", "Shows blue notification messages when colonists equip or drop weapons.",
                ref settings.showNotifications);

            listing.Gap(20f);

            // Status section
            DrawSectionHeader(listing, "Mod Status");

            Text.Font = GameFont.Tiny;
            listing.Label(new TaggedString($"Think tree injection: {(settings.thinkTreeInjectionFailed ? "FAILED - using fallback" : "OK")}"), -1, null);
            Text.Font = GameFont.Small;

            listing.Gap(20f);

            // Reset button
            var resetRect = listing.GetRect(30f);
            resetRect.width = 150f;
            if (Widgets.ButtonText(resetRect, "Reset to defaults"))
            {
                settings.ResetToDefaults();
                Messages.Message("Settings have been reset to defaults", MessageTypeDefOf.TaskCompletion, false);
            }

            listing.End();
        }

        private void DrawCompatibilityTab(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            DrawSectionHeader(listing, "Detected Mods");

            // Show detected mods with icons
            DrawModStatus(listing, "Simple Sidearms", SimpleSidearmsCompat.IsLoaded());
            DrawModStatus(listing, "Combat Extended", CECompat.IsLoaded());
            DrawModStatus(listing, "Infusion 2", InfusionCompat.IsLoaded());

            listing.Gap(20f);

            // Mod-specific settings
            if (SimpleSidearmsCompat.IsLoaded())
            {
                DrawSectionHeader(listing, "Simple Sidearms");
                DrawCheckboxRow(listing, "Auto-equip sidearms",
                    "Allows colonists to automatically pick up additional weapons as sidearms.",
                    ref settings.autoEquipSidearms);
            }

            if (CECompat.IsLoaded())
            {
                listing.Gap(20f);
                DrawSectionHeader(listing, "Combat Extended");
                DrawCheckboxRow(listing, "Check for ammunition",
                    "When enabled, colonists will only pick up weapons if they have access to appropriate ammunition.",
                    ref settings.checkCEAmmo);
            }

            listing.End();
        }

        private void DrawAdvancedTab(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            DrawSectionHeader(listing, "Weapon Selection");

            // Weapon upgrade threshold
            listing.Label($"Weapon upgrade threshold: {settings.weaponUpgradeThreshold:P0}");
            settings.weaponUpgradeThreshold = listing.Slider(settings.weaponUpgradeThreshold, 1.01f, 1.50f);
            Text.Font = GameFont.Tiny;
            listing.Label("How much better a weapon must be to trigger an upgrade (default: 5%)");
            Text.Font = GameFont.Small;

            listing.Gap(20f);

            // Age restrictions
            if (ModsConfig.BiotechActive)
            {
                DrawSectionHeader(listing, "Age Restrictions");

                DrawCheckboxRow(listing, "Allow children to equip weapons",
                    "When enabled, child colonists can pick up and use weapons.",
                    ref settings.allowChildrenToEquipWeapons);

                if (settings.allowChildrenToEquipWeapons)
                {
                    listing.Label($"Minimum age: {settings.childrenMinAge}");
                    settings.childrenMinAge = (int)listing.Slider(settings.childrenMinAge, 3, 18);
                }
            }

            listing.Gap(20f);

            // Nobility
            if (ModsConfig.RoyaltyActive)
            {
                DrawSectionHeader(listing, "Nobility");

                DrawCheckboxRow(listing, "Respect conceited nobles",
                    "When enabled, conceited nobles won't automatically switch weapons.",
                    ref settings.respectConceitedNobles);
            }

            listing.End();
        }

        private void DrawDebugTab(Rect inRect)
        {
            var listing = new Listing_Standard();

            // Split the area if showing test results
            Rect mainRect = showTestResults ? new Rect(inRect.x, inRect.y, inRect.width, inRect.height * 0.5f) : inRect;

            listing.Begin(mainRect);

            DrawSectionHeader(listing, "Debug Options");

            DrawCheckboxRow(listing, "Enable debug logging",
                "Outputs detailed information to the debug log for troubleshooting.",
                ref settings.debugLogging);

            if (settings.debugLogging && Current.Game != null && Find.CurrentMap != null)
            {
                listing.Gap(20f);
                DrawSectionHeader(listing, "Testing Tools");

                // Test buttons in a row
                var buttonRect = listing.GetRect(30f);
                var buttonWidth = (buttonRect.width - 10f) / 2f;

                var testWeaponRect = new Rect(buttonRect.x, buttonRect.y, buttonWidth, buttonRect.height);
                if (Widgets.ButtonText(testWeaponRect, "Test Weapon Detection"))
                {
                    TestWeaponDetection();
                }

                var testPawnRect = new Rect(buttonRect.x + buttonWidth + 10f, buttonRect.y, buttonWidth, buttonRect.height);
                if (Widgets.ButtonText(testPawnRect, "Test Pawn Validation"))
                {
                    TestPawnValidation();
                }

                listing.Gap(10f);

                // Full test runner
                var runTestsRect = listing.GetRect(35f);
                runTestsRect.width = 200f;
                if (Widgets.ButtonText(runTestsRect, "Run All Tests", true, false, true))
                {
                    RunAllTests();
                }

                if (lastTestResults != null)
                {
                    listing.Gap(10f);
                    var toggleRect = listing.GetRect(25f);
                    toggleRect.width = 150f;
                    string buttonText = showTestResults ? "Hide Results" : "Show Results";
                    if (Widgets.ButtonText(toggleRect, buttonText))
                    {
                        showTestResults = !showTestResults;
                    }
                }
            }
            else if (settings.debugLogging)
            {
                listing.Gap(20f);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                listing.Label("Testing tools require an active game");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            listing.End();

            // Draw test results if shown
            if (showTestResults && lastTestResults != null)
            {
                DrawTestResults(new Rect(inRect.x, mainRect.yMax + 10f, inRect.width, inRect.height - mainRect.height - 10f));
            }
        }

        // Helper methods
        private void DrawSectionHeader(Listing_Standard listing, string text)
        {
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            listing.Label(text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.GapLine(6f);
        }

        private void DrawCheckboxRow(Listing_Standard listing, string label, string tooltip, ref bool value)
        {
            var rect = listing.GetRect(24f);

            // Draw checkbox on the left
            var checkRect = new Rect(rect.x, rect.y, 24f, 24f);
            Widgets.Checkbox(checkRect.x, checkRect.y, ref value);

            // Draw label
            var labelRect = new Rect(rect.x + 30f, rect.y, rect.width - 30f, rect.height);
            Widgets.Label(labelRect, label);

            // Add tooltip to entire row
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        private void DrawModStatus(Listing_Standard listing, string modName, bool isLoaded)
        {
            var rect = listing.GetRect(22f);

            // Draw status indicator
            GUI.color = isLoaded ? Color.green : Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y, 20f, rect.height), isLoaded ? "✓" : "✗");
            GUI.color = Color.white;

            // Draw mod name
            Widgets.Label(new Rect(rect.x + 25f, rect.y, rect.width - 25f, rect.height), modName);
        }

        private void DrawTestResults(Rect rect)
        {
            // Draw background
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));
            Widgets.DrawBox(rect, 2);
            rect = rect.ContractedBy(10f);

            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), "Test Results");
            Text.Font = GameFont.Small;

            // Results
            var textRect = new Rect(rect.x, rect.y + 35f, rect.width, rect.height - 35f);
            Widgets.BeginScrollView(textRect, ref scrollPosition,
                new Rect(0, 0, rect.width - 20f, Text.CalcHeight(testResultsText, rect.width - 20f)));

            GUI.color = new Color(0.8f, 1f, 0.8f);
            Widgets.Label(new Rect(0, 0, rect.width - 20f, 9999f), testResultsText);
            GUI.color = Color.white;

            Widgets.EndScrollView();
        }

        // Test methods remain the same...
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

            Messages.Message($"Found {weapons.Count} weapons. Check log for details.", MessageTypeDefOf.NeutralEvent, false);
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
                MessageTypeDefOf.NeutralEvent, false);
        }

        private void RunAllTests()
        {
            testResultsText = "Running tests...\n";
            showTestResults = true;

            try
            {
                var results = TestRunner.RunAllTests(Find.CurrentMap);
                lastTestResults = results;

                testResultsText = "";
                foreach (var test in TestRunner.GetAllTests())
                {
                    var result = results.GetResult(test.Name);
                    if (result != null)
                    {
                        testResultsText += $"{test.Name}: {(result.Success ? "PASSED" : "FAILED")}\n";
                        if (!result.Success)
                        {
                            testResultsText += $"  Reason: {result.FailureReason}\n";
                        }
                    }
                }

                testResultsText += $"\n=== Summary ===\n";
                testResultsText += $"Total: {results.TotalTests}\n";
                testResultsText += $"Passed: {results.PassedTests}\n";
                testResultsText += $"Failed: {results.FailedTests}\n";
                testResultsText += $"Success Rate: {results.SuccessRate:P0}\n";

                string msg = $"Tests complete: {results.PassedTests}/{results.TotalTests} passed";
                MessageTypeDef messageType = results.FailedTests > 0 ?
                    MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.PositiveEvent;
                Messages.Message(msg, messageType, false);
            }
            catch (System.Exception e)
            {
                testResultsText += $"\nTest runner error: {e.Message}";
                Log.Error($"[AutoArm] Test runner error: {e}");
            }
        }

        private List<ITestScenario> GetAllTests()
        {
            return TestRunner.GetAllTests();
        }

        public override string SettingsCategory()
        {
            return "AutoArm";
        }
    }
}
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
            // Use simpler UI approach for better compatibility
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // Title
            Text.Font = GameFont.Medium;
            listing.Label("AutoArm Settings");
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            // Simple tab buttons
            var tabRect = listing.GetRect(30f);
            var tabWidth = tabRect.width / 4f - 5f;

            if (Widgets.ButtonText(new Rect(tabRect.x, tabRect.y, tabWidth, 30f), "General"))
                currentTab = SettingsTab.General;
            if (Widgets.ButtonText(new Rect(tabRect.x + tabWidth + 5f, tabRect.y, tabWidth, 30f), "Compatibility"))
                currentTab = SettingsTab.Compatibility;
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 2, tabRect.y, tabWidth, 30f), "Advanced"))
                currentTab = SettingsTab.Advanced;
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 3, tabRect.y, tabWidth, 30f), "Debug"))
                currentTab = SettingsTab.Debug;

            listing.Gap(12f);

            // Content area
            var contentRect = listing.GetRect(inRect.height - listing.CurHeight - 30f);
            var innerRect = contentRect.ContractedBy(10f);

            Widgets.DrawBoxSolid(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.3f));

            // Draw tab content
            var innerListing = new Listing_Standard();
            innerListing.Begin(innerRect);

            switch (currentTab)
            {
                case SettingsTab.General:
                    DrawGeneralTab(innerListing);
                    break;
                case SettingsTab.Compatibility:
                    DrawCompatibilityTab(innerListing);
                    break;
                case SettingsTab.Advanced:
                    DrawAdvancedTab(innerListing);
                    break;
                case SettingsTab.Debug:
                    DrawDebugTab(innerListing, innerRect);
                    break;
            }

            innerListing.End();
            listing.End();
        }

        private void DrawGeneralTab(Listing_Standard listing)
        {
            // Main settings
            listing.CheckboxLabeled("Enable AutoArm", ref settings.modEnabled,
                "When enabled, colonists will automatically equip better weapons based on their outfit policy.");

            listing.Gap(12f);

            listing.CheckboxLabeled("Show notifications", ref settings.showNotifications,
                "Shows blue notification messages when colonists equip or drop weapons.");

            listing.Gap(20f);

            // Status
            Text.Font = GameFont.Medium;
            listing.Label("Mod Status");
            Text.Font = GameFont.Small;

            listing.Label($"Think tree injection: {(settings.thinkTreeInjectionFailed ? "FAILED - using fallback" : "OK")}");

            listing.Gap(20f);

            // Reset button
            if (listing.ButtonText("Reset to defaults"))
            {
                settings.ResetToDefaults();
                Messages.Message("Settings have been reset to defaults", MessageTypeDefOf.TaskCompletion, false);
            }
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Detected Mods");
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            // Show detected mods
            listing.Label($"Simple Sidearms: {(SimpleSidearmsCompat.IsLoaded() ? "Loaded" : "Not found")}");
            listing.Label($"Combat Extended: {(CECompat.IsLoaded() ? "Loaded" : "Not found")}");
            listing.Label($"Infusion 2: {(InfusionCompat.IsLoaded() ? "Loaded" : "Not found")}");

            listing.Gap(20f);

            // Mod-specific settings
            if (SimpleSidearmsCompat.IsLoaded())
            {
                Text.Font = GameFont.Medium;
                listing.Label("Simple Sidearms");
                Text.Font = GameFont.Small;

                listing.CheckboxLabeled("Auto-equip sidearms", ref settings.autoEquipSidearms,
                    "Allows colonists to automatically pick up additional weapons as sidearms.");
            }

            if (CECompat.IsLoaded())
            {
                listing.Gap(20f);
                Text.Font = GameFont.Medium;
                listing.Label("Combat Extended");
                Text.Font = GameFont.Small;

                listing.CheckboxLabeled("Check for ammunition", ref settings.checkCEAmmo,
                    "When enabled, colonists will only pick up weapons if they have access to appropriate ammunition.");
            }
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Weapon Selection");
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            // Weapon upgrade threshold
            listing.Label($"Weapon upgrade threshold: {settings.weaponUpgradeThreshold:P0}");
            settings.weaponUpgradeThreshold = listing.Slider(settings.weaponUpgradeThreshold, 1.01f, 1.50f);

            listing.Gap(20f);

            // Age restrictions
            if (ModsConfig.BiotechActive)
            {
                Text.Font = GameFont.Medium;
                listing.Label("Age Restrictions");
                Text.Font = GameFont.Small;

                listing.CheckboxLabeled("Allow children to equip weapons", ref settings.allowChildrenToEquipWeapons,
                    "When enabled, child colonists can pick up and use weapons.");

                if (settings.allowChildrenToEquipWeapons)
                {
                    listing.Label($"Minimum age: {settings.childrenMinAge}");
                    settings.childrenMinAge = (int)listing.Slider(settings.childrenMinAge, 3, 18);
                }
            }

            // Nobility
            if (ModsConfig.RoyaltyActive)
            {
                listing.Gap(20f);
                Text.Font = GameFont.Medium;
                listing.Label("Nobility");
                Text.Font = GameFont.Small;

                listing.CheckboxLabeled("Respect conceited nobles", ref settings.respectConceitedNobles,
                    "When enabled, conceited nobles won't automatically switch weapons.");
            }
        }

        private void DrawDebugTab(Listing_Standard listing, Rect contentRect)
        {
            listing.CheckboxLabeled("Enable debug logging", ref settings.debugLogging,
                "Outputs detailed information to the debug log for troubleshooting.");

            if (settings.debugLogging && Current.Game != null && Find.CurrentMap != null)
            {
                listing.Gap(20f);
                Text.Font = GameFont.Medium;
                listing.Label("Testing Tools");
                Text.Font = GameFont.Small;

                if (listing.ButtonText("Test Weapon Detection"))
                {
                    TestWeaponDetection();
                }

                if (listing.ButtonText("Test Pawn Validation"))
                {
                    TestPawnValidation();
                }

                if (listing.ButtonText("Run All Tests"))
                {
                    RunAllTests();
                }

                // Show test results if available
                if (lastTestResults != null && showTestResults)
                {
                    listing.Gap(10f);

                    var resultsRect = new Rect(contentRect.x, listing.CurHeight + 20f, contentRect.width, contentRect.height - listing.CurHeight - 30f);

                    Widgets.DrawBoxSolid(resultsRect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

                    var innerResultsRect = resultsRect.ContractedBy(10f);
                    var textHeight = Text.CalcHeight(testResultsText, innerResultsRect.width);

                    Widgets.BeginScrollView(innerResultsRect, ref scrollPosition,
                        new Rect(0, 0, innerResultsRect.width - 20f, textHeight));

                    Widgets.Label(new Rect(0, 0, innerResultsRect.width - 20f, textHeight), testResultsText);

                    Widgets.EndScrollView();
                }
            }
            else if (settings.debugLogging)
            {
                listing.Gap(20f);
                listing.Label("Testing tools require an active game");
            }
        }

        // Test methods
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

        public override string SettingsCategory()
        {
            return "AutoArm";
        }
    }
}
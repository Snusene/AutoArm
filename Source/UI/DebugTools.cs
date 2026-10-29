
using AutoArm.Definitions;
using AutoArm.UI;
using UnityEngine;
using Verse;

namespace AutoArm
{
    public sealed class DebugTools : Window
    {
        private bool refocusing = false;

        public DebugTools()
        {
            doCloseX = true;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(Constants.DEBUG_WINDOW_WIDTH, Constants.DEBUG_WINDOW_HEIGHT);

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            float x = (Verse.UI.screenWidth - size.x) / 2f;
            float y = (Verse.UI.screenHeight - size.y) / 2f;
            windowRect = new Rect(x, y, size.x, size.y);
        }

        public void SetFocus()
        {
            refocusing = true;
            Find.WindowStack.TryRemove(this, doCloseSound: false);
            Find.WindowStack.Add(this);
            refocusing = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Small;
            var headerRect = listing.GetRect(24f);
            using (new TextBlock(UIColors.Dim))
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(new Rect(headerRect.x, headerRect.y, headerRect.width - 220f, headerRect.height), DebugPanel.Summary(Find.CurrentMap));

            string logLabel = "AutoArm_EnableDebugLogging".Translate();
            float logWidth = Text.CalcSize(logLabel).x;
            var checkboxRect = new Rect(headerRect.xMax - 44f, headerRect.y, 24f, 24f);
            using (new TextBlock(TextAnchor.MiddleRight))
                Widgets.Label(new Rect(checkboxRect.x - logWidth - 8f, headerRect.y, logWidth + 4f, headerRect.height), logLabel);
            bool oldDebugLogging = AutoArmMod.settings.debugLogging;
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref AutoArmMod.settings.debugLogging, 24f);
            if (oldDebugLogging != AutoArmMod.settings.debugLogging)
            {
                AutoArmLogger.Info($"Debug logging {(AutoArmMod.settings.debugLogging ? "enabled" : "disabled")}");

                if (AutoArmMod.settings.debugLogging)
                {
                    AutoArmLogger.AnnounceVerboseLogging();
                }
            }

            listing.Gap(6f);

            var resultsRect = new Rect(0f, listing.CurHeight, inRect.width, inRect.height - listing.CurHeight);
            DebugPanel.Draw(resultsRect);

            listing.End();
        }

        public override void PostClose()
        {
            base.PostClose();

            if (!refocusing)
            {
                DebugPanel.ResetState();
            }
        }
    }
}

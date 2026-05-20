using RimWorld;
using UnityEngine;

namespace AutoArm.UI
{
    internal static class UIColors
    {
        public static readonly Color Pass = new Color(0.45f, 0.85f, 0.45f);
        public static readonly Color Fail = new Color(1.00f, 0.45f, 0.45f);
        public static readonly Color Skip = new Color(0.95f, 0.80f, 0.35f);
        public static readonly Color Warning = new Color(1.00f, 0.60f, 0.60f);
        public static readonly Color Dim = new Color(0.65f, 0.65f, 0.65f);

        public static readonly Color Active = new Color(0.70f, 1.00f, 0.70f);
        public static readonly Color Busy = new Color(0.80f, 0.80f, 1.00f);
        public static readonly Color Invalid = new Color(1.00f, 0.80f, 0.80f);
        public static readonly Color InvalidReason = new Color(1.00f, 0.80f, 0.50f);
        public static readonly Color NoWeapon = new Color(1.00f, 0.70f, 0.50f);

        public static readonly Color Ranged = new Color(1.00f, 0.65f, 0.65f);
        public static readonly Color Melee = new Color(0.65f, 0.85f, 1.00f);
        public static readonly Color Forbidden = new Color(0.60f, 0.60f, 0.60f);

        public static readonly Color AccentHeader = new Color(0.70f, 0.78f, 0.88f);
        public static readonly Color LabelMuted = new Color(0.85f, 0.85f, 0.85f);

        public static readonly Color TabInactive = new Color(0.75f, 0.75f, 0.75f);
        public static readonly Color LinkIdle = new Color(0.50f, 0.75f, 1.00f);
        public static readonly Color LinkHover = new Color(0.60f, 0.85f, 1.00f);
        public static readonly Color WarningSoft = new Color(1.00f, 0.80f, 0.40f);

        public static readonly Color PillRangedBg = new Color(0.70f, 0.25f, 0.25f, 0.35f);
        public static readonly Color PillMeleeBg = new Color(0.25f, 0.40f, 0.70f, 0.35f);
        public static readonly Color PillForbiddenBg = new Color(0.45f, 0.45f, 0.45f, 0.35f);

        public static Color QualityColor(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return new Color(0.60f, 0.60f, 0.60f);
                case QualityCategory.Poor: return new Color(0.85f, 0.70f, 0.55f);
                case QualityCategory.Normal: return Color.white;
                case QualityCategory.Good: return new Color(0.75f, 1.00f, 0.75f);
                case QualityCategory.Excellent: return new Color(0.50f, 0.95f, 0.50f);
                case QualityCategory.Masterwork: return new Color(0.55f, 0.80f, 1.00f);
                case QualityCategory.Legendary: return new Color(1.00f, 0.85f, 0.35f);
                default: return Color.white;
            }
        }
    }
}

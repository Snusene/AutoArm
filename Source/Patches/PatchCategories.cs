
namespace AutoArm.Patches
{
    /// <summary>
    /// Patch categories
    /// Selective enabling
    /// </summary>
    public static class PatchCategories
    {
        /// <summary>
        /// Core weapon management functionality
        /// Essential patches that must always be active
        /// </summary>
        public const string Core = "AutoArm.Core";

        /// <summary>
        /// UI integration patches
        /// Outfit dialog, weapon filters, etc.
        /// </summary>
        public const string UI = "AutoArm.UI";

        /// <summary>
        /// Performance and caching patches
        /// Cache sync
        /// </summary>
        public const string Performance = "AutoArm.Performance";

        /// <summary>
        /// Mod compatibility patches
        /// SimpleSidearms, Combat Extended, etc.
        /// </summary>
        public const string Compatibility = "AutoArm.Compatibility";

        /// <summary>
        /// Child/age restrictions
        /// Only needed when child-related mods are present
        /// </summary>
        public const string AgeRestrictions = "AutoArm.AgeRestrictions";

        /// <summary>
        /// Testing patches
        /// Active only during test runs to prevent errors
        /// </summary>
        public const string Testing = "AutoArm.Testing";
    }
}

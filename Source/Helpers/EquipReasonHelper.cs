
namespace AutoArm.Helpers
{
    internal static class EquipReasonHelper
    {
        /// <summary>
        /// Normalize vanilla reason strings to concise, stable labels for UI/logs.
        /// Falls back to the original when no mapping is detected.
        /// </summary>
        public static string Normalize(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return reason;

            var r = reason.ToLowerInvariant();

            if (r.Contains("biocode") || r.Contains("biocoded") || r.Contains("biocodable"))
                return "Biocoded";

            if (r.Contains("persona") || r.Contains("bladelink"))
                return "Persona bonded";

            if (r.Contains("lodger") || r.Contains("guest"))
                return "Quest lodger";

            if (r.Contains("role") || r.Contains("noranged") || r.Contains("nomelee"))
                return "Ideology role forbids";

            return reason;
        }
    }
}

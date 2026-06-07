using System;

namespace AutoArm.UI
{
    internal static class BlockerClassifier
    {
        private static readonly (string pattern, string bucket)[] rules =
        {
            ("Reserved", "Reserved"),
            ("Already owned", "Owned"),
            ("Equipped by someone", "Owned"),
            ("someone's inventory", "Owned"),
            ("ideology", "Ideology"),
            ("outfit", "Outfit filter"),
            ("Quest", "Quest pawn"),
            ("Temporary colonist", "Quest pawn"),
            ("unreachable", "Unreachable"),
            ("Cannot reach", "Unreachable"),
            ("Brawler", "Brawler trait"),
            ("skill too low", "Skill too low"),
            ("shooting skill", "Skill too low"),
            ("mental state", "Mental state"),
            ("Currently hauling", "Hauling"),
            ("ritual", "In ritual"),
            ("ceremony", "In ritual"),
            ("Forbidden", "Forbidden"),
            ("Too young", "Too young"),
            ("blacklisted", "Blacklisted"),
            ("dropped", "Recently dropped"),
            ("SimpleSidearms", "SimpleSidearms"),
            ("No weapons found", "No weapons found"),
            ("Failed validation", "Failed validation"),
        };

        private const int MAX_TRUNCATE_LENGTH = 20;

        public static string Classify(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return reason;

            if (Contains(reason, "No ammo") && Contains(reason, "Combat Extended"))
                return "No ammo (CE)";

            foreach (var rule in rules)
            {
                if (ContainsWord(reason, rule.pattern))
                    return rule.bucket;
            }

            if (reason.Length > MAX_TRUNCATE_LENGTH)
            {
                int cut = MAX_TRUNCATE_LENGTH;
                if (char.IsHighSurrogate(reason[cut - 1])) cut--;
                return reason.Substring(0, cut);
            }
            return reason;
        }

        private static bool Contains(string s, string sub)
            => s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool ContainsWord(string s, string word)
        {
            int idx = s.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                bool leftOk = idx == 0 || !char.IsLetterOrDigit(s[idx - 1]);
                int endIdx = idx + word.Length;
                bool rightOk = endIdx == s.Length || !char.IsLetterOrDigit(s[endIdx]);
                if (leftOk && rightOk) return true;
                idx = s.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}

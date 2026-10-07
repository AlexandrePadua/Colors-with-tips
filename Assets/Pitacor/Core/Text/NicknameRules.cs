using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pitacor.Core.Text
{
    /// <summary>Guest nickname rules (docs/tech/core-spec.md section 3.3).</summary>
    public static class NicknameRules
    {
        public const int MinLength = 1;
        public const int MaxLength = 16;

        /// <summary>
        /// Returns the cleaned nickname (trimmed, single-spaced), or null when it is not acceptable: length outside
        /// 1-16, a char other than letters, ASCII digits, space, '_' and '-', no letter or digit at all, or profanity
        /// (any language) when filterProfanity is set.
        /// </summary>
        public static string Sanitize(string raw, bool filterProfanity)
        {
            string s = TextNormalization.CollapseSpaces(raw);
            if (s.Length < MinLength || s.Length > MaxLength) return null;

            bool hasLetterOrDigit = false;
            int marks = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (TextNormalization.IsWordLetter(c) || (c >= '0' && c <= '9'))
                {
                    hasLetterOrDigit = true;
                    marks = 0;
                }
                else if (TextNormalization.IsCombiningMark(c))
                {
                    if (i == 0 || !(char.IsLetter(s[i - 1]) || TextNormalization.IsCombiningMark(s[i - 1]))) return null;
                    if (++marks > ClueValidator.MaxCombiningMarksPerLetter) return null;
                }
                else if (c == ' ' || c == '_' || c == '-')
                {
                    marks = 0;
                }
                else
                {
                    return null;
                }
            }
            if (!hasLetterOrDigit) return null;
            if (filterProfanity && TextFilter.IsProfaneName(s)) return null;
            return s;
        }

        /// <summary>
        /// Returns the nickname unchanged when no taken nickname has the same normalized form (case, accents and
        /// look-alike letters ignored); otherwise appends " 2", " 3"... trimming the base so the result fits MaxLength.
        /// </summary>
        public static string MakeUnique(string nickname, IEnumerable<string> taken)
        {
            if (string.IsNullOrEmpty(nickname) || taken == null) return nickname;
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string t in taken)
                if (t != null) used.Add(TextNormalization.Normalize(t));
            if (!used.Contains(TextNormalization.Normalize(nickname))) return nickname;

            for (int n = 2; ; n++)
            {
                string number = n.ToString(CultureInfo.InvariantCulture);
                int maxBase = Math.Max(0, MaxLength - 1 - number.Length);
                string head = (nickname.Length > maxBase ? nickname.Substring(0, maxBase) : nickname).TrimEnd();
                string candidate = head.Length == 0 ? number : head + " " + number;
                if (!used.Contains(TextNormalization.Normalize(candidate))) return candidate;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Pitacor.Core.Rules;

namespace Pitacor.Core.Text
{
    /// <summary>
    /// Whole-word masking for chat and other free text. A word is a run of letters (combining marks, invisible chars and
    /// internal hyphens included; leetspeak digits/symbols too for profanity); everything else, including the original
    /// spacing and punctuation, is preserved. A matching word is replaced as a whole, hyphen parts included.
    /// </summary>
    public static class TextFilter
    {
        public const string Mask = "***";

        private static readonly ProfanityMatcher PtMatcher = new ProfanityMatcher(WordLists.Profanity(Language.PtBR));
        private static readonly ProfanityMatcher EnMatcher = new ProfanityMatcher(WordLists.Profanity(Language.En));
        private static readonly ProfanityMatcher AnyMatcher = new ProfanityMatcher(WordLists.AllProfanity());

        /// <summary>
        /// Replaces every word whose normalized form (or any hyphen part of it) equals one of the given words with "***".
        /// Entries are normalized again and split on spaces, so a two-word clue can be passed as is.
        /// </summary>
        public static string MaskWords(string text, IEnumerable<string> normalizedWords)
        {
            if (string.IsNullOrEmpty(text) || normalizedWords == null) return text;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entry in normalizedWords)
            {
                foreach (string word in TextNormalization.Normalize(entry).Split(' '))
                {
                    if (word.Length == 0) continue;
                    set.Add(word);
                    if (word.IndexOf('-') >= 0) set.Add(word.Replace("-", string.Empty));
                }
            }
            if (set.Count == 0) return text;
            return MaskTokens(text, false, w => MatchesAnyPart(w, set.Contains));
        }

        /// <summary>Masks every basic color word of the language (same rules as the clue validator).</summary>
        public static string MaskColorNames(string text, Language lang)
        {
            WordSet colors = WordLists.Colors(lang);
            return MaskTokens(text, false, colors.MatchesAnyPart);
        }

        public static bool ContainsProfanity(string text, Language lang)
        {
            ProfanityMatcher matcher = MatcherFor(lang);
            bool found = false;
            ScanTokens(text, true, w =>
            {
                if (matcher.IsMatch(w)) found = true;
                return found;
            });
            return found;
        }

        /// <summary>Masks profane words, matched after leetspeak folding (4 3 1 0 5 @ $) and tolerating repeated letters.</summary>
        public static string MaskProfanity(string text, Language lang) => MaskTokens(text, true, MatcherFor(lang).IsMatch);

        /// <summary>True when a single normalized word (as produced by TextNormalization.Normalize) is profane.</summary>
        public static bool IsProfaneWord(string normalizedWord, Language lang) => MatcherFor(lang).IsMatch(normalizedWord);

        /// <summary>
        /// Profanity check for nicknames, against every language: each word, and the whole name with separators
        /// removed ("Fu_ck", "P0RR4").
        /// </summary>
        public static bool IsProfaneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            bool found = false;
            var joined = new StringBuilder(name.Length);
            ScanTokens(name, true, w =>
            {
                joined.Append(w.Replace("-", string.Empty));
                if (AnyMatcher.IsMatch(w)) found = true;
                return found;
            });
            return found || AnyMatcher.IsMatch(joined.ToString());
        }

        private static ProfanityMatcher MatcherFor(Language lang) => lang == Language.En ? EnMatcher : PtMatcher;

        private static bool MatchesAnyPart(string word, Func<string, bool> match)
        {
            if (match(word)) return true;
            if (word.IndexOf('-') < 0) return false;
            if (match(word.Replace("-", string.Empty))) return true;
            foreach (string part in word.Split('-'))
                if (part.Length > 0 && match(part)) return true;
            return false;
        }

        // ---------------------------------------------------------------- tokenizer

        private static bool IsLeet(char c) =>
            c == '4' || c == '3' || c == '1' || c == '0' || c == '5' || c == '@' || c == '$';

        private static char FoldLeet(char c)
        {
            switch (c)
            {
                case '4': case '@': return 'a';
                case '3': return 'e';
                case '1': return 'i';
                case '0': return 'o';
                case '5': case '$': return 's';
                default: return c;
            }
        }

        private static bool IsCore(char c, bool leet) => TextNormalization.IsWordLetter(c) || (leet && IsLeet(c));

        /// <summary>Calls visit(foldedWord) for each word until it returns true.</summary>
        private static void ScanTokens(string text, bool leet, Func<string, bool> visit)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = 0;
            while (i < text.Length)
            {
                if (!IsCore(text[i], leet))
                {
                    i++;
                    continue;
                }
                int end = TokenEnd(text, i, leet);
                if (visit(Fold(text, i, end, leet))) return;
                i = end;
            }
        }

        private static string MaskTokens(string text, bool leet, Func<string, bool> match)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder sb = null;
            int copied = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (!IsCore(text[i], leet))
                {
                    i++;
                    continue;
                }
                int end = TokenEnd(text, i, leet);
                if (match(Fold(text, i, end, leet)))
                {
                    sb ??= new StringBuilder(text.Length);
                    sb.Append(text, copied, i - copied).Append(Mask);
                    copied = end;
                }
                i = end;
            }
            if (sb == null) return text;
            sb.Append(text, copied, text.Length - copied);
            return sb.ToString();
        }

        /// <summary>
        /// End (exclusive) of the word starting at start: core chars, combining marks, invisible chars (so "p​orra"
        /// stays one word) and hyphens between cores.
        /// </summary>
        private static int TokenEnd(string text, int start, bool leet)
        {
            int j = start;
            int end = start;
            while (j < text.Length)
            {
                char c = text[j];
                if (IsCore(c, leet) || TextNormalization.IsCombiningMark(c) || TextNormalization.IsInvisible(c))
                {
                    j++;
                    end = j;
                }
                else if (TextNormalization.IsHyphen(c) && j + 1 < text.Length && IsCore(text[j + 1], leet))
                {
                    j++;
                }
                else
                {
                    break;
                }
            }
            return end;
        }

        private static string Fold(string text, int start, int end, bool leet)
        {
            var sb = new StringBuilder(end - start);
            for (int k = start; k < end; k++)
                TextNormalization.AppendFolded(sb, leet ? FoldLeet(text[k]) : text[k]);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- profanity matching

        /// <summary>
        /// Whole-word matcher tolerant to repeated letters: a word matches an entry when it has the same letter runs and
        /// each run is at least as long ("fuuuck" matches "fuck"; "niger" does not match "nigger").
        /// </summary>
        private sealed class ProfanityMatcher
        {
            private readonly Dictionary<string, List<string>> _byCollapsed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            private readonly IReadOnlyList<string> _embedded = WordLists.EmbeddedProfanity();

            public ProfanityMatcher(IEnumerable<string> words)
            {
                foreach (string w in words)
                {
                    string key = Collapse(w);
                    if (!_byCollapsed.TryGetValue(key, out var list)) _byCollapsed[key] = list = new List<string>();
                    list.Add(w);
                }
            }

            /// <summary>Checks a folded word: whole, without hyphens, each hyphen part, and embedded strong words.</summary>
            public bool IsMatch(string word)
            {
                if (string.IsNullOrEmpty(word)) return false;
                if (MatchesAnyPart(word, MatchesWhole)) return true;
                string collapsed = Collapse(word.Replace("-", string.Empty));
                foreach (string e in _embedded)
                    if (collapsed.IndexOf(e, StringComparison.Ordinal) >= 0) return true;
                return false;
            }

            private bool MatchesWhole(string word)
            {
                if (!_byCollapsed.TryGetValue(Collapse(word), out var candidates)) return false;
                foreach (string entry in candidates)
                    if (RunsCover(word, entry)) return true;
                return false;
            }

            private static string Collapse(string s)
            {
                var sb = new StringBuilder(s.Length);
                foreach (char c in s)
                    if (sb.Length == 0 || sb[sb.Length - 1] != c) sb.Append(c);
                return sb.ToString();
            }

            private static bool RunsCover(string word, string entry)
            {
                int i = 0, j = 0;
                while (i < word.Length && j < entry.Length)
                {
                    char c = entry[j];
                    if (word[i] != c) return false;
                    int entryRunEnd = j;
                    while (entryRunEnd < entry.Length && entry[entryRunEnd] == c) entryRunEnd++;
                    int wordRunEnd = i;
                    while (wordRunEnd < word.Length && word[wordRunEnd] == c) wordRunEnd++;
                    if (wordRunEnd - i < entryRunEnd - j) return false;
                    i = wordRunEnd;
                    j = entryRunEnd;
                }
                return i == word.Length && j == entry.Length;
            }
        }
    }
}

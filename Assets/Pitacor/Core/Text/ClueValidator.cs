using System;
using System.Collections.Generic;
using System.Text;

namespace Pitacor.Core.Text
{
    /// <summary>
    /// Server-authoritative clue check (docs/tech/core-spec.md section 3.1); the client runs it too to warn early.
    /// When several rules fail, the reason reported follows this precedence: Empty, InvalidCharacters, TooManyWords,
    /// TooLong, TooShort, ColorName, LightDarkOrDirection, PlayerName, HexLike, Profanity, Repeated.
    /// OffendingWord is the word as typed; the whole Display when the match spans both words (joined nickname, hex or
    /// profanity) or for Repeated; null for Empty, TooManyWords and TooLong.
    /// </summary>
    public static class ClueValidator
    {
        public const int FirstMaxWords = 1;
        public const int SecondMaxWords = 2;
        public const int FirstMaxLength = 20;
        public const int SecondMaxLength = 40;
        public const int MinLettersPerWord = 2;
        public const int MaxHyphensPerWord = 1;
        /// <summary>Nickname words shorter than this are not blocked ("Bo", "Lu").</summary>
        public const int MinNicknameWordLetters = 3;
        /// <summary>Consecutive combining marks allowed after a letter (decomposed accents; blocks "zalgo" text).</summary>
        public const int MaxCombiningMarksPerLetter = 2;

        public static int MaxWords(ClueKind kind) => kind == ClueKind.First ? FirstMaxWords : SecondMaxWords;

        public static int MaxLength(ClueKind kind) => kind == ClueKind.First ? FirstMaxLength : SecondMaxLength;

        public static ClueValidationResult Validate(string raw, ClueKind kind, ClueContext ctx)
        {
            if (ctx == null) ctx = new ClueContext();
            string display = TextNormalization.CollapseSpaces(raw);
            var result = new ClueValidationResult
            {
                Ok = false,
                Reason = ClueRejectReason.None,
                Display = display,
                Normalized = TextNormalization.Normalize(display),
            };
            if (display.Length == 0) return Reject(result, ClueRejectReason.Empty, null);

            string[] words = display.Split(' ');
            foreach (string w in words)
                if (!IsWellFormed(w)) return Reject(result, ClueRejectReason.InvalidCharacters, w);

            if (words.Length > MaxWords(kind)) return Reject(result, ClueRejectReason.TooManyWords, null);
            if (display.Length > MaxLength(kind)) return Reject(result, ClueRejectReason.TooLong, null);

            foreach (string w in words)
                if (CountLetters(w) < MinLettersPerWord) return Reject(result, ClueRejectReason.TooShort, w);

            var normalized = new string[words.Length];
            for (int i = 0; i < words.Length; i++) normalized[i] = TextNormalization.Normalize(words[i]);
            // Both words glued together ("ana maria" -> "anamaria"); only meaningful for two-word clues.
            string joined = words.Length > 1 ? LettersOnly(result.Normalized) : null;

            int hit = FindWord(normalized, w => WordLists.IsColorWord(w, ctx.Language));
            if (hit >= 0) return Reject(result, ClueRejectReason.ColorName, words[hit]);

            if (ctx.BlockLightDarkDirectionsNames)
            {
                hit = FindWord(normalized, w => WordLists.IsLightDarkOrDirectionWord(w, ctx.Language));
                if (hit >= 0) return Reject(result, ClueRejectReason.LightDarkOrDirection, words[hit]);

                HashSet<string> names = NicknameWords(ctx.PlayerNicknames);
                if (names.Count > 0)
                {
                    hit = FindWord(normalized, w => AnyPart(w, names.Contains));
                    if (hit >= 0) return Reject(result, ClueRejectReason.PlayerName, words[hit]);
                    if (joined != null && names.Contains(joined)) return Reject(result, ClueRejectReason.PlayerName, display);
                }
            }

            if (ctx.UltraHard)
            {
                hit = FindWord(normalized, w => AnyPart(w, IsHexLike));
                if (hit >= 0) return Reject(result, ClueRejectReason.HexLike, words[hit]);
                if (joined != null && IsHexLike(joined)) return Reject(result, ClueRejectReason.HexLike, display);
            }

            if (ctx.FilterProfanity)
            {
                hit = FindWord(normalized, w => TextFilter.IsProfaneWord(w, ctx.Language));
                if (hit >= 0) return Reject(result, ClueRejectReason.Profanity, words[hit]);
                if (joined != null && TextFilter.IsProfaneWord(joined, ctx.Language))
                    return Reject(result, ClueRejectReason.Profanity, display);
            }

            if (ctx.PreviousClues != null)
            {
                foreach (string previous in ctx.PreviousClues)
                    if (string.Equals(TextNormalization.Normalize(previous), result.Normalized, StringComparison.Ordinal))
                        return Reject(result, ClueRejectReason.Repeated, display);
            }

            result.Ok = true;
            return result;
        }

        /// <summary>
        /// Word = visible Unicode letters (up to two combining marks after a letter) with at most one hyphen, between
        /// letters. Letter count is checked separately (TooShort).
        /// </summary>
        public static bool IsWellFormed(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            int hyphens = 0;
            int marks = 0;
            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];
                if (TextNormalization.IsCombiningMark(c))
                {
                    // Never first, and a hyphen must be followed by a letter, so a mark always follows a letter.
                    if (i == 0 || ++marks > MaxCombiningMarksPerLetter) return false;
                }
                else if (TextNormalization.IsWordLetter(c))
                {
                    marks = 0;
                }
                else if (TextNormalization.IsHyphen(c))
                {
                    if (i == 0 || i == word.Length - 1 || ++hyphens > MaxHyphensPerWord) return false;
                    if (!TextNormalization.IsWordLetter(word[i + 1])) return false;
                    marks = 0;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Only a-f, with 3, 4, 6 or 8 letters: reads as a hex color ("cafe", "decade").</summary>
        public static bool IsHexLike(string normalizedWord)
        {
            if (string.IsNullOrEmpty(normalizedWord)) return false;
            int n = normalizedWord.Length;
            if (n != 3 && n != 4 && n != 6 && n != 8) return false;
            foreach (char c in normalizedWord)
                if (c < 'a' || c > 'f') return false;
            return true;
        }

        /// <summary>
        /// Normalized words of the nicknames (split on anything that is not a letter) with at least 3 letters, plus each
        /// multi-word nickname joined ("Ana Maria" blocks "ana", "maria" and "anamaria").
        /// </summary>
        public static HashSet<string> NicknameWords(IEnumerable<string> nicknames)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (nicknames == null) return set;
            var current = new StringBuilder();
            foreach (string nick in nicknames)
            {
                string n = TextNormalization.Normalize(nick);
                current.Clear();
                foreach (char c in n)
                {
                    if (char.IsLetter(c))
                    {
                        current.Append(c);
                        continue;
                    }
                    AddNicknameWord(set, current.ToString());
                    current.Clear();
                }
                AddNicknameWord(set, current.ToString());
                AddNicknameWord(set, LettersOnly(n));
            }
            return set;
        }

        private static void AddNicknameWord(HashSet<string> set, string word)
        {
            if (word.Length >= MinNicknameWordLetters) set.Add(word);
        }

        private static int FindWord(string[] normalizedWords, Func<string, bool> predicate)
        {
            for (int i = 0; i < normalizedWords.Length; i++)
                if (predicate(normalizedWords[i])) return i;
            return -1;
        }

        /// <summary>Whole word, word without hyphens, or any hyphen part.</summary>
        private static bool AnyPart(string word, Func<string, bool> predicate)
        {
            if (predicate(word)) return true;
            if (word.IndexOf('-') < 0) return false;
            if (predicate(word.Replace("-", string.Empty))) return true;
            foreach (string part in word.Split('-'))
                if (predicate(part)) return true;
            return false;
        }

        private static int CountLetters(string word)
        {
            int n = 0;
            foreach (char c in word)
                if (TextNormalization.IsWordLetter(c)) n++;
            return n;
        }

        private static string LettersOnly(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetter(c)) sb.Append(c);
            return sb.ToString();
        }

        private static ClueValidationResult Reject(ClueValidationResult result, ClueRejectReason reason, string offendingWord)
        {
            result.Ok = false;
            result.Reason = reason;
            result.OffendingWord = offendingWord;
            return result;
        }
    }
}

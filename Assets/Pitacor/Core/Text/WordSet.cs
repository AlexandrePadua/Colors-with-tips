using System;
using System.Collections.Generic;

namespace Pitacor.Core.Text
{
    /// <summary>
    /// Blocked-word matcher over normalized words: exact entries plus a few start/end rules for stems that cannot
    /// produce false positives (each rule is documented in WordLists).
    /// </summary>
    public sealed class WordSet
    {
        private readonly HashSet<string> _exact;
        private readonly string[] _prefixes;
        private readonly string[] _suffixes;

        public WordSet(IEnumerable<string> exact, string[] prefixes = null, string[] suffixes = null)
        {
            _exact = new HashSet<string>(exact, StringComparer.Ordinal);
            _prefixes = prefixes ?? new string[0];
            _suffixes = suffixes ?? new string[0];
        }

        public IReadOnlyCollection<string> Exact => _exact;

        /// <summary>Any word starting with one of these is blocked.</summary>
        public IReadOnlyList<string> Prefixes => _prefixes;

        /// <summary>Any word ending with one of these, after at least two other letters, is blocked (closed compounds).</summary>
        public IReadOnlyList<string> Suffixes => _suffixes;

        /// <summary>True when the normalized word (no spaces, no hyphens) is blocked.</summary>
        public bool Matches(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            if (_exact.Contains(word)) return true;
            foreach (string p in _prefixes)
                if (word.StartsWith(p, StringComparison.Ordinal)) return true;
            foreach (string s in _suffixes)
                if (word.Length >= s.Length + 2 && word.EndsWith(s, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Checks a normalized word that may contain one or more hyphens: the whole word, the word with hyphens removed,
        /// and every hyphen-separated part ("verde-agua" is blocked because of "verde").
        /// </summary>
        public bool MatchesAnyPart(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            if (word.IndexOf('-') < 0) return Matches(word);
            if (Matches(word) || Matches(word.Replace("-", string.Empty))) return true;
            foreach (string part in word.Split('-'))
                if (Matches(part)) return true;
            return false;
        }
    }
}

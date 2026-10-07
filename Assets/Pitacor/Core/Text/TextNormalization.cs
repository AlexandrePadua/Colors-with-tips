using System.Collections.Generic;
using System.Text;

namespace Pitacor.Core.Text
{
    /// <summary>
    /// Culture-independent folding used by every text comparison: lower-case, accent-free, single-spaced.
    /// Uses explicit tables instead of string.Normalize, which may lack data on Unity WebGL/IL2CPP.
    /// </summary>
    public static class TextNormalization
    {
        private const int LatinStart = 0x00C0;
        private const int LatinEnd = 0x0250; // exclusive; Latin-1 letters, Latin Extended-A and Extended-B

        // One folded char per code point from U+00C0 to U+017F (Latin-1 Supplement letters + Latin Extended-A).
        // '.' = not a letter / no Latin base (kept as is); '*' = ligature, expanded by Ligature().
        private const string LatinTable =
            "aaaaaa*ceeeeiiii" + // U+00C0 A grave .. I diaeresis (U+00C6 AE)
            "dnooooo.ouuuuy.*" + // U+00D0 Eth, N tilde, O.., multiplication sign, O stroke, U.., Y acute, Thorn, sharp s
            "aaaaaa*ceeeeiiii" + // U+00E0 lower-case of the U+00C0 row
            "dnooooo.ouuuuy.y" + // U+00F0 lower-case of the U+00D0 row (division sign), y diaeresis
            "aaaaaaccccccccdd" + // U+0100 A macron .. d caron
            "ddeeeeeeeeeegggg" + // U+0110 D stroke .. g breve
            "gggghhhhiiiiiiii" + // U+0120 G dot .. i ogonek
            "ii**jjkkklllllll" + // U+0130 I dot, dotless i, IJ, J circumflex, K cedilla, kra, L acute .. L middle dot
            "lllnnnnnnnnnoooo" + // U+0140 l middle dot, L stroke, N.., eng, O macron .. o breve
            "oo**rrrrrrssssss" + // U+0150 O double acute, OE, R.., S..
            "ssttttttuuuuuuuu" + // U+0160 S caron .. u ring
            "uuuuwwyyyzzzzzzs";  // U+0170 U double acute .. long s

        private const int CombiningFirst = 0x0300;
        private const int CombiningLast = 0x036F;

        private static readonly string[] Folds = BuildFolds();
        private static readonly Dictionary<char, char> Confusables = BuildConfusables();

        /// <summary>ASCII hyphen-minus, U+2010 hyphen and U+2011 non-breaking hyphen.</summary>
        public static bool IsHyphen(char c) => c == '-' || c == (char)0x2010 || c == (char)0x2011;

        /// <summary>Combining diacritical marks (U+0300..U+036F), as produced by decomposed input. Dropped when folding.</summary>
        public static bool IsCombiningMark(char c) => c >= CombiningFirst && c <= CombiningLast;

        /// <summary>
        /// Chars that render as nothing: zero-width and format chars, variation selectors and the Hangul fillers (which
        /// char.IsLetter accepts). Dropped when folding so they cannot split a word; never part of a word.
        /// </summary>
        public static bool IsInvisible(char c)
        {
            switch ((int)c)
            {
                case 0x00AD: // soft hyphen
                case 0x115F: case 0x1160: case 0x3164: case 0xFFA0: // Hangul fillers
                case 0x180E: // Mongolian vowel separator
                case 0x200B: case 0x200C: case 0x200D: case 0x200E: case 0x200F: // zero-width, LRM/RLM
                case 0x2060: case 0x2061: case 0x2062: case 0x2063: case 0x2064: // word joiner, invisible operators
                case 0xFEFF: // BOM / zero-width no-break space
                    return true;
                default:
                    return c >= 0xFE00 && c <= 0xFE0F; // variation selectors
            }
        }

        /// <summary>A visible letter: char.IsLetter minus the invisible fillers.</summary>
        public static bool IsWordLetter(char c) => char.IsLetter(c) && !IsInvisible(c);

        /// <summary>Trims and collapses every whitespace run into a single ASCII space. Null becomes "".</summary>
        public static string CollapseSpaces(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;
            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Comparison form: collapsed spaces, lower-case, diacritics removed (explicit table), hyphen variants as '-',
        /// combining marks and invisible chars dropped, common Cyrillic/Greek/full-width look-alikes folded to their
        /// Latin twin. Idempotent.
        /// </summary>
        public static string Normalize(string raw)
        {
            string collapsed = CollapseSpaces(raw);
            var sb = new StringBuilder(collapsed.Length);
            foreach (char c in collapsed) AppendFolded(sb, c);
            return sb.ToString();
        }

        /// <summary>Appends the folded form of a single char (zero, one or two chars).</summary>
        public static void AppendFolded(StringBuilder sb, char c)
        {
            if (c < 0x80)
            {
                sb.Append(c >= 'A' && c <= 'Z' ? (char)(c + 32) : c);
                return;
            }
            if (IsCombiningMark(c) || IsInvisible(c)) return;
            if (IsHyphen(c))
            {
                sb.Append('-');
                return;
            }
            // Upper-case look-alikes are mapped first: some (Cyrillic En, Em, Te, Ve) stop looking Latin once lower-cased.
            if (Confusables.TryGetValue(c, out char twin))
            {
                sb.Append(twin);
                return;
            }
            char lower = char.ToLowerInvariant(c);
            string fold = FoldOf(lower) ?? FoldOf(c);
            if (fold != null)
            {
                sb.Append(fold);
                return;
            }
            if (Confusables.TryGetValue(lower, out twin))
            {
                sb.Append(twin);
                return;
            }
            sb.Append(lower);
        }

        private static string FoldOf(char c)
        {
            if (c < 0x80) return c >= 'A' && c <= 'Z' ? ((char)(c + 32)).ToString() : c.ToString();
            if (c >= LatinStart && c < LatinEnd) return Folds[c - LatinStart];
            if (c >= 0x1E00 && c <= 0x1EFF) return FoldLatinExtendedAdditional(c);
            if (c >= 0xFF21 && c <= 0xFF3A) return ((char)('a' + (c - 0xFF21))).ToString(); // full-width A..Z
            if (c >= 0xFF41 && c <= 0xFF5A) return ((char)('a' + (c - 0xFF41))).ToString(); // full-width a..z
            return null;
        }

        private static string[] BuildFolds()
        {
            var folds = new string[LatinEnd - LatinStart];
            for (int i = 0; i < LatinTable.Length; i++)
            {
                char f = LatinTable[i];
                if (f == '.') continue;
                folds[i] = f == '*' ? Ligature(LatinStart + i) : f.ToString();
            }
            // Latin Extended-B: pinyin carons (U+01CD..U+01DC), Romanian comma-below S/T (U+0218..U+021B).
            Fill(folds, 0x01CD, "aaiioouuuuuuuuuu");
            Fill(folds, 0x0218, "sstt");
            return folds;
        }

        private static void Fill(string[] folds, int start, string letters)
        {
            for (int i = 0; i < letters.Length; i++) folds[start + i - LatinStart] = letters[i].ToString();
        }

        private static string Ligature(int codePoint)
        {
            switch (codePoint)
            {
                case 0x00C6: case 0x00E6: return "ae";
                case 0x00DF: return "ss";
                case 0x0132: case 0x0133: return "ij";
                case 0x0152: case 0x0153: return "oe";
                default: return null;
            }
        }

        // Latin Extended Additional (U+1E00..U+1EFF): upper/lower pairs grouped by base letter. Each entry starts a range
        // that runs to the next entry; ExtAddLetters holds the base letter of each range ('.' = leave as is).
        private static readonly int[] ExtAddStarts =
        {
            0x1E00, 0x1E02, 0x1E08, 0x1E0A, 0x1E14, 0x1E1E, 0x1E20, 0x1E22, 0x1E2C, 0x1E30, 0x1E36, 0x1E3E, 0x1E44,
            0x1E4C, 0x1E54, 0x1E58, 0x1E60, 0x1E6A, 0x1E72, 0x1E7C, 0x1E80, 0x1E8A, 0x1E8E, 0x1E90,
            0x1E96, 0x1E97, 0x1E98, 0x1E99, 0x1E9A, 0x1E9B, 0x1E9C,
            0x1EA0, 0x1EB8, 0x1EC8, 0x1ECC, 0x1EE4, 0x1EF2, 0x1EFA,
        };

        private const string ExtAddLetters = "abcdefghiklmnoprstuvwxyz" + "htwyas." + "aeiouy.";

        private static string FoldLatinExtendedAdditional(char c)
        {
            if (c == 0x1E9E) return "ss"; // capital sharp s
            int range = -1;
            for (int i = 0; i < ExtAddStarts.Length && ExtAddStarts[i] <= c; i++) range = i;
            if (range < 0) return null;
            char f = ExtAddLetters[range];
            return f == '.' ? null : f.ToString();
        }

        private static Dictionary<char, char> BuildConfusables()
        {
            var map = new Dictionary<char, char>();
            // Cyrillic capitals: A Ve Ie Ka Em En O Er Es Te U Ha Dze I(Ukrainian) Je
            Add(map, new[] { 0x0410, 0x0412, 0x0415, 0x041A, 0x041C, 0x041D, 0x041E, 0x0420, 0x0421, 0x0422, 0x0423, 0x0425,
                0x0405, 0x0406, 0x0408 }, "abekmhopctyxsij");
            // Cyrillic small: a ie io o er es u ha ka dze i yi je shha komi-de qa we palochka
            Add(map, new[] { 0x0430, 0x0435, 0x0451, 0x043E, 0x0440, 0x0441, 0x0443, 0x0445, 0x043A, 0x0455, 0x0456, 0x0457,
                0x0458, 0x04BB, 0x0501, 0x051B, 0x051D, 0x04CF }, "aeeopcyxksiijhdqwl");
            // Greek capitals: Alpha Beta Epsilon Zeta Eta Iota Kappa Mu Nu Omicron Rho Tau Upsilon Chi
            Add(map, new[] { 0x0391, 0x0392, 0x0395, 0x0396, 0x0397, 0x0399, 0x039A, 0x039C, 0x039D, 0x039F, 0x03A1, 0x03A4,
                0x03A5, 0x03A7 }, "abezhikmnoptyx");
            // Greek small: alpha epsilon iota kappa nu omicron rho tau upsilon chi
            Add(map, new[] { 0x03B1, 0x03B5, 0x03B9, 0x03BA, 0x03BD, 0x03BF, 0x03C1, 0x03C4, 0x03C5, 0x03C7 }, "aeikvoptux");
            // IPA alpha and script g
            Add(map, new[] { 0x0251, 0x0261 }, "ag");
            return map;
        }

        private static void Add(Dictionary<char, char> map, int[] from, string to)
        {
            for (int i = 0; i < from.Length; i++) map[(char)from[i]] = to[i];
        }
    }
}

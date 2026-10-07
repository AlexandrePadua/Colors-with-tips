using System.Collections.Generic;
using Pitacor.Core.Rules;

namespace Pitacor.Core.Text
{
    /// <summary>
    /// Blocked-word data. Every entry is already normalized (lower-case ASCII a-z, no accents; see TextNormalization).
    /// Lists apply to the room language only (spec 3.1): "red" is a valid clue in a pt-BR room.
    ///
    /// Color policy (both languages): block the 11 basic color words in every inflection (gender, number, degree),
    /// diminutive/augmentative/superlative, "-ish"/"-ado" adjective, color verb and "-ness" noun (brancura, verdor,
    /// vermelhidao, redness). Nouns that merely share a root and name a thing are allowed, like the other objects used
    /// as colors (vinho, salmao, cafe, limao): laranjeira, roseira, cinzeiro, verdura, azulejo. Exception: "laranjada"
    /// is blocked because it is spelled like the adjective "laranjado". Accidental look-alikes are allowed: verdade,
    /// rosario, Brancaleone, pretexto, negrito, amargo, Roxanne. Shades and synonyms that are not one of the 11 basic
    /// terms stay allowed (violeta, lilas, bege, dourado, rubro, castanho, anil; violet, gold, tan, navy, cyan, rose),
    /// except pt-BR "negro", an everyday synonym of "preto" (cabelo negro, buraco negro).
    ///
    /// Matching: exact entries, plus prefix rules only for stems that no unrelated word starts with ("vermelh",
    /// "amarel", "esverde"). "azul" is exact-only because of "azulejo", "verde" because of "verdade", "pret" because of
    /// "pretexto", "rosa" because of "rosario", "branc" because of "Brancaleone". EN blocks closed compounds built on a
    /// color word (blueberry, greenhouse, brownie, evergreen, offwhite) through start/end rules, accepting a few
    /// proper names (Greenland, Bluetooth); "red" has an explicit list only because it starts and ends too many
    /// unrelated words (reduce, credit, sacred). Hyphenated words are checked whole and per part ("verde-agua").
    /// </summary>
    public static class WordLists
    {
        // ---------------------------------------------------------------- colors, pt-BR

        private static readonly string[] PtColorExact = Concat(
            new[]
            {
                // blue
                "azul", "azuis", "azulao", "azuloes", "azulona", "azulonas",
                "azular", "azula", "azulam", "azulou", "azularam", "azulando",
                // green
                "verde", "verdes", "verdao", "verdoes", "verdona", "verdonas", "verdor", "verdores",
                // orange (also the fruit; always blocked)
                "laranja", "laranjas", "laranjinha", "laranjinhas", "laranjao", "laranjona",
                // pink (also the flower; always blocked)
                "rosa", "rosas", "rosinha", "rosinhas", "rosao", "rosona",
                // purple
                "roxao", "roxona", "roxidao",
                // brown
                "marrom", "marrons",
                // black
                "pretao", "pretona", "pretume", "pretidao", "negrume", "negrumes",
                // white
                "brancao", "brancona", "brancura", "brancuras",
                // gray
                "cinza", "cinzas", "cinzinha", "cinzinhas", "cinzao", "cinzona",
            },
            Gender("azulin"), Gender("verdoeng"), Gender("laranjad"), Gender("rose"), Gender("rosace"), Gender("rox"),
            Gender("pret"), Gender("negr"), Gender("branc"));

        private static readonly string[] PtColorPrefixes =
        {
            "vermelh", "avermelh",                                  // vermelho, vermelhidao, avermelhado...
            "azulad", "azulz", "azulinh", "azulissim",              // azulado, azulzinho, azulzao...
            "verdinh", "verdezinh", "verdissim", "verdej", "esverde", "esverdinh", // verdejante, esverdeado...
            "amarel",                                               // amarelo, amarelado, amarelinha, amarelar...
            "alaranj",
            "rosad", "rosinh",
            "roxinh", "roxissim", "roxea", "arroxe",                // roxeado, roxear, arroxeado...
            "marronz", "marromz", "amarronz", "amarromz",
            "pretinh", "pretissim", "pretej", "negrinh", "enegrec",
            "branquinh", "branquissim", "branque", "esbranquic", "embranquec", // branquear, esbranquicado...
            "cinzent", "acinzent",
        };

        // ---------------------------------------------------------------- colors, EN

        private static readonly string[] EnColorExact =
        {
            "red", "reds", "redder", "reddest", "reddish", "redish", "reddishness", "redness", "reddy",
            "redden", "reddens", "reddened", "reddening", "infrared",
            "redhead", "redheads", "redheaded", "redwood", "redwoods", "redcoat", "redcoats", "redbreast",
            "redbird", "redfish", "redcurrant", "redcurrants", "redneck", "rednecks",
            // "-ish"/"-y" forms that do not start with the full color word
            "bluish", "whitish", "purplish", "purply", "orangish", "orangy",
        };

        // blue*, green*, ... cover plurals, comparatives, -ish, -ness, -en verbs and compounds (blueberry, brownie).
        private static readonly string[] EnColorStems =
        {
            "blue", "green", "yellow", "orange", "pink", "purple", "brown", "black", "white", "gray", "grey",
        };

        // ---------------------------------------------------------------- light/dark + directions

        private static readonly string[] PtLightDarkDirectionExact = Concat(
            new[]
            {
                "clarear", "clareia", "clareiam", "clareou", "clarearam", "clareando", "clareamento",
                "clareza", "claridade", "claridades", "clarao", "claroes",
                "escuridao", "escuridoes", "escurao", "escurona",
                "acima", "abaixo", "cima", "baixo", "baixa", "baixos", "baixas", "embaixo", "debaixo",
                "perto", "longe", "canto", "cantos", "cantinho", "cantinhos", "centro", "centros", "central", "centrais",
                "meio", "meios", "topo", "topos", "borda", "bordas",
                "superior", "superiores", "inferior", "inferiores", "norte", "sul", "leste", "oeste",
            },
            Gender("clar"), Gender("claread"), Gender("escur"), Gender("esquerd"), Gender("direit"));

        private static readonly string[] PtLightDarkDirectionPrefixes =
        {
            "clarinh", "clarissim", "escurinh", "escurissim", "escurec", // clarinho, escurecer, escurecido...
        };

        private static readonly string[] EnLightDarkDirectionExact =
        {
            "light", "lights", "lighter", "lightest", "lighten", "lightens", "lightened", "lightening", "lightness",
            "lightish",
            "dark", "darks", "darker", "darkest", "darken", "darkens", "darkened", "darkening", "darkness", "darkish",
            "up", "down", "above", "below", "left", "right", "near", "nearer", "nearest", "far", "farther", "farthest",
            "corner", "corners", "center", "centers", "centre", "centres", "central", "centered", "centred", "middle",
            "top", "tops", "bottom", "bottoms", "edge", "edges", "upper", "lower", "north", "south", "east", "west",
            "leftmost", "rightmost", "topmost", "upward", "upwards", "downward", "downwards",
        };

        // ---------------------------------------------------------------- profanity (moderate)
        // Whole-word lists, matched after leetspeak folding and tolerating repeated letters ("poooorra").
        // Ambiguous everyday words are left out on purpose: pinto, rola, pau, saco, piranha, veado, macaco, cock, ass,
        // cum, tit, dick, prick, chink, coon, dyke, rape (pt "rape" = snuff), kike (Spanish nickname), cu in EN ("cu
        // later"). Nicknames are checked against every language, chat and clues against the room language.

        private static readonly string[] PtProfanityWords =
        {
            "porra", "caralho", "cacete", "buceta", "boceta", "xoxota", "xereca", "xota", "piroca",
            "cu", "cuzao", "cuzinho", "merda", "merdinha", "bosta",
            "foda", "fodase", "foder", "fodeu", "fodido", "fodida", "fudido", "fudida", "fuder", "fudeu",
            "puta", "putas", "puto", "putos", "putaria", "puteiro", "putinha",
            "vagabunda", "vadia", "arrombado", "arrombada", "corno", "escroto", "escrota",
            "viado", "viadinho", "bicha", "traveco", "retardado", "retardada",
            "punheta", "punheteiro", "boquete", "siririca", "tesao", "cagar", "cagada", "cagao",
            "fdp", "vsf", "pqp", "krl", "crl", "tnc", "vtnc",
            "nazista", "nazistas",
        };

        private static readonly string[] EnProfanityWords =
        {
            "fuck", "fucks", "fucked", "fucker", "fuckers", "fucking", "fuckin", "fck", "motherfucker", "motherfucking",
            "shit", "shits", "shitty", "shithead", "bullshit",
            "bitch", "bitches", "bitchy", "cunt", "cunts", "dickhead", "cocksucker", "pussy", "pussies",
            "asshole", "assholes", "arsehole", "bastard", "bastards", "whore", "whores", "slut", "sluts", "slutty",
            "faggot", "faggots", "fag", "fags", "nigger", "niggers", "nigga", "niggas", "retard", "retards", "retarded",
            "twat", "wanker", "wank", "jizz", "dildo", "spic", "spics", "tranny", "wetback", "rapist",
            "milf", "kys", "stfu",
        };

        // Applied to every language.
        private static readonly string[] CommonProfanityWords = { "porn", "porno", "nazi", "nazis", "hitler" };

        // Matched as substrings of a word ("fuckyou", "xxcaralhoxx"). Only long, unambiguous entries without double
        // letters belong here (not "nigger": "snigger"; not "porra": "porrada"; not "shit": "shitake").
        private static readonly string[] EmbeddedProfanityWords =
        {
            "fuck", "caralho", "buceta", "boceta", "piroca", "punheta", "xoxota", "hitler",
        };

        // ---------------------------------------------------------------- public access

        private static readonly WordSet PtColors = new WordSet(PtColorExact, PtColorPrefixes);
        private static readonly WordSet EnColors = new WordSet(EnColorExact, EnColorStems, EnColorStems);
        private static readonly WordSet PtLightDarkDirections = new WordSet(PtLightDarkDirectionExact, PtLightDarkDirectionPrefixes);
        private static readonly WordSet EnLightDarkDirections = new WordSet(EnLightDarkDirectionExact);

        public static WordSet Colors(Language lang) => lang == Language.En ? EnColors : PtColors;

        public static WordSet LightDarkDirections(Language lang) => lang == Language.En ? EnLightDarkDirections : PtLightDarkDirections;

        /// <summary>Whole-word profanity for a room language. pt-BR rooms also apply the EN list (common in Brazilian chat).</summary>
        public static IReadOnlyList<string> Profanity(Language lang) =>
            lang == Language.En ? Concat(EnProfanityWords, CommonProfanityWords) : Concat(PtProfanityWords, EnProfanityWords, CommonProfanityWords);

        /// <summary>Profanity from every language (nicknames have no room language).</summary>
        public static IReadOnlyList<string> AllProfanity() => Concat(PtProfanityWords, EnProfanityWords, CommonProfanityWords);

        public static IReadOnlyList<string> EmbeddedProfanity() => EmbeddedProfanityWords;

        public static bool IsColorWord(string normalizedWord, Language lang) => Colors(lang).MatchesAnyPart(normalizedWord);

        public static bool IsLightDarkOrDirectionWord(string normalizedWord, Language lang) =>
            LightDarkDirections(lang).MatchesAnyPart(normalizedWord);

        // ---------------------------------------------------------------- helpers

        /// <summary>pt-BR adjective in four forms: stem + o, a, os, as.</summary>
        private static string[] Gender(string stem) => new[] { stem + "o", stem + "a", stem + "os", stem + "as" };

        private static string[] Concat(params string[][] parts)
        {
            int total = 0;
            foreach (var p in parts) total += p.Length;
            var all = new string[total];
            int i = 0;
            foreach (var p in parts)
            {
                p.CopyTo(all, i);
                i += p.Length;
            }
            return all;
        }
    }
}

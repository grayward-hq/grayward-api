namespace Application.Helpers;
public record LookAlikeDomain(string Domain, string VariationType);

public static class LookAlikeDomainGenerator
{
    private static readonly string[] AltTlds = ["net", "org", "co", "io", "online", "site", "info"];

    private static readonly Dictionary<char, string[]> Homoglyphs = new()
    {
        ['a'] = ["à", "á", "â", "ä", "а"],
        ['e'] = ["è", "é", "ê", "ë", "е"],
        ['i'] = ["í", "î", "ï", "ì", "і"],
        ['o'] = ["ó", "ô", "ö", "ò", "о"],
        ['u'] = ["ú", "û", "ü", "ù"],
        ['c'] = ["ç", "с"],
        ['l'] = ["1", "І"],
    };

    private static readonly Dictionary<char, char> AdjacentKeys = new()
    {
        ['a'] = 's', ['b'] = 'v', ['c'] = 'x', ['d'] = 's', ['e'] = 'r',
        ['f'] = 'g', ['g'] = 'h', ['h'] = 'j', ['i'] = 'u', ['j'] = 'k',
        ['k'] = 'l', ['l'] = 'k', ['m'] = 'n', ['n'] = 'm', ['o'] = 'p',
        ['p'] = 'o', ['q'] = 'w', ['r'] = 'e', ['s'] = 'a', ['t'] = 'r',
        ['u'] = 'y', ['v'] = 'b', ['w'] = 'q', ['x'] = 'z', ['y'] = 'u',
        ['z'] = 'x',
    };

    public static List<LookAlikeDomain> Generate(string domainName)
    {
        // domainName = "google.com" → name = "google", tld = "com"
        var lastDot = domainName.LastIndexOf('.');
        if (lastDot < 0) return [];

        var name = domainName[..lastDot];
        var tld  = domainName[(lastDot + 1)..];
        var results = new HashSet<LookAlikeDomain>(LookAlikeComparer.Instance);

        // 1. Character repetition — gooogle.com
        for (var i = 0; i < name.Length; i++)
            results.Add(new($"{name[..i]}{name[i]}{name[i..]}.{tld}", "CharRepetition"));

        // 2. Character omission — gogle.com
        for (var i = 0; i < name.Length; i++)
            results.Add(new($"{name[..i]}{name[(i + 1)..]}.{tld}", "CharOmission"));

        // 3. Adjacent key substitution — googke.com
        for (var i = 0; i < name.Length; i++)
            if (AdjacentKeys.TryGetValue(name[i], out var replacement))
                results.Add(new($"{name[..i]}{replacement}{name[(i + 1)..]}.{tld}", "Typo"));

        // 4. Homoglyph substitution — gооgle.com (cyrillic о)
        for (var i = 0; i < name.Length; i++)
            if (Homoglyphs.TryGetValue(name[i], out var glyphs))
                foreach (var glyph in glyphs)
                    results.Add(new($"{name[..i]}{glyph}{name[(i + 1)..]}.{tld}", "Homoglyph"));

        // 5. Alt TLDs — google.net, google.org
        foreach (var altTld in AltTlds.Where(t => t != tld))
            results.Add(new($"{name}.{altTld}", "AltTld"));

        // 6. Common prefixes/suffixes
        foreach (var affix in new[] { "my", "get", "the", "app", "secure", "login", "official" })
        {
            results.Add(new($"{affix}{name}.{tld}", "Prefix"));
            results.Add(new($"{name}{affix}.{tld}", "Suffix"));
        }

        // 7. Hyphen insertion — goo-gle.com
        for (var i = 1; i < name.Length; i++)
            results.Add(new($"{name[..i]}-{name[i..]}.{tld}", "HyphenInsertion"));

        return results
            .Where(r => r.Domain != domainName)  // exclude original
            .DistinctBy(r => r.Domain)
            .ToList();
    }

    // Comparer to deduplicate by domain string
    private class LookAlikeComparer : IEqualityComparer<LookAlikeDomain>
    {
        public static readonly LookAlikeComparer Instance = new();
        public bool Equals(LookAlikeDomain? x, LookAlikeDomain? y) =>
            string.Equals(x?.Domain, y?.Domain, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(LookAlikeDomain obj) =>
            obj.Domain.ToLowerInvariant().GetHashCode();
    }
}
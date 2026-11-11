namespace cs2_rockthevote;

// Used by MapLister and managed tests. The only source is the plugin-local file;
// this policy never falls back to another directory or writes a map list.
public static class MapListPolicy
{
    public static string ResolvePath(string moduleDirectory) => Path.Combine(moduleDirectory, "maplist.txt");

    public static Map[] Load(string moduleDirectory)
    {
        string mapsFile = ResolvePath(moduleDirectory);
        if (!File.Exists(mapsFile))
            throw new FileNotFoundException($"MapLister: Maps file not found at {mapsFile}", mapsFile);

        return Parse(File.ReadAllText(mapsFile));
    }

    public static Map[] Parse(string text)
    {
        // Keep the existing line ordering, duplicate entries and Map normalization.
        // Only an exactly two-part name:ID line supplies a workshop ID.
        return text
            .Replace("\r\n", "\n")
            .Split("\n")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("//"))
            .Select(mapLine =>
            {
                string[] args = mapLine.Split(":");
                return new Map(args[0], args.Length == 2 ? args[1] : null);
            })
            .ToArray();
    }
}

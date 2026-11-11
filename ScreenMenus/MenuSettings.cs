using System.Globalization;
using CounterStrikeSharp.API;
using Tomlyn;
using Tomlyn.Model;

namespace cs2_rockthevote.ScreenMenus;

public enum MenuType { KeyPress, Scrollable, Both }
public sealed record MenuPosition(float PositionX = 0, float PositionY = 0);

public sealed class MenuSettings
{
    public string FontName { get; private set; } = "Tahoma Bold";
    public MenuType Mode { get; private set; } = MenuType.KeyPress;
    public int Size { get; private set; } = 25;
    public MenuPosition Position { get; private set; } = new();
    public bool HasExitOption { get; private set; } = true;
    public bool ShowResolutionOption { get; private set; } = true;
    public bool ShowPageCount { get; private set; } = true;
    public bool ShowDisabledOptionNum { get; private set; } = true;
    public bool FreezePlayer { get; private set; } = true;
    public bool ShowControlsInfo { get; private set; } = true;
    public string ScrollPrefix { get; private set; } = ">";
    public string UpKey { get; private set; } = "W";
    public string DownKey { get; private set; } = "S";
    public string SelectKey { get; private set; } = "E";
    public PlayerButtons Up => ParseButton(UpKey);
    public PlayerButtons Down => ParseButton(DownKey);
    public PlayerButtons Select => ParseButton(SelectKey);
    public float Volume { get; private set; } = 1;
    public Dictionary<string, MenuPosition> Resolutions { get; } = new();
    public Dictionary<string, string> Sounds { get; } = new()
    { ["Select"] = "menu.Select", ["Next"] = "menu.Select", ["Prev"] = "menu.Close", ["Close"] = "menu.Close", ["ScrollUp"] = "menu.ScrollUp", ["ScrollDown"] = "menu.ScrollDown" };
    public Dictionary<string, Dictionary<string, string>> Languages { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static PlayerButtons ParseButton(string key)
    {
        var alias = key.ToUpperInvariant() switch
        { "W" => "Forward", "S" => "Back", "A" => "Moveleft", "D" => "Moveright", "E" => "Use", "R" => "Reload", "SPACE" => "Jump", "SHIFT" => "Speed", _ => key };
        if (Enum.TryParse<PlayerButtons>(alias, true, out var value) && Enum.IsDefined(value) && value != 0) return value;
        throw new FormatException($"Unknown screen-menu control: {key}");
    }

    public string Text(string language, string key)
    {
        if (Languages.TryGetValue(language, out var local) && local.TryGetValue(key, out var text)) return text;
        var shortLanguage = language.Split('-')[0];
        if (Languages.TryGetValue(shortLanguage, out local) && local.TryGetValue(key, out text)) return text;
        if (Languages.TryGetValue("en", out local) && local.TryGetValue(key, out text)) return text;
        return key switch { "Prev" => "Back", "Next" => "Next", "Close" => "Close", "SelectRes" => "Select Your Game Resolution", "ChangeRes" => "Change Resolution", "ScrollKeys" => "[{0}/{1}] Scroll", "SelectKey" => "[{0}] Select", _ => key };
    }

    public static MenuSettings Parse(string input)
    {
        var root = Toml.ToModel(input);
        var settings = Table(root, "Settings"); var controls = Table(root, "Controls"); var sounds = Table(root, "Sounds");
        var result = new MenuSettings();
        result.FontName = String(settings, "FontName", result.FontName);
        string mode = String(settings, "MenuType", "KeyPress");
        if (!Enum.TryParse<MenuType>(mode, true, out var parsed) || !Enum.IsDefined(parsed)) throw new FormatException($"Invalid screen-menu MenuType: {mode}");
        result.Mode = parsed;
        var size = Number(settings, "Size", 25);
        if (size is < 1 or > 100 || size != MathF.Truncate(size)) throw new FormatException("Screen-menu Size must be an integer from 1 to 100.");
        result.Size = (int)size;
        result.Position = PositionFrom(settings);
        result.HasExitOption = Boolean(settings, "HasExitOption", true);
        result.ShowResolutionOption = Boolean(settings, "ShowResolutionOption", true);
        result.ShowPageCount = Boolean(settings, "ShowPageCount", true);
        result.ShowDisabledOptionNum = Boolean(settings, "ShowDisabledOptionNum", true);
        result.FreezePlayer = Boolean(settings, "FreezePlayer", true);
        result.ShowControlsInfo = Boolean(settings, "ShowControlsInfo", true);
        result.ScrollPrefix = String(settings, "ScrollPrefix", ">");
        result.UpKey = String(controls, "ScrollUp", "W"); result.DownKey = String(controls, "ScrollDown", "S"); result.SelectKey = String(controls, "Select", "E");
        _ = result.Up; _ = result.Down; _ = result.Select;
        if (result.Up == result.Down || result.Up == result.Select || result.Down == result.Select) throw new FormatException("Screen-menu navigation controls must be distinct.");
        foreach (var key in result.Sounds.Keys.ToArray()) result.Sounds[key] = String(sounds, key, result.Sounds[key]);
        result.Volume = Number(sounds, "Volume", 1);
        if (result.Volume is < 0 or > 1) throw new FormatException("Screen-menu Volume must be between 0 and 1.");
        foreach (var (key, value) in Table(settings, "Resolutions"))
        { if (value is not TomlTable table) throw new FormatException($"Resolution {key} must be a table."); result.Resolutions.Add(key, PositionFrom(table)); }
        foreach (var (key, value) in Table(root, "Lang"))
        {
            if (value is not TomlTable table) throw new FormatException($"Language {key} must be a table.");
            result.Languages[key] = table.ToDictionary(x => x.Key, x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? "");
        }
        if (System.Text.Encoding.UTF8.GetByteCount(result.FontName) > 63) throw new FormatException("Screen-menu FontName exceeds the engine's 63-byte limit.");
        return result;
    }

    private static MenuPosition PositionFrom(TomlTable table) => new(Number(table, "PositionX", 0), Number(table, "PositionY", 0));
    private static TomlTable Table(TomlTable table, string key) => !table.TryGetValue(key, out var value) ? new() : value as TomlTable ?? throw new FormatException($"{key} must be a table.");
    private static string String(TomlTable table, string key, string fallback) => !table.TryGetValue(key, out var value) ? fallback : value as string ?? throw new FormatException($"{key} must be text.");
    private static bool Boolean(TomlTable table, string key, bool fallback) => !table.TryGetValue(key, out var value) ? fallback : value is bool b ? b : throw new FormatException($"{key} must be boolean.");
    private static float Number(TomlTable table, string key, float fallback)
    {
        if (!table.TryGetValue(key, out var value)) return fallback;
        if (value is not (long or double)) throw new FormatException($"{key} must be numeric.");
        float number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (!float.IsFinite(number) || Math.Abs(number) > 1000) throw new FormatException($"{key} is outside the supported range.");
        return number;
    }
}

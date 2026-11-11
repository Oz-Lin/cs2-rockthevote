using CounterStrikeSharp.API.Core;

namespace cs2_rockthevote.ScreenMenus;

public enum PostSelect { Nothing, Close, Reset }
public sealed class MenuOption(string text, Action<CCSPlayerController, MenuOption> callback, bool disabled)
{
    public string Text { get; set; } = text;
    public bool IsDisabled { get; set; } = disabled;
    public Action<CCSPlayerController, MenuOption> Callback { get; } = callback;
}

// Plugin-local menu model. Construction has no native side effects.
public sealed class Menu(CCSPlayerController player, Plugin plugin)
{
    internal CCSPlayerController Player { get; } = player;
    internal bool IsResolutionChooser { get; set; }
    public object? OwnerTag { get; set; }
    public string Title { get; set; } = "";
    public PostSelect PostSelect { get; set; } = PostSelect.Nothing;
    public bool HasExitButon { get; set; } = true;
    public List<MenuOption> Options { get; } = new();
    public void AddItem(string text, Action<CCSPlayerController, MenuOption> callback, bool disabled = false) => Options.Add(new(text, callback, disabled));
    public void Display() => plugin.ScreenMenus.Open(this);
    public void Refresh() => plugin.ScreenMenus.Refresh(this);
}

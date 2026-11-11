using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using static CounterStrikeSharp.API.Core.Listeners;
using CssMenus = CounterStrikeSharp.API.Modules.Menu.MenuManager;

namespace cs2_rockthevote.ScreenMenus;

/// <summary>Owns native callbacks and entities for this plugin only.</summary>
public sealed class ScreenMenuService : IDisposable
{
    private readonly Plugin _plugin;
    private readonly CallbackLease _callbacks = new();
    private readonly OwnedSessions<uint, Session> _sessions;
    private readonly List<Session> _retiring = new();
    private readonly PositionBook _positions = new();
    private readonly Dictionary<int, uint> _temporaryPositionOwners = new();
    private readonly string _legacyDirectory;
    private readonly string _positionFile;
    private bool _disposed;
    private bool _started;
    private bool _positionsWritable = true;

    public ScreenMenuService(Plugin plugin)
    {
        _plugin = plugin;
        _sessions = new(Release);
        _legacyDirectory = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "shared", "CS2ScreenMenuAPI");
        _positionFile = Path.Combine(plugin.ModuleDirectory, "player_resolutions.json");
        ReadPositions();
    }

    public void Start()
    {
        if (_started || _disposed) throw new InvalidOperationException("Screen-menu service cannot be started twice.");
        _started = true;
        var plugin = _plugin;
        try
        {
            Listen<OnTick>(Tick);
            Listen<OnMapEnd>(() => { CloseAll(); ClearTemporaryPositions(); });
            Listen<OnMapStart>(_ => { CloseAll(); ClearTemporaryPositions(); });
            Listen<OnClientDisconnect>(slot =>
            {
                foreach (var session in _sessions.Snapshot.Where(x => x.Slot == slot)) { _sessions.Remove(session.Controller); _positions.ForgetTemporary(session.Controller); }
                if (_temporaryPositionOwners.Remove(slot, out var controller)) _positions.ForgetTemporary(controller);
            });
            Listen<CheckTransmit>(FilterTransmit);
            _callbacks.Add(() => plugin.RegisterEventHandler<EventRoundStart>(RoundStart, HookMode.Pre),
                () => plugin.DeregisterEventHandler<EventRoundStart>(RoundStart, HookMode.Pre));
            for (int key = 0; key <= 9; key++)
            {
                string name = $"css_{key}";
                int selected = key;
                CommandInfo.CommandCallback command = (_, _) => { }; // Registers !0...!9 with CSS's chat command router.
                CommandInfo.CommandListenerCallback listener = (player, _) =>
                {
                    if (player == null || !player.IsValid) return HookResult.Continue;
                    var session = _sessions.Get(player.EntityHandle.Raw);
                    if (session == null || session.Settings.Mode == MenuType.Scrollable) return HookResult.Continue;
                    RunSafely(session, () => Choose(session, selected));
                    return HookResult.Handled;
                };
                _callbacks.Add(() => plugin.AddCommand(name, "Screen-menu choice", command), () => plugin.RemoveCommand(name, command));
                _callbacks.Add(() => plugin.AddCommandListener(name, listener), () => plugin.RemoveCommandListener(name, listener, HookMode.Pre));
            }
        }
        catch { Dispose(); throw; }
    }

    private void Listen<T>(T callback) where T : Delegate => _callbacks.Add(() => _plugin.RegisterListener(callback), () => _plugin.RemoveListener(callback));

    public void Open(Menu menu)
    {
        if (_disposed || !menu.Player.IsValid || menu.Player.IsBot || menu.Player.IsHLTV) return;
        try
        {
            var settings = ReadSettings();
            // Releasing first ensures the new session captures the real pre-menu movement state.
            Close(menu.Player);
            if (_retiring.Any(x => x.Controller == menu.Player.EntityHandle.Raw))
                throw new InvalidOperationException("The previous menu is still cleaning up; opening a replacement would capture an invalid movement baseline.");
            CssMenus.CloseActiveMenu(menu.Player);
            if (!menu.IsResolutionChooser && settings.Resolutions.Count > 0 && _positions.Get(menu.Player.SteamID, menu.Player.EntityHandle.Raw) == null)
            { ShowResolutions(menu, settings); return; }
            var pawn = menu.Player.Pawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            var session = new Session(menu, settings, pawn);
            _sessions.Replace(session.Controller, session);
            RunSafely(session, () =>
            {
                var ownPawn = menu.Player.PlayerPawn.Value;
                if (settings.Mode != MenuType.KeyPress && settings.FreezePlayer && menu.Player.PawnIsAlive && ownPawn is { IsValid: true })
                {
                    session.FrozenPawn = ownPawn;
                    session.Movement = new(ownPawn.EntityHandle.Raw, ownPawn.MoveType, ownPawn.ActualMoveType, MoveType_t.MOVETYPE_NONE);
                    session.Movement.Apply(value => ownPawn.MoveType = value, value => ownPawn.ActualMoveType = value);
                    ChangedMovement(ownPawn);
                }
                Render(session);
            });
        }
        catch (Exception e)
        {
            Close(menu.Player);
            _plugin.Logger.LogError(e, "Cannot open screen menu. Check screenmenu.toml and the server schema.");
            menu.Player.PrintToChat("[RockTheVote] Screen menu unavailable. Check the server log/configuration.");
        }
    }

    public void Refresh(Menu menu)
    {
        if (!menu.Player.IsValid) return;
        var session = _sessions.Get(menu.Player.EntityHandle.Raw);
        if (session != null && ReferenceEquals(session.Menu, menu)) RunSafely(session, () => Render(session));
    }
    public void Close(CCSPlayerController player) { if (player.IsValid) _sessions.Remove(player.EntityHandle.Raw); }
    public void CloseOwned(object owner)
    { foreach (var session in _sessions.Snapshot.Where(x => ReferenceEquals(x.Menu.OwnerTag, owner))) _sessions.Remove(session.Controller); }
    private void CloseAll() => _sessions.Clear();
    private HookResult RoundStart(EventRoundStart @event, GameEventInfo info) { CloseAll(); return HookResult.Continue; }

    private bool Current(Session session) => !_disposed && !session.Closed && _sessions.IsCurrent(session.Controller, session);
    private void RunSafely(Session session, Action action)
    {
        if (!Current(session)) return;
        try { action(); }
        catch (Exception e)
        {
            // Fail closed, including partially created mounts/text and movement leases.
            if (_sessions.IsCurrent(session.Controller, session)) _sessions.Remove(session.Controller);
            _plugin.Logger.LogError(e, "Screen-menu session failed and was closed.");
        }
    }

    private void Tick()
    {
        RetryRetiring();
        if (_disposed) return;
        foreach (var session in _sessions.Snapshot)
        {
            RunSafely(session, () =>
            {
                var player = session.Menu.Player;
                var pawn = player.IsValid ? player.Pawn.Value : null;
                if (!player.IsValid || player.EntityHandle.Raw != session.Controller || pawn is not { IsValid: true } || pawn.EntityHandle.Raw != session.Pawn)
                { _sessions.Remove(session.Controller); return; }
                // A chat/HTML menu opened by another component takes control, rather than leaving both input handlers active.
                if (CssMenus.GetActiveMenu(player) != null) { _sessions.Remove(session.Controller); return; }
                UpdateMount(session);
                var pressed = session.Edges.Sample((ulong)player.Buttons);
                if (session.Settings.Mode == MenuType.KeyPress) return;
                var entries = Entries(session).Where(x => x.Enabled).ToArray();
                session.Cursor.Clamp(entries.Length);
                if ((pressed & (ulong)session.Settings.Select) != 0)
                {
                    if (entries.Length > 0) Choose(session, entries[session.Cursor.Selection].Key);
                }
                else if ((pressed & (ulong)session.Settings.Up) != 0)
                { session.Cursor.Move(-1, entries.Length); Sound(session, "ScrollUp"); Render(session); }
                else if ((pressed & (ulong)session.Settings.Down) != 0)
                { session.Cursor.Move(1, entries.Length); Sound(session, "ScrollDown"); Render(session); }
            });
        }
    }

    private string Language(Session session) => session.Menu.Player.GetLanguage().Name;
    private string Label(Session session, string key) => session.Settings.Text(Language(session), key);

    private List<MenuEntry> Entries(Session session)
    {
        return MenuNavigation.Entries(session.Menu.Options.Select(x => (x.Text, x.IsDisabled)).ToArray(), session.Cursor,
            session.Menu.HasExitButon && session.Settings.HasExitOption,
            !session.Menu.IsResolutionChooser && session.Settings.ShowResolutionOption && session.Settings.Resolutions.Count > 0,
            key => Label(session, key));
    }

    private void Choose(Session session, int key)
    {
        if (!Current(session)) return;
        var entry = MenuNavigation.Choice(Entries(session), key);
        if (entry == null) return;
        if (entry.Value.Option >= 0)
        {
            var option = session.Menu.Options[entry.Value.Option];
            Sound(session, "Select");
            // Retire before invoking user code: callbacks may close this vote or open another menu.
            _sessions.Invoke(session.Controller, session, session.Menu.PostSelect == PostSelect.Close,
                () => option.Callback(session.Menu.Player, option), () =>
            {
                if (session.Menu.PostSelect == PostSelect.Reset) session.Cursor.Reset();
                Render(session);
            });
        }
        else if (key is 7 or 8)
        { session.Cursor.Turn(key == 7 ? -1 : 1, session.Menu.Options.Count); Sound(session, key == 7 ? "Prev" : "Next"); Render(session); }
        else if (key == 9) { Sound(session, "Close"); _sessions.Remove(session.Controller); }
        else if (key == 0) ShowResolutions(session.Menu, session.Settings);
    }

    private void ShowResolutions(Menu parent, MenuSettings settings)
    {
        var chooser = new Menu(parent.Player, _plugin)
        { IsResolutionChooser = true, OwnerTag = parent.OwnerTag, Title = settings.Text(parent.Player.GetLanguage().Name, "SelectRes"), PostSelect = PostSelect.Close };
        foreach (var (name, position) in settings.Resolutions)
            chooser.AddItem(name, (player, _) =>
            {
                if (_positions.Set(player.SteamID, player.EntityHandle.Raw, position)) SavePositions();
                else _temporaryPositionOwners[player.Slot] = player.EntityHandle.Raw;
                parent.Display();
            });
        chooser.Display();
    }

    private void ClearTemporaryPositions()
    { _positions.ClearTemporary(); _temporaryPositionOwners.Clear(); }

    private MenuSettings ReadSettings()
    {
        string local = Path.Combine(_plugin.ModuleDirectory, "screenmenu.toml");
        string legacy = Path.Combine(_legacyDirectory, "config.toml");
        string? source = File.Exists(local) ? local : File.Exists(legacy) ? legacy : null;
        return source == null ? new() : MenuSettings.Parse(File.ReadAllText(source));
    }

    private void ReadPositions()
    {
        string source = File.Exists(_positionFile) ? _positionFile : Path.Combine(_legacyDirectory, "player_resolutions.json");
        if (!File.Exists(source)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<ulong, MenuPosition>>(File.ReadAllText(source));
            if (saved != null) foreach (var (id, position) in saved)
                if (id != 0 && position != null && float.IsFinite(position.PositionX) && float.IsFinite(position.PositionY) && Math.Abs(position.PositionX) <= 1000 && Math.Abs(position.PositionY) <= 1000) _positions.Set(id, 0, position);
        }
        catch (Exception e)
        { _positionsWritable = false; _plugin.Logger.LogError(e, "Cannot read menu resolutions. Existing file will not be overwritten."); }
    }
    private void SavePositions()
    {
        if (!_positionsWritable) return;
        try
        {
            string temporary = _positionFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_positions.Saved, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _positionFile, true);
        }
        catch (Exception e) { _plugin.Logger.LogError(e, "Cannot persist screen-menu resolution."); }
    }

    private void Render(Session session)
    {
        UpdateMount(session);
        var front = new StringBuilder(); var back = new StringBuilder();
        string title = ScreenText.Fit(ScreenText.OneLine(session.Menu.Title), 60);
        if (session.Settings.ShowPageCount) title += $" ({session.Cursor.Page + 1}/{session.Cursor.Pages(session.Menu.Options.Count)})";
        front.AppendLine(); back.AppendLine(title);
        var entries = Entries(session);
        session.Cursor.Clamp(entries.Count(x => x.Enabled));
        int selected = 0;
        foreach (var entry in entries)
        {
            string prefix = entry.Enabled && session.Settings.Mode != MenuType.KeyPress && selected == session.Cursor.Selection ? ScreenText.Fit(session.Settings.ScrollPrefix, 6) : " ";
            string number = entry.Enabled || session.Settings.ShowDisabledOptionNum ? $"{entry.Key}. " : "";
            string line = prefix + number + ScreenText.Fit(ScreenText.OneLine(entry.Text), 26);
            if (entry.Enabled) { front.AppendLine(line); back.AppendLine(); selected++; }
            else { front.AppendLine(); back.AppendLine(line); }
        }
        if (session.Settings.Mode != MenuType.KeyPress && session.Settings.ShowControlsInfo)
        {
            string controls = Label(session, "ControlInfo") != "ControlInfo"
                ? string.Format(CultureInfo.InvariantCulture, Label(session, "ControlInfo"), session.Settings.UpKey, session.Settings.DownKey, session.Settings.SelectKey)
                : string.Format(CultureInfo.InvariantCulture, Label(session, "ScrollKeys"), session.Settings.UpKey, session.Settings.DownKey) + "\n" + string.Format(CultureInfo.InvariantCulture, Label(session, "SelectKey"), session.Settings.SelectKey);
            foreach (var line in controls.Split('\n').Take(2)) { front.AppendLine(ScreenText.Fit(ScreenText.OneLine(line), 38)); back.AppendLine(); }
        }
        if (session.Text == null) session.Text = CreateText(session, Color.White, false, 0);
        if (session.Background == null) session.Background = CreateText(session, Color.LightGray, true, -0.001f);
        session.Text.AcceptInput("SetMessage", null, null, ScreenText.Fit(front.ToString()));
        session.Background.AcceptInput("SetMessage", null, null, ScreenText.Fit(back.ToString()));
    }

    private CBasePlayerPawn CameraPawn(Session session)
    {
        var pawn = session.Menu.Player.Pawn.Value ?? throw new InvalidOperationException("Player has no camera pawn.");
        if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE && pawn.ObserverServices is { } observer &&
            observer.ObserverMode == (byte)ObserverMode_t.OBS_MODE_IN_EYE &&
            observer.ObserverTarget.Value is { IsValid: true } target && target.IsPlayerPawn())
            return target.As<CBasePlayerPawn>();
        return pawn;
    }

    private void UpdateMount(Session session)
    {
        var target = CameraPawn(session);
        if (!target.IsValid || target.AbsOrigin == null) throw new InvalidOperationException("Camera target is invalid.");
        Vector3 eye = (Vector3)target.AbsOrigin + new Vector3(0, 0, target.ViewOffset.Z);
        bool changed = session.Camera != target.EntityHandle.Raw || session.Orient is not { IsValid: true };
        if (changed)
        {
            RemoveDisplay(session);
            session.Camera = target.EntityHandle.Raw;
            session.Orient = Utilities.CreateEntityByName<CPointOrient>("point_orient") ?? throw new InvalidOperationException("point_orient creation failed.");
            Track(session, session.Orient);
            session.Orient.Active = true;
            session.Orient.GoalDirection = PointOrientGoalDirectionType_t.eEyesForward;
            session.Orient.DispatchSpawn();
            session.Orient.Teleport(eye, (Vector3)target.V_angle, null);
            session.Orient.AcceptInput("SetParent", target, null, "!activator");
            session.Orient.AcceptInput("SetTarget", target, null, "!activator");
        }
        session.Orient!.Teleport(eye, (Vector3?)null, null);
        // Recreate owned text after camera-target changes. Render does not recurse once the mount is stable.
        if (changed && session.Rendered) Render(session);
        uint fov = session.Menu.Player.DesiredFOV;
        if (session.Fov != fov) { session.Fov = fov; PlaceText(session, target, eye); }
    }

    private CPointWorldText CreateText(Session session, Color color, bool background, float depth)
    {
        var entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext") ?? throw new InvalidOperationException("point_worldtext creation failed.");
        // Record ownership immediately so a partial initialization is still cleaned up.
        Track(session, entity);
        entity.Enabled = true; entity.Fullbright = true;
        entity.FontName = session.Settings.FontName; entity.FontSize = session.Settings.Size;
        entity.Color = color; entity.WorldUnitsPerPx = 0.01f; entity.DepthOffset = depth;
        entity.DrawBackground = background; entity.BackgroundBorderHeight = 0.1f; entity.BackgroundBorderWidth = 0.1f;
        entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
        entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        entity.DispatchSpawn();
        var target = CameraPawn(session);
        Place(entity, session, target, (Vector3)target.AbsOrigin! + new Vector3(0, 0, target.ViewOffset.Z));
        entity.AcceptInput("SetParent", session.Orient, null, "!activator");
        session.Rendered = true;
        return entity;
    }
    private void PlaceText(Session session, CBasePlayerPawn target, Vector3 eye)
    { if (session.Text is { IsValid: true }) Place(session.Text, session, target, eye); if (session.Background is { IsValid: true }) Place(session.Background, session, target, eye); }
    private void Place(CPointWorldText entity, Session session, CBasePlayerPawn target, Vector3 eye)
    {
        // Use the mount's current rotation, not a second independent eye rotation. The native orient then rotates the local plane once.
        Vector3 angles = (Vector3)(session.Orient?.AbsRotation ?? target.V_angle);
        float pitch = angles.X * MathF.PI / 180, yaw = angles.Y * MathF.PI / 180;
        Vector3 forward = new(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        Vector3 right = new(MathF.Sin(yaw), -MathF.Cos(yaw), 0);
        Vector3 up = new(MathF.Sin(pitch) * MathF.Cos(yaw), MathF.Sin(pitch) * MathF.Sin(yaw), MathF.Cos(pitch));
        float fov = session.Menu.Player.DesiredFOV == 0 ? 90 : Math.Clamp(session.Menu.Player.DesiredFOV, 1, 179);
        float scale = MathF.Tan(fov * MathF.PI / 360);
        MenuPosition position = _positions.Get(session.Menu.Player.SteamID, session.Controller) ?? session.Settings.Position;
        entity.Teleport(eye + forward * 7 + scale * (right * position.PositionX + up * position.PositionY), new Vector3(0, angles.Y + 270, 90 - angles.X), null);
    }

    private void FilterTransmit(CCheckTransmitInfoList infoList)
    {
        var sessions = _sessions.Snapshot.Concat(_retiring).ToArray();
        foreach (var (info, recipient) in infoList)
            foreach (var session in sessions)
            {
                if (!session.Closed && recipient is { IsValid: true } && recipient.EntityHandle.Raw == session.Controller) continue;
                foreach (var resource in session.Resources.Snapshot)
                {
                    var entity = resource.Resolve();
                    if (entity == null) continue;
                    info.TransmitEntities.Remove(entity.Index);
                    // Optional in the host for normal clients. Default equality checks the managed pointer value without dereferencing it.
                    if (!info.TransmitAlways.Equals(default(CFixedBitVecBase))) info.TransmitAlways.Remove(entity.Index);
                }
            }
    }
    private void Sound(Session session, string key)
    {
        string name = session.Settings.Sounds[key];
        if (!string.IsNullOrWhiteSpace(name) && session.Settings.Volume > 0)
            session.Menu.Player.EmitSound(name, new RecipientFilter(session.Menu.Player), session.Settings.Volume);
    }
    private void Release(Session session)
    {
        session.Closed = true;
        if (!_retiring.Contains(session)) _retiring.Add(session);
        Cleanup(session);
    }
    private void RetryRetiring()
    { foreach (var session in _retiring.ToArray()) Cleanup(session); }
    private void Cleanup(Session session)
    {
        // Failed deletion remains owned (and transmit-filtered) until a later retry succeeds.
        try { RemoveDisplay(session); }
        catch (Exception e) { _plugin.Logger.LogWarning(e, "Screen-menu deletion will be retried."); }
        try
        {
            if (session.Movement is { Pending: true } movement)
            {
                if (session.FrozenPawn is not { IsValid: true } pawn) movement.Abandon();
                else
                {
                    movement.Restore(pawn.EntityHandle.Raw, () => pawn.MoveType, () => pawn.ActualMoveType,
                        value => { pawn.MoveType = value; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType"); },
                        value => { pawn.ActualMoveType = value; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_nActualMoveType"); });
                }
            }
        }
        catch (Exception e) { _plugin.Logger.LogWarning(e, "Screen-menu movement restoration will be retried."); }
        if (session.Resources.Snapshot.Length == 0 && session.Movement?.Pending != true) _retiring.Remove(session);
    }
    private static void ChangedMovement(CBaseEntity pawn)
    { Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType"); Utilities.SetStateChanged(pawn, "CBaseEntity", "m_nActualMoveType"); }
    private static void Track(Session session, CBaseEntity entity)
    {
        uint raw = entity.EntityHandle.Raw;
        // CSS379 NativeEntity already captures this raw handle; IsValid freshly resolves and verifies its serial.
        session.Resources.Add(new(raw, _ => entity.IsValid ? entity : null, value => value.EntityHandle.Raw));
    }

    private void RemoveDisplay(Session session)
    {
        session.Resources.ReleaseAll(resource =>
        {
            var entity = resource.Resolve();
            if (entity == null) return;
            if (entity is CPointWorldText text)
            {
                // Stop drawing before removal. Failure still leaves the resource in the ownership/transmit registry.
                text.Enabled = false;
                Utilities.SetStateChanged(text, "CPointWorldText", "m_bEnabled");
            }
            entity.Remove();
        });
        session.Text = session.Background = null; session.Orient = null;
    }
    public void Dispose()
    {
        _disposed = true;
        ClearTemporaryPositions();
        CloseAll();
        for (int attempt = 0; attempt < 3 && _retiring.Count != 0; attempt++) RetryRetiring();
        if (_retiring.Count != 0) throw new InvalidOperationException("Screen-menu cleanup is incomplete; native resources remain owned for retry. Do not treat unload as successful.");
        for (int attempt = 0; ; attempt++)
        {
            try { _callbacks.Dispose(); break; }
            catch when (attempt < 2) { }
        }
    }

    private sealed class Session(Menu menu, MenuSettings settings, CBasePlayerPawn pawn)
    {
        public Menu Menu { get; } = menu;
        public MenuSettings Settings { get; } = settings;
        public uint Controller { get; } = menu.Player.EntityHandle.Raw;
        public int Slot { get; } = menu.Player.Slot;
        public uint Pawn { get; } = pawn.EntityHandle.Raw;
        public uint Camera;
        public uint Fov;
        public bool Closed;
        public bool Rendered;
        public MenuCursor Cursor { get; } = new();
        public ButtonEdges Edges { get; } = new((ulong)menu.Player.Buttons);
        public CPointOrient? Orient;
        public CPointWorldText? Text;
        public CPointWorldText? Background;
        public OwnedResources<IdentityLease<CBaseEntity>> Resources { get; } = new();
        public CCSPlayerPawn? FrozenPawn;
        public MovementLease<MoveType_t>? Movement;
    }
}

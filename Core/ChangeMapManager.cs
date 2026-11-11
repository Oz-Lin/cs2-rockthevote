using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundEndMapChanger(EventRoundEnd @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundStartMapChanger(EventRoundStart @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }
    }

    public class ChangeMapManager : IPluginDependency<Plugin, Config>
    {
        private Plugin? _plugin;
        private readonly StringLocalizer _localizer;
        private readonly PluginState _pluginState;
        private readonly MapTransitionPolicy _transition = new();
        private readonly NextLevelLease _nextLevel = new();
        private Map[] _maps = Array.Empty<Map>();
        private Config? _config;
        private bool _stopped;
        private readonly OneShotTimerSlot<CounterStrikeSharp.API.Modules.Timers.Timer> _changeTimer = new();
        private readonly OneShotTimerSlot<CounterStrikeSharp.API.Modules.Timers.Timer> _matchEndTimer = new();
        private const string DEFAULT_PREFIX = "rtv.prefix";
        public string? NextMap => _transition.Current?.Target.Name;

        public ChangeMapManager(StringLocalizer localizer, PluginState pluginState, MapLister mapLister)
        {
            _localizer = localizer; _pluginState = pluginState;
            mapLister.EventMapsLoaded += OnMapsLoaded;
        }
        public void OnMapsLoaded(object? sender, Map[] maps) => _maps = maps;

        private void AssignNextLevel(MapTarget target, bool localMap)
        {
            if (!target.UsesNextLevel(localMap)) return;
            var variable = CounterStrikeSharp.API.Modules.Cvars.ConVar<string>.Find("nextlevel")
                ?? throw new InvalidOperationException("The nextlevel convar is unavailable.");
            // A timer reaffirms only its unchanged current assignment; a new schedule releases first.
            if (_nextLevel.Pending)
                NextLevelPolicy.Assign(target, localMap, () => variable.ValueAsString, value => variable.ValueAsString = value);
            else _nextLevel.Assign(target, localMap, () => variable.ValueAsString, value => variable.ValueAsString = value);
        }
        private void RestoreNextLevel()
        {
            if (!_nextLevel.Pending) return;
            var variable = CounterStrikeSharp.API.Modules.Cvars.ConVar<string>.Find("nextlevel")
                ?? throw new InvalidOperationException("Cannot restore owned nextlevel: convar unavailable.");
            _nextLevel.Release(() => variable.ValueAsString, value => variable.ValueAsString = value);
        }
        public void ScheduleMapChange(string map, bool mapEnd = false, string prefix = DEFAULT_PREFIX)
        {
            if (_stopped) throw new InvalidOperationException("Map changer is stopped.");
            var target = MapTarget.Resolve(_maps, map); // Snapshot before a map-list reload or delayed callback.
            // Invalidate old callbacks before any cleanup/native write can fail.
            CancelScheduledChange();
            try { AssignNextLevel(target, Server.IsMapValid(target.Name)); }
            catch (Exception e)
            {
                _plugin?.Logger.LogError(e, "Cannot set nextlevel for selected map {Map}.", target.Name);
                try { RestoreNextLevel(); } catch (Exception cleanup) { throw new AggregateException(e, cleanup); }
                throw;
            }
            _transition.Schedule(target, mapEnd, prefix);
            _pluginState.MapChangeScheduled = true;
        }
        public void OnMapStart(string map)
        {
            // CSS has already deleted STOP_ON_MAPCHANGE timers before this callback.
            _transition.Reset(); _pluginState.MapChangeScheduled = false; _nextLevel.Abandon();
            _changeTimer.Abandon(timer => PluginTimers.Forget(_plugin, timer));
            _matchEndTimer.Abandon(timer => PluginTimers.Forget(_plugin, timer));
        }
        private void CancelTimers()
        {
            CleanupActions.Run(() => _changeTimer.Cancel(timer => PluginTimers.Kill(_plugin, timer)),
                () => _matchEndTimer.Cancel(timer => PluginTimers.Kill(_plugin, timer)));
        }
        private void ResetForMapChange()
        {
            _nextLevel.Abandon(); // The previous map consumed it; never restore into a new map.
            _transition.Reset(); _pluginState.MapChangeScheduled = false;
            CancelTimers();
        }
        public void CancelScheduledChange()
        {
            _transition.Reset(); _pluginState.MapChangeScheduled = false;
            CleanupActions.Run(CancelTimers, RestoreNextLevel);
        }
        public void Stop() { _stopped = true; CancelScheduledChange(); }

        public bool ChangeNextMap(bool mapEnd = false)
        {
            if (_stopped || !_pluginState.MapChangeScheduled) return false;
            var ticket = _transition.Take(mapEnd);
            if (ticket == null) return false;
            _pluginState.MapChangeScheduled = false;
            Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(ticket.Prefix, "general.changing-map", ticket.Target.Name));
            CounterStrikeSharp.API.Modules.Timers.Timer? ownedTimer = null;
            ownedTimer = _plugin!.AddTimer(3.0F, () =>
            {
                _changeTimer.Retire(ownedTimer!);
                PluginTimers.Forget(_plugin, ownedTimer!);
                if (_stopped || !_transition.TryExecute(ticket)) return;
                try
                {
                    bool localMap = Server.IsMapValid(ticket.Target.Name);
                    AssignNextLevel(ticket.Target, localMap);
                    Server.ExecuteCommand(ticket.Target.ChangeCommand(localMap));
                }
                catch (Exception e) { _plugin.Logger.LogError(e, "Map transition failed for {Map}.", ticket.Target.Name); }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            _changeTimer.Assign(ownedTimer);
            return true;
        }
        public void OnConfigParsed(Config config) => _config = config;
        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin; _stopped = false;
            plugin.RegisterListener<CounterStrikeSharp.API.Core.Listeners.OnMapEnd>(ResetForMapChange);
            plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) =>
            {
                var ticket = _transition.Current;
                if (!_stopped && _pluginState.MapChangeScheduled && ticket is { AtMatchEnd: true } && _transition.Pending)
                {
                    float configured = _config?.EndOfMapVote.DelayToChangeInTheEnd ?? 6F;
                    float delay = float.IsFinite(configured) ? Math.Max(0, configured - 3F) : 3F;
                    _matchEndTimer.Cancel(timer => PluginTimers.Kill(_plugin, timer));
                    CounterStrikeSharp.API.Modules.Timers.Timer? ownedTimer = null;
                    ownedTimer = plugin.AddTimer(delay, () =>
                    {
                        _matchEndTimer.Retire(ownedTimer!);
                        PluginTimers.Forget(_plugin, ownedTimer!);
                        if (!_stopped && _transition.IsCurrent(ticket)) ChangeNextMap(true);
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    _matchEndTimer.Assign(ownedTimer);
                }
                return HookResult.Continue;
            }, HookMode.Post);
        }
    }
}

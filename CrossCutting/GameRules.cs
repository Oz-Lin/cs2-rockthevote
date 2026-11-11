using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public class GameRules : IPluginDependency<Plugin, Config>
    {
        CCSGameRules? _gameRules = null;
        private Plugin? _plugin;
        private bool _stopped;
        private long _generation;
        private readonly OneShotTimerSlot<Timer> _refresh = new();

        public bool IsReady => !_stopped && _gameRules != null;

        public void SetGameRules()
        {
            if (!_stopped)
                _gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        }

        public void SetGameRulesAsync()
        {
            _gameRules = null;
            long generation = ++_generation;
            _refresh.Cancel(timer => PluginTimers.Kill(_plugin, timer));
            if (_stopped || _plugin == null) return;
            Timer? ownedTimer = null;
            ownedTimer = _plugin.AddTimer(1.0F, () =>
            {
                _refresh.Retire(ownedTimer!);
                PluginTimers.Forget(_plugin, ownedTimer!);
                if (!_stopped && generation == _generation) SetGameRules();
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            _refresh.Assign(ownedTimer);
        }

        private void EndMap()
        {
            ++_generation; _stopped = true; _gameRules = null;
            _refresh.Cancel(timer => PluginTimers.Kill(_plugin, timer));
        }
        public void Stop() => EndMap();

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin; _stopped = false;
            plugin.RegisterListener<CounterStrikeSharp.API.Core.Listeners.OnMapEnd>(EndMap);
            SetGameRulesAsync();
            plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            plugin.RegisterEventHandler<EventRoundAnnounceWarmup>(OnAnnounceWarmup);
        }

        public float GameStartTime => _gameRules?.GameStartTime ?? 0;

        public void OnMapStart(string map)
        {
            ++_generation; _gameRules = null;
            _refresh.Abandon(timer => PluginTimers.Forget(_plugin, timer));
            _stopped = false;
            SetGameRulesAsync();
        }


        public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            SetGameRules();
            return HookResult.Continue;
        }

        public HookResult OnAnnounceWarmup(EventRoundAnnounceWarmup @event, GameEventInfo info)
        {
            SetGameRules();
            return HookResult.Continue;
        }

        public MatchPauseSnapshot? ReadPauseState() => _gameRules == null ? null : new(
            _gameRules.MatchWaitingForResume,
            _gameRules.TechnicalTimeOut || _gameRules.TerroristTimeOutActive || _gameRules.CTTimeOutActive);

        public bool WarmupRunning => _gameRules?.WarmupPeriod ?? false;

        public int TotalRoundsPlayed => _gameRules?.TotalRoundsPlayed ?? 0;

        public int RoundTime
        {
            get
            {
                return _gameRules?.RoundTime ?? 0;
            }

            set
            {
                if (_gameRules != null) _gameRules.RoundTime = value;
            }

        }
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public class GameRules : IPluginDependency<Plugin, Config>
    {
        CCSGameRules? _gameRules = null;

        public void SetGameRules() => _gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;

        public void SetGameRulesAsync()
        {
            _gameRules = null;
            //new Timer(1.0F, () =>
            //{
            //    SetGameRules();
            //});
            new Timer(1.0F, SetGameRules); //change to method group
        }

        public void OnLoad(Plugin plugin)
        {
            SetGameRulesAsync();
            plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            plugin.RegisterEventHandler<EventRoundAnnounceWarmup>(OnAnnounceWarmup);
        }

        public float GameStartTime => _gameRules?.GameStartTime ?? 0;

        public void OnMapStart(string map)
        {
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

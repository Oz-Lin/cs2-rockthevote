using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.DependencyInjection;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public class EndOfMapVote : IPluginDependency<Plugin, Config>
    {
        private readonly StringLocalizer? _localizer;
        private TimeLimitManager _timeLimit;
        private MaxRoundsManager _maxRounds;
        private PluginState _pluginState;
        private GameRules _gameRules;
        private EndMapVoteManager _voteManager;
        private EndOfMapConfig _config = new();
        private Timer? _timer;
        private bool deathMatch => _gameMode?.GetPrimitiveValue<int>() == 2 && _gameType?.GetPrimitiveValue<int>() == 1;
        private ConVar? _gameType;
        private ConVar? _gameMode;

        // overload for multilang support
        public EndOfMapVote(StringLocalizer localizer, TimeLimitManager timeLimit, MaxRoundsManager maxRounds, PluginState pluginState, GameRules gameRules, EndMapVoteManager voteManager)
        {
            _localizer = localizer;
            _timeLimit = timeLimit;
            _maxRounds = maxRounds;
            _pluginState = pluginState;
            _gameRules = gameRules;
            _voteManager = voteManager;
        }
        public EndOfMapVote(TimeLimitManager timeLimit, MaxRoundsManager maxRounds, PluginState pluginState, GameRules gameRules, EndMapVoteManager voteManager)
        {
            //_localizer = new StringLocalizer();
            _timeLimit = timeLimit;
            _maxRounds = maxRounds;
            _pluginState = pluginState;
            _gameRules = gameRules;
            _voteManager = voteManager;
        }

        bool CheckMaxRounds()
        {
#if DEBUG
            // Server.PrintToChatAll($"[RTV debug] Remaining rounds {_maxRounds.RemainingRounds}, remaining wins: {_maxRounds.RemainingWins}, triggerBefore {_config.TriggerRoundsBeforeEnd}");
#endif
            // Prevent triggering vote too early
            if (_gameRules.TotalRoundsPlayed < 1) // This line is a newly added safeguard
                return false;
            if (_maxRounds.UnlimitedRounds)
                return false;
            if (_maxRounds.RemainingRounds <= _config.TriggerRoundsBeforeEnd)
                return true;
            return _maxRounds.CanClinch && _maxRounds.RemainingWins <= _config.TriggerRoundsBeforeEnd;
        }


        bool CheckTimeLeft()
        {
            return !_timeLimit.UnlimitedTime && _timeLimit.TimeRemaining <= _config.TriggerSecondsBeforeEnd;
        }

        public void StartVote()
        {
            KillTimer();
            if (_config.Enabled)
            {
                if (_pluginState.EofVoteHappening)
                {
                    Server.PrintToChatAll($"[RockTheVote] End of map vote is already in progress. Cannot start a new vote.");
                    return;
                }
                _voteManager.StartVote(_config);
            }
        }

        public void OnMapStart(string map)
        {
            KillTimer();
        }

        void KillTimer()
        {
            _timer?.Kill();
            _timer = null;
        }



        public void OnLoad(Plugin plugin)
        {
            _gameMode = ConVar.Find("game_mode");
            _gameType = ConVar.Find("game_type");

            void MaybeStartTimer()
            {
                KillTimer();
                if (!_timeLimit.UnlimitedTime && _config.Enabled)
                {
                    _timer = plugin.AddTimer(1.0F, () =>
                    {
                        if (_gameRules is not null && !_gameRules.WarmupRunning && !_pluginState.DisableCommands && _timeLimit.TimeRemaining > 0)
                        {
                            if (CheckTimeLeft() && !_pluginState.EofVoteHappening)
                                StartVote();
                        }
                    }, TimerFlags.REPEAT);
                }
            }

            plugin.RegisterEventHandler<EventRoundStart>((ev, info) =>
            {
                if (!_pluginState.DisableCommands && !_gameRules.WarmupRunning && CheckMaxRounds() && _config.Enabled && !_pluginState.EofVoteHappening)
                    StartVote();
                else if (deathMatch)
                {
                    MaybeStartTimer();
                }

                return HookResult.Continue;
            });

            plugin.RegisterEventHandler<EventRoundAnnounceMatchStart>((ev, info) =>
            {
                MaybeStartTimer();
                return HookResult.Continue;
            });
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.EndOfMapVote;
        }
    }
}

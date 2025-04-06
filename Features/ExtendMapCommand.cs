using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [ConsoleCommand("ext", "Vote to extend current map")]
        [ConsoleCommand("css_ext", "Vote to extend current map")]
        //[ConsoleCommand("extend", "Vote to extend current map")] // conflict with admin extend <minutes>
        //[ConsoleCommand("css_extend", "Vote to extend current map")] // conflict with admin extend <minutes>
        [ConsoleCommand("extendmap", "Vote to extend current map")]
        [ConsoleCommand("css_extendmap", "Vote to extend current map")]
        public void OnExtend(CCSPlayerController? player, CommandInfo? command)
        {
            _extendMapManager.CommandHandler(player!);
        }
    }

    public class ExtendMapCommand : IPluginDependency<Plugin, Config>
    {
        private readonly StringLocalizer _localizer;
        private readonly GameRules _gameRules;
        private TimeLimitManager _timeLimitManager;
        private RoundLimitManager _roundLimitManager;
        private ExtendRoundTimeManager _extendRoundTimeManager;
        private PluginState _pluginState;
        private ExtendMapConfig _config = new();
        private EndOfMapConfig? _eomConfig = new();
        private AsyncVoteManager? _voteManager;
        private int _totalExtendLimit;

        public bool VotesAlreadyReached => _voteManager!.VotesAlreadyReached;

        public ExtendMapCommand(GameRules gameRules, IStringLocalizer localizer, PluginState pluginState, TimeLimitManager timeLimitManager, RoundLimitManager roundLimitManager, ExtendRoundTimeManager extendRoundTimeManager)
        {
            _localizer = new StringLocalizer(localizer, "extendmap.prefix");
            _gameRules = gameRules;
            _pluginState = pluginState;
            _timeLimitManager = timeLimitManager;
            _roundLimitManager = roundLimitManager;
            _extendRoundTimeManager = extendRoundTimeManager;
        }

        public void OnMapStart(string map)
        {
            _voteManager!.OnMapStart(map);
        }

        public void CommandHandler(CCSPlayerController? player)
        {
            if (player is null || !player.IsValid || player.IsBot)
                return;

            /*if (_pluginState.DisableCommands || !_config.Enabled)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }*/

            if (!_config.Enabled)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }

            if (_gameRules.WarmupRunning)
            {
                if (!_config.EnabledInWarmup)
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                    return;
                }
            }
            else if (_config.MinRounds > 0 && _config.MinRounds > _gameRules.TotalRoundsPlayed)
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-rounds", _config.MinRounds));
                return;
            }

            if (ServerManager.ValidPlayerCount() < _config!.MinPlayers)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-players", _config!.MinPlayers));
                return;
            }

            if (_eomConfig!.ExtendLimit <= 0 && _eomConfig.ExtendLimit != -1)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("extendmap.no-extends-left") + _localizer.Localize("extendtime.extendsleft", _eomConfig.ExtendLimit, _totalExtendLimit));
                return;
            }

            VoteResult result = _voteManager!.AddVote(player.UserId!.Value);
            switch (result.Result)
            {
                case VoteResultEnum.Added:
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendmap.player-voted", player.PlayerName)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    break;
                case VoteResultEnum.AlreadyAddedBefore:
                    player.PrintToChat($"{_localizer.LocalizeWithPrefix("extendmap.already-voted")} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    break;
                case VoteResultEnum.VotesAlreadyReached:
                    player.PrintToChat($"{_localizer.LocalizeWithPrefix("extendmap.already-extended", _config.ExtendTimeStep)}");
                    break;
                case VoteResultEnum.VotesReached:
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendmap.player-voted", player.PlayerName)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendmap.map-extended", _config.ExtendTimeStep)}");
                    ApplyExtend();
                    break;
            }
        }

        void ApplyExtend()
        {
            if (_config.ExtendRoundStep > 0)
            {
                _roundLimitManager.RoundsRemaining = _roundLimitManager.RoundLimitValue + _config.ExtendRoundStep;
            }
            if (_config.ExtendTimeStep > 0 && _config.RoundBased)
            {
                _extendRoundTimeManager.ExtendMapTimeLimit(_config.ExtendTimeStep, _timeLimitManager, _gameRules);
            }
            else
            {
                _extendRoundTimeManager.ExtendRoundTime(_config.ExtendTimeStep, _timeLimitManager, _gameRules);
            }
            if (_eomConfig!.ExtendLimit != -1)
            {
                _eomConfig!.ExtendLimit--;
                Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendtime.extendsleft", _eomConfig.ExtendLimit, _totalExtendLimit)}");
            }
            _voteManager!.ResetVotes();
        }

        public void PlayerDisconnected(CCSPlayerController? player)
        {
            if (player?.UserId != null)
                _voteManager!.RemoveVote(player.UserId.Value);
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.ExtendMapVote;
            _voteManager = new AsyncVoteManager(_config);
            _eomConfig = config.EndOfMapVote;
            _totalExtendLimit = config.EndOfMapVote.ExtendLimit;
        }
    }
}

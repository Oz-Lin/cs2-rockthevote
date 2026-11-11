using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;
using System.Data;
using System.Text;
using Microsoft.Extensions.Logging;
using static CounterStrikeSharp.API.Core.Listeners;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using cs2_rockthevote.ScreenMenus;
//using CS2ScreenMenuAPI.Enums;
using CounterStrikeSharp.API.Modules.Entities;
//using CS2ScreenMenuAPI.Internal;

namespace cs2_rockthevote
{
    public class ExtendRoundTimeManager : IPluginDependency<Plugin, Config>
    {
        const int MAX_OPTIONS_HUD_MENU = 6;
        public ExtendRoundTimeManager(IStringLocalizer stringLocalizer, PluginState pluginState, TimeLimitManager timeLimitManager, GameRules gameRules)
        {
            _localizer = new StringLocalizer(stringLocalizer, "extendtime.prefix");
            _pluginState = pluginState;
            _timeLimitManager = timeLimitManager;
            _gameRules = gameRules;
        }

        private readonly StringLocalizer _localizer;
        private PluginState _pluginState;
        private TimeLimitManager _timeLimitManager;
        private Timer? Timer;
        private GameRules _gameRules;

        Dictionary<string, int> Votes = new();
        Dictionary<uint, string> PlayerVotes = new();
        private long _ballotGeneration;
        private readonly Dictionary<uint, ChatMenu> _chatMenus = new();
        int timeLeft = -1;

        private IExtendMapConfig? _config = null;

        private int _totalExtendLimit;
        private int _canVote = 0;
        private Plugin? _plugin;
        private VipExtendMapConfig? _veConfig;

        public bool VoteInProgress => timeLeft >= 0;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterListener<OnTick>(VoteDisplayTick);
            plugin.RegisterListener<OnMapEnd>(Stop);
        }

        public void OnConfigParsed(Config config)
        {
            _veConfig = config.VipExtendMapVote;
            _totalExtendLimit = config.VipExtendMapVote.ExtendLimit;
        }

        /*
         *  Need a better way to handle this, as the plugin reload approach breaks map cooldown.
         *
         */
        public void Stop()
        {
            _ballotGeneration++;
            _pluginState.ExtendTimeVoteHappening = false;
            timeLeft = -1;
            KillTimer();
        }

        public void OnMapStart(string map)
        {
            // STOP timer deletion precedes managed map start in CSS379.
            if (Timer != null) PluginTimers.Forget(_plugin, Timer);
            Timer = null;
            _ballotGeneration++;
            _pluginState.ExtendTimeVoteHappening = false;
            // Preserve the configured VIP extension limit across map changes.
            _totalExtendLimit = _veConfig?.ExtendLimit ?? new Config().VipExtendMapVote.ExtendLimit;
            _pluginState.ExtendsLeft = _totalExtendLimit;

            Votes.Clear();
            PlayerVotes.Clear();
            timeLeft = 0;
            KillTimer();
        }

        // VIP Extend Start
        public void ExtendTimeVoted(CCSPlayerController player, string voteResponse)
        {
            if (!_pluginState.ExtendTimeVoteHappening || !player.IsValid || !ExtensionVotePolicy.Cast(Votes, PlayerVotes, player.EntityHandle.Raw, voteResponse))
                return;

            player.PrintToCenter(_localizer.LocalizeWithPrefix("extendtime.you-voted", voteResponse));
            if (Votes.Select(x => x.Value).Sum() >= _canVote)
            {
                ExtendTimeVote();
            }
        }

        public void RevokeVote(CCSPlayerController player)
        {
            if (_pluginState.ExtendTimeVoteHappening && player.IsValid && ExtensionVotePolicy.Revoke(Votes, PlayerVotes, player.EntityHandle.Raw))
            {
                player.PrintToCenter(_localizer.LocalizeWithPrefix("general.vote-revoked-choose-again"));
                ShowVoteMenu(player); // Bring back the vote menu
            }
            else
            {
                player.PrintToCenter(_localizer.LocalizeWithPrefix("general.no-vote-to-revoke"));
            }
        }

        private void ShowVoteMenu(CCSPlayerController player)
        {
            if (_config!.HudMenu == 2)
            {
                var menu = CreateVoteScreenMenu(player);
                menu.Display();
            }
            if (_config.HudMenu is 0 or 1)
            {
                var menu = CreateVoteMenu();
                _chatMenus[player.EntityHandle.Raw] = menu;
                MenuManager.OpenChatMenu(player, menu);
            }
        }

        private ChatMenu CreateVoteMenu()
        {
            long generation = _ballotGeneration;
            ChatMenu menu = new(_localizer.Localize("extendtime.hud.menu-title")) { PostSelectAction = PostSelectAction.Nothing };

            var answers = new List<string>() { "Yes", "No" };

            foreach (var answer in answers)
            {
                if (!Votes.ContainsKey(answer))
                {
                    Votes[answer] = 0;
                }
                menu.AddMenuOption(answer, (player, option) =>
                {
                    if (generation != _ballotGeneration || !_pluginState.ExtendTimeVoteHappening) return;
                    ExtendTimeVoted(player, answer);
                    if (MenuManager.GetActiveMenu(player) is BaseMenuInstance instance && ReferenceEquals(instance.Menu, menu)) MenuManager.CloseActiveMenu(player);
                });
            }

            return menu;
        }

        private Menu CreateVoteScreenMenu(CCSPlayerController player)
        {
            long generation = _ballotGeneration;
            Menu menu = new Menu(player, _plugin!)
            {
                OwnerTag = this,
                Title = _localizer.Localize("extendtime.hud.menu-title"),
                PostSelect = PostSelect.Close,
                HasExitButon = true
            };

            var answers = new List<string>() { "Yes", "No" };

            foreach (var answer in answers)
            {
                ExtensionVotePolicy.EnsureOptions(Votes);
                menu.AddItem(answer, (player, option) =>
                {
                    if (generation != _ballotGeneration || !_pluginState.ExtendTimeVoteHappening) return;
                    ExtendTimeVoted(player, answer);
                    //MenuManager.CloseActiveMenu(player);
                });
            }

            return menu;
        }

        void KillTimer() => KillTimer(Utilities.GetPlayers);

        void KillTimer(Func<List<CCSPlayerController>> getPlayers)
        {
            timeLeft = -1;
            CleanupActions.Run(() =>
            {
                try
                {
                    _plugin?.ScreenMenus?.CloseOwned(this);
                    // Still release screen menus and timers when no chat menu exists.
                    if (_chatMenus.Count > 0)
                    foreach (var player in getPlayers())
                        if (player.IsValid && _chatMenus.TryGetValue(player.EntityHandle.Raw, out var menu) && MenuManager.GetActiveMenu(player) is BaseMenuInstance instance && ReferenceEquals(instance.Menu, menu))
                            MenuManager.CloseActiveMenu(player);
                }
                finally { _chatMenus.Clear(); }
            }, () =>
            {
                if (Timer != null) { PluginTimers.Kill(_plugin, Timer); Timer = null; }
            });
        }

        void PrintCenterTextAll(string text)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid)
                {
                    player.PrintToCenter(text);
                }
            }
        }

        public void VoteDisplayTick()
        {
            if (timeLeft < 0)
                return;

            int index = 1;
            StringBuilder stringBuilder = new();
            stringBuilder.AppendFormat($"<b>{_localizer.Localize("extendtime.hud.hud-timer", timeLeft)}</b>");
            if (_config!.HudMenu == 0)
                foreach (var kv in Votes.OrderByDescending(x => x.Value).Take(MAX_OPTIONS_HUD_MENU).Where(x => x.Value > 0))
                {
                    stringBuilder.AppendFormat($"<br>{kv.Key} <font color='green'>({kv.Value})</font>");
                }
            if (_config!.HudMenu > 0)
                foreach (var kv in Votes.Take(MAX_OPTIONS_HUD_MENU))
                {
                    stringBuilder.AppendFormat($"<br><font color='yellow'>!{index++}</font> {kv.Key} <font color='green'>({kv.Value})</font>");
                }

            foreach (CCSPlayerController player in ServerManager.ValidPlayers())
            {
                player.PrintToCenterHtml(stringBuilder.ToString());
            }
        }

        void ExtendTimeVote()
        {
            if (!_pluginState.ExtendTimeVoteHappening) return;
            _pluginState.ExtendTimeVoteHappening = false;
            KillTimer();

            var outcome = ExtensionVotePolicy.Evaluate(Votes, Random.Shared.Next);
            decimal totalVotes = outcome.TotalVotes;
            decimal percent = outcome.WinningPercentage;

            if (totalVotes > 0)
            {
                if (!outcome.Passed)
                {
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.failed", percent, totalVotes));
                }
                else
                {
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed", _config!.ExtendTimeStep, percent, totalVotes));
                }
            }
            else
            {
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended-no-votes"));
            }

            if (!outcome.Passed)
            {
                // Do nothing, vote did not pass
                PrintCenterTextAll(_localizer.Localize("extendtime.hud.finished", "not be extended."));
            }
            else
            {
                // Extend mp_timelimit
                // Use ExtendMapTimeLimit for round-based gamemodes (ze/zm/normal gunfights etc), and ExtendRoundTime for non-round-based gamemodes (bhop/surf/kz/deathmatch etc)
                if (_veConfig != null && _veConfig.RoundBased == true)
                {
                    ExtendMapTimeLimit(_config!.ExtendTimeStep, _timeLimitManager, _gameRules);
                }
                else
                {
                    ExtendRoundTime(_config!.ExtendTimeStep, _timeLimitManager, _gameRules);
                }

                PrintCenterTextAll(_localizer.Localize("extendtime.hud.finished", "be extended."));
                if (_pluginState.ExtendsLeft != -1)
                {
                    _pluginState.ExtendsLeft -= 1;
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.extendsleft", _pluginState.ExtendsLeft, _totalExtendLimit));
                }
                _pluginState.CommandsDisabled = false;
            }

            _pluginState.ExtendTimeVoteHappening = false;
        }

        public void StartVote(IExtendMapConfig config)
        {
            if (_pluginState.ExtendTimeVoteHappening) return;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            long generation = ++_ballotGeneration;
            Votes.Clear();
            ExtensionVotePolicy.EnsureOptions(Votes);
            PlayerVotes.Clear();
            _pluginState.ExtendTimeVoteHappening = true;
            timeLeft = _config.VoteDuration;

            _canVote = ServerManager.ValidPlayerCount();
            foreach (var player in ServerManager.ValidPlayers())
            {
                if (_config.HudMenu == 2)
                {
                    var sMenu = CreateVoteScreenMenu(player);
                    //foreach (var player in ServerManager.ValidPlayers())
                    sMenu.Display();
                }

                if (_config.HudMenu is 0 or 1)
                {
                    var cMenu = CreateVoteMenu();
                    _chatMenus[player.EntityHandle.Raw] = cMenu;
                    //foreach (var player in ServerManager.ValidPlayers())
                    MenuManager.OpenChatMenu(player, cMenu);
                }
            }

            timeLeft = _config.VoteDuration;
            Timer = _plugin!.AddTimer(1.0F, () =>
            {
                if (generation != _ballotGeneration || !_pluginState.ExtendTimeVoteHappening) return;
                if (timeLeft <= 0)
                {
                    ExtendTimeVote();
                }
                else
                    timeLeft--;
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }
        // VIP Extend End

        // ExtendRoundTime: Extend the current round time for non-round-based gamemodes (bhop/surf/kz/deathmatch etc)
        public bool ExtendRoundTime(int minutesToExtendBy, TimeLimitManager timeLimitManager, GameRules gameRules)
        {
            try
            {
                /* IDK why, razpberry decided to sync the mp_timelimit by current mp_roundtime here.
                 Maybe suits the use case for bhup/surf/kz, but bugged the use case that depends on rounds.

                 Case 1: When !timeleft is around 10 minutes and the current round time is 55 minutes remaining, after !rtv extends by 20 minutes, the current map round time is 75 minutes left, and !timeleft also sets to 75 minutes. 

                 Case 2: When !timeleft is around 55 minutes and the current round time is 2 minutes left, after rtv extends it by 20 minutes, the current map round is 22 minutes left, and !timeleft also follows 22 minutes.
                */
                // RoundTime is in seconds, so multiply by 60 to convert to minutes
                gameRules.RoundTime += (minutesToExtendBy * 60);

                var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First();
                Utilities.SetStateChanged(gameRulesProxy, "CCSGameRulesProxy", "m_pGameRules");

                // Update TimeRemaining in timeLimitManager
                // TimeRemaining is in minutes, divide round time by 60
                _timeLimitManager.TimeRemaining = _gameRules.RoundTime / 60;

                _pluginState.MapChangeScheduled = false;
                _pluginState.EofVoteHappening = false;
                _pluginState.CommandsDisabled = false;

                return true;
            }
            catch (Exception ex) 
            {
#if DEBUG
                _plugin?.Logger.LogWarning("Something went wrong when updating the round time {message}", ex.Message);
#endif
                return false;
            }

        }


        public bool ExtendMapTimeLimit(int minutesToExtendBy, TimeLimitManager timeLimitManager, GameRules gameRules)
        {
            try
            {
                _timeLimitManager.TimeLimitValue += (minutesToExtendBy * 60); //convert to seconds

                // Update TimeRemaining
                //_timeLimitManager.TimeRemaining = (_timeLimitManager.TimeLimitValue/60) - (_timeLimitManager.TimePlayed/60);

                _pluginState.MapChangeScheduled = false;
                _pluginState.EofVoteHappening = false;
                _pluginState.CommandsDisabled = false;

                return true;
            }
            catch (Exception ex) 
            {
#if DEBUG
                _plugin?.Logger.LogWarning("Something went wrong when updating the round time {message}", ex.Message);
#endif
                return false;
            }
        }
    }
}

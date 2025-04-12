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
using CS2ScreenMenuAPI;
using CS2ScreenMenuAPI.Enums;
using CS2ScreenMenuAPI.Internal;

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
        Dictionary<CCSPlayerController, string> PlayerVotes = new();
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
        public void OnMapStart(string map)
        {
            try
            {
                // _pluginState.ExtendsLeft = _config!.ExtendLimit; //Null ref happened! Maplist broken!
                _pluginState.ExtendsLeft = new Config().VipExtendMapVote.ExtendLimit; // try this approach
                _totalExtendLimit = new Config().VipExtendMapVote.ExtendLimit;
            }
            catch (Exception)
            {
                // Screw it, Oz-Lin is fixing the null reference exception issue for razpberry.
#if DEBUG
                Server.PrintToConsole(_localizer.LocalizeWithPrefix("") + "Null Reference Exception happened, default to 3 extends.");
#endif
                Server.ExecuteCommand("css_plugins reload RockTheVote"); // screw it!
            }

            Votes.Clear();
            PlayerVotes.Clear();
            timeLeft = 0;
            KillTimer();
        }

        // VIP Extend Start
        public void ExtendTimeVoted(CCSPlayerController player, string voteResponse)
        {
            if (PlayerVotes.ContainsKey(player))
            {
                Votes[PlayerVotes[player]] -= 1;
            }

            if (!Votes.ContainsKey(voteResponse))
            {
                Votes[voteResponse] = 0;
            }

            Votes[voteResponse] += 1;
            PlayerVotes[player] = voteResponse;
            player.PrintToCenter(_localizer.LocalizeWithPrefix("extendtime.you-voted", voteResponse));
            if (Votes.Select(x => x.Value).Sum() >= _canVote)
            {
                ExtendTimeVote();
            }
        }

        public void RevokeVote(CCSPlayerController player)
        {
            if (PlayerVotes.ContainsKey(player))
            {
                Votes[PlayerVotes[player]] -= 1;
                PlayerVotes.Remove(player);
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
            if (_veConfig.HudMenu == 2)
            {
                var menu = CreateVoteScreenMenu();
                MenuAPI.OpenSubMenu(_plugin!, player, menu);
            }
            if (_veConfig.HudMenu == 1)
            {
                var menu = CreateVoteMenu();
                MenuManager.OpenChatMenu(player, menu);
            }
        }

        private ChatMenu CreateVoteMenu()
        {
            ChatMenu menu = new(_localizer.Localize("extendtime.hud.menu-title"));

            var answers = new List<string>() { "Yes", "No" };

            foreach (var answer in answers)
            {
                if (!Votes.ContainsKey(answer))
                {
                    Votes[answer] = 0;
                }
                menu.AddMenuOption(answer, (player, option) =>
                {
                    ExtendTimeVoted(player, answer);
                    MenuManager.CloseActiveMenu(player);
                });
            }

            return menu;
        }

        private ScreenMenu CreateVoteScreenMenu()
        {
            ScreenMenu menu = new ScreenMenu(_localizer.Localize("extendtime.hud.menu-title"), _plugin!)
            {
                PostSelectAction = CS2ScreenMenuAPI.Enums.PostSelectAction.Close,
                IsSubMenu = false,
            };

            var answers = new List<string>() { "Yes", "No" };

            foreach (var answer in answers)
            {
                Votes[answer] = 0;
                menu.AddOption(answer, (player, option) =>
                {
                    ExtendTimeVoted(player, answer);
                    //MenuManager.CloseActiveMenu(player);
                });
            }

            return menu;
        }

        void KillTimer()
        {
            timeLeft = -1;
            if (Timer is not null)
            {
                Timer!.Kill();
                Timer = null;
            }
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
            KillTimer();



            decimal maxVotes = Votes.Select(x => x.Value).Max();
            IEnumerable<KeyValuePair<string, int>> potentialWinners = Votes.Where(x => x.Value == maxVotes);
            Random rnd = new();
            KeyValuePair<string, int> winner = potentialWinners.ElementAt(rnd.Next(0, potentialWinners.Count()));

            decimal totalVotes = Votes.Select(x => x.Value).Sum();
            decimal percent = totalVotes > 0 ? winner.Value / totalVotes * 100M : 0;

            if (maxVotes > 0)
            {
                if (winner.Key == "No")
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

            if (winner.Key == "No")
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
            Votes.Clear();
            PlayerVotes.Clear();
            _pluginState.ExtendTimeVoteHappening = true;
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _canVote = ServerManager.ValidPlayerCount();
            
            if (_veConfig.HudMenu == 2)
            {
                var sMenu = CreateVoteScreenMenu();
                foreach (var player in ServerManager.ValidPlayers())
                    MenuAPI.OpenSubMenu(_plugin!, player, sMenu);
            }
            if (_veConfig.HudMenu == 1)
            {
                var cMenu = CreateVoteMenu();
                foreach (var player in ServerManager.ValidPlayers())
                    MenuManager.OpenChatMenu(player, cMenu);
            }

            timeLeft = _config.VoteDuration;
            Timer = _plugin!.AddTimer(1.0F, () =>
            {
                if (timeLeft <= 0)
                {
                    ExtendTimeVote();
                    if (_veConfig.HudMenu == 2)
                    {
                        foreach (var player in Utilities.GetPlayers())
                            if (player != null)
                                MenuAPI.CloseActiveMenu(player);
                    }
                }
                else
                    timeLeft--;
            }, TimerFlags.REPEAT);
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

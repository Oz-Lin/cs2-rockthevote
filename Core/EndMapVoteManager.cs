using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using CS2ScreenMenuAPI;
using CS2ScreenMenuAPI.Internal;
using System.Data;
using System.Drawing;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using static CounterStrikeSharp.API.Core.Listeners;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    //public partial class Plugin
    //{

    //    [ConsoleCommand("votebot", "Votes to rock the vote")]
    //    public void VoteBot(CCSPlayerController? player, CommandInfo? command)
    //    {
    //        var bot = ServerManager.ValidPlayers().FirstOrDefault(x => x.IsBot);
    //        if (bot is not null)
    //        {
    //            _endMapVoteManager.MapVoted(bot, "de_dust2");
    //        }
    //    }
    //}

    public class EndMapVoteManager : IPluginDependency<Plugin, Config>
    {
        const int MAX_OPTIONS_HUD_MENU = 6;
        public EndMapVoteManager(MapLister mapLister, ChangeMapManager changeMapManager, NominationCommand nominationManager, StringLocalizer localizer, PluginState pluginState, MapCooldown mapCooldown, ExtendRoundTimeManager extendRoundTimeManager, TimeLimitManager timeLimitManager, RoundLimitManager roundLimitManager, GameRules gameRules)
        {
            _mapLister = mapLister;
            _changeMapManager = changeMapManager;
            _nominationManager = nominationManager;
            _localizer = localizer;
            _pluginState = pluginState;
            _mapCooldown = mapCooldown;
            _extendRoundTimeManager = extendRoundTimeManager;
            _timeLimitManager = timeLimitManager;
            _roundLimitManager = roundLimitManager;
            _gameRules = gameRules;
        }

        private readonly MapLister _mapLister;
        private readonly ChangeMapManager _changeMapManager;
        private readonly NominationCommand _nominationManager;
        private readonly StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapCooldown _mapCooldown;
        private Timer? Timer;
        private readonly ExtendRoundTimeManager _extendRoundTimeManager;
        private readonly TimeLimitManager _timeLimitManager;
        private readonly RoundLimitManager _roundLimitManager;
        private readonly GameRules _gameRules;
        private VotemapConfig _votemapConfig = new(); // dealing with votemap overrides endmapvote nextmap

        Dictionary<string, int> Votes = new();
        Dictionary<CCSPlayerController, string> PlayerVotes = new();
        int timeLeft = -1;

        List<string> mapsEllected = new();

        private IEndOfMapConfig? _config = null;
        private IEndOfMapConfig? _configBackup = null;
        private int _canVote = 0;
        private Plugin? _plugin;
        private EndOfMapConfig? _eomConfig = new();
        private int _totalExtendLimit;

        HashSet<int> _voted = new();

        public bool VoteInProgress => timeLeft >= 0;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterListener<OnTick>(VoteDisplayTick);
        }

        public void OnConfigParsed(Config config)
        {
            _eomConfig = config.EndOfMapVote;
            _totalExtendLimit = config.EndOfMapVote.ExtendLimit;
        }

        public void OnMapStart(string map)
        {
            Votes.Clear();
            PlayerVotes.Clear();
            timeLeft = 0;
            mapsEllected.Clear();
            KillTimer();
            _eomConfig!.ExtendLimit = _totalExtendLimit;

            // Restore the config if it was changed by the server command
            if (_configBackup is not null)
            {
                _config = _configBackup;
                _configBackup = null;
            }
        }

        public void MapVoted(CCSPlayerController player, string mapName)
        {
            if (_config!.HideHudAfterVote)
                _voted.Add(player.UserId!.Value);

            if (PlayerVotes.ContainsKey(player))
            {
                Votes[PlayerVotes[player]] -= 1;
            }

            Votes[mapName] += 1;
            PlayerVotes[player] = mapName;
            player.PrintToChat(_localizer.LocalizeWithPrefix("emv.you-voted", mapName));
            if (Votes.Select(x => x.Value).Sum() >= _canVote)
            {
                EndVote();
            }
        }

        public void RevokeVote(CCSPlayerController player)
        {
            if (PlayerVotes.ContainsKey(player))
            {
                Votes[PlayerVotes[player]] -= 1;
                PlayerVotes.Remove(player);
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.vote-revoked-choose-again"));
                ShowMapVoteMenu(player); // Bring back the map vote menu
            }
            else
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.no-vote-to-revoke"));
            }
        }

        private void ShowMapVoteMenu(CCSPlayerController player)
        {
            var menu = CreateMapVoteMenu();
            // MenuManager.OpenChatMenu(player, menu);
            MenuAPI.OpenMenu(_plugin, player!, menu);
        }

        // private ChatMenu CreateMapVoteMenu()
        private ScreenMenu CreateMapVoteMenu()
        {
            // ChatMenu menu = new(_localizer.Localize("emv.hud.menu-title"));

            ScreenMenu MENU = new ScreenMenu(_localizer.Localize("emv.hud.menu-title"), _plugin) // Creating the menu
            {
                PostSelectAction = CS2ScreenMenuAPI.Enums.PostSelectAction.Nothing,
                IsSubMenu = false, // this is not a sub menu
                TextColor = Color.DarkOrange, // if this not set it will be the API default color
                FontName = "Impact",
                //MenuType = MenuType.KeyPress// IF you wanna use both types you don't need to add this since default value is using Both Types.
            };

            if (_eomConfig != null && _eomConfig.AllowExtend && (_eomConfig.ExtendLimit > 0 || _eomConfig.ExtendLimit == -1))
            {
                Votes[_localizer.Localize("general.extend-current-map")] = 0;
                // menu.AddMenuOption(_localizer.Localize("general.extend-current-map"), (player, option) =>
                MENU.AddOption(_localizer.Localize("general.extend-current-map"), (player, option) =>
                {
                    MapVoted(player, _localizer.Localize("general.extend-current-map"));
                    // MenuManager.CloseActiveMenu(player);
                    MenuAPI.CloseActiveMenu(player);
                });

            }

            foreach (var map in mapsEllected.Take((_eomConfig != null && _eomConfig.AllowExtend && (_eomConfig.ExtendLimit > 0 || _eomConfig.ExtendLimit == -1)) ? (MAX_OPTIONS_HUD_MENU - 1) : MAX_OPTIONS_HUD_MENU))
            {
                Votes[map] = 0;
                //menu.AddMenuOption(map, (player, option) =>
                MENU.AddOption(map, (player, option) =>
                {
                    MapVoted(player, map);
                    //MenuManager.CloseActiveMenu(player);
                    MenuAPI.CloseActiveMenu(player);
                });
            }

            //return menu;
            return MENU;
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
            {
                foreach (CCSPlayerController player in ServerManager.ValidPlayers())
                {
                    MenuAPI.CloseActiveMenu(player);
                }
                return;
            }
                

            int index = 1;
            StringBuilder stringBuilder = new();
            stringBuilder.AppendFormat($"<b>{_localizer.Localize("emv.hud.hud-timer", timeLeft)}</b>");
            if (_config!.HudMenu == 1)
                foreach (var kv in Votes.OrderByDescending(x => x.Value).Take(MAX_OPTIONS_HUD_MENU).Where(x => x.Value > 0))
                {
                    stringBuilder.AppendFormat($"<br>{kv.Key} <font color='green'>({kv.Value})</font>");
                }
            if (_config!.HudMenu == 0)
                foreach (var kv in Votes.Take(MAX_OPTIONS_HUD_MENU))
                {
                    stringBuilder.AppendFormat($"<br><font color='yellow'>!{index++}</font> {kv.Key} <font color='green'>({kv.Value})</font>");
                }

            foreach (CCSPlayerController player in ServerManager.ValidPlayers().Where(x => !_voted.Contains(x.UserId!.Value)))
            {
                player.PrintToCenterHtml(stringBuilder.ToString());
            }
        }

        void EndVote()
        {
            bool mapEnd = _config is EndOfMapConfig;
            KillTimer();
            decimal maxVotes = Votes.Select(x => x.Value).Max();
            IEnumerable<KeyValuePair<string, int>> potentialWinners = Votes.Where(x => x.Value == maxVotes);
            Random rnd = new();
            KeyValuePair<string, int> winner = potentialWinners.ElementAt(rnd.Next(0, potentialWinners.Count()));

            decimal totalVotes = Votes.Select(x => x.Value).Sum();
            decimal percent = totalVotes > 0 ? winner.Value / totalVotes * 100M : 0;

            if (maxVotes > 0)
            {
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended", winner.Key, percent, totalVotes));
            }
            else
            {
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended-no-votes", winner.Key));
            }

            PrintCenterTextAll(_localizer.Localize("emv.hud.finished", winner.Key));

            if (winner.Key == _localizer.Localize("general.extend-current-map"))
            {
                if (_config != null)
                {
                    if (_config.ExtendTimeStep > 0 && !_timeLimitManager.UnlimitedTime)
                    {
                        if (_eomConfig!.RoundBased == true)
                        {
                            _extendRoundTimeManager.ExtendMapTimeLimit(_config.ExtendTimeStep, _timeLimitManager, _gameRules);
                        }
                        else
                        {
                            _extendRoundTimeManager.ExtendRoundTime(_config.ExtendTimeStep, _timeLimitManager, _gameRules);
                        }
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed",
                            _config.ExtendTimeStep, percent, totalVotes));
                    }
                    else if (_config.ExtendRoundStep > 0 && !_roundLimitManager.UnlimitedRound)
                    {
                        _roundLimitManager.RoundsRemaining =
                            _roundLimitManager.RoundLimitValue + _config.ExtendRoundStep;
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed.rounds",
                            _config.ExtendRoundStep, percent, totalVotes));
                    }

                    if (_eomConfig!.ExtendLimit != -1)
                    {
                        _eomConfig!.ExtendLimit--;
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.extendsleft", _eomConfig.ExtendLimit, _totalExtendLimit));
                    }

                    _pluginState.MapChangeScheduled = false;
                    _pluginState.EofVoteHappening = false;
                    _pluginState.CommandsDisabled = false;
                    _pluginState.ExtendTimeVoteHappening = false;

                    _nominationManager.ResetNominations();
                    _nominationManager.Nomlist.Clear();
                }
            }
            else
            {
                _changeMapManager.ScheduleMapChange(winner.Key, mapEnd: mapEnd);
                _votemapConfig.Enabled = false;
                if (_config != null && _config.ChangeMapImmediately)
                    _changeMapManager.ChangeNextMap(mapEnd);
                else
                {
                    if (!mapEnd)
                    {
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("general.changing-map-next-round", winner.Key));
                        _pluginState.CommandsDisabled = true;
                    }
                }
            }
            _pluginState.EofVoteHappening = false;
        }

        IList<T> Shuffle<T>(Random rng, IList<T> array)
        {
            int n = array.Count;
            while (n > 1)
            {
                int k = rng.Next(n--);
                T temp = array[n];
                array[n] = array[k];
                array[k] = temp;
            }
            return array;
        }

        public void StartVote(IEndOfMapConfig config)
        {
            Votes.Clear();
            PlayerVotes.Clear();
            _voted.Clear();

            // Backup the current config as if this is called via the server command, the config will be changed
            _configBackup = _config;

            _pluginState.EofVoteHappening = true;
            _config = config;
            int mapsToShow = _config!.MapsToShow == 0 ? MAX_OPTIONS_HUD_MENU : _config!.MapsToShow;
            if (config.HudMenu == 1 && mapsToShow > MAX_OPTIONS_HUD_MENU)
                mapsToShow = MAX_OPTIONS_HUD_MENU;

            var mapsScrambled = Shuffle(new Random(),
                _mapLister.Maps!.Select(x => x.Name).Where(x => x != Server.MapName && !_mapCooldown.IsMapInCooldown(x))
                    .ToList());
            mapsEllected = _nominationManager.NominationWinners().Concat(mapsScrambled).Distinct().ToList();

            _canVote = ServerManager.ValidPlayerCount();
            var menu = CreateMapVoteMenu();

            foreach (var player in ServerManager.ValidPlayers())
                MenuAPI.OpenMenu(_plugin, player!, menu);

            timeLeft = _config.VoteDuration;
            Timer = _plugin!.AddTimer(1.0F, () =>
            {
                if (timeLeft <= 0)
                {
                    EndVote();
                }
                else
                    timeLeft--;
            }, TimerFlags.REPEAT);
        }
    }
}

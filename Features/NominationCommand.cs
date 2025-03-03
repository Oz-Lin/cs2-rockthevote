using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using cs2_rockthevote.Core;
using CS2ScreenMenuAPI;
using CS2ScreenMenuAPI.Enums;
using CS2ScreenMenuAPI.Internal;
using System.Drawing;
using System.Numerics;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [ConsoleCommand("css_nominate", "nominate a map to rtv")]
        [ConsoleCommand("nominate", "nominate a map to rtv")]
        [ConsoleCommand("css_nom", "nominate a map to rtv")]
        [ConsoleCommand("nom", "nominate a map to rtv")]
        public void OnNominate(CCSPlayerController? player, CommandInfo command)
        {
            string map = command.GetArg(1).Trim().ToLower();
            _nominationManager.CommandHandler(player!, map);
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult EventPlayerDisconnectNominate(EventPlayerDisconnect @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player != null)
            {
                _nominationManager.PlayerDisconnected(player);
            }

            return HookResult.Continue;
        }
    }

    public class NominationCommand : IPluginDependency<Plugin, Config>
    {
        Dictionary<int, (string PlayerName, List<string> Maps)> Nominations = new();
        ChatMenu? nominationMenu = null;
        ScreenMenu? nominationScreenMenu = null;
        private RtvConfig _config = new();
        private GameRules _gamerules;
        private StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapCooldown _mapCooldown;
        private MapLister _mapLister;
        private Plugin? _plugin;
        public Dictionary<int, (string PlayerName, List<string> Maps)> Nomlist => Nominations;

        public NominationCommand(MapLister mapLister, GameRules gamerules, StringLocalizer localizer, PluginState pluginState, MapCooldown mapCooldown)
        {
            _mapLister = mapLister;
            _gamerules = gamerules;
            _localizer = localizer;
            _pluginState = pluginState;
            _mapCooldown = mapCooldown;
            _mapCooldown.EventCooldownRefreshed += OnMapsLoaded;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }
        public void OnMapStart(string map)
        {
            Nominations.Clear();
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.Rtv;
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            if (_config.HudMenu == 1)
            {
                nominationMenu = new("Nomination");
                foreach (var map in _mapLister.Maps!.Where(x => x.Name != Server.MapName))
                {
                    nominationMenu.AddMenuOption(map.Name, (CCSPlayerController player, ChatMenuOption option) =>
                    {
                        Nominate(player, option.Text);
                        MenuManager.CloseActiveMenu(player);
                    }, _mapCooldown.IsMapInCooldown(map.Name));
                }

                nominationMenu.AddMenuOption("Exit", (CCSPlayerController player, ChatMenuOption option) =>
                {
                    MenuManager.CloseActiveMenu(player);
                });
            }

            if (_config.HudMenu == 2)
            {
                nominationScreenMenu = new ScreenMenu("Nomination", _plugin) // Creating the menu
                {
                    PostSelectAction = CS2ScreenMenuAPI.Enums.PostSelectAction.Nothing,
                    IsSubMenu = false, // this is not a sub menu
                    //TextColor = Color.DarkOrange, // if this not set it will be the API default color
                    //FontName = "Impact",
                    //MenuType = MenuType.KeyPress// IF you wanna use both types you don't need to add this since default value is using Both Types.
                };

                foreach (var map in _mapLister.Maps!.Where(x => x.Name != Server.MapName))
                {
                    nominationScreenMenu.AddOption(map.Name, (p, option) =>
                    {
                        Nominate(p, option.Text);
                        MenuAPI.CloseActiveMenu(p);
                    }, _mapCooldown.IsMapInCooldown(map.Name));
                }
            }
        }

        public void CommandHandler(CCSPlayerController? player, string map)
        {
            if (player is null)
                return;

            map = map.ToLower().Trim();
            if (_pluginState.DisableCommands || !_config.NominationEnabled)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }

            if (_gamerules.WarmupRunning)
            {
                if (!_config.EnabledInWarmup)
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                    return;
                }
            }
            else if (_config.MinRounds > 0 && _config.MinRounds > _gamerules.TotalRoundsPlayed)
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-rounds", _config.MinRounds));
                return;
            }

            if (ServerManager.ValidPlayerCount() < _config!.MinPlayers)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-players", _config!.MinPlayers));
                return;
            }

            if (string.IsNullOrEmpty(map))
            {
                OpenNominationMenu(player!);
            }
            else
            {
                Nominate(player, map);
            }
        }

        public void OpenNominationMenu(CCSPlayerController player)
        {
            if (_config.HudMenu == 1) MenuManager.OpenChatMenu(player, nominationMenu!);
            if (_config.HudMenu == 2) MenuAPI.OpenMenu(_plugin, player, nominationScreenMenu!);
        }

        void Nominate(CCSPlayerController player, string map)
        {
            if (map == Server.MapName)
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.current-map"));
                return;
            }

            if (_mapCooldown.IsMapInCooldown(map))
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.map-played-recently"));
                return;
            }

            string matchingMap = _mapLister.GetSingleMatchingMapName(map, player, _localizer);

            if (matchingMap == "")
                return;

            var userId = player.UserId!.Value;
            if (!Nominations.ContainsKey(userId))
                Nominations[userId] = (player.PlayerName, new List<string>());

            bool alreadyVoted = Nominations[userId].Maps.IndexOf(matchingMap) != -1;
            if (!alreadyVoted)
                Nominations[userId].Maps.Add(matchingMap);

            var totalVotes = Nominations.Select(x => x.Value.Maps.Where(y => y == matchingMap).Count())
                .Sum();

            if (!alreadyVoted)
            {
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("nominate.nominated", player.PlayerName,
                    matchingMap, totalVotes));
            }
            else
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.already-nominated", matchingMap,
                    totalVotes));
            }
            if (_config.HudMenu == 1) MenuManager.CloseActiveMenu(player);
            if (_config.HudMenu == 2) MenuAPI.CloseActiveMenu(player);
        }

        public List<string> NominationWinners()
        {
            if (Nominations.Count == 0)
                return new List<string>();

            var rawNominations = Nominations
                .Select(x => x.Value.Maps)
                .Aggregate((acc, x) => acc.Concat(x).ToList());

            return rawNominations
                .Distinct()
                .Select(map => new KeyValuePair<string, int>(map, rawNominations.Count(x => x == map)))
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key)
                .ToList();
        }

        public void ResetNominations()
        {
            Nominations.Clear();
        }

        public void PlayerDisconnected(CCSPlayerController player)
        {
            int userId = player.UserId!.Value;
            if (Nominations.ContainsKey(userId))
                Nominations.Remove(userId);
        }
    }
}

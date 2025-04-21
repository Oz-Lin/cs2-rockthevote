using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Logging;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;
using CS2ScreenMenuAPI;
//using CS2ScreenMenuAPI.Enums;
//using CS2ScreenMenuAPI.Internal;
//using CS2ScreenMenuAPI.Interfaces;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        [ConsoleCommand("votemap", "Vote to change to a map")]
        [ConsoleCommand("vm", "Vote to change to a map")]
        [ConsoleCommand("css_votemap", "Vote to change to a map")]
        public void OnVotemap(CCSPlayerController? player, CommandInfo command)
        {
            string map = command.GetArg(1).Trim().ToLower();
            _votemapManager.CommandHandler(player!, map);
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult EventPlayerDisconnectVotemap(EventPlayerDisconnect @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player != null)
            {
                _votemapManager.PlayerDisconnected(player);
            }
            return HookResult.Continue;
        }
    }

    public class VotemapCommand : IPluginDependency<Plugin, Config>
    {
        private Dictionary<string, AsyncVoteManager> VotedMaps = new();
        private ChatMenu? votemapMenu = null;
        private CenterHtmlMenu? votemapMenuHud = null;
        private Menu? votemapScreenMenuHud = null;
        private VotemapConfig _config = new();
        private GameRules _gamerules;
        private StringLocalizer _localizer;
        private ChangeMapManager _changeMapManager;
        private PluginState _pluginState;
        private MapCooldown _mapCooldown;
        private MapLister _mapLister;
        private Plugin? _plugin;

        public VotemapCommand(MapLister mapLister, GameRules gamerules, IStringLocalizer stringLocalizer, ChangeMapManager changeMapManager, PluginState pluginState, MapCooldown mapCooldown)
        {
            _mapLister = mapLister;
            _gamerules = gamerules;
            _localizer = new StringLocalizer(stringLocalizer, "votemap.prefix");
            _changeMapManager = changeMapManager;
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
            VotedMaps.Clear();
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.Votemap;
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            InitializeMenus();
            PopulateMenus();
        }

        private void InitializeMenus()
        {
            votemapMenu = new("Votemap");
#pragma warning disable CS0618 // Type or member is obsolete
            votemapMenuHud = new("VoteMap");
#pragma warning restore CS0618 // Type or member is obsolete
            //votemapScreenMenuHud = CreateVotemapScreenMenu(CCSPlayerController player);
        }

        private void PopulateMenus()
        {
            foreach (var map in _mapLister.Maps!.Where(x => x.Name != Server.MapName))
            {
                AddMenuOption(votemapMenu, map.Name);
                AddHTMLMenuOption(votemapMenuHud, map.Name);
                //AddScreenMenuOption(votemapScreenMenuHud, map.Name);
            }
        }

        private void AddMenuOption(ChatMenu? menu, string mapName)
        {
            menu?.AddMenuOption(mapName, (CCSPlayerController player, ChatMenuOption option) =>
            {
                AddVote(player, option.Text);
            }, _mapCooldown.IsMapInCooldown(mapName));
        }
        private void AddHTMLMenuOption(CenterHtmlMenu? menu, string mapName)
        {
            menu?.AddMenuOption(mapName, (player, option) =>
            {
                AddVote(player, mapName);
            }, _mapCooldown.IsMapInCooldown(mapName));
        }

        //private void AddScreenMenuOption(Menu? menu, string mapName)
        //{
        //    menu?.AddItem(mapName, (player, option) =>
        //    {
        //        AddVote(player, mapName);
        //        //MenuAPI.CloseActiveMenu(player);
        //    }, _mapCooldown.IsMapInCooldown(mapName));
        //}

        private Menu CreateVotemapScreenMenu(CCSPlayerController player)
        {
            Menu menu = new Menu(player, _plugin!)
            {
                Title = "Votemap",
                PostSelect = PostSelect.Close,
                HasExitButon = true
            };

            foreach (var map in _mapLister.Maps!.Where(x => x.Name != Server.MapName))
            {
                menu.AddItem(map.Name, (player, option) =>
                {
                    AddVote(player, map.Name);
                }, _mapCooldown.IsMapInCooldown(map.Name));
            }

            return menu;
        }

        public void CommandHandler(CCSPlayerController? player, string map)
        {
            if (player is null) return;

            map = map.ToLower().Trim();
            if (_pluginState.DisableCommands || !_config.Enabled)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }

            if (_gamerules.WarmupRunning && !_config.EnabledInWarmup)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                return;
            }

            if (_config.MinRounds > 0 && _config.MinRounds > _gamerules.TotalRoundsPlayed)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-rounds", _config.MinRounds));
                return;
            }

            if (ServerManager.ValidPlayerCount() < _config.MinPlayers)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-players", _config.MinPlayers));
                return;
            }

            if (string.IsNullOrEmpty(map))
            {
                OpenVotemapMenu(player);
            }
            else
            {
                AddVote(player, map);
            }
        }

        public void OpenVotemapMenu(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid)
            {
                player?.PrintToChat("You are not in a valid state to open the votemap menu.");
                return;
            }

            if (votemapScreenMenuHud is null)
            {
                player.PrintToChat("Votemap screen menu is not initialized.");
                return;
            }

            if (_plugin == null)
            {
                player.PrintToChat("Plugin is not initialized.");
                return;
            }

            switch (_config.HudMenu)
            {
                case 2:
                    votemapScreenMenuHud = CreateVotemapScreenMenu(player); // Assign the menu here
                    votemapScreenMenuHud.Display();
                    //MenuManager.OpenChatMenu(player, votemapMenu!);
                    break;
                case 1:
                    MenuManager.OpenCenterHtmlMenu(_plugin!, player, votemapMenuHud!);
                    break;
                case 0:
                    MenuManager.OpenChatMenu(player, votemapMenu!);
                    break;
            }
        }

        private void AddVote(CCSPlayerController player, string map)
        {
            if (map == Server.MapName)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.current-map"));
                return;
            }

            if (_mapCooldown.IsMapInCooldown(map))
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.map-played-recently"));
                return;
            }

            string matchingMap = _mapLister.GetSingleMatchingMapName(map, player, _localizer);

            if (matchingMap == "")
                return;

            var userId = player.UserId!.Value;
            if (!VotedMaps.ContainsKey(matchingMap))
                VotedMaps.Add(matchingMap, new AsyncVoteManager(_config));

            var voteManager = VotedMaps[matchingMap];
            VoteResult result = voteManager.AddVote(userId);
            HandleVoteResult(player, matchingMap, result);
        }

        private void HandleVoteResult(CCSPlayerController player, string map, VoteResult result)
        {
            switch (result.Result)
            {
                case VoteResultEnum.Added:
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("votemap.player-voted", player.PlayerName, map)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    break;
                case VoteResultEnum.AlreadyAddedBefore:
                    player.PrintToChat($"{_localizer.LocalizeWithPrefix("votemap.already-voted", map)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    break;
                case VoteResultEnum.VotesAlreadyReached:
                    player.PrintToChat(_localizer.LocalizeWithPrefix("votemap.disabled"));
                    break;
                case VoteResultEnum.VotesReached:
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("votemap.player-voted", player.PlayerName, map)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    _changeMapManager.ScheduleMapChange(map, prefix: "votemap.prefix");
                    if (_config.ChangeMapImmediately)
                        _changeMapManager.ChangeNextMap();
                    else
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("general.changing-map-next-round", map));
                    break;
            }
            CloseActiveMenus(player);
        }

        private void CloseActiveMenus(CCSPlayerController player)
        {
            switch (_config.HudMenu)
            {
                case 2:
                    MenuAPI.CloseActiveMenu(player);
                    MenuManager.CloseActiveMenu(player);
                    break;
                case 1:
                case 0:
                    MenuManager.CloseActiveMenu(player);
                    break;
            }
        }

        public void PlayerDisconnected(CCSPlayerController player)
        {
            int userId = player.UserId!.Value;
            foreach (var map in VotedMaps)
                map.Value.RemoveVote(userId);
        }
    }
}

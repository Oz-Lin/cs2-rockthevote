using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Logging;
using System.Text;
using cs2_rockthevote.ScreenMenus;
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

        private readonly MapVotePolicy _vote = new();
        private readonly VotePauseLease _pause = new();
        private long _mapGeneration;
        private IReadOnlyDictionary<string, int> Votes => _vote.Totals;
        private readonly Dictionary<uint, ChatMenu> _chatVoteMenus = new();
        private string? _extendOption;
        int timeLeft = -1;

        List<string> mapsEllected = new();

        private IEndOfMapConfig? _config = null;
        private IEndOfMapConfig? _configBackup = null;
        private int _canVote = 0;
        private Plugin? _plugin;
        private EndOfMapConfig? _eomConfig = new();
        private int _totalExtendLimit;

        private readonly HashSet<uint> _voted = new();

        public bool VoteInProgress => _vote.Active;
        public bool MapTransitionPending => _changeMapManager.NextMap != null;
        public event EventHandler? RtvVoteCompleted;
        public event EventHandler? MapVoteStarted;
        public event EventHandler? ExtensionUpdated;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterListener<OnTick>(VoteDisplayTick);
            plugin.RegisterListener<OnMapEnd>(CancelForMapChange);
        }

        public void OnConfigParsed(Config config)
        {
            _eomConfig = config.EndOfMapVote;
            _totalExtendLimit = config.EndOfMapVote.ExtendLimit;
        }

        private void ReleasePause()
        {
            _pause.ReleaseIfPending(_pause.Generation, _mapGeneration, _gameRules.ReadPauseState, () => Server.ExecuteCommand("mp_unpause_match"));
        }
        private void CleanupVote() => CleanupActions.ResetThenRun(
            () => { timeLeft = -1; _pluginState.EofVoteHappening = false; }, KillTimer, CloseVoteMenus, ReleasePause);
        private void CancelForMapChange()
        {
            ++_mapGeneration; _pause.Abandon(); _vote.Reset();
            CleanupActions.ResetThenRun(() => { timeLeft = -1; _pluginState.EofVoteHappening = false; }, KillTimer, CloseVoteMenus);
        }
        public void Stop()
        {
            _vote.Reset();
            CleanupVote();
        }
        public void FinishActiveVote() { if (_vote.Active) EndVote(_vote.Generation); }

        public void OnMapStart(string map)
        {
            ++_mapGeneration;
            _pause.Abandon(); // Never send an old-map unpause into the new map.
            _vote.Reset();
            _voted.Clear();
            mapsEllected.Clear();
            _extendOption = null;
            _eomConfig!.ExtendLimit = _totalExtendLimit;

            // Make sure all state flags are reset
            _pluginState.EofVoteHappening = false;
            _pluginState.CommandsDisabled = false;
            _pluginState.MapChangeScheduled = false;
            _pluginState.ExtendTimeVoteHappening = false;
#if DEBUG
            _plugin?.Logger.LogInformation("OnMapStart: Reset all state flags including EofVoteHappening to false");
#endif
            // Restore the config if it was changed by the server command
            if (_configBackup is not null)
            {
                _config = _configBackup;
                _configBackup = null;
            }
            // Native map cleanup has already removed the old STOP timer.
            if (Timer != null) PluginTimers.Forget(_plugin, Timer);
            Timer = null;
            timeLeft = -1;
            CloseVoteMenus();
        }

        public void MapVoted(CCSPlayerController player, string mapName)
        {
            MapVoted(player, mapName, _vote.Generation);
        }

        private void MapVoted(CCSPlayerController player, string mapName, long generation)
        {
            if (!TryGetVoterId(player, out var voter) || !_vote.Cast(generation, voter, mapName)) return;
            if (_config!.HideHudAfterVote) _voted.Add(voter);
            player.PrintToChat(_localizer.LocalizeWithPrefix("emv.you-voted", mapName));
            if (_vote.BallotCount >= _canVote)
            {
                EndVote(generation);
            }
        }

        public void RevokeVote(CCSPlayerController player)
        {
            if (!VoteInProgress || !TryGetVoterId(player, out var voter)) return;
            if (_vote.Revoke(_vote.Generation, voter))
            {
                _voted.Remove(voter);
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
            if (!VoteInProgress) return;
            if (_config!.HudMenu == 2)
            {
                var menu = CreateMapVoteScreenMenu(player);
                menu.Display();
            }
            if (_config.HudMenu == 0 || _config.HudMenu == 1)
            {
                var cMenu = CreateMapVoteMenu();
                _chatVoteMenus[player.EntityHandle.Raw] = cMenu;
                MenuManager.OpenChatMenu(player, cMenu);
            }
        }

        private ChatMenu CreateMapVoteMenu()
        {
            var generation = _vote.Generation;
            ChatMenu cMenu = new(_localizer.Localize("emv.hud.menu-title"))
            {
                // Close only this menu in the callback, never a replacement opened there.
                PostSelectAction = PostSelectAction.Nothing
            };
            foreach (var map in Votes.Keys)
            {
                cMenu.AddMenuOption(map, (player, option) =>
                {
                    MapVoted(player, map, generation);
                    CloseChatMenuIfOwned(player, cMenu);
                });
            }

            return cMenu;
        }

        private Menu CreateMapVoteScreenMenu(CCSPlayerController player)
        {
            var generation = _vote.Generation;
            Menu sMenu = new Menu(player, _plugin!)
            {
                Title = _localizer.Localize("emv.hud.menu-title"),
                PostSelect = PostSelect.Close,
                HasExitButon = true,
                OwnerTag = this
            };
            foreach (var map in Votes.Keys)
            {
                sMenu.AddItem(map, (player, option) =>
                {
                    MapVoted(player, map, generation);
                });
            }
            return sMenu;
        }

        private static bool TryGetVoterId(CCSPlayerController player, out uint voter)
        {
            voter = 0;
            if (player is null || !player.IsValid || !player.EntityHandle.IsValid) return false;
            voter = player.EntityHandle.Raw;
            return true;
        }

        private static void CloseChatMenuIfOwned(CCSPlayerController player, ChatMenu menu)
        {
            if (player.IsValid && MenuManager.GetActiveMenu(player) is BaseMenuInstance active && ReferenceEquals(active.Menu, menu))
                MenuManager.CloseActiveMenu(player);
        }

        private void CloseVoteMenus()
        {
            try
            {
                _plugin?.ScreenMenus.CloseOwned(this);
            }
            catch (Exception ex)
            {
                _plugin?.Logger.LogWarning(ex, "Could not close this map vote's screen menus.");
            }
            try
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    try
                    {
                        if (TryGetVoterId(player, out var voter) && _chatVoteMenus.TryGetValue(voter, out var menu))
                            CloseChatMenuIfOwned(player, menu);
                    }
                    catch (Exception ex)
                    {
                        _plugin?.Logger.LogWarning(ex, "Could not close a map vote chat menu.");
                    }
                }
            }
            catch (Exception ex)
            {
                _plugin?.Logger.LogWarning(ex, "Could not enumerate map vote chat menus for cleanup.");
            }
            finally { _chatVoteMenus.Clear(); }
        }

        private IEnumerable<string> GetVoteOptions()
        {
            var allowExtend = _eomConfig != null && _eomConfig.AllowExtend && (_eomConfig.ExtendLimit > 0 || _eomConfig.ExtendLimit == -1);
            _extendOption = allowExtend ? _localizer.Localize("general.extend-current-map") : null;
            if (_extendOption != null) yield return _extendOption;
            int mapsToShow = _config!.MapsToShow == 0 ? MAX_OPTIONS_HUD_MENU : _config.MapsToShow;
            if (_config.HudMenu > 0 && mapsToShow > MAX_OPTIONS_HUD_MENU) mapsToShow = MAX_OPTIONS_HUD_MENU;
            foreach (var map in mapsEllected.Take(allowExtend ? mapsToShow - 1 : mapsToShow)) yield return map;
        }


        void KillTimer()
        {
            // Logical state is retired even if the native timer cannot be killed yet.
            timeLeft = -1;
            _pluginState.EofVoteHappening = false;
            if (Timer is not null) { PluginTimers.Kill(_plugin, Timer); Timer = null; }
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
            if (!VoteInProgress) return;
                

            int index = 1;
            StringBuilder stringBuilder = new();
            stringBuilder.AppendFormat($"<b>{_localizer.Localize("emv.hud.hud-timer", timeLeft)}</b>");
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

            foreach (CCSPlayerController player in ServerManager.ValidPlayers().Where(x => !_voted.Contains(x.EntityHandle.Raw)))
            {
                player.PrintToCenterHtml(stringBuilder.ToString());
            }
        }

        void EndVote(long generation)
        {
            if (!_vote.IsCurrent(generation)) return;
            try
            {
                if (!_vote.TryEndAndNotify(generation, Random.Shared.Next, _config is RtvConfig,
                    () => RtvVoteCompleted?.Invoke(this, EventArgs.Empty), out var result)) return;
                bool mapEnd = _config is EndOfMapConfig;
                KillTimer();
                CloseVoteMenus();
                if (result is null)
                {
                    _plugin?.Logger.LogWarning("EndVote: No map candidates were available; no map change was scheduled.");
                    return;
                }
                var winner = new KeyValuePair<string, int>(result.Winner, result.WinningVotes);
                decimal maxVotes = result.WinningVotes;
                decimal totalVotes = result.TotalVotes;
                decimal percent = result.WinningPercentage;

                if (maxVotes > 0)
                {
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended", winner.Key, percent, totalVotes));
                }
                else
                {
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended-no-votes", winner.Key));
                }

                PrintCenterTextAll(_localizer.Localize("emv.hud.finished", winner.Key));

                if (winner.Key == _extendOption)
                {
                    _changeMapManager.CancelScheduledChange();
                    if (_config != null)
                    {
                        bool extensionUpdated = false;
                        if (_config.ExtendTimeStep > 0 && !_timeLimitManager.UnlimitedTime)
                        {
                            if (_eomConfig!.RoundBased == true)
                            {
                                extensionUpdated = _extendRoundTimeManager.ExtendMapTimeLimit(_config.ExtendTimeStep, _timeLimitManager, _gameRules);
                            }
                            else
                            {
                                extensionUpdated = _extendRoundTimeManager.ExtendRoundTime(_config.ExtendTimeStep, _timeLimitManager, _gameRules);
                            }
                            if (extensionUpdated)
                                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed",
                                    _config.ExtendTimeStep, percent, totalVotes));
                            else _plugin?.Logger.LogWarning("The requested map time extension failed.");
#if DEBUG
                            _plugin?.Logger.LogInformation($"EndVote: Extended map time by {_config.ExtendTimeStep} minutes");
#endif
                        }
                        if (_config.ExtendRoundStep > 0 && !_roundLimitManager.UnlimitedRound)
                        {
                            _roundLimitManager.RoundsRemaining =
                                _roundLimitManager.RoundLimitValue + _config.ExtendRoundStep;
                            extensionUpdated = true;
                            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed.rounds",
                                _config.ExtendRoundStep, percent, totalVotes));
#if DEBUG
                            _plugin?.Logger.LogInformation($"EndVote: Extended current map by {_config.ExtendRoundStep} rounds");
#endif
                        }

                        if (_eomConfig!.ExtendLimit != -1)
                        {
                            _eomConfig!.ExtendLimit--;
                            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.extendsleft", _eomConfig.ExtendLimit, _totalExtendLimit));
                        }

                        _pluginState.MapChangeScheduled = false;
                        _pluginState.CommandsDisabled = false;
                        _pluginState.ExtendTimeVoteHappening = false;

                        _nominationManager.ResetNominations();
                        _nominationManager.Nomlist.Clear();
                        if (extensionUpdated) ExtensionUpdated?.Invoke(this, EventArgs.Empty);
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
            }
            catch (Exception ex)
            {
                _plugin?.Logger.LogError(ex, "Error ending map vote.");
            }
            finally
            {
                // A map transition or replacement vote owns its own timer and menus.
                if (_vote.Generation == generation)
                {
                    CleanupVote();
                    _pluginState.EofVoteHappening = false;
                }
#if DEBUG
                _plugin?.Logger.LogInformation("EndVote: Reset EofVoteHappening to false in finally block");
#endif
            }
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
            try
            {
                // Check if vote is already in progress
                if (_pluginState.EofVoteHappening || VoteInProgress)
                {
#if DEBUG
                    _plugin?.Logger.LogWarning("StartVote: Attempted to start a vote while one is already in progress. Ignoring request.");
#endif
                    return;
                }

                ReleasePause(); // An earlier failed cleanup cannot be inherited by this ballot.
                KillTimer();
                CloseVoteMenus();
                _voted.Clear();

                // Backup the current config as if this is called via the server command, the config will be changed
                _configBackup = _config;

                _pluginState.EofVoteHappening = true;
#if DEBUG
                _plugin?.Logger.LogInformation("StartVote: Set EofVoteHappening to true at start of vote");
#endif
                _config = config;


                var mapsScrambled = Shuffle(new Random(),
                    _mapLister.Maps!.Select(x => x.Name).Where(x => !_mapCooldown.IsCurrentMap(x) && !_mapCooldown.IsMapInCooldown(x))
                        .ToList());
                mapsEllected = _nominationManager.NominationWinners().Concat(mapsScrambled).Distinct().ToList();

                var generation = _vote.Begin(GetVoteOptions());
                if (Votes.Count > 0) MapVoteStarted?.Invoke(this, EventArgs.Empty);
                // Main's single EndOfMapVote option also governs RTV ballots in this shared manager.
                _pause.AcquireIfEnabled(generation, _mapGeneration, _eomConfig!.PauseMatchWhenVote, () =>
                {
                    var state = _gameRules.ReadPauseState();
                    if (state == null) _plugin?.Logger.LogWarning("PauseMatchWhenVote cannot pause: game rules are unavailable.");
                    return state;
                }, () => Server.ExecuteCommand("mp_pause_match"));
                var players = ServerManager.ValidPlayers();
                _canVote = players.Length;
                timeLeft = _config.VoteDuration;
                if (Votes.Count == 0)
                {
                    EndVote(generation);
                    return;
                }

                foreach (var player in players)
                {
                    ShowMapVoteMenu(player);
                }

                if (!_vote.IsCurrent(generation)) return;
                Timer = _plugin!.AddTimer(1.0F, () =>
                {
                    // A queued callback from a prior vote must not end its replacement.
                    if (!_vote.IsCurrent(generation)) return;
                    try
                    {
                        if (timeLeft <= 0)
                        {
                            EndVote(generation);
                            return; // Add explicit return to ensure code after doesn't run
                        }
                        else
                        {
                            timeLeft--;
                            // Add a timeout check as a safety measure
                            if (_config!.VoteDuration > 0 && ((_config!.VoteDuration - timeLeft) > _config!.VoteDuration + 10))
                            {
#if DEBUG
                                _plugin?.Logger.LogWarning($"Vote timer safety triggered: Vote has been running too long. Forcing end.");
#endif
                                EndVote(generation);
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Ensure flag is reset even if there's an exception in the timer callback
                        if (_vote.Generation != generation) return;
                        _plugin?.Logger.LogError(ex, "Error in map vote timer.");
                        _vote.Reset();
                        CleanupVote();
                        _pluginState.EofVoteHappening = false;
#if DEBUG
                        _plugin?.Logger.LogInformation("StartVote: Reset EofVoteHappening to false due to exception in timer callback");
#endif
                    }
                }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception ex)
            {
                _plugin?.Logger.LogError(ex, "Error starting map vote.");
                _vote.Reset();
                CleanupVote();
                _pluginState.EofVoteHappening = false;
#if DEBUG
                _plugin?.Logger.LogInformation("StartVote: Reset EofVoteHappening to false due to exception in StartVote");
#endif
            }
        }
    }
}

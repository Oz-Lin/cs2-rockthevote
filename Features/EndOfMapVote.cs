using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Logging;
using static CounterStrikeSharp.API.Core.Listeners;

namespace cs2_rockthevote;

public class EndOfMapVote : IPluginDependency<Plugin, Config>
{
    private readonly TimeLimitManager _timeLimit;
    private readonly MaxRoundsManager _maxRounds;
    private readonly PluginState _pluginState;
    private readonly GameRules _gameRules;
    private readonly EndMapVoteManager _voteManager;
    private readonly EndVoteTriggerPolicy _triggers = new();
    private EndOfMapConfig _config = new();
    private Plugin? _plugin;
    private readonly EndVoteMonitor _monitor = new();
    private bool _loaded;
    private bool _mapActive;

    public EndOfMapVote(StringLocalizer localizer, TimeLimitManager timeLimit, MaxRoundsManager maxRounds,
        PluginState pluginState, GameRules gameRules, EndMapVoteManager voteManager)
        : this(timeLimit, maxRounds, pluginState, gameRules, voteManager) { }

    public EndOfMapVote(TimeLimitManager timeLimit, MaxRoundsManager maxRounds, PluginState pluginState,
        GameRules gameRules, EndMapVoteManager voteManager)
    {
        _timeLimit = timeLimit; _maxRounds = maxRounds; _pluginState = pluginState;
        _gameRules = gameRules; _voteManager = voteManager;
        // Also observe RTV/manual starts, including ballots that finish between timer ticks.
        _voteManager.MapVoteStarted += (_, _) => _triggers.BallotStarted();
        _voteManager.ExtensionUpdated += (_, _) =>
        {
            if (!_loaded || !_mapActive || !_config.Enabled || !_gameRules.IsReady) return;
            try { ObserveThresholdExits(); }
            catch (Exception ex) { _plugin!.Logger.LogError(ex, "Could not observe extended vote thresholds."); }
        };
    }

    private bool CheckMaxRounds() => _gameRules.TotalRoundsPlayed >= 1 && !_maxRounds.UnlimitedRounds &&
        (_maxRounds.RemainingRounds <= _config.TriggerRoundsBeforeEnd ||
         (_maxRounds.CanClinch && _maxRounds.RemainingWins <= _config.TriggerRoundsBeforeEnd));

    private bool Blocked => !_config.Enabled || !_gameRules.IsReady || _gameRules.WarmupRunning || _pluginState.DisableCommands ||
        _voteManager.VoteInProgress || _voteManager.MapTransitionPending;

    public void StartVote()
    {
        if (!_config.Enabled || _voteManager.VoteInProgress || _pluginState.EofVoteHappening) return;
        _triggers.BallotStarted();
        _voteManager.StartVote(_config);
    }

    private void ObserveThresholdExits()
    {
        // Observe an extension before the next round can decrement its new limit.
        // This only rearms; automatic round ballots still start at RoundStart.
        _triggers.Observe(EndVoteTrigger.Rounds, CheckMaxRounds(), true);
        _triggers.Observe(EndVoteTrigger.Time,
            !_timeLimit.UnlimitedTime && _timeLimit.TimeRemaining <= _config.TriggerSecondsBeforeEnd, true);
    }

    private void ObserveTime()
    {
        if (!_gameRules.IsReady) return;
        _triggers.Observe(EndVoteTrigger.Rounds, CheckMaxRounds(), true);
        // Expired/unknown time is not a fresh threshold exit: do not rearm on zero.
        bool unlimited = _timeLimit.UnlimitedTime;
        decimal remaining = unlimited ? 0 : _timeLimit.TimeRemaining;
        bool eligible = !unlimited && remaining <= _config.TriggerSecondsBeforeEnd;
        if (_triggers.Observe(EndVoteTrigger.Time, eligible, Blocked || remaining <= 0)) StartVote();
    }

    private void EnsureTimer()
    {
        if (!_loaded || !_mapActive || !_config.Enabled) return;
        // Keep monitoring through completed extension votes. Do not require a new round.
        CounterStrikeSharp.API.Modules.Timers.Timer? ownedTimer = null;
        _monitor.Start(() =>
        {
            if (!_loaded || !_mapActive) return;
            try { ObserveTime(); }
            catch (Exception ex) { _plugin!.Logger.LogError(ex, "Error monitoring automatic end-of-map vote."); }
        }, callback =>
        {
            ownedTimer = _plugin!.AddTimer(1.0F, callback, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            return () => PluginTimers.Kill(_plugin, ownedTimer);
        }, () => { if (ownedTimer != null) PluginTimers.Forget(_plugin, ownedTimer); });
    }

    private void InvalidateTimer() => _monitor.Stop();

    private void EndMap()
    {
        _mapActive = false;
        InvalidateTimer();
    }

    public void Stop()
    {
        _loaded = false; _mapActive = false;
        InvalidateTimer();
    }

    public void OnMapStart(string map)
    {
        _mapActive = false;
        _monitor.Abandon(); // Engine already deleted old STOP timers; never Kill their pointers.
        _triggers.Reset(); _mapActive = true;
        EnsureTimer();
    }

    public void OnLoad(Plugin plugin)
    {
        _plugin = plugin; _loaded = true; _mapActive = true;
        plugin.RegisterListener<OnMapEnd>(EndMap);
        plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) =>
        {
            _voteManager.FinishActiveVote();
            return HookResult.Continue;
        }, HookMode.Pre);
        plugin.RegisterEventHandler<EventRoundStart>((ev, info) =>
        {
            if (!_loaded || !_mapActive || !_gameRules.IsReady) return HookResult.Continue;
            EnsureTimer();
            if (_triggers.Observe(EndVoteTrigger.Rounds, CheckMaxRounds(), Blocked)) StartVote();
            return HookResult.Continue;
        });
        plugin.RegisterEventHandler<EventRoundAnnounceMatchStart>((ev, info) =>
        {
            EnsureTimer();
            return HookResult.Continue;
        });
        EnsureTimer();
    }

    public void OnConfigParsed(Config config)
    {
        _config = config.EndOfMapVote;
        if (!_config.Enabled) InvalidateTimer();
        else EnsureTimer();
    }
}

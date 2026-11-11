namespace cs2_rockthevote;

public enum EndVoteTrigger { Time, Rounds }

// One automatic ballot per threshold episode, not one ballot per timer tick.
// Extension must leave a threshold before re-entering it can trigger another vote.
public sealed class EndVoteTriggerPolicy
{
    private bool _timeArmed = true;
    private bool _roundsArmed = true;
    public void Reset() { _timeArmed = true; _roundsArmed = true; }
    public void BallotStarted() { _timeArmed = false; _roundsArmed = false; }
    public bool Observe(EndVoteTrigger trigger, bool eligible, bool blocked)
    {
        ref bool armed = ref (trigger == EndVoteTrigger.Time ? ref _timeArmed : ref _roundsArmed);
        if (!eligible) { armed = true; return false; }
        if (blocked || !armed) return false;
        BallotStarted();
        return true;
    }
}

// Live native timer ownership with a managed cancellation/generation boundary.
// A failed cancellation retains the owned release action for unload/map retry.
public sealed class EndVoteMonitor
{
    private Action? _cancel;
    private Action? _abandon;
    private long _generation;
    public bool Active { get; private set; }
    public void Start(Action tick, Func<Action, Action> schedule, Action? abandon = null)
    {
        if (Active) return;
        if (_cancel != null) throw new InvalidOperationException("Previous monitor cancellation is pending.");
        long generation = ++_generation;
        Active = true;
        try
        {
            _cancel = schedule(() => { if (Active && generation == _generation) tick(); });
            _abandon = abandon;
        }
        catch { Active = false; ++_generation; throw; }
    }
    public void Stop()
    {
        Active = false; ++_generation;
        _cancel?.Invoke();
        _cancel = null; _abandon = null;
    }
    public void Abandon()
    {
        Active = false; ++_generation;
        _abandon?.Invoke();
        _cancel = null; _abandon = null;
    }
}

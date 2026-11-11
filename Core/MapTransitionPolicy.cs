namespace cs2_rockthevote;

public sealed record MapTarget(string Name, string? WorkshopId)
{
    public static MapTarget Resolve(IEnumerable<Map> maps, string name) => From(
        maps.FirstOrDefault(map => string.Equals(map.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Selected map is not in the loaded map list.", nameof(name)));
    public static MapTarget From(Map map)
    {
        var name = map.Name;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.EndsWith('/') ||
            name.Split('/').Any(part => part is "." or "..") ||
            name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/')))
            throw new ArgumentException("Map name is not a safe engine command token.");
        var id = map.Id;
        if (id != null && (id.Length == 0 || id.Any(c => c is < '0' or > '9') || !ulong.TryParse(id, out var value) || value == 0))
            throw new ArgumentException("Workshop ID must be a positive unsigned integer.");
        return new(name, id);
    }
    public string ChangeCommand(bool localMap) => localMap ? $"changelevel {Name}" : WorkshopId != null ? $"host_workshop_map {WorkshopId}" : $"ds_workshop_changelevel {Name}";
    public bool UsesNextLevel(bool localMap) => localMap || WorkshopId == null;
}

public sealed record MapTransition(long Generation, MapTarget Target, bool AtMatchEnd, string Prefix);

// The timer captures an immutable target; another schedule or map invalidates that exact ticket.
public sealed class MapTransitionPolicy
{
    private long _generation;
    private bool _executed;
    public MapTransition? Current { get; private set; }
    public bool Pending { get; private set; }
    public MapTransition Schedule(MapTarget target, bool atMatchEnd, string prefix)
    {
        Current = new(++_generation, target, atMatchEnd, prefix);
        Pending = true; _executed = false; return Current;
    }
    public MapTransition? Take(bool atMatchEnd)
    {
        if (!Pending || Current == null || Current.AtMatchEnd != atMatchEnd) return null;
        Pending = false; return Current;
    }
    public bool IsCurrent(MapTransition ticket) => ReferenceEquals(Current, ticket) && !_executed;
    public bool TryExecute(MapTransition ticket)
    {
        if (!IsCurrent(ticket)) return false;
        _executed = true; return true;
    }
    public void Reset() { ++_generation; Current = null; Pending = false; _executed = false; }
}

public static class NextLevelPolicy
{
    // Only called with a validated real map, never the localized extension option or an empty result.
    public static void Assign(MapTarget target, bool localMap, Func<string> read, Action<string> write)
    {
        if (!target.UsesNextLevel(localMap)) return;
        write(target.Name);
        if (!string.Equals(read(), target.Name, StringComparison.Ordinal))
            throw new InvalidOperationException("nextlevel did not retain the selected map after the synchronous write.");
    }
}

// Restores only this plugin's still-current assignment, never a later value from another system.
public sealed class NextLevelLease
{
    private string _before = "";
    private string _assigned = "";
    public bool Pending { get; private set; }
    public void Assign(MapTarget target, bool localMap, Func<string> read, Action<string> write)
    {
        if (!target.UsesNextLevel(localMap)) return;
        if (Pending) throw new InvalidOperationException("Previous nextlevel assignment must be released first.");
        _before = read(); _assigned = target.Name; Pending = true;
        NextLevelPolicy.Assign(target, localMap, read, write);
    }
    public void Release(Func<string> read, Action<string> write)
    {
        if (!Pending) return;
        if (!string.Equals(read(), _assigned, StringComparison.Ordinal)) { Pending = false; return; }
        write(_before);
        if (!string.Equals(read(), _before, StringComparison.Ordinal))
            throw new InvalidOperationException("Could not restore the previous owned nextlevel value.");
        Pending = false;
    }
    public void Abandon() => Pending = false;
}

public readonly record struct MatchPauseSnapshot(bool WaitingForResume, bool TimeoutActive);

// This owns a request, not a native engine ownership token. See the documented concurrency limitation.
public sealed class VotePauseLease
{
    public bool Pending { get; private set; }
    public long Generation { get; private set; }
    private long _map;
    public bool AcquireIfEnabled(long generation, long map, bool enabled, Func<MatchPauseSnapshot?> readState, Action pause)
        => enabled && Acquire(generation, map, true, readState(), pause);
    public void ReleaseIfPending(long generation, long map, Func<MatchPauseSnapshot?> readState, Action unpause)
    {
        if (Pending) Release(generation, map, readState(), unpause);
    }
    public bool Acquire(long generation, long map, bool enabled, MatchPauseSnapshot? state, Action pause)
    {
        if (Pending) throw new InvalidOperationException("Previous pause cleanup is incomplete.");
        if (!enabled || state == null || state.Value.WaitingForResume || state.Value.TimeoutActive) return false;
        Generation = generation; _map = map; Pending = true;
        pause(); // Keep cleanup pending if enqueue throws after a partial effect.
        return true;
    }
    public void Release(long generation, long map, MatchPauseSnapshot? state, Action unpause)
    {
        if (!Pending || generation != Generation) return;
        if (map != _map) { Abandon(); return; }
        if (state == null) throw new InvalidOperationException("Cannot verify pause cleanup without game rules.");
        if (state.Value.TimeoutActive) { Abandon(); return; } // A team/technical timeout takes precedence.
        unpause(); // Also cancels a still-queued own pause in normal engine queue order.
        Pending = false;
    }
    public void Abandon() => Pending = false;
}

public static class CleanupActions
{
    public static void ResetThenRun(Action resetState, params Action[] actions) => Run(new[] { resetState }.Concat(actions).ToArray());
    public static void Run(params Action[] actions)
    {
        var errors = new List<Exception>();
        foreach (var action in actions) { try { action(); } catch (Exception e) { errors.Add(e); } }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
}

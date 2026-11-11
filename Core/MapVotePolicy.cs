using System.Collections.ObjectModel;

namespace cs2_rockthevote;

// The live map vote and managed tests share this ballot/lifecycle implementation.
// A voter key is the controller's full entity handle (index AND serial number).
public sealed class MapVotePolicy
{
    private readonly Dictionary<string, int> _totals = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _ballots = new();

    public MapVotePolicy()
    {
        Totals = new ReadOnlyDictionary<string, int>(_totals);
    }

    public IReadOnlyDictionary<string, int> Totals { get; }
    public int BallotCount => _ballots.Count;
    public long Generation { get; private set; }
    public bool Active { get; private set; }

    public long Begin(IEnumerable<string> options)
    {
        if (Active) throw new InvalidOperationException("A map vote is already active.");
        // Materialize before changing state, so failed enumeration cannot leave half a vote.
        var candidates = options.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        Reset();
        foreach (var option in candidates) _totals.Add(option, 0);
        Active = true;
        return Generation;
    }

    public bool IsCurrent(long generation) => Active && generation == Generation;

    public bool Cast(long generation, uint voter, string option)
    {
        if (!IsCurrent(generation) || !_totals.ContainsKey(option)) return false;
        if (_ballots.TryGetValue(voter, out var previous))
        {
            if (previous == option) return true;
            _totals[previous] = Math.Max(0, _totals[previous] - 1);
        }
        _totals[option]++;
        _ballots[voter] = option;
        return true;
    }

    public bool Revoke(long generation, uint voter)
    {
        if (!IsCurrent(generation) || !_ballots.Remove(voter, out var previous)) return false;
        _totals[previous] = Math.Max(0, _totals[previous] - 1);
        return true;
    }

    public bool TryEnd(long generation, Func<int, int> chooseIndex, out MapVoteResult? result)
    {
        result = null;
        if (!IsCurrent(generation)) return false;
        // Latch before evaluating or calling any external result/cleanup code.
        Active = false;
        result = Evaluate(Totals, chooseIndex);
        return true;
    }

    public void Reset()
    {
        Active = false;
        Generation++;
        _totals.Clear();
        _ballots.Clear();
    }

    public static MapVoteResult? Evaluate(IReadOnlyDictionary<string, int> totals, Func<int, int> chooseIndex)
    {
        var options = totals.Select(x => new KeyValuePair<string, int>(x.Key, Math.Max(0, x.Value))).ToArray();
        if (options.Length == 0) return null;
        var highest = options.Max(x => x.Value);
        var winners = options.Where(x => x.Value == highest).ToArray();
        // Preserve map-vote behavior: an all-zero ballot still chooses a candidate.
        var winner = winners.Length == 1 ? winners[0] : winners[chooseIndex(winners.Length)];
        decimal total = options.Sum(x => (decimal)x.Value);
        return new(winner.Key, winner.Value, total, total > 0 ? winner.Value / total * 100M : 0);
    }
}

public sealed record MapVoteResult(string Winner, int WinningVotes, decimal TotalVotes, decimal WinningPercentage);

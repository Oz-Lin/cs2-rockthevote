namespace cs2_rockthevote;

// Shared by the live extension vote and managed regression tests.
public static class ExtensionVotePolicy
{
    public static void EnsureOptions(IDictionary<string, int> totals)
    {
        totals.TryAdd("Yes", 0);
        totals.TryAdd("No", 0);
    }

    public static bool Cast<T>(IDictionary<string, int> totals, IDictionary<T, string> voters, T voter, string answer) where T : notnull
    {
        if (answer != "Yes" && answer != "No") return false;
        EnsureOptions(totals);
        if (voters.TryGetValue(voter, out var previous))
            totals[previous] = Math.Max(0, totals[previous] - 1);
        totals[answer]++;
        voters[voter] = answer;
        return true;
    }

    public static bool Revoke<T>(IDictionary<string, int> totals, IDictionary<T, string> voters, T voter) where T : notnull
    {
        if (!voters.TryGetValue(voter, out var previous)) return false;
        EnsureOptions(totals);
        totals[previous] = Math.Max(0, totals[previous] - 1);
        voters.Remove(voter);
        return true;
    }

    // Preserve the existing random tie-break only when actual votes were cast.
    public static ExtensionVoteResult Evaluate(IReadOnlyDictionary<string, int> totals, Func<int, int> chooseIndex)
    {
        var options = new[] { "Yes", "No" }.Select(answer => new KeyValuePair<string, int>(answer, Math.Max(0, totals.GetValueOrDefault(answer)))).ToArray();
        var total = options.Sum(x => x.Value);
        if (total == 0) return new(null, 0, 0);
        var highest = options.Max(x => x.Value);
        var winners = options.Where(x => x.Value == highest).ToArray();
        var winner = winners.Length == 1 ? winners[0] : winners[chooseIndex(winners.Length)];
        return new(winner.Key, total, winner.Value / (decimal)total * 100M);
    }
}

public sealed record ExtensionVoteResult(string? Winner, int TotalVotes, decimal WinningPercentage)
{
    public bool Passed => TotalVotes > 0 && Winner == "Yes";
}

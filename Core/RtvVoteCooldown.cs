namespace cs2_rockthevote.Core
{
    /// <summary>Time since the last completed RTV ballot, never time since map start.</summary>
    public sealed class RtvVoteCooldown
    {
        private readonly TimeProvider _clock;
        private long? _lastCompleted;

        public RtvVoteCooldown() : this(TimeProvider.System) { }

        public RtvVoteCooldown(TimeProvider clock)
        {
            ArgumentNullException.ThrowIfNull(clock);
            _clock = clock;
        }

        public void RecordCompletedVote() => _lastCompleted = _clock.GetTimestamp();

        public int RemainingSeconds(int cooldownSeconds)
        {
            if (cooldownSeconds <= 0 || _lastCompleted is not long completed) return 0;

            // Monotonic timestamps avoid changes to the server's wall clock. Subtract
            // elapsed seconds rather than adding a duration to a bounded timestamp.
            var elapsed = Math.Max(0, _clock.GetElapsedTime(completed, _clock.GetTimestamp()).TotalSeconds);
            return (int)Math.Ceiling(Math.Max(0, cooldownSeconds - elapsed));
        }
    }
}

namespace cs2_rockthevote
{
    public class AsyncVoteValidator
    {
        private float VotePercentage = 0F;
        public int RequiredVotes => GetRequiredVotes(ServerManager.ValidPlayerCount());

        public int GetRequiredVotes(int totalPlayers) => (int)Math.Round(totalPlayers * VotePercentage);
        private IVoteConfig _config { get; set; }
        private HashSet<int> VotedPlayers { get; set; } = new HashSet<int>();


        public AsyncVoteValidator(IVoteConfig config)
        {
            _config = config;
            VotePercentage = _config.VotePercentage / 100F;
        }

        public bool CheckVotes(int numberOfVotes)
        {
            return CheckVotes(numberOfVotes, ServerManager.ValidPlayerCount());
        }

        public bool CheckVotes(int numberOfVotes, int totalPlayers)
        {
            return numberOfVotes > 0 && numberOfVotes >= GetRequiredVotes(totalPlayers);
        }

        public void AddVote(int playerId)
        {
            VotedPlayers.Add(playerId);
        }

        public void RemoveVote(int playerId)
        {
            VotedPlayers.Remove(playerId);
        }

        public int GetVoteCount()
        {
            return VotedPlayers.Count;
        }
    }
}

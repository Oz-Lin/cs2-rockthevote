using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace cs2_rockthevote
{
    public interface ICommandConfig
    {
        public bool EnabledInWarmup { get; set; }
        public int MinPlayers { get; set; }
        public int MinRounds { get; set; }
    }

    public interface IVoteConfig
    {
        public int VotePercentage { get; set; }
        public bool ChangeMapImmediately { get; set; }
        public bool IgnoreSpec { get; set; }
    }

    public interface IEndOfMapConfig
    {
        public int MapsToShow { get; set; }
        public bool ChangeMapImmediately { get; set; }
        public int VoteDuration { get; set; }
        public int HudMenu { get; set; }
        public bool HideHudAfterVote { get; set; }
        public int ExtendTimeStep { get; set; } 
        public int ExtendRoundStep { get; set; }

    }

    public interface IExtendMapConfig
    {
        public bool Enabled { get; set; }
        public int VoteDuration { get; set; }
        public int VotePercentage { get; set; }
        public int ExtendTimeStep { get; set; }
        public int ExtendRoundStep { get; set; } 
        public int HudMenu { get; set; }
        public int ExtendLimit { get; set; }
    }

    public class EndOfMapConfig : IEndOfMapConfig, IExtendMapConfig
    {
        public bool Enabled { get; set; } = true;
        public int MapsToShow { get; set; } = 6;
        public int HudMenu { get; set; } = 1;
        public bool ChangeMapImmediately { get; set; } = false;
        public int VoteDuration { get; set; } = 30;
        public bool HideHudAfterVote { get; set; } = false;
        public int TriggerSecondsBeforeEnd { get; set; } = 120;
        public int TriggerRoundsBeforeEnd { get; set; } = 2;
        public float DelayToChangeInTheEnd { get; set; } = 6F;
        public bool AllowExtend { get; set; } = true;
        public bool RoundBased { get; set; } = true;
        public int ExtendTimeStep { get; set; } = 15;
        public int ExtendRoundStep { get; set; } = 5;
        public int VotePercentage { get; set; } = 60;
        public int ExtendLimit { get; set; } = 3;
    }

    public class RtvConfig : ICommandConfig, IVoteConfig, IEndOfMapConfig, IExtendMapConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = true;
        public bool NominationEnabled { get; set; } = true;
        public int MinPlayers { get; set; } = 0;
        public int MinRounds { get; set; } = 0;
        public bool ChangeMapImmediately { get; set; } = true;
        public bool HideHudAfterVote { get; set; } = false;
        public int MapsToShow { get; set; } = 6;
        public int VoteDuration { get; set; } = 30;
        public int VotePercentage { get; set; } = 60;
        public int HudMenu { get; set; } = 1;
        public bool DontChangeRtv { get; set; } = true;
        public bool IgnoreSpec { get; set; } = true;
        public int VoteCooldownTime { get; set; } = 300;
        public int ExtendTimeStep { get; set; } = 15; 
        public int ExtendRoundStep { get; set; } = 5;
        public int ExtendLimit { get; set; } = 3;

    }

    public class ExtendMapConfig : ICommandConfig, IVoteConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = true;
        public int MinPlayers { get; set; } = 0;
        public int MinRounds { get; set; } = 0;
        public int VotePercentage { get; set; } = 60;
        public bool ChangeMapImmediately { get; set; } = false;
        public bool IgnoreSpec { get; set; } = true;
        public bool RoundBased { get; set; } = true;

        public int ExtendTimeStep { get; set; } = 15;
        public int ExtendRoundStep { get; set; } = 5;
        //public int ExtendLimit { get; set; } = 3; // share with EndOfMapConfig ExtendLimit
    }

    public class VotemapConfig : ICommandConfig, IVoteConfig
    {
        public bool Enabled { get; set; } = true;
        public int VotePercentage { get; set; } = 60;
        public bool ChangeMapImmediately { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = true;
        public int MinPlayers { get; set; } = 0;
        public int MinRounds { get; set; } = 0;
        public int HudMenu { get; set; } = 0;
        public bool IgnoreSpec { get; set; } = true;
    }

    public class VipExtendMapConfig : IExtendMapConfig
    {
        public bool Enabled { get; set; } = true;
        public int VoteDuration { get; set; } = 30;
        public int VotePercentage { get; set; } = 60;
        public int ExtendTimeStep { get; set; } = 15;
        public int ExtendRoundStep { get; set; } = 5;
        public int ExtendLimit { get; set; } = 3;
        public bool RoundBased { get; set; } = true;
        public int HudMenu { get; set; } = 1;
    }

    public class TimeleftConfig
    {
        public bool ShowToAll { get; set; } = false;
    }

    public class NextmapConfig
    {
        public bool ShowToAll { get; set; } = false;
    }

    public class Config : IBasePluginConfig
    {
        public int Version { get; set; } = 16;
        public RtvConfig Rtv { get; set; } = new();
        public VotemapConfig Votemap { get; set; } = new();
        public EndOfMapConfig EndOfMapVote { get; set; } = new();
        public ExtendMapConfig ExtendMapVote { get; set; } = new();
        public VipExtendMapConfig VipExtendMapVote { get; set; } = new();
        public TimeleftConfig Timeleft { get; set; } = new();
        public NextmapConfig Nextmap { get; set; } = new();
        public ushort MapsInCoolDown { get; set; } = 3;
    }
}

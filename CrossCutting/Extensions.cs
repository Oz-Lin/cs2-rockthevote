using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace cs2_rockthevote
{
    public static class Extensions
    {
        public static bool ReallyValid(this CCSPlayerController? player, bool considerBots = false, bool ignoreSpecs = false)
        {
            return player is not null && player.IsValid && player.Connected == PlayerConnectedState.Connected &&
                (considerBots || (!player.IsBot && !player.IsHLTV)) &&
                (!ignoreSpecs || (player.Team != CsTeam.Spectator && player.Team != CsTeam.None));
        }
    }
}

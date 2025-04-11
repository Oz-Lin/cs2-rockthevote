using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [ConsoleCommand("css_extend", "Extends time for the current map")] // admin extend time
        [CommandHelper(minArgs: 1, usage: "<number of minutes to extend the map time ex. 30>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        [RequiresPermissions("@css/changemap")]
        public void OnExtendRoundTimeCommand(CCSPlayerController? player, CommandInfo commandInfo)
        {
            var newRoundTime = commandInfo.GetArg(1);
            int newRoundTimeInt = 0;

            var intParseSuccess = int.TryParse(newRoundTime, out newRoundTimeInt);

            if (intParseSuccess)
            {
                var secondsToIncrease = newRoundTimeInt;

                _extendRoundTime.CommandHandler(player!, commandInfo, newRoundTimeInt);
            }
            else
            {
                commandInfo.ReplyToCommand("You entered an incorrect integer for the map time limit. Try a number between 1 and 60.");
            }
        }
    }

    public class ExtendRoundTimeCommand : IPluginDependency<Plugin, Config>
    {
        private TimeLimitManager _timeLimitManager;
        private readonly ExtendRoundTimeManager _extendRoundTimeManager;
        private readonly GameRules _gameRules;
        private StringLocalizer _localizer;
        private EndOfMapConfig? _eomConfig = new();

        public ExtendRoundTimeCommand(TimeLimitManager timeLimitManager, ExtendRoundTimeManager extendRoundTimeManager, GameRules gameRules, IStringLocalizer stringLocalizer)
        {
            _gameRules = gameRules;
            _localizer = new StringLocalizer(stringLocalizer, "extendtime.prefix");
            _timeLimitManager = timeLimitManager;
            _extendRoundTimeManager = extendRoundTimeManager;

        }

        public bool CommandHandler(CCSPlayerController player, CommandInfo commandInfo, int minutesToExtend)
        {
            if (_gameRules.WarmupRunning)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                return false;
            }

            if (!_timeLimitManager.UnlimitedTime)
            {
                //if (_timeLimitManager.TimeRemaining > 1)
                //{
                    if (_eomConfig!.RoundBased == true)
                    {
                        _extendRoundTimeManager.ExtendMapTimeLimit(minutesToExtend, _timeLimitManager, _gameRules);
                    }
                    else
                    {
                        _extendRoundTimeManager.ExtendRoundTime(minutesToExtend, _timeLimitManager, _gameRules);
                    }

                    player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.admin-extend-time", minutesToExtend));

                    return true;
                //}
                //else
                //{
                //    player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.notapplicable"));
                 //   return false;
                //}
            }
            else
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.notapplicable"));
                return false;
            }
        }
    }
}

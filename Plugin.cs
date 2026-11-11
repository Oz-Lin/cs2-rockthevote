using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Attributes;
using cs2_rockthevote.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static CounterStrikeSharp.API.Core.Listeners;

namespace cs2_rockthevote
{
    public class PluginDependencyInjection : IPluginServiceCollection<Plugin>
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddLogging();
            var di = new DependencyManager<Plugin, Config>(serviceCollection.BuildServiceProvider().GetRequiredService<ILogger<DependencyManager<Plugin, Config>>>());
            di.LoadDependencies(typeof(Plugin).Assembly);
            di.AddIt(serviceCollection);
            serviceCollection.AddScoped<StringLocalizer>();
        }
    }

    [MinimumApiVersion(379)]
    public partial class Plugin : BasePlugin, IPluginConfig<Config>
    {
        public override string ModuleName => "RockTheVote";
        public override string ModuleVersion => "1.9.7-css379-votefixes";
        public override string ModuleAuthor => "abnerfs, Oz-Lin, [ZeRo]XiaoLinWuDi, theextr1m";
        public override string ModuleDescription => "https://github.com/oz-lin/cs2-rockthevote";

        private readonly DependencyManager<Plugin, Config> _dependencyManager;
        private readonly NominationCommand _nominationManager;
        private readonly ChangeMapManager _changeMapManager;
        private readonly VotemapCommand _votemapManager;
        private readonly RockTheVoteCommand _rtvManager;
        //private readonly ExtendCommand _extManager;
        private readonly TimeLeftCommand _timeLeft;
        private readonly ExtendRoundTimeCommand _extendRoundTime;
        private readonly VoteExtendRoundTimeCommand _voteExtendRoundTime;
        private readonly NextMapCommand _nextMap;
        private readonly EndMapVoteManager _endMapVoteManager;
        private readonly DisplayMapListCommandHandler _displayMapListCommandHandler;
        private readonly ExtendMapCommand _extendMapManager;
        private readonly RevoteCommand _revoteCommand;
        private readonly EndOfMapVote _endOfMapVote;
        private readonly ExtendRoundTimeManager _extendVoteManager;
        private readonly GameRules _gameRules;

        public Plugin(DependencyManager<Plugin, Config> dependencyManager,
            NominationCommand nominationManager,
            ChangeMapManager changeMapManager,
            VotemapCommand voteMapManager,
            RockTheVoteCommand rtvManager,
            //ExtendCommand extManager,
            TimeLeftCommand timeLeft,
            ExtendRoundTimeCommand extendRoundTime,
            VoteExtendRoundTimeCommand voteExtendRoundTime,
            NextMapCommand nextMap,
            EndMapVoteManager endMapVoteManager,
            DisplayMapListCommandHandler displayMapListCommandHandler,
            MapLister mapLister,
            ExtendMapCommand extendMapManager,
            RevoteCommand revoteCommand, EndOfMapVote endOfMapVote,
            ExtendRoundTimeManager extendVoteManager, GameRules gameRules)
        {
            _dependencyManager = dependencyManager;
            _nominationManager = nominationManager;
            _changeMapManager = changeMapManager;
            _votemapManager = voteMapManager;
            _rtvManager = rtvManager;
            //_extManager = extManager;
            _timeLeft = timeLeft;
            _extendRoundTime = extendRoundTime;
            _voteExtendRoundTime = voteExtendRoundTime;
            _nextMap = nextMap;
            _endMapVoteManager = endMapVoteManager;
            _displayMapListCommandHandler = displayMapListCommandHandler;
            _mapLister = mapLister;
            _extendMapManager = extendMapManager;
            _revoteCommand = revoteCommand;
            _endOfMapVote = endOfMapVote;
            _extendVoteManager = extendVoteManager; _gameRules = gameRules;
        }

        public Config Config { get; set; } = null!;

        public string Localize(string prefix, string key, params object[] values)
        {
            return $"{Localizer[prefix]} {Localizer[key, values]}";
        }

        public override void Load(bool hotReload)
        {
            ScreenMenus = new ScreenMenus.ScreenMenuService(this);
#if DEBUG
            Logger.LogInformation($"Plugin loading... (hot reload: {hotReload})");
#endif
            global::cs2_rockthevote.ScreenMenus.StartupGuard.Complete(() =>
            {
                ScreenMenus.Start();
                _dependencyManager.OnPluginLoad(this);
                RegisterListener<OnMapStart>(_dependencyManager.OnMapStart);
            }, () => CleanupActions.Run(_endOfMapVote.Stop, _endMapVoteManager.Stop, _extendVoteManager.Stop, _changeMapManager.Stop, _gameRules.Stop, () => ScreenMenus.Dispose()));
#if DEBUG
            Logger.LogInformation("Plugin loaded successfully");
#endif
        }

        public ScreenMenus.ScreenMenuService ScreenMenus { get; private set; } = null!;

        public override void Unload(bool hotReload)
        {
            CleanupActions.Run(_endOfMapVote.Stop, _endMapVoteManager.Stop, _extendVoteManager.Stop, _changeMapManager.Stop, _gameRules.Stop, () => ScreenMenus?.Dispose());
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult OnChat(EventPlayerChat @event, GameEventInfo info)
        {
            var player = Utilities.GetPlayerFromUserid(@event.Userid);
            if (player is not null)
            {
                var text = @event.Text.Trim().ToLower();
                if (text == "rtv")
                {
                    _rtvManager.CommandHandler(player);
                }
                else if (text == "ext")
                {
                    //_extManager.CommandHandler(player);
                    _extendMapManager.CommandHandler(player);
                }
                else if (text.StartsWith("nominate"))
                {
                    var split = text.Split("nominate");
                    var map = split.Length > 1 ? split[1].Trim() : "";
                    _nominationManager.CommandHandler(player, map);
                }
                else if (text.StartsWith("nom") || text.StartsWith("!nom"))
                {
                    var split = text.Split("nom");
                    var map = split.Length > 1 ? split[1].Trim() : "";
                    _nominationManager.CommandHandler(player, map);
                }
                else if (text.StartsWith("yd"))
                {
                    var split = text.Split("yd");
                    var map = split.Length > 1 ? split[1].Trim() : "";
                    _nominationManager.CommandHandler(player, map);
                }
                else if (text.StartsWith("votemap"))
                {
                    var split = text.Split("votemap");
                    var map = split.Length > 1 ? split[1].Trim() : "";
                    _votemapManager.CommandHandler(player, map);
                }
                else if (text.StartsWith("vm"))
                {
                    var split = text.Split("vm");
                    var map = split.Length > 1 ? split[1].Trim() : "";
                    _votemapManager.CommandHandler(player, map);
                }
                else if (text.StartsWith("timeleft"))
                {
                    _timeLeft.CommandHandler(player);
                }
                else if (text.StartsWith("nextmap"))
                {
                    _nextMap.CommandHandler(player);
                }
                else if (text.StartsWith("extend"))
                {
                    _extendMapManager.CommandHandler(player);
                }
                // TODO: Implement this later
                //else if (text == "revote")
                //{
                //    _endMapVoteManager.HandleRevoteCommand(player);
                //}
            }
            return HookResult.Continue;
        }

        public void OnConfigParsed(Config config)
        {
            Config = config;

            if (Config.Version < 16)
                Console.WriteLine("[RockTheVote] please delete it from addons/counterstrikesharp/configs/plugins/RockTheVote and let the plugin recreate it on load");

            if (Config.Version < 14)
                throw new Exception("Your config file is too old, please delete it from addons/counterstrikesharp/configs/plugins/RockTheVote and let the plugin recreate it on load");

            _dependencyManager.OnConfigParsed(config);
        }
    }
}

using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class MapLister : IPluginDependency<Plugin, Config>
    {
        public Map[]? Maps { get; private set; } = null;
        public bool MapsLoaded { get; private set; } = false;

        public event EventHandler<Map[]>? EventMapsLoaded;

        private Plugin? _plugin;

        public MapLister()
        {

        }

        public void Clear()
        {
            MapsLoaded = false;
            Maps = null;
        }

        public void LoadMaps()
        {
            Clear();
            string moduleDirectory = _plugin!.ModuleDirectory;
#if DEBUG
            string mapsFile = MapListPolicy.ResolvePath(moduleDirectory);
            _plugin?.Logger.LogInformation($"MapLister: Loading maps from {mapsFile}");
#endif
            Maps = MapListPolicy.Load(moduleDirectory);

            MapsLoaded = true;
#if DEBUG
            _plugin?.Logger.LogInformation($"MapLister: Successfully loaded {Maps.Length} maps");
#endif
            if (EventMapsLoaded is not null)
                EventMapsLoaded.Invoke(this, Maps!);
        }

        public void OnMapStart(string _map)
        {
            if (_plugin is not null)
            {
#if DEBUG
                _plugin.Logger.LogInformation($"MapLister: Map started, reloading maps");
#endif
                LoadMaps();
            }
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            LoadMaps();
        }

        // returns "" if there's no matching
        // if there's more than one matching name, list all the matching names for players to choose
        // otherwise, returns the matching name
        public string GetSingleMatchingMapName(string map, CCSPlayerController player, StringLocalizer _localizer)
        {
            if (this.Maps!.Select(x => x.Name).FirstOrDefault(x => x.ToLower() == map) is not null)
                return map;

            var matchingMaps = this.Maps!
                .Select(x => x.Name)
                .Where(x => x.ToLower().Contains(map.ToLower()))
                .ToList();

            if (matchingMaps.Count == 0)
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.invalid-map"));
                return "";
            }
            else if (matchingMaps.Count > 1)
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("nominate.multiple-maps-containing-name"));
                player!.PrintToChat(string.Join(", ", matchingMaps));
                //return matchingMaps;
                return "";
            }

            return matchingMaps[0];
        }

        public IEnumerable<Map> GetMaps()
        {
            return Maps ?? Enumerable.Empty<Map>();
        }
    }
}

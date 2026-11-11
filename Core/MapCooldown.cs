using CounterStrikeSharp.API;

namespace cs2_rockthevote.Core
{
    public class MapCooldown : IPluginDependency<Plugin, Config>
    {
        private readonly MapCooldownPolicy _policy = new();

        public event EventHandler<Map[]>? EventCooldownRefreshed;

        public MapCooldown(MapLister mapLister)
        {
            mapLister.EventMapsLoaded += (sender, maps) => RefreshMaps(maps, Server.MapName);
        }

        public void RefreshMaps(Map[] maps, string? currentMap)
        {
            _policy.Refresh(maps, currentMap);
            // Consumers initialize chat/HTML menus here even when cooldown is off
            // or the engine has not provided a current map name yet.
            EventCooldownRefreshed?.Invoke(this, maps);
        }

        public void OnConfigParsed(Config config)
        {
            _policy.Configure(config.MapsInCoolDown);
        }

        public bool IsMapInCooldown(string map)
        {
            return _policy.Contains(map);
        }

        public bool IsCurrentMap(string map) => _policy.IsCurrentMap(map);

        public bool AreSameMap(string? first, string? second) => _policy.AreSameMap(first, second);

        public bool AddMapToCooldown(string map)
        {
            return _policy.Add(map);
        }
    }
}

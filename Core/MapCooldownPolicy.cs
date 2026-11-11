using System.Globalization;

namespace cs2_rockthevote.Core
{
    /// <summary>Cooldown identities derived only from the explicitly loaded map list.</summary>
    public sealed class MapCooldownPolicy
    {
        private readonly List<string> _history = new();
        private Dictionary<string, string> _identities = new(StringComparer.OrdinalIgnoreCase);
        private ushort _mapsInCooldown;
        private string? _currentMap;

        public void Configure(ushort mapsInCooldown)
        {
            _mapsInCooldown = mapsInCooldown;
            if (mapsInCooldown == 0) _history.Clear();
        }

        public void Refresh(Map[] maps, string? currentMap)
        {
            ArgumentNullException.ThrowIfNull(maps);
            _identities = maps.Where(map => !string.IsNullOrWhiteSpace(map.Name))
                .GroupBy(map => map.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group =>
                {
                    // Conflicting definitions of the same name do not prove an ID.
                    var identities = group.Select(MapIdentity).Distinct(StringComparer.Ordinal).ToArray();
                    return identities.Length == 1 ? identities[0] : NameIdentity(group.Key);
                }, StringComparer.OrdinalIgnoreCase);
            _currentMap = currentMap;

            if (_mapsInCooldown == 0)
            {
                _history.Clear();
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentMap)) Append(Resolve(currentMap));
        }

        public bool Contains(string map) => !string.IsNullOrWhiteSpace(map) && _history.Contains(Resolve(map));

        public bool IsCurrentMap(string map) => AreSameMap(map, _currentMap);

        public bool AreSameMap(string? first, string? second) =>
            !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
            StringComparer.Ordinal.Equals(Resolve(first), Resolve(second));

        public bool Add(string map)
        {
            if (string.IsNullOrWhiteSpace(map) || Contains(map)) return false;
            Append(Resolve(map));
            return true;
        }

        private void Append(string identity)
        {
            // Preserve the existing history convention: the current map plus the
            // configured number of previous entries. Alias handling does not resize it.
            if (_mapsInCooldown > 0 && _history.Count > _mapsInCooldown) _history.RemoveAt(0);
            _history.Add(identity);
        }

        private string Resolve(string name) => _identities.TryGetValue(name.Trim(), out var identity)
            ? identity : NameIdentity(name);

        private static string NameIdentity(string name) => "name:" + name.Trim().ToUpperInvariant();

        private static string MapIdentity(Map map)
        {
            var id = map.Id?.Trim();
            return !string.IsNullOrEmpty(id) && id.All(character => character is >= '0' and <= '9') &&
                ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var workshopId) && workshopId > 0
                ? "workshop:" + workshopId.ToString(CultureInfo.InvariantCulture)
                : NameIdentity(map.Name);
        }
    }
}

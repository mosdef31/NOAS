using System;
using System.Collections.Generic;
using System.Linq;

namespace AirSpawn
{
    /// <summary>
    /// Collects aircraft definitions that the Encyclopedia has loaded.
    /// Populated via a Harmony postfix on Encyclopedia.AfterLoad; entries
    /// are therefore available from the first time a map is entered.
    /// </summary>
    internal static class AircraftRegistry
    {
        // jsonKey → display name (e.g. "Cricket" → "CI-22 Cricket")
        private static readonly Dictionary<string, string> _aircraft =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Raised on the main thread whenever new aircraft are added.</summary>
        internal static event Action? Updated;

        /// <summary>Read-only view of every registered aircraft.</summary>
        internal static IReadOnlyDictionary<string, string> All => _aircraft;

        internal static void Register(string jsonKey, string displayName)
        {
            if (string.IsNullOrEmpty(jsonKey)) return;
            if (_aircraft.ContainsKey(jsonKey))  return;

            _aircraft[jsonKey] = displayName ?? jsonKey;
            Updated?.Invoke();
        }

        /// <summary>Returns jsonKeys sorted alphabetically.</summary>
        internal static string[] SortedKeys() =>
            _aircraft.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

        internal static bool Contains(string jsonKey) => _aircraft.ContainsKey(jsonKey);
    }
}

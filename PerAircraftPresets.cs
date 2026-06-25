using BepInEx.Configuration;
using BepInEx.Logging;

namespace AirSpawn
{
    /// <summary>
    /// Stores per-aircraft spawn parameters in a separate file
    /// (BepInEx/config/AirSpawnPresets.cfg).
    ///
    /// Altitudes are always stored in metres.
    /// Airspeeds are always stored in m/s, independent of the user's
    /// display-unit preference, so presets remain valid if the unit is changed.
    /// </summary>
    internal sealed class PerAircraftPresets
    {
        private readonly ConfigFile _file;
        private readonly ManualLogSource _log;

        internal PerAircraftPresets(string filePath, ManualLogSource log)
        {
            _log = log;
            _file = new ConfigFile(filePath, saveOnInit: true);
        }

        /// <summary>
        /// Returns the stored preset for <paramref name="jsonKey"/>.
        /// If no preset exists the supplied defaults are returned and written
        /// to the file so the user can see and edit them.
        /// </summary>
        internal (float altitudeM, float airspeedMs) Get(
            string jsonKey,
            float defaultAltM,
            float defaultSpdMs)
        {
            var alt = _file.Bind(
                jsonKey, "AltitudeM", defaultAltM,
                new ConfigDescription("Spawn altitude in metres for this aircraft."));

            var spd = _file.Bind(
                jsonKey, "AirspeedMs", defaultSpdMs,
                new ConfigDescription("Spawn airspeed in m/s for this aircraft."));

            return (alt.Value, spd.Value);
        }

        /// <summary>
        /// Persists a preset. <paramref name="airspeedMs"/> must be in m/s
        /// regardless of which display unit is currently active.
        /// </summary>
        internal void Set(string jsonKey, float altitudeM, float airspeedMs)
        {
            // Bind returns the existing entry when already created; only the
            // value assignment matters here.
            _file.Bind(jsonKey, "AltitudeM",  0f, "").Value = altitudeM;
            _file.Bind(jsonKey, "AirspeedMs", 0f, "").Value = airspeedMs;
            _file.Save();

            _log.LogDebug(
                $"[AirSpawn] Saved preset [{jsonKey}] " +
                $"alt={altitudeM:F0} m  spd={airspeedMs:F0} m/s");
        }
    }
}

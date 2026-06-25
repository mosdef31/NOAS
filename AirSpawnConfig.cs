using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace AirSpawn
{
    /// <summary>
    /// Owns every <see cref="ConfigEntry{T}"/> and wires the runtime behaviours:
    ///   • speed-unit conversion when the unit selection changes
    ///   • per-aircraft preset load / auto-save
    ///   • aircraft dropdown refresh when <see cref="AircraftRegistry"/> updates
    /// </summary>
    internal sealed class AirSpawnConfig
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const string Sec1 = "1 - Air Spawn";
        private const string Sec2 = "2 - Per-Aircraft Presets";
        private const string NoAircraft = "(default)";

        // Airspeed range covers ≈ Mach 3 in every unit:
        //   m/s: 3–1030   km/h: 10–3710   mph: 6–2300   kt: 5–2000
        // We use a single wide range so AcceptableValueRange never rejects
        // a converted value after a unit change.
        private const float SpeedMin = 3f;
        private const float SpeedMax = 3710f;

        // ── Public ConfigEntries (read by patches) ────────────────────────
        internal ConfigEntry<bool>      EnableAirSpawn    { get; }
        internal ConfigEntry<float>     SpawnAltitude     { get; }   // metres
        internal ConfigEntry<float>     SpawnAirspeed     { get; }   // display unit
        internal ConfigEntry<SpeedUnit> SpeedUnit         { get; }

        internal ConfigEntry<bool>      EnablePerAircraft { get; }
        internal ConfigEntry<string>    SelectedAircraft  { get; }
        internal ConfigEntry<float>     PresetAltitude    { get; }   // metres
        internal ConfigEntry<float>     PresetAirspeed    { get; }   // display unit

        // ── Private state ────────────────────────────────────────────────────
        private readonly PerAircraftPresets _presets;
        private readonly ManualLogSource    _log;

        private SpeedUnit _prevUnit;
        private bool      _loadingPreset;

        // ConfigurationManagerAttributes instances held by reference so their
        // Browsable flag can be flipped at runtime.
        private readonly ConfigurationManagerAttributes _selAttr =
            new ConfigurationManagerAttributes { Order = 30 };
        private readonly ConfigurationManagerAttributes _preAltAttr =
            new ConfigurationManagerAttributes { Order = 20 };
        private readonly ConfigurationManagerAttributes _preSpdAttr =
            new ConfigurationManagerAttributes { Order = 10 };

        // AcceptableValueList for the aircraft dropdown and its backing field.
        private readonly AcceptableValueList<string> _aircraftList =
            new AcceptableValueList<string>(NoAircraft);

        // We update the list's backing array at runtime via reflection so that
        // ConfigurationManager's combo-box renders the full aircraft list.
        private static readonly FieldInfo? AvlBackingField =
            typeof(AcceptableValueList<string>)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.FieldType == typeof(string[]));

        // ── Constructor ──────────────────────────────────────────────────────
        internal AirSpawnConfig(
            ConfigFile          cfg,
            PerAircraftPresets  presets,
            ManualLogSource     log)
        {
            _presets  = presets;
            _log      = log;

            // ── Section 1: Air Spawn ─────────────────────────────────────────
            EnableAirSpawn = cfg.Bind(
                Sec1, "Enable", false,
                new ConfigDescription(
                    "Master switch — when on, player aircraft spawn in the air.\n" +
                    "AI units and mission-placed aircraft are never affected.",
                    null,
                    new ConfigurationManagerAttributes { Order = 50 }));

            SpeedUnit = cfg.Bind(
                Sec1, "Speed Unit", AirSpawn.SpeedUnit.MetersPerSecond,
                new ConfigDescription(
                    "Unit used for all airspeed values in this config.\n" +
                    "Changing this automatically converts the stored speed values.\n" +
                    "Reference: 1 m/s = 1.944 kt = 3.6 km/h = 2.237 mph",
                    null,
                    new ConfigurationManagerAttributes { Order = 45 }));

            SpawnAltitude = cfg.Bind(
                Sec1, "Spawn Altitude", 1500f,
                new ConfigDescription(
                    "Height in metres above the airbase at which to spawn.\n" +
                    "Gear retracts and throttle initialises automatically at any altitude above ~2 m.",
                    new AcceptableValueRange<float>(100f, 15000f),
                    new ConfigurationManagerAttributes { Order = 40 }));

            SpawnAirspeed = cfg.Bind(
                Sec1, "Spawn Airspeed", 150f,
                new ConfigDescription(
                    BuildSpeedDesc(150f, AirSpawn.SpeedUnit.MetersPerSecond),
                    new AcceptableValueRange<float>(SpeedMin, SpeedMax),
                    new ConfigurationManagerAttributes { Order = 35 }));

            // ── Section 2: Per-Aircraft Presets ──────────────────────────────
            EnablePerAircraft = cfg.Bind(
                Sec2, "Enable Per-Aircraft Presets", false,
                new ConfigDescription(
                    "When on, each aircraft type can have its own spawn altitude and airspeed.\n" +
                    "Falls back to the global values for any aircraft without a saved preset.\n" +
                    "Close and re-open the config window to show or hide the controls below.",
                    null,
                    new ConfigurationManagerAttributes { Order = 50 }));

            SelectedAircraft = cfg.Bind(
                Sec2, "Selected Aircraft", NoAircraft,
                new ConfigDescription(
                    "Choose an aircraft to view or edit its preset.\n" +
                    "Aircraft appear here after entering a game (Encyclopedia loads on map load).\n" +
                    "Select an aircraft, adjust the values below, and they save automatically.",
                    _aircraftList,
                    _selAttr));

            PresetAltitude = cfg.Bind(
                Sec2, "Preset Altitude", 1500f,
                new ConfigDescription(
                    "Spawn altitude in metres for the selected aircraft. Saved automatically.",
                    new AcceptableValueRange<float>(100f, 15000f),
                    _preAltAttr));

            PresetAirspeed = cfg.Bind(
                Sec2, "Preset Airspeed", 150f,
                new ConfigDescription(
                    "Spawn airspeed (in the Speed Unit chosen above) for the selected aircraft.\n" +
                    "Saved automatically.",
                    new AcceptableValueRange<float>(SpeedMin, SpeedMax),
                    _preSpdAttr));

            // ── Wire events ──────────────────────────────────────────────────
            _prevUnit = SpeedUnit.Value;
            SetPresetBrowsable(EnablePerAircraft.Value);

            SpeedUnit.SettingChanged         += OnSpeedUnitChanged;
            EnablePerAircraft.SettingChanged += OnEnablePerAircraftChanged;
            SelectedAircraft.SettingChanged  += OnSelectedAircraftChanged;
            PresetAltitude.SettingChanged    += OnPresetValueChanged;
            PresetAirspeed.SettingChanged    += OnPresetValueChanged;

            AircraftRegistry.Updated += RefreshDropdown;
        }

        // ── Public helpers used by patches ────────────────────────────────────

        /// <summary>Spawn altitude in metres for <paramref name="jsonKey"/>.</summary>
        internal float GetAltitudeFor(string jsonKey)
        {
            if (!EnablePerAircraft.Value || !AircraftRegistry.Contains(jsonKey))
                return SpawnAltitude.Value;

            var (altM, _) = _presets.Get(jsonKey, SpawnAltitude.Value, GetGlobalAirspeedMs());
            return altM;
        }

        /// <summary>Spawn airspeed in m/s for <paramref name="jsonKey"/>.</summary>
        internal float GetAirspeedMsFor(string jsonKey)
        {
            if (!EnablePerAircraft.Value || !AircraftRegistry.Contains(jsonKey))
                return GetGlobalAirspeedMs();

            var (_, spdMs) = _presets.Get(jsonKey, SpawnAltitude.Value, GetGlobalAirspeedMs());
            return spdMs;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnSpeedUnitChanged(object sender, EventArgs e)
        {
            var newUnit = SpeedUnit.Value;
            if (newUnit == _prevUnit) return;

            // Convert the global airspeed entry value to the new unit
            float ms = SpeedConverter.ToMs(SpawnAirspeed.Value, _prevUnit);
            SpawnAirspeed.Value = Mathf.Round(SpeedConverter.FromMs(ms, newUnit));

            // Convert the displayed preset airspeed (the stored m/s value is unchanged)
            if (EnablePerAircraft.Value && SelectedAircraft.Value != NoAircraft)
            {
                float presetMs = SpeedConverter.ToMs(PresetAirspeed.Value, _prevUnit);
                _loadingPreset = true;
                PresetAirspeed.Value = Mathf.Round(SpeedConverter.FromMs(presetMs, newUnit));
                _loadingPreset = false;
                // No need to re-save: the preset file stores m/s and hasn't changed.
            }

            _prevUnit = newUnit;

            _log.LogInfo(
                $"[AirSpawn] Speed unit → {SpeedConverter.Abbreviation(newUnit)}  " +
                $"(global airspeed = {SpawnAirspeed.Value:F0} {SpeedConverter.Abbreviation(newUnit)})");
        }

        private void OnEnablePerAircraftChanged(object sender, EventArgs e)
        {
            SetPresetBrowsable(EnablePerAircraft.Value);
        }

        private void OnSelectedAircraftChanged(object sender, EventArgs e)
        {
            LoadPresetForSelected();
        }

        private void OnPresetValueChanged(object sender, EventArgs e)
        {
            if (_loadingPreset) return;
            SaveCurrentPreset();
        }

        // ── Preset load / save ────────────────────────────────────────────────

        private void LoadPresetForSelected()
        {
            _loadingPreset = true;
            try
            {
                var key = SelectedAircraft.Value;
                if (key == NoAircraft || !AircraftRegistry.Contains(key))
                {
                    PresetAltitude.Value = SpawnAltitude.Value;
                    PresetAirspeed.Value = SpawnAirspeed.Value;
                    return;
                }

                var (altM, spdMs) = _presets.Get(
                    key,
                    SpawnAltitude.Value,
                    GetGlobalAirspeedMs());

                PresetAltitude.Value = altM;
                PresetAirspeed.Value = Mathf.Round(
                    SpeedConverter.FromMs(spdMs, SpeedUnit.Value));
            }
            finally
            {
                _loadingPreset = false;
            }
        }

        private void SaveCurrentPreset()
        {
            var key = SelectedAircraft.Value;
            if (key == NoAircraft) return;

            _presets.Set(
                key,
                PresetAltitude.Value,
                SpeedConverter.ToMs(PresetAirspeed.Value, SpeedUnit.Value));
        }

        // ── Dropdown refresh ──────────────────────────────────────────────────

        private void RefreshDropdown()
        {
            if (AvlBackingField == null)
            {
                _log.LogWarning(
                    "[AirSpawn] Could not update aircraft dropdown: " +
                    "AcceptableValueList backing field not found. " +
                    "Type the aircraft jsonKey manually into Selected Aircraft.");
                return;
            }

            var values = new[] { NoAircraft }
                .Concat(AircraftRegistry.SortedKeys())
                .ToArray();

            AvlBackingField.SetValue(_aircraftList, values);

            // If the previously selected key is no longer in the list, reset.
            if (!values.Contains(SelectedAircraft.Value))
                SelectedAircraft.Value = NoAircraft;

            _log.LogDebug(
                $"[AirSpawn] Aircraft dropdown refreshed — {values.Length - 1} entries.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private float GetGlobalAirspeedMs() =>
            SpeedConverter.ToMs(SpawnAirspeed.Value, SpeedUnit.Value);

        private void SetPresetBrowsable(bool visible)
        {
            _selAttr.Browsable    = visible;
            _preAltAttr.Browsable = visible;
            _preSpdAttr.Browsable = visible;
        }

        private static string BuildSpeedDesc(float defaultMs, SpeedUnit unit)
        {
            float display = SpeedConverter.FromMs(defaultMs, unit);
            string abbr   = SpeedConverter.Abbreviation(unit);
            return
                $"Forward airspeed at spawn, in {abbr}.\n" +
                $"Default {display:F0} {abbr} ≈ 150 m/s ≈ 291 kt ≈ 540 km/h ≈ 336 mph.\n" +
                $"Change 'Speed Unit' above to enter the value in a different unit.";
        }
    }
}

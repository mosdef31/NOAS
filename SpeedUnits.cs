namespace AirSpawn
{
    public enum SpeedUnit
    {
        MetersPerSecond,
        KilometersPerHour,
        MilesPerHour,
        Knots,
    }

    internal static class SpeedConverter
    {
        internal static float ToMs(float value, SpeedUnit unit) => unit switch
        {
            SpeedUnit.KilometersPerHour => value / 3.6f,
            SpeedUnit.MilesPerHour      => value / 2.23694f,
            SpeedUnit.Knots             => value / 1.94384f,
            _                           => value,
        };

        internal static float FromMs(float ms, SpeedUnit unit) => unit switch
        {
            SpeedUnit.KilometersPerHour => ms * 3.6f,
            SpeedUnit.MilesPerHour      => ms * 2.23694f,
            SpeedUnit.Knots             => ms * 1.94384f,
            _                           => ms,
        };

        internal static string Abbreviation(SpeedUnit unit) => unit switch
        {
            SpeedUnit.KilometersPerHour => "km/h",
            SpeedUnit.MilesPerHour      => "mph",
            SpeedUnit.Knots             => "kt",
            _                           => "m/s",
        };
    }
}

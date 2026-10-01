namespace VikingOarsmen
{
    /// <summary>
    /// A rower's own propulsion setting. Reuses the same names and order as Ship.Speed (Stop, Back,
    /// Slow, Half, Full) so the stepwise W/S behavior matches the helm exactly (see
    /// VikingOarsmen-Documents/Plano-Marchas-e-Remo.md). Stop is neutral, Back is reverse.
    /// </summary>
    internal enum RowingGear
    {
        Stop,
        Back,
        Slow,
        Half,
        Full,
    }
}

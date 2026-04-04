namespace FartSymphony
{
    /// <summary>
    /// Immutable runtime representation of a single beat-map accent.
    /// Converted from JSON on load; all fields are read-only after construction.
    /// </summary>
    public readonly struct AccentData
    {
        /// <summary>Time of this accent from the start of the track, in milliseconds.</summary>
        public readonly float TimeMs;

        /// <summary>Intensity label (e.g. "forte", "pianissimo"). Informational; drives visual weight.</summary>
        public readonly string Intensity;

        /// <summary>Total judgment window width centred on TimeMs, in milliseconds.</summary>
        public readonly float WindowMs;

        /// <summary>Accent type label (e.g. "beat", "downbeat"). Informational.</summary>
        public readonly string Type;

        /// <summary>Half of WindowMs — distance from centre to edge of the judgment window.</summary>
        public float HalfWindow => WindowMs * 0.5f;

        public AccentData(float timeMs, string intensity, float windowMs, string type)
        {
            TimeMs    = timeMs;
            Intensity = intensity;
            WindowMs  = windowMs;
            Type      = type;
        }
    }
}

namespace FartSymphony
{
    /// <summary>
    /// Immutable runtime representation of a beat-map quiet zone.
    /// Inside a quiet zone the SuspicionMeter applies a danger multiplier to any Miss.
    /// </summary>
    public readonly struct QuietZoneData
    {
        public readonly float  StartMs;
        public readonly float  EndMs;

        /// <summary>Danger label (e.g. "extreme", "high"). Mapped to multiplier by SuspicionMeter.</summary>
        public readonly string DangerLevel;

        public QuietZoneData(float startMs, float endMs, string dangerLevel)
        {
            StartMs     = startMs;
            EndMs       = endMs;
            DangerLevel = dangerLevel;
        }
    }
}

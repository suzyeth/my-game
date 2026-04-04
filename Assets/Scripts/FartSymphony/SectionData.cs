namespace FartSymphony
{
    /// <summary>
    /// Immutable runtime representation of a beat-map section (e.g. "Exposition", "Development").
    /// Sections cover the full track with no gaps; boundary ownership is [StartMs, EndMs).
    /// </summary>
    public readonly struct SectionData
    {
        public readonly string Name;
        public readonly float  StartMs;
        public readonly float  EndMs;

        /// <summary>Dynamic level string (e.g. "fortissimo", "piano"). Drives suspicion multipliers.</summary>
        public readonly string DynamicLevel;

        public SectionData(string name, float startMs, float endMs, string dynamicLevel)
        {
            Name         = name;
            StartMs      = startMs;
            EndMs        = endMs;
            DynamicLevel = dynamicLevel;
        }
    }
}

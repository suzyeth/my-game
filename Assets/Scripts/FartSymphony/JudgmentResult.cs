namespace FartSymphony
{
    /// <summary>Outcome tier of a single timing judgment.</summary>
    public enum JudgmentTier { Perfect, Good, Miss }

    /// <summary>
    /// Immutable event data broadcast by TimingJudgment after every judgment.
    /// Zero GC allocation — struct passed by value through event Action&lt;JudgmentResult&gt;.
    /// </summary>
    public readonly struct JudgmentResult
    {
        /// <summary>Outcome tier: Perfect | Good | Miss.</summary>
        public readonly JudgmentTier Tier;

        /// <summary>
        /// Signed distance from accent centre in milliseconds.
        /// Negative = pressed early, positive = pressed late.
        /// </summary>
        public readonly float DeltaMs;

        /// <summary>Absolute value of DeltaMs. Used for score interpolation.</summary>
        public readonly float AbsDeltaMs;

        /// <summary>
        /// The accent that was judged, or null if the press matched no accent window.
        /// </summary>
        public readonly AccentData? Accent;

        /// <summary>AdjustedTimestamp from the triggering InputEvent, in milliseconds.</summary>
        public readonly double Timestamp;

        /// <summary>True when the press landed outside every accent window.</summary>
        public readonly bool WasOutsideWindow;

        /// <summary>
        /// True when this Miss was generated automatically because the player did not press
        /// before an accent window expired. No bloat drain for auto-miss.
        /// </summary>
        public readonly bool IsAutoMiss;

        public JudgmentResult(
            JudgmentTier tier,
            float        deltaMs,
            float        absDeltaMs,
            AccentData?  accent,
            double       timestamp,
            bool         wasOutsideWindow,
            bool         isAutoMiss = false)
        {
            Tier             = tier;
            DeltaMs          = deltaMs;
            AbsDeltaMs       = absDeltaMs;
            Accent           = accent;
            Timestamp        = timestamp;
            WasOutsideWindow = wasOutsideWindow;
            IsAutoMiss       = isAutoMiss;
        }
    }
}

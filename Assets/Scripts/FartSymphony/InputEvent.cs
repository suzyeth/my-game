namespace FartSymphony
{
    /// <summary>
    /// Immutable event data emitted by InputSystem when the player presses the release key.
    /// All timestamps are in milliseconds.
    /// </summary>
    public readonly struct InputEvent
    {
        /// <summary>
        /// Audio-clock timestamp adjusted for configured input latency offset, in milliseconds.
        /// Formula: RawDspTimestamp - InputLatencyOffsetMs
        /// Used by TimingJudgment for all window calculations.
        /// </summary>
        public readonly double AdjustedTimestamp;

        /// <summary>
        /// Raw AudioSettings.dspTime at the moment of key press, converted to milliseconds.
        /// Preserved for debugging and calibration screens.
        /// </summary>
        public readonly double RawDspTimestamp;

        public InputEvent(double adjustedTimestamp, double rawDspTimestamp)
        {
            AdjustedTimestamp = adjustedTimestamp;
            RawDspTimestamp   = rawDspTimestamp;
        }
    }
}

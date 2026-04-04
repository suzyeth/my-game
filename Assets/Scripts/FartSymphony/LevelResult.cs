namespace FartSymphony
{
    /// <summary>
    /// Immutable snapshot of a completed (or aborted) level run.
    /// Produced by ScoreAndRating.FinalizeLevel() and consumed by
    /// LevelFlowManager → Results Screen / Game Over Screen.
    /// </summary>
    public readonly struct LevelResult
    {
        public readonly int    TotalScore;
        public readonly int    PerfectCount;
        public readonly int    GoodCount;
        public readonly int    MissCount;
        public readonly int    MaxCombo;
        public readonly float  PeakSuspicion;
        public readonly bool   HadOverflow;
        public readonly bool   Cleared;        // false = aborted (Overflow / Social Death)
        public readonly string Rating;         // "S", "A", "B", "C", "D"
        public readonly float  PerfectRatio;   // perfectCount / totalAccents

        public LevelResult(
            int    totalScore,
            int    perfectCount,
            int    goodCount,
            int    missCount,
            int    maxCombo,
            float  peakSuspicion,
            bool   hadOverflow,
            bool   cleared,
            string rating,
            float  perfectRatio)
        {
            TotalScore    = totalScore;
            PerfectCount  = perfectCount;
            GoodCount     = goodCount;
            MissCount     = missCount;
            MaxCombo      = maxCombo;
            PeakSuspicion = peakSuspicion;
            HadOverflow   = hadOverflow;
            Cleared       = cleared;
            Rating        = rating;
            PerfectRatio  = perfectRatio;
        }

        public override string ToString() =>
            $"[LevelResult] Rating={Rating}  Score={TotalScore}  " +
            $"P={PerfectCount}/G={GoodCount}/M={MissCount}  " +
            $"MaxCombo={MaxCombo}  PeakSuspicion={PeakSuspicion:F0}  " +
            $"Overflow={HadOverflow}  Cleared={Cleared}";
    }
}

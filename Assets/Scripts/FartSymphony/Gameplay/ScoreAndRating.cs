using System;
using UnityEngine;

namespace FartSymphony.Gameplay
{
    /// <summary>
    /// Accumulates per-judgment scores, tracks combo and stats,
    /// and produces a final LevelResult with S/A/B/C/D rating.
    ///
    /// Pure data system — no direct visuals or audio.
    /// Display is delegated to Results Screen and Game Over Screen.
    ///
    /// ADR-0002: C# events; subscribe in OnEnable, unsubscribe in OnDisable.
    /// </summary>
    public sealed class ScoreAndRating : MonoBehaviour
    {
        // ── Inspector knobs — rating thresholds ───────────────────────────────
        [Header("Rating Thresholds (perfectRatio)")]
        [Tooltip("Min perfect ratio for S rating (missCount must also be 0).")]
        [SerializeField] [Range(0.90f, 1.00f)] private float _sRatingThreshold = 0.95f;

        [Tooltip("Min perfect ratio for A rating (missCount ≤ 3).")]
        [SerializeField] [Range(0.70f, 0.90f)] private float _aRatingThreshold = 0.80f;

        [Tooltip("Min perfect ratio for B rating (missCount ≤ 8).")]
        [SerializeField] [Range(0.50f, 0.75f)] private float _bRatingThreshold = 0.60f;

        [Tooltip("Min perfect ratio for C rating (missCount ≤ 15).")]
        [SerializeField] [Range(0.30f, 0.55f)] private float _cRatingThreshold = 0.40f;

        [Header("Miss Limits per Rating")]
        [SerializeField] private int _aMissLimit = 3;
        [SerializeField] private int _bMissLimit = 8;
        [SerializeField] private int _cMissLimit = 15;

        // ── Dependencies ──────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private TimingJudgment _timingJudgment;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fired when FinalizeLevel() produces the LevelResult.</summary>
        public event Action<LevelResult> OnLevelFinalized;

        // ── Runtime state ─────────────────────────────────────────────────────
        private bool  _active;
        private float _peakSuspicion;
        private bool  _hadOverflow;
        private int   _totalAccents;   // set from BeatMapData at activation

        // Stats (mirrors TimingJudgment for direct access; kept in sync via events)
        public int TotalScore   { get; private set; }
        public int PerfectCount { get; private set; }
        public int GoodCount    { get; private set; }
        public int MissCount    { get; private set; }
        public int CurrentCombo { get; private set; }
        public int MaxCombo     { get; private set; }

        // ── Dependency injection ───────────────────────────────────────────────
        public void SetDependencies(TimingJudgment tj)
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment -= HandleJudgment;
            _timingJudgment = tj;
            if (_timingJudgment != null) _timingJudgment.OnJudgment += HandleJudgment;
        }

        // ── ADR-0002 subscription pattern ─────────────────────────────────────
        private void OnEnable()
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment += HandleJudgment;
        }

        private void OnDisable()
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment -= HandleJudgment;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Begin scoring. totalAccents is used to compute perfectRatio.</summary>
        public void Activate(int totalAccents)
        {
            _totalAccents = totalAccents;
            _active       = true;
            _hadOverflow  = false;
            _peakSuspicion = 0f;

            TotalScore = PerfectCount = GoodCount = MissCount = 0;
            CurrentCombo = MaxCombo = 0;
        }

        public void Deactivate() => _active = false;
        public void Pause()      => _active = false;
        public void Resume()     => _active = true;

        /// <summary>Notify that a BloatGauge overflow occurred (recorded in LevelResult).</summary>
        public void RecordOverflow() => _hadOverflow = true;

        /// <summary>Update peak suspicion each frame (called by LevelFlowManager).</summary>
        public void UpdatePeakSuspicion(float currentSuspicion)
        {
            if (currentSuspicion > _peakSuspicion)
                _peakSuspicion = currentSuspicion;
        }

        /// <summary>
        /// Compute and broadcast the final LevelResult.
        /// Safe to call even if the level was aborted.
        /// </summary>
        public LevelResult FinalizeLevel(bool cleared)
        {
            float perfectRatio = _totalAccents > 0
                ? (float)PerfectCount / _totalAccents
                : 0f;

            string rating = ComputeRating(perfectRatio, MissCount, cleared);

            var result = new LevelResult(
                totalScore:    TotalScore,
                perfectCount:  PerfectCount,
                goodCount:     GoodCount,
                missCount:     MissCount,
                maxCombo:      MaxCombo,
                peakSuspicion: _peakSuspicion,
                hadOverflow:   _hadOverflow,
                cleared:       cleared,
                rating:        rating,
                perfectRatio:  perfectRatio);

            Debug.Log(result.ToString());
            OnLevelFinalized?.Invoke(result);
            return result;
        }

        // ── Judgment handler ──────────────────────────────────────────────────

        private void HandleJudgment(JudgmentResult result)
        {
            if (!_active) return;

            TotalScore += result.Score;

            switch (result.Tier)
            {
                case JudgmentTier.Perfect:
                    PerfectCount++;
                    CurrentCombo++;
                    break;
                case JudgmentTier.Good:
                    GoodCount++;
                    CurrentCombo++;
                    break;
                case JudgmentTier.Miss:
                    MissCount++;
                    CurrentCombo = 0;
                    break;
            }

            if (CurrentCombo > MaxCombo) MaxCombo = CurrentCombo;
        }

        // ── Rating algorithm ──────────────────────────────────────────────────

        private string ComputeRating(float perfectRatio, int missCount, bool cleared)
        {
            if (!cleared) return "D";

            if (perfectRatio >= _sRatingThreshold && missCount == 0)          return "S";
            if (perfectRatio >= _aRatingThreshold && missCount <= _aMissLimit) return "A";
            if (perfectRatio >= _bRatingThreshold && missCount <= _bMissLimit) return "B";
            if (perfectRatio >= _cRatingThreshold && missCount <= _cMissLimit) return "C";
            return "D";
        }
    }
}

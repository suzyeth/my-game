using System;
using System.Collections.Generic;
using UnityEngine;
using FartSymphony.Core;

namespace FartSymphony.Gameplay
{
    /// <summary>
    /// Core timing judgment system.
    ///
    /// Responsibilities:
    ///   1. Subscribe to InputSystem.OnReleasePressed.
    ///   2. Query BeatMapData for the nearest unconsumed accent within its window.
    ///   3. Compute tier (Perfect / Good / Miss) using proportional sub-windows.
    ///   4. Broadcast JudgmentResult via OnJudgment.
    ///   5. Poll for missed accents every MissedAccentCheckIntervalMs and emit
    ///      auto-Miss results for any expired, unconsumed accent.
    ///
    /// ADR-0002: C# event; subscribers wire up in OnEnable / OnDisable.
    /// </summary>
    public sealed class TimingJudgment : MonoBehaviour
    {
        // ── Inspector knobs ───────────────────────────────────────────────────
        [Header("Timing Windows (proportional to accent windowMs)")]
        [Tooltip("Inner window ratio — accents within this fraction get Perfect.")]
        [SerializeField] [Range(0.10f, 0.50f)] private float _perfectRatio = 0.30f;

        [Tooltip("Outer window ratio — accents within this fraction get Good (beyond Perfect).")]
        [SerializeField] [Range(0.50f, 0.90f)] private float _goodRatio    = 0.70f;

        [Header("Score Values")]
        [SerializeField] private int _perfectScoreMax = 1000;
        [SerializeField] private int _perfectScoreMin =  800;
        [SerializeField] private int _goodScoreMax    =  600;
        [SerializeField] private int _goodScoreMin    =  300;
        [SerializeField] private int _missScoreMax    =  100;
        [SerializeField] private int _missScoreMin    =    0;

        [Header("Missed Accent Detection")]
        [Tooltip("How often (ms) to poll for accents whose window has expired.")]
        [SerializeField] [Range(16f, 100f)] private float _missedCheckIntervalMs = 50f;

        [Header("Audio Calibration")]
        [Tooltip("MVP: 0. Vertical Slice: set by AudioCalibration via Settings. " +
                 "Subtracted from InputEvent.AdjustedTimestamp before window math.")]
        [SerializeField] private float _audioOffsetMs = 0f;

        // ── Dependencies (set via Inspector or SetDependencies) ───────────────
        [Header("Dependencies")]
        [SerializeField] private InputSystem _inputSystem;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Broadcast after every judgment (player press or auto-miss).</summary>
        public event Action<JudgmentResult> OnJudgment;

        // ── Runtime state ─────────────────────────────────────────────────────
        private BeatMapData    _beatMap;
        private HashSet<int>   _consumed = new HashSet<int>();
        private bool           _active;
        private double         _lastMissedCheckMs;
        // Track-relative offset: subtract from dspTime to get beat-map time.
        // Set by Activate(); updated when Audio Manager syncs the music start.
        private double         _trackStartMs;

        // ── Stats (read by Score & Rating) ────────────────────────────────────
        public int PerfectCount  { get; private set; }
        public int GoodCount     { get; private set; }
        public int MissCount     { get; private set; }
        public int TotalScore    { get; private set; }
        public int CurrentCombo  { get; private set; }
        public int MaxCombo      { get; private set; }

        // ── Dependency injection ───────────────────────────────────────────────
        public void SetDependencies(InputSystem inputSystem)
        {
            if (_inputSystem != null) _inputSystem.OnReleasePressed -= HandleReleasePressed;
            _inputSystem = inputSystem;
            if (_inputSystem != null) _inputSystem.OnReleasePressed += HandleReleasePressed;
        }

        // ── ADR-0002 subscription pattern ─────────────────────────────────────
        private void OnEnable()
        {
            if (_inputSystem != null)
                _inputSystem.OnReleasePressed += HandleReleasePressed;
        }

        private void OnDisable()
        {
            if (_inputSystem != null)
                _inputSystem.OnReleasePressed -= HandleReleasePressed;
        }

        // ── Unity update ──────────────────────────────────────────────────────
        private void Update()
        {
            if (!_active || _beatMap == null) return;
            double trackMs = AudioSettings.dspTime * 1000.0 - _trackStartMs;
            PollMissedAccents(trackMs);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Begin judging against the supplied beat map.
        /// <paramref name="trackStartMs"/> is AudioSettings.dspTime * 1000 at the moment
        /// the music/clock starts — used to convert absolute dspTime to beat-map-relative time.
        /// </summary>
        public void Activate(BeatMapData beatMap, double trackStartMs = 0.0)
        {
            _beatMap      = beatMap;
            _trackStartMs = trackStartMs;
            _consumed.Clear();
            _active            = true;
            _lastMissedCheckMs = 0.0;

            PerfectCount = GoodCount = MissCount = 0;
            TotalScore   = CurrentCombo = MaxCombo = 0;
        }

        /// <summary>Stop judging. Preserves stats for the Results Screen.</summary>
        public void Deactivate() => _active = false;

        /// <summary>Freeze judgment during pause (consumed state preserved).</summary>
        public void Pause()  => _active = false;

        /// <summary>Resume after pause.</summary>
        public void Resume() => _active = true;

        /// <summary>MVP: 0. Set by AudioCalibration in Vertical Slice.</summary>
        public void SetAudioOffset(float offsetMs) => _audioOffsetMs = offsetMs;

        // ── Input handler ─────────────────────────────────────────────────────

        private void HandleReleasePressed(InputEvent inputEvent)
        {
            if (!_active || _beatMap == null) return;

            // Convert absolute dspTime to beat-map-relative track time.
            // Then apply audio calibration offset (MVP: _audioOffsetMs == 0).
            double adjustedPressTime = (inputEvent.AdjustedTimestamp - _trackStartMs) - _audioOffsetMs;

            var accent = _beatMap.GetNearestAccent(adjustedPressTime, _consumed, out int idx);

            if (accent == null)
            {
                // Press outside every window
                EmitOutsideWindowMiss(adjustedPressTime);
                return;
            }

            _consumed.Add(idx);

            float deltaMs    = (float)(adjustedPressTime - accent.Value.TimeMs);
            float absDeltaMs = Mathf.Abs(deltaMs);

            var (tier, score) = EvaluateTiming(absDeltaMs, accent.Value.HalfWindow);
            bool outsideWindow = absDeltaMs > accent.Value.HalfWindow;

            UpdateStats(tier, score);

            var result = new JudgmentResult(tier, deltaMs, absDeltaMs,
                                            accent, adjustedPressTime, outsideWindow,
                                            isAutoMiss: false, score: score);
            OnJudgment?.Invoke(result);

            Debug.Log($"[TimingJudgment] {tier}  Δ{deltaMs:+0.0;-0.0}ms  score={score}  " +
                      $"combo={CurrentCombo}  total={TotalScore}");
        }

        // ── Missed accent polling ─────────────────────────────────────────────

        private void PollMissedAccents(double currentDspMs)
        {
            if (currentDspMs - _lastMissedCheckMs < _missedCheckIntervalMs) return;
            _lastMissedCheckMs = currentDspMs;

            for (int i = 0; i < _beatMap.AccentCount; i++)
            {
                if (_consumed.Contains(i)) continue;

                AccentData? accentNullable = _beatMap.GetAccentAt(i);
                if (accentNullable == null) continue;

                AccentData accent = accentNullable.Value;
                float windowEnd = accent.TimeMs + accent.HalfWindow;

                if (windowEnd >= (float)currentDspMs) continue; // window not yet expired

                _consumed.Add(i);

                // Auto-miss: player did not press. No bloat drain (IsAutoMiss = true).
                UpdateStats(JudgmentTier.Miss, _missScoreMin);

                var result = new JudgmentResult(
                    JudgmentTier.Miss,
                    deltaMs:          accent.HalfWindow,   // treat as maximally late
                    absDeltaMs:       accent.HalfWindow,
                    accent:           accent,
                    timestamp:        currentDspMs,
                    wasOutsideWindow: false,
                    isAutoMiss:       true);

                OnJudgment?.Invoke(result);

                Debug.Log($"[TimingJudgment] AUTO Miss  accent={accent.TimeMs:F0}ms  " +
                          $"expired at {currentDspMs:F0}ms");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private (JudgmentTier tier, int score) EvaluateTiming(float absDeltaMs, float halfWindow)
        {
            float perfectMax = halfWindow * _perfectRatio;
            float goodMax    = halfWindow * _goodRatio;

            if (absDeltaMs <= perfectMax)
            {
                float t     = (perfectMax > 0f) ? absDeltaMs / perfectMax : 0f;
                int   score = Mathf.RoundToInt(Mathf.Lerp(_perfectScoreMax, _perfectScoreMin, t));
                return (JudgmentTier.Perfect, score);
            }

            if (absDeltaMs <= goodMax)
            {
                float t     = (absDeltaMs - perfectMax) / (goodMax - perfectMax);
                int   score = Mathf.RoundToInt(Mathf.Lerp(_goodScoreMax, _goodScoreMin, t));
                return (JudgmentTier.Good, score);
            }

            if (absDeltaMs <= halfWindow)
            {
                float t     = (absDeltaMs - goodMax) / (halfWindow - goodMax);
                int   score = Mathf.RoundToInt(Mathf.Lerp(_missScoreMax, _missScoreMin,
                                                           Mathf.Clamp01(t)));
                return (JudgmentTier.Miss, score);
            }

            // Beyond halfWindow — outside window
            return (JudgmentTier.Miss, _missScoreMin);
        }

        private void EmitOutsideWindowMiss(double timestamp)
        {
            UpdateStats(JudgmentTier.Miss, _missScoreMin);

            var result = new JudgmentResult(
                JudgmentTier.Miss,
                deltaMs:          0f,
                absDeltaMs:       float.MaxValue,
                accent:           null,
                timestamp:        timestamp,
                wasOutsideWindow: true);

            OnJudgment?.Invoke(result);
            Debug.Log($"[TimingJudgment] Miss (outside all windows)  total={TotalScore}");
        }

        private void UpdateStats(JudgmentTier tier, int score)
        {
            TotalScore += score;
            switch (tier)
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
    }
}

using System;
using UnityEngine;

namespace FartSymphony.Gameplay
{
    /// <summary>
    /// Tracks the player's "bloat" level — the core tension mechanic.
    ///
    /// Rules:
    ///   • Bloat fills at a constant rate every frame (simulates biological pressure).
    ///   • Each JudgmentResult drains bloat (Perfect drains most, Miss drains least).
    ///   • Auto-miss judgments (player did not press) produce NO drain.
    ///   • When bloat reaches MaxBloat an uncontrolled release occurs: OnOverflow fires.
    ///   • Bloat never drops below 0.
    ///   • While paused, bloat is frozen.
    ///
    /// ADR-0002: C# events; subscribe in OnEnable, unsubscribe in OnDisable.
    /// </summary>
    public sealed class BloatGauge : MonoBehaviour
    {
        // ── Inspector knobs ───────────────────────────────────────────────────
        [Header("Fill")]
        [Tooltip("Maximum bloat value. Overflow triggers at this threshold.")]
        [SerializeField] [Range(50f, 200f)] private float _maxBloat = 100f;

        [Tooltip("Bloat units added per second. " +
                 "Default 8.0 → empty-to-full in 12.5 s with no drains.")]
        [SerializeField] [Range(3f, 15f)]   private float _fillRate  = 8f;

        [Tooltip("Starting bloat value. 0 = no pressure at level start.")]
        [SerializeField] [Range(0f, 50f)]   private float _initialBloat = 0f;

        [Header("Drain per Judgment Tier")]
        [SerializeField] [Range(20f, 60f)]  private float _perfectDrain = 35f;
        [SerializeField] [Range(10f, 40f)]  private float _goodDrain    = 20f;
        [SerializeField] [Range(5f,  20f)]  private float _missDrain    = 10f;

        // ── Dependencies ──────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private TimingJudgment _timingJudgment;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>
        /// Fired exactly once when BloatValue reaches MaxBloat.
        /// Subscribers: LevelFlowManager, SuspicionMeter, AudienceReaction, AudioManager.
        /// </summary>
        public event Action OnOverflow;

        // ── Runtime state ─────────────────────────────────────────────────────
        private float _bloatValue;
        private bool  _active;
        private bool  _overflowFired; // guard: fire OnOverflow only once per activation

        public float BloatValue  => _bloatValue;
        public float MaxBloat    => _maxBloat;
        /// <summary>Normalised bloat in [0, 1]. Use for UI progress bars.</summary>
        public float BloatRatio  => _maxBloat > 0f ? _bloatValue / _maxBloat : 0f;

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
            if (_timingJudgment != null)
                _timingJudgment.OnJudgment += HandleJudgment;
        }

        private void OnDisable()
        {
            if (_timingJudgment != null)
                _timingJudgment.OnJudgment -= HandleJudgment;
        }

        // ── Unity update ──────────────────────────────────────────────────────
        private void Update()
        {
            if (!_active || _overflowFired) return;

            _bloatValue = Mathf.Min(_maxBloat, _bloatValue + _fillRate * Time.deltaTime);

            if (_bloatValue >= _maxBloat)
                TriggerOverflow();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Begin filling. Called by LevelFlowManager when the level starts.</summary>
        public void Activate()
        {
            _bloatValue    = _initialBloat;
            _active        = true;
            _overflowFired = false;
        }

        /// <summary>Stop filling and draining (level ended).</summary>
        public void Deactivate() => _active = false;

        /// <summary>Freeze bloat during pause.</summary>
        public void Pause()  => _active = false;

        /// <summary>Resume after pause.</summary>
        public void Resume() => _active = true;

        /// <summary>Hard-reset to initial state (e.g. restart).</summary>
        public void Reset()
        {
            _bloatValue    = _initialBloat;
            _overflowFired = false;
        }

        // ── Judgment handler ──────────────────────────────────────────────────

        private void HandleJudgment(JudgmentResult result)
        {
            if (!_active || _overflowFired) return;

            // Auto-miss: player did not press (no fart released) → no drain
            if (result.IsAutoMiss) return;

            float drain = result.Tier switch
            {
                JudgmentTier.Perfect => _perfectDrain,
                JudgmentTier.Good    => _goodDrain,
                JudgmentTier.Miss    => _missDrain,
                _                    => 0f
            };

            _bloatValue = Mathf.Max(0f, _bloatValue - drain);

            Debug.Log($"[BloatGauge] {result.Tier}  drain={drain:F1}  " +
                      $"bloat={_bloatValue:F1}/{_maxBloat:F0}  ({BloatRatio * 100f:F0}%)");
        }

        // ── Overflow ──────────────────────────────────────────────────────────

        private void TriggerOverflow()
        {
            _bloatValue    = _maxBloat; // clamp
            _overflowFired = true;
            _active        = false;

            Debug.Log("[BloatGauge] OVERFLOW — uncontrolled release!");
            OnOverflow?.Invoke();
        }
    }
}

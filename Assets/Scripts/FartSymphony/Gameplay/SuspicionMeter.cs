using System;
using UnityEngine;
using FartSymphony.Core;

namespace FartSymphony.Gameplay
{
    /// <summary>
    /// Social survival system.
    ///
    /// Tracks audience suspicion on a 0–MaxSuspicion scale:
    ///   • Grows on each judgment (Perfect: 0, Good: +5, Miss: +20).
    ///   • Quiet-zone judgments apply a multiplier (default 1.5×).
    ///   • Decays continuously at SuspicionDecayRate units/second.
    ///   • OnOverflow (BloatGauge) immediately maximises suspicion.
    ///   • When suspicion reaches MaxSuspicion → OnSocialDeath fires (once).
    ///   • Frozen while paused.
    ///
    /// ADR-0002: C# events; subscribe in OnEnable, unsubscribe in OnDisable.
    /// </summary>
    public sealed class SuspicionMeter : MonoBehaviour
    {
        // ── Inspector knobs ───────────────────────────────────────────────────
        [Header("Thresholds")]
        [SerializeField] [Range(50f,  200f)] private float _maxSuspicion        = 100f;

        [Header("Judgment Gains")]
        [SerializeField] [Range(0f,   0f)]   private float _perfectGain         = 0f;
        [SerializeField] [Range(2f,  10f)]   private float _goodGain            = 5f;
        [SerializeField] [Range(10f, 40f)]   private float _missGain            = 20f;

        [Header("Quiet Zone")]
        [SerializeField] [Range(1f,   3f)]   private float _quietZoneMultiplier = 1.5f;

        [Header("Decay")]
        [SerializeField] [Range(0.5f, 5f)]   private float _decayRate           = 2f;

        // ── Dependencies ──────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private TimingJudgment _timingJudgment;
        [SerializeField] private BloatGauge     _bloatGauge;
        [SerializeField] private BeatMapLoader  _beatMapLoader;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fires once when suspicionValue reaches MaxSuspicion.</summary>
        public event Action OnSocialDeath;

        // ── Runtime state ─────────────────────────────────────────────────────
        private float _suspicionValue;
        private bool  _active;
        private bool  _socialDeathFired;

        public float SuspicionValue     => _suspicionValue;
        public float MaxSuspicion       => _maxSuspicion;
        /// <summary>Normalised suspicion in [0, 1]. Use for UI and atmosphere.</summary>
        public float GetSuspicionRatio() => _maxSuspicion > 0f ? _suspicionValue / _maxSuspicion : 0f;

        // ── Dependency injection ───────────────────────────────────────────────
        public void SetDependencies(TimingJudgment tj, BloatGauge bloatGauge, BeatMapLoader bml)
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment -= HandleJudgment;
            if (_bloatGauge     != null) _bloatGauge.OnOverflow      -= HandleOverflow;

            _timingJudgment = tj;
            _bloatGauge     = bloatGauge;
            _beatMapLoader  = bml;

            if (_timingJudgment != null) _timingJudgment.OnJudgment += HandleJudgment;
            if (_bloatGauge     != null) _bloatGauge.OnOverflow      += HandleOverflow;
        }

        // ── ADR-0002 subscription pattern ─────────────────────────────────────
        private void OnEnable()
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment += HandleJudgment;
            if (_bloatGauge     != null) _bloatGauge.OnOverflow      += HandleOverflow;
        }

        private void OnDisable()
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment -= HandleJudgment;
            if (_bloatGauge     != null) _bloatGauge.OnOverflow      -= HandleOverflow;
        }

        // ── Unity update ──────────────────────────────────────────────────────
        private void Update()
        {
            if (!_active || _socialDeathFired) return;

            _suspicionValue = Mathf.Max(0f, _suspicionValue - _decayRate * Time.deltaTime);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Begin tracking. Called by LevelFlowManager at level start.</summary>
        public void Activate()
        {
            _suspicionValue   = 0f;
            _active           = true;
            _socialDeathFired = false;
        }

        public void Deactivate() => _active = false;
        public void Pause()      => _active = false;
        public void Resume()     => _active = true;

        // ── Judgment handler ──────────────────────────────────────────────────

        private void HandleJudgment(JudgmentResult result)
        {
            if (!_active || _socialDeathFired) return;

            float baseGain = result.Tier switch
            {
                JudgmentTier.Perfect => _perfectGain,
                JudgmentTier.Good    => _goodGain,
                JudgmentTier.Miss    => _missGain,
                _                    => 0f
            };

            if (baseGain <= 0f) return;

            bool inQuiet = _beatMapLoader != null && _beatMapLoader.Data != null &&
                           result.Accent.HasValue &&
                           _beatMapLoader.Data.IsInQuietZone(result.Accent.Value.TimeMs);

            float multiplier = inQuiet ? _quietZoneMultiplier : 1f;
            _suspicionValue  = Mathf.Min(_maxSuspicion, _suspicionValue + baseGain * multiplier);

            Debug.Log($"[SuspicionMeter] {result.Tier}  +{baseGain * multiplier:F1}  " +
                      $"suspicion={_suspicionValue:F1}/{_maxSuspicion:F0}  " +
                      $"({GetSuspicionRatio() * 100f:F0}%)  quietZone={inQuiet}");

            if (_suspicionValue >= _maxSuspicion)
                TriggerSocialDeath();
        }

        private void HandleOverflow()
        {
            if (!_active || _socialDeathFired) return;

            _suspicionValue = _maxSuspicion;
            Debug.Log("[SuspicionMeter] OnOverflow → suspicion maxed instantly");
            TriggerSocialDeath();
        }

        private void TriggerSocialDeath()
        {
            _socialDeathFired = true;
            _active           = false;
            Debug.Log("[SuspicionMeter] SOCIAL DEATH — audience fully suspicious!");
            OnSocialDeath?.Invoke();
        }
    }
}

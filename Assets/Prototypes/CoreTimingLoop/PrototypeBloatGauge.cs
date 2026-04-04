// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the core timing loop feel fun, tense, and satisfying?
// Date: 2026-03-28

using UnityEngine;
using System;

namespace Prototype.CoreTimingLoop
{
    public class PrototypeBloatGauge : MonoBehaviour
    {
        [Header("Bloat Settings")]
        public float MaxBloat = 100f;
        public float BloatFillRate = 8f;
        public float InitialBloat = 0f;

        [Header("Drain Amounts")]
        public float PerfectDrain = 35f;
        public float GoodDrain = 20f;
        public float MissDrain = 10f;

        public float CurrentBloat { get; private set; }
        public float NormalizedBloat => CurrentBloat / MaxBloat;
        public bool HasOverflowed { get; private set; }

        public event Action OnOverflow;

        private bool _active;

        public void Activate()
        {
            CurrentBloat = InitialBloat;
            HasOverflowed = false;
            _active = true;
        }

        public void Deactivate() => _active = false;

        public void ResumeAfterPause()
        {
            if (!HasOverflowed) _active = true;
        }

        public void Tick(float deltaTime)
        {
            if (!_active || HasOverflowed) return;

            CurrentBloat = Mathf.Min(MaxBloat, CurrentBloat + BloatFillRate * deltaTime);

            if (CurrentBloat >= MaxBloat)
            {
                HasOverflowed = true;
                _active = false;
                Debug.Log("[Prototype] OVERFLOW! Game Over.");
                OnOverflow?.Invoke();
            }
        }

        public void OnJudgmentReceived(JudgmentResult result)
        {
            if (!_active) return;

            // No press = no fart = no drain
            if (result.IsAutoMiss) return;

            float drain = result.Tier switch
            {
                JudgmentTier.Perfect => PerfectDrain,
                JudgmentTier.Good => GoodDrain,
                JudgmentTier.Miss => MissDrain,
                _ => 0f
            };

            CurrentBloat = Mathf.Max(0f, CurrentBloat - drain);
        }
    }
}

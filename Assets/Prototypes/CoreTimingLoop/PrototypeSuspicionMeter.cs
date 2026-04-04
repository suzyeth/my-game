// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the core timing loop feel fun, tense, and satisfying?
// Date: 2026-03-28

using UnityEngine;
using System;

namespace Prototype.CoreTimingLoop
{
    public class PrototypeSuspicionMeter : MonoBehaviour
    {
        [Header("Suspicion Settings")]
        public float MaxSuspicion = 100f;
        public float SuspicionDecayRate = 4f;
        public float QuietZoneMultiplier = 1.2f;

        [Header("Gain Amounts")]
        public float PerfectGain = 0f;
        public float GoodGain = 3f;
        public float MissGain = 10f;

        public float CurrentSuspicion { get; private set; }
        public float NormalizedSuspicion => CurrentSuspicion / MaxSuspicion;
        public float PeakSuspicion { get; private set; }
        public bool IsSocialDeath { get; private set; }

        public event Action OnSocialDeath;

        private BeatMapData _beatMap;
        private bool _active;

        public void Activate(BeatMapData beatMap)
        {
            _beatMap = beatMap;
            CurrentSuspicion = 0f;
            PeakSuspicion = 0f;
            IsSocialDeath = false;
            _active = true;
        }

        public void Deactivate() => _active = false;

        public void ResumeAfterPause()
        {
            if (!IsSocialDeath) _active = true;
        }

        public void Tick(float deltaTime)
        {
            if (!_active || IsSocialDeath) return;
            CurrentSuspicion = Mathf.Max(0f, CurrentSuspicion - SuspicionDecayRate * deltaTime);
        }

        public void OnJudgmentReceived(JudgmentResult result)
        {
            if (!_active || IsSocialDeath) return;

            float baseGain = result.Tier switch
            {
                JudgmentTier.Perfect => PerfectGain,
                JudgmentTier.Good => GoodGain,
                JudgmentTier.Miss => MissGain,
                _ => 0f
            };

            float multiplier = 1f;
            if (_beatMap != null && result.Accent != null)
            {
                multiplier = _beatMap.IsInQuietZone(result.Accent.timeMs)
                    ? QuietZoneMultiplier : 1f;
            }

            CurrentSuspicion = Mathf.Min(MaxSuspicion, CurrentSuspicion + baseGain * multiplier);
            if (CurrentSuspicion > PeakSuspicion) PeakSuspicion = CurrentSuspicion;

            if (CurrentSuspicion >= MaxSuspicion)
            {
                IsSocialDeath = true;
                _active = false;
                Debug.Log("[Prototype] SOCIAL DEATH! Game Over.");
                OnSocialDeath?.Invoke();
            }
        }

        public void HandleOverflow()
        {
            if (IsSocialDeath) return;
            CurrentSuspicion = MaxSuspicion;
            PeakSuspicion = MaxSuspicion;
            IsSocialDeath = true;
            _active = false;
            // Do NOT fire OnSocialDeath — the caller (GameLoop) handles the game-over reason
            Debug.Log("[Prototype] Overflow -> Suspicion maxed (handled by LFM as Overflow).");
        }
    }
}

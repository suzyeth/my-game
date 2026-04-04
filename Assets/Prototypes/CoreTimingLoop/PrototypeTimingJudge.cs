// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the core timing loop feel fun, tense, and satisfying?
// Date: 2026-03-28

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Prototype.CoreTimingLoop
{
    public enum JudgmentTier { Perfect, Good, Miss }

    public struct JudgmentResult
    {
        public JudgmentTier Tier;
        public float DeltaMs;
        public float AbsDeltaMs;
        public AccentJson Accent;
        public int Score;
        public bool WasOutsideWindow;
        public bool IsHoldStart;  // true = press judgment for hold note
        public bool IsHoldEnd;    // true = release judgment for hold note
        public bool IsAutoMiss;   // true = player didn't press (no fart released)
    }

    public class PrototypeTimingJudge : MonoBehaviour
    {
        [Header("Timing Ratios")]
        public float PerfectRatio = 0.30f;
        public float GoodRatio = 0.70f;

        [Header("Score")]
        public int PerfectScoreMax = 1000;
        public int PerfectScoreMin = 800;
        public int GoodScoreMax = 600;
        public int GoodScoreMin = 300;
        public int MissScoreMax = 100;
        public int MissScoreMin = 0;

        [Header("Missed Accent Check")]
        public float MissedCheckIntervalMs = 50f;

        public event Action<JudgmentResult> OnJudgment;

        private BeatMapData _beatMap;
        private HashSet<int> _consumed = new HashSet<int>();
        private float _lastMissedCheckTime;
        private bool _active;

        // Hold state
        private AccentJson _activeHold;
        private int _activeHoldPressScore;
        private JudgmentTier _activeHoldPressTier;
        public AccentJson ActiveHold => _activeHold;
        public bool IsHolding => _activeHold != null;

        // Stats
        public int PerfectCount { get; private set; }
        public int GoodCount { get; private set; }
        public int MissCount { get; private set; }
        public int TotalScore { get; private set; }
        public int MaxCombo { get; private set; }
        public int CurrentCombo { get; private set; }

        public void Activate(BeatMapData beatMap)
        {
            _beatMap = beatMap;
            _consumed.Clear();
            _active = true;
            _lastMissedCheckTime = 0;
            _activeHold = null;
            PerfectCount = GoodCount = MissCount = 0;
            TotalScore = 0;
            MaxCombo = CurrentCombo = 0;
        }

        public void Deactivate() => _active = false;

        public void ResumeAfterPause()
        {
            _active = true;
        }

        public void HandlePress(float dspTimeMs)
        {
            if (!_active || _beatMap == null) return;
            if (_activeHold != null) return; // Already holding — ignore until release

            var nearest = _beatMap.GetNearestAccent(dspTimeMs, _consumed);
            if (nearest == null)
            {
                EmitMiss(null, dspTimeMs, true);
                return;
            }

            float deltaMs = dspTimeMs - nearest.timeMs;
            float absDelta = Mathf.Abs(deltaMs);
            float halfWindow = nearest.windowMs / 2f;

            int idx = _beatMap.IndexOf(nearest);
            _consumed.Add(idx);

            var (tier, score, outside) = EvaluateTiming(absDelta, halfWindow);

            if (nearest.IsHold)
            {
                // Hold note: store press result, wait for release
                _activeHold = nearest;
                _activeHoldPressScore = score;
                _activeHoldPressTier = tier;

                OnJudgment?.Invoke(new JudgmentResult
                {
                    Tier = tier, DeltaMs = deltaMs, AbsDeltaMs = absDelta,
                    Accent = nearest, Score = score,
                    WasOutsideWindow = outside, IsHoldStart = true
                });
                // Don't update stats yet — wait for release
            }
            else
            {
                // Tap note: immediate full judgment
                UpdateStats(tier, score);
                OnJudgment?.Invoke(new JudgmentResult
                {
                    Tier = tier, DeltaMs = deltaMs, AbsDeltaMs = absDelta,
                    Accent = nearest, Score = score, WasOutsideWindow = outside
                });
            }
        }

        public void HandleRelease(float dspTimeMs)
        {
            if (!_active || _activeHold == null) return;

            float targetReleaseMs = _activeHold.ReleaseTimeMs;
            float deltaMs = dspTimeMs - targetReleaseMs;
            float absDelta = Mathf.Abs(deltaMs);
            float halfWindow = _activeHold.windowMs / 2f;

            var (releaseTier, releaseScore, outside) = EvaluateTiming(absDelta, halfWindow);

            // Combine press + release: average score, worst (highest enum) tier
            int combinedScore = (_activeHoldPressScore + releaseScore) / 2;
            JudgmentTier worstTier = (int)_activeHoldPressTier >= (int)releaseTier
                ? _activeHoldPressTier : releaseTier;

            UpdateStats(worstTier, combinedScore);

            OnJudgment?.Invoke(new JudgmentResult
            {
                Tier = worstTier, DeltaMs = deltaMs, AbsDeltaMs = absDelta,
                Accent = _activeHold, Score = combinedScore,
                WasOutsideWindow = outside, IsHoldEnd = true
            });

            _activeHold = null;
        }

        public void CheckHoldTimeout(float currentTimeMs)
        {
            if (_activeHold == null) return;
            float deadline = _activeHold.ReleaseTimeMs + _activeHold.windowMs / 2f;
            if (currentTimeMs > deadline)
            {
                // Held too long — force miss on release
                UpdateStats(JudgmentTier.Miss, MissScoreMin);
                OnJudgment?.Invoke(new JudgmentResult
                {
                    Tier = JudgmentTier.Miss, DeltaMs = _activeHold.windowMs,
                    AbsDeltaMs = _activeHold.windowMs, Accent = _activeHold,
                    Score = MissScoreMin, WasOutsideWindow = true, IsHoldEnd = true,
                    IsAutoMiss = true
                });
                _activeHold = null;
            }
        }

        private (JudgmentTier tier, int score, bool outside) EvaluateTiming(float absDelta, float halfWindow)
        {
            float perfectMax = halfWindow * PerfectRatio;
            float goodMax = halfWindow * GoodRatio;
            bool outside = absDelta > halfWindow;

            if (absDelta <= perfectMax)
            {
                float t = perfectMax > 0 ? absDelta / perfectMax : 0;
                return (JudgmentTier.Perfect, Mathf.RoundToInt(Mathf.Lerp(PerfectScoreMax, PerfectScoreMin, t)), outside);
            }
            if (absDelta <= goodMax)
            {
                float t = (absDelta - perfectMax) / (goodMax - perfectMax);
                return (JudgmentTier.Good, Mathf.RoundToInt(Mathf.Lerp(GoodScoreMax, GoodScoreMin, t)), outside);
            }
            if (absDelta <= halfWindow)
            {
                float t = (absDelta - goodMax) / (halfWindow - goodMax);
                return (JudgmentTier.Miss, Mathf.RoundToInt(Mathf.Lerp(MissScoreMax, MissScoreMin, Mathf.Clamp01(t))), outside);
            }
            return (JudgmentTier.Miss, MissScoreMin, true);
        }

        public void CheckMissedAccents(float currentTimeMs)
        {
            if (!_active || _beatMap == null) return;
            if (currentTimeMs - _lastMissedCheckTime < MissedCheckIntervalMs) return;
            _lastMissedCheckTime = currentTimeMs;

            for (int i = 0; i < _beatMap.AccentCount; i++)
            {
                if (_consumed.Contains(i)) continue;
                var accent = _beatMap.GetAccentAt(i);
                // For hold notes, the press window is still based on timeMs
                float windowEnd = accent.timeMs + accent.windowMs / 2f;
                if (windowEnd < currentTimeMs)
                {
                    _consumed.Add(i);
                    EmitMiss(accent, currentTimeMs, false, isAutoMiss: true);
                }
            }

            CheckHoldTimeout(currentTimeMs);
        }

        private void EmitMiss(AccentJson accent, float timeMs, bool outsideWindow, bool isAutoMiss = false)
        {
            UpdateStats(JudgmentTier.Miss, MissScoreMin);
            OnJudgment?.Invoke(new JudgmentResult
            {
                Tier = JudgmentTier.Miss,
                DeltaMs = accent != null ? accent.windowMs / 2f : 0,
                AbsDeltaMs = accent != null ? accent.windowMs / 2f : float.MaxValue,
                Accent = accent,
                Score = MissScoreMin,
                WasOutsideWindow = outsideWindow,
                IsAutoMiss = isAutoMiss
            });
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

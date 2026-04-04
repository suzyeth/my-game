// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the core timing loop feel fun, tense, and satisfying?
// Date: 2026-03-28
// Updated: 2026-04-01 — Refactored into Level Flow Manager state machine

using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype.CoreTimingLoop
{
    public enum LevelState
    {
        Loading,
        Countdown,
        Playing,
        Paused,
        GameOver,
        Cleared
    }

    public enum GameOverReason
    {
        None,
        Overflow,
        SocialDeath
    }

    public class PrototypeGameLoop : MonoBehaviour
    {
        [Header("References (auto-found if null)")]
        public PrototypeBeatMapLoader BeatMapLoader;
        public PrototypeTimingJudge TimingJudge;
        public PrototypeBloatGauge BloatGauge;
        public PrototypeSuspicionMeter SuspicionMeter;
        public PrototypeUI UI;

        [Header("Audio")]
        public AudioClip MusicClip;

        [Header("Level Flow")]
        public float CountdownDuration = 3f;
        public float EndDelay = 1.5f;

        public LevelState State { get; private set; } = LevelState.Loading;
        public GameOverReason OverReason { get; private set; } = GameOverReason.None;

        private AudioSource _audioSource;
        private double _trackStartDspTime;
        private float _countdownTimer;
        private float _endDelayTimer;

        private void Awake()
        {
            if (BeatMapLoader == null) BeatMapLoader = GetComponent<PrototypeBeatMapLoader>();
            if (TimingJudge == null) TimingJudge = GetComponent<PrototypeTimingJudge>();
            if (BloatGauge == null) BloatGauge = GetComponent<PrototypeBloatGauge>();
            if (SuspicionMeter == null) SuspicionMeter = GetComponent<PrototypeSuspicionMeter>();
            if (UI == null) UI = FindFirstObjectByType<PrototypeUI>();

            _audioSource = gameObject.AddComponent<AudioSource>();
        }

        private void Start()
        {
            LoadLevel();
        }

        private void LoadLevel()
        {
            State = LevelState.Loading;

            BeatMapLoader.Load();
            if (!BeatMapLoader.IsReady)
            {
                Debug.LogError($"[LFM] Cannot start: {BeatMapLoader.Error}");
                if (UI != null) UI.ShowError(BeatMapLoader.Error);
                return;
            }

            // Wire events
            TimingJudge.OnJudgment += BloatGauge.OnJudgmentReceived;
            TimingJudge.OnJudgment += SuspicionMeter.OnJudgmentReceived;
            TimingJudge.OnJudgment += OnJudgment;
            BloatGauge.OnOverflow += OnOverflow;
            SuspicionMeter.OnSocialDeath += OnSocialDeath;

            EnterCountdown();
        }

        // --- State: Countdown ---

        private void EnterCountdown()
        {
            State = LevelState.Countdown;
            _countdownTimer = CountdownDuration;
            Debug.Log($"[LFM] Countdown started ({CountdownDuration}s)");
        }

        private void UpdateCountdown()
        {
            _countdownTimer -= Time.deltaTime;

            if (UI != null)
            {
                int display = Mathf.CeilToInt(_countdownTimer);
                string text = display > 0 ? display.ToString() : "GO!";
                UI.ShowCountdown(text, _countdownTimer);
            }

            if (_countdownTimer <= 0f)
            {
                EnterPlaying();
            }
        }

        // --- State: Playing ---

        private void EnterPlaying()
        {
            State = LevelState.Playing;

            // Activate subsystems
            TimingJudge.Activate(BeatMapLoader.Data);
            BloatGauge.Activate();
            SuspicionMeter.Activate(BeatMapLoader.Data);

            // Start music
            if (MusicClip != null)
            {
                _audioSource.clip = MusicClip;
                _trackStartDspTime = AudioSettings.dspTime + 0.1;
                _audioSource.PlayScheduled(_trackStartDspTime);
            }
            else
            {
                _trackStartDspTime = AudioSettings.dspTime + 0.1;
                Debug.LogWarning("[LFM] No music clip assigned. Running with silent audio clock.");
            }

            if (UI != null) UI.HideCountdown();
            Debug.Log("[LFM] Playing! Press SPACE on the accent markers.");
        }

        private void UpdatePlaying()
        {
            float currentTimeMs = GetCurrentTimeMs();
            float deltaTime = Time.deltaTime;

            // Check track end
            if (currentTimeMs >= BeatMapLoader.Data.Raw.meta.durationMs)
            {
                EnterCleared();
                return;
            }

            // Tick subsystems
            BloatGauge.Tick(deltaTime);
            SuspicionMeter.Tick(deltaTime);
            TimingJudge.CheckMissedAccents(currentTimeMs);

            // Input — press
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                float inputTimeMs = GetCurrentTimeMs();
                TimingJudge.HandlePress(inputTimeMs);
            }
            // Input — release (for hold notes)
            if (Keyboard.current.spaceKey.wasReleasedThisFrame)
            {
                float inputTimeMs = GetCurrentTimeMs();
                TimingJudge.HandleRelease(inputTimeMs);
            }

            // Pause
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                EnterPaused();
                return;
            }

            // Update UI
            if (UI != null)
            {
                UI.UpdateState(
                    currentTimeMs,
                    BeatMapLoader.Data,
                    BloatGauge,
                    SuspicionMeter,
                    TimingJudge
                );
            }
        }

        // --- State: Paused ---

        private void EnterPaused()
        {
            State = LevelState.Paused;
            Time.timeScale = 0f;

            if (_audioSource.isPlaying) _audioSource.Pause();

            // Deactivate subsystems (freeze state)
            TimingJudge.Deactivate();
            BloatGauge.Deactivate();
            SuspicionMeter.Deactivate();

            if (UI != null) UI.ShowPause();
            Debug.Log("[LFM] Paused.");
        }

        public void Resume()
        {
            if (State != LevelState.Paused) return;

            State = LevelState.Playing;
            Time.timeScale = 1f;

            if (MusicClip != null) _audioSource.UnPause();

            // Reactivate subsystems (restore state, don't reset)
            TimingJudge.ResumeAfterPause();
            BloatGauge.ResumeAfterPause();
            SuspicionMeter.ResumeAfterPause();

            if (UI != null) UI.HidePause();
            Debug.Log("[LFM] Resumed.");
        }

        // --- State: GameOver ---

        private void EnterGameOver(GameOverReason reason)
        {
            if (State == LevelState.GameOver) return; // Only die once

            State = LevelState.GameOver;
            OverReason = reason;

            // Stop all subsystems
            TimingJudge.Deactivate();
            BloatGauge.Deactivate();
            SuspicionMeter.Deactivate();

            // Stop music
            _audioSource.Stop();

            string reasonText = reason switch
            {
                GameOverReason.Overflow => "OVERFLOW — 气胀溢出！",
                GameOverReason.SocialDeath => "SOCIAL DEATH — 社死！",
                _ => "GAME OVER"
            };

            if (UI != null) UI.ShowGameOver(reasonText, TimingJudge);
            Debug.Log($"[LFM] === GAME OVER: {reason} === Press R to restart, ESC for menu.");
        }

        // --- State: Cleared ---

        private void EnterCleared()
        {
            State = LevelState.Cleared;
            _endDelayTimer = EndDelay;

            // Stop subsystems
            TimingJudge.Deactivate();
            BloatGauge.Deactivate();
            SuspicionMeter.Deactivate();

            Debug.Log("[LFM] Track complete, entering end delay...");
        }

        private void UpdateCleared()
        {
            _endDelayTimer -= Time.deltaTime;
            if (_endDelayTimer <= 0f)
            {
                ShowResults();
            }
        }

        private void ShowResults()
        {
            int total = TimingJudge.PerfectCount + TimingJudge.GoodCount + TimingJudge.MissCount;
            float perfectRatio = total > 0 ? (float)TimingJudge.PerfectCount / total : 0;
            string rating = perfectRatio >= 0.95f ? "S" :
                            perfectRatio >= 0.80f ? "A" :
                            perfectRatio >= 0.60f ? "B" :
                            perfectRatio >= 0.40f ? "C" : "D";

            Debug.Log($"[LFM] === RESULTS ===");
            Debug.Log($"  Score: {TimingJudge.TotalScore} | Rating: {rating}");
            Debug.Log($"  Perfect: {TimingJudge.PerfectCount} | Good: {TimingJudge.GoodCount} | Miss: {TimingJudge.MissCount}");
            Debug.Log($"  Max Combo: {TimingJudge.MaxCombo} | Peak Suspicion: {SuspicionMeter.PeakSuspicion:F1}");

            if (UI != null) UI.ShowResults(rating, TimingJudge, SuspicionMeter);

            // Switch to GameOver-like state so R/ESC work
            State = LevelState.GameOver;
            OverReason = GameOverReason.None; // Cleared, not failed
        }

        private void OnDestroy()
        {
            if (TimingJudge != null)
            {
                TimingJudge.OnJudgment -= BloatGauge.OnJudgmentReceived;
                TimingJudge.OnJudgment -= SuspicionMeter.OnJudgmentReceived;
                TimingJudge.OnJudgment -= OnJudgment;
            }
            if (BloatGauge != null) BloatGauge.OnOverflow -= OnOverflow;
            if (SuspicionMeter != null) SuspicionMeter.OnSocialDeath -= OnSocialDeath;
        }

        // --- Main Update ---

        private void Update()
        {
            switch (State)
            {
                case LevelState.Countdown:
                    UpdateCountdown();
                    break;

                case LevelState.Playing:
                    UpdatePlaying();
                    break;

                case LevelState.Paused:
                    // Escape to resume, R to restart (using unscaledDeltaTime since timeScale=0)
                    if (Keyboard.current.escapeKey.wasPressedThisFrame)
                    {
                        Resume();
                    }
                    else if (Keyboard.current.rKey.wasPressedThisFrame)
                    {
                        RestartLevel();
                    }
                    break;

                case LevelState.GameOver:
                    if (Keyboard.current.rKey.wasPressedThisFrame)
                    {
                        RestartLevel();
                    }
                    else if (Keyboard.current.escapeKey.wasPressedThisFrame)
                    {
                        // Future: ReturnToMenu()
                        RestartLevel(); // For now, just restart
                    }
                    break;

                case LevelState.Cleared:
                    UpdateCleared();
                    break;
            }
        }

        // --- Actions ---

        public void RestartLevel()
        {
            Time.timeScale = 1f; // Ensure timeScale restored
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        // --- Helpers ---

        private float GetCurrentTimeMs()
        {
            double elapsed = AudioSettings.dspTime - _trackStartDspTime;
            return Mathf.Max(0f, (float)(elapsed * 1000.0));
        }

        // --- Event Handlers ---

        private void OnJudgment(JudgmentResult result)
        {
            string tierStr = result.Tier.ToString();
            string timing = result.DeltaMs < 0 ? "早" : result.DeltaMs > 0 ? "晚" : "完美";
            string holdTag = result.IsHoldStart ? " [HOLD▶]" : result.IsHoldEnd ? " [HOLD■]" : "";
            string autoTag = result.IsAutoMiss ? " [AUTO]" : "";
            Debug.Log($"[LFM] {tierStr}{holdTag}{autoTag} ({result.DeltaMs:+0.0;-0.0}ms {timing}) Score: {result.Score}");

            if (UI != null) UI.ShowJudgment(result);
        }

        private void OnOverflow()
        {
            SuspicionMeter.HandleOverflow();
            EnterGameOver(GameOverReason.Overflow);
        }

        private void OnSocialDeath()
        {
            EnterGameOver(GameOverReason.SocialDeath);
        }
    }
}

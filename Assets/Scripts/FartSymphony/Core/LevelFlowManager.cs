using System;
using UnityEngine;
using FartSymphony.Gameplay;
using FartSymphony.UI;

namespace FartSymphony.Core
{
    /// <summary>
    /// Lifecycle orchestrator for a single level run.
    ///
    /// State machine:
    ///   Idle → Loading → Playing → Paused → GameOver / Cleared
    ///
    /// Responsibilities:
    ///   1. Load beat map and schedule audio.
    ///   2. Activate all subsystems in dependency order.
    ///   3. Monitor end conditions (Overflow, SocialDeath, TrackEnd).
    ///   4. Finalize score and route to the appropriate end screen.
    ///   5. Handle Pause / Resume / Restart / ReturnToMenu.
    ///
    /// Replaces GameBootstrap in the scene for full Sprint-2 runs.
    /// GameBootstrap remains for unit-level prototype testing.
    /// </summary>
    public sealed class LevelFlowManager : MonoBehaviour
    {
        // ── State machine ─────────────────────────────────────────────────────
        private enum State { Idle, Loading, Playing, Paused, GameOver, Cleared, Error }

        // ── Inspector — level config ──────────────────────────────────────────
        [Header("Level Config")]
        [Tooltip("Path under StreamingAssets/BeatMaps/ (no extension).")]
        [SerializeField] private string _beatMapFileName = "beethoven_5_mvt1";

        [Tooltip("The symphony AudioClip to play.")]
        [SerializeField] private AudioClip _trackClip;

        [Tooltip("Seconds to wait after track ends before transitioning to Cleared.")]
        [SerializeField] [Range(0.5f, 3f)] private float _endDelay = 1f;

        // ── Inspector — subsystem references ─────────────────────────────────
        [Header("Systems")]
        [SerializeField] private BeatMapLoader   _beatMapLoader;
        [SerializeField] private AudioManager    _audioManager;
        [SerializeField] private InputSystem     _inputSystem;
        [SerializeField] private TimingJudgment  _timingJudgment;
        [SerializeField] private BloatGauge      _bloatGauge;
        [SerializeField] private SuspicionMeter  _suspicionMeter;
        [SerializeField] private ScoreAndRating  _scoreAndRating;
        [SerializeField] private VisualCueSystem _visualCueSystem;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<LevelResult> OnLevelCleared;
        public event Action<LevelResult> OnLevelFailed;

        // ── Runtime state ─────────────────────────────────────────────────────
        private State _state = State.Idle;
        private bool  _endConditionHandled;

        // ── Unity lifecycle ───────────────────────────────────────────────────
        private void Start()
        {
            Application.runInBackground = true;
            LoadLevel();
        }

        private void Update()
        {
            if (_state != State.Playing) return;

            // Continuously track peak suspicion
            if (_suspicionMeter != null && _scoreAndRating != null)
                _scoreAndRating.UpdatePeakSuspicion(_suspicionMeter.SuspicionValue);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void LoadLevel()
        {
            if (_state != State.Idle && _state != State.Cleared && _state != State.GameOver)
            {
                Debug.LogWarning($"[LFM] LoadLevel ignored — current state: {_state}");
                return;
            }

            _endConditionHandled = false;
            SetState(State.Loading);
            StartCoroutine(LoadLevelRoutine());
        }

        public void Pause()
        {
            if (_state != State.Playing) return;
            SetState(State.Paused);
            Time.timeScale = 0f;

            _audioManager?.Pause();
            _timingJudgment?.Pause();
            _bloatGauge?.Pause();
            _suspicionMeter?.Pause();
            _scoreAndRating?.Pause();
            _visualCueSystem?.Pause();
        }

        public void Resume()
        {
            if (_state != State.Paused) return;
            Time.timeScale = 1f;

            _audioManager?.Resume();
            _timingJudgment?.Resume();
            _bloatGauge?.Resume();
            _suspicionMeter?.Resume();
            _scoreAndRating?.Resume();
            _visualCueSystem?.Resume();

            SetState(State.Playing);
        }

        public void RestartLevel()
        {
            if (_state == State.Paused) Time.timeScale = 1f;
            DeactivateAll();
            SetState(State.Idle);
            LoadLevel();
            Debug.Log("[LFM] Level restarted.");
        }

        public void ReturnToMenu()
        {
            if (_state == State.Paused) Time.timeScale = 1f;
            DeactivateAll();
            SetState(State.Idle);
            Debug.Log("[LFM] ReturnToMenu — load main menu scene here (Sprint 3).");
            // UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        // ── Load coroutine ────────────────────────────────────────────────────

        private System.Collections.IEnumerator LoadLevelRoutine()
        {
            // 1. Load beat map (synchronous)
            Debug.Log($"[LFM] Loading beat map: {_beatMapFileName}");

            // Inject filename into loader via reflection (same pattern as GameBootstrap)
            if (!string.IsNullOrEmpty(_beatMapFileName))
            {
                var field = typeof(BeatMapLoader).GetField("_beatMapFileName",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(_beatMapLoader, _beatMapFileName);
            }

            _beatMapLoader.Load();
            yield return null;  // allow a frame for any async side-effects

            if (!_beatMapLoader.IsReady)
            {
                Debug.LogError($"[LFM] Beat map failed to load: {_beatMapLoader.Error}");
                SetState(State.Error);
                yield break;
            }

            // 2. Wire dependencies
            _audioManager?.SetDependencies(_timingJudgment, _bloatGauge);
            _timingJudgment?.SetDependencies(_inputSystem);
            _bloatGauge?.SetDependencies(_timingJudgment);
            _suspicionMeter?.SetDependencies(_timingJudgment, _bloatGauge, _beatMapLoader);
            _scoreAndRating?.SetDependencies(_timingJudgment);
            _visualCueSystem?.SetDependencies(_timingJudgment, _bloatGauge,
                                               _suspicionMeter, _beatMapLoader, _audioManager);

            // 3. Subscribe to end conditions
            if (_bloatGauge     != null) _bloatGauge.OnOverflow        += HandleOverflow;
            if (_suspicionMeter != null) _suspicionMeter.OnSocialDeath += HandleSocialDeath;
            if (_audioManager   != null) _audioManager.OnTrackFinished += HandleTrackFinished;

            // 4. Schedule audio (returns dspTime of music start)
            double trackStartDsp = _trackClip != null
                ? _audioManager.PlayTrack(_trackClip)
                : AudioSettings.dspTime;

            double trackStartMs = trackStartDsp * 1000.0;

            // 5. Activate gameplay systems
            _timingJudgment?.Activate(_beatMapLoader.Data, trackStartMs);
            _bloatGauge?.Activate();
            _suspicionMeter?.Activate();
            _scoreAndRating?.Activate(_beatMapLoader.Data.AccentCount);
            _visualCueSystem?.Activate();
            _inputSystem?.SetActive();

            SetState(State.Playing);
            Debug.Log("[LFM] Level started. Playing.");
        }

        // ── End condition handlers ────────────────────────────────────────────

        private void HandleOverflow()
        {
            if (_endConditionHandled) return;
            _endConditionHandled = true;
            _scoreAndRating?.RecordOverflow();
            Debug.Log("[LFM] End condition: OVERFLOW → GameOver");
            EndLevel(cleared: false);
        }

        private void HandleSocialDeath()
        {
            if (_endConditionHandled) return;
            _endConditionHandled = true;
            Debug.Log("[LFM] End condition: SOCIAL DEATH → GameOver");
            EndLevel(cleared: false);
        }

        private void HandleTrackFinished()
        {
            if (_endConditionHandled) return;
            _endConditionHandled = true;
            Debug.Log($"[LFM] End condition: TRACK FINISHED → Cleared (delay {_endDelay}s)");
            StartCoroutine(EndAfterDelay(cleared: true));
        }

        private System.Collections.IEnumerator EndAfterDelay(bool cleared)
        {
            yield return new WaitForSeconds(_endDelay);
            EndLevel(cleared);
        }

        private void EndLevel(bool cleared)
        {
            DeactivateAll();

            var result = _scoreAndRating != null
                ? _scoreAndRating.FinalizeLevel(cleared)
                : new LevelResult(0, 0, 0, 0, 0, 0f, !cleared, cleared, "D", 0f);

            if (cleared)
            {
                SetState(State.Cleared);
                OnLevelCleared?.Invoke(result);
                Debug.Log($"[LFM] CLEARED — {result}");
            }
            else
            {
                SetState(State.GameOver);
                OnLevelFailed?.Invoke(result);
                Debug.Log($"[LFM] GAME OVER — {result}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void DeactivateAll()
        {
            _inputSystem?.SetDisabled();
            _timingJudgment?.Deactivate();
            _bloatGauge?.Deactivate();
            _suspicionMeter?.Deactivate();
            _scoreAndRating?.Deactivate();
            _visualCueSystem?.Deactivate();
            _audioManager?.Stop();

            if (_bloatGauge     != null) _bloatGauge.OnOverflow        -= HandleOverflow;
            if (_suspicionMeter != null) _suspicionMeter.OnSocialDeath -= HandleSocialDeath;
            if (_audioManager   != null) _audioManager.OnTrackFinished -= HandleTrackFinished;
        }

        private void SetState(State s)
        {
            _state = s;
            Debug.Log($"[LFM] → {s}");
        }
    }
}

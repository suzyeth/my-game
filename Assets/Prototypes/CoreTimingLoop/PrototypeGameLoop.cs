// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the core timing loop feel fun, tense, and satisfying?
// Date: 2026-03-28

using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype.CoreTimingLoop
{
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

        private AudioSource _audioSource;
        private double _trackStartDspTime;
        private bool _playing;
        private bool _gameOver;
        private string _gameOverReason;

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
            BeatMapLoader.Load();
            if (!BeatMapLoader.IsReady)
            {
                Debug.LogError($"[Prototype] Cannot start: {BeatMapLoader.Error}");
                return;
            }

            // Wire events
            TimingJudge.OnJudgment += BloatGauge.OnJudgmentReceived;
            TimingJudge.OnJudgment += SuspicionMeter.OnJudgmentReceived;
            TimingJudge.OnJudgment += OnJudgment;
            BloatGauge.OnOverflow += OnOverflow;
            SuspicionMeter.OnSocialDeath += OnSocialDeath;

            // Start systems
            TimingJudge.Activate(BeatMapLoader.Data);
            BloatGauge.Activate();
            SuspicionMeter.Activate(BeatMapLoader.Data);

            // Start music
            if (MusicClip != null)
            {
                _audioSource.clip = MusicClip;
                _trackStartDspTime = AudioSettings.dspTime + 0.5;
                _audioSource.PlayScheduled(_trackStartDspTime);
            }
            else
            {
                _trackStartDspTime = AudioSettings.dspTime + 0.5;
                Debug.LogWarning("[Prototype] No music clip assigned. Running with silent audio clock.");
            }

            _playing = true;
            Debug.Log("[Prototype] Game started! Press SPACE on the accent markers.");
        }

        private void Update()
        {
            // Restart must be checked before the early return
            if (_gameOver && Keyboard.current.rKey.wasPressedThisFrame)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                return;
            }

            if (!_playing || _gameOver) return;

            float currentTimeMs = GetCurrentTimeMs();
            float deltaTime = Time.deltaTime;

            // Check track end
            if (currentTimeMs >= BeatMapLoader.Data.Raw.meta.durationMs)
            {
                OnTrackComplete();
                return;
            }

            // Tick systems
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

        private float GetCurrentTimeMs()
        {
            double elapsed = AudioSettings.dspTime - _trackStartDspTime;
            return (float)(elapsed * 1000.0);
        }

        private void OnJudgment(JudgmentResult result)
        {
            string tierStr = result.Tier.ToString();
            string timing = result.DeltaMs < 0 ? "早" : result.DeltaMs > 0 ? "晚" : "完美";
            string holdTag = result.IsHoldStart ? " [HOLD▶]" : result.IsHoldEnd ? " [HOLD■]" : "";
            Debug.Log($"[Prototype] {tierStr}{holdTag} ({result.DeltaMs:+0.0;-0.0}ms {timing}) Score: {result.Score}");

            if (UI != null) UI.ShowJudgment(result);
        }

        private void OnOverflow()
        {
            _gameOver = true;
            _gameOverReason = "OVERFLOW";
            TimingJudge.Deactivate();
            BloatGauge.Deactivate();
            SuspicionMeter.HandleOverflow();
            if (UI != null) UI.ShowGameOver("OVERFLOW — 气胀溢出！", TimingJudge);
            Debug.Log("[Prototype] === GAME OVER: OVERFLOW === Press R to restart.");
        }

        private void OnSocialDeath()
        {
            if (_gameOver) return;
            _gameOver = true;
            _gameOverReason = "SOCIAL_DEATH";
            TimingJudge.Deactivate();
            BloatGauge.Deactivate();
            if (UI != null) UI.ShowGameOver("SOCIAL DEATH — 社死！", TimingJudge);
            Debug.Log("[Prototype] === GAME OVER: SOCIAL DEATH === Press R to restart.");
        }

        private void OnTrackComplete()
        {
            _playing = false;
            TimingJudge.Deactivate();
            BloatGauge.Deactivate();
            SuspicionMeter.Deactivate();

            int total = TimingJudge.PerfectCount + TimingJudge.GoodCount + TimingJudge.MissCount;
            float perfectRatio = total > 0 ? (float)TimingJudge.PerfectCount / total : 0;
            string rating = perfectRatio >= 0.95f ? "S" :
                            perfectRatio >= 0.80f ? "A" :
                            perfectRatio >= 0.60f ? "B" :
                            perfectRatio >= 0.40f ? "C" : "D";

            Debug.Log($"[Prototype] === TRACK COMPLETE ===");
            Debug.Log($"  Score: {TimingJudge.TotalScore}");
            Debug.Log($"  Rating: {rating}");
            Debug.Log($"  Perfect: {TimingJudge.PerfectCount} | Good: {TimingJudge.GoodCount} | Miss: {TimingJudge.MissCount}");
            Debug.Log($"  Max Combo: {TimingJudge.MaxCombo}");
            Debug.Log($"  Peak Suspicion: {SuspicionMeter.PeakSuspicion:F1}");

            if (UI != null) UI.ShowResults(rating, TimingJudge, SuspicionMeter);
        }
    }
}

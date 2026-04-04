using System;
using UnityEngine;
using UnityEngine.Audio;
using FartSymphony.Gameplay;

namespace FartSymphony.Core
{
    /// <summary>
    /// Global audio clock authority and multi-channel mixer.
    ///
    /// Responsibilities:
    ///   1. Schedule and play the symphony track via AudioSettings.dspTime (ADR-0001).
    ///   2. Expose GetCurrentTrackTimeMs() as the authoritative game clock.
    ///   3. Manage fart SFX pool: select variant by JudgmentTier and BeatMap dynamic level.
    ///   4. Support Pause / Resume / Stop with no audio artefacts.
    ///
    /// ADR-0001: dspTime is the single source of truth for all timing.
    /// ADR-0002: Subscribes to TimingJudgment.OnJudgment and BloatGauge.OnOverflow.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        // ── Inspector — mixer channels ────────────────────────────────────────
        [Header("Audio Mixer (optional)")]
        [Tooltip("Assign an AudioMixer to use exposed volume parameters. " +
                 "Leave empty to use direct AudioSource volume instead.")]
        [SerializeField] private AudioMixer _mixer;

        [Header("Volume (0–1)")]
        [SerializeField] [Range(0f, 1f)] private float _musicVolume    = 0.8f;
        [SerializeField] [Range(0f, 1f)] private float _fartVolume     = 0.7f;
        [SerializeField] [Range(0f, 1f)] private float _audienceVolume = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float _uiVolume       = 0.8f;

        // ── Inspector — SFX clips ─────────────────────────────────────────────
        [Header("Fart SFX Pool")]
        [Tooltip("Multiple clips; no two consecutive plays will use the same index.")]
        [SerializeField] private AudioClip[] _fartClips;

        [Tooltip("Played at full volume on BloatGauge Overflow.")]
        [SerializeField] private AudioClip   _overflowFartClip;

        [Header("Fart Tuning")]
        [SerializeField] [Range(0.3f, 1f)]  private float _baseFartVolume        = 0.7f;
        [SerializeField] [Range(0.3f, 0.9f)]private float _fartReverbMixPerfect  = 0.6f;
        [SerializeField] [Range(0f,   0.2f)]private float _fartReverbMixMiss     = 0.05f;

        // ── Dependencies ──────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private TimingJudgment _timingJudgment;
        [SerializeField] private BloatGauge     _bloatGauge;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fires when the scheduled music track reaches its natural end.</summary>
        public event Action OnTrackFinished;

        // ── Runtime state ─────────────────────────────────────────────────────
        private AudioSource _musicSource;
        private AudioSource _fartSource;
        private AudioSource _audienceSource;
        private AudioSource _uiSource;

        private double _trackStartDspTime;  // dspTime when music was scheduled to start
        private float  _trackDurationSec;
        private bool   _playing;
        private bool   _paused;
        private double _pausedDspTime;      // dspTime snapshot on pause
        private int    _lastFartIndex = -1;

        // ── Public clock ──────────────────────────────────────────────────────
        /// <summary>
        /// Current music playback position in milliseconds.
        /// Authoritative game clock (ADR-0001).
        /// Returns 0 if not playing.
        /// </summary>
        public float GetCurrentTrackTimeMs()
        {
            if (!_playing) return 0f;
            double elapsed = AudioSettings.dspTime - _trackStartDspTime;
            return (float)(elapsed * 1000.0);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            _musicSource    = CreateSource("Music",    _musicVolume);
            _fartSource     = CreateSource("Fart",     _fartVolume);
            _audienceSource = CreateSource("Audience", _audienceVolume);
            _uiSource       = CreateSource("UI",       _uiVolume);
        }

        private AudioSource CreateSource(string label, float volume)
        {
            var go = new GameObject($"AudioSource_{label}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.volume      = volume;
            src.loop        = false;
            return src;
        }

        // ── ADR-0002 subscription pattern ─────────────────────────────────────
        public void SetDependencies(TimingJudgment tj, BloatGauge bloatGauge)
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment -= HandleJudgment;
            if (_bloatGauge     != null) _bloatGauge.OnOverflow      -= HandleOverflow;

            _timingJudgment = tj;
            _bloatGauge     = bloatGauge;

            if (_timingJudgment != null) _timingJudgment.OnJudgment += HandleJudgment;
            if (_bloatGauge     != null) _bloatGauge.OnOverflow      += HandleOverflow;
        }

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

        // ── Update — track end detection ──────────────────────────────────────
        private void Update()
        {
            if (!_playing || _paused || _trackDurationSec <= 0f) return;

            double elapsed = AudioSettings.dspTime - _trackStartDspTime;
            if (elapsed >= _trackDurationSec)
            {
                _playing = false;
                Debug.Log("[AudioManager] Track finished naturally.");
                OnTrackFinished?.Invoke();
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Schedule the music clip to start playing at the next available dspTime.
        /// Returns the exact dspTime the music will start (use as trackStartMs reference).
        /// </summary>
        public double PlayTrack(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogError("[AudioManager] PlayTrack: clip is null.");
                return AudioSettings.dspTime;
            }

            _musicSource.clip = clip;
            _trackDurationSec = clip.length;

            // Schedule slightly ahead to guarantee frame-accurate start
            double startTime = AudioSettings.dspTime + 0.1;
            _musicSource.PlayScheduled(startTime);

            _trackStartDspTime = startTime;
            _playing           = true;
            _paused            = false;

            Debug.Log($"[AudioManager] PlayTrack '{clip.name}'  " +
                      $"scheduled at dspTime={startTime:F4}  duration={_trackDurationSec:F1}s");

            return startTime;
        }

        /// <summary>Pause music and freeze game clock.</summary>
        public void Pause()
        {
            if (!_playing || _paused) return;
            _paused        = true;
            _pausedDspTime = AudioSettings.dspTime;
            _musicSource.Pause();
            Debug.Log("[AudioManager] Paused.");
        }

        /// <summary>Resume music. Adjusts track start time to account for pause duration.</summary>
        public void Resume()
        {
            if (!_playing || !_paused) return;
            double pauseDuration   = AudioSettings.dspTime - _pausedDspTime;
            _trackStartDspTime    += pauseDuration;
            _paused                = false;
            _musicSource.UnPause();
            Debug.Log($"[AudioManager] Resumed. Pause was {pauseDuration * 1000:F0}ms.");
        }

        /// <summary>Stop music and reset state.</summary>
        public void Stop()
        {
            _playing = false;
            _paused  = false;
            _musicSource.Stop();
            Debug.Log("[AudioManager] Stopped.");
        }

        /// <summary>Play the overflow fart at maximum volume.</summary>
        public void PlayOverflowFart()
        {
            if (_overflowFartClip == null) return;
            _fartSource.PlayOneShot(_overflowFartClip, 1f);
            Debug.Log("[AudioManager] PlayOverflowFart");
        }

        // ── Judgment handler — fart SFX ───────────────────────────────────────

        private void HandleJudgment(JudgmentResult result)
        {
            if (_fartClips == null || _fartClips.Length == 0) return;
            if (result.IsAutoMiss) return; // no sound for silent missed accents

            float volume = ComputeFartVolume(result.Tier);
            int   idx    = PickFartIndex();
            _fartSource.PlayOneShot(_fartClips[idx], volume);
        }

        private void HandleOverflow() => PlayOverflowFart();

        // ── Fart volume formula (GDD audio-manager.md) ────────────────────────
        private float ComputeFartVolume(JudgmentTier tier)
        {
            float judgmentMultiplier = tier switch
            {
                JudgmentTier.Perfect => 0.6f,
                JudgmentTier.Good    => 1.0f,
                JudgmentTier.Miss    => 1.8f,
                _                    => 1.0f
            };
            return Mathf.Clamp01(_baseFartVolume * judgmentMultiplier);
        }

        private int PickFartIndex()
        {
            if (_fartClips.Length == 1) return 0;

            int idx;
            do { idx = UnityEngine.Random.Range(0, _fartClips.Length); }
            while (idx == _lastFartIndex);

            _lastFartIndex = idx;
            return idx;
        }
    }
}

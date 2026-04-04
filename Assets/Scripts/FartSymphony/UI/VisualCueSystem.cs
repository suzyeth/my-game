using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using FartSymphony.Core;
using FartSymphony.Gameplay;

namespace FartSymphony.UI
{
    /// <summary>
    /// Visual bridge between Beat Map data and on-screen gameplay feedback.
    ///
    /// Responsibilities:
    ///   1. Scroll note icons from right to left along the judgment track.
    ///   2. Show Perfect / Good / Exposed! popups on judgment events.
    ///   3. Drive bloat gauge color (green → yellow → orange → red).
    ///   4. Drive screen vignette darkness based on suspicion ratio.
    ///
    /// Uses a fixed-size note pool to achieve zero per-frame allocations.
    /// ADR-0002: Subscribes to TimingJudgment.OnJudgment in OnEnable/OnDisable.
    /// </summary>
    public sealed class VisualCueSystem : MonoBehaviour
    {
        // ── Inspector — Layout ────────────────────────────────────────────────
        [Header("Track Layout")]
        [Tooltip("The RectTransform of the horizontal note track.")]
        [SerializeField] private RectTransform _trackRect;

        [Tooltip("X position (in track-local space) of the judgment line. " +
                 "Notes spawn at right edge and travel here.")]
        [SerializeField] [Range(0f, 0.3f)] private float _judgmentLineRatio = 0.15f;

        [Tooltip("How many ms ahead of the judgment line notes first appear.")]
        [SerializeField] [Range(1000f, 5000f)] private float _lookAheadMs = 3000f;

        // ── Inspector — Note Pool ─────────────────────────────────────────────
        [Header("Note Pool")]
        [Tooltip("Prefab for a note icon. Must have an Image component.")]
        [SerializeField] private GameObject _notePrefab;

        [Tooltip("Pool size. Should be >= max simultaneous on-screen notes.")]
        [SerializeField] [Range(8, 32)] private int _poolSize = 16;

        [Tooltip("Base pixel size of a mezzo-intensity note icon.")]
        [SerializeField] [Range(24f, 96f)] private float _iconBaseSize = 48f;

        // ── Inspector — Judgment Popups ───────────────────────────────────────
        [Header("Judgment Popups")]
        [SerializeField] private Text     _popupText;
        [SerializeField] [Range(0.2f, 1f)] private float _popupDuration = 0.5f;

        [Header("Popup Sprites (optional — falls back to text color)")]
        [SerializeField] private Sprite _perfectSprite;
        [SerializeField] private Sprite _goodSprite;
        [SerializeField] private Sprite _exposedSprite;

        // ── Inspector — Bloat Gauge ───────────────────────────────────────────
        [Header("Bloat Gauge UI")]
        [Tooltip("Image used as the bloat fill bar. Set Image.Type = Filled.")]
        [SerializeField] private Image _bloatFillImage;

        private static readonly Color _bloatColorSafe    = new Color(0.2f, 0.8f, 0.2f);
        private static readonly Color _bloatColorWarn    = new Color(1.0f, 0.8f, 0.0f);
        private static readonly Color _bloatColorDanger  = new Color(1.0f, 0.4f, 0.0f);
        private static readonly Color _bloatColorCritical= new Color(0.9f, 0.1f, 0.1f);

        // ── Inspector — Suspicion Vignette ────────────────────────────────────
        [Header("Suspicion Vignette")]
        [Tooltip("Full-screen overlay Image. Set alpha via color.a. " +
                 "Leave empty to skip vignette.")]
        [SerializeField] private Image  _vignetteImage;
        [SerializeField] [Range(0f, 0.8f)] private float _vignetteMaxAlpha = 0.6f;

        // ── Dependencies ──────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private TimingJudgment _timingJudgment;
        [SerializeField] private BloatGauge     _bloatGauge;
        [SerializeField] private SuspicionMeter _suspicionMeter;
        [SerializeField] private BeatMapLoader  _beatMapLoader;
        [SerializeField] private AudioManager   _audioManager;

        // ── Runtime state ─────────────────────────────────────────────────────
        private bool _active;

        // Note pool
        private GameObject[]    _pool;
        private RectTransform[] _poolRects;
        private Image[]         _poolImages;
        private float[]         _poolTargetTimeMs;  // beat-map time this note is targeting
        private bool[]          _poolActive;
        private int             _poolHead;

        // Buffer for zero-alloc accent queries
        private AccentData[]    _accentBuffer;
        private float           _lastLookAheadFrontMs = -1f;

        // ── Dependency injection ───────────────────────────────────────────────
        public void SetDependencies(
            TimingJudgment tj, BloatGauge bg, SuspicionMeter sm,
            BeatMapLoader bml, AudioManager am)
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment -= HandleJudgment;

            _timingJudgment = tj;
            _bloatGauge     = bg;
            _suspicionMeter = sm;
            _beatMapLoader  = bml;
            _audioManager   = am;

            if (_timingJudgment != null) _timingJudgment.OnJudgment += HandleJudgment;
        }

        private void OnEnable()
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment += HandleJudgment;
        }

        private void OnDisable()
        {
            if (_timingJudgment != null) _timingJudgment.OnJudgment -= HandleJudgment;
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────
        private void Awake()
        {
            BuildPool();
            _accentBuffer = new AccentData[_poolSize];
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Activate()
        {
            _active              = true;
            _lastLookAheadFrontMs = -1f;
            ReturnAllToPool();
        }

        public void Deactivate()
        {
            _active = false;
            ReturnAllToPool();
        }

        public void Pause()  => _active = false;
        public void Resume() => _active = true;

        // ── Update loop ───────────────────────────────────────────────────────
        private void Update()
        {
            if (!_active) return;

            float currentMs = _audioManager != null
                ? _audioManager.GetCurrentTrackTimeMs()
                : (float)(AudioSettings.dspTime * 1000.0);

            ScrollNotes(currentMs);
            SpawnNotes(currentMs);
            UpdateBloatGauge();
            UpdateVignette();
        }

        // ── Note scrolling ────────────────────────────────────────────────────

        private void ScrollNotes(float currentMs)
        {
            if (_trackRect == null) return;

            float trackWidth      = _trackRect.rect.width;
            float judgmentLineX   = _trackRect.rect.xMin + trackWidth * _judgmentLineRatio;
            float scrollSpeed     = trackWidth / _lookAheadMs;   // px/ms

            for (int i = 0; i < _poolSize; i++)
            {
                if (!_poolActive[i]) continue;

                float timeUntil  = _poolTargetTimeMs[i] - currentMs;
                float x          = judgmentLineX + timeUntil * scrollSpeed;

                _poolRects[i].anchoredPosition = new Vector2(x, 0f);

                // Recycle notes that have passed the judgment line by > 1 LookAhead
                if (timeUntil < -_lookAheadMs)
                    ReturnToPool(i);
            }
        }

        private void SpawnNotes(float currentMs)
        {
            if (_beatMapLoader == null || _beatMapLoader.Data == null) return;

            float windowEnd   = currentMs + _lookAheadMs;
            float windowStart = Mathf.Max(0f, currentMs);

            // Only query when the window front advances
            if (windowEnd <= _lastLookAheadFrontMs) return;
            _lastLookAheadFrontMs = windowEnd;

            int count = _beatMapLoader.Data.GetAccentsInRange(
                windowStart, windowEnd, _accentBuffer, _poolSize);

            for (int i = 0; i < count; i++)
            {
                var accent = _accentBuffer[i];
                if (IsAlreadySpawned(accent.TimeMs)) continue;
                SpawnNote(accent);
            }
        }

        private bool IsAlreadySpawned(float timeMs)
        {
            for (int i = 0; i < _poolSize; i++)
                if (_poolActive[i] && Mathf.Approximately(_poolTargetTimeMs[i], timeMs))
                    return true;
            return false;
        }

        private void SpawnNote(AccentData accent)
        {
            int slot = GetPoolSlot();
            if (slot < 0) return;

            _poolActive[slot]        = true;
            _poolTargetTimeMs[slot]  = accent.TimeMs;
            _pool[slot].SetActive(true);

            // Size by intensity
            float sizeMultiplier = accent.Intensity switch
            {
                "fortissimo" => 1.5f,
                "forte"      => 1.2f,
                "mezzo"      => 1.0f,
                "piano"      => 0.7f,
                "pianissimo" => 0.5f,
                _            => 1.0f
            };
            float size = _iconBaseSize * sizeMultiplier;
            _poolRects[slot].sizeDelta = new Vector2(size, size);

            // Color by intensity
            Color noteColor = accent.Intensity switch
            {
                "fortissimo" => new Color(1.0f, 0.85f, 0.0f),
                "forte"      => new Color(1.0f, 0.95f, 0.5f),
                "mezzo"      => Color.white,
                "piano"      => new Color(0.8f, 0.8f, 0.8f, 0.7f),
                "pianissimo" => new Color(0.8f, 0.8f, 0.8f, 0.4f),
                _            => Color.white
            };
            if (_poolImages[slot] != null) _poolImages[slot].color = noteColor;
        }

        // ── Judgment popup ────────────────────────────────────────────────────

        private void HandleJudgment(JudgmentResult result)
        {
            if (!_active) return;

            string label = result.Tier switch
            {
                JudgmentTier.Perfect => "Perfect!",
                JudgmentTier.Good    => "Good",
                JudgmentTier.Miss    => "Exposed!",
                _                    => ""
            };

            Color color = result.Tier switch
            {
                JudgmentTier.Perfect => new Color(1f, 0.85f, 0f),
                JudgmentTier.Good    => Color.white,
                JudgmentTier.Miss    => new Color(1f, 0.3f, 0.1f),
                _                    => Color.white
            };

            if (_popupText != null)
                StartCoroutine(ShowPopup(label, color));
        }

        private IEnumerator ShowPopup(string text, Color color)
        {
            _popupText.text  = text;
            _popupText.color = color;
            _popupText.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < _popupDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / _popupDuration;
                Color c = _popupText.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                _popupText.color = c;
                yield return null;
            }

            _popupText.gameObject.SetActive(false);
        }

        // ── Bloat gauge color ─────────────────────────────────────────────────

        private void UpdateBloatGauge()
        {
            if (_bloatFillImage == null || _bloatGauge == null) return;

            float ratio = _bloatGauge.BloatRatio;
            _bloatFillImage.fillAmount = ratio;

            Color targetColor;
            if      (ratio < 0.5f) targetColor = Color.Lerp(_bloatColorSafe,     _bloatColorWarn,    ratio / 0.5f);
            else if (ratio < 0.75f)targetColor = Color.Lerp(_bloatColorWarn,     _bloatColorDanger,  (ratio - 0.5f) / 0.25f);
            else                   targetColor = Color.Lerp(_bloatColorDanger,   _bloatColorCritical,(ratio - 0.75f) / 0.25f);

            _bloatFillImage.color = targetColor;
        }

        // ── Suspicion vignette ────────────────────────────────────────────────

        private void UpdateVignette()
        {
            if (_vignetteImage == null || _suspicionMeter == null) return;

            float ratio = _suspicionMeter.GetSuspicionRatio();
            float alpha = Mathf.Clamp01((ratio - 0.3f) / 0.7f) * _vignetteMaxAlpha;
            Color c     = _vignetteImage.color;
            c.a         = alpha;
            _vignetteImage.color = c;
        }

        // ── Pool management ───────────────────────────────────────────────────

        private void BuildPool()
        {
            _pool             = new GameObject[_poolSize];
            _poolRects        = new RectTransform[_poolSize];
            _poolImages       = new Image[_poolSize];
            _poolTargetTimeMs = new float[_poolSize];
            _poolActive       = new bool[_poolSize];

            Transform parent = _trackRect != null ? _trackRect : transform;

            for (int i = 0; i < _poolSize; i++)
            {
                GameObject go = _notePrefab != null
                    ? Instantiate(_notePrefab, parent)
                    : CreateDefaultNotePrefab(parent);

                go.SetActive(false);
                _pool[i]       = go;
                _poolRects[i]  = go.GetComponent<RectTransform>();
                _poolImages[i] = go.GetComponent<Image>();
            }
        }

        private GameObject CreateDefaultNotePrefab(Transform parent)
        {
            var go  = new GameObject("Note");
            go.transform.SetParent(parent, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(_iconBaseSize, _iconBaseSize);
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            return go;
        }

        private int GetPoolSlot()
        {
            for (int i = 0; i < _poolSize; i++)
            {
                int slot = (_poolHead + i) % _poolSize;
                if (!_poolActive[slot])
                {
                    _poolHead = (slot + 1) % _poolSize;
                    return slot;
                }
            }
            Debug.LogWarning("[VisualCueSystem] Note pool exhausted — increase pool size.");
            return -1;
        }

        private void ReturnToPool(int slot)
        {
            _poolActive[slot] = false;
            _pool[slot].SetActive(false);
        }

        private void ReturnAllToPool()
        {
            for (int i = 0; i < _poolSize; i++)
                ReturnToPool(i);
        }
    }
}

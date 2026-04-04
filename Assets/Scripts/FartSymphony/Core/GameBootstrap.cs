using UnityEngine;
using FartSymphony.Gameplay;

namespace FartSymphony.Core
{
    /// <summary>
    /// Sprint 1 bootstrap — auto-discovers systems in the scene and starts the core loop.
    /// No UI, no audio. Console output only.
    ///
    /// Attach to a single GameObject in the Sprint1Dev scene.
    /// No Inspector wiring required — systems are found by type at runtime.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Beat Map")]
        [Tooltip("File name inside StreamingAssets/BeatMaps/")]
        [SerializeField] private string _beatMapFileName = "beethoven_5_mvt1.json";

        // Discovered at Start — no serialized cross-references needed
        private InputSystem    _inputSystem;
        private BeatMapLoader  _beatMapLoader;
        private TimingJudgment _timingJudgment;
        private BloatGauge     _bloatGauge;

        private void Start()
        {
            // Ensure game runs even when the Editor window loses focus (e.g. remote MCP sessions)
            Application.runInBackground = true;

            // ── 1. Discover systems ──────────────────────────────────────────
            _inputSystem    = FindFirstObjectByType<InputSystem>();
            _beatMapLoader  = FindFirstObjectByType<BeatMapLoader>();
            _timingJudgment = FindFirstObjectByType<TimingJudgment>();
            _bloatGauge     = FindFirstObjectByType<BloatGauge>();

            if (_inputSystem == null || _beatMapLoader == null ||
                _timingJudgment == null || _bloatGauge == null)
            {
                Debug.LogError("[Bootstrap] Could not find all required systems in the scene! " +
                               $"IS={_inputSystem != null} BML={_beatMapLoader != null} " +
                               $"TJ={_timingJudgment != null} BG={_bloatGauge != null}");
                return;
            }

            // ── 2. Load beat map ─────────────────────────────────────────────
            // Override the loader's filename if specified here
            if (!string.IsNullOrEmpty(_beatMapFileName))
            {
                var field = typeof(BeatMapLoader).GetField("_beatMapFileName",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(_beatMapLoader, _beatMapFileName);
            }

            _beatMapLoader.OnBeatMapLoadError += OnBeatMapLoadError;
            _beatMapLoader.Load();

            if (!_beatMapLoader.IsReady)
            {
                Debug.LogError($"[Bootstrap] Beat map failed to load: {_beatMapLoader.Error}");
                return;
            }

            // ── 3. Wire cross-references ─────────────────────────────────────
            // SetDependencies subscribes immediately, so even if OnEnable ran
            // before Inspector refs were valid, the correct instances are wired now.
            _timingJudgment.SetDependencies(_inputSystem);
            _bloatGauge.SetDependencies(_timingJudgment);

            // ── 4. Activate systems ──────────────────────────────────────────
            // Record exact dspTime at level start so TimingJudgment can
            // convert absolute dspTime → beat-map-relative track time.
            double trackStartMs = AudioSettings.dspTime * 1000.0;
            _timingJudgment.Activate(_beatMapLoader.Data, trackStartMs);
            _bloatGauge.Activate();
            _inputSystem.SetActive();

            // ── 5. Subscribe to overflow ─────────────────────────────────────
            _bloatGauge.OnOverflow += OnOverflow;

            Debug.Log("[Bootstrap] Core loop STARTED. " +
                      $"Beat map: \"{_beatMapLoader.Data.Title}\" | " +
                      $"{_beatMapLoader.Data.AccentCount} accents | " +
                      $"trackStart={trackStartMs:F0}ms | " +
                      "Press SPACE to judge. Press ESC to pause.");

            // Diagnostic: log status at 3s and 10s to confirm systems are running
            Invoke(nameof(LogStatus3s),  3f);
            Invoke(nameof(LogStatus10s), 10f);
        }

        private void LogStatus3s()
        {
            Debug.Log($"[Bootstrap] 3s STATUS  " +
                      $"TJ.active={_timingJudgment != null}  " +
                      $"TJ.Miss={_timingJudgment?.MissCount}  " +
                      $"BG.Bloat={_bloatGauge?.BloatValue:F1}/{_bloatGauge?.MaxBloat:F0}");
        }

        private void LogStatus10s()
        {
            Debug.Log($"[Bootstrap] 10s STATUS  " +
                      $"TJ.Miss={_timingJudgment?.MissCount}  " +
                      $"TJ.Score={_timingJudgment?.TotalScore}  " +
                      $"BG.Bloat={_bloatGauge?.BloatValue:F1}/{_bloatGauge?.MaxBloat:F0}");
        }

        private void OnDestroy()
        {
            if (_beatMapLoader != null)
                _beatMapLoader.OnBeatMapLoadError -= OnBeatMapLoadError;
            if (_bloatGauge != null)
                _bloatGauge.OnOverflow -= OnOverflow;
        }

        private void OnBeatMapLoadError(string filePath)
        {
            Debug.LogError($"[Bootstrap] Beat map load error: {filePath}");
        }

        private void OnOverflow()
        {
            _inputSystem.SetDisabled();
            _timingJudgment.Deactivate();
            Debug.Log("[Bootstrap] OVERFLOW — level ended! " +
                      $"Score={_timingJudgment.TotalScore}  " +
                      $"P={_timingJudgment.PerfectCount}  " +
                      $"G={_timingJudgment.GoodCount}  " +
                      $"Miss={_timingJudgment.MissCount}  " +
                      $"Combo={_timingJudgment.MaxCombo}");
        }
    }
}

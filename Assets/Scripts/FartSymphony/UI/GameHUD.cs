using System;
using UnityEngine;
using UnityEngine.UI;
using FartSymphony.Core;
using FartSymphony.Gameplay;

namespace FartSymphony.UI
{
    /// <summary>
    /// Text HUD layer: score, combo, timer, judgment hints, results panel, pause overlay.
    /// Subscribes to TimingJudgment.OnJudgment and LevelFlowManager events.
    /// </summary>
    public sealed class GameHUD : MonoBehaviour
    {
        // ── Inspector — Score / Timer bar ────────────────────────────────────
        [Header("Score / Timer")]
        [SerializeField] private Text _scoreText;
        [SerializeField] private Text _comboText;
        [SerializeField] private Text _timeText;

        // ── Inspector — Judgment hint ────────────────────────────────────────
        [Header("Judgment Hint")]
        [Tooltip("Small text below the popup showing 早了 / 晚了 / delta ms.")]
        [SerializeField] private Text _judgmentHintText;
        [SerializeField] [Range(0.2f, 1.5f)] private float _hintDuration = 0.6f;

        // ── Inspector — Results panel ────────────────────────────────────────
        [Header("Results Panel")]
        [SerializeField] private GameObject _resultsPanel;
        [SerializeField] private Text       _resultsTitleText;
        [SerializeField] private Text       _resultsBodyText;

        // ── Inspector — Pause overlay ────────────────────────────────────────
        [Header("Pause Overlay")]
        [SerializeField] private GameObject _pausePanel;
        [SerializeField] private Text       _pauseText;

        // ── Dependencies ──────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private TimingJudgment  _timingJudgment;
        [SerializeField] private ScoreAndRating  _scoreAndRating;
        [SerializeField] private AudioManager    _audioManager;
        [SerializeField] private LevelFlowManager _levelFlowManager;

        // ── Runtime ───────────────────────────────────────────────────────────
        private float _hintTimer;

        // ── Dependency injection ──────────────────────────────────────────────
        public void SetDependencies(
            TimingJudgment tj, ScoreAndRating sar,
            AudioManager am, LevelFlowManager lfm)
        {
            Unsubscribe();
            _timingJudgment   = tj;
            _scoreAndRating   = sar;
            _audioManager     = am;
            _levelFlowManager = lfm;
            Subscribe();
        }

        // ── ADR-0002 ──────────────────────────────────────────────────────────
        private void OnEnable()  => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_timingJudgment   != null) _timingJudgment.OnJudgment     += HandleJudgment;
            if (_levelFlowManager != null)
            {
                _levelFlowManager.OnLevelCleared += HandleCleared;
                _levelFlowManager.OnLevelFailed  += HandleFailed;
            }
        }

        private void Unsubscribe()
        {
            if (_timingJudgment   != null) _timingJudgment.OnJudgment     -= HandleJudgment;
            if (_levelFlowManager != null)
            {
                _levelFlowManager.OnLevelCleared -= HandleCleared;
                _levelFlowManager.OnLevelFailed  -= HandleFailed;
            }
        }

        // ── Unity ─────────────────────────────────────────────────────────────
        private void Start()
        {
            if (_resultsPanel != null) _resultsPanel.SetActive(false);
            if (_pausePanel   != null) _pausePanel.SetActive(false);
            if (_judgmentHintText != null) _judgmentHintText.gameObject.SetActive(false);
        }

        private void Update()
        {
            UpdateScoreCombo();
            UpdateTimer();
            TickHint();
        }

        // ── Score / combo / timer ─────────────────────────────────────────────
        private void UpdateScoreCombo()
        {
            if (_scoreAndRating == null) return;
            if (_scoreText  != null)
                _scoreText.text  = $"Score: {_scoreAndRating.TotalScore}";
            if (_comboText != null)
                _comboText.text = _scoreAndRating.CurrentCombo > 1
                    ? $"{_scoreAndRating.CurrentCombo} Combo!"
                    : "";
        }

        private void UpdateTimer()
        {
            if (_timeText == null) return;
            float ms = _audioManager != null
                ? _audioManager.GetCurrentTrackTimeMs()
                : 0f;
            int seconds = Mathf.FloorToInt(ms / 1000f);
            _timeText.text = $"{seconds / 60}:{seconds % 60:D2}";
        }

        // ── Judgment hint ─────────────────────────────────────────────────────
        private void HandleJudgment(JudgmentResult result)
        {
            if (_judgmentHintText == null) return;
            if (result.IsAutoMiss || result.WasOutsideWindow)
            {
                _judgmentHintText.gameObject.SetActive(false);
                return;
            }

            string hint = "";
            if (Mathf.Abs(result.DeltaMs) > 5f)
                hint = result.DeltaMs < 0
                    ? $"← 早了 {-result.DeltaMs:F0}ms"
                    : $"→ 晚了 {result.DeltaMs:F0}ms";

            if (hint.Length > 0)
            {
                _judgmentHintText.text  = hint;
                _judgmentHintText.color = result.DeltaMs < 0
                    ? new Color(0.4f, 0.8f, 1f, 1f)   // blue = early
                    : new Color(1f, 0.6f, 0.2f, 1f);   // orange = late
                _judgmentHintText.gameObject.SetActive(true);
                _hintTimer = _hintDuration;
            }
        }

        private void TickHint()
        {
            if (_hintTimer <= 0f || _judgmentHintText == null) return;
            _hintTimer -= Time.deltaTime;
            float alpha = Mathf.Clamp01(_hintTimer / (_hintDuration * 0.4f));
            var c = _judgmentHintText.color;
            c.a = alpha;
            _judgmentHintText.color = c;
            if (_hintTimer <= 0f)
                _judgmentHintText.gameObject.SetActive(false);
        }

        // ── Level end ─────────────────────────────────────────────────────────
        private void HandleCleared(LevelResult result)
        {
            if (_resultsPanel == null) return;
            _resultsPanel.SetActive(true);

            if (_resultsTitleText != null)
            {
                _resultsTitleText.text  = "演出完成！";
                _resultsTitleText.color = result.Rating switch
                {
                    "S" => new Color(1f, 0.9f, 0.1f),
                    "A" => Color.white,
                    "B" => new Color(0.6f, 1f, 0.6f),
                    _   => new Color(0.7f, 0.7f, 0.7f)
                };
            }

            if (_resultsBodyText != null)
                _resultsBodyText.text =
                    $"评级: {result.Rating}\n" +
                    $"Score: {result.TotalScore}\n" +
                    $"Perfect: {result.PerfectCount}  " +
                    $"Good: {result.GoodCount}  " +
                    $"Miss: {result.MissCount}\n" +
                    $"Max Combo: {result.MaxCombo}\n" +
                    $"Peak Suspicion: {result.PeakSuspicion:F0}%\n\n" +
                    "[R] 重试     [ESC] 菜单";
        }

        private void HandleFailed(LevelResult result)
        {
            if (_resultsPanel == null) return;
            _resultsPanel.SetActive(true);

            if (_resultsTitleText != null)
            {
                _resultsTitleText.text  = result.HadOverflow ? "屁值爆表！" : "被发现了！";
                _resultsTitleText.color = Color.red;
            }

            if (_resultsBodyText != null)
                _resultsBodyText.text =
                    $"Score: {result.TotalScore}\n" +
                    $"Perfect: {result.PerfectCount}  " +
                    $"Good: {result.GoodCount}  " +
                    $"Miss: {result.MissCount}\n" +
                    $"Max Combo: {result.MaxCombo}\n\n" +
                    "[R] 重试     [ESC] 菜单";
        }

        // ── Pause ─────────────────────────────────────────────────────────────
        public void ShowPause()
        {
            if (_pausePanel != null) _pausePanel.SetActive(true);
            if (_pauseText  != null) _pauseText.text = "PAUSED\n\n[ESC] 继续\n[R] 重新开始";
        }

        public void HidePause()
        {
            if (_pausePanel != null) _pausePanel.SetActive(false);
        }
    }
}

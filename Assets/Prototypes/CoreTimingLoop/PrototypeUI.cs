// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the core timing loop feel fun, tense, and satisfying?
// Date: 2026-03-28

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Prototype.CoreTimingLoop
{
    public class PrototypeUI : MonoBehaviour
    {
        [Header("Colors")]
        public Color PerfectColor = Color.yellow;
        public Color GoodColor = Color.white;
        public Color MissColor = Color.red;

        [Header("Look Ahead")]
        public float LookAheadMs = 3000f;

        // UI elements created at runtime
        private Text _bloatText;
        private Text _suspicionText;
        private Text _scoreText;
        private Text _comboText;
        private Text _judgmentText;
        private Text _timeText;
        private Text _gameOverText;
        private Image _bloatBar;
        private Image _suspicionBar;
        private Image _bloatBarBg;
        private Image _suspicionBarBg;
        private RectTransform _trackArea;
        private Image _judgmentLine;
        private List<Image> _accentMarkers = new List<Image>();
        private float _judgmentFadeTimer;
        private bool _holdingJudgment; // true while hold note is active — don't fade
        private Canvas _canvas;

        private readonly List<AccentJson> _accentBuffer = new List<AccentJson>();

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            if (GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }
            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            BuildUI();
        }

        private void BuildUI()
        {
            // Bloat bar (left side) — background
            _bloatBarBg = CreateBar("BloatBg", new Vector2(40, 300), new Vector2(50, 0),
                new Color(0.2f, 0.2f, 0.2f, 0.8f));
            // Bloat fill — child of background, anchor-stretched from bottom
            _bloatBar = CreateFillBar("BloatFill", _bloatBarBg, Color.green);
            // Label below bloat bar
            _bloatText = CreateText("BloatLabel", "BLOAT", 14, new Vector2(50, -175));
            _bloatText.alignment = TextAnchor.UpperCenter;

            // Suspicion bar (right side) — background
            _suspicionBarBg = CreateBar("SuspBg", new Vector2(40, 300), new Vector2(-50, 0),
                new Color(0.2f, 0.2f, 0.2f, 0.8f));
            _suspicionBarBg.rectTransform.anchorMin = new Vector2(1, 0.5f);
            _suspicionBarBg.rectTransform.anchorMax = new Vector2(1, 0.5f);
            // Suspicion fill — child of background, anchor-stretched from bottom
            _suspicionBar = CreateFillBar("SuspFill", _suspicionBarBg, new Color(0.6f, 0.2f, 0.8f));
            // Label below suspicion bar
            _suspicionText = CreateText("SuspLabel", "SUSPICION", 14, new Vector2(-50, -175));
            _suspicionText.rectTransform.anchorMin = new Vector2(1, 0.5f);
            _suspicionText.rectTransform.anchorMax = new Vector2(1, 0.5f);
            _suspicionText.alignment = TextAnchor.UpperCenter;

            // Track area (center-bottom)
            var trackObj = new GameObject("TrackArea");
            trackObj.transform.SetParent(transform, false);
            _trackArea = trackObj.AddComponent<RectTransform>();
            _trackArea.anchorMin = new Vector2(0.1f, 0.15f);
            _trackArea.anchorMax = new Vector2(0.9f, 0.25f);
            _trackArea.offsetMin = Vector2.zero;
            _trackArea.offsetMax = Vector2.zero;

            // Track background
            var trackBg = trackObj.AddComponent<Image>();
            trackBg.color = new Color(0.1f, 0.1f, 0.15f, 0.7f);

            // Judgment line
            var lineObj = new GameObject("JudgmentLine");
            lineObj.transform.SetParent(_trackArea, false);
            _judgmentLine = lineObj.AddComponent<Image>();
            _judgmentLine.color = new Color(1f, 1f, 1f, 0.8f);
            var lineRT = _judgmentLine.rectTransform;
            lineRT.anchorMin = new Vector2(0.15f, 0);
            lineRT.anchorMax = new Vector2(0.15f, 1);
            lineRT.sizeDelta = new Vector2(3, 0);

            // Score (top center)
            _scoreText = CreateText("Score", "Score: 0", 24, new Vector2(0, -40));
            _scoreText.rectTransform.anchorMin = new Vector2(0.5f, 1);
            _scoreText.rectTransform.anchorMax = new Vector2(0.5f, 1);
            _scoreText.alignment = TextAnchor.UpperCenter;

            // Combo (top right-ish)
            _comboText = CreateText("Combo", "", 20, new Vector2(200, -40));
            _comboText.rectTransform.anchorMin = new Vector2(0.5f, 1);
            _comboText.rectTransform.anchorMax = new Vector2(0.5f, 1);

            // Time
            _timeText = CreateText("Time", "0:00", 16, new Vector2(-200, -40));
            _timeText.rectTransform.anchorMin = new Vector2(0.5f, 1);
            _timeText.rectTransform.anchorMax = new Vector2(0.5f, 1);

            // Judgment popup (center)
            _judgmentText = CreateText("Judgment", "", 48, new Vector2(0, 80));
            _judgmentText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _judgmentText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _judgmentText.alignment = TextAnchor.MiddleCenter;

            // Game over (hidden)
            _gameOverText = CreateText("GameOver", "", 36, Vector2.zero);
            _gameOverText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _gameOverText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _gameOverText.alignment = TextAnchor.MiddleCenter;
            _gameOverText.gameObject.SetActive(false);
        }

        public void UpdateState(float currentTimeMs, BeatMapData beatMap,
            PrototypeBloatGauge bloat, PrototypeSuspicionMeter suspicion,
            PrototypeTimingJudge judge)
        {
            // Bloat bar — stretch fill via anchorMax.y
            SetFillBarAmount(_bloatBar, bloat.NormalizedBloat);
            _bloatBar.color = bloat.NormalizedBloat < 0.5f ? Color.green :
                              bloat.NormalizedBloat < 0.75f ? Color.yellow :
                              bloat.NormalizedBloat < 0.9f ? new Color(1, 0.5f, 0) : Color.red;
            _bloatText.text = $"BLOAT {bloat.NormalizedBloat * 100:F0}%";

            // Suspicion bar — stretch fill via anchorMax.y
            SetFillBarAmount(_suspicionBar, suspicion.NormalizedSuspicion);
            _suspicionBar.color = Color.Lerp(new Color(0.4f, 0.2f, 0.6f), Color.red,
                suspicion.NormalizedSuspicion);
            _suspicionText.text = $"SUSPICION {suspicion.NormalizedSuspicion * 100:F0}%";

            // Score & combo
            _scoreText.text = $"Score: {judge.TotalScore}";
            _comboText.text = judge.CurrentCombo > 1 ? $"{judge.CurrentCombo} Combo!" : "";
            int seconds = Mathf.FloorToInt(currentTimeMs / 1000f);
            _timeText.text = $"{seconds / 60}:{seconds % 60:D2}";

            // Judgment fade — skip during active hold
            if (_holdingJudgment)
            {
                // Keep text fully visible while holding
                var c = _judgmentText.color;
                c.a = 1f;
                _judgmentText.color = c;
            }
            else if (_judgmentFadeTimer > 0)
            {
                _judgmentFadeTimer -= Time.deltaTime;
                var c = _judgmentText.color;
                c.a = Mathf.Clamp01(_judgmentFadeTimer / 0.3f);
                _judgmentText.color = c;
            }

            // Accent markers on track
            UpdateAccentMarkers(currentTimeMs, beatMap);
        }

        private void UpdateAccentMarkers(float currentTimeMs, BeatMapData beatMap)
        {
            beatMap.GetAccentsInRange(currentTimeMs, currentTimeMs + LookAheadMs, _accentBuffer);

            // Reuse or create markers
            while (_accentMarkers.Count < _accentBuffer.Count)
            {
                var markerObj = new GameObject($"Accent{_accentMarkers.Count}");
                markerObj.transform.SetParent(_trackArea, false);
                var img = markerObj.AddComponent<Image>();
                img.color = Color.cyan;
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0, 0.2f);
                rt.anchorMax = new Vector2(0, 0.8f);
                rt.sizeDelta = new Vector2(12, 0);
                _accentMarkers.Add(img);
            }

            for (int i = 0; i < _accentMarkers.Count; i++)
            {
                if (i < _accentBuffer.Count)
                {
                    _accentMarkers[i].gameObject.SetActive(true);
                    var accent = _accentBuffer[i];
                    float normalizedStart = (accent.timeMs - currentTimeMs) / LookAheadMs;
                    float anchorXStart = 0.15f + normalizedStart * 0.85f;

                    var rt = _accentMarkers[i].rectTransform;

                    if (accent.IsHold)
                    {
                        // Hold note: draw as a wide bar spanning press→release
                        // Clamp left edge to judgment line so the bar stays visible while holding
                        float normalizedEnd = (accent.ReleaseTimeMs - currentTimeMs) / LookAheadMs;
                        float anchorXEnd = Mathf.Clamp(0.15f + normalizedEnd * 0.85f, 0.15f, 1f);
                        float clampedStart = Mathf.Max(anchorXStart, 0.15f);
                        rt.anchorMin = new Vector2(clampedStart, 0.15f);
                        rt.anchorMax = new Vector2(anchorXEnd, 0.85f);
                        rt.sizeDelta = Vector2.zero;
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }
                    else
                    {
                        // Tap note: thin vertical line
                        rt.anchorMin = new Vector2(anchorXStart, 0.1f);
                        rt.anchorMax = new Vector2(anchorXStart, 0.9f);
                        float sizeScale = accent.intensity switch
                        {
                            "fortissimo" => 16f,
                            "forte" => 13f,
                            "mezzo" => 10f,
                            "piano" => 8f,
                            _ => 10f
                        };
                        rt.sizeDelta = new Vector2(sizeScale, 0);
                    }

                    // Color by intensity, hold notes slightly more transparent
                    Color baseColor = accent.intensity switch
                    {
                        "fortissimo" => new Color(1f, 0.9f, 0.3f, 0.9f),
                        "forte" => new Color(0.9f, 0.8f, 0.4f, 0.8f),
                        "mezzo" => new Color(0.8f, 0.8f, 0.8f, 0.7f),
                        "piano" => new Color(0.6f, 0.6f, 0.7f, 0.5f),
                        _ => Color.cyan
                    };
                    if (accent.IsHold) baseColor.a *= 0.6f;
                    _accentMarkers[i].color = baseColor;
                }
                else
                {
                    _accentMarkers[i].gameObject.SetActive(false);
                }
            }
        }

        public void ShowJudgment(JudgmentResult result)
        {
            string text;
            Color color;

            switch (result.Tier)
            {
                case JudgmentTier.Perfect:
                    text = "Perfect!";
                    color = PerfectColor;
                    break;
                case JudgmentTier.Good:
                    text = "Good";
                    color = GoodColor;
                    break;
                default:
                    text = "Exposed!";
                    color = MissColor;
                    break;
            }

            if (result.IsHoldStart)
            {
                // Hold press: show and keep visible until release
                text = "Hold... " + text;
                _holdingJudgment = true;
                _judgmentFadeTimer = 0;
            }
            else if (result.IsHoldEnd)
            {
                // Hold release: show final result and start fade
                text = "Release " + text;
                _holdingJudgment = false;
                _judgmentFadeTimer = 0.8f;
            }
            else
            {
                // Tap: normal fade
                _holdingJudgment = false;
                _judgmentFadeTimer = 0.5f;
            }

            if (!result.WasOutsideWindow && result.Accent != null)
            {
                string timing = result.DeltaMs < -5 ? " ← 早了" :
                                result.DeltaMs > 5 ? " → 晚了" : "";
                text += timing;
            }

            _judgmentText.text = text;
            _judgmentText.color = color;
        }

        public void ShowGameOver(string reason, PrototypeTimingJudge judge)
        {
            _gameOverText.gameObject.SetActive(true);
            _gameOverText.text = $"{reason}\n\n" +
                $"Score: {judge.TotalScore}\n" +
                $"Perfect: {judge.PerfectCount} | Good: {judge.GoodCount} | Miss: {judge.MissCount}\n" +
                $"Max Combo: {judge.MaxCombo}\n\n" +
                $"[按 R 重试]";
            _gameOverText.color = Color.red;
        }

        public void ShowResults(string rating, PrototypeTimingJudge judge,
            PrototypeSuspicionMeter suspicion)
        {
            _gameOverText.gameObject.SetActive(true);
            _gameOverText.text = $"演出完成！\n\n" +
                $"评级: {rating}\n" +
                $"Score: {judge.TotalScore}\n" +
                $"Perfect: {judge.PerfectCount} | Good: {judge.GoodCount} | Miss: {judge.MissCount}\n" +
                $"Max Combo: {judge.MaxCombo}\n" +
                $"Peak Suspicion: {suspicion.PeakSuspicion:F0}%\n\n" +
                $"[按 R 重试]";
            _gameOverText.color = rating == "S" ? Color.yellow :
                                  rating == "A" ? Color.white : Color.gray;
        }

        private Text CreateText(string name, string content, int fontSize, Vector2 pos)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var text = obj.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            var rt = text.rectTransform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(400, 60);
            return text;
        }

        private Image CreateBar(string name, Vector2 size, Vector2 pos, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var img = obj.AddComponent<Image>();
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return img;
        }

        private Image CreateFillBar(string name, Image parent, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform, false);
            var img = obj.AddComponent<Image>();
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.offsetMin = new Vector2(2, 2);
            rt.offsetMax = new Vector2(-2, 0);
            return img;
        }

        private void SetFillBarAmount(Image fillBar, float normalized)
        {
            var rt = fillBar.rectTransform;
            rt.anchorMax = new Vector2(1, Mathf.Clamp01(normalized));
            rt.offsetMax = new Vector2(-2, -2);
        }
    }
}

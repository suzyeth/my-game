using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FartSymphony.Core;
using FartSymphony.Gameplay;
using FartSymphony.UI;

namespace FartSymphony.Editor
{
    /// <summary>
    /// Builds the Sprint 2 development scene with all systems and art assets wired.
    /// Menu: FartSymphony → Build Sprint2Dev Scene
    /// </summary>
    public static class Sprint2SceneBuilder
    {
        // Asset paths (Unicode escapes to avoid source-file encoding issues)
        private const string PATH_BG           = "Assets/art/\u6F14\u594F\u72B6\u6001\u5E95\u56FE.png";
        private const string PATH_TRACK        = "Assets/art/UI/\u97F3\u7B26\u8F68\u9053.png";
        private const string PATH_JUDGE_POINT  = "Assets/art/UI/\u97F3\u7B26\u8F68\u9053\u5224\u5B9A\u70B9.png";
        private const string PATH_NOTE_NORMAL  = "Assets/art/\u6F14\u594F\u666E\u901A\u97F3\u7B26\u6548\u679C\u56FE.png";
        private const string PATH_NOTE_FART    = "Assets/art/\u6F14\u594F\u653E\u5C41\u97F3\u7B26\u6548\u679C\u56FE.png";
        private const string PATH_BLOAT_BG     = "Assets/art/UI/\u5C41\u503C\u6761\u5E95\u56FE.png";
        private const string PATH_BLOAT_FILL   = "Assets/art/UI/\u5C41\u503C\u6761\u80FD\u91CF.png";
        private const string PATH_BLOAT_TOP    = "Assets/art/UI/\u5C41\u503C\u6761\u9876\u56FE.png";

        [MenuItem("FartSymphony/Build Sprint2Dev Scene")]
        public static void Build()
        {
            // 1. Ensure all PNGs are imported as Sprite type
            SetSprite(PATH_BG);
            SetSprite(PATH_TRACK);
            SetSprite(PATH_JUDGE_POINT);
            SetSprite(PATH_NOTE_NORMAL);
            SetSprite(PATH_NOTE_FART);
            SetSprite(PATH_BLOAT_BG);
            SetSprite(PATH_BLOAT_FILL);
            SetSprite(PATH_BLOAT_TOP);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();

            // 2. Load sprites
            var bgSprite         = LoadSprite(PATH_BG);
            var trackSprite      = LoadSprite(PATH_TRACK);
            var judgePtSprite    = LoadSprite(PATH_JUDGE_POINT);
            var noteSprite       = LoadSprite(PATH_NOTE_NORMAL);
            var bloatBgSprite    = LoadSprite(PATH_BLOAT_BG);
            var bloatFillSprite  = LoadSprite(PATH_BLOAT_FILL);
            var bloatTopSprite   = LoadSprite(PATH_BLOAT_TOP);

            // 3. Create fresh scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Camera ───────────────────────────────────────────────────────
            var camGo = new GameObject("Main Camera");
            var cam   = camGo.AddComponent<Camera>();
            cam.tag            = "MainCamera";
            cam.backgroundColor= Color.black;
            cam.clearFlags     = CameraClearFlags.SolidColor;
            cam.orthographic   = true;
            camGo.AddComponent<AudioListener>();

            // ── Directional Light ────────────────────────────────────────────
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var dl = lightGo.AddComponent<Light>();
            dl.type  = LightType.Directional;
            dl.color = Color.white;

            // ── Canvas ───────────────────────────────────────────────────────
            var canvasGo = new GameObject("Canvas");
            var canvas   = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode    = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Background (fill entire canvas)
            var bgGo  = CreateImage(canvasGo.transform, "Background", bgSprite);
            FillParent(bgGo.GetComponent<RectTransform>());

            // Vignette overlay (semi-transparent black, starts at alpha=0)
            var vigGo  = CreateImage(canvasGo.transform, "Vignette", null);
            var vigImg = vigGo.GetComponent<Image>();
            vigImg.color = new Color(0f, 0f, 0f, 0f);
            FillParent(vigGo.GetComponent<RectTransform>());

            // Note Track (horizontal bar across the screen middle)
            var trackGo   = CreateImage(canvasGo.transform, "NoteTrack", trackSprite);
            var trackRect = trackGo.GetComponent<RectTransform>();
            trackRect.anchorMin       = new Vector2(0f, 0.45f);
            trackRect.anchorMax       = new Vector2(1f, 0.55f);
            trackRect.offsetMin       = Vector2.zero;
            trackRect.offsetMax       = Vector2.zero;
            var trackImg = trackGo.GetComponent<Image>();
            trackImg.type = Image.Type.Tiled;

            // Judgment line indicator (thin vertical bar at 15% from left)
            var judgeGo   = CreateImage(trackGo.transform, "JudgmentLine", judgePtSprite);
            var judgeRect = judgeGo.GetComponent<RectTransform>();
            judgeRect.anchorMin       = new Vector2(0.15f, 0f);
            judgeRect.anchorMax       = new Vector2(0.15f, 1f);
            judgeRect.pivot           = new Vector2(0.5f, 0.5f);
            judgeRect.sizeDelta       = new Vector2(6f, 0f);
            judgeRect.anchoredPosition= Vector2.zero;
            var judgeImg = judgeGo.GetComponent<Image>();
            if (judgePtSprite == null) judgeImg.color = new Color(1f, 0.85f, 0f, 0.9f);

            // Note prefab (hidden — used as pool source by VisualCueSystem)
            var notePrefabGo   = CreateImage(trackGo.transform, "NotePrefab", noteSprite);
            var notePrefabRect = notePrefabGo.GetComponent<RectTransform>();
            notePrefabRect.anchorMin       = new Vector2(0f, 0.5f);
            notePrefabRect.anchorMax       = new Vector2(0f, 0.5f);
            notePrefabRect.pivot           = new Vector2(0.5f, 0.5f);
            notePrefabRect.sizeDelta       = new Vector2(48f, 48f);
            notePrefabRect.anchoredPosition= Vector2.zero;
            notePrefabGo.SetActive(false);

            // ── Bloat Gauge (right side, vertical fill) ──────────────────────
            var bloatContGo   = new GameObject("BloatGaugeContainer");
            bloatContGo.transform.SetParent(canvasGo.transform, false);
            var bloatContRect = bloatContGo.AddComponent<RectTransform>();
            bloatContRect.anchorMin       = new Vector2(0.90f, 0.10f);
            bloatContRect.anchorMax       = new Vector2(0.97f, 0.90f);
            bloatContRect.offsetMin       = Vector2.zero;
            bloatContRect.offsetMax       = Vector2.zero;

            var bloatBgGo  = CreateImage(bloatContGo.transform, "BloatBackground", bloatBgSprite);
            FillParent(bloatBgGo.GetComponent<RectTransform>());

            var bloatFillGo  = CreateImage(bloatContGo.transform, "BloatFill", bloatFillSprite);
            var bloatFillImg = bloatFillGo.GetComponent<Image>();
            bloatFillImg.type        = Image.Type.Filled;
            bloatFillImg.fillMethod  = Image.FillMethod.Vertical;
            bloatFillImg.fillOrigin  = (int)Image.OriginVertical.Bottom;
            bloatFillImg.fillAmount  = 0f;
            bloatFillImg.color       = new Color(0.2f, 0.8f, 0.2f, 1f);
            FillParent(bloatFillGo.GetComponent<RectTransform>());

            var bloatTopGo = CreateImage(bloatContGo.transform, "BloatTop", bloatTopSprite);
            FillParent(bloatTopGo.GetComponent<RectTransform>());

            // ── Judgment Popup Text ───────────────────────────────────────────
            var popupGo   = new GameObject("PopupText");
            popupGo.transform.SetParent(canvasGo.transform, false);
            var popupText = popupGo.AddComponent<Text>();
            popupText.text      = "Perfect!";
            popupText.fontSize  = 72;
            popupText.fontStyle = FontStyle.Bold;
            popupText.alignment = TextAnchor.MiddleCenter;
            popupText.color     = Color.white;
            popupText.resizeTextForBestFit = false;
            var popupRect = popupGo.GetComponent<RectTransform>();
            popupRect.anchorMin       = new Vector2(0.25f, 0.55f);
            popupRect.anchorMax       = new Vector2(0.75f, 0.75f);
            popupRect.offsetMin       = Vector2.zero;
            popupRect.offsetMax       = Vector2.zero;
            popupGo.SetActive(false);

            // ── EventSystem ──────────────────────────────────────────────────
            // Project uses New Input System package — use InputSystemUIInputModule,
            // not the legacy StandaloneInputModule.
            var evtGo = new GameObject("EventSystem");
            evtGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            evtGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // ── Game Systems ─────────────────────────────────────────────────
            var systemsGo = new GameObject("GameSystems");

            var bmlGo  = new GameObject("BeatMapLoader");
            bmlGo.transform.SetParent(systemsGo.transform);
            var beatMapLoader = bmlGo.AddComponent<BeatMapLoader>();

            var isGo  = new GameObject("InputSystem");
            isGo.transform.SetParent(systemsGo.transform);
            var inputSystem = isGo.AddComponent<InputSystem>();

            var amGo  = new GameObject("AudioManager");
            amGo.transform.SetParent(systemsGo.transform);
            var audioManager = amGo.AddComponent<AudioManager>();

            var tjGo  = new GameObject("TimingJudgment");
            tjGo.transform.SetParent(systemsGo.transform);
            var timingJudgment = tjGo.AddComponent<TimingJudgment>();

            var bgaugGo = new GameObject("BloatGauge");
            bgaugGo.transform.SetParent(systemsGo.transform);
            var bloatGauge = bgaugGo.AddComponent<BloatGauge>();

            var smGo  = new GameObject("SuspicionMeter");
            smGo.transform.SetParent(systemsGo.transform);
            var suspicionMeter = smGo.AddComponent<SuspicionMeter>();

            var sarGo = new GameObject("ScoreAndRating");
            sarGo.transform.SetParent(systemsGo.transform);
            var scoreAndRating = sarGo.AddComponent<ScoreAndRating>();

            // VisualCueSystem lives on the Canvas root
            var visualCueSystem = canvasGo.AddComponent<VisualCueSystem>();

            var lfmGo = new GameObject("LevelFlowManager");
            var lfm   = lfmGo.AddComponent<LevelFlowManager>();

            // ── Wire VisualCueSystem ──────────────────────────────────────────
            var vcsSO = new SerializedObject(visualCueSystem);
            vcsSO.FindProperty("_trackRect")     .objectReferenceValue = trackRect;
            vcsSO.FindProperty("_notePrefab")    .objectReferenceValue = notePrefabGo;
            vcsSO.FindProperty("_popupText")     .objectReferenceValue = popupText;
            vcsSO.FindProperty("_bloatFillImage").objectReferenceValue = bloatFillImg;
            vcsSO.FindProperty("_vignetteImage") .objectReferenceValue = vigImg;
            vcsSO.FindProperty("_timingJudgment").objectReferenceValue = timingJudgment;
            vcsSO.FindProperty("_bloatGauge")    .objectReferenceValue = bloatGauge;
            vcsSO.FindProperty("_suspicionMeter").objectReferenceValue = suspicionMeter;
            vcsSO.FindProperty("_beatMapLoader") .objectReferenceValue = beatMapLoader;
            vcsSO.FindProperty("_audioManager")  .objectReferenceValue = audioManager;
            vcsSO.ApplyModifiedProperties();

            // ── Wire LevelFlowManager ─────────────────────────────────────────
            var lfmSO = new SerializedObject(lfm);
            lfmSO.FindProperty("_beatMapLoader")  .objectReferenceValue = beatMapLoader;
            lfmSO.FindProperty("_audioManager")   .objectReferenceValue = audioManager;
            lfmSO.FindProperty("_inputSystem")    .objectReferenceValue = inputSystem;
            lfmSO.FindProperty("_timingJudgment") .objectReferenceValue = timingJudgment;
            lfmSO.FindProperty("_bloatGauge")     .objectReferenceValue = bloatGauge;
            lfmSO.FindProperty("_suspicionMeter") .objectReferenceValue = suspicionMeter;
            lfmSO.FindProperty("_scoreAndRating") .objectReferenceValue = scoreAndRating;
            lfmSO.FindProperty("_visualCueSystem").objectReferenceValue = visualCueSystem;
            lfmSO.ApplyModifiedProperties();

            // ── Wire TimingJudgment ───────────────────────────────────────────
            var tjSO = new SerializedObject(timingJudgment);
            tjSO.FindProperty("_inputSystem").objectReferenceValue = inputSystem;
            tjSO.ApplyModifiedProperties();

            // ── Save scene ────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Sprint2Dev.unity");
            AssetDatabase.Refresh();

            Debug.Log("[Sprint2SceneBuilder] Done! Scene saved to Assets/Scenes/Sprint2Dev.unity");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SetSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[Sprint2SceneBuilder] TextureImporter not found: {path}");
                return;
            }

            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }
            // Ensure Single sprite mode so LoadAssetAtPath<Sprite> works
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        private static Sprite LoadSprite(string path)
        {
            // Try direct load (Single sprite mode)
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null) return s;

            // Fallback: scan all sub-assets (Multiple sprite mode)
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                if (obj is Sprite sp) return sp;

            Debug.LogWarning($"[Sprint2SceneBuilder] Sprite not found at: {path}");
            return null;
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                Debug.LogWarning($"[Sprint2SceneBuilder] Asset not found at: {path}");
            return asset;
        }

        private static GameObject CreateImage(Transform parent, string name, Sprite sprite)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            if (sprite != null) img.sprite = sprite;
            return go;
        }

        private static void FillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}

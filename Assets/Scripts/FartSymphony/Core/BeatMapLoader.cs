using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FartSymphony.Core
{
    /// <summary>
    /// Loads a beat-map JSON file from StreamingAssets/BeatMaps/ and constructs
    /// an immutable <see cref="BeatMapData"/> instance.
    ///
    /// On success: <see cref="Data"/> is set and <see cref="IsReady"/> is true.
    /// On failure: <see cref="Error"/> describes the problem and <see cref="OnBeatMapLoadError"/> fires.
    ///
    /// File naming convention: {composer}_{symphony}_{movement}.json
    /// e.g. beethoven_5_mvt1.json
    /// </summary>
    public sealed class BeatMapLoader : MonoBehaviour
    {
        [Header("Beat Map")]
        [Tooltip("File name inside StreamingAssets/BeatMaps/ (e.g. beethoven_5_mvt1.json)")]
        [SerializeField] private string _beatMapFileName = "beethoven_5_mvt1.json";

        [Header("Validation")]
        [Tooltip("Default window width (ms) applied when an accent's windowMs is missing or <= 0.")]
        [SerializeField] [Range(50f, 500f)] private float _defaultWindowMs = 200f;

        [Tooltip("Max allowed deviation between declared durationMs and last accent timeMs.")]
        [SerializeField] [Range(50f, 500f)] private float _durationMismatchToleranceMs = 100f;

        // ── Runtime state ─────────────────────────────────────────────────────
        public BeatMapData Data    { get; private set; }
        public bool        IsReady { get; private set; }
        public string      Error   { get; private set; }

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fired when a load attempt fails. Argument is the file path that failed.</summary>
        public event Action<string> OnBeatMapLoadError;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Synchronously loads the beat map from StreamingAssets.
        /// Safe to call from Awake/Start on the main thread.
        /// </summary>
        public void Load()
        {
            IsReady = false;
            Error   = null;
            Data    = null;

            string path = Path.Combine(Application.streamingAssetsPath, "BeatMaps", _beatMapFileName);

            // ── 1. Read file ──────────────────────────────────────────────────
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                FailWith(path, $"Cannot read file: {e.Message}");
                return;
            }

            // ── 2. Deserialise ────────────────────────────────────────────────
            BeatMapJson raw;
            try
            {
                raw = JsonUtility.FromJson<BeatMapJson>(json);
            }
            catch (Exception e)
            {
                FailWith(path, $"JSON parse error: {e.Message}");
                return;
            }

            // ── 3. Validate required fields ───────────────────────────────────
            if (raw == null || raw.meta == null)
            {
                FailWith(path, "Missing 'meta' object.");
                return;
            }
            if (string.IsNullOrEmpty(raw.meta.audioFile))
            {
                FailWith(path, "meta.audioFile is empty or missing.");
                return;
            }
            if (raw.meta.durationMs <= 0f)
            {
                FailWith(path, "meta.durationMs must be > 0.");
                return;
            }
            if (raw.accents == null || raw.accents.Length == 0)
            {
                FailWith(path, "Beat map contains no accents.");
                return;
            }

            // ── 4. Build immutable data arrays ────────────────────────────────
            AccentData[] accents = BuildAccents(raw);
            SectionData[] sections = BuildSections(raw);
            QuietZoneData[] quietZones = BuildQuietZones(raw);

            // ── 5. Cross-validate ─────────────────────────────────────────────
            float lastAccentTime = accents[accents.Length - 1].TimeMs;
            if (lastAccentTime > raw.meta.durationMs + _durationMismatchToleranceMs)
            {
                FailWith(path, $"Accent at {lastAccentTime}ms exceeds declared duration {raw.meta.durationMs}ms.");
                return;
            }

            // ── 6. Construct BeatMapData ──────────────────────────────────────
            Data    = new BeatMapData(raw.meta.title, raw.meta.composer, raw.meta.audioFile,
                                      raw.meta.durationMs, raw.meta.bpm,
                                      accents, sections, quietZones);
            IsReady = true;

            Debug.Log($"[BeatMapLoader] Loaded \"{Data.Title}\" — " +
                      $"{accents.Length} accents, {sections.Length} sections, " +
                      $"{quietZones.Length} quiet zones, {Data.DurationMs:F0}ms");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private AccentData[] BuildAccents(BeatMapJson raw)
        {
            var result = new AccentData[raw.accents.Length];
            for (int i = 0; i < raw.accents.Length; i++)
            {
                var a = raw.accents[i];
                float window = a.windowMs > 0f ? a.windowMs : _defaultWindowMs;
                result[i] = new AccentData(a.timeMs, a.intensity ?? "", window, a.type ?? "");
            }
            Array.Sort(result, (x, y) => x.TimeMs.CompareTo(y.TimeMs));
            return result;
        }

        private SectionData[] BuildSections(BeatMapJson raw)
        {
            if (raw.sections == null || raw.sections.Length == 0)
                return Array.Empty<SectionData>();

            var result = new SectionData[raw.sections.Length];
            for (int i = 0; i < raw.sections.Length; i++)
            {
                var s = raw.sections[i];
                result[i] = new SectionData(s.name ?? "", s.startMs, s.endMs, s.dynamicLevel ?? "");
            }
            Array.Sort(result, (x, y) => x.StartMs.CompareTo(y.StartMs));
            return result;
        }

        private QuietZoneData[] BuildQuietZones(BeatMapJson raw)
        {
            if (raw.quietZones == null || raw.quietZones.Length == 0)
                return Array.Empty<QuietZoneData>();

            var result = new QuietZoneData[raw.quietZones.Length];
            for (int i = 0; i < raw.quietZones.Length; i++)
            {
                var q = raw.quietZones[i];
                result[i] = new QuietZoneData(q.startMs, q.endMs, q.dangerLevel ?? "");
            }
            Array.Sort(result, (x, y) => x.StartMs.CompareTo(y.StartMs));
            return result;
        }

        private void FailWith(string path, string message)
        {
            Error = message;
            Debug.LogError($"[BeatMapLoader] Load failed ({path}): {message}");
            OnBeatMapLoadError?.Invoke(path);
        }

        // ── JSON schema (internal; matches StreamingAssets JSON format) ───────

        [Serializable]
        private class BeatMapJson
        {
            public MetaJson        meta;
            public SectionJson[]   sections;
            public AccentJson[]    accents;
            public QuietZoneJson[] quietZones;
        }

        [Serializable]
        private class MetaJson
        {
            public string title;
            public string composer;
            public string audioFile;
            public float  durationMs;
            public int    bpm;
        }

        [Serializable]
        private class SectionJson
        {
            public string name;
            public float  startMs;
            public float  endMs;
            public string dynamicLevel;
        }

        [Serializable]
        private class AccentJson
        {
            public float  timeMs;
            public string intensity;
            public float  windowMs;
            public string type;
        }

        [Serializable]
        private class QuietZoneJson
        {
            public float  startMs;
            public float  endMs;
            public string dangerLevel;
        }
    }
}

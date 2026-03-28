// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the core timing loop feel fun, tense, and satisfying?
// Date: 2026-03-28

using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Prototype.CoreTimingLoop
{
    [Serializable]
    public class BeatMapJson
    {
        public MetaJson meta;
        public SectionJson[] sections;
        public AccentJson[] accents;
        public QuietZoneJson[] quietZones;
    }

    [Serializable]
    public class MetaJson
    {
        public string title;
        public string composer;
        public string audioFile;
        public float durationMs;
        public int bpm;
        public string timeSignature;
        public int difficulty;
        public int version;
    }

    [Serializable]
    public class SectionJson
    {
        public string name;
        public float startMs;
        public float endMs;
        public string dynamicLevel;
    }

    [Serializable]
    public class AccentJson
    {
        public float timeMs;
        public string intensity;
        public float windowMs;
        public string type;
        public string noteType; // "tap" or "hold"
        public float holdMs;    // duration for hold notes (0 for tap)

        public bool IsHold => noteType == "hold" && holdMs > 0;
        public float ReleaseTimeMs => timeMs + holdMs;
    }

    [Serializable]
    public class QuietZoneJson
    {
        public float startMs;
        public float endMs;
        public string dangerLevel;
    }

    public class BeatMapData
    {
        public BeatMapJson Raw { get; private set; }

        private readonly AccentJson[] _sortedAccents;
        private readonly SectionJson[] _sortedSections;
        private readonly QuietZoneJson[] _sortedQuietZones;
        private int _cachedSectionIndex = -1;

        public BeatMapData(BeatMapJson json)
        {
            Raw = json;
            _sortedAccents = json.accents.OrderBy(a => a.timeMs).ToArray();
            _sortedSections = json.sections.OrderBy(s => s.startMs).ToArray();
            _sortedQuietZones = json.quietZones != null
                ? json.quietZones.OrderBy(q => q.startMs).ToArray()
                : Array.Empty<QuietZoneJson>();
        }

        public AccentJson GetNearestAccent(float timeMs, HashSet<int> consumed)
        {
            int nextIdx = BinarySearchFirstGE(timeMs);
            int prevIdx = nextIdx - 1;

            AccentJson prev = (prevIdx >= 0 && !consumed.Contains(prevIdx))
                ? _sortedAccents[prevIdx] : null;
            AccentJson next = (nextIdx < _sortedAccents.Length && !consumed.Contains(nextIdx))
                ? _sortedAccents[nextIdx] : null;

            if (prev == null && next == null)
            {
                for (int i = 0; i < _sortedAccents.Length; i++)
                {
                    if (!consumed.Contains(i)) return _sortedAccents[i];
                }
                return null;
            }
            if (prev == null) return next;
            if (next == null) return prev;

            float distPrev = timeMs - prev.timeMs;
            float distNext = next.timeMs - timeMs;
            return distPrev <= distNext ? prev : next;
        }

        public int IndexOf(AccentJson accent)
        {
            return Array.IndexOf(_sortedAccents, accent);
        }

        public AccentJson GetAccentAt(int index)
        {
            if (index < 0 || index >= _sortedAccents.Length) return null;
            return _sortedAccents[index];
        }

        public int AccentCount => _sortedAccents.Length;

        public int GetAccentsInRange(float startMs, float endMs, List<AccentJson> buffer)
        {
            buffer.Clear();
            int startIdx = BinarySearchFirstGE(startMs);
            for (int i = startIdx; i < _sortedAccents.Length; i++)
            {
                if (_sortedAccents[i].timeMs > endMs) break;
                buffer.Add(_sortedAccents[i]);
            }
            return buffer.Count;
        }

        public SectionJson GetCurrentSection(float timeMs)
        {
            if (_cachedSectionIndex >= 0 && _cachedSectionIndex < _sortedSections.Length)
            {
                var cached = _sortedSections[_cachedSectionIndex];
                if (timeMs >= cached.startMs && timeMs < cached.endMs)
                    return cached;
            }
            for (int i = 0; i < _sortedSections.Length; i++)
            {
                if (timeMs >= _sortedSections[i].startMs && timeMs < _sortedSections[i].endMs)
                {
                    _cachedSectionIndex = i;
                    return _sortedSections[i];
                }
            }
            return null;
        }

        public bool IsInQuietZone(float timeMs)
        {
            foreach (var qz in _sortedQuietZones)
            {
                if (timeMs >= qz.startMs && timeMs < qz.endMs) return true;
                if (qz.startMs > timeMs) break;
            }
            return false;
        }

        private int BinarySearchFirstGE(float timeMs)
        {
            int lo = 0, hi = _sortedAccents.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_sortedAccents[mid].timeMs < timeMs) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }

    public class PrototypeBeatMapLoader : MonoBehaviour
    {
        public string BeatMapFileName = "test-beatmap.json";

        public BeatMapData Data { get; private set; }
        public bool IsReady { get; private set; }
        public string Error { get; private set; }

        public void Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "BeatMaps", BeatMapFileName);
            try
            {
                string json = File.ReadAllText(path);
                var raw = JsonUtility.FromJson<BeatMapJson>(json);
                if (raw.accents == null || raw.accents.Length == 0)
                {
                    Error = "Beat map has no accents";
                    return;
                }
                Data = new BeatMapData(raw);
                IsReady = true;
                Debug.Log($"[Prototype] Beat map loaded: {raw.meta.title}, {raw.accents.Length} accents");
            }
            catch (Exception e)
            {
                Error = e.Message;
                Debug.LogError($"[Prototype] Beat map load failed: {e.Message}");
            }
        }
    }
}

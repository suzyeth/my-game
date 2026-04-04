using System;
using System.Collections.Generic;

namespace FartSymphony.Core
{
    /// <summary>
    /// Immutable runtime representation of a loaded beat map.
    /// All query methods are O(log n) binary search or O(1) amortized (section cache).
    /// Accent arrays are sorted by TimeMs at construction time.
    /// </summary>
    public sealed class BeatMapData
    {
        // ── Meta ──────────────────────────────────────────────────────────────
        public string Title      { get; }
        public string Composer   { get; }
        public string AudioFile  { get; }
        public float  DurationMs { get; }
        public int    Bpm        { get; }

        // ── Sorted data arrays (immutable after construction) ─────────────────
        private readonly AccentData[]    _accents;
        private readonly SectionData[]   _sections;
        private readonly QuietZoneData[] _quietZones;

        // Mutable cache only — not part of the observable state
        private int _cachedSectionIndex;

        public int AccentCount => _accents.Length;

        // ── Construction ──────────────────────────────────────────────────────

        public BeatMapData(
            string          title,
            string          composer,
            string          audioFile,
            float           durationMs,
            int             bpm,
            AccentData[]    accents,
            SectionData[]   sections,
            QuietZoneData[] quietZones)
        {
            Title       = title;
            Composer    = composer;
            AudioFile   = audioFile;
            DurationMs  = durationMs;
            Bpm         = bpm;
            _accents    = accents    ?? Array.Empty<AccentData>();
            _sections   = sections   ?? Array.Empty<SectionData>();
            _quietZones = quietZones ?? Array.Empty<QuietZoneData>();
            _cachedSectionIndex = 0;
        }

        // ── Accent queries ────────────────────────────────────────────────────

        /// <summary>
        /// Returns accent at index, or null if index is out of range.
        /// </summary>
        public AccentData? GetAccentAt(int index)
        {
            if (index < 0 || index >= _accents.Length) return null;
            return _accents[index];
        }

        /// <summary>
        /// Finds the nearest unconsumed accent whose window contains <paramref name="currentTimeMs"/>.
        /// Returns null if no such accent exists.
        /// <paramref name="accentIndex"/> is set to the accent's index in the sorted array, or -1.
        /// </summary>
        public AccentData? GetNearestAccent(double currentTimeMs, HashSet<int> consumed, out int accentIndex)
        {
            accentIndex = -1;
            float timeF   = (float)currentTimeMs;
            int   nextIdx = BinarySearchFirstGE(timeF);
            int   prevIdx = nextIdx - 1;

            bool prevValid = prevIdx >= 0                 && !consumed.Contains(prevIdx);
            bool nextValid = nextIdx < _accents.Length    && !consumed.Contains(nextIdx);

            if (!prevValid && !nextValid) return null;

            int chosenIdx;
            if      (!prevValid) chosenIdx = nextIdx;
            else if (!nextValid) chosenIdx = prevIdx;
            else
            {
                float distPrev = timeF - _accents[prevIdx].TimeMs;
                float distNext = _accents[nextIdx].TimeMs - timeF;
                chosenIdx = (distPrev <= distNext) ? prevIdx : nextIdx;
            }

            float dist = Math.Abs(timeF - _accents[chosenIdx].TimeMs);
            if (dist > _accents[chosenIdx].HalfWindow) return null; // outside window

            accentIndex = chosenIdx;
            return _accents[chosenIdx];
        }

        /// <summary>
        /// Returns the next accent strictly after <paramref name="currentTimeMs"/>, or null.
        /// Does not consider consumed state — used for VCS look-ahead.
        /// </summary>
        public AccentData? GetNextAccent(float currentTimeMs)
        {
            int idx = BinarySearchFirstGE(currentTimeMs);
            // Skip exact match so "strictly after" is guaranteed
            while (idx < _accents.Length && _accents[idx].TimeMs <= currentTimeMs)
                idx++;
            if (idx >= _accents.Length) return null;
            return _accents[idx];
        }

        /// <summary>
        /// Buffer-fill: writes accents in [startMs, endMs] into <paramref name="buffer"/>.
        /// Returns the number of accents written. Zero heap allocation.
        /// </summary>
        public int GetAccentsInRange(float startMs, float endMs, AccentData[] buffer, int maxCount)
        {
            int count    = 0;
            int startIdx = BinarySearchFirstGE(startMs);
            for (int i = startIdx; i < _accents.Length && count < maxCount; i++)
            {
                if (_accents[i].TimeMs > endMs) break;
                buffer[count++] = _accents[i];
            }
            return count;
        }

        // ── Section queries ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the section whose [StartMs, EndMs) range contains <paramref name="timeMs"/>, or null.
        /// O(1) amortized via a forward-moving cache index.
        /// </summary>
        public SectionData? GetCurrentSection(float timeMs)
        {
            if (_sections.Length == 0) return null;

            // Fast path: check cached section first
            if (_cachedSectionIndex >= 0 && _cachedSectionIndex < _sections.Length)
            {
                ref readonly SectionData c = ref _sections[_cachedSectionIndex];
                if (timeMs >= c.StartMs && timeMs < c.EndMs)
                    return c;
            }

            // Linear scan — sections are few (typically 3-10)
            for (int i = 0; i < _sections.Length; i++)
            {
                if (timeMs >= _sections[i].StartMs && timeMs < _sections[i].EndMs)
                {
                    _cachedSectionIndex = i;
                    return _sections[i];
                }
            }
            return null;
        }

        // ── Quiet-zone query ──────────────────────────────────────────────────

        /// <summary>Returns true if <paramref name="timeMs"/> falls inside any quiet zone.</summary>
        public bool IsInQuietZone(float timeMs)
        {
            for (int i = 0; i < _quietZones.Length; i++)
            {
                if (_quietZones[i].StartMs > timeMs) break; // sorted, early exit
                if (timeMs < _quietZones[i].EndMs)   return true;
            }
            return false;
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        /// <summary>Binary search: first index i where _accents[i].TimeMs >= timeMs.</summary>
        private int BinarySearchFirstGE(float timeMs)
        {
            int lo = 0, hi = _accents.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_accents[mid].TimeMs < timeMs) lo = mid + 1;
                else                               hi = mid;
            }
            return lo;
        }
    }
}

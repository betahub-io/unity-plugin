using System;
using System.Collections.Generic;

namespace BetaHub
{
    /// <summary>
    /// Bookkeeping for the recorder's preview frames: one every interval, the oldest overwritten. Pure;
    /// <see cref="GameRecorder"/> owns the textures in the slots.
    /// </summary>
    public sealed class ThumbnailRing
    {
        public struct Entry
        {
            public int Slot;
            public float Time;
        }

        private readonly float[] _times;
        private readonly float _interval;
        private int _next;

        public int Capacity => _times.Length;
        public int Count { get; private set; }

        public ThumbnailRing(int capacity, float intervalSeconds)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, null);
            if (intervalSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(intervalSeconds), intervalSeconds, null);
            _times = new float[capacity];
            _interval = intervalSeconds;
        }

        public bool IsDue(float now)
        {
            if (Count == 0) return true;
            int newest = (_next - 1 + Capacity) % Capacity;
            return now - _times[newest] >= _interval;
        }

        /// <returns>The slot to write the new frame into.</returns>
        public int Claim(float now)
        {
            int slot = _next;
            _times[slot] = now;
            _next = (_next + 1) % Capacity;
            if (Count < Capacity) Count++;
            return slot;
        }

        /// <summary>Filled slots, oldest first.</summary>
        public IEnumerable<Entry> Ordered()
        {
            int start = Count < Capacity ? 0 : _next;
            for (int i = 0; i < Count; i++)
            {
                int slot = (start + i) % Capacity;
                yield return new Entry { Slot = slot, Time = _times[slot] };
            }
        }

        public void Clear()
        {
            Count = 0;
            _next = 0;
        }
    }
}

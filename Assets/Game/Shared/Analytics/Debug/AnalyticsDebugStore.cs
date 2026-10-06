using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Shared.Analytics.Debugging
{
    public sealed class AnalyticsDebugStore
    {
        private const int DefaultCapacity = 100;

        private readonly List<AnalyticsDebugRecord> records;

        public AnalyticsDebugStore(int capacity = DefaultCapacity)
        {
            Capacity = Math.Max(1, capacity);
            records = new List<AnalyticsDebugRecord>(Capacity);
        }

        public int Count => records.Count;
        public int Capacity { get; }

        public event Action Changed;

        public void Add(AnalyticsDebugRecord record)
        {
            if (record == null)
            {
                return;
            }

            records.Add(record);

            while (records.Count > Capacity)
            {
                records.RemoveAt(0);
            }

            Changed?.Invoke();
        }

        public void Clear()
        {
            if (records.Count == 0)
            {
                return;
            }

            records.Clear();
            Changed?.Invoke();
        }

        public IReadOnlyList<AnalyticsDebugRecord> GetSnapshot()
        {
            return new ReadOnlyCollection<AnalyticsDebugRecord>(
                new List<AnalyticsDebugRecord>(records)
            );
        }
    }
}

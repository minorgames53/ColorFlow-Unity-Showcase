using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Shared.Analytics.Debugging
{
    public sealed class AnalyticsDebugRecord
    {
        public AnalyticsDebugRecord(
            int sequenceNumber,
            DateTime occurredAtUtc,
            float realtimeSinceStartup,
            string eventName,
            IReadOnlyDictionary<string, object> properties,
            IReadOnlyList<AnalyticsDebugProviderResult> providerResults)
        {
            SequenceNumber = sequenceNumber;
            OccurredAtUtc = occurredAtUtc;
            RealtimeSinceStartup = realtimeSinceStartup;
            EventName = eventName;
            Properties = new ReadOnlyDictionary<string, object>(CopyProperties(properties));
            ProviderResults = new ReadOnlyCollection<AnalyticsDebugProviderResult>(CopyProviderResults(providerResults));
        }

        public int SequenceNumber { get; }
        public DateTime OccurredAtUtc { get; }
        public float RealtimeSinceStartup { get; }
        public string EventName { get; }
        public IReadOnlyDictionary<string, object> Properties { get; }
        public IReadOnlyList<AnalyticsDebugProviderResult> ProviderResults { get; }

        private static Dictionary<string, object> CopyProperties(
            IReadOnlyDictionary<string, object> properties)
        {
            Dictionary<string, object> copy = new Dictionary<string, object>();

            if (properties == null)
            {
                return copy;
            }

            foreach (KeyValuePair<string, object> property in properties)
            {
                copy[property.Key] = property.Value;
            }

            return copy;
        }

        private static List<AnalyticsDebugProviderResult> CopyProviderResults(
            IReadOnlyList<AnalyticsDebugProviderResult> providerResults)
        {
            List<AnalyticsDebugProviderResult> copy = new List<AnalyticsDebugProviderResult>();

            if (providerResults == null)
            {
                return copy;
            }

            for (int i = 0; i < providerResults.Count; i++)
            {
                if (providerResults[i] != null)
                {
                    copy.Add(providerResults[i]);
                }
            }

            return copy;
        }
    }
}

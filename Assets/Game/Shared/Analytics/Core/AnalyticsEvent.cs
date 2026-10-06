using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Shared.Analytics.Core
{
    public sealed class AnalyticsEvent
    {
        private static readonly IReadOnlyDictionary<string, object> EmptyProperties =
            new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());

        public AnalyticsEvent(
            string name,
            IReadOnlyDictionary<string, object> properties = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Analytics event name cannot be empty.", nameof(name));
            }

            Name = name;
            Properties = properties == null
                ? EmptyProperties
                : new ReadOnlyDictionary<string, object>(CopyProperties(properties));
        }

        public string Name { get; }
        public IReadOnlyDictionary<string, object> Properties { get; }

        private static Dictionary<string, object> CopyProperties(
            IReadOnlyDictionary<string, object> properties)
        {
            Dictionary<string, object> copy = new Dictionary<string, object>();

            foreach (KeyValuePair<string, object> property in properties)
            {
                copy[property.Key] = property.Value;
            }

            return copy;
        }
    }
}

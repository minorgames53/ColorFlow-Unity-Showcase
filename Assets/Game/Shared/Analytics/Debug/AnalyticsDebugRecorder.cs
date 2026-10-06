using System;
using System.Collections.Generic;
using Game.Shared.Analytics.Core;
using UnityEngine;

namespace Game.Shared.Analytics.Debugging
{
    public static class AnalyticsDebugRecorder
    {
        private static int nextSequenceNumber;

        public static AnalyticsDebugStore Store { get; } = new AnalyticsDebugStore();

        public static void Record(
            AnalyticsEvent analyticsEvent,
            IReadOnlyList<AnalyticsDebugProviderResult> providerResults)
        {
            if (!IsEnabled || analyticsEvent == null)
            {
                return;
            }

            try
            {
                AnalyticsDebugRecord record = new AnalyticsDebugRecord(
                    ++nextSequenceNumber,
                    DateTime.UtcNow,
                    UnityEngine.Time.realtimeSinceStartup,
                    analyticsEvent.Name,
                    analyticsEvent.Properties,
                    providerResults
                );

                Store.Add(record);
            }
            catch
            {
                // Analytics debug data must never interrupt gameplay analytics.
            }
        }

        public static void Clear()
        {
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                Store.Clear();
            }
            catch
            {
                // Analytics debug data must never interrupt gameplay analytics.
            }
        }

        internal static bool IsEnabled =>
            Game.Shared.DeveloperTools.DeveloperPanelAvailability.IsAllowed;
    }
}

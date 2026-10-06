using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Shared.Analytics.Debugging;
using UnityEngine;

namespace Game.Shared.Analytics.Core
{
    public sealed class AnalyticsService
    {
        private readonly List<IAnalyticsProvider> providers;
        private readonly bool enableDebugLogs;
        private object defaultLevelContextOwner;

        public AnalyticsService(
            IEnumerable<IAnalyticsProvider> providers,
            bool initialCollectionEnabled = true,
            bool enableDebugLogs = false)
        {
            this.providers = new List<IAnalyticsProvider>();

            if (providers != null)
            {
                foreach (IAnalyticsProvider provider in providers)
                {
                    if (provider == null || ContainsProviderInstance(provider))
                    {
                        continue;
                    }

                    this.providers.Add(provider);
                }
            }

            Providers = new ReadOnlyCollection<IAnalyticsProvider>(this.providers);
            IsCollectionEnabled = initialCollectionEnabled;
            this.enableDebugLogs = enableDebugLogs;
        }

        public bool IsInitialized { get; private set; }
        public bool IsCollectionEnabled { get; private set; }
        public IReadOnlyList<IAnalyticsProvider> Providers { get; }

        public void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            for (int i = 0; i < providers.Count; i++)
            {
                IAnalyticsProvider provider = providers[i];

                try
                {
                    provider.Initialize();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Analytics provider {GetProviderName(provider)} failed while initializing: {exception}");
                }

                try
                {
                    provider.SetCollectionEnabled(IsCollectionEnabled);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Analytics provider {GetProviderName(provider)} failed while setting collection state: {exception}");
                }
            }

            IsInitialized = true;
        }

        public void Track(
            string eventName,
            IReadOnlyDictionary<string, object> properties = null)
        {
            Track(new AnalyticsEvent(eventName, properties));
        }

        public void Track(AnalyticsEvent analyticsEvent)
        {
            TrackInternal(analyticsEvent, true);
        }

        internal AnalyticsProviderTrackResult TrackWithoutProviderQueue(
            AnalyticsEvent analyticsEvent)
        {
            return TrackInternal(analyticsEvent, false);
        }

        private AnalyticsProviderTrackResult TrackInternal(
            AnalyticsEvent analyticsEvent,
            bool queueWhenProviderNotReady)
        {
            if (analyticsEvent == null)
            {
                throw new ArgumentNullException(nameof(analyticsEvent));
            }

            if (!IsInitialized)
            {
                Initialize();
            }

            LogSourceEventInEditor(analyticsEvent);

            if (!IsCollectionEnabled)
            {
                if (enableDebugLogs)
                {
                    Debug.Log(
                        $"[Analytics][Service][skipped-collection-disabled] event='{analyticsEvent.Name}'.");
                }

                if (AnalyticsDebugRecorder.IsEnabled)
                {
                    AnalyticsDebugRecorder.Record(
                        analyticsEvent,
                        BuildGlobalCollectionDisabledResults()
                    );
                }

                return AnalyticsProviderTrackResult.SkippedCollectionDisabled(
                    "Global analytics collection is disabled.");
            }

            bool shouldRecordDebug = AnalyticsDebugRecorder.IsEnabled;
            List<AnalyticsDebugProviderResult> providerResults = shouldRecordDebug
                ? new List<AnalyticsDebugProviderResult>()
                : null;

            if (providers.Count == 0)
            {
                if (enableDebugLogs)
                {
                    Debug.Log(
                        $"[Analytics][Service][skipped-no-provider] event='{analyticsEvent.Name}'.");
                }

                if (shouldRecordDebug)
                {
                    providerResults.Add(BuildNoProvidersResult());
                    AnalyticsDebugRecorder.Record(analyticsEvent, providerResults);
                }

                return AnalyticsProviderTrackResult.SkippedUnsupported(
                    "No analytics providers are configured.");
            }

            AnalyticsProviderTrackResult aggregateResult = null;
            for (int i = 0; i < providers.Count; i++)
            {
                IAnalyticsProvider provider = providers[i];

                try
                {
                    AnalyticsProviderTrackResult trackResult = provider.Track(
                        analyticsEvent,
                        queueWhenProviderNotReady);
                    aggregateResult = SelectAggregateResult(aggregateResult, trackResult);

                    if (shouldRecordDebug)
                    {
                        AddProviderResultSafely(
                            providerResults,
                            BuildProviderResult(provider, null, trackResult)
                        );
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Analytics provider {GetProviderName(provider)} failed while tracking {analyticsEvent.Name}: {exception}");
                    aggregateResult = SelectAggregateResult(
                        aggregateResult,
                        AnalyticsProviderTrackResult.Error(exception.Message));

                    if (shouldRecordDebug)
                    {
                        AddProviderResultSafely(
                            providerResults,
                            BuildProviderResult(provider, exception)
                        );
                    }
                }
            }

            if (shouldRecordDebug)
            {
                AnalyticsDebugRecorder.Record(analyticsEvent, providerResults);
            }

            return aggregateResult ?? AnalyticsProviderTrackResult.SkippedUnsupported(
                "No provider returned a dispatch result.");
        }

        internal void SetDefaultLevelContext(
            int displayedLevelNumber,
            int internalLevelNumber,
            object owner,
            string source)
        {
            if (displayedLevelNumber <= 0 || internalLevelNumber <= 0)
            {
                ClearDefaultLevelContext(owner, source);
                return;
            }

            defaultLevelContextOwner = owner;
            ApplyDefaultLevelContext(
                displayedLevelNumber,
                internalLevelNumber,
                source);
        }

        internal void RefreshDefaultLevelContext(
            int displayedLevelNumber,
            int internalLevelNumber,
            string source)
        {
            if (displayedLevelNumber <= 0 || internalLevelNumber <= 0)
            {
                return;
            }

            ApplyDefaultLevelContext(
                displayedLevelNumber,
                internalLevelNumber,
                source);
        }

        internal void ClearDefaultLevelContext(object owner, string source)
        {
            if (owner != null && defaultLevelContextOwner != null &&
                !ReferenceEquals(owner, defaultLevelContextOwner))
            {
                if (enableDebugLogs)
                {
                    Debug.Log(
                        $"[Analytics][Service][default-context-clear-ignored] source='{NormalizeSource(source)}'; a newer owner is active.");
                }

                return;
            }

            defaultLevelContextOwner = null;
            for (int i = 0; i < providers.Count; i++)
            {
                try
                {
                    providers[i].ClearDefaultLevelContext(source);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Analytics provider {GetProviderName(providers[i])} failed while clearing default level context: {exception}");
                }
            }
        }

        internal void ResetAnalyticsData(string source = "analytics_reset")
        {
            defaultLevelContextOwner = null;
            for (int i = 0; i < providers.Count; i++)
            {
                try
                {
                    providers[i].ResetAnalyticsData(source);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Analytics provider {GetProviderName(providers[i])} failed while resetting analytics data: {exception}");
                }
            }
        }

        public void SetCollectionEnabled(bool enabled)
        {
            IsCollectionEnabled = enabled;

            for (int i = 0; i < providers.Count; i++)
            {
                IAnalyticsProvider provider = providers[i];

                try
                {
                    provider.SetCollectionEnabled(enabled);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Analytics provider {GetProviderName(provider)} failed while setting collection state: {exception}");
                }
            }
        }

        private bool ContainsProviderInstance(IAnalyticsProvider provider)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                if (ReferenceEquals(providers[i], provider))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetProviderName(IAnalyticsProvider provider)
        {
            return provider == null ? "Unknown" : provider.GetType().Name;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogSourceEventInEditor(AnalyticsEvent analyticsEvent)
        {
            if (!enableDebugLogs || analyticsEvent == null)
            {
                return;
            }

            Debug.Log(
                $"[Analytics][Service][source-payload] event='{analyticsEvent.Name}', parameterCount={analyticsEvent.Properties?.Count ?? 0}. Final Firebase payload is logged by the provider.");
        }

        private void ApplyDefaultLevelContext(
            int displayedLevelNumber,
            int internalLevelNumber,
            string source)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                try
                {
                    providers[i].SetDefaultLevelContext(
                        displayedLevelNumber,
                        internalLevelNumber,
                        source);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Analytics provider {GetProviderName(providers[i])} failed while setting default level context: {exception}");
                }
            }
        }

        private static AnalyticsProviderTrackResult SelectAggregateResult(
            AnalyticsProviderTrackResult current,
            AnalyticsProviderTrackResult candidate)
        {
            if (candidate == null)
            {
                return current;
            }

            if (current == null ||
                candidate.Status == AnalyticsProviderTrackStatus.ForwardedToSdk)
            {
                return candidate;
            }

            return current;
        }

        private static string NormalizeSource(string source)
        {
            return string.IsNullOrWhiteSpace(source) ? "unspecified" : source.Trim();
        }

        private IReadOnlyList<AnalyticsDebugProviderResult> BuildGlobalCollectionDisabledResults()
        {
            List<AnalyticsDebugProviderResult> results = new List<AnalyticsDebugProviderResult>();

            if (providers.Count == 0)
            {
                results.Add(BuildNoProvidersResult());
                return results;
            }

            for (int i = 0; i < providers.Count; i++)
            {
                AddProviderResultSafely(
                    results,
                    BuildProviderResult(
                        providers[i],
                        null,
                        forcedStatus: AnalyticsDebugDispatchStatus.SkippedGlobalCollectionDisabled,
                        forcedDetail: "Global analytics collection is disabled."
                    )
                );
            }

            return results;
        }

        private static AnalyticsDebugProviderResult BuildNoProvidersResult()
        {
            return new AnalyticsDebugProviderResult(
                "No Providers",
                "None",
                false,
                false,
                AnalyticsDebugDispatchStatus.NoProviders,
                AnalyticsProviderDebugState.Unknown,
                0,
                "No analytics providers are configured."
            );
        }

        private static AnalyticsDebugProviderResult BuildProviderResult(
            IAnalyticsProvider provider,
            Exception exception,
            AnalyticsProviderTrackResult trackResult = null,
            AnalyticsDebugDispatchStatus? forcedStatus = null,
            string forcedDetail = null)
        {
            string providerTypeName = GetProviderName(provider);
            IAnalyticsProviderDebugInfo debugInfo = provider as IAnalyticsProviderDebugInfo;

            bool isReady = GetIsReady(provider);
            bool isCollectionEnabled = GetIsCollectionEnabled(provider);
            int pendingEventCount = GetPendingEventCount(debugInfo);
            AnalyticsProviderDebugState providerState = GetProviderDebugState(
                debugInfo,
                isReady,
                isCollectionEnabled
            );

            AnalyticsDebugDispatchStatus dispatchStatus = forcedStatus
                ?? GetDispatchStatus(exception, trackResult);

            string detail = forcedDetail;

            if (string.IsNullOrEmpty(detail) && trackResult != null)
            {
                detail = trackResult.Detail;
            }

            if (string.IsNullOrEmpty(detail))
            {
                detail = exception != null
                    ? exception.Message
                    : GetLastError(debugInfo);
            }

            if (string.IsNullOrEmpty(detail)
                && dispatchStatus == AnalyticsDebugDispatchStatus.SkippedProviderCollectionDisabled)
            {
                detail = "Provider collection is disabled.";
            }

            return new AnalyticsDebugProviderResult(
                GetProviderDisplayName(provider, debugInfo),
                providerTypeName,
                isReady,
                isCollectionEnabled,
                dispatchStatus,
                providerState,
                pendingEventCount,
                detail
            );
        }

        private static AnalyticsDebugDispatchStatus GetDispatchStatus(
            Exception exception,
            AnalyticsProviderTrackResult trackResult)
        {
            if (exception != null)
            {
                return AnalyticsDebugDispatchStatus.ProviderException;
            }

            if (trackResult == null)
            {
                return AnalyticsDebugDispatchStatus.ForwardedToProvider;
            }

            switch (trackResult.Status)
            {
                case AnalyticsProviderTrackStatus.ForwardedToSdk:
                    return AnalyticsDebugDispatchStatus.ForwardedToSdk;
                case AnalyticsProviderTrackStatus.Queued:
                    return AnalyticsDebugDispatchStatus.QueuedByProvider;
                case AnalyticsProviderTrackStatus.SkippedCollectionDisabled:
                    return AnalyticsDebugDispatchStatus.SkippedProviderCollectionDisabled;
                case AnalyticsProviderTrackStatus.SkippedInvalidEvent:
                    return AnalyticsDebugDispatchStatus.SkippedInvalidEvent;
                case AnalyticsProviderTrackStatus.SkippedUnsupportedEvent:
                    return AnalyticsDebugDispatchStatus.SkippedUnsupportedEvent;
                case AnalyticsProviderTrackStatus.SkippedProviderNotReady:
                    return AnalyticsDebugDispatchStatus.SkippedProviderNotReady;
                case AnalyticsProviderTrackStatus.SdkUnavailable:
                    return AnalyticsDebugDispatchStatus.SdkUnavailable;
                case AnalyticsProviderTrackStatus.ProviderError:
                    return AnalyticsDebugDispatchStatus.ProviderError;
                default:
                    return AnalyticsDebugDispatchStatus.ForwardedToProvider;
            }
        }

        private static string GetProviderDisplayName(
            IAnalyticsProvider provider,
            IAnalyticsProviderDebugInfo debugInfo)
        {
            if (debugInfo != null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(debugInfo.ProviderDisplayName))
                    {
                        return debugInfo.ProviderDisplayName;
                    }
                }
                catch
                {
                    return GetProviderName(provider);
                }
            }

            return GetProviderName(provider);
        }

        private static AnalyticsProviderDebugState GetProviderDebugState(
            IAnalyticsProviderDebugInfo debugInfo,
            bool isReady,
            bool isCollectionEnabled)
        {
            if (debugInfo != null)
            {
                try
                {
                    return debugInfo.DebugState;
                }
                catch
                {
                    return AnalyticsProviderDebugState.Unknown;
                }
            }

            if (!isCollectionEnabled)
            {
                return AnalyticsProviderDebugState.Disabled;
            }

            return isReady
                ? AnalyticsProviderDebugState.Ready
                : AnalyticsProviderDebugState.Unknown;
        }

        private static int GetPendingEventCount(IAnalyticsProviderDebugInfo debugInfo)
        {
            if (debugInfo == null)
            {
                return 0;
            }

            try
            {
                return Math.Max(0, debugInfo.PendingEventCount);
            }
            catch
            {
                return 0;
            }
        }

        private static string GetLastError(IAnalyticsProviderDebugInfo debugInfo)
        {
            if (debugInfo == null)
            {
                return null;
            }

            try
            {
                return debugInfo.LastError;
            }
            catch
            {
                return null;
            }
        }

        private static bool GetIsReady(IAnalyticsProvider provider)
        {
            try
            {
                return provider != null && provider.IsReady;
            }
            catch
            {
                return false;
            }
        }

        private static bool GetIsCollectionEnabled(IAnalyticsProvider provider)
        {
            try
            {
                return provider != null && provider.IsCollectionEnabled;
            }
            catch
            {
                return false;
            }
        }

        private static void AddProviderResultSafely(
            List<AnalyticsDebugProviderResult> results,
            AnalyticsDebugProviderResult result)
        {
            if (results == null || result == null)
            {
                return;
            }

            try
            {
                results.Add(result);
            }
            catch
            {
                // Debug result collection must never affect analytics dispatch.
            }
        }
    }
}

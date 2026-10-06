namespace Game.Shared.Analytics.Core
{
    public enum AnalyticsProviderTrackStatus
    {
        Unknown,
        ForwardedToSdk,
        Queued,
        SkippedCollectionDisabled,
        SkippedInvalidEvent,
        SkippedUnsupportedEvent,
        SkippedProviderNotReady,
        SdkUnavailable,
        ProviderError
    }

    public sealed class AnalyticsProviderTrackResult
    {
        public AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus status, string detail = null)
        {
            Status = status;
            Detail = detail;
        }

        public AnalyticsProviderTrackStatus Status { get; }
        public string Detail { get; }

        public static AnalyticsProviderTrackResult Forwarded(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.ForwardedToSdk, detail);
        }

        public static AnalyticsProviderTrackResult Queued(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.Queued, detail);
        }

        public static AnalyticsProviderTrackResult SkippedCollectionDisabled(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.SkippedCollectionDisabled, detail);
        }

        public static AnalyticsProviderTrackResult SkippedInvalid(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.SkippedInvalidEvent, detail);
        }

        public static AnalyticsProviderTrackResult SkippedUnsupported(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.SkippedUnsupportedEvent, detail);
        }

        public static AnalyticsProviderTrackResult SkippedProviderNotReady(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.SkippedProviderNotReady, detail);
        }

        public static AnalyticsProviderTrackResult SdkUnavailable(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.SdkUnavailable, detail);
        }

        public static AnalyticsProviderTrackResult Error(string detail = null)
        {
            return new AnalyticsProviderTrackResult(AnalyticsProviderTrackStatus.ProviderError, detail);
        }
    }

    public interface IAnalyticsProvider
    {
        bool IsReady { get; }
        bool IsCollectionEnabled { get; }

        void Initialize();
        AnalyticsProviderTrackResult Track(
            AnalyticsEvent analyticsEvent,
            bool queueWhenProviderNotReady = true);
        void SetCollectionEnabled(bool enabled);
        void SetDefaultLevelContext(
            int displayedLevelNumber,
            int internalLevelNumber,
            string source);
        void ClearDefaultLevelContext(string source);
        void ResetAnalyticsData(string source = "analytics_reset");
    }
}

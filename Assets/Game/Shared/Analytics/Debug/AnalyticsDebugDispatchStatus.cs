namespace Game.Shared.Analytics.Debugging
{
    public enum AnalyticsDebugDispatchStatus
    {
        ForwardedToProvider,
        QueuedByProvider,
        SkippedGlobalCollectionDisabled,
        SkippedProviderCollectionDisabled,
        ForwardedToSdk,
        SkippedInvalidEvent,
        SkippedUnsupportedEvent,
        SkippedProviderNotReady,
        SdkUnavailable,
        ProviderError,
        ProviderException,
        NoProviders
    }
}

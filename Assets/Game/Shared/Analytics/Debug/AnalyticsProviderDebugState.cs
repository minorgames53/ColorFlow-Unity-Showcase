namespace Game.Shared.Analytics.Debugging
{
    public enum AnalyticsProviderDebugState
    {
        Unknown,
        Disabled,
        Initializing,
        Ready,
        Queued,
        ForwardedToSdk,
        Error
    }
}

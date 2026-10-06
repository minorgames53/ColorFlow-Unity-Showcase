namespace Game.Shared.Analytics.Debugging
{
    public interface IAnalyticsProviderDebugInfo
    {
        string ProviderDisplayName { get; }
        AnalyticsProviderDebugState DebugState { get; }
        int PendingEventCount { get; }
        string LastError { get; }
    }
}

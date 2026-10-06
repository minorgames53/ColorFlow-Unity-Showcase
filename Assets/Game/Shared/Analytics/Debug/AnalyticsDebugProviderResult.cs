namespace Game.Shared.Analytics.Debugging
{
    public sealed class AnalyticsDebugProviderResult
    {
        public AnalyticsDebugProviderResult(
            string providerDisplayName,
            string providerTypeName,
            bool isReady,
            bool isCollectionEnabled,
            AnalyticsDebugDispatchStatus dispatchStatus,
            AnalyticsProviderDebugState providerState,
            int pendingEventCount,
            string detail)
        {
            ProviderDisplayName = providerDisplayName;
            ProviderTypeName = providerTypeName;
            IsReady = isReady;
            IsCollectionEnabled = isCollectionEnabled;
            DispatchStatus = dispatchStatus;
            ProviderState = providerState;
            PendingEventCount = pendingEventCount;
            Detail = detail;
        }

        public string ProviderDisplayName { get; }
        public string ProviderTypeName { get; }
        public bool IsReady { get; }
        public bool IsCollectionEnabled { get; }
        public AnalyticsDebugDispatchStatus DispatchStatus { get; }
        public AnalyticsProviderDebugState ProviderState { get; }
        public int PendingEventCount { get; }
        public string Detail { get; }
    }
}

namespace Game.Shared.Ads.Core
{
    public enum AdsInitializationState
    {
        None,
        GatheringConsent,
        InitializingSdk,
        Ready,
        Failed
    }
}

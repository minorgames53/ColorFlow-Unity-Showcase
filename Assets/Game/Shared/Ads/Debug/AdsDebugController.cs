using Game.Shared.Ads.Core;
using UnityEngine;

namespace Game.Shared.Ads.Debugging
{
    [DisallowMultipleComponent]
    public sealed class AdsDebugController : MonoBehaviour
    {
        private AdsService service;

        public AdsInitializationState InitializationState =>
            service?.InitializationState ?? AdsInitializationState.None;
        public bool CanRequestAds => service != null && service.CanRequestAds;
        public bool PrivacyOptionsRequired =>
            service != null && service.IsPrivacyOptionsRequired;
        public AdLoadState RewardedLifeState => GetState(AdPlacement.RewardedLife);
        public AdLoadState RewardedDoubleGoldState => GetState(AdPlacement.RewardedDoubleGold);
        public AdLoadState InterstitialState => GetState(AdPlacement.InterstitialGameToMenu);

        private void Awake()
        {
            service = AdsService.Instance;
        }

        internal void Attach(AdsService adsService)
        {
            service = adsService;
        }

        [ContextMenu("Ads/Load Rewarded Life")]
        public void LoadRewardedLife()
        {
            if (IsAllowed) service?.Load(AdPlacement.RewardedLife);
        }

        [ContextMenu("Ads/Show Rewarded Life")]
        public void ShowRewardedLife()
        {
            if (!IsAllowed) return;
            service?.ShowRewarded(
                AdPlacement.RewardedLife,
                () => Debug.Log("[Ads Debug] RewardedLife reward callback"),
                result => Debug.Log($"[Ads Debug] RewardedLife finished: {result}"));
        }

        [ContextMenu("Ads/Load Rewarded Double Gold")]
        public void LoadRewardedDoubleGold()
        {
            if (IsAllowed) service?.Load(AdPlacement.RewardedDoubleGold);
        }

        [ContextMenu("Ads/Show Rewarded Double Gold")]
        public void ShowRewardedDoubleGold()
        {
            if (!IsAllowed) return;
            service?.ShowRewarded(
                AdPlacement.RewardedDoubleGold,
                () => Debug.Log("[Ads Debug] RewardedDoubleGold reward callback"),
                result => Debug.Log($"[Ads Debug] RewardedDoubleGold finished: {result}"));
        }

        [ContextMenu("Ads/Load Interstitial")]
        public void LoadInterstitial()
        {
            if (IsAllowed) service?.Load(AdPlacement.InterstitialGameToMenu);
        }

        [ContextMenu("Ads/Show Interstitial")]
        public void ShowInterstitial()
        {
            if (!IsAllowed) return;
            service?.ShowInterstitial(
                AdPlacement.InterstitialGameToMenu,
                result => Debug.Log($"[Ads Debug] Interstitial finished: {result}"));
        }

        [ContextMenu("Ads/Show Privacy Options")]
        public void ShowPrivacyOptions()
        {
            if (IsAllowed) service?.ShowPrivacyOptions();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [ContextMenu("Ads/Open Ad Inspector")]
        public void OpenAdInspector()
        {
            if (IsAllowed) service?.OpenAdInspector();
        }

        [ContextMenu("Ads/Reset Consent (Testing Only)")]
        public void ResetConsentForTesting()
        {
            if (IsAllowed) service?.ResetConsentForTesting();
        }
#endif

        private bool IsAllowed => Game.Shared.DeveloperTools.DeveloperPanelAvailability.IsAllowed;

        private AdLoadState GetState(AdPlacement placement)
        {
            return service?.GetLoadState(placement) ?? AdLoadState.Idle;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Shared.Ads.Core
{
    [CreateAssetMenu(
        fileName = "AdsConfig",
        menuName = "Game/Shared/Ads Config")]
    public sealed class AdsConfig : ScriptableObject
    {
        private const string AndroidRewardedTestAdUnitId =
            "ca-app-pub-3940256099942544/5224354917";
        private const string IosRewardedTestAdUnitId =
            "ca-app-pub-3940256099942544/1712485313";
        private const string AndroidInterstitialTestAdUnitId =
            "ca-app-pub-3940256099942544/1033173712";
        private const string IosInterstitialTestAdUnitId =
            "ca-app-pub-3940256099942544/4411468910";

        [Header("General")]
        [SerializeField] private bool adsEnabled = true;
        [SerializeField] private bool useTestAds = true;
        [SerializeField] private bool debugLogs = true;

        [Header("Interstitial")]
        [SerializeField, Min(1), Tooltip("First displayed level eligible for interstitials, inclusive. 19 means no interstitials for level 1-18 results; level 19 and later use the Win/Lose rules.")]
        private int interstitialStartLevel = 19;
        [SerializeField] private bool showInterstitialAfterWin = true;
        [SerializeField, Min(1)] private int interstitialLoseInterval = 4;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("Consent Debug (Editor / Development Build Only)")]
        [SerializeField] private bool forceEeaConsentDebugMode;
        [SerializeField] private string umpTestDeviceHashedId = string.Empty;
        [SerializeField] private bool resetConsentOnStartupForTesting;
#endif

        [Header("Android")]
        [SerializeField] private string androidRewardedLifeAdUnitId = string.Empty;
        [SerializeField] private string androidRewardedDoubleGoldAdUnitId = string.Empty;
        [SerializeField] private string androidInterstitialGameToMenuAdUnitId = string.Empty;

        [Header("iOS")]
        [SerializeField] private string iosRewardedLifeAdUnitId = string.Empty;
        [SerializeField] private string iosRewardedDoubleGoldAdUnitId = string.Empty;
        [SerializeField] private string iosInterstitialGameToMenuAdUnitId = string.Empty;

        [Header("Retry (seconds)")]
        [SerializeField] private List<float> retryDelaysSeconds =
            new List<float> { 5f, 15f, 30f, 60f };

        [Header("Cache")]
        [SerializeField, Min(1f)] private float maxAdAgeMinutes = 55f;

        public bool AdsEnabled => adsEnabled;
        public bool UseTestAds => useTestAds;
        public bool DebugLogs => debugLogs;
        public int InterstitialStartLevel => Math.Max(1, interstitialStartLevel);
        public bool ShowInterstitialAfterWin => showInterstitialAfterWin;
        public int InterstitialLoseInterval => Math.Max(1, interstitialLoseInterval);
        public TimeSpan MaxAdAge => TimeSpan.FromMinutes(Math.Max(1f, maxAdAgeMinutes));

        private void OnValidate()
        {
            interstitialStartLevel = Math.Max(1, interstitialStartLevel);
            interstitialLoseInterval = Math.Max(1, interstitialLoseInterval);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal bool ForceEeaConsentDebugMode => forceEeaConsentDebugMode;
        internal string UmpTestDeviceHashedId => umpTestDeviceHashedId;
        internal bool ResetConsentOnStartupForTesting => resetConsentOnStartupForTesting;
#endif

        internal bool IsPlatformSupported
        {
            get
            {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        public float GetRetryDelaySeconds(int retryCount)
        {
            if (retryDelaysSeconds == null || retryDelaysSeconds.Count == 0)
            {
                return 60f;
            }

            int index = Mathf.Clamp(retryCount - 1, 0, retryDelaysSeconds.Count - 1);
            return Mathf.Max(0.1f, retryDelaysSeconds[index]);
        }

        internal bool TryGetAdUnitId(AdPlacement placement, out string adUnitId)
        {
            adUnitId = string.Empty;

#if UNITY_EDITOR
            // The SDK's Editor client must never receive production ad units.
#if UNITY_IOS
            adUnitId = placement == AdPlacement.InterstitialGameToMenu
                ? IosInterstitialTestAdUnitId
                : IosRewardedTestAdUnitId;
#else
            // Use Android samples for Android and non-mobile Editor build targets.
            adUnitId = placement == AdPlacement.InterstitialGameToMenu
                ? AndroidInterstitialTestAdUnitId
                : AndroidRewardedTestAdUnitId;
#endif
#elif UNITY_ANDROID
            adUnitId = ResolveAndroidAdUnitId(placement);
#elif UNITY_IOS
            adUnitId = ResolveIosAdUnitId(placement);
#else
            return false;
#endif

            return !string.IsNullOrWhiteSpace(adUnitId);
        }

        private string ResolveAndroidAdUnitId(AdPlacement placement)
        {
            if (useTestAds)
            {
                return placement == AdPlacement.InterstitialGameToMenu
                    ? AndroidInterstitialTestAdUnitId
                    : AndroidRewardedTestAdUnitId;
            }

            switch (placement)
            {
                case AdPlacement.RewardedLife:
                    return androidRewardedLifeAdUnitId;
                case AdPlacement.RewardedDoubleGold:
                    return androidRewardedDoubleGoldAdUnitId;
                case AdPlacement.InterstitialGameToMenu:
                    return androidInterstitialGameToMenuAdUnitId;
                default:
                    return string.Empty;
            }
        }

        private string ResolveIosAdUnitId(AdPlacement placement)
        {
            if (useTestAds)
            {
                return placement == AdPlacement.InterstitialGameToMenu
                    ? IosInterstitialTestAdUnitId
                    : IosRewardedTestAdUnitId;
            }

            switch (placement)
            {
                case AdPlacement.RewardedLife:
                    return iosRewardedLifeAdUnitId;
                case AdPlacement.RewardedDoubleGold:
                    return iosRewardedDoubleGoldAdUnitId;
                case AdPlacement.InterstitialGameToMenu:
                    return iosInterstitialGameToMenuAdUnitId;
                default:
                    return string.Empty;
            }
        }
    }
}

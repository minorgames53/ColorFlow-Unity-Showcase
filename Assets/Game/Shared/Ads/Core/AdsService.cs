using System;
using System.Collections;
using Game.Shared.DeveloperTools;
using Game.Shared.Config;
using Game.Shared.Ads.Consent;
using Game.Shared.Ads.Debugging;
using Game.Shared.Ads.Interstitial;
using Game.Shared.Ads.Rewarded;
using Game.Shared.Save;
using Game.Shared.Store;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Game.Shared.Ads.Core
{
    [DisallowMultipleComponent]
    public sealed class AdsService : MonoBehaviour
    {
        public const int MonetizationUnlockCompletedLevel = 19;
        public const int RewardedUnlockCompletedLevel = 10;
        private const float InterstitialShowCallbackTimeoutSeconds = 60f;

        [SerializeField] private AdsConfig config;

        private static bool sdkInitializationStarted;
        private static bool sdkInitializationCompleted;

        private readonly object fullscreenLock = new object();

        private AdsLogger logger;
        private AdsConsentService consentService;
        private RewardedAdController rewardedController;
        private InterstitialAdController interstitialController;
        private bool controllersCreated;
        private bool isFullscreenAdShowing;
        private bool isDestroyed;
        private SaveManager saveManager;
        private StoreManager storeManager;
        // Runtime result context, not a second persistent pending-lose flag.
        private UnityEngine.Object interstitialResultOwner;
        private bool interstitialResultWasWin;
        private int interstitialResultDisplayedLevel;
        private bool resultInterstitialConsumed;
        private bool resultExitInProgress;
        private int resultInterstitialVersion;
        private bool presentationSnapshotValid;
        private bool observedMonetizationUnlocked;
        private bool observedInterstitialLevelUnlocked;
        private bool observedRewardedOffersUnlocked;
        private bool observedHasNoAds;
        private bool observedEntitlementResolved;

        public static AdsService Instance { get; private set; }

        public AdsInitializationState InitializationState { get; private set; } =
            AdsInitializationState.None;

        public bool CanRequestAds
        {
            get
            {
                if (!FeatureConfig.ExternalServicesEnabled) return false;
#if UNITY_EDITOR
                return config != null && config.AdsEnabled &&
                       InitializationState == AdsInitializationState.Ready;
#else
                return consentService != null && consentService.CanRequestAds;
#endif
            }
        }

        public bool IsPrivacyOptionsRequired =>
            FeatureConfig.ExternalServicesEnabled &&
            consentService != null && consentService.IsPrivacyOptionsRequired;

        public bool CanShowInterstitial =>
            FeatureConfig.ExternalServicesEnabled &&
            IsInterstitialLevelUnlocked &&
            !HasNoAdsEntitlement &&
            IsNoAdsEntitlementResolved &&
            config != null && config.AdsEnabled &&
            InitializationState == AdsInitializationState.Ready &&
            interstitialController != null;

        private bool IsInterstitialLevelUnlocked
        {
            get
            {
                // A win advances the save before the result is left. Use the result's
                // displayed level so winning level 18 cannot unlock a level-19 ad early.
                int level = interstitialResultOwner != null
                    ? interstitialResultDisplayedLevel
                    : saveManager != null && saveManager.IsInitialized ? saveManager.CurrentLevel : 0;
                return config != null && level >= config.InterstitialStartLevel;
            }
        }

        public bool IsMonetizationUnlocked =>
            FeatureConfig.ExternalServicesEnabled &&
            saveManager != null && saveManager.IsInitialized &&
            saveManager.CurrentLevel > MonetizationUnlockCompletedLevel;

        public bool AreRewardedOffersUnlocked =>
            FeatureConfig.ExternalServicesEnabled &&
            saveManager != null && saveManager.IsInitialized &&
            saveManager.CurrentLevel > RewardedUnlockCompletedLevel;

        public bool HasNoAds => HasNoAdsEntitlement;

        public bool IsNoAdsEntitlementResolved =>
            storeManager != null &&
            storeManager.EntitlementSyncState == StoreEntitlementSyncState.Succeeded;

        public bool CanShowRewardedOffers => AreRewardedOffersUnlocked;

        public bool CanShowNoAdsOffer =>
            IsMonetizationUnlocked &&
            !HasNoAdsEntitlement &&
            IsNoAdsEntitlementResolved;

        public bool NoAdsIntroShown =>
            saveManager != null && saveManager.IsInitialized && saveManager.NoAdsIntroShown;

        public event Action<AdPlacement, bool> OnAdAvailabilityChanged;
        public event Action AdPresentationStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            logger = new AdsLogger(config != null && config.DebugLogs);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            isDestroyed = true;
            StopAllCoroutines();
            rewardedController?.Dispose();
            interstitialController?.Dispose();
            consentService?.Dispose();
            UnbindNoAdsEntitlement();
            UnbindStoreEntitlement();
            rewardedController = null;
            interstitialController = null;
            consentService = null;
            controllersCreated = false;
            ReleaseFullscreenLock();
            OnAdAvailabilityChanged = null;
            AdPresentationStateChanged = null;
            Instance = null;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus || isDestroyed)
            {
                return;
            }

            RefreshPresentationState();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            sdkInitializationStarted = false;
            sdkInitializationCompleted = false;
        }

        public void Configure(AdsConfig adsConfig)
        {
            if (InitializationState != AdsInitializationState.None)
            {
                logger?.Warning("Configure ignored after initialization started");
                return;
            }

            if (adsConfig != null || config == null)
            {
                config = adsConfig;
            }

            logger = new AdsLogger(config != null && config.DebugLogs);
        }

        public void Initialize()
        {
            if (isDestroyed || InitializationState != AdsInitializationState.None)
            {
                return;
            }

            BindNoAdsEntitlement();
            BindStoreEntitlement();
            RefreshPresentationState();

            if (config == null)
            {
                InitializationState = AdsInitializationState.Failed;
                logger.Error("AdsConfig is missing. Ads will stay disabled.");
                return;
            }

            logger.Log("Initialize started");
            AttachDebugControllerIfAllowed();

            if (!FeatureConfig.ExternalServicesEnabled || !config.AdsEnabled)
            {
                InitializationState = AdsInitializationState.Ready;
                logger.Log("Ads are disabled by showcase settings or config");
                return;
            }

#if UNITY_EDITOR
            InitializeSdkOnce();
#else
            if (!config.IsPlatformSupported)
            {
                InitializationState = AdsInitializationState.Ready;
                logger.Warning("Current platform is unsupported. Ads will be unavailable.");
                return;
            }

            InitializationState = AdsInitializationState.GatheringConsent;
            consentService = new AdsConsentService(logger, config);
            consentService.GatherConsent(canRequestAds =>
            {
                try
                {
                    HandleConsentGathered(canRequestAds);
                }
                finally
                {
                    NotifyConsentLifecycleChangedForDeveloperTools();
                }
            });
#endif
        }

        public bool IsRewardedReady(AdPlacement placement)
        {
            return CanShowRewardedOffers &&
                   InitializationState == AdsInitializationState.Ready &&
                   rewardedController != null &&
                   rewardedController.IsReady(placement);
        }

        public void ShowRewarded(
            AdPlacement placement,
            Action onRewardEarned,
            Action<AdShowResult> onFinished = null)
        {
            if (!CanShowRewardedOffers)
            {
                InvokeFinishedSafely(onFinished, AdShowResult.Disabled, placement);
                return;
            }

            AdShowResult rejection = GetShowRejection();
            if (rejection != AdShowResult.Closed)
            {
                InvokeFinishedSafely(onFinished, rejection, placement);
                return;
            }

            if (rewardedController == null || !rewardedController.IsReady(placement))
            {
                InvokeFinishedSafely(onFinished, AdShowResult.NotReady, placement);
                return;
            }

            if (!TryAcquireFullscreenLock())
            {
                InvokeFinishedSafely(onFinished, AdShowResult.Busy, placement);
                return;
            }

            bool finishInvoked = false;
            Action<AdShowResult> finish = result =>
            {
                if (finishInvoked)
                {
                    return;
                }

                finishInvoked = true;
                ReleaseFullscreenLock();
                InvokeFinishedSafely(onFinished, result, placement);
            };

            try
            {
                if (!rewardedController.Show(placement, onRewardEarned, finish))
                {
                    finish(AdShowResult.NotReady);
                }
            }
            catch (Exception exception)
            {
                logger.Warning(placement, $"Show request threw: {exception.Message}");
                finish(AdShowResult.Failed);
            }
        }

        public bool IsInterstitialReady(AdPlacement placement)
        {
            return CanShowInterstitial &&
                   interstitialController != null &&
                   interstitialController.IsReady(placement);
        }

        public void ShowInterstitial(
            AdPlacement placement,
            Action<AdShowResult> onFinished = null,
            Action onOpened = null)
        {
            if (!IsInterstitialLevelUnlocked || HasNoAdsEntitlement ||
                !IsNoAdsEntitlementResolved)
            {
                InvokeFinishedSafely(onFinished, AdShowResult.Disabled, placement);
                return;
            }

            AdShowResult rejection = GetShowRejection();
            if (rejection != AdShowResult.Closed)
            {
                InvokeFinishedSafely(onFinished, rejection, placement);
                return;
            }

            if (interstitialController == null || !interstitialController.IsReady(placement))
            {
                InvokeFinishedSafely(onFinished, AdShowResult.NotReady, placement);
                return;
            }

            if (!TryAcquireFullscreenLock())
            {
                InvokeFinishedSafely(onFinished, AdShowResult.Busy, placement);
                return;
            }

            bool finishInvoked = false;
            Action<AdShowResult> finish = result =>
            {
                if (finishInvoked)
                {
                    return;
                }

                finishInvoked = true;
                ReleaseFullscreenLock();
                InvokeFinishedSafely(onFinished, result, placement);
            };

            try
            {
                if (!interstitialController.Show(placement, finish, onOpened))
                {
                    finish(AdShowResult.NotReady);
                }
            }
            catch (Exception exception)
            {
                logger.Warning(placement, $"Show request threw: {exception.Message}");
                finish(AdShowResult.Failed);
            }
        }

        public void RecordWinForInterstitial(UnityEngine.Object owner, int displayedLevelNumber)
        {
            if (owner == null || (interstitialResultOwner == owner && interstitialResultWasWin)) return;
            BindNoAdsEntitlement();
            SetInterstitialLoseCount(0);
            SetInterstitialResult(owner, true, displayedLevelNumber);
        }

        public void RecordFinalizedLoseForInterstitial(UnityEngine.Object owner, int displayedLevelNumber)
        {
            if (owner == null || (interstitialResultOwner == owner && !interstitialResultWasWin)) return;
            BindNoAdsEntitlement();
            int count = InterstitialLoseCount;
            SetInterstitialLoseCount(HasNoAdsEntitlement ? 0 : count == int.MaxValue ? count : count + 1);
            SetInterstitialResult(owner, false, displayedLevelNumber);
        }

        private int InterstitialLoseCount => saveManager != null && saveManager.IsInitialized
            ? Math.Max(0, saveManager.GetIntSetting(SaveKeys.InterstitialLoseCount)) : 0;

        private void SetInterstitialLoseCount(int count)
        {
            if (saveManager == null || !saveManager.IsInitialized ||
                saveManager.GetIntSetting(SaveKeys.InterstitialLoseCount) == count) return;
            saveManager.SetSetting(SaveKeys.InterstitialLoseCount, count);
            saveManager.Save();
        }

        private void SetInterstitialResult(UnityEngine.Object owner, bool wasWin, int displayedLevelNumber)
        {
            interstitialResultOwner = owner;
            interstitialResultWasWin = wasWin;
            interstitialResultDisplayedLevel = Math.Max(1, displayedLevelNumber);
            resultInterstitialConsumed = false;
            resultInterstitialVersion++;
            RefreshPresentationState();
        }

        public void ClearInterstitialResult(UnityEngine.Object owner)
        {
            if (interstitialResultOwner != owner) return;
            interstitialResultOwner = null;
            interstitialResultDisplayedLevel = 0;
            resultInterstitialConsumed = false;
            resultInterstitialVersion++;
            RefreshPresentationState();
        }

        /// <summary>
        /// Gates an accepted result exit, independent of Retry/next-level/Menu destination.
        /// A scene transition alone cannot schedule an ad: a finalized result must own it.
        /// Returns false when the caller should continue immediately.
        /// </summary>
        public bool TryRunResultExitInterstitial(Action continuation)
        {
            if (resultExitInProgress) return true;
            if (interstitialResultOwner == null || resultInterstitialConsumed) return false;
            resultInterstitialConsumed = true; // At most one attempt per result, even if unavailable.
            bool shouldShow = config != null && (interstitialResultWasWin
                ? config.ShowInterstitialAfterWin
                : InterstitialLoseCount >= config.InterstitialLoseInterval);
            if (!shouldShow || !isActiveAndEnabled || !CanShowInterstitial) return false;
            if (!isFullscreenAdShowing && !IsInterstitialReady(AdPlacement.InterstitialGameToMenu)) return false;

            resultExitInProgress = true;
            StartCoroutine(ShowResultInterstitial(continuation, resultInterstitialVersion, !interstitialResultWasWin));
            return true;
        }

        private IEnumerator ShowResultInterstitial(Action continuation, int version, bool resetLoseOnOpen)
        {
            // Early-level Double Gold may finish its coin/panel flow before rewarded closes.
            // Do not consume the Win attempt as Busy, or alter rewarded's reward callbacks.
            float fullscreenWaitStartedAt = UnityEngine.Time.realtimeSinceStartup;
            while (isFullscreenAdShowing &&
                   UnityEngine.Time.realtimeSinceStartup - fullscreenWaitStartedAt < InterstitialShowCallbackTimeoutSeconds)
                yield return null;

            if (version != resultInterstitialVersion || interstitialResultOwner == null)
            {
                resultExitInProgress = false;
                yield break;
            }

            bool finished = false;
            bool acceptingCallbacks = true;
            ShowInterstitial(AdPlacement.InterstitialGameToMenu, _ => finished = true, () =>
            {
                // SDK fullscreen-opened, NOT Show() return or load readiness, commits the reset.
                if (acceptingCallbacks && resetLoseOnOpen && version == resultInterstitialVersion)
                    SetInterstitialLoseCount(0);
            });

            float startedAt = UnityEngine.Time.realtimeSinceStartup;
            while (!finished && UnityEngine.Time.realtimeSinceStartup - startedAt < InterstitialShowCallbackTimeoutSeconds)
                yield return null;

            acceptingCallbacks = false;
            if (!finished)
            {
                logger?.Warning("Result interstitial completion timed out; continuing gameplay.");
                interstitialController?.CancelShow(); // Releases the fullscreen lock through the normal finish callback.
            }
            resultExitInProgress = false;
            if (!isDestroyed && version == resultInterstitialVersion && interstitialResultOwner != null)
                continuation?.Invoke();
        }

        public void ShowPrivacyOptions(Action<bool> onComplete = null)
        {
            if (!FeatureConfig.ExternalServicesEnabled ||
                consentService == null || !consentService.IsPrivacyOptionsRequired)
            {
                logger?.Warning("Privacy options are not currently required/available");
                onComplete?.Invoke(false);
                return;
            }

            consentService.ShowPrivacyOptions(result =>
            {
                NotifyConsentLifecycleChangedForDeveloperTools();
                if (!result.Success)
                {
                    if (result.CanRequestAds)
                    {
                        logger.Warning(
                            "Privacy options form did not complete; current consent still allows ads, so existing ads remain active");
                        onComplete?.Invoke(false);
                        return;
                    }

                    DisposeControllers();
                    InitializationState = AdsInitializationState.Failed;
                    logger.Warning(
                        "Privacy options form did not complete and current consent state does not allow ads");
                    onComplete?.Invoke(false);
                    return;
                }

                DisposeControllers();

                if (!result.CanRequestAds)
                {
                    InitializationState = AdsInitializationState.Failed;
                    logger.Warning("Privacy options changed; ads can no longer be requested");
                    onComplete?.Invoke(true);
                    return;
                }

                logger.Log("Privacy options changed; rebuilding ad caches with current consent state");
                InitializeSdkOnce();
                onComplete?.Invoke(true);
            });
        }

        internal AdLoadState GetLoadState(AdPlacement placement)
        {
            switch (placement)
            {
                case AdPlacement.RewardedLife:
                case AdPlacement.RewardedDoubleGold:
                    return rewardedController?.GetState(placement) ?? AdLoadState.Idle;
                case AdPlacement.InterstitialGameToMenu:
                    return interstitialController?.GetState(placement) ?? AdLoadState.Idle;
                default:
                    return AdLoadState.Idle;
            }
        }

        internal bool Load(AdPlacement placement)
        {
            if (InitializationState != AdsInitializationState.Ready || !IsOperational)
            {
                return false;
            }

            switch (placement)
            {
                case AdPlacement.RewardedLife:
                case AdPlacement.RewardedDoubleGold:
                    return rewardedController != null && rewardedController.Load(placement);
                case AdPlacement.InterstitialGameToMenu:
                    return interstitialController != null && interstitialController.Load(placement);
                default:
                    return false;
            }
        }

        internal event Action ConsentLifecycleChangedForDeveloperTools;

        internal bool HasResolvedConsent => FeatureConfig.ExternalServicesEnabled && consentService != null &&
            consentService.HasCompletedForDeveloperTools && !consentService.IsGatheringForDeveloperTools &&
            GoogleMobileAds.Ump.Api.ConsentInformation.ConsentStatus != GoogleMobileAds.Ump.Api.ConsentStatus.Unknown;

        internal bool IsMobileAdsInitializedForDeveloperTools =>
            sdkInitializationCompleted;

        internal bool IsConsentOperationRunningForDeveloperTools =>
            consentService != null && consentService.IsGatheringForDeveloperTools;

        internal string ConsentLifecycleForDeveloperTools
        {
            get
            {
                if (consentService == null)
                {
                    return "Unavailable";
                }

                if (consentService.IsGatheringForDeveloperTools)
                {
                    return "GatheringConsent";
                }

                return consentService.HasCompletedForDeveloperTools
                    ? "ConsentCompleted"
                    : "ConsentIdle";
            }
        }

        internal string LastConsentResultForDeveloperTools =>
            consentService?.LastResultForDeveloperTools ?? "Consent service is unavailable.";

        internal void OpenAdInspector()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return;
#if UNITY_EDITOR
            logger.Log("Ad Inspector is unavailable in the Editor mock");
#else
            if (!sdkInitializationCompleted)
            {
                logger.Warning("Ad Inspector requires an initialized SDK");
                return;
            }

            MobileAds.OpenAdInspector(error =>
            {
                AdsMainThread.Execute(() =>
                {
                    if (error != null)
                    {
                        logger.Warning($"Ad Inspector failed: {error.GetCode()}");
                    }
                    else
                    {
                        logger.Log("Ad Inspector opened");
                    }
                });
            });
#endif
        }

        internal void ResetConsentForTesting()
        {
            ResetAndRefreshConsentForDeveloperTools(null);
        }

        internal bool ResetConsentOnlyForDeveloperTools()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return false;
            EnsureDeveloperConsentService();
            if (consentService.IsGatheringForDeveloperTools)
            {
                return false;
            }

            consentService.ResetForTesting();
            DisposeControllers();
            InitializationState = AdsInitializationState.Failed;
            RefreshPresentationState();
            NotifyConsentLifecycleChangedForDeveloperTools();
            return true;
        }

        internal bool RefreshConsentForDeveloperTools(Action<bool> onComplete)
        {
            if (!DeveloperPanelAvailability.IsAllowed) return false;
            EnsureDeveloperConsentService();
            if (consentService.IsGatheringForDeveloperTools)
            {
                return false;
            }

            DisposeControllers();
            InitializationState = AdsInitializationState.GatheringConsent;
            NotifyConsentLifecycleChangedForDeveloperTools();
            return consentService.RefreshForTesting(canRequestAds =>
            {
                try
                {
                    HandleConsentGathered(canRequestAds);
                }
                finally
                {
                    NotifyConsentLifecycleChangedForDeveloperTools();
                    onComplete?.Invoke(canRequestAds);
                }
            });
        }

        internal bool ResetAndRefreshConsentForDeveloperTools(Action<bool> onComplete)
        {
            if (!DeveloperPanelAvailability.IsAllowed) return false;
            EnsureDeveloperConsentService();
            if (consentService.IsGatheringForDeveloperTools)
            {
                return false;
            }

            consentService.ResetForTesting();
            return RefreshConsentForDeveloperTools(onComplete);
        }

        internal void RefreshRuntimeStateForDeveloperTools()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return;
            RefreshPresentationState();
        }

        private void EnsureDeveloperConsentService()
        {
            if (consentService == null)
            {
                consentService = new AdsConsentService(logger, config);
            }
        }

        private void NotifyConsentLifecycleChangedForDeveloperTools()
        {
            Delegate[] subscribers =
                ConsentLifecycleChangedForDeveloperTools?.GetInvocationList();
            if (subscribers == null)
            {
                return;
            }

            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action)subscribers[i]).Invoke();
                }
                catch (Exception exception)
                {
                    logger?.Warning(
                        $"Developer consent lifecycle listener failed: {exception.Message}");
                }
            }
        }

        private bool IsOperational =>
            FeatureConfig.ExternalServicesEnabled && config != null && config.AdsEnabled && controllersCreated;

        private bool HasNoAdsEntitlement =>
            saveManager != null && saveManager.IsInitialized && saveManager.HasNoAds;

        public bool TryMarkNoAdsIntroShown()
        {
            return CanShowNoAdsOffer && !NoAdsIntroShown &&
                   saveManager != null && saveManager.MarkNoAdsIntroShown();
        }

        private void BindNoAdsEntitlement()
        {
            SaveManager availableSaveManager = SaveManager.Instance;
            if (saveManager == availableSaveManager)
            {
                return;
            }

            UnbindNoAdsEntitlement();
            saveManager = availableSaveManager;
            if (saveManager != null)
            {
                saveManager.StoreStateChanged += HandleStoreStateChanged;
                saveManager.CurrentLevelChanged += HandleCurrentLevelChanged;
                saveManager.OnSaveLoaded += HandleSaveLoaded;
            }
        }

        private void UnbindNoAdsEntitlement()
        {
            if (saveManager != null)
            {
                saveManager.StoreStateChanged -= HandleStoreStateChanged;
                saveManager.CurrentLevelChanged -= HandleCurrentLevelChanged;
                saveManager.OnSaveLoaded -= HandleSaveLoaded;
                saveManager = null;
            }
        }

        private void HandleStoreStateChanged()
        {
            if (HasNoAdsEntitlement)
            {
                HandleAvailabilityChanged(AdPlacement.InterstitialGameToMenu, false);
            }

            RefreshPresentationState();
        }

        private void HandleSaveLoaded()
        {
            RefreshPresentationState();
        }

        private void HandleCurrentLevelChanged(int _)
        {
            RefreshPresentationState();
        }

        private void BindStoreEntitlement()
        {
            StoreManager availableStoreManager = StoreManager.Instance;
            if (storeManager == availableStoreManager)
            {
                return;
            }

            UnbindStoreEntitlement();
            storeManager = availableStoreManager;
            if (storeManager != null)
            {
                storeManager.EntitlementSyncStateChanged += HandleEntitlementSyncStateChanged;
            }
        }

        private void UnbindStoreEntitlement()
        {
            if (storeManager != null)
            {
                storeManager.EntitlementSyncStateChanged -= HandleEntitlementSyncStateChanged;
                storeManager = null;
            }
        }

        private void HandleEntitlementSyncStateChanged(StoreEntitlementSyncState _)
        {
            RefreshPresentationState();
        }

        private void RefreshPresentationState()
        {
            BindNoAdsEntitlement();
            BindStoreEntitlement();
            if (HasNoAdsEntitlement) SetInterstitialLoseCount(0);

            bool monetizationUnlocked = IsMonetizationUnlocked;
            bool interstitialLevelUnlocked = IsInterstitialLevelUnlocked;
            bool rewardedOffersUnlocked = AreRewardedOffersUnlocked;
            bool hasNoAds = HasNoAdsEntitlement;
            bool entitlementResolved = IsNoAdsEntitlementResolved;
            bool interstitialLevelChanged = !presentationSnapshotValid ||
                observedInterstitialLevelUnlocked != interstitialLevelUnlocked;

            if (presentationSnapshotValid &&
                observedMonetizationUnlocked == monetizationUnlocked &&
                observedInterstitialLevelUnlocked == interstitialLevelUnlocked &&
                observedRewardedOffersUnlocked == rewardedOffersUnlocked &&
                observedHasNoAds == hasNoAds &&
                observedEntitlementResolved == entitlementResolved)
            {
                return;
            }

            observedMonetizationUnlocked = monetizationUnlocked;
            observedInterstitialLevelUnlocked = interstitialLevelUnlocked;
            observedRewardedOffersUnlocked = rewardedOffersUnlocked;
            observedHasNoAds = hasNoAds;
            observedEntitlementResolved = entitlementResolved;
            presentationSnapshotValid = true;
            AdPresentationStateChanged?.Invoke();

            if (interstitialLevelChanged)
            {
                HandleAvailabilityChanged(
                    AdPlacement.InterstitialGameToMenu,
                    IsInterstitialReady(AdPlacement.InterstitialGameToMenu));
            }
        }

        private void HandleConsentGathered(bool canRequestAds)
        {
            if (isDestroyed || InitializationState != AdsInitializationState.GatheringConsent)
            {
                return;
            }

            if (!canRequestAds)
            {
                InitializationState = AdsInitializationState.Failed;
                logger.Warning("Consent flow completed but ads cannot currently be requested");
                return;
            }

            InitializeSdkOnce();
        }

        private void InitializeSdkOnce()
        {
            if (!FeatureConfig.ExternalServicesEnabled || isDestroyed || controllersCreated)
            {
                return;
            }

            if (sdkInitializationCompleted)
            {
                CreateControllersAndPreload();
                return;
            }

            if (sdkInitializationStarted)
            {
                return;
            }

            sdkInitializationStarted = true;
            InitializationState = AdsInitializationState.InitializingSdk;
            logger.Log("Initializing SDK");

            try
            {
                MobileAds.Initialize(status =>
                {
                    AdsMainThread.Execute(() => HandleSdkInitialized(status));
                });
            }
            catch (Exception exception)
            {
                InitializationState = AdsInitializationState.Failed;
                logger.Error($"SDK initialization threw: {exception.Message}");
            }
        }

        private void HandleSdkInitialized(InitializationStatus status)
        {
            if (isDestroyed)
            {
                return;
            }

            if (status == null)
            {
                InitializationState = AdsInitializationState.Failed;
                logger.Error("SDK initialization returned no status");
                return;
            }

            sdkInitializationCompleted = true;
            LogAdapterStatuses(status);
            logger.Log("SDK initialized");

            if (InitializationState == AdsInitializationState.InitializingSdk)
            {
                CreateControllersAndPreload();
            }
        }

        private void CreateControllersAndPreload()
        {
            if (!FeatureConfig.ExternalServicesEnabled || isDestroyed ||
                controllersCreated || config == null || !config.AdsEnabled)
            {
                return;
            }

            rewardedController = new RewardedAdController(
                this,
                config,
                logger,
                HandleAvailabilityChanged);
            interstitialController = new InterstitialAdController(
                this,
                config,
                logger,
                HandleAvailabilityChanged);
            controllersCreated = true;
            InitializationState = AdsInitializationState.Ready;
            rewardedController.PreloadAll();
            interstitialController.Preload();
        }

        private void HandleAvailabilityChanged(AdPlacement placement, bool ready)
        {
            if (isDestroyed)
            {
                return;
            }

            Delegate[] subscribers = OnAdAvailabilityChanged?.GetInvocationList();
            if (subscribers == null)
            {
                return;
            }

            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action<AdPlacement, bool>)subscribers[i]).Invoke(placement, ready);
                }
                catch (Exception exception)
                {
                    logger.Warning(placement, $"Availability subscriber threw: {exception.Message}");
                }
            }
        }

        private void DisposeControllers()
        {
            rewardedController?.Dispose();
            interstitialController?.Dispose();
            rewardedController = null;
            interstitialController = null;
            controllersCreated = false;
        }

        private AdShowResult GetShowRejection()
        {
            if (!FeatureConfig.ExternalServicesEnabled || config == null || !config.AdsEnabled)
            {
                return AdShowResult.Disabled;
            }

            if (InitializationState == AdsInitializationState.Failed)
            {
                return AdShowResult.Failed;
            }

            return InitializationState == AdsInitializationState.Ready
                ? AdShowResult.Closed
                : AdShowResult.NotReady;
        }

        private bool TryAcquireFullscreenLock()
        {
            lock (fullscreenLock)
            {
                if (isFullscreenAdShowing)
                {
                    return false;
                }

                isFullscreenAdShowing = true;
                return true;
            }
        }

        private void ReleaseFullscreenLock()
        {
            lock (fullscreenLock)
            {
                isFullscreenAdShowing = false;
            }
        }

        private void InvokeFinishedSafely(
            Action<AdShowResult> callback,
            AdShowResult result,
            AdPlacement placement)
        {
            try
            {
                callback?.Invoke(result);
            }
            catch (Exception exception)
            {
                logger?.Warning(placement, $"Finished callback threw: {exception.Message}");
            }
        }

        private void LogAdapterStatuses(InitializationStatus status)
        {
            if (!config.DebugLogs)
            {
                return;
            }

            var adapterStatuses = status.getAdapterStatusMap();
            if (adapterStatuses == null)
            {
                return;
            }

            foreach (var pair in adapterStatuses)
            {
                logger.Log(
                    $"Adapter {pair.Key}: state={pair.Value.InitializationState}, " +
                    $"latencyMs={pair.Value.Latency}, description={pair.Value.Description}");
            }
        }

        private void AttachDebugControllerIfAllowed()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AdsDebugController debugController = GetComponent<AdsDebugController>();
            if (debugController == null)
            {
                debugController = gameObject.AddComponent<AdsDebugController>();
            }

            debugController.Attach(this);
#endif
        }
    }
}

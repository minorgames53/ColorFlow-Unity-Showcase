using System;
using Game.Shared.Ads.Core;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Bootstrap;
using Game.Shared.Save;
using Game.Shared.UI.Panels;
using Game.Menu;
using Gameplay.Analytics;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Game.Shared.Lives.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup), typeof(UIPanel))]
    public sealed class LivesRefillPanelController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private UIPanel panel;

        [Header("Dynamic Text")]
        [SerializeField] private TMP_Text currentLivesText;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text rewardedLifeGainText;
        [SerializeField] private TMP_Text goldCostText;

        [Header("Localization")]
        [SerializeField] private LocalizedString rewardedLifeGainFormat;

        [Header("Actions")]
        [SerializeField] private Button adButton;
        [SerializeField] private Button buyButton;

        [Header("Reward Presentation")]
        [SerializeField] private HeartFlyAnimator heartFlyAnimator;

        private LivesService livesService;
        private LivesPurchaseService purchaseService;
        private SaveManager saveManager;
        private AdsService adsService;
        private bool servicesSubscribed;
        private bool buttonsSubscribed;
        private bool rewardedLifeGainSubscribed;
        private bool actionInProgress;
        private bool closeRequested;
        private bool rewardGrantedForCurrentRequest;
        private int rewardedHeartsToPresent;
        private int rewardedRequestVersion;
        private int activeRewardedRequest;
        private int rewardedDisplayedLevelNumber;
        private int rewardedLevelNumber;
        private bool hasRewardedAnalyticsContext;
        private long lastDisplayedRemainingSeconds = -1;
        private readonly object[] rewardedLifeGainArguments = new object[1];

        public event Action InsufficientGold;

        public bool IsOpen => panel != null && panel.gameObject.activeInHierarchy;

        private void OnEnable()
        {
            closeRequested = false;

            if (activeRewardedRequest == 0)
            {
                actionInProgress = false;
            }

            BindLocalization();
            BindButtons();
            BindServices();
            RefreshAll();
        }

        private void Update()
        {
            RefreshCountdownIfNeeded();
        }

        private void OnDisable()
        {
            heartFlyAnimator?.Cancel();
            rewardedHeartsToPresent = 0;
            UnbindButtons();
            UnbindServices();
            UnbindLocalization();

            if (activeRewardedRequest == 0)
            {
                actionInProgress = false;
            }

            closeRequested = false;
        }

        private void OnDestroy()
        {
            heartFlyAnimator?.Cancel();
            UnbindLocalization();
            rewardedRequestVersion++;
            activeRewardedRequest = 0;
            ClearRewardedAnalyticsContext();
            InsufficientGold = null;
        }

        private void BindLocalization()
        {
            if (rewardedLifeGainSubscribed ||
                rewardedLifeGainFormat == null ||
                rewardedLifeGainFormat.IsEmpty)
            {
                return;
            }

            rewardedLifeGainArguments[0] = LivesEconomyConfig.RewardedAdLifeGain;
            rewardedLifeGainFormat.Arguments = rewardedLifeGainArguments;
            rewardedLifeGainFormat.StringChanged += HandleRewardedLifeGainChanged;
            rewardedLifeGainSubscribed = true;
        }

        private void UnbindLocalization()
        {
            if (!rewardedLifeGainSubscribed)
            {
                return;
            }

            rewardedLifeGainFormat.StringChanged -= HandleRewardedLifeGainChanged;
            rewardedLifeGainSubscribed = false;
        }

        private void HandleRewardedLifeGainChanged(string localizedText)
        {
            if (rewardedLifeGainText != null)
            {
                rewardedLifeGainText.text = localizedText;
            }
        }

        public bool Open()
        {
            return Open(false);
        }

        public bool OpenOverCurrentPanel()
        {
            return Open(true);
        }

        private bool Open(bool pushOverCurrentPanel)
        {
            if (panelManager != null && panelManager.TryDeferOpen(panel, () => Open(pushOverCurrentPanel))) return true;

            BindServices();

            if (!CanRefill())
            {
                return false;
            }

            if (panelManager == null || panel == null)
            {
                Debug.LogWarning(
                    $"{nameof(LivesRefillPanelController)} cannot open because its panel references are incomplete.",
                    this);
                return false;
            }

            if (IsOpen)
            {
                RefreshAll();
                return true;
            }

            lastDisplayedRemainingSeconds = -1;
            if (pushOverCurrentPanel && panelManager.HasOpenPanel)
            {
                panelManager.Push(panel);
            }
            else
            {
                panelManager.OpenRoot(panel);
            }

            return true;
        }

        public bool Close(Action onClosed = null)
        {
            if (closeRequested)
            {
                return true;
            }

            if (panelManager == null || panel == null)
            {
                return false;
            }

            heartFlyAnimator?.Cancel();
            closeRequested = panelManager.TryClose(panel, onClosed);
            return closeRequested;
        }

        private void BindButtons()
        {
            if (buttonsSubscribed)
            {
                return;
            }

            if (adButton != null)
            {
                adButton.onClick.RemoveListener(HandleAdClicked);
                adButton.onClick.AddListener(HandleAdClicked);
            }

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
                buyButton.onClick.AddListener(HandleBuyClicked);
            }

            buttonsSubscribed = true;
        }

        private void UnbindButtons()
        {
            if (!buttonsSubscribed)
            {
                return;
            }

            adButton?.onClick.RemoveListener(HandleAdClicked);
            buyButton?.onClick.RemoveListener(HandleBuyClicked);
            buttonsSubscribed = false;
        }

        private void BindServices()
        {
            SharedSystemsBootstrap bootstrap = SharedSystemsBootstrap.Instance;
            LivesService availableLivesService = bootstrap?.LivesService;
            LivesPurchaseService availablePurchaseService = bootstrap?.LivesPurchaseService;
            SaveManager availableSaveManager = SaveManager.Instance;
            AdsService availableAdsService = AdsService.Instance;

            if (livesService == availableLivesService &&
                purchaseService == availablePurchaseService &&
                saveManager == availableSaveManager &&
                adsService == availableAdsService &&
                servicesSubscribed)
            {
                return;
            }

            UnbindServices();
            livesService = availableLivesService;
            purchaseService = availablePurchaseService;
            saveManager = availableSaveManager;
            adsService = availableAdsService;

            if (livesService != null)
            {
                livesService.LivesChanged += HandleLivesChanged;
                livesService.RefillStateChanged += HandleRefillStateChanged;
            }

            if (saveManager != null)
            {
                saveManager.GoldChanged += HandleGoldChanged;
            }

            if (adsService != null)
            {
                adsService.OnAdAvailabilityChanged += HandleAdAvailabilityChanged;
                adsService.AdPresentationStateChanged += HandleAdPresentationStateChanged;
            }

            servicesSubscribed = true;
        }

        private void UnbindServices()
        {
            if (!servicesSubscribed)
            {
                return;
            }

            if (livesService != null)
            {
                livesService.LivesChanged -= HandleLivesChanged;
                livesService.RefillStateChanged -= HandleRefillStateChanged;
            }

            if (saveManager != null)
            {
                saveManager.GoldChanged -= HandleGoldChanged;
            }

            if (adsService != null)
            {
                adsService.OnAdAvailabilityChanged -= HandleAdAvailabilityChanged;
                adsService.AdPresentationStateChanged -= HandleAdPresentationStateChanged;
            }

            livesService = null;
            purchaseService = null;
            saveManager = null;
            adsService = null;
            servicesSubscribed = false;
            lastDisplayedRemainingSeconds = -1;
        }

        private void HandleBuyClicked()
        {
            if (actionInProgress || purchaseService == null || !CanRefill())
            {
                return;
            }

            actionInProgress = true;
            RefreshActionState();

            int livesBefore = livesService.CurrentLives;
            heartFlyAnimator?.Prepare();
            LivesPurchaseResult result = purchaseService.TryFillToMaxWithGold();
            int grantedLives = Mathf.Max(0, livesService.CurrentLives - livesBefore);
            if (result == LivesPurchaseResult.Success && grantedLives > 0)
            {
                PresentReward(grantedLives, true);
                return;
            }

            heartFlyAnimator?.Cancel();
            if (result == LivesPurchaseResult.InsufficientGold)
            {
                Action handler = InsufficientGold;
                if (handler != null)
                {
                    handler.Invoke();
                    return;
                }
            }

            actionInProgress = false;
            RefreshAll();

            if (result == LivesPurchaseResult.Success ||
                result == LivesPurchaseResult.NoRefillNeeded ||
                result == LivesPurchaseResult.UnlimitedLives)
            {
                Close();
            }
        }

        private void HandleAdClicked()
        {
            if (actionInProgress || !CanRefill() || !IsRewardedReady())
            {
                return;
            }

            actionInProgress = true;
            rewardGrantedForCurrentRequest = false;
            rewardedHeartsToPresent = 0;
            int requestVersion = ++rewardedRequestVersion;
            activeRewardedRequest = requestVersion;
            BeginRewardedAnalyticsRequest();
            RefreshActionState();

            adsService.ShowRewarded(
                AdPlacement.RewardedLife,
                () => HandleRewardEarned(requestVersion),
                result => HandleRewardedFinished(requestVersion, result));
        }

        private void HandleRewardEarned(int requestVersion)
        {
            if (requestVersion != activeRewardedRequest || rewardGrantedForCurrentRequest)
            {
                return;
            }

            rewardGrantedForCurrentRequest = true;
            TrackRewardedAnalyticsCompleted();
            int livesBefore = livesService?.CurrentLives ?? 0;
            heartFlyAnimator?.Prepare();
            purchaseService?.GrantRewardedLives();
            // Capture the actual grant now; regeneration while the ad closes is not a reward.
            rewardedHeartsToPresent = Mathf.Min(
                LivesEconomyConfig.RewardedAdLifeGain,
                Mathf.Max(0, (livesService?.CurrentLives ?? livesBefore) - livesBefore));
            if (rewardedHeartsToPresent == 0)
            {
                heartFlyAnimator?.Cancel();
            }
        }

        private void HandleRewardedFinished(int requestVersion, AdShowResult result)
        {
            if (requestVersion != activeRewardedRequest)
            {
                return;
            }

            if (!rewardGrantedForCurrentRequest)
            {
                TrackRewardedAnalyticsFailed();
            }

            activeRewardedRequest = 0;
            rewardGrantedForCurrentRequest = false;
            ClearRewardedAnalyticsContext();
            int heartsToPresent = rewardedHeartsToPresent;
            rewardedHeartsToPresent = 0;
            if (heartsToPresent > 0 && result == AdShowResult.Closed && isActiveAndEnabled)
            {
                // Reward is already committed. Wait for the full-screen ad to leave before flying.
                PresentReward(heartsToPresent, false);
                return;
            }

            heartFlyAnimator?.Cancel();
            actionInProgress = false;
            RefreshAll();
            CloseIfRefillNoLongerNeeded();
        }

        private void PresentReward(int grantedLives, bool closeAfterReward)
        {
            if (heartFlyAnimator != null)
            {
                heartFlyAnimator.Play(grantedLives, () => FinishRewardPresentation(closeAfterReward));
            }
            else
            {
                FinishRewardPresentation(closeAfterReward);
            }
        }

        private void FinishRewardPresentation(bool closeAfterReward)
        {
            actionInProgress = false;
            if (!isActiveAndEnabled) return;
            RefreshAll();
            if (closeAfterReward) Close();
            else CloseIfRefillNoLongerNeeded();
        }

        private void BeginRewardedAnalyticsRequest()
        {
            ClearRewardedAnalyticsContext();
            LevelAnalyticsTracker tracker = LevelAnalyticsTracker.Instance;
            if (tracker != null)
            {
                if (!tracker.TryGetLevelContext(
                    out rewardedDisplayedLevelNumber,
                    out rewardedLevelNumber))
                {
                    return;
                }
            }
            else
            {
                MenuLevelProgressController menuLevelProgressController =
                    FindFirstObjectByType<MenuLevelProgressController>();
                if (menuLevelProgressController == null ||
                    !menuLevelProgressController.TryGetCurrentLevelContext(
                        out rewardedDisplayedLevelNumber,
                        out rewardedLevelNumber))
                {
                    return;
                }
            }

            hasRewardedAnalyticsContext = true;
            AnalyticsBootstrap.Instance?.Track(
                AnalyticsEventFactory.CreateRewardedAdRequested(
                    rewardedDisplayedLevelNumber,
                    rewardedLevelNumber,
                    AnalyticsAdPlacements.RewardedLife));
        }

        private void TrackRewardedAnalyticsCompleted()
        {
            if (!hasRewardedAnalyticsContext)
            {
                return;
            }

            AnalyticsBootstrap.Instance?.Track(
                AnalyticsEventFactory.CreateRewardedAdCompleted(
                    rewardedDisplayedLevelNumber,
                    rewardedLevelNumber,
                    AnalyticsAdPlacements.RewardedLife));
            ClearRewardedAnalyticsContext();
        }

        private void TrackRewardedAnalyticsFailed()
        {
            if (!hasRewardedAnalyticsContext)
            {
                return;
            }

            AnalyticsBootstrap.Instance?.Track(
                AnalyticsEventFactory.CreateRewardedAdFailed(
                    rewardedDisplayedLevelNumber,
                    rewardedLevelNumber,
                    AnalyticsAdPlacements.RewardedLife));
        }

        private void ClearRewardedAnalyticsContext()
        {
            rewardedDisplayedLevelNumber = 0;
            rewardedLevelNumber = 0;
            hasRewardedAnalyticsContext = false;
        }

        private void HandleLivesChanged(int currentLives, int maxLives)
        {
            RefreshAll();
            CloseIfRefillNoLongerNeeded();
        }

        private void HandleRefillStateChanged()
        {
            RefreshAll();
            CloseIfRefillNoLongerNeeded();
        }

        private void HandleGoldChanged(int gold)
        {
            RefreshActionState();
        }

        private void HandleAdAvailabilityChanged(AdPlacement placement, bool ready)
        {
            if (placement == AdPlacement.RewardedLife)
            {
                RefreshActionState();
            }
        }

        private void HandleAdPresentationStateChanged()
        {
            RefreshActionState();
        }

        private void RefreshAll()
        {
            if (livesService == null)
            {
                RefreshActionState();
                return;
            }

            currentLivesText?.SetText("{0}", Mathf.Max(0, livesService.CurrentLives));
            goldCostText?.SetText("{0}", purchaseService?.FillToMaxGoldCost ?? 0);
            lastDisplayedRemainingSeconds = -1;
            RefreshCountdownIfNeeded();
            RefreshActionState();
        }

        private void RefreshCountdownIfNeeded()
        {
            if (livesService == null || countdownText == null)
            {
                return;
            }

            long remainingSeconds = CanRefill()
                ? Math.Max(0L, (long)Math.Ceiling(livesService.RemainingRefillTime.TotalSeconds))
                : 0L;

            if (remainingSeconds == lastDisplayedRemainingSeconds)
            {
                return;
            }

            lastDisplayedRemainingSeconds = remainingSeconds;
            countdownText.SetText("{0:00}:{1:00}", remainingSeconds / 60L, remainingSeconds % 60L);
        }

        private void RefreshActionState()
        {
            bool canRefill = CanRefill();

            if (buyButton != null)
            {
                buyButton.interactable = canRefill && !actionInProgress;
            }

            if (adButton != null)
            {
                bool offerVisible = adsService != null &&
                                    adsService.CanShowRewardedOffers;
                if (adButton.gameObject.activeSelf != offerVisible)
                {
                    adButton.gameObject.SetActive(offerVisible);
                }

                adButton.interactable = offerVisible && canRefill &&
                                        !actionInProgress && IsRewardedReady();
            }
        }

        private bool CanRefill()
        {
            return livesService != null &&
                   !livesService.HasInfiniteLives &&
                   livesService.CurrentLives < livesService.MaxLives;
        }

        private bool IsRewardedReady()
        {
            return adsService != null && adsService.IsRewardedReady(AdPlacement.RewardedLife);
        }

        private void CloseIfRefillNoLongerNeeded()
        {
            if (IsOpen && !actionInProgress && !CanRefill())
            {
                Close();
            }
        }
    }
}

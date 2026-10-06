using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Ads.Core;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Audio;
using Game.Shared.Haptics;
using Game.Shared.Navigation;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using Gameplay.Analytics;
using Gameplay.Levels;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Gameplay.UI.HUD
{
    [DisallowMultipleComponent]
    public sealed class LevelCompleteCanvasView : MonoBehaviour
    {
        private enum ResultState
        {
            None,
            Win,
            Lose
        }

        [Header("Result Flow")]
        [SerializeField] private LevelResultFlowController resultFlowController;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private UIInputLockService uiInputLockService;
        [SerializeField] private bool subscribeToResultFlow = true;
        [SerializeField] private bool hideOnAwake = true;

        [Header("Win Panel")]
        [SerializeField] private UIPanel winPanel;
        [SerializeField] private TMP_Text winRibbonText;
        [SerializeField] private TMP_Text earnedGoldText;
        [SerializeField] private Button winContinueButton;
        [SerializeField] private Button doubleRewardButton;
        [SerializeField] private CoinFlyAnimator coinFlyAnimator;
        [SerializeField] private GameplayHudController gameplayHudController;
        [SerializeField] private RectTransform winGoldTarget;

        [Header("Lose Panel")]
        [SerializeField] private UIPanel losePanel;
        [SerializeField] private TMP_Text loseRibbonText;
        [SerializeField] private Button loseContinueButton;
        [SerializeField] private TMP_Text loseContinueButtonText;

        [Header("Localization")]
        [SerializeField] private LocalizedString winLevelCompleteFormat;
        [SerializeField] private LocalizedString loseLevelTitleFormat;
        [SerializeField] private LocalizedString loseTryAgainText;
        [SerializeField] private LocalizedString loseContinueToMenuText;

        [Header("Show Delay")]
        [SerializeField, Min(0f)] private float winShowDelay = 0.5f;
        [SerializeField, Min(0f)] private float loseShowDelay = 0.5f;
        [SerializeField] private bool playResultSfxOnShow = true;
        [SerializeField] private bool playResultHapticsOnShow = true;

        private ResultState currentState;
        private bool actionInProgress;
        private bool rewardGrantedForCurrentRequest;
        private int rewardedRequestVersion;
        private int activeRewardedRequest;
        private int deferredRewardAmountForCurrentRequest;
        private int rewardedDisplayedLevelNumber;
        private int rewardedLevelNumber;
        private bool hasRewardedAnalyticsContext;
        private Tween showDelayTween;
        private AdsService adsService;
        private bool returnToMenuAfterLose;
        private readonly Dictionary<string, object> levelFormatValues =
            new Dictionary<string, object>();
        private object[] levelFormatArguments;
        private int currentDisplayedLevelNumber = 1;
        private string localizedLoseTryAgainText = string.Empty;
        private string localizedLoseContinueToMenuText = string.Empty;
        private bool winLevelFormatSubscribed;
        private bool loseLevelFormatSubscribed;
        private bool loseTryAgainSubscribed;
        private bool loseContinueToMenuSubscribed;
        private bool winGoldForegroundApplied;
        private bool ownsLevel19TransitionInputLock;

        private void Awake()
        {
            CacheReferences();
            ApplyResultPanelPolicies();

            if (hideOnAwake)
            {
                HidePanelsImmediately();
            }
        }

        private void OnEnable()
        {
            CacheReferences();
            ApplyResultPanelPolicies();
            BindLocalization();
            RegisterListeners();
            BindAdsService();
            RefreshButtonStates();
        }

        private void OnDisable()
        {
            KillShowDelayTween();
            InvalidateRewardedRequest();
            RestoreWinGoldForeground();
            UnbindLocalization();
            UnregisterListeners();
            UnbindAdsService();
            SetLevel19TransitionInputLocked(false);
        }

        private void OnDestroy()
        {
            RestoreWinGoldForeground();
            UnbindLocalization();
            InvalidateRewardedRequest();
        }

        public void ShowWin()
        {
            ShowWithDelay(ResultState.Win, winShowDelay);
        }

#if UNITY_EDITOR
        public void ResetForDebugLevelLoad()
        {
            KillShowDelayTween();
            InvalidateRewardedRequest();
            SetLevel19TransitionInputLocked(false);
            HidePanelsImmediately();
        }
#endif

        public void ShowLose()
        {
            ShowWithDelay(ResultState.Lose, loseShowDelay);
        }

        private void ShowWithDelay(ResultState state, float delay)
        {
            KillShowDelayTween();

            if (delay <= 0f)
            {
                Show(state);
                return;
            }

            showDelayTween = DOVirtual
                .DelayedCall(delay, () => Show(state))
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void Show(ResultState state)
        {
            showDelayTween = null;
            CacheReferences();
            UIPanel requestedPanel = state == ResultState.Win ? winPanel : losePanel;
            if (panelManager != null && panelManager.TryDeferOpen(requestedPanel, () => Show(state))) return;

            BindAdsService();

            if (!ValidateReferences(state))
            {
                return;
            }

            currentState = state;
            actionInProgress = false;
            returnToMenuAfterLose = false;
            InvalidateRewardedRequest();

            currentDisplayedLevelNumber = Mathf.Max(
                1,
                resultFlowController.CurrentDisplayedLevelNumber);
            levelFormatValues["level"] = currentDisplayedLevelNumber;
            winLevelCompleteFormat?.RefreshString();
            loseLevelTitleFormat?.RefreshString();

            switch (state)
            {
                case ResultState.Win:
                    earnedGoldText.text = resultFlowController.PreparedWinGoldReward.ToString();
                    panelManager.OpenRoot(winPanel);
                    TryStartAutomaticBaseWinReward();
                    break;

                case ResultState.Lose:
                    returnToMenuAfterLose = resultFlowController.DidFinalizedFailExhaustLives;
                    RefreshLoseContinueText();
                    panelManager.OpenRoot(losePanel);
                    break;
            }

            PlayResultFeedback(state);
            RefreshButtonStates();
        }

        private void HandleWinContinueClicked()
        {
            if (actionInProgress || currentState != ResultState.Win || resultFlowController == null)
            {
                return;
            }

            actionInProgress = true;
            RefreshButtonStates();

            int rewardAmount = resultFlowController.PreparedWinGoldReward;
            bool shouldDeferToMenu = ShouldReturnToMenuAfterWin();
            if (!resultFlowController.HasClaimedWinReward &&
                !resultFlowController.TryClaimBaseWinReward())
            {
                actionInProgress = false;
                RefreshButtonStates();
                return;
            }

            coinFlyAnimator?.CancelActiveAnimation();
            if (shouldDeferToMenu)
            {
                SetLevel19TransitionInputLocked(IsLevel19Milestone());
                CloseResultPanel(winPanel, () => TryReturnToMenu(rewardAmount));
            }
            else
            {
                CloseResultPanel(winPanel, () => resultFlowController.ContinueClaimedWin());
            }
        }

        private void TryStartAutomaticBaseWinReward()
        {
            if (currentState != ResultState.Win || ShouldReturnToMenuAfterWin() ||
                resultFlowController == null || !resultFlowController.TryClaimBaseWinReward())
            {
                return;
            }

            if (!winGoldForegroundApplied && gameplayHudController != null)
            {
                gameplayHudController.BringGoldDisplayToFront();
                winGoldForegroundApplied = true;
            }

            coinFlyAnimator?.Play(
                resultFlowController.PreparedWinGoldReward,
                null,
                winGoldTarget);
        }

        private void HandleDoubleRewardClicked()
        {
            if (actionInProgress || currentState != ResultState.Win ||
                resultFlowController == null || !resultFlowController.CanClaimWinReward)
            {
                return;
            }

            BindAdsService();
            if (adsService == null || !adsService.IsRewardedReady(AdPlacement.RewardedDoubleGold))
            {
                RefreshButtonStates();
                return;
            }

            actionInProgress = true;
            rewardGrantedForCurrentRequest = false;
            int requestVersion = ++rewardedRequestVersion;
            activeRewardedRequest = requestVersion;
            deferredRewardAmountForCurrentRequest = 0;
            BeginRewardedAnalyticsRequest();
            RefreshButtonStates();

            adsService.ShowRewarded(
                AdPlacement.RewardedDoubleGold,
                () => HandleDoubleRewardEarned(requestVersion),
                result => HandleDoubleRewardFinished(requestVersion));
        }

        private void HandleDoubleRewardEarned(int requestVersion)
        {
            if (!IsCurrentRewardedRequest(requestVersion) || rewardGrantedForCurrentRequest)
            {
                return;
            }

            rewardGrantedForCurrentRequest = true;
            TrackRewardedAnalyticsCompleted();
            int rewardAmount = (int)System.Math.Min(
                int.MaxValue,
                (long)resultFlowController.PreparedWinGoldReward * 2L);
            if (!resultFlowController.TryClaimDoubleWinReward())
            {
                activeRewardedRequest = 0;
                actionInProgress = false;
                RefreshButtonStates();
                return;
            }

            if (ShouldReturnToMenuAfterWin())
            {
                deferredRewardAmountForCurrentRequest = rewardAmount;
                RefreshButtonStates();
                return;
            }

            activeRewardedRequest = 0;
            RefreshButtonStates();
            if (!winGoldForegroundApplied && gameplayHudController != null)
            {
                gameplayHudController.BringGoldDisplayToFront();
                winGoldForegroundApplied = true;
            }

            coinFlyAnimator?.Play(rewardAmount, null, winGoldTarget);
            CloseResultPanel(winPanel, () => resultFlowController.ContinueClaimedWin());
        }

        private void HandleDoubleRewardFinished(int requestVersion)
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
            if (rewardGrantedForCurrentRequest && deferredRewardAmountForCurrentRequest > 0)
            {
                int rewardAmount = deferredRewardAmountForCurrentRequest;
                deferredRewardAmountForCurrentRequest = 0;
                RefreshButtonStates();
                SetLevel19TransitionInputLocked(IsLevel19Milestone());
                CloseResultPanel(winPanel, () => TryReturnToMenu(rewardAmount));
                return;
            }

            if (!rewardGrantedForCurrentRequest)
            {
                actionInProgress = false;
            }

            RefreshButtonStates();
        }

        private void HandleLoseContinueClicked()
        {
            if (actionInProgress || currentState != ResultState.Lose || resultFlowController == null)
            {
                return;
            }

            if (resultFlowController.IsFailOfferPurchaseLocked)
            {
                return;
            }

            if (returnToMenuAfterLose)
            {
                TryReturnToMenu();
                return;
            }

            actionInProgress = true;
            RefreshButtonStates();
            CloseResultPanel(losePanel, resultFlowController.TryAgain);
        }

        private void TryReturnToMenu(int deferredCoinFlyAmount = 0)
        {
            SceneLoader sceneLoader = SceneLoader.Instance;
            if (sceneLoader == null)
            {
                Debug.LogWarning(
                    $"{nameof(LevelCompleteCanvasView)} cannot load Menu because {nameof(SceneLoader)} is unavailable.",
                    this);
                SetLevel19TransitionInputLocked(false);
                return;
            }

            if (sceneLoader.State != SceneTransitionState.Idle)
            {
                SetLevel19TransitionInputLocked(false);
                return;
            }

            if (levelSessionController == null)
            {
                Debug.LogWarning(
                    $"{nameof(LevelCompleteCanvasView)} cannot exit gameplay because no {nameof(LevelSessionController)} is assigned.",
                    this);
                SetLevel19TransitionInputLocked(false);
                return;
            }

            if (levelSessionController.GameplayState != GameplaySessionState.Exiting &&
                !levelSessionController.TryBeginExit())
            {
                SetLevel19TransitionInputLocked(false);
                return;
            }

            actionInProgress = true;
            RefreshButtonStates();
            coinFlyAnimator?.CancelActiveAnimation();
            if (deferredCoinFlyAmount > 0)
            {
                CoinFlyPresentationHandoff.Defer(
                    deferredCoinFlyAmount,
                    currentDisplayedLevelNumber == AdsService.RewardedUnlockCompletedLevel,
                    IsLevel19Milestone());
            }

            sceneLoader.LoadMenu();
            if (ownsLevel19TransitionInputLock &&
                sceneLoader.State == SceneTransitionState.Idle)
            {
                CoinFlyPresentationHandoff.Cancel();
                SetLevel19TransitionInputLocked(false);
            }
        }

        private void CloseResultPanel(UIPanel panel, System.Action onClosed)
        {
            if (panelManager == null || panel == null || !panelManager.TryClose(panel, () =>
                {
                    if (panel == winPanel)
                    {
                        RestoreWinGoldForeground();
                    }

                    currentState = ResultState.None;
                    onClosed?.Invoke();
                }))
            {
                Debug.LogError(
                    $"{nameof(LevelCompleteCanvasView)} on '{name}' cannot close the active result panel through {nameof(PanelManager)}.",
                    this);
                actionInProgress = false;
                SetLevel19TransitionInputLocked(false);
                RefreshButtonStates();
            }
        }

        private void RegisterListeners()
        {
            if (winContinueButton != null)
            {
                winContinueButton.onClick.RemoveListener(HandleWinContinueClicked);
                winContinueButton.onClick.AddListener(HandleWinContinueClicked);
            }

            if (doubleRewardButton != null)
            {
                doubleRewardButton.onClick.RemoveListener(HandleDoubleRewardClicked);
                doubleRewardButton.onClick.AddListener(HandleDoubleRewardClicked);
            }

            if (loseContinueButton != null)
            {
                loseContinueButton.onClick.RemoveListener(HandleLoseContinueClicked);
                loseContinueButton.onClick.AddListener(HandleLoseContinueClicked);
            }

            if (!subscribeToResultFlow || resultFlowController == null)
            {
                return;
            }

            resultFlowController.WinAnimationTriggered -= ShowWin;
            resultFlowController.WinAnimationTriggered += ShowWin;
            resultFlowController.LoseAnimationTriggered -= ShowLose;
            resultFlowController.LoseAnimationTriggered += ShowLose;
        }

        private void UnregisterListeners()
        {
            winContinueButton?.onClick.RemoveListener(HandleWinContinueClicked);
            doubleRewardButton?.onClick.RemoveListener(HandleDoubleRewardClicked);
            loseContinueButton?.onClick.RemoveListener(HandleLoseContinueClicked);

            if (resultFlowController == null)
            {
                return;
            }

            resultFlowController.WinAnimationTriggered -= ShowWin;
            resultFlowController.LoseAnimationTriggered -= ShowLose;
        }

        private void BindLocalization()
        {
            currentDisplayedLevelNumber = resultFlowController != null
                ? Mathf.Max(1, resultFlowController.CurrentDisplayedLevelNumber)
                : 1;
            levelFormatValues["level"] = currentDisplayedLevelNumber;
            if (levelFormatArguments == null)
            {
                levelFormatArguments = new object[] { levelFormatValues };
            }

            if (!winLevelFormatSubscribed &&
                winLevelCompleteFormat != null &&
                !winLevelCompleteFormat.IsEmpty)
            {
                winLevelCompleteFormat.Arguments = levelFormatArguments;
                winLevelCompleteFormat.StringChanged += HandleWinLevelCompleteChanged;
                winLevelFormatSubscribed = true;
            }

            if (!loseLevelFormatSubscribed &&
                loseLevelTitleFormat != null &&
                !loseLevelTitleFormat.IsEmpty)
            {
                loseLevelTitleFormat.Arguments = levelFormatArguments;
                loseLevelTitleFormat.StringChanged += HandleLoseLevelTitleChanged;
                loseLevelFormatSubscribed = true;
            }

            if (!loseTryAgainSubscribed &&
                loseTryAgainText != null &&
                !loseTryAgainText.IsEmpty)
            {
                loseTryAgainText.StringChanged += HandleLoseTryAgainChanged;
                loseTryAgainSubscribed = true;
            }

            if (!loseContinueToMenuSubscribed &&
                loseContinueToMenuText != null &&
                !loseContinueToMenuText.IsEmpty)
            {
                loseContinueToMenuText.StringChanged += HandleLoseContinueToMenuChanged;
                loseContinueToMenuSubscribed = true;
            }
        }

        private void UnbindLocalization()
        {
            if (winLevelFormatSubscribed)
            {
                winLevelCompleteFormat.StringChanged -= HandleWinLevelCompleteChanged;
                winLevelFormatSubscribed = false;
            }

            if (loseLevelFormatSubscribed)
            {
                loseLevelTitleFormat.StringChanged -= HandleLoseLevelTitleChanged;
                loseLevelFormatSubscribed = false;
            }

            if (loseTryAgainSubscribed)
            {
                loseTryAgainText.StringChanged -= HandleLoseTryAgainChanged;
                loseTryAgainSubscribed = false;
            }

            if (loseContinueToMenuSubscribed)
            {
                loseContinueToMenuText.StringChanged -= HandleLoseContinueToMenuChanged;
                loseContinueToMenuSubscribed = false;
            }
        }

        private void HandleWinLevelCompleteChanged(string localizedText)
        {
            if (currentState == ResultState.Win && winRibbonText != null)
            {
                winRibbonText.text = localizedText;
            }
        }

        private void HandleLoseLevelTitleChanged(string localizedText)
        {
            if (currentState == ResultState.Lose && loseRibbonText != null)
            {
                loseRibbonText.text = localizedText;
            }
        }

        private void HandleLoseTryAgainChanged(string localizedText)
        {
            localizedLoseTryAgainText = localizedText;
            RefreshLoseContinueText();
        }

        private void HandleLoseContinueToMenuChanged(string localizedText)
        {
            localizedLoseContinueToMenuText = localizedText;
            RefreshLoseContinueText();
        }

        private void BindAdsService()
        {
            AdsService currentAdsService = AdsService.Instance;
            if (adsService == currentAdsService)
            {
                return;
            }

            UnbindAdsService();
            adsService = currentAdsService;
            if (adsService != null)
            {
                adsService.OnAdAvailabilityChanged -= HandleAdAvailabilityChanged;
                adsService.OnAdAvailabilityChanged += HandleAdAvailabilityChanged;
                adsService.AdPresentationStateChanged -= HandleAdPresentationStateChanged;
                adsService.AdPresentationStateChanged += HandleAdPresentationStateChanged;
            }
        }

        private void UnbindAdsService()
        {
            if (adsService != null)
            {
                adsService.OnAdAvailabilityChanged -= HandleAdAvailabilityChanged;
                adsService.AdPresentationStateChanged -= HandleAdPresentationStateChanged;
                adsService = null;
            }
        }

        private void HandleAdAvailabilityChanged(AdPlacement placement, bool isReady)
        {
            if (placement == AdPlacement.RewardedDoubleGold)
            {
                RefreshButtonStates();
            }
        }

        private void HandleAdPresentationStateChanged()
        {
            RefreshButtonStates();
        }

        private bool ShouldReturnToMenuAfterWin()
        {
            return currentDisplayedLevelNumber >= AdsService.RewardedUnlockCompletedLevel;
        }

        private bool IsLevel19Milestone()
        {
            return currentDisplayedLevelNumber == AdsService.MonetizationUnlockCompletedLevel;
        }

        private void SetLevel19TransitionInputLocked(bool locked)
        {
            if (ownsLevel19TransitionInputLock == locked)
            {
                return;
            }

            ownsLevel19TransitionInputLock = locked;
            if (uiInputLockService == null)
            {
                return;
            }

            if (locked)
            {
                uiInputLockService.LockUIInput();
            }
            else
            {
                uiInputLockService.UnlockUIInput();
            }
        }

        private void RestoreWinGoldForeground()
        {
            if (!winGoldForegroundApplied)
            {
                return;
            }

            gameplayHudController?.RestoreGoldDisplaySortingOrder();
            winGoldForegroundApplied = false;
        }

        private bool IsCurrentRewardedRequest(int requestVersion)
        {
            return requestVersion == activeRewardedRequest &&
                   isActiveAndEnabled &&
                   currentState == ResultState.Win &&
                   resultFlowController != null &&
                   resultFlowController.CanClaimWinReward;
        }

        private void InvalidateRewardedRequest()
        {
            rewardedRequestVersion++;
            activeRewardedRequest = 0;
            rewardGrantedForCurrentRequest = false;
            deferredRewardAmountForCurrentRequest = 0;
            ClearRewardedAnalyticsContext();
        }

        private void BeginRewardedAnalyticsRequest()
        {
            ClearRewardedAnalyticsContext();
            LevelAnalyticsTracker tracker = LevelAnalyticsTracker.Instance;
            if (tracker == null ||
                !tracker.TryGetLevelContext(
                    out rewardedDisplayedLevelNumber,
                    out rewardedLevelNumber))
            {
                return;
            }

            hasRewardedAnalyticsContext = true;
            AnalyticsBootstrap.Instance?.Track(
                AnalyticsEventFactory.CreateRewardedAdRequested(
                    rewardedDisplayedLevelNumber,
                    rewardedLevelNumber,
                    AnalyticsAdPlacements.RewardedDoubleGold));
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
                    AnalyticsAdPlacements.RewardedDoubleGold));
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
                    AnalyticsAdPlacements.RewardedDoubleGold));
        }

        private void ClearRewardedAnalyticsContext()
        {
            rewardedDisplayedLevelNumber = 0;
            rewardedLevelNumber = 0;
            hasRewardedAnalyticsContext = false;
        }

        private void RefreshButtonStates()
        {
            bool canClaimWinReward = !actionInProgress &&
                                     currentState == ResultState.Win &&
                                     resultFlowController != null &&
                                     resultFlowController.CanClaimWinReward;
            bool canContinueWin = !actionInProgress &&
                                  currentState == ResultState.Win &&
                                  resultFlowController != null &&
                                  (resultFlowController.CanClaimWinReward ||
                                   resultFlowController.HasClaimedWinReward);

            if (winContinueButton != null)
            {
                winContinueButton.interactable = canContinueWin;
            }

            if (doubleRewardButton != null)
            {
                bool offerVisible = adsService != null &&
                                    adsService.CanShowRewardedOffers;
                if (doubleRewardButton.gameObject.activeSelf != offerVisible)
                {
                    doubleRewardButton.gameObject.SetActive(offerVisible);
                }

                doubleRewardButton.interactable = offerVisible &&
                                                  canClaimWinReward &&
                                                  adsService != null &&
                                                  adsService.IsRewardedReady(AdPlacement.RewardedDoubleGold);
            }

            if (loseContinueButton != null)
            {
                loseContinueButton.interactable = !actionInProgress && currentState == ResultState.Lose;
            }
        }

        private bool ValidateReferences(ResultState state)
        {
            if (resultFlowController == null || panelManager == null)
            {
                Debug.LogError(
                    $"{nameof(LevelCompleteCanvasView)} on '{name}' requires both {nameof(LevelResultFlowController)} and {nameof(PanelManager)} references.",
                    this);
                return false;
            }

            bool valid = state == ResultState.Win
                ? winPanel != null && winRibbonText != null && earnedGoldText != null &&
                  winContinueButton != null && doubleRewardButton != null
                : losePanel != null && loseRibbonText != null && loseContinueButton != null &&
                  loseContinueButtonText != null && levelSessionController != null;

            if (!valid)
            {
                Debug.LogError(
                    $"{nameof(LevelCompleteCanvasView)} on '{name}' is missing critical {state} panel references.",
                    this);
            }

            return valid;
        }

        private void CacheReferences()
        {
            if (panelManager == null)
            {
                panelManager = GetComponentInParent<PanelManager>(true);
            }

            if (resultFlowController == null)
            {
                resultFlowController = GetComponent<LevelResultFlowController>();
            }

            if (levelSessionController == null)
            {
                levelSessionController = GetComponent<LevelSessionController>();
            }
        }

        private void RefreshLoseContinueText()
        {
            if (loseContinueButtonText == null)
            {
                return;
            }

            string localizedText = returnToMenuAfterLose
                ? localizedLoseContinueToMenuText
                : localizedLoseTryAgainText;
            if (!string.IsNullOrEmpty(localizedText))
            {
                loseContinueButtonText.text = localizedText;
            }
        }

        private void ApplyResultPanelPolicies()
        {
            winPanel?.SetAllowBackgroundDismiss(false);
            losePanel?.SetAllowBackgroundDismiss(false);
        }

        private void HidePanelsImmediately()
        {
            RestoreWinGoldForeground();
            winPanel?.HideImmediately();
            losePanel?.HideImmediately();
            currentState = ResultState.None;
            actionInProgress = false;
            returnToMenuAfterLose = false;
        }

        private void KillShowDelayTween()
        {
            showDelayTween?.Kill(false);
            showDelayTween = null;
        }

        private void PlayResultFeedback(ResultState state)
        {
            if (playResultSfxOnShow)
            {
                AudioManager.Instance?.PlaySfx(
                    state == ResultState.Win ? AudioKey.Win : AudioKey.FailPanel);
            }

            if (playResultHapticsOnShow)
            {
                HapticManager.Instance?.Play(
                    state == ResultState.Win ? HapticType.Success : HapticType.Failure,
                    true);
            }
        }
    }
}

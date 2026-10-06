using System;
using Coffee.UIEffects;
using DG.Tweening;
using Game.Shared.Ads.Core;
using Game.Shared.Bootstrap;
using Game.Shared.Save;
using Game.Shared.Store;
using Gameplay.Analytics;
using Gameplay.Conveyor;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using UnityEngine;
using UnityEngine.Events;

namespace Gameplay.Levels
{
    [DisallowMultipleComponent]
    public sealed class LevelResultFlowController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private LevelBuildController levelBuildController;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private TargetLaneController targetLaneController;
        [SerializeField] private ConveyorController conveyorController;
        [SerializeField] private GameplaySpeedController gameplaySpeedController;
        [SerializeField] private LevelAnalyticsTracker levelAnalyticsTracker;

        [Header("No Interaction Speed Up")]
        [SerializeField] private bool enableNoInteractableSpeedUp = true;
        [SerializeField, Min(1f)] private float noInteractableSpeedMultiplier = 1.5f;
        [SerializeField, Min(0.02f)] private float noInteractableCheckInterval = 0.1f;

        [Header("Win Stuff")]
        [SerializeField] private GameObject sparkleFxObj;
        [SerializeField] private UIEffect shinyUIEffect;
        [SerializeField, Min(0f)] private float winPanelDelay = 0.5f;

        [Header("Lose Detection")]
        [SerializeField] private bool enableFullConveyorLose = true;
        [SerializeField, Min(0f)] private float failDelay = 0.5f;
        [SerializeField, Min(0.02f)] private float loseCheckInterval = 0.1f;

        [Header("Win Events")]
        [SerializeField] private UnityEvent onWinLogic;
        [SerializeField] private UnityEvent onWinAnimation;

        [Header("Lose Events")]
        [SerializeField] private UnityEvent onLoseLogic;
        [SerializeField] private UnityEvent onLoseAnimation;

        private bool resultActive;
        private bool fastForwardActive;
        private float nextNoInteractableCheckTime;
        private float nextLoseCheckTime;
        private float loseConditionStartTime = -1f;
        private LevelDefinition cachedLevel;
        private SaveManager saveManager;
        private bool failedResultActive;
        private bool failRecoveryPending;
        private string failSessionToken = string.Empty;
        private int preparedWinGoldReward;
        private int preparedWinDisplayedLevel;
        private bool winRewardClaimed;
        private bool winPresentationPending;
        private Tween pendingWinPanelTween;
        private bool hasReportedLogicFailCondition;
        private bool lastReportedLogicFailConditionMet;

#if UNITY_EDITOR
        private bool debugImmediateResult;

        public bool TryTriggerImmediateResultForEditor(bool win)
        {
            CacheReferences();
            if (!Application.isPlaying || !isActiveAndEnabled || resultActive ||
                levelSessionController == null || !levelSessionController.IsPlaying)
            {
                return false;
            }

            levelAnalyticsTracker?.SuppressCurrentAttemptForDebug();
            debugImmediateResult = true;
            try
            {
                if (win)
                {
                    if (!BeginWin())
                    {
                        return false;
                    }

                    CompletePendingWinPresentation();
                    return true;
                }

                TriggerLose();
                return FinalizePendingFail();
            }
            finally
            {
                debugImmediateResult = false;
            }
        }
#endif

        public event Action WinLogicTriggered;
        public event Action WinAnimationTriggered;
        public event Action LoseLogicTriggered;
        public event Action LoseAnimationTriggered;
        public event Action FailRecoveryRequested;
        public event Action LogicFailConditionChanged;
        public bool IsFailOfferPurchaseLocked => IsCurrentFailSessionPurchaseLocked();
        public bool IsFailRecoveryPending => failRecoveryPending;
        public bool IsLogicFailConditionMet =>
            enableFullConveyorLose &&
            conveyorController != null &&
            targetLaneController != null &&
            conveyorController.IsFull &&
            !targetLaneController.CanConveyorStillFillAnyTarget(
                conveyorController,
                levelBuildController != null ? levelBuildController.ConveyorEntryZone : null);
        public int PreparedWinGoldReward => preparedWinGoldReward;
        public int CurrentDisplayedLevelNumber => levelSessionController != null
            ? Mathf.Max(1, levelSessionController.DisplayedLevelNumber)
            : Mathf.Max(1, preparedWinDisplayedLevel);
        public bool CanClaimWinReward => resultActive && !failedResultActive &&
                                         preparedWinGoldReward > 0 && !winRewardClaimed;
        public bool HasClaimedWinReward => resultActive && !failedResultActive &&
                                           winRewardClaimed;
        public bool DidFinalizedFailExhaustLives
        {
            get
            {
                Game.Shared.Lives.LivesService livesService =
                    SharedSystemsBootstrap.Instance?.LivesService;
                return failedResultActive &&
                       levelSessionController != null &&
                       levelSessionController.HasSpentLifeForCurrentAttempt &&
                       livesService != null &&
                       livesService.IsEnabled &&
                       !livesService.HasInfiniteLives &&
                       livesService.CurrentLives <= 0;
            }
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            SubscribeToTargetLane();
        }

        private void OnDisable()
        {
            AdsService.Instance?.ClearInterstitialResult(this);
            CancelPendingWinPanelDelay();
            InvalidateFailedSession();
            UnsubscribeFromTargetLane();
            if (fastForwardActive)
            {
                SetFastForward(false);
            }
        }

        private void OnValidate()
        {
            noInteractableSpeedMultiplier = Mathf.Max(1f, noInteractableSpeedMultiplier);
            noInteractableCheckInterval = Mathf.Max(0.02f, noInteractableCheckInterval);
            winPanelDelay = Mathf.Max(0f, winPanelDelay);
            failDelay = Mathf.Max(0f, failDelay);
            loseCheckInterval = Mathf.Max(0.02f, loseCheckInterval);
        }

        private void Update()
        {
            UpdateNoInteractableSpeedUp();
            UpdateLoseDetection();
        }

        private void UpdateNoInteractableSpeedUp()
        {
            if (!enableNoInteractableSpeedUp || resultActive ||
                levelSessionController == null || !levelSessionController.IsPlaying ||
                Time.unscaledTime < nextNoInteractableCheckTime)
            {
                return;
            }

            nextNoInteractableCheckTime = Time.unscaledTime + noInteractableCheckInterval;
            bool shouldFastForward =
                sourceBoxBoardController != null &&
                sourceBoxBoardController.HasBuiltBoard &&
                !sourceBoxBoardController.HasUserInteractableSourceBox() &&
                !sourceBoxBoardController.HasPendingSpawnerSourceBoxes &&
                !sourceBoxBoardController.IsGiftBoxResolving;

            SetFastForward(shouldFastForward);
        }

        private void UpdateLoseDetection()
        {
            if (!enableFullConveyorLose || resultActive ||
                levelSessionController == null || !levelSessionController.IsPlaying ||
                Time.unscaledTime < nextLoseCheckTime)
            {
                return;
            }

            nextLoseCheckTime = Time.unscaledTime + loseCheckInterval;

            bool loseConditionMet = IsLogicFailConditionMet;
            ReportLogicFailCondition(loseConditionMet);

            if (!loseConditionMet)
            {
                loseConditionStartTime = -1f;
                return;
            }

            if (loseConditionStartTime < 0f)
            {
                loseConditionStartTime = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - loseConditionStartTime >= failDelay)
            {
                TriggerLose();
            }
        }

        private void ReportLogicFailCondition(bool conditionMet)
        {
            if (hasReportedLogicFailCondition && lastReportedLogicFailConditionMet == conditionMet)
            {
                return;
            }

            hasReportedLogicFailCondition = true;
            lastReportedLogicFailConditionMet = conditionMet;
            LogicFailConditionChanged?.Invoke();
        }

        public void TriggerWin()
        {
            if (BeginWin())
            {
                BeginWinPanelDelay();
            }
        }

        private void HandleAllLanesCompleted()
        {
            BeginWin();
        }

        private bool BeginWin()
        {
            if (resultActive)
            {
                return false;
            }

            if (sourceBoxBoardController != null && sourceBoxBoardController.HasSealedSources
#if UNITY_EDITOR
                && !debugImmediateResult
#endif
                )
            {
                Debug.LogError($"{nameof(LevelResultFlowController)} on '{name}' ignored level completion while board-feature-covered SourceBoxes are still sealed.", this);
                return false;
            }

            if (levelSessionController == null || !levelSessionController.TryEnterWonState())
            {
                return false;
            }

            CacheCurrentLevel();
            LevelDefinition completedLevel = GetCurrentOrCachedLevel();
            resultActive = true;
            winPresentationPending = true;
            InvalidateFailedSession();
            SetFastForward(false);
            PrepareWinReward(completedLevel);
            levelAnalyticsTracker?.CompleteLevel();
            levelSessionController?.MarkCurrentLevelCompleted();
            AdsService.Instance?.RecordWinForInterstitial(this, CurrentDisplayedLevelNumber);

            WinLogicTriggered?.Invoke();
            onWinLogic?.Invoke();

            return true;
        }

        private void BeginWinPanelDelay()
        {
            if (!resultActive || failedResultActive || !winPresentationPending ||
                (pendingWinPanelTween != null && pendingWinPanelTween.IsActive()))
            {
                return;
            }

            if (winPanelDelay <= 0f)
            {
                CompletePendingWinPresentation();
                return;
            }

            pendingWinPanelTween = DOVirtual
                .DelayedCall(winPanelDelay, () =>
                {
                    pendingWinPanelTween = null;
                    CompletePendingWinPresentation();
                }, false)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void CompletePendingWinPresentation()
        {
            if (!resultActive || failedResultActive || !winPresentationPending ||
                levelSessionController == null ||
                levelSessionController.GameplayState != GameplaySessionState.Won)
            {
                return;
            }

            winPresentationPending = false;
            WinAnimationTriggered?.Invoke();
            onWinAnimation?.Invoke();

            if (sparkleFxObj != null)
            {
                sparkleFxObj.SetActive(true);
            }
            if (shinyUIEffect != null)
            {
                shinyUIEffect.enabled = true;
            }
        }

        public void TriggerLose()
        {
            if (resultActive)
            {
                return;
            }

            if (levelSessionController == null || !levelSessionController.TryEnterRecoveryState())
            {
                return;
            }

            CacheCurrentLevel();
            resultActive = true;
            ResetWinRewardState();
            failRecoveryPending = true;
            failedResultActive = false;
            failSessionToken = Guid.NewGuid().ToString("N");
            SetFastForward(false);
#if UNITY_EDITOR
            // Immediate debug loss finalizes below without opening recovery UI or spending continue credits.
            if (debugImmediateResult)
            {
                return;
            }
#endif
            conveyorController?.PlayWarningGlow();
            FailRecoveryRequested?.Invoke();
        }

        public bool FinalizePendingFail()
        {
            if (!failRecoveryPending || !resultActive || levelSessionController == null ||
                !levelSessionController.TryFinalizeRecoveryAsFailed())
            {
                return false;
            }

            failRecoveryPending = false;
            failedResultActive = true;
            failSessionToken = string.Empty;
            levelSessionController.TrySpendLifeForCurrentAttempt();
            levelAnalyticsTracker?.FailLevel();
            AdsService.Instance?.RecordFinalizedLoseForInterstitial(this, CurrentDisplayedLevelNumber);

            LoseLogicTriggered?.Invoke();
            onLoseLogic?.Invoke();
            LoseAnimationTriggered?.Invoke();
            onLoseAnimation?.Invoke();

            if (sparkleFxObj != null)
            {
                sparkleFxObj.SetActive(false);
            }

            if (shinyUIEffect != null)
            {
                shinyUIEffect.enabled = false;
            }

            return true;
        }

        public bool TryCaptureRecoverySession(out string sessionToken)
        {
            if (isActiveAndEnabled && resultActive && failRecoveryPending)
            {
                sessionToken = failSessionToken;
                return true;
            }

            sessionToken = string.Empty;
            return false;
        }

        public bool CanRecoverSession(string sessionToken)
        {
            return isActiveAndEnabled && resultActive && failRecoveryPending &&
                   !string.IsNullOrEmpty(sessionToken) &&
                   string.Equals(sessionToken, failSessionToken, StringComparison.Ordinal) &&
                   levelSessionController != null &&
                   levelSessionController.GameplayState == GameplaySessionState.Recovering;
        }

        public bool TryCompleteRecovery(string sessionToken)
        {
            if (!CanRecoverSession(sessionToken) ||
                !levelSessionController.TryResumeFromRecovery())
            {
                return false;
            }

            ResetResultState();
            return true;
        }

        public void ResetForDebugLevelLoad()
        {
            ResetResultState();
            CacheCurrentLevel();
        }

        public void TryAgain()
        {
            if (IsCurrentFailSessionPurchaseLocked())
            {
                Debug.LogWarning(
                    $"{nameof(LevelResultFlowController)} on '{name}' kept the current fail session open because its Fail Offer purchase is still being fulfilled.",
                    this);
                return;
            }

            if (AdsService.Instance != null && AdsService.Instance.TryRunResultExitInterstitial(TryAgain)) return;

            ResetResultState();

            if (levelSessionController != null)
            {
                levelSessionController.RetryCurrentLevel();
                CacheCurrentLevel();
                return;
            }

            LevelDefinition levelToRetry = GetCurrentOrCachedLevel();
            if (levelBuildController != null && levelToRetry != null)
            {
                levelBuildController.BuildLevel(levelToRetry);
                cachedLevel = levelToRetry;
                return;
            }

            Debug.LogWarning($"{nameof(LevelResultFlowController)} on '{name}' cannot retry because no level session or current level is available.", this);
        }

        public void Continue()
        {
            if (resultActive && !failedResultActive)
            {
                if (HasClaimedWinReward || TryClaimBaseWinReward())
                {
                    ContinueClaimedWin();
                }

                return;
            }

            if (IsCurrentFailSessionPurchaseLocked())
            {
                Debug.LogWarning(
                    $"{nameof(LevelResultFlowController)} on '{name}' kept the current fail session open because its Fail Offer purchase is still being fulfilled.",
                    this);
                return;
            }

            if (!TryContinueFailedLevel())
            {
                Debug.LogWarning(
                    $"{nameof(LevelResultFlowController)} on '{name}' cannot continue because the current level session is unavailable.",
                    this);
            }
        }

        public bool TryClaimBaseWinReward()
        {
            return TryClaimWinReward(1);
        }

        public bool TryClaimDoubleWinReward()
        {
            return TryClaimWinReward(2);
        }

        public bool ContinueClaimedWin()
        {
            if (!resultActive || failedResultActive || !winRewardClaimed)
            {
                return false;
            }

            if (AdsService.Instance != null &&
                AdsService.Instance.TryRunResultExitInterstitial(() => ContinueClaimedWin())) return true;

            if (levelSessionController == null ||
                !levelSessionController.TryContinueToCurrentLevel())
            {
                Debug.LogError(
                    $"{nameof(LevelResultFlowController)} on '{name}' cannot continue the completed level because the current level session is unavailable.",
                    this);
                return false;
            }

            ResetResultState();
            CacheCurrentLevel();
            return true;
        }

        private bool TryContinueFailedLevel()
        {
            if (levelSessionController == null ||
                !levelSessionController.TryContinueToCurrentLevel())
            {
                return false;
            }

            ResetResultState();
            CacheCurrentLevel();
            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("Trigger Win")]
        private void TriggerWinFromContextMenu()
        {
            TriggerWin();
        }

        [ContextMenu("Trigger Lose")]
        private void TriggerLoseFromContextMenu()
        {
            TriggerLose();
        }
#endif

        private void CacheReferences()
        {
            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>();
            }

            if (levelBuildController == null)
            {
                levelBuildController = FindFirstObjectByType<LevelBuildController>();
            }

            if (sourceBoxBoardController == null)
            {
                sourceBoxBoardController = FindFirstObjectByType<SourceBoxBoardController>();
            }

            if (targetLaneController == null)
            {
                targetLaneController = FindFirstObjectByType<TargetLaneController>();
            }

            if (conveyorController == null)
            {
                conveyorController = FindFirstObjectByType<ConveyorController>();
            }

            if (gameplaySpeedController == null)
            {
                gameplaySpeedController = FindFirstObjectByType<GameplaySpeedController>();
            }

            if (levelAnalyticsTracker == null)
            {
                levelAnalyticsTracker = LevelAnalyticsTracker.Instance != null
                    ? LevelAnalyticsTracker.Instance
                    : FindFirstObjectByType<LevelAnalyticsTracker>();
            }

            CacheCurrentLevel();
        }

        private void SubscribeToTargetLane()
        {
            if (targetLaneController != null)
            {
                targetLaneController.AllLanesCompleted -= HandleAllLanesCompleted;
                targetLaneController.AllLanesCompleted += HandleAllLanesCompleted;
                targetLaneController.AllLanesCompletionPresentationCompleted -= BeginWinPanelDelay;
                targetLaneController.AllLanesCompletionPresentationCompleted += BeginWinPanelDelay;
            }
        }

        private void UnsubscribeFromTargetLane()
        {
            if (targetLaneController != null)
            {
                targetLaneController.AllLanesCompleted -= HandleAllLanesCompleted;
                targetLaneController.AllLanesCompletionPresentationCompleted -= BeginWinPanelDelay;
            }
        }

        private void ResetResultState()
        {
            AdsService.Instance?.ClearInterstitialResult(this);
            CancelPendingWinPanelDelay();
            resultActive = false;
            winPresentationPending = false;
            hasReportedLogicFailCondition = false;
            InvalidateFailedSession();
            ResetWinRewardState();
            loseConditionStartTime = -1f;
            SetFastForward(false);
        }

        private void CancelPendingWinPanelDelay()
        {
            pendingWinPanelTween?.Kill(false);
            pendingWinPanelTween = null;
        }

        private void InvalidateFailedSession()
        {
            failedResultActive = false;
            failRecoveryPending = false;
            failSessionToken = string.Empty;
        }

        private bool IsCurrentFailSessionPurchaseLocked()
        {
            return failRecoveryPending &&
                   !string.IsNullOrEmpty(failSessionToken) &&
                   StoreManager.Instance != null &&
                   StoreManager.Instance.IsFailOfferSessionLocked(failSessionToken);
        }

        private void SetFastForward(bool isEnabled)
        {
            if (fastForwardActive == isEnabled)
            {
                return;
            }

            fastForwardActive = isEnabled;
            float multiplier = isEnabled ? noInteractableSpeedMultiplier : 1f;
            gameplaySpeedController?.SetMultiplier(multiplier);
        }

        private void CacheCurrentLevel()
        {
            LevelDefinition currentLevel = GetCurrentOrCachedLevel();
            if (currentLevel != null)
            {
                cachedLevel = currentLevel;
            }
        }

        private LevelDefinition GetCurrentOrCachedLevel()
        {
            if (levelBuildController != null && levelBuildController.CurrentLevel != null)
            {
                return levelBuildController.CurrentLevel;
            }

            if (levelSessionController != null && levelSessionController.CurrentLevelDefinition != null)
            {
                return levelSessionController.CurrentLevelDefinition;
            }

            return cachedLevel;
        }

        private void PrepareWinReward(LevelDefinition completedLevel)
        {
            winRewardClaimed = false;
            preparedWinGoldReward = 0;
            preparedWinDisplayedLevel = levelSessionController != null
                ? Mathf.Max(1, levelSessionController.DisplayedLevelNumber)
                : 1;

            if (completedLevel == null)
            {
                Debug.LogError($"{nameof(LevelResultFlowController)} on '{name}' cannot prepare the win reward because the completed {nameof(LevelDefinition)} is unavailable.", this);
                return;
            }

            preparedWinGoldReward = LevelWinRewardConfig.GetGoldReward(completedLevel.Difficulty);
        }

        private bool TryClaimWinReward(int multiplier)
        {
            if (!CanClaimWinReward || multiplier < 1)
            {
                return false;
            }

            SaveManager currentSaveManager = ResolveSaveManager();
            if (currentSaveManager == null)
            {
                Debug.LogError($"{nameof(LevelResultFlowController)} on '{name}' cannot grant the win reward because no {nameof(SaveManager)} is available.", this);
                return false;
            }

            int reward = (int)Math.Min(int.MaxValue, (long)preparedWinGoldReward * multiplier);
            winRewardClaimed = true;
            currentSaveManager.AddGold(reward);
            currentSaveManager.Save();
            return true;
        }

        private void ResetWinRewardState()
        {
            preparedWinGoldReward = 0;
            preparedWinDisplayedLevel = 0;
            winRewardClaimed = false;
        }

        private SaveManager ResolveSaveManager()
        {
            if (saveManager == null)
            {
                saveManager = SaveManager.Instance != null
                    ? SaveManager.Instance
                    : FindFirstObjectByType<SaveManager>();
            }

            if (saveManager != null && !saveManager.IsInitialized)
            {
                saveManager.Initialize();
            }

            return saveManager;
        }
    }
}

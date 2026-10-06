using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Audio;
using Game.Shared.Save;
using Game.Shared.Store;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using Gameplay.Analytics;
using Gameplay.Conveyor;
using Gameplay.SourceBoxes;
using Gameplay.Tutorial;
using Gameplay.UI.HUD;
using Gameplay.UI.Shop;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif
using UnityEngine.Localization;

namespace Gameplay.Levels
{
    [DisallowMultipleComponent]
    public sealed class FailRecoveryController : MonoBehaviour, IFailOfferContinueHandler
    {
        private const int DefaultMaximumCleanupMarbles = 30;
        private const int RecoveredSourceBoxCapacity = LevelCellData.MaxMarbleCount;
        private const int MaximumRecoveredSourceBoxesPerRow = 7;

        [Header("Lifecycle")]
        [SerializeField] private LevelResultFlowController resultFlowController;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private LevelBuildController levelBuildController;

        [Header("Gameplay References")]
        [SerializeField] private ConveyorController conveyorController;
        [SerializeField] private ConveyorEntryZone conveyorEntryZone;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private GameplayInputController gameplayInputController;
        [SerializeField] private GameplayHudController gameplayHudController;

        [Header("Clean Up Offer")]
        [SerializeField] private GameObject cleanUpOfferRoot;
        [SerializeField] private TweenButton cleanUpButton;
        [SerializeField] private TMP_Text cleanUpRequiredGoldText;
        [SerializeField] private TweenButton quitButton;
        [SerializeField, Min(0f)] private float cleanUpOfferShowDelay = 1f;

        [Header("First Clean Up Tutorial")]
        [SerializeField] private LevelOneTapTutorialController tutorialInfoController;
        [SerializeField] private LocalizedString firstCleanupMessage =
            new LocalizedString("General", "tutorial.first_cleanup");
        [SerializeField] private LocalizedString freeCleanupLabel =
            new LocalizedString("General", "cleanup.free");
        [SerializeField, Min(1f)] private float pulseMaxScale = 1.1f;
        [SerializeField, Min(0.01f), Tooltip("Duration of each up or down scale step, in seconds.")]
        private float pulseStepDuration = 0.2f;
        [SerializeField, Min(0f)] private float pulseLoopWait = 0.6f;
        [SerializeField, Tooltip("Offset from the button's original anchored Y, in canvas pixels.")]
        private float tutorialButtonYOffset = -100f;

        [Header("Fail Offer Panel")]
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private UIPanel failOfferPanel;
        [SerializeField] private FailOfferPanelController failOfferPanelController;
        [SerializeField] private TweenButton failOfferCloseButton;
        [SerializeField] private TweenButton playOnButton;
        [SerializeField] private TMP_Text playOnRequiredGoldText;
        [SerializeField] private TweenButton premiumBuyButton;
        [SerializeField] private TMP_Text premiumPriceText;
        [SerializeField] private TMP_Text offerGoldRewardText;
        [SerializeField] private TMP_Text offerBooster01RewardText;
        [SerializeField] private TMP_Text offerBooster02RewardText;
        [SerializeField] private TMP_Text offerBooster03RewardText;
        [SerializeField] private TMP_Text failOfferGoldText;
        [SerializeField] private GameShopPanelController gameShopPanelController;

        [Header("Localization")]
        [SerializeField] private LocalizedString offerBooster01QuantityFormat;
        [SerializeField] private LocalizedString offerBooster02QuantityFormat;
        [SerializeField] private LocalizedString offerBooster03QuantityFormat;

        [Header("Cleanup Economy")]
        [SerializeField, Min(0)] private int cleanupGoldCost = 900;
        [SerializeField, Range(1, DefaultMaximumCleanupMarbles)]
        private int maximumCleanupMarbles = DefaultMaximumCleanupMarbles;

        [Header("Cleanup Animation")]
        [SerializeField, Min(0f)] private float marbleStagger = 0.05f;
        [SerializeField, Min(0f)] private float liftDistance = 0.35f;
        [SerializeField, Min(0f)] private float liftDuration = 0.15f;
        [SerializeField, Min(0f)] private float gatherDuration = 0.35f;
        [SerializeField] private Ease cleanupEase = Ease.InOutQuad;
        [SerializeField, InspectorName("Recovered SourceBox Scale"), Min(0.01f)]
        private float recoveredSourceBoxScale = 0.667f;
        [SerializeField, Min(0f)] private float boxSpawnDuration = 0.25f;
        [SerializeField, Min(0f)] private float boxSpacing = 0.8f;
        [SerializeField] private Vector2 recoveredBoxLayoutOffset = new Vector2(0f, -0.8f);
        [SerializeField] private float recoveredBoxRowOffset = 0.8f;

        private readonly List<Marble> eligibleMarbles = new List<Marble>(DefaultMaximumCleanupMarbles * 2);
        private readonly List<CleanupCandidate> candidates = new List<CleanupCandidate>(DefaultMaximumCleanupMarbles * 2);
        private readonly List<CleanupCandidate> selectedCandidates = new List<CleanupCandidate>(DefaultMaximumCleanupMarbles);
        private readonly List<RecoveryGroup> activeGroups = new List<RecoveryGroup>();
        private readonly List<SourceBox> recoveredSourceBoxes = new List<SourceBox>();
        private readonly List<Marble> cleanupOwnedMarbles = new List<Marble>(DefaultMaximumCleanupMarbles);
        private readonly List<Marble> previewOutlinedMarbles = new List<Marble>(DefaultMaximumCleanupMarbles);
        private readonly Dictionary<string, object> booster01QuantityValues =
            new Dictionary<string, object>();
        private readonly Dictionary<string, object> booster02QuantityValues =
            new Dictionary<string, object>();
        private readonly Dictionary<string, object> booster03QuantityValues =
            new Dictionary<string, object>();
        private object[] booster01QuantityArguments;
        private object[] booster02QuantityArguments;
        private object[] booster03QuantityArguments;

        private SaveManager saveManager;
        private StoreManager storeManager;
        private Sequence cleanupSequence;
        private Tween cleanUpOfferDelayTween;
        private Sequence firstCleanupPulse;
        private bool firstCleanupTutorialActive;
        private bool freeCleanupLabelSubscribed;
        private RectTransform tutorialButtonRect;
        private Vector2 tutorialButtonBasePosition;
        private Vector3 tutorialButtonBaseScale;
        private bool tutorialQuitWasActive;
        private string activeRecoveryToken = string.Empty;
        private int lifecycleVersion;
        private bool recoveryUiActive;
        private bool cleanupInProgress;
        private bool actionInProgress;
        private bool goldSubscribed;
        private bool booster01QuantitySubscribed;
        private bool booster02QuantitySubscribed;
        private bool booster03QuantitySubscribed;
        private bool failOfferShownTracked;
        private AnalyticsLevelContext failOfferAnalyticsContext;
        private AnalyticsLevelContext cleanupUsedAnalyticsContext;

        private struct AnalyticsLevelContext
        {
            public int DisplayedLevelNumber;
            public int InternalLevelNumber;
            public bool IsValid;
        }

        private sealed class RecoveryGroup
        {
            public MarbleColorId ColorId;
            public MarbleColorCatalog.Entry ColorEntry;
            public int ColorBoxIndex;
            public readonly List<CleanupCandidate> Candidates = new List<CleanupCandidate>();
            public SourceBox SourceBox;
            public Transform PresentationRoot;
            public Vector3 PresentationFinalScale;
        }

        private sealed class CleanupCandidate
        {
            public Marble Marble;
            public bool IsEntryZoneMarble;
            public float SelectionWorldY;
            public int StableTieBreaker;
            public RecoveryGroup Group;
            public int GroupIndex;
            public Vector3 TargetWorldPosition;
        }

        private void Awake()
        {
            HideRecoveryUiImmediately();
            failOfferPanel?.SetAllowBackgroundDismiss(false);
            RefreshConfiguredTexts();
        }

        private void OnEnable()
        {
            BindLocalization();
            RegisterButtons();
            SubscribeLifecycle();
            BindStoreManager();
            StoreGameplayActionRegistry.Register(this);
        }

        private void OnDisable()
        {
            StoreGameplayActionRegistry.Unregister(this);
            UnsubscribeLifecycle();
            UnbindStoreManager();
            UnbindGold();
            CancelRuntimeRecovery(true);
            HideRecoveryUiImmediately();
            SetRecoveryInteractionBlocked(false);
            UnregisterButtons();
            UnbindLocalization();
        }

        private void OnDestroy()
        {
            UnbindLocalization();
        }

        private void OnValidate()
        {
            cleanupGoldCost = Mathf.Max(0, cleanupGoldCost);
            pulseMaxScale = Mathf.Max(1f, pulseMaxScale);
            pulseStepDuration = Mathf.Max(0.01f, pulseStepDuration);
            pulseLoopWait = Mathf.Max(0f, pulseLoopWait);
            cleanUpOfferShowDelay = Mathf.Max(0f, cleanUpOfferShowDelay);
            maximumCleanupMarbles = Mathf.Clamp(
                maximumCleanupMarbles,
                1,
                DefaultMaximumCleanupMarbles);
            marbleStagger = Mathf.Max(0f, marbleStagger);
            liftDistance = Mathf.Max(0f, liftDistance);
            liftDuration = Mathf.Max(0f, liftDuration);
            gatherDuration = Mathf.Max(0f, gatherDuration);
            recoveredSourceBoxScale = Mathf.Max(0.01f, recoveredSourceBoxScale);
            boxSpawnDuration = Mathf.Max(0f, boxSpawnDuration);
            boxSpacing = Mathf.Max(0f, boxSpacing);
        }

        public bool TryCaptureFailedSession(out string sessionToken)
        {
            sessionToken = string.Empty;
            return resultFlowController != null &&
                   resultFlowController.TryCaptureRecoverySession(out sessionToken);
        }

        public bool CanContinueFailedSession(string sessionToken)
        {
            return resultFlowController != null &&
                   resultFlowController.CanRecoverSession(sessionToken) &&
                   !cleanupInProgress &&
                   CanPrepareCleanup();
        }

        public bool TryContinueFailedSession(string sessionToken)
        {
            return CanContinueFailedSession(sessionToken) &&
                   TryStartCleanup(false, sessionToken, false);
        }

        private void SubscribeLifecycle()
        {
            if (resultFlowController != null)
            {
                resultFlowController.FailRecoveryRequested -= HandleFailRecoveryRequested;
                resultFlowController.FailRecoveryRequested += HandleFailRecoveryRequested;
                resultFlowController.WinLogicTriggered -= HandleTerminalLevelFlow;
                resultFlowController.WinLogicTriggered += HandleTerminalLevelFlow;
            }

            if (levelBuildController != null)
            {
                levelBuildController.LevelClearing -= HandleLevelClearing;
                levelBuildController.LevelClearing += HandleLevelClearing;
            }
        }

        private void UnsubscribeLifecycle()
        {
            if (resultFlowController != null)
            {
                resultFlowController.FailRecoveryRequested -= HandleFailRecoveryRequested;
                resultFlowController.WinLogicTriggered -= HandleTerminalLevelFlow;
            }

            if (levelBuildController != null)
            {
                levelBuildController.LevelClearing -= HandleLevelClearing;
            }
        }

        private void RegisterButtons()
        {
            RegisterButton(cleanUpButton, HandleCleanUpClicked);
            RegisterButton(quitButton, HandleQuitClicked);
            RegisterButton(failOfferCloseButton, HandleFailOfferClosed);
            RegisterButton(playOnButton, HandlePlayOnClicked);
            RegisterButton(premiumBuyButton, HandlePremiumBuyClicked);
        }

        private void UnregisterButtons()
        {
            UnregisterButton(cleanUpButton, HandleCleanUpClicked);
            UnregisterButton(quitButton, HandleQuitClicked);
            UnregisterButton(failOfferCloseButton, HandleFailOfferClosed);
            UnregisterButton(playOnButton, HandlePlayOnClicked);
            UnregisterButton(premiumBuyButton, HandlePremiumBuyClicked);
        }

        private static void RegisterButton(TweenButton button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void UnregisterButton(TweenButton button, UnityEngine.Events.UnityAction action)
        {
            button?.onClick.RemoveListener(action);
        }

        private void HandleFailRecoveryRequested()
        {
            if (recoveryUiActive || cleanupInProgress || resultFlowController == null ||
                !resultFlowController.TryCaptureRecoverySession(out activeRecoveryToken))
            {
                return;
            }

            lifecycleVersion++;
            recoveryUiActive = true;
            actionInProgress = false;
            failOfferShownTracked = false;
            failOfferAnalyticsContext = default;
            cleanupUsedAnalyticsContext = default;
            SetRecoveryInteractionBlocked(true);
            panelManager?.CloseAll();
            failOfferPanel?.HideImmediately();
            cleanUpOfferRoot?.SetActive(false);
            UnbindGold();
            RefreshConfiguredTexts();
            RefreshButtonStates();

            BindStoreManager();
            if (storeManager != null && storeManager.PendingFailOfferRecoveryContinues > 0)
            {
                storeManager.TryConsumeFailOfferRecoveryContinue(activeRecoveryToken);
            }

            if (!cleanupInProgress && IsCurrentRecoveryValid())
            {
                ScheduleCleanUpOffer(lifecycleVersion, activeRecoveryToken);
            }
        }

        private void ScheduleCleanUpOffer(int version, string sessionToken)
        {
            CancelPendingCleanUpOffer();
            if (cleanUpOfferRoot == null)
            {
                return;
            }

            if (cleanUpOfferShowDelay <= 0f)
            {
                ShowCleanUpOffer();
                return;
            }

            cleanUpOfferDelayTween = DOVirtual
                .DelayedCall(cleanUpOfferShowDelay, () =>
                {
                    cleanUpOfferDelayTween = null;
                    if (version == lifecycleVersion &&
                        sessionToken == activeRecoveryToken &&
                        IsCurrentRecoveryValid() &&
                        !cleanupInProgress)
                    {
                        ShowCleanUpOffer();
                    }
                }, true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void ShowCleanUpOffer()
        {
            if (cleanUpOfferRoot == null)
            {
                return;
            }

            cleanUpOfferRoot.SetActive(true);
            BeginFirstCleanupTutorial();
            RefreshCleanupPreviewOutlines();
            failOfferPanelController?.PreloadPreview();
            AudioManager.Instance?.PlaySfx(AudioKey.Fail);
        }

        private void CancelPendingCleanUpOffer()
        {
            cleanUpOfferDelayTween?.Kill(false);
            cleanUpOfferDelayTween = null;
        }

        private void HandleCleanUpClicked()
        {
            if (firstCleanupTutorialActive)
            {
                if (actionInProgress || !IsCurrentRecoveryValid()) return;
                BindSaveManager();
                // Never charge for a displayed FREE offer, including save-unavailable states.
                if (saveManager == null || saveManager.IsWriteBlocked) return;
                if (TryStartCleanup(false, activeRecoveryToken, true))
                {
                    // Commit only after real marbles were detached and cleanup successfully started.
                    saveManager.SetSetting(SaveKeys.FirstCleanupTutorialCompleted, true);
                    if (!saveManager.TrySave())
                    {
                        Debug.LogWarning("First Clean Up tutorial completion is pending a successful save.", this);
                    }
                }
                else
                {
                    RefreshButtonStates();
                }
                return;
            }

            TryStartPaidCleanup(true);
        }

        private void BeginFirstCleanupTutorial()
        {
            EndFirstCleanupTutorial();
            BindSaveManager();
            if (saveManager == null ||
                saveManager.GetBoolSetting(SaveKeys.FirstCleanupTutorialCompleted)) return;

            firstCleanupTutorialActive = true;
            tutorialQuitWasActive = quitButton != null && quitButton.gameObject.activeSelf;
            quitButton?.gameObject.SetActive(false);
            tutorialInfoController?.ShowTutorialInfo(this, firstCleanupMessage);
            if (freeCleanupLabel != null && !freeCleanupLabel.IsEmpty)
            {
                cleanUpRequiredGoldText?.SetText(string.Empty);
                freeCleanupLabelSubscribed = true;
                freeCleanupLabel.StringChanged += HandleFreeCleanupLabelChanged;
                freeCleanupLabel.RefreshString();
            }

            tutorialButtonRect = cleanUpButton != null ? cleanUpButton.transform as RectTransform : null;
            if (tutorialButtonRect == null) return;
            tutorialButtonBasePosition = tutorialButtonRect.anchoredPosition;
            tutorialButtonBaseScale = tutorialButtonRect.localScale;
            tutorialButtonRect.anchoredPosition = tutorialButtonBasePosition + Vector2.up * tutorialButtonYOffset;
            float duration = Mathf.Max(0.01f, pulseStepDuration);
            Vector3 peak = tutorialButtonBaseScale * Mathf.Max(1f, pulseMaxScale);
            firstCleanupPulse = DOTween.Sequence()
                .Append(tutorialButtonRect.DOScale(peak, duration).SetEase(Ease.InOutSine))
                .Append(tutorialButtonRect.DOScale(tutorialButtonBaseScale, duration).SetEase(Ease.InOutSine))
                .Append(tutorialButtonRect.DOScale(peak, duration).SetEase(Ease.InOutSine))
                .Append(tutorialButtonRect.DOScale(tutorialButtonBaseScale, duration).SetEase(Ease.InOutSine))
                .AppendInterval(Mathf.Max(0f, pulseLoopWait))
                .SetLoops(-1)
                .SetUpdate(true)
                .SetLink(cleanUpOfferRoot, LinkBehaviour.KillOnDisable);
        }

        private void HandleFreeCleanupLabelChanged(string value)
        {
            if (firstCleanupTutorialActive) cleanUpRequiredGoldText?.SetText(value);
        }

        private void EndFirstCleanupTutorial()
        {
            firstCleanupPulse?.Kill(false);
            firstCleanupPulse = null;
            tutorialInfoController?.HideTutorialInfo(this);
            if (freeCleanupLabelSubscribed)
            {
                freeCleanupLabel.StringChanged -= HandleFreeCleanupLabelChanged;
                freeCleanupLabelSubscribed = false;
            }
            if (!firstCleanupTutorialActive) return;
            firstCleanupTutorialActive = false;
            if (tutorialButtonRect != null)
            {
                tutorialButtonRect.anchoredPosition = tutorialButtonBasePosition;
                tutorialButtonRect.localScale = tutorialButtonBaseScale;
            }
            tutorialButtonRect = null;
            quitButton?.gameObject.SetActive(tutorialQuitWasActive);
            cleanUpRequiredGoldText?.SetText("{0}", cleanupGoldCost);
        }

        private void HandlePlayOnClicked()
        {
            TryStartPaidCleanup(false);
        }

        private void TryStartPaidCleanup(bool trackCleanupUsed)
        {
            if (actionInProgress || !IsCurrentRecoveryValid())
            {
                return;
            }

            BindSaveManager();
            if (saveManager == null || saveManager.Gold < cleanupGoldCost)
            {
                gameShopPanelController?.OpenForInsufficientGold();
                RefreshButtonStates();
                return;
            }

            if (!TryStartCleanup(true, activeRecoveryToken, trackCleanupUsed))
            {
                RefreshButtonStates();
            }
        }

        private void HandleQuitClicked()
        {
            if (firstCleanupTutorialActive || actionInProgress || !IsCurrentRecoveryValid())
            {
                return;
            }

            ClearCleanupPreviewOutlines();
            cleanUpOfferRoot?.SetActive(false);
            OpenFailOfferPanel();
        }

        private void OpenFailOfferPanel()
        {
            string requestedSession = activeRecoveryToken;
            if (panelManager != null && panelManager.TryDeferOpen(failOfferPanel, () =>
                {
                    if (requestedSession == activeRecoveryToken && IsCurrentRecoveryValid()) OpenFailOfferPanel();
                })) return;

            if (panelManager == null || failOfferPanel == null)
            {
                Debug.LogError(
                    $"{nameof(FailRecoveryController)} on '{name}' cannot open the Fail Offer Panel because its panel references are incomplete.",
                    this);
                return;
            }

            failOfferPanel.SetAllowBackgroundDismiss(false);
            panelManager.OpenRoot(failOfferPanel);
            if (!failOfferShownTracked)
            {
                failOfferShownTracked = true;
                if (!failOfferAnalyticsContext.IsValid)
                {
                    TryCaptureAnalyticsLevelContext(out failOfferAnalyticsContext);
                }

                if (failOfferAnalyticsContext.IsValid)
                {
                    Track(AnalyticsEventFactory.CreateFailOfferShown(
                        failOfferAnalyticsContext.DisplayedLevelNumber,
                        failOfferAnalyticsContext.InternalLevelNumber));
                }
            }

            gameplayHudController?.BringGoldDisplayToFront();
            failOfferPanelController?.PlayPreparedPreview();
            BindGold();
            RefreshConfiguredTexts();
            RefreshStorePresentation();
            RefreshButtonStates();
        }

        private void HandleFailOfferClosed()
        {
            if (actionInProgress || !IsCurrentRecoveryValid() ||
                resultFlowController.IsFailOfferPurchaseLocked)
            {
                return;
            }

            actionInProgress = true;
            RefreshButtonStates();
            Action finalize = () =>
            {
                UnbindGold();
                CancelRuntimeRecovery(true);
                recoveryUiActive = false;
                actionInProgress = false;
                activeRecoveryToken = string.Empty;
                SetRecoveryInteractionBlocked(false);
                if (!resultFlowController.FinalizePendingFail())
                {
                    Debug.LogError(
                        $"{nameof(FailRecoveryController)} on '{name}' could not finalize the pending fail lifecycle.",
                        this);
                }
            };

            if (panelManager == null || failOfferPanel == null ||
                !panelManager.TryClose(failOfferPanel, finalize))
            {
                failOfferPanel?.HideImmediately();
                finalize();
            }
        }

        private void HandlePremiumBuyClicked()
        {
            if (actionInProgress || !IsCurrentRecoveryValid())
            {
                return;
            }

            BindStoreManager();
            if (storeManager == null || !storeManager.CanPurchase(StoreProductIds.FailOffer))
            {
                RefreshButtonStates();
                return;
            }

            if (!failOfferAnalyticsContext.IsValid)
            {
                TryCaptureAnalyticsLevelContext(out failOfferAnalyticsContext);
            }

            if (failOfferAnalyticsContext.IsValid)
            {
                Track(AnalyticsEventFactory.CreateFailOfferClicked(
                    failOfferAnalyticsContext.DisplayedLevelNumber,
                    failOfferAnalyticsContext.InternalLevelNumber));
            }

            actionInProgress = true;
            RefreshButtonStates();
            if (!storeManager.Purchase(StoreProductIds.FailOffer))
            {
                actionInProgress = false;
                RefreshButtonStates();
            }
        }

        private bool TryStartCleanup(
            bool payWithGold,
            string sessionToken,
            bool trackCleanupUsed)
        {
            if (cleanupInProgress || resultFlowController == null ||
                !resultFlowController.CanRecoverSession(sessionToken))
            {
                return false;
            }

            ClearCleanupPreviewOutlines();
            if (!TryPrepareCandidatesAndGroups())
            {
                return false;
            }

            BindSaveManager();
            if (payWithGold && (saveManager == null || saveManager.Gold < cleanupGoldCost))
            {
                return false;
            }

            if (!TryCreateRecoveredSourceBoxes())
            {
                DestroyPreparedGroups();
                return false;
            }

            if (payWithGold && !saveManager.SpendGold(cleanupGoldCost))
            {
                DestroyPreparedGroups();
                return false;
            }

            cleanupOwnedMarbles.Clear();
            for (int i = 0; i < selectedCandidates.Count; i++)
            {
                CleanupCandidate candidate = selectedCandidates[i];
                bool detached = candidate.IsEntryZoneMarble
                    ? conveyorEntryZone != null &&
                      conveyorEntryZone.TryDetachMarbleForRecovery(candidate.Marble)
                    : conveyorController != null &&
                      conveyorController.RemoveMarble(candidate.Marble);
                if (!detached)
                {
                    Debug.LogWarning(
                        $"{nameof(FailRecoveryController)} on '{name}' skipped marble '{candidate.Marble?.name}' because its ownership changed before cleanup could detach it.",
                        this);
                    continue;
                }

                candidate.Marble.PrepareForRecoveryControl();
                candidate.Marble.SetOutlineColor(Color.white);
                candidate.Marble.SetOutlineActive(true);
                candidate.Marble.transform.SetParent(sourceBoxBoardController.transform, true);
                cleanupOwnedMarbles.Add(candidate.Marble);
#if UNITY_EDITOR
                MarbleDebugTracker.SetLocation(candidate.Marble, MarbleDebugLocation.Recovery, this,
                    $"Cleanup #{GetInstanceID()} -> Recovered source #{candidate.Group.SourceBox?.GetInstanceID()}",
                    "Cleanup ownership committed");
#endif
            }

            if (cleanupOwnedMarbles.Count == 0)
            {
                if (payWithGold)
                {
                    saveManager.AddGold(cleanupGoldCost);
                }

                DestroyPreparedGroups();
                return false;
            }

            RemoveEmptyPreparedGroups();
            RefreshPreparedGroupTargets();
            activeRecoveryToken = sessionToken;
            cleanupInProgress = true;
            actionInProgress = true;
            cleanupUsedAnalyticsContext = default;
            if (trackCleanupUsed)
            {
                TryCaptureAnalyticsLevelContext(out cleanupUsedAnalyticsContext);
            }

            conveyorEntryZone.SetCleanupEntryAcceptanceBlocked(true);
            failOfferPanelController?.StopPreviewPlayback();
            CancelPendingCleanUpOffer();
            EndFirstCleanupTutorial();
            cleanUpOfferRoot?.SetActive(false);
            CloseFailOfferForCleanup();
            PlayCleanupAnimation(++lifecycleVersion, sessionToken);
            RefreshButtonStates();
            return true;
        }

        private bool TryPrepareCandidatesAndGroups()
        {
            selectedCandidates.Clear();
            activeGroups.Clear();

            if (!TryCollectCleanupCandidates())
            {
                return false;
            }

            int selectedCount = Mathf.Min(maximumCleanupMarbles, candidates.Count);
            Dictionary<MarbleColorId, RecoveryGroup> currentGroupByColor =
                new Dictionary<MarbleColorId, RecoveryGroup>();
            Dictionary<MarbleColorId, int> boxCountByColor =
                new Dictionary<MarbleColorId, int>();

            for (int i = 0; i < selectedCount; i++)
            {
                CleanupCandidate candidate = candidates[i];
                if (!currentGroupByColor.TryGetValue(candidate.Marble.ColorId, out RecoveryGroup group) ||
                    group.Candidates.Count >= RecoveredSourceBoxCapacity)
                {
                    if (!sourceBoxBoardController.ColorCatalog.TryGetEntry(
                            candidate.Marble.ColorId,
                            out MarbleColorCatalog.Entry colorEntry))
                    {
                        return false;
                    }

                    boxCountByColor.TryGetValue(candidate.Marble.ColorId, out int colorBoxCount);
                    group = new RecoveryGroup
                    {
                        ColorId = candidate.Marble.ColorId,
                        ColorEntry = colorEntry,
                        ColorBoxIndex = colorBoxCount
                    };
                    boxCountByColor[group.ColorId] = colorBoxCount + 1;
                    currentGroupByColor[group.ColorId] = group;
                    activeGroups.Add(group);
                }

                candidate.Group = group;
                candidate.GroupIndex = group.Candidates.Count;
                group.Candidates.Add(candidate);
                selectedCandidates.Add(candidate);
            }

            return selectedCandidates.Count > 0;
        }

        private bool TryCollectCleanupCandidates()
        {
            candidates.Clear();
            eligibleMarbles.Clear();

            if (!CanPrepareCleanup())
            {
                return false;
            }

            conveyorController.GetOccupiedMarbles(eligibleMarbles);
            int conveyorMarbleCount = eligibleMarbles.Count;
            AppendReleasedDropZoneMarbles(eligibleMarbles);
            conveyorEntryZone?.AppendMarblesForRecovery(eligibleMarbles);

            for (int i = 0; i < eligibleMarbles.Count; i++)
            {
                Marble marble = eligibleMarbles[i];
                if (marble == null || !marble.gameObject.activeInHierarchy ||
                    !MarbleColorCatalog.IsGameplayColor(marble.ColorId))
                {
                    continue;
                }

                candidates.Add(new CleanupCandidate
                {
                    Marble = marble,
                    IsEntryZoneMarble = i >= conveyorMarbleCount,
                    SelectionWorldY = marble.transform.position.y,
                    StableTieBreaker = marble.GetInstanceID()
                });
            }

            candidates.Sort(CompareCleanupCandidates);
            return candidates.Count > 0;
        }

#if UNITY_EDITOR
        public void DebugCollectMarbles(List<MarbleDebugObservation> result)
        {
            foreach (var marble in cleanupOwnedMarbles)
                MarbleDebugTracker.Observe(result, marble, MarbleDebugLocation.Recovery, this,
                    $"Cleanup #{GetInstanceID()}");
        }
#endif

        private void AppendReleasedDropZoneMarbles(List<Marble> results)
        {
            Transform releasedContainer = sourceBoxBoardController?.ReleasedMarbleContainer;
            if (releasedContainer == null || results == null)
            {
                return;
            }

            Marble[] releasedMarbles = releasedContainer.GetComponentsInChildren<Marble>(false);
            for (int i = 0; i < releasedMarbles.Length; i++)
            {
                Marble marble = releasedMarbles[i];
                if (marble == null || !marble.gameObject.activeInHierarchy ||
                    !marble.CanBeOwnedByUfo ||
                    (conveyorController != null && conveyorController.IsMarbleOnConveyor(marble)) ||
                    results.Contains(marble))
                {
                    continue;
                }

                results.Add(marble);
            }
        }

        private bool CanPrepareCleanup()
        {
            return sourceBoxBoardController != null &&
                   levelSessionController != null &&
                   levelSessionController.GameplayState == GameplaySessionState.Recovering &&
                   sourceBoxBoardController.FeatureCatalog != null &&
                   sourceBoxBoardController.FeatureCatalog.SourceBoxPrefab != null &&
                   sourceBoxBoardController.ColorCatalog != null &&
                   sourceBoxBoardController.ReleasedMarbleContainer != null &&
                   sourceBoxBoardController.CapacityController != null &&
                   conveyorController != null &&
                   conveyorEntryZone != null;
        }

        private void RefreshCleanupPreviewOutlines()
        {
            ClearCleanupPreviewOutlines();
            if (!TryCollectCleanupCandidates())
            {
                return;
            }

            int previewCount = Mathf.Min(maximumCleanupMarbles, candidates.Count);
            for (int i = 0; i < previewCount; i++)
            {
                Marble marble = candidates[i].Marble;
                if (marble == null)
                {
                    continue;
                }

                marble.SetOutlineColor(Color.white);
                marble.SetOutlineActive(true);
                previewOutlinedMarbles.Add(marble);
            }
        }

        private void ClearCleanupPreviewOutlines()
        {
            for (int i = 0; i < previewOutlinedMarbles.Count; i++)
            {
                Marble marble = previewOutlinedMarbles[i];
                if (marble != null)
                {
                    marble.SetOutlineColor(Color.black);
                }
            }

            previewOutlinedMarbles.Clear();
        }

        private static int CompareCleanupCandidates(CleanupCandidate left, CleanupCandidate right)
        {
            float leftY = left?.Marble != null ? left.SelectionWorldY : float.NegativeInfinity;
            float rightY = right?.Marble != null ? right.SelectionWorldY : float.NegativeInfinity;
            int yComparison = rightY.CompareTo(leftY);
            return yComparison != 0
                ? yComparison
                : (left?.StableTieBreaker ?? int.MaxValue).CompareTo(
                    right?.StableTieBreaker ?? int.MaxValue);
        }

        private bool TryCreateRecoveredSourceBoxes()
        {
            SourceBox prefab = sourceBoxBoardController.FeatureCatalog.SourceBoxPrefab;
            for (int i = 0; i < activeGroups.Count; i++)
            {
                RecoveryGroup group = activeGroups[i];
                SourceBox sourceBox = Instantiate(prefab, sourceBoxBoardController.transform);
                sourceBox.name = $"RecoveredSourceBox_{group.ColorId}_{group.ColorBoxIndex + 1}";
                sourceBox.transform.localRotation = Quaternion.identity;
                sourceBox.transform.localScale = Vector3.one * recoveredSourceBoxScale;
                if (!sourceBox.InitializeRecovered(
                        group.ColorId,
                        group.ColorEntry,
                        sourceBoxBoardController.ReleasedMarbleContainer,
                        sourceBoxBoardController.CapacityController,
                        HandleRecoveredSourceBoxReleased))
                {
                    Destroy(sourceBox.gameObject);
                    return false;
                }

                group.SourceBox = sourceBox;
                group.PresentationRoot = sourceBox.PresentationRoot;
                group.PresentationFinalScale = group.PresentationRoot != null
                    ? group.PresentationRoot.localScale
                    : Vector3.one;
                recoveredSourceBoxes.Add(sourceBox);
                sourceBoxBoardController.RegisterRecoveredSourceBox(sourceBox);
            }

            LayoutRecoveredSourceBoxes();
            for (int i = 0; i < activeGroups.Count; i++)
            {
                RecoveryGroup group = activeGroups[i];
                for (int j = 0; j < group.Candidates.Count; j++)
                {
                    group.Candidates[j].TargetWorldPosition = group.SourceBox.transform.position;
                }

                if (group.PresentationRoot != null)
                {
                    group.PresentationRoot.localScale = Vector3.zero;
                }
            }

            return true;
        }

        private void RefreshPreparedGroupTargets()
        {
            for (int i = 0; i < activeGroups.Count; i++)
            {
                RecoveryGroup group = activeGroups[i];
                if (group.SourceBox == null)
                {
                    continue;
                }

                for (int j = 0; j < group.Candidates.Count; j++)
                {
                    group.Candidates[j].TargetWorldPosition = group.SourceBox.transform.position;
                }
            }
        }

        private void LayoutRecoveredSourceBoxes(bool applyFinalScale = true)
        {
            for (int i = recoveredSourceBoxes.Count - 1; i >= 0; i--)
            {
                if (recoveredSourceBoxes[i] == null)
                {
                    recoveredSourceBoxes.RemoveAt(i);
                }
            }

            if (recoveredSourceBoxes.Count == 0)
            {
                return;
            }

            Vector3 layoutCenter = ResolveRecoveredLayoutCenter();
            for (int i = 0; i < recoveredSourceBoxes.Count; i++)
            {
                int rowIndex = i / MaximumRecoveredSourceBoxesPerRow;
                int rowStartIndex = rowIndex * MaximumRecoveredSourceBoxesPerRow;
                int boxesInRow = Mathf.Min(
                    MaximumRecoveredSourceBoxesPerRow,
                    recoveredSourceBoxes.Count - rowStartIndex);
                int indexInRow = i - rowStartIndex;
                float rowWidth = (boxesInRow - 1) * boxSpacing;

                SourceBox sourceBox = recoveredSourceBoxes[i];
                Vector3 position = layoutCenter;
                position.x += -rowWidth * 0.5f + indexInRow * boxSpacing;
                position.y += rowIndex * recoveredBoxRowOffset;
                sourceBox.transform.position = position;
                Vector3 localPosition = sourceBox.transform.localPosition;
                localPosition.z = 0f;
                sourceBox.transform.localPosition = localPosition;
                if (applyFinalScale)
                {
                    sourceBox.transform.localScale = Vector3.one * recoveredSourceBoxScale;
                }
            }
        }

        private Vector3 ResolveRecoveredLayoutCenter()
        {
            Transform gridRoot = sourceBoxBoardController.PlaceholderGridController != null
                ? sourceBoxBoardController.PlaceholderGridController.GeneratedGridRoot
                : null;
            if (gridRoot == null || gridRoot.childCount == 0)
            {
                Vector3 fallback = sourceBoxBoardController.transform.position;
                fallback += (Vector3)recoveredBoxLayoutOffset;
                return fallback;
            }

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            for (int i = 0; i < gridRoot.childCount; i++)
            {
                Vector3 position = gridRoot.GetChild(i).position;
                minX = Mathf.Min(minX, position.x);
                maxX = Mathf.Max(maxX, position.x);
                minY = Mathf.Min(minY, position.y);
            }

            return new Vector3(
                (minX + maxX) * 0.5f + recoveredBoxLayoutOffset.x,
                minY + recoveredBoxLayoutOffset.y,
                gridRoot.position.z);
        }

        private void PlayCleanupAnimation(int version, string sessionToken)
        {
            cleanupSequence?.Kill(false);
            cleanupSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            cleanupSequence.InsertCallback(
                0f,
                () => AudioManager.Instance?.PlaySfx(AudioKey.MarbleLift));

            float liftPhaseEnd = 0f;
            int liftOrder = 0;
            for (int i = 0; i < selectedCandidates.Count; i++)
            {
                CleanupCandidate candidate = selectedCandidates[i];
                if (candidate.Marble == null || !cleanupOwnedMarbles.Contains(candidate.Marble))
                {
                    continue;
                }

                Marble marble = candidate.Marble;
                Transform marbleTransform = marble.transform;
                float liftStartTime = liftOrder * marbleStagger;
                cleanupSequence.Insert(
                    liftStartTime,
                    marbleTransform
                        .DOMoveY(marbleTransform.position.y + liftDistance, liftDuration)
                        .SetEase(cleanupEase));
                liftPhaseEnd = Mathf.Max(liftPhaseEnd, liftStartTime + liftDuration);
                liftOrder++;
            }

            float gatherPhaseEnd = liftPhaseEnd + gatherDuration;
            for (int i = 0; i < selectedCandidates.Count; i++)
            {
                CleanupCandidate candidate = selectedCandidates[i];
                if (candidate.Marble == null || !cleanupOwnedMarbles.Contains(candidate.Marble))
                {
                    continue;
                }

                cleanupSequence.Insert(
                    liftPhaseEnd,
                    candidate.Marble.transform
                        .DOMove(candidate.TargetWorldPosition, gatherDuration)
                        .SetEase(cleanupEase));
            }

            cleanupSequence.InsertCallback(gatherPhaseEnd, RestoreCleanupOwnedOutlineColors);
            for (int i = 0; i < selectedCandidates.Count; i++)
            {
                CleanupCandidate candidate = selectedCandidates[i];
                if (candidate.Marble == null || !cleanupOwnedMarbles.Contains(candidate.Marble))
                {
                    continue;
                }

                cleanupSequence.Insert(
                    gatherPhaseEnd,
                    candidate.Marble.transform
                        .DOScale(Vector3.zero, boxSpawnDuration)
                        .SetEase(cleanupEase));
            }

            for (int i = 0; i < activeGroups.Count; i++)
            {
                RecoveryGroup group = activeGroups[i];
                if (group.PresentationRoot == null)
                {
                    continue;
                }

                cleanupSequence.Insert(
                    gatherPhaseEnd,
                    group.PresentationRoot
                        .DOScale(group.PresentationFinalScale, boxSpawnDuration)
                        .SetEase(cleanupEase));
            }

            float animationEnd = gatherPhaseEnd + boxSpawnDuration;
            cleanupSequence.InsertCallback(
                animationEnd + 0.001f,
                () => CompleteCleanup(version, sessionToken));
        }

        private void RestoreCleanupOwnedOutlineColors()
        {
            for (int i = 0; i < cleanupOwnedMarbles.Count; i++)
            {
                Marble marble = cleanupOwnedMarbles[i];
                if (marble != null)
                {
                    marble.SetOutlineColor(Color.black);
                }
            }
        }

        private void CompleteCleanup(int version, string sessionToken)
        {
            cleanupSequence = null;
            if (version != lifecycleVersion || !cleanupInProgress ||
                resultFlowController == null ||
                !resultFlowController.CanRecoverSession(sessionToken))
            {
                CancelRuntimeRecovery(true);
                return;
            }

            for (int i = 0; i < selectedCandidates.Count; i++)
            {
                CleanupCandidate candidate = selectedCandidates[i];
                Marble marble = candidate.Marble;
                if (marble == null || !cleanupOwnedMarbles.Contains(marble))
                {
                    continue;
                }

                if (candidate.Group?.SourceBox == null ||
                    !candidate.Group.SourceBox.AddRecoveredMarble(
                        marble,
                        candidate.GroupIndex))
                {
                    CancelRuntimeRecovery(true);
                    return;
                }

                cleanupOwnedMarbles.Remove(marble);
            }

            if (!resultFlowController.TryCompleteRecovery(sessionToken))
            {
                CancelRuntimeRecovery(true);
                return;
            }

            if (cleanupUsedAnalyticsContext.IsValid)
            {
                Track(AnalyticsEventFactory.CreateCleanupUsed(
                    cleanupUsedAnalyticsContext.DisplayedLevelNumber,
                    cleanupUsedAnalyticsContext.InternalLevelNumber));
                cleanupUsedAnalyticsContext = default;
            }

            AudioManager.Instance?.PlaySfx(AudioKey.CleanupSuccess);

            if (conveyorEntryZone != null)
            {
                conveyorEntryZone.SetCleanupEntryAcceptanceBlocked(false);
                conveyorEntryZone.ResumeAfterRecovery();
            }

            for (int i = 0; i < activeGroups.Count; i++)
            {
                activeGroups[i].SourceBox?.RefreshInputState();
            }

            activeGroups.Clear();
            selectedCandidates.Clear();
            cleanupOwnedMarbles.Clear();
            cleanupInProgress = false;
            actionInProgress = false;
            recoveryUiActive = false;
            activeRecoveryToken = string.Empty;
            failOfferShownTracked = false;
            failOfferAnalyticsContext = default;
            cleanupUsedAnalyticsContext = default;
            HideRecoveryUiImmediately();
            SetRecoveryInteractionBlocked(false);
        }

        private void CloseFailOfferForCleanup()
        {
            UnbindGold();
            gameplayHudController?.RestoreGoldDisplaySortingOrder();
            if (failOfferPanel == null)
            {
                return;
            }

            if (panelManager == null || !panelManager.ContainsPanel(failOfferPanel) ||
                !panelManager.TryClose(failOfferPanel))
            {
                failOfferPanel.HideImmediately();
            }
        }

        private void RemoveEmptyPreparedGroups()
        {
            for (int i = activeGroups.Count - 1; i >= 0; i--)
            {
                RecoveryGroup group = activeGroups[i];
                for (int j = group.Candidates.Count - 1; j >= 0; j--)
                {
                    if (group.Candidates[j].Marble == null ||
                        !cleanupOwnedMarbles.Contains(group.Candidates[j].Marble))
                    {
                        group.Candidates.RemoveAt(j);
                    }
                }

                if (group.Candidates.Count > 0)
                {
                    for (int j = 0; j < group.Candidates.Count; j++)
                    {
                        group.Candidates[j].GroupIndex = j;
                    }

                    continue;
                }

                recoveredSourceBoxes.Remove(group.SourceBox);
                if (group.SourceBox != null)
                {
                    sourceBoxBoardController?.UnregisterRecoveredSourceBox(group.SourceBox);
                    Destroy(group.SourceBox.gameObject);
                }

                activeGroups.RemoveAt(i);
            }

            LayoutRecoveredSourceBoxes(false);
        }

        private void DestroyPreparedGroups()
        {
            for (int i = 0; i < activeGroups.Count; i++)
            {
                SourceBox sourceBox = activeGroups[i].SourceBox;
                recoveredSourceBoxes.Remove(sourceBox);
                if (sourceBox != null)
                {
                    sourceBoxBoardController?.UnregisterRecoveredSourceBox(sourceBox);
                    Destroy(sourceBox.gameObject);
                }
            }

            activeGroups.Clear();
            selectedCandidates.Clear();
            LayoutRecoveredSourceBoxes();
        }

        private void HandleRecoveredSourceBoxReleased(SourceBox sourceBox)
        {
            if (sourceBox == null)
            {
                return;
            }

            recoveredSourceBoxes.Remove(sourceBox);
            sourceBoxBoardController?.UnregisterRecoveredSourceBox(sourceBox);
            Destroy(sourceBox.gameObject);
            LayoutRecoveredSourceBoxes();
        }

        private void HandleTerminalLevelFlow()
        {
            CancelRuntimeRecovery(true);
            HideRecoveryUiImmediately();
            SetRecoveryInteractionBlocked(false);
        }

        private void HandleLevelClearing()
        {
            lifecycleVersion++;
            if (resultFlowController != null && resultFlowController.IsFailRecoveryPending)
            {
                resultFlowController.ResetForDebugLevelLoad();
            }

            CancelRuntimeRecovery(true);
            HideRecoveryUiImmediately();
            SetRecoveryInteractionBlocked(false);
        }

        private void CancelRuntimeRecovery(bool destroyRecoveredBoxes)
        {
            EndFirstCleanupTutorial();
            lifecycleVersion++;
            CancelPendingCleanUpOffer();
            gameplayHudController?.RestoreGoldDisplaySortingOrder();
            failOfferPanelController?.StopPreviewPlayback();
            ClearCleanupPreviewOutlines();
            if (conveyorEntryZone != null)
            {
                conveyorEntryZone.SetCleanupEntryAcceptanceBlocked(false);
            }

            cleanupSequence?.Kill(false);
            cleanupSequence = null;

            for (int i = 0; i < cleanupOwnedMarbles.Count; i++)
            {
                if (cleanupOwnedMarbles[i] != null)
                {
                    Destroy(cleanupOwnedMarbles[i].gameObject);
                }
            }

            cleanupOwnedMarbles.Clear();
            if (destroyRecoveredBoxes)
            {
                for (int i = 0; i < recoveredSourceBoxes.Count; i++)
                {
                    if (recoveredSourceBoxes[i] != null)
                    {
                        sourceBoxBoardController?.UnregisterRecoveredSourceBox(
                            recoveredSourceBoxes[i]);
                        Destroy(recoveredSourceBoxes[i].gameObject);
                    }
                }

                recoveredSourceBoxes.Clear();
            }

            activeGroups.Clear();
            candidates.Clear();
            selectedCandidates.Clear();
            eligibleMarbles.Clear();
            cleanupInProgress = false;
            actionInProgress = false;
            recoveryUiActive = false;
            activeRecoveryToken = string.Empty;
        }

        private void SetRecoveryInteractionBlocked(bool blocked)
        {
            gameplayInputController?.SetInputEnabled(!blocked);
            gameplayHudController?.SetSettingsInteractionEnabled(!blocked);
        }

        private bool IsCurrentRecoveryValid()
        {
            return recoveryUiActive && resultFlowController != null &&
                   resultFlowController.CanRecoverSession(activeRecoveryToken);
        }

        private void HideRecoveryUiImmediately()
        {
            EndFirstCleanupTutorial();
            ClearCleanupPreviewOutlines();
            gameplayHudController?.RestoreGoldDisplaySortingOrder();
            failOfferPanelController?.StopPreviewPlayback();
            if (cleanUpOfferRoot != null)
            {
                cleanUpOfferRoot.SetActive(false);
            }

            if (failOfferPanel != null)
            {
                failOfferPanel.HideImmediately();
            }

            UnbindGold();
        }

        private void BindSaveManager()
        {
            SaveManager current = SaveManager.Instance;
            if (current == null || !current.IsInitialized)
            {
                return;
            }

            saveManager = current;
        }

        private void BindGold()
        {
            BindSaveManager();
            if (saveManager == null)
            {
                return;
            }

            if (!goldSubscribed)
            {
                saveManager.GoldChanged += HandleGoldChanged;
                goldSubscribed = true;
            }

            SetFailOfferGold(saveManager.Gold);
        }

        private void UnbindGold()
        {
            if (saveManager != null && goldSubscribed)
            {
                saveManager.GoldChanged -= HandleGoldChanged;
            }

            goldSubscribed = false;
        }

        private void HandleGoldChanged(int gold)
        {
            SetFailOfferGold(gold);
            RefreshButtonStates();
        }

        private void SetFailOfferGold(int gold)
        {
            failOfferGoldText?.SetText("{0}", Mathf.Max(0, gold));
        }

        private void BindStoreManager()
        {
            StoreManager current = StoreManager.Instance;
            if (storeManager == current)
            {
                return;
            }

            UnbindStoreManager();
            storeManager = current;
            if (storeManager == null)
            {
                return;
            }

            storeManager.StoreReady += HandleStoreReady;
            storeManager.ProductStateChanged += HandleProductStateChanged;
            storeManager.PurchaseSucceeded += HandlePurchaseSucceeded;
            storeManager.PurchaseFailed += HandlePurchaseFailed;
            storeManager.PurchaseCancelled += HandlePurchaseCancelled;
        }

        private void UnbindStoreManager()
        {
            if (storeManager != null)
            {
                storeManager.StoreReady -= HandleStoreReady;
                storeManager.ProductStateChanged -= HandleProductStateChanged;
                storeManager.PurchaseSucceeded -= HandlePurchaseSucceeded;
                storeManager.PurchaseFailed -= HandlePurchaseFailed;
                storeManager.PurchaseCancelled -= HandlePurchaseCancelled;
            }

            storeManager = null;
        }

        private void HandleStoreReady()
        {
            RefreshStorePresentation();
            RefreshButtonStates();
        }

        private void HandleProductStateChanged(string productId)
        {
            if (productId != StoreProductIds.FailOffer)
            {
                return;
            }

            RefreshStorePresentation();
            if (storeManager == null || !storeManager.IsPurchasing)
            {
                actionInProgress = cleanupInProgress;
            }

            RefreshButtonStates();
        }

        private void HandlePurchaseFailed(StorePurchaseFailure failure)
        {
            if (failure.ProductId == StoreProductIds.FailOffer)
            {
                actionInProgress = cleanupInProgress;
                RefreshButtonStates();
            }
        }

        private void HandlePurchaseCancelled(string productId)
        {
            if (productId == StoreProductIds.FailOffer)
            {
                actionInProgress = cleanupInProgress;
                RefreshButtonStates();
            }
        }

        private void HandlePurchaseSucceeded(string productId)
        {
            if (productId != StoreProductIds.FailOffer)
            {
                return;
            }

            actionInProgress = cleanupInProgress;
            RefreshButtonStates();
        }

        private static bool TryCaptureAnalyticsLevelContext(out AnalyticsLevelContext context)
        {
            context = default;
            LevelAnalyticsTracker tracker = LevelAnalyticsTracker.Instance;
            if (tracker == null ||
                !tracker.TryGetLevelContext(
                    out context.DisplayedLevelNumber,
                    out context.InternalLevelNumber))
            {
                return false;
            }

            context.IsValid = true;
            return true;
        }

        private static void Track(AnalyticsEvent analyticsEvent)
        {
            AnalyticsBootstrap.Instance?.Track(analyticsEvent);
        }

        private void RefreshStorePresentation()
        {
            BindStoreManager();
            string localizedPrice = storeManager?.GetLocalizedPrice(StoreProductIds.FailOffer);
            if (premiumPriceText != null && !string.IsNullOrEmpty(localizedPrice))
            {
                premiumPriceText.text = localizedPrice;
            }
        }

        private void RefreshConfiguredTexts()
        {
            if (firstCleanupTutorialActive) freeCleanupLabel?.RefreshString();
            else cleanUpRequiredGoldText?.SetText("{0}", cleanupGoldCost);
            playOnRequiredGoldText?.SetText("{0}", cleanupGoldCost);

            if (!StoreCatalog.TryGet(StoreProductIds.FailOffer, out StoreProductDefinition product))
            {
                return;
            }

            offerGoldRewardText?.SetText("{0}", GetRewardAmount(product, StoreRewardType.Coin));
            booster01QuantityValues["quantity"] = GetRewardAmount(product, StoreRewardType.Hand);
            booster02QuantityValues["quantity"] = GetRewardAmount(product, StoreRewardType.Shuffle);
            booster03QuantityValues["quantity"] = GetRewardAmount(product, StoreRewardType.Ufo);
            offerBooster01QuantityFormat?.RefreshString();
            offerBooster02QuantityFormat?.RefreshString();
            offerBooster03QuantityFormat?.RefreshString();
        }

        private void BindLocalization()
        {
            BindQuantityFormat(
                offerBooster01QuantityFormat,
                booster01QuantityValues,
                ref booster01QuantityArguments,
                HandleBooster01QuantityChanged,
                ref booster01QuantitySubscribed);
            BindQuantityFormat(
                offerBooster02QuantityFormat,
                booster02QuantityValues,
                ref booster02QuantityArguments,
                HandleBooster02QuantityChanged,
                ref booster02QuantitySubscribed);
            BindQuantityFormat(
                offerBooster03QuantityFormat,
                booster03QuantityValues,
                ref booster03QuantityArguments,
                HandleBooster03QuantityChanged,
                ref booster03QuantitySubscribed);
        }

        private static void BindQuantityFormat(
            LocalizedString localizedString,
            Dictionary<string, object> values,
            ref object[] arguments,
            LocalizedString.ChangeHandler handler,
            ref bool subscribed)
        {
            if (subscribed || localizedString == null || localizedString.IsEmpty)
            {
                return;
            }

            if (!values.ContainsKey("quantity"))
            {
                values["quantity"] = 0;
            }

            if (arguments == null)
            {
                arguments = new object[] { values };
            }

            localizedString.Arguments = arguments;
            localizedString.StringChanged += handler;
            subscribed = true;
        }

        private void UnbindLocalization()
        {
            UnbindQuantityFormat(
                offerBooster01QuantityFormat,
                HandleBooster01QuantityChanged,
                ref booster01QuantitySubscribed);
            UnbindQuantityFormat(
                offerBooster02QuantityFormat,
                HandleBooster02QuantityChanged,
                ref booster02QuantitySubscribed);
            UnbindQuantityFormat(
                offerBooster03QuantityFormat,
                HandleBooster03QuantityChanged,
                ref booster03QuantitySubscribed);
        }

        private static void UnbindQuantityFormat(
            LocalizedString localizedString,
            LocalizedString.ChangeHandler handler,
            ref bool subscribed)
        {
            if (!subscribed)
            {
                return;
            }

            localizedString.StringChanged -= handler;
            subscribed = false;
        }

        private void HandleBooster01QuantityChanged(string localizedText)
        {
            offerBooster01RewardText?.SetText(localizedText);
        }

        private void HandleBooster02QuantityChanged(string localizedText)
        {
            offerBooster02RewardText?.SetText(localizedText);
        }

        private void HandleBooster03QuantityChanged(string localizedText)
        {
            offerBooster03RewardText?.SetText(localizedText);
        }

        private static int GetRewardAmount(
            StoreProductDefinition product,
            StoreRewardType rewardType)
        {
            for (int i = 0; i < product.Rewards.Count; i++)
            {
                if (product.Rewards[i].Type == rewardType)
                {
                    return product.Rewards[i].Amount;
                }
            }

            return 0;
        }

        private void RefreshButtonStates()
        {
            bool canChooseRecovery = IsCurrentRecoveryValid() && !actionInProgress;
            if (cleanUpButton != null)
            {
                cleanUpButton.interactable = canChooseRecovery;
            }

            if (quitButton != null)
            {
                quitButton.interactable = canChooseRecovery;
            }

            if (playOnButton != null)
            {
                playOnButton.interactable = canChooseRecovery;
            }

            if (failOfferCloseButton != null)
            {
                failOfferCloseButton.interactable = canChooseRecovery &&
                                                    !resultFlowController.IsFailOfferPurchaseLocked;
            }

            if (premiumBuyButton != null)
            {
                premiumBuyButton.interactable = canChooseRecovery &&
                                                storeManager != null &&
                                                storeManager.CanPurchase(StoreProductIds.FailOffer);
            }
        }
    }
}

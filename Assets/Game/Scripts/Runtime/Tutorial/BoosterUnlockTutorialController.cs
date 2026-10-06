using DG.Tweening;
using Game.Shared.Save;
using Game.Shared.UI;
using Gameplay.Boosters;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.UI.HUD;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Gameplay.Tutorial
{
    public enum BoosterTutorialState
    {
        None = 0,
        InfoPanel = 1,
        Unlocking = 2,
        WaitForHandBooster = 3,
        WaitForHandSourceBox = 4,
        WaitForShuffle = 5,
        WaitForUfo = 6,
        WaitingForInfoPanel = 7,
        ClosingInfoPanel = 8
    }

    [DisallowMultipleComponent]
    public sealed class BoosterUnlockTutorialController : MonoBehaviour
    {
        private const float InfoPanelDelaySeconds = 1f;
        private const float LockTextFadeDurationSeconds = 0.1f;
        private const float ValueBackgroundDurationSeconds = 0.25f;

        [Header("UI")]
        [SerializeField] private GameObject tutorialOverlay;
        [SerializeField] private GameObject cutoutRoot;
        [SerializeField] private RectTransform tutorialSpace;
        [SerializeField] private RectTransform cutoutMask;
        [SerializeField] private GameObject infoPanel;
        [SerializeField] private RectTransform infoPanelAnimationTarget;
        [SerializeField] private CanvasGroup infoPanelCanvasGroup;
        [SerializeField] private UIConfig uiConfig;
        [SerializeField] private Image boosterImage;
        [SerializeField] private TMP_Text boosterNameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TweenButton acceptButton;
        [SerializeField] private Canvas tutorialCanvas;
        [SerializeField] private Camera gameplayCamera;

        [Header("Runtime Sources")]
        [SerializeField] private BoosterHudController boosterHudController;
        [SerializeField] private AddBoosterPanelController addBoosterPanelController;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;

        [Header("Tutorial Info")]
        [SerializeField] private LevelOneTapTutorialController tutorialInfoController;
        [SerializeField] private LevelBuildController levelBuildController;
        [SerializeField] private BoosterController boosterController;
        [SerializeField] private LocalizedString handSelectionMessage =
            new LocalizedString("General", "tutorial.hand.select_source");
        [SerializeField] private LocalizedString shuffleDescriptionMessage =
            new LocalizedString("General", "tutorial.shuffle.description");
        [SerializeField] private LocalizedString ufoDescriptionMessage =
            new LocalizedString("General", "tutorial.ufo.description");

        [Header("Hand Target")]
        [SerializeField] private Vector2Int handTargetCoordinate = new Vector2Int(1, 2);

        [Header("Cutout")]
        [SerializeField] private Vector2 boosterCutoutPadding = new Vector2(30f, 30f);
        [SerializeField] private Vector2 sourceBoxCutoutPadding = new Vector2(35f, 35f);
        [SerializeField, Min(0.01f)] private float cutoutMinScale = 0.8f;
        [SerializeField, Min(0.01f)] private float cutoutMaxScale = 1f;
        [SerializeField] private float cutoutYOffset;
        [SerializeField, Min(0.01f)] private float pulseHalfDuration = 0.55f;
        [SerializeField] private Ease pulseEase = Ease.InOutSine;

        [Header("Unlock Animation")]
        [SerializeField, Min(0f)] private float lockDuration = 0.22f;
        [SerializeField, Min(1f)] private float lockScaleMultiplier = 1.3f;
        [SerializeField, Range(0.01f, 1f)] private float boosterStartScaleMultiplier = 0.35f;
        [SerializeField, Min(0f)] private float boosterDuration = 0.34f;
        [SerializeField] private Ease boosterEase = Ease.OutBack;

        private static BoosterUnlockTutorialController activeInstance;

        private BoosterTutorialState state;
        private BoosterType tutorialBooster = BoosterType.None;
        private SourceBox targetSourceBox;
        private Tween infoPanelDelayTween;
        private Sequence infoPanelSequence;
        private Sequence unlockSequence;
        private Tween pulseTween;
        private Image animatedBoosterIcon;
        private Vector3 authoredIconScale;
        private Color authoredIconColor;
        private TMP_Text animatedLockText;
        private Color authoredLockTextColor;
        private RectTransform animatedValueBackground;
        private Vector3 authoredValueBackgroundScale;
        private Vector3 authoredCutoutScale;
        private bool hasAuthoredCutoutScale;
        private LocalizedString activeBoosterName;
        private LocalizedString activeDescription;
        private bool boosterNameSubscribed;
        private bool descriptionSubscribed;
        private SaveManager saveManager;
        private Vector3 authoredInfoPanelScale;
        private bool hasAuthoredInfoPanelScale;

        public BoosterTutorialState State => state;
        public bool IsRunning => state != BoosterTutorialState.None;
        public static bool BlocksAutomaticGameplay => activeInstance != null && activeInstance.IsRunning;

        private void Awake()
        {
            CacheCutoutScale();
            CacheInfoPanelScale();
            RegisterAcceptButton();
        }

        private void OnEnable()
        {
            activeInstance = this;
            RegisterAcceptButton();
            if (levelBuildController != null)
            {
                levelBuildController.LevelClearing += HideHandTutorialInfo;
            }
            if (boosterController != null)
            {
                boosterController.BoosterStateChanged += HandleHandBoosterStateChanged;
            }
        }

        private void OnDisable()
        {
            if (levelBuildController != null)
            {
                levelBuildController.LevelClearing -= HideHandTutorialInfo;
            }
            if (boosterController != null)
            {
                boosterController.BoosterStateChanged -= HandleHandBoosterStateChanged;
            }

            if (activeInstance == this)
            {
                activeInstance = null;
            }

            UnregisterAcceptButton();
            ClearRuntimeState(true);
        }

        private void OnDestroy()
        {
            if (activeInstance == this)
            {
                activeInstance = null;
            }

            UnregisterAcceptButton();
            ClearRuntimeState(true);
        }

        private void OnValidate()
        {
            cutoutMinScale = Mathf.Max(0.01f, cutoutMinScale);
            cutoutMaxScale = Mathf.Max(cutoutMinScale, cutoutMaxScale);
            pulseHalfDuration = Mathf.Max(0.01f, pulseHalfDuration);
            lockDuration = Mathf.Max(0f, lockDuration);
            lockScaleMultiplier = Mathf.Max(1f, lockScaleMultiplier);
            boosterStartScaleMultiplier = Mathf.Clamp(boosterStartScaleMultiplier, 0.01f, 1f);
            boosterDuration = Mathf.Max(0f, boosterDuration);
        }

        private void LateUpdate()
        {
            if (state == BoosterTutorialState.WaitForHandSourceBox && targetSourceBox != null)
            {
                PositionCutoutOnSourceBox(targetSourceBox);
            }
        }

        public void TryStartTutorial(int displayedLevelNumber, LevelDefinition levelDefinition)
        {
            StopWithoutCompletion();

            if (infoPanel == null || infoPanelAnimationTarget == null || infoPanelCanvasGroup == null ||
                uiConfig == null || boosterHudController == null ||
                !boosterHudController.TryGetBoosterAtUnlockLevel(displayedLevelNumber, out BoosterType boosterType))
            {
                return;
            }

            saveManager = SaveManager.Instance;
            if (saveManager == null || !saveManager.IsInitialized ||
                !boosterHudController.TryGetInventoryId(boosterType, out string inventoryId) ||
                saveManager.IsBoosterUnlocked(inventoryId))
            {
                return;
            }

            if (addBoosterPanelController == null ||
                !addBoosterPanelController.TryGetOfferPresentation(
                    boosterType,
                    out Sprite offerSprite,
                    out LocalizedString boosterName,
                    out LocalizedString description))
            {
                Debug.LogError(
                    $"{nameof(BoosterUnlockTutorialController)} on '{name}' cannot start because Add Booster metadata for {boosterType} is unavailable.",
                    this);
                return;
            }

            if (boosterType == BoosterType.Hand && !TryResolveHandTarget(levelDefinition))
            {
                return;
            }

            tutorialBooster = boosterType;
            state = BoosterTutorialState.WaitingForInfoPanel;
            SetActive(tutorialOverlay, false);
            SetActive(cutoutRoot, false);
            HideInfoPanelImmediately();
            BindOfferPresentation(offerSprite, boosterName, description);
            boosterHudController.RestoreTutorialPresentation(tutorialBooster);

            if (saveManager.GetBoosterAmount(inventoryId) <= 0)
            {
                saveManager.AddBooster(inventoryId, 1);
            }

            gameObject.SetActive(true);
            StartInfoPanelDelay();
        }

        public bool ShouldPresentBoosterLocked(BoosterType boosterType)
        {
            return IsRunning && boosterType == tutorialBooster &&
                   (state == BoosterTutorialState.WaitingForInfoPanel ||
                    state == BoosterTutorialState.InfoPanel);
        }

        public bool CanInteractWithBooster(BoosterType boosterType)
        {
            if (!IsRunning)
            {
                return true;
            }

            return boosterType == tutorialBooster &&
                   (state == BoosterTutorialState.WaitForHandBooster ||
                    state == BoosterTutorialState.WaitForShuffle ||
                    state == BoosterTutorialState.WaitForUfo);
        }

        public bool CanInteractWithSourceBox(SourceBox sourceBox)
        {
            return !IsRunning ||
                   state == BoosterTutorialState.WaitForHandSourceBox && sourceBox == targetSourceBox;
        }

        public bool CanPresentHandTarget(SourceBox sourceBox)
        {
            if (!IsRunning || tutorialBooster != BoosterType.Hand)
            {
                return true;
            }

            return sourceBox == targetSourceBox &&
                   (state == BoosterTutorialState.WaitForHandBooster ||
                    state == BoosterTutorialState.WaitForHandSourceBox);
        }

        public bool AllowsGeneralGameplayInput()
        {
            return !IsRunning;
        }

        public void NotifyBoosterActivationAccepted(BoosterType boosterType)
        {
            if (!IsRunning || boosterType != tutorialBooster)
            {
                return;
            }

            if (boosterType == BoosterType.Hand && state == BoosterTutorialState.WaitForHandBooster)
            {
                StopPulse();
                state = BoosterTutorialState.WaitForHandSourceBox;
                PositionCutoutOnSourceBox(targetSourceBox);
                StartPulse();
                tutorialInfoController?.ShowTutorialInfo(this, handSelectionMessage);
                return;
            }

            if (boosterType == BoosterType.Shuffle && state == BoosterTutorialState.WaitForShuffle)
            {
                // Preserve completion/save and unlock cleanup at activation acceptance.
                CompleteTutorial();
                return;
            }

            if (boosterType == BoosterType.Ufo && state == BoosterTutorialState.WaitForUfo)
            {
                HideCutout();
                CompleteTutorial();
            }
        }

        public void NotifySourceBoxInteractionAccepted(SourceBox sourceBox)
        {
            if (state == BoosterTutorialState.WaitForHandSourceBox && sourceBox == targetSourceBox)
            {
                CompleteTutorial();
            }
        }

        private void HandleAcceptClicked()
        {
            if (state != BoosterTutorialState.InfoPanel || infoPanel == null ||
                !boosterHudController.TryGetTutorialPresentation(
                    tutorialBooster,
                    out RectTransform boosterRect,
                    out Image icon,
                    out TMP_Text lockText,
                    out RectTransform valueBackgroundRect,
                    out Sprite lockedSprite,
                    out Sprite unlockedSprite))
            {
                return;
            }

            state = BoosterTutorialState.ClosingInfoPanel;
            PlayInfoPanelCloseAnimation(() => BeginUnlockAfterInfoPanelClosed(
                    boosterRect,
                    icon,
                    lockText,
                    valueBackgroundRect,
                    lockedSprite,
                    unlockedSprite));

            UnbindOfferLocalization();
        }

        private void BeginUnlockAfterInfoPanelClosed(
            RectTransform boosterRect,
            Image icon,
            TMP_Text lockText,
            RectTransform valueBackgroundRect,
            Sprite lockedSprite,
            Sprite unlockedSprite)
        {
            if (state != BoosterTutorialState.ClosingInfoPanel)
            {
                return;
            }

            SetActive(cutoutRoot, true);
            PositionCutoutOnRectTransform(boosterRect, boosterCutoutPadding);
            state = BoosterTutorialState.Unlocking;
            PlayUnlockAnimation(icon, lockText, valueBackgroundRect, lockedSprite, unlockedSprite);
        }

        private void PlayUnlockAnimation(
            Image icon,
            TMP_Text lockText,
            RectTransform valueBackgroundRect,
            Sprite lockedSprite,
            Sprite unlockedSprite)
        {
            KillUnlockSequence(true);
            animatedBoosterIcon = icon;
            authoredIconScale = icon.rectTransform.localScale;
            authoredIconColor = icon.color;
            animatedLockText = lockText;
            if (animatedLockText != null)
            {
                authoredLockTextColor = animatedLockText.color;
                animatedLockText.color = authoredLockTextColor;
            }

            animatedValueBackground = valueBackgroundRect;
            if (animatedValueBackground != null)
            {
                authoredValueBackgroundScale = animatedValueBackground.localScale;
                animatedValueBackground.localScale = Vector3.zero;
            }

            icon.sprite = lockedSprite;
            icon.rectTransform.localScale = authoredIconScale;
            icon.color = authoredIconColor;

            unlockSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);

            if (animatedLockText != null)
            {
                unlockSequence.Insert(0f, animatedLockText
                    .DOFade(0f, LockTextFadeDurationSeconds)
                    .SetEase(Ease.OutQuad));
            }

            if (lockDuration > 0f)
            {
                unlockSequence.Append(icon.rectTransform
                    .DOScale(authoredIconScale * lockScaleMultiplier, lockDuration)
                    .SetEase(Ease.OutQuad));
                unlockSequence.Join(icon.DOFade(0f, lockDuration).SetEase(Ease.OutQuad));
            }

            unlockSequence.AppendCallback(() =>
            {
                icon.sprite = unlockedSprite;
                icon.color = authoredIconColor;
                icon.rectTransform.localScale = authoredIconScale * boosterStartScaleMultiplier;
            });

            if (boosterDuration > 0f)
            {
                unlockSequence.Append(icon.rectTransform
                    .DOScale(authoredIconScale, boosterDuration)
                    .SetEase(boosterEase));
            }

            unlockSequence.AppendCallback(PrepareValueBackgroundForUnlock);
            if (animatedValueBackground != null)
            {
                unlockSequence.Append(animatedValueBackground
                    .DOScale(authoredValueBackgroundScale, ValueBackgroundDurationSeconds)
                    .SetEase(Ease.OutBack));
            }

            unlockSequence.OnComplete(() =>
            {
                unlockSequence = null;
                RestoreAnimatedPresentation();
                state = GetWaitState(tutorialBooster);
                StartPulse();
                if (state == BoosterTutorialState.WaitForShuffle)
                {
                    tutorialInfoController?.ShowTutorialInfo(this, shuffleDescriptionMessage);
                }
                else if (state == BoosterTutorialState.WaitForUfo)
                {
                    tutorialInfoController?.ShowTutorialInfo(this, ufoDescriptionMessage);
                }
            });
        }

        private void CompleteTutorial()
        {
            if (!IsRunning)
            {
                return;
            }

            if (saveManager != null && boosterHudController != null &&
                boosterHudController.TryGetInventoryId(tutorialBooster, out string inventoryId))
            {
                bool alreadyCompleted = saveManager.IsBoosterUnlocked(inventoryId);
                saveManager.SetBoosterUnlocked(inventoryId, true);
                if (!alreadyCompleted)
                {
                    saveManager.Save();
                }
            }

            ClearRuntimeState(true);
            gameObject.SetActive(false);
        }

        private void StopWithoutCompletion()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
            else
            {
                ClearRuntimeState(true);
            }
        }

        private void ClearRuntimeState(bool restorePresentation)
        {
            HideHandTutorialInfo();
            BoosterType completedBooster = tutorialBooster;
            KillInfoPanelDelay();
            StopPulse();
            KillUnlockSequence(restorePresentation);
            UnbindOfferLocalization();
            HideInfoPanelImmediately();
            SetActive(cutoutRoot, false);
            SetActive(tutorialOverlay, false);
            targetSourceBox = null;
            tutorialBooster = BoosterType.None;
            state = BoosterTutorialState.None;
            saveManager = null;

            if (restorePresentation && completedBooster != BoosterType.None)
            {
                boosterHudController?.RestoreTutorialPresentation(completedBooster);
            }
        }

        private void HideHandTutorialInfo()
        {
            tutorialInfoController?.HideTutorialInfo(this);
        }

        private void HandleHandBoosterStateChanged(BoosterType type, BoosterState boosterState)
        {
            if (type != BoosterType.Hand || boosterState != BoosterState.Targeting)
            {
                HideHandTutorialInfo();
            }
        }

        private void StartInfoPanelDelay()
        {
            KillInfoPanelDelay();
            infoPanelDelayTween = DOVirtual.DelayedCall(InfoPanelDelaySeconds, ShowInfoPanel)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        }

        private void ShowInfoPanel()
        {
            infoPanelDelayTween = null;
            if (state != BoosterTutorialState.WaitingForInfoPanel || infoPanel == null)
            {
                return;
            }

            state = BoosterTutorialState.InfoPanel;
            SetActive(tutorialOverlay, true);
            PlayInfoPanelOpenAnimation();
        }

        private void HideInfoPanelImmediately()
        {
            if (infoPanel == null)
            {
                return;
            }

            KillInfoPanelSequence();
            CacheInfoPanelScale();
            if (infoPanelAnimationTarget != null && hasAuthoredInfoPanelScale)
            {
                infoPanelAnimationTarget.localScale = authoredInfoPanelScale;
            }

            if (infoPanelCanvasGroup != null)
            {
                infoPanelCanvasGroup.alpha = 0f;
                infoPanelCanvasGroup.interactable = false;
                infoPanelCanvasGroup.blocksRaycasts = false;
            }

            SetActive(infoPanel, false);
        }

        private void PlayInfoPanelOpenAnimation()
        {
            if (infoPanel == null || infoPanelAnimationTarget == null || infoPanelCanvasGroup == null ||
                uiConfig == null)
            {
                return;
            }

            KillInfoPanelSequence();
            CacheInfoPanelScale();
            SetActive(infoPanel, true);
            infoPanelCanvasGroup.alpha = 0f;
            infoPanelCanvasGroup.interactable = true;
            infoPanelCanvasGroup.blocksRaycasts = true;
            infoPanelAnimationTarget.localScale = authoredInfoPanelScale * uiConfig.PanelInitialScale;

            infoPanelSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
            infoPanelSequence.Append(infoPanelAnimationTarget
                .DOScale(authoredInfoPanelScale * uiConfig.PanelOvershootScale, uiConfig.PanelGrowDuration)
                .SetEase(uiConfig.PanelGrowEase));
            infoPanelSequence.Join(infoPanelCanvasGroup
                .DOFade(1f, uiConfig.PanelGrowDuration)
                .SetEase(uiConfig.PanelGrowEase));
            infoPanelSequence.Append(infoPanelAnimationTarget
                .DOScale(authoredInfoPanelScale, uiConfig.PanelSettleDuration)
                .SetEase(uiConfig.PanelSettleEase));
            infoPanelSequence.OnComplete(() => infoPanelSequence = null);
        }

        private void PlayInfoPanelCloseAnimation(System.Action onClosed)
        {
            if (infoPanel == null || infoPanelAnimationTarget == null || infoPanelCanvasGroup == null ||
                uiConfig == null)
            {
                return;
            }

            KillInfoPanelSequence();
            infoPanelCanvasGroup.interactable = false;
            infoPanelCanvasGroup.blocksRaycasts = false;
            infoPanelSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
            infoPanelSequence.Join(infoPanelAnimationTarget
                .DOScale(authoredInfoPanelScale * uiConfig.PanelInitialScale, uiConfig.PanelCloseDuration)
                .SetEase(uiConfig.PanelCloseEase));
            infoPanelSequence.Join(infoPanelCanvasGroup
                .DOFade(0f, uiConfig.PanelCloseDuration)
                .SetEase(uiConfig.PanelCloseEase));
            infoPanelSequence.OnComplete(() =>
            {
                infoPanelSequence = null;
                HideInfoPanelImmediately();
                onClosed?.Invoke();
            });
        }

        private void CacheInfoPanelScale()
        {
            if (hasAuthoredInfoPanelScale || infoPanelAnimationTarget == null)
            {
                return;
            }

            authoredInfoPanelScale = infoPanelAnimationTarget.localScale;
            hasAuthoredInfoPanelScale = true;
        }

        private void KillInfoPanelSequence()
        {
            infoPanelSequence?.Kill(false);
            infoPanelSequence = null;
        }

        private void KillInfoPanelDelay()
        {
            infoPanelDelayTween?.Kill(false);
            infoPanelDelayTween = null;
        }

        private bool TryResolveHandTarget(LevelDefinition levelDefinition)
        {
            targetSourceBox = null;
            if (levelDefinition == null || sourceBoxBoardController == null ||
                !levelDefinition.TryGetCell(handTargetCoordinate.x, handTargetCoordinate.y, out LevelCellData targetCell) ||
                targetCell.CellType != LevelCellType.SourceBox || targetCell.ColorId != MarbleColorId.Pink)
            {
                Debug.LogError(
                    $"{nameof(BoosterUnlockTutorialController)} on '{name}' expected the Level 6 pink SourceBox at row {handTargetCoordinate.x}, column {handTargetCoordinate.y}.",
                    this);
                return false;
            }

            int cellIndex = handTargetCoordinate.x * levelDefinition.ColumnCount + handTargetCoordinate.y;
            if (sourceBoxBoardController.TryGetSourceBoxByCellIndex(cellIndex, out targetSourceBox))
            {
                return true;
            }

            Debug.LogError(
                $"{nameof(BoosterUnlockTutorialController)} on '{name}' could not resolve the runtime SourceBox at cell {cellIndex}.",
                this);
            return false;
        }

        private void BindOfferPresentation(
            Sprite sprite,
            LocalizedString boosterName,
            LocalizedString description)
        {
            UnbindOfferLocalization();
            if (boosterImage != null)
            {
                boosterImage.sprite = sprite;
            }

            activeBoosterName = boosterName;
            activeDescription = description;
            if (activeBoosterName != null && !activeBoosterName.IsEmpty)
            {
                activeBoosterName.StringChanged += HandleBoosterNameChanged;
                boosterNameSubscribed = true;
                activeBoosterName.RefreshString();
            }

            if (activeDescription != null && !activeDescription.IsEmpty)
            {
                activeDescription.StringChanged += HandleDescriptionChanged;
                descriptionSubscribed = true;
                activeDescription.RefreshString();
            }
        }

        private void UnbindOfferLocalization()
        {
            if (boosterNameSubscribed && activeBoosterName != null)
            {
                activeBoosterName.StringChanged -= HandleBoosterNameChanged;
            }

            if (descriptionSubscribed && activeDescription != null)
            {
                activeDescription.StringChanged -= HandleDescriptionChanged;
            }

            boosterNameSubscribed = false;
            descriptionSubscribed = false;
            activeBoosterName = null;
            activeDescription = null;
        }

        private void HandleBoosterNameChanged(string localizedText)
        {
            boosterNameText?.SetText(localizedText);
        }

        private void HandleDescriptionChanged(string localizedText)
        {
            descriptionText?.SetText(localizedText);
        }

        private void PositionCutoutOnRectTransform(RectTransform target, Vector2 padding)
        {
            if (target == null || tutorialSpace == null || cutoutMask == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Vector3[] worldCorners = new Vector3[4];
            target.GetWorldCorners(worldCorners);
            Camera eventCamera = GetCanvasEventCamera();
            Vector2 screenMin = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCorners[0]);
            Vector2 screenMax = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCorners[2]);
            ApplyScreenBoundsToCutout(screenMin, screenMax, padding);
        }

        private void PositionCutoutOnSourceBox(SourceBox sourceBox)
        {
            if (sourceBox == null || gameplayCamera == null ||
                !sourceBox.TryGetInputBounds(out Bounds bounds))
            {
                return;
            }

            Vector2 screenMin = gameplayCamera.WorldToScreenPoint(
                new Vector3(bounds.min.x, bounds.min.y, bounds.center.z));
            Vector2 screenMax = gameplayCamera.WorldToScreenPoint(
                new Vector3(bounds.max.x, bounds.max.y, bounds.center.z));
            ApplyScreenBoundsToCutout(screenMin, screenMax, sourceBoxCutoutPadding);
        }

        private void ApplyScreenBoundsToCutout(Vector2 screenMin, Vector2 screenMax, Vector2 padding)
        {
            if (tutorialSpace == null || cutoutMask == null)
            {
                return;
            }

            Camera eventCamera = GetCanvasEventCamera();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    tutorialSpace,
                    screenMin,
                    eventCamera,
                    out Vector2 localMin) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    tutorialSpace,
                    screenMax,
                    eventCamera,
                    out Vector2 localMax))
            {
                return;
            }

            cutoutMask.anchorMin = new Vector2(0.5f, 0.5f);
            cutoutMask.anchorMax = new Vector2(0.5f, 0.5f);
            cutoutMask.anchoredPosition = (localMin + localMax) * 0.5f + Vector2.up * cutoutYOffset;
            cutoutMask.sizeDelta = new Vector2(
                Mathf.Abs(localMax.x - localMin.x) + Mathf.Max(0f, padding.x),
                Mathf.Abs(localMax.y - localMin.y) + Mathf.Max(0f, padding.y));
        }

        private Camera GetCanvasEventCamera()
        {
            if (tutorialCanvas == null || tutorialCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return tutorialCanvas.worldCamera != null ? tutorialCanvas.worldCamera : gameplayCamera;
        }

        private void StartPulse()
        {
            StopPulse();
            if (cutoutMask == null)
            {
                return;
            }

            CacheCutoutScale();
            cutoutMask.localScale = authoredCutoutScale * cutoutMinScale;
            pulseTween = cutoutMask
                .DOScale(authoredCutoutScale * cutoutMaxScale, pulseHalfDuration)
                .SetEase(pulseEase)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        }

        private void StopPulse()
        {
            pulseTween?.Kill(false);
            pulseTween = null;
            if (cutoutMask != null && hasAuthoredCutoutScale)
            {
                cutoutMask.localScale = authoredCutoutScale;
            }
        }

        private void HideCutout()
        {
            StopPulse();
            SetActive(cutoutRoot, false);
        }

        private void PrepareValueBackgroundForUnlock()
        {
            boosterHudController?.RestoreTutorialPresentation(tutorialBooster);
            if (animatedValueBackground != null)
            {
                animatedValueBackground.localScale = Vector3.zero;
            }
        }

        private void KillUnlockSequence(bool restorePresentation)
        {
            unlockSequence?.Kill(false);
            unlockSequence = null;
            if (restorePresentation)
            {
                RestoreAnimatedPresentation();
            }
        }

        private void RestoreAnimatedPresentation()
        {
            bool hasAnimatedPresentation = animatedBoosterIcon != null || animatedLockText != null ||
                                           animatedValueBackground != null;
            if (!hasAnimatedPresentation)
            {
                return;
            }

            if (animatedBoosterIcon != null)
            {
                animatedBoosterIcon.rectTransform.localScale = authoredIconScale;
                animatedBoosterIcon.color = authoredIconColor;
            }

            if (animatedLockText != null)
            {
                animatedLockText.color = authoredLockTextColor;
            }

            if (animatedValueBackground != null)
            {
                animatedValueBackground.localScale = authoredValueBackgroundScale;
            }

            boosterHudController?.RestoreTutorialPresentation(tutorialBooster);
            animatedBoosterIcon = null;
            animatedLockText = null;
            animatedValueBackground = null;
        }

        private void CacheCutoutScale()
        {
            if (!hasAuthoredCutoutScale && cutoutMask != null)
            {
                authoredCutoutScale = cutoutMask.localScale;
                hasAuthoredCutoutScale = true;
            }
        }

        private void RegisterAcceptButton()
        {
            if (acceptButton == null)
            {
                return;
            }

            acceptButton.onClick.RemoveListener(HandleAcceptClicked);
            acceptButton.onClick.AddListener(HandleAcceptClicked);
        }

        private void UnregisterAcceptButton()
        {
            acceptButton?.onClick.RemoveListener(HandleAcceptClicked);
        }

        private static BoosterTutorialState GetWaitState(BoosterType boosterType)
        {
            switch (boosterType)
            {
                case BoosterType.Hand:
                    return BoosterTutorialState.WaitForHandBooster;
                case BoosterType.Shuffle:
                    return BoosterTutorialState.WaitForShuffle;
                case BoosterType.Ufo:
                    return BoosterTutorialState.WaitForUfo;
                default:
                    return BoosterTutorialState.None;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}

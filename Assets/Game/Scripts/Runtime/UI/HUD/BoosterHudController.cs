using System;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Save;
using Gameplay.Analytics;
using Gameplay.Boosters;
using Gameplay.Levels;
using Gameplay.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Gameplay.UI.HUD
{
    [DisallowMultipleComponent]
    public sealed class BoosterHudController : MonoBehaviour
    {
        private const string HandBoosterId = "hand";
        private const string ShuffleBoosterId = "shuffle";
        private const string UfoBoosterId = "ufo";

        [Serializable]
        private sealed class BoosterPresentation
        {
            [Min(1)] public int unlockLevel;
            public Image icon;
            public TMP_Text unlockLevelText;
            public GameObject valueBackground;
            public GameObject addIndicator;
            public TMP_Text countText;
            public Sprite unlockedIcon;
        }

        [Header("Buttons")]
        [SerializeField] private Button handButton;
        [SerializeField] private Button shuffleButton;
        [SerializeField] private Button ufoButton;

        [Header("Booster Presentation")]
        [SerializeField] private Sprite lockedBoosterSprite;
        [SerializeField] private BoosterPresentation handPresentation = new BoosterPresentation
        {
            unlockLevel = 6
        };
        [SerializeField] private BoosterPresentation shufflePresentation = new BoosterPresentation
        {
            unlockLevel = 8
        };
        [SerializeField] private BoosterPresentation ufoPresentation = new BoosterPresentation
        {
            unlockLevel = 11
        };

        [Header("Localization")]
        [SerializeField] private LocalizedString unlockLevelPrefix;

        [Header("Runtime")]
        [SerializeField] private BoosterController boosterController;
        [SerializeField] private LevelSessionController levelSessionController;

        [Header("Panels")]
        [SerializeField] private AddBoosterPanelController addBoosterPanelController;
        [SerializeField] private BoosterUnlockTutorialController boosterUnlockTutorialController;

        [Header("Selection Presentation")]
        [SerializeField] private GameObject selectedBoosterFx;
        [SerializeField] private Vector3 selectedFxLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 selectedFxLocalEulerAngles = Vector3.zero;
        [SerializeField] private Vector3 selectedFxLocalScale = Vector3.one;

        private SaveManager saveManager;
        private int consumedBoosterLifecycleVersion = -1;
        private int consumedBoosterDisplayedLevelNumber;
        private int consumedBoosterLevelNumber;
        private BoosterType consumedBoosterType = BoosterType.None;
        private bool hasConsumedBoosterAnalyticsContext;
        private string localizedUnlockLevelPrefix;
        private bool unlockLevelPrefixSubscribed;

        private void Awake()
        {
            SetSelectedBoosterFxActive(false);
        }

        private void OnEnable()
        {
            BindLocalization();
            RegisterListeners();
            RegisterRuntimeEvents();
            RefreshHud();
        }

        private void OnDisable()
        {
            UnregisterListeners();
            UnregisterRuntimeEvents();
            UnbindLocalization();
            SetSelectedBoosterFxActive(false);
            ClearConsumedBoosterAnalyticsContext();
        }

        private void BindLocalization()
        {
            if (unlockLevelPrefixSubscribed || unlockLevelPrefix == null || unlockLevelPrefix.IsEmpty)
            {
                return;
            }

            unlockLevelPrefix.StringChanged += HandleLocalizedUnlockLevelPrefixChanged;
            unlockLevelPrefixSubscribed = true;
            unlockLevelPrefix.RefreshString();
        }

        private void UnbindLocalization()
        {
            if (!unlockLevelPrefixSubscribed)
            {
                return;
            }

            unlockLevelPrefix.StringChanged -= HandleLocalizedUnlockLevelPrefixChanged;
            unlockLevelPrefixSubscribed = false;
        }

        private void HandleLocalizedUnlockLevelPrefixChanged(string localizedPrefix)
        {
            localizedUnlockLevelPrefix = localizedPrefix?.Trim();
            RefreshUnlockLevelTexts();
        }

        private void RegisterListeners()
        {
            if (handButton != null)
            {
                handButton.onClick.RemoveListener(HandleHandClicked);
                handButton.onClick.AddListener(HandleHandClicked);
            }

            if (shuffleButton != null)
            {
                shuffleButton.onClick.RemoveListener(HandleShuffleClicked);
                shuffleButton.onClick.AddListener(HandleShuffleClicked);
            }

            if (ufoButton != null)
            {
                ufoButton.onClick.RemoveListener(HandleUfoClicked);
                ufoButton.onClick.AddListener(HandleUfoClicked);
            }
        }

        private void UnregisterListeners()
        {
            handButton?.onClick.RemoveListener(HandleHandClicked);
            shuffleButton?.onClick.RemoveListener(HandleShuffleClicked);
            ufoButton?.onClick.RemoveListener(HandleUfoClicked);
        }

        private void HandleHandClicked()
        {
            if (boosterUnlockTutorialController != null &&
                !boosterUnlockTutorialController.CanInteractWithBooster(BoosterType.Hand))
            {
                return;
            }

            if (!ValidateBoosterController(BoosterType.Hand))
            {
                return;
            }

            if (boosterController.ActiveBooster == BoosterType.Hand &&
                boosterController.State == BoosterState.Targeting)
            {
                boosterController.CancelActiveBooster();
                return;
            }

            if (boosterController.IsBoosterActive)
            {
                return;
            }

            TryActivateOwnedBooster(BoosterType.Hand, HandBoosterId, handPresentation);
        }

        private void HandleShuffleClicked()
        {
            if (boosterUnlockTutorialController != null &&
                !boosterUnlockTutorialController.CanInteractWithBooster(BoosterType.Shuffle))
            {
                return;
            }

            if (!ValidateBoosterController(BoosterType.Shuffle) ||
                boosterController.IsBoosterActive)
            {
                return;
            }

            TryActivateOwnedBooster(
                BoosterType.Shuffle,
                ShuffleBoosterId,
                shufflePresentation);
        }

        private void HandleUfoClicked()
        {
            if (boosterUnlockTutorialController != null &&
                !boosterUnlockTutorialController.CanInteractWithBooster(BoosterType.Ufo))
            {
                return;
            }

            if (!ValidateBoosterController(BoosterType.Ufo))
            {
                return;
            }

            if (boosterController.IsBoosterActive)
            {
                return;
            }

            TryActivateOwnedBooster(BoosterType.Ufo, UfoBoosterId, ufoPresentation);
        }

        private bool ValidateBoosterController(BoosterType type)
        {
            if (boosterController != null)
            {
                return true;
            }

            Debug.LogWarning(
                $"{nameof(BoosterHudController)} on '{name}' cannot activate {type} because no {nameof(BoosterController)} is assigned.",
                this);
            return false;
        }

        private void TryActivateOwnedBooster(
            BoosterType type,
            string inventoryId,
            BoosterPresentation presentation)
        {
            if (!CanProcessBoosterActivationClick())
            {
                return;
            }

            if (!IsUnlocked(presentation))
            {
                return;
            }

            if (GetBoosterAmount(inventoryId) <= 0)
            {
                if (addBoosterPanelController == null)
                {
                    Debug.LogWarning(
                        $"{nameof(BoosterHudController)} on '{name}' cannot open the add-booster offer because no {nameof(AddBoosterPanelController)} is assigned.",
                        this);
                    return;
                }

                addBoosterPanelController.Open(type);
                return;
            }

            if (!boosterController.CanActivateBooster(type))
            {
                return;
            }

            if (boosterController.TryActivateBooster(type) &&
                boosterController.ActiveBooster == type)
            {
                boosterUnlockTutorialController?.NotifyBoosterActivationAccepted(type);
            }
        }

        private bool CanProcessBoosterActivationClick()
        {
            return boosterController != null &&
                   levelSessionController != null &&
                   levelSessionController.IsPlaying &&
                   !boosterController.IsBoosterActive;
        }

        private void RegisterRuntimeEvents()
        {
            if (boosterController != null)
            {
                boosterController.BoosterActivated -= HandleBoosterActivated;
                boosterController.BoosterActivated += HandleBoosterActivated;
                boosterController.BoosterCompleted -= HandleBoosterCompleted;
                boosterController.BoosterCompleted += HandleBoosterCompleted;
                boosterController.BoosterCancelled -= HandleBoosterFinished;
                boosterController.BoosterCancelled += HandleBoosterFinished;
                boosterController.BoosterStateChanged -= HandleBoosterStateChanged;
                boosterController.BoosterStateChanged += HandleBoosterStateChanged;
                boosterController.BoosterAvailabilityChanged -= RefreshHud;
                boosterController.BoosterAvailabilityChanged += RefreshHud;
            }

            if (levelSessionController != null)
            {
                levelSessionController.GameplayStateChanged -= HandleGameplayStateChanged;
                levelSessionController.GameplayStateChanged += HandleGameplayStateChanged;
                levelSessionController.DisplayedLevelNumberChanged -=
                    HandleDisplayedLevelNumberChanged;
                levelSessionController.DisplayedLevelNumberChanged +=
                    HandleDisplayedLevelNumberChanged;
            }

            saveManager = SaveManager.Instance;
            if (saveManager == null)
            {
                return;
            }

            saveManager.OnSaveLoaded -= HandleSaveLoaded;
            saveManager.OnSaveLoaded += HandleSaveLoaded;
            saveManager.BoosterAmountChanged -= HandleBoosterAmountChanged;
            saveManager.BoosterAmountChanged += HandleBoosterAmountChanged;
        }

        private void UnregisterRuntimeEvents()
        {
            if (boosterController != null)
            {
                boosterController.BoosterActivated -= HandleBoosterActivated;
                boosterController.BoosterCompleted -= HandleBoosterCompleted;
                boosterController.BoosterCancelled -= HandleBoosterFinished;
                boosterController.BoosterStateChanged -= HandleBoosterStateChanged;
                boosterController.BoosterAvailabilityChanged -= RefreshHud;
            }

            if (levelSessionController != null)
            {
                levelSessionController.GameplayStateChanged -= HandleGameplayStateChanged;
                levelSessionController.DisplayedLevelNumberChanged -=
                    HandleDisplayedLevelNumberChanged;
            }

            if (saveManager != null)
            {
                saveManager.OnSaveLoaded -= HandleSaveLoaded;
                saveManager.BoosterAmountChanged -= HandleBoosterAmountChanged;
                saveManager = null;
            }
        }

        private void HandleBoosterActivated(BoosterType type)
        {
            ShowSelectedBoosterFx(type);
            RefreshHud();
        }

        private void HandleBoosterFinished(BoosterType type)
        {
            ClearConsumedBoosterAnalyticsContext();
            SetSelectedBoosterFxActive(false);
            RefreshHud();
        }

        private void HandleBoosterCompleted(BoosterType type)
        {
            if (hasConsumedBoosterAnalyticsContext && consumedBoosterType == type)
            {
                AnalyticsBootstrap.Instance?.Track(
                    AnalyticsEventFactory.CreateBoosterUsed(
                        consumedBoosterDisplayedLevelNumber,
                        consumedBoosterLevelNumber,
                        GetInventoryId(type)));
            }

            HandleBoosterFinished(type);
        }

        private void HandleBoosterStateChanged(BoosterType type, BoosterState state)
        {
            if (type == BoosterType.None || state == BoosterState.Idle)
            {
                SetSelectedBoosterFxActive(false);
                RefreshHud();
                return;
            }

            TryConsumeRunningBooster(type, state);
            if (boosterController == null || boosterController.ActiveBooster != type ||
                boosterController.State != state)
            {
                RefreshHud();
                return;
            }

            ShowSelectedBoosterFx(type);
            RefreshHud();
        }

        private void TryConsumeRunningBooster(BoosterType type, BoosterState state)
        {
            if (state != BoosterState.Running || boosterController == null ||
                boosterController.ActiveBooster != type)
            {
                return;
            }

            int lifecycleVersion = boosterController.ActiveLifecycleVersion;
            if (consumedBoosterLifecycleVersion == lifecycleVersion)
            {
                return;
            }

            string inventoryId = GetInventoryId(type);
            if (string.IsNullOrEmpty(inventoryId) || saveManager == null ||
                !saveManager.IsInitialized)
            {
                boosterController.CancelActiveBooster();
                return;
            }

            consumedBoosterLifecycleVersion = lifecycleVersion;
            if (saveManager.UseBooster(inventoryId))
            {
                CaptureConsumedBoosterAnalyticsContext(type);
                return;
            }

            consumedBoosterLifecycleVersion = -1;
            boosterController.CancelActiveBooster();
            Debug.LogError(
                $"{nameof(BoosterHudController)} could not consume '{inventoryId}' when {type} started running.",
                this);
        }

        private void CaptureConsumedBoosterAnalyticsContext(BoosterType type)
        {
            ClearConsumedBoosterAnalyticsContext();
            LevelAnalyticsTracker tracker = LevelAnalyticsTracker.Instance;
            if (tracker == null ||
                !tracker.TryGetLevelContext(
                    out consumedBoosterDisplayedLevelNumber,
                    out consumedBoosterLevelNumber))
            {
                return;
            }

            consumedBoosterType = type;
            hasConsumedBoosterAnalyticsContext = true;
        }

        private void ClearConsumedBoosterAnalyticsContext()
        {
            consumedBoosterDisplayedLevelNumber = 0;
            consumedBoosterLevelNumber = 0;
            consumedBoosterType = BoosterType.None;
            hasConsumedBoosterAnalyticsContext = false;
        }

        private void HandleGameplayStateChanged(GameplaySessionState state)
        {
            RefreshHud();
        }

        private void HandleDisplayedLevelNumberChanged(int displayedLevelNumber)
        {
            RefreshHud();
        }

        private void HandleSaveLoaded()
        {
            RefreshHud();
        }

        private void HandleBoosterAmountChanged(string boosterId, int amount)
        {
            if (IsKnownBoosterId(boosterId))
            {
                RefreshHud();
            }
        }

        private void RefreshHud()
        {
            RefreshPresentation(BoosterType.Hand, HandBoosterId, handPresentation);
            RefreshPresentation(BoosterType.Shuffle, ShuffleBoosterId, shufflePresentation);
            RefreshPresentation(BoosterType.Ufo, UfoBoosterId, ufoPresentation);
            RefreshButtonStates();
            RefreshSelectedBoosterFx();
        }

        private void RefreshPresentation(
            BoosterType boosterType,
            string inventoryId,
            BoosterPresentation presentation)
        {
            if (presentation == null)
            {
                return;
            }

            bool unlocked = IsUnlocked(presentation) &&
                            (boosterUnlockTutorialController == null ||
                             !boosterUnlockTutorialController.ShouldPresentBoosterLocked(boosterType));
            int amount = GetBoosterAmount(inventoryId);

            if (presentation.icon != null)
            {
                presentation.icon.sprite = unlocked
                    ? presentation.unlockedIcon
                    : lockedBoosterSprite;
            }

            if (presentation.unlockLevelText != null)
            {
                presentation.unlockLevelText.text = FormatUnlockLevel(presentation.unlockLevel);
                presentation.unlockLevelText.gameObject.SetActive(!unlocked);
            }

            SetActive(presentation.valueBackground, unlocked);
            SetActive(presentation.addIndicator, unlocked && amount <= 0);

            if (presentation.countText != null)
            {
                presentation.countText.text = Mathf.Max(0, amount).ToString();
                presentation.countText.gameObject.SetActive(unlocked && amount > 0);
            }
        }

        private void RefreshButtonStates()
        {
            bool canStartNewActivation = boosterController == null ||
                                         boosterController.CanStartNewBoosterActivation;
            bool canCancelTargetingHand = boosterController != null &&
                                          boosterController.ActiveBooster == BoosterType.Hand &&
                                          boosterController.State == BoosterState.Targeting;

            SetButtonInteractable(handButton, canStartNewActivation || canCancelTargetingHand);
            SetButtonInteractable(shuffleButton, canStartNewActivation);
            SetButtonInteractable(ufoButton, canStartNewActivation);
        }

        private bool IsUnlocked(BoosterPresentation presentation)
        {
            return presentation != null &&
                   levelSessionController != null &&
                   levelSessionController.DisplayedLevelNumber >=
                   Mathf.Max(1, presentation.unlockLevel);
        }

        public bool TryGetBoosterAtUnlockLevel(int displayedLevelNumber, out BoosterType boosterType)
        {
            boosterType = BoosterType.None;
            if (handPresentation != null && displayedLevelNumber == Mathf.Max(1, handPresentation.unlockLevel))
            {
                boosterType = BoosterType.Hand;
                return true;
            }

            if (shufflePresentation != null && displayedLevelNumber == Mathf.Max(1, shufflePresentation.unlockLevel))
            {
                boosterType = BoosterType.Shuffle;
                return true;
            }

            if (ufoPresentation != null && displayedLevelNumber == Mathf.Max(1, ufoPresentation.unlockLevel))
            {
                boosterType = BoosterType.Ufo;
                return true;
            }

            return false;
        }

        public bool TryGetInventoryId(BoosterType type, out string inventoryId)
        {
            inventoryId = GetInventoryId(type);
            return !string.IsNullOrEmpty(inventoryId);
        }

        public bool TryGetTutorialPresentation(
            BoosterType type,
            out RectTransform buttonRect,
            out Image icon,
            out TMP_Text lockText,
            out RectTransform valueBackgroundRect,
            out Sprite lockedSprite,
            out Sprite unlockedSprite)
        {
            BoosterPresentation presentation = GetPresentation(type);
            Transform buttonTransform = GetButtonTransform(type);
            buttonRect = buttonTransform as RectTransform;
            icon = presentation?.icon;
            lockText = presentation?.unlockLevelText;
            valueBackgroundRect = presentation?.valueBackground != null
                ? presentation.valueBackground.transform as RectTransform
                : null;
            lockedSprite = lockedBoosterSprite;
            unlockedSprite = presentation?.unlockedIcon;
            return buttonRect != null && icon != null && lockedSprite != null && unlockedSprite != null;
        }

        public void RestoreTutorialPresentation(BoosterType type)
        {
            BoosterPresentation presentation = GetPresentation(type);
            if (presentation != null)
            {
                RefreshPresentation(type, GetInventoryId(type), presentation);
            }
        }

        private void RefreshUnlockLevelTexts()
        {
            SetUnlockLevelText(handPresentation);
            SetUnlockLevelText(shufflePresentation);
            SetUnlockLevelText(ufoPresentation);
        }

        private void SetUnlockLevelText(BoosterPresentation presentation)
        {
            if (presentation?.unlockLevelText != null)
            {
                presentation.unlockLevelText.text = FormatUnlockLevel(presentation.unlockLevel);
            }
        }

        private string FormatUnlockLevel(int unlockLevel)
        {
            int normalizedUnlockLevel = Mathf.Max(1, unlockLevel);
            return string.IsNullOrWhiteSpace(localizedUnlockLevelPrefix)
                ? normalizedUnlockLevel.ToString()
                : $"{localizedUnlockLevelPrefix} {normalizedUnlockLevel}";
        }

        private int GetBoosterAmount(string inventoryId)
        {
            return saveManager != null && saveManager.IsInitialized
                ? saveManager.GetBoosterAmount(inventoryId)
                : 0;
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private void RefreshSelectedBoosterFx()
        {
            if (boosterController == null || !boosterController.IsBoosterActive)
            {
                SetSelectedBoosterFxActive(false);
                return;
            }

            ShowSelectedBoosterFx(boosterController.ActiveBooster);
        }

        private void ShowSelectedBoosterFx(BoosterType type)
        {
            Transform parent = GetButtonTransform(type);
            BoosterPresentation presentation = GetPresentation(type);
            if (selectedBoosterFx == null || parent == null || !IsUnlocked(presentation))
            {
                SetSelectedBoosterFxActive(false);
                return;
            }

            Transform fxTransform = selectedBoosterFx.transform;
            fxTransform.SetParent(parent, false);
            fxTransform.SetAsFirstSibling();
            fxTransform.localPosition = selectedFxLocalPosition;
            fxTransform.localRotation = Quaternion.Euler(selectedFxLocalEulerAngles);
            fxTransform.localScale = selectedFxLocalScale;
            selectedBoosterFx.SetActive(true);
        }

        private BoosterPresentation GetPresentation(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.Hand:
                    return handPresentation;
                case BoosterType.Shuffle:
                    return shufflePresentation;
                case BoosterType.Ufo:
                    return ufoPresentation;
                default:
                    return null;
            }
        }

        private Transform GetButtonTransform(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.Hand:
                    return handButton != null ? handButton.transform : null;
                case BoosterType.Shuffle:
                    return shuffleButton != null ? shuffleButton.transform : null;
                case BoosterType.Ufo:
                    return ufoButton != null ? ufoButton.transform : null;
                default:
                    return null;
            }
        }

        private void SetSelectedBoosterFxActive(bool active)
        {
            SetActive(selectedBoosterFx, active);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static bool IsKnownBoosterId(string boosterId)
        {
            return string.Equals(boosterId, HandBoosterId, StringComparison.Ordinal) ||
                   string.Equals(boosterId, ShuffleBoosterId, StringComparison.Ordinal) ||
                   string.Equals(boosterId, UfoBoosterId, StringComparison.Ordinal);
        }

        private static string GetInventoryId(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.Hand:
                    return HandBoosterId;
                case BoosterType.Shuffle:
                    return ShuffleBoosterId;
                case BoosterType.Ufo:
                    return UfoBoosterId;
                default:
                    return string.Empty;
            }
        }
    }
}

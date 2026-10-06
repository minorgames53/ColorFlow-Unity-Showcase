using System;
using Game.Shared.Bootstrap;
using Game.Shared.Lives;
using Game.Shared.Save;
using Game.Shared.UI;
using Game.Shared.UI.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuTopHudController : MonoBehaviour
    {
        [SerializeField] private TMP_Text goldText;
        [SerializeField] private GameObject infiniteLivesIcon;
        [SerializeField] private TMP_Text livesValueText;
        [SerializeField] private TMP_Text livesStatusText;

        [Header("Localization")]
        [SerializeField] private LocalizedString fullLivesLabel;

        [Header("Shop")]
        [SerializeField] private TweenButton goldAddButton;
        [SerializeField] private MenuBottomNavigationController bottomNavigation;

        [Header("Settings")]
        [SerializeField] private TweenButton settingsButton;
        [SerializeField] private SettingsPanelController settingsPanelController;

        [Header("Lives Refill")]
        [SerializeField] private Button livesDisplayButton;
        [SerializeField] private TweenButton livesAddButton;
        [SerializeField] private MenuLivesRefillHost livesRefillHost;
        [SerializeField] private RectTransform livesRewardTarget;
        [SerializeField] private Canvas livesDisplayCanvas;

        private object livesPresentationOwner;
        public RectTransform LivesRewardTarget => livesRewardTarget;

        public void BringForward()
        {
            if (livesDisplayCanvas != null) livesDisplayCanvas.sortingOrder = 102;
        }

        public void SendBack()
        {
            if (livesDisplayCanvas != null) livesDisplayCanvas.sortingOrder = 1;
        }

        public void BeginLivesRewardPresentation(object owner)
        {
            livesPresentationOwner = owner;
        }

        public void EndLivesRewardPresentation(object owner)
        {
            if (!ReferenceEquals(livesPresentationOwner, owner)) return;
            livesPresentationOwner = null;
            if (isActiveAndEnabled) RefreshLives();
        }

        private SaveManager saveManager;
        private LivesService livesService;
        private bool goldSubscribed;
        private bool livesSubscribed;
        private bool fullLivesLabelSubscribed;
        private long lastDisplayedRemainingSeconds = -1;
        private string localizedFullLivesLabel = string.Empty;

        private void Awake()
        {
            if (livesStatusText != null)
            {
                localizedFullLivesLabel = livesStatusText.text;
            }
        }

        private void OnEnable()
        {
            BindLocalization();
            RegisterButtons();
            TryBindServices();
            RefreshLivesInteraction();
        }

        private void Update()
        {
            if (!goldSubscribed || !livesSubscribed)
            {
                TryBindServices();
            }

            RefreshCountdownIfNeeded();
        }

        private void OnDisable()
        {
            SendBack();
            livesPresentationOwner = null;
            UnregisterButtons();
            UnbindServices();
            UnbindLocalization();
        }

        private void BindLocalization()
        {
            if (fullLivesLabelSubscribed || fullLivesLabel == null || fullLivesLabel.IsEmpty)
            {
                return;
            }

            fullLivesLabel.StringChanged += HandleFullLivesLabelChanged;
            fullLivesLabelSubscribed = true;
        }

        private void UnbindLocalization()
        {
            if (!fullLivesLabelSubscribed)
            {
                return;
            }

            fullLivesLabel.StringChanged -= HandleFullLivesLabelChanged;
            fullLivesLabelSubscribed = false;
        }

        private void HandleFullLivesLabelChanged(string localizedText)
        {
            localizedFullLivesLabel = localizedText;

            if (livesService != null &&
                !livesService.HasInfiniteLives &&
                livesService.IsFull &&
                livesStatusText != null)
            {
                livesStatusText.text = localizedFullLivesLabel;
            }
        }

        private void RegisterButtons()
        {
            if (goldAddButton != null)
            {
                goldAddButton.onClick.RemoveListener(HandleGoldAddClicked);
                goldAddButton.onClick.AddListener(HandleGoldAddClicked);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(HandleSettingsClicked);
                settingsButton.onClick.AddListener(HandleSettingsClicked);
            }

            if (livesDisplayButton != null)
            {
                livesDisplayButton.onClick.RemoveListener(HandleLivesRefillClicked);
                livesDisplayButton.onClick.AddListener(HandleLivesRefillClicked);
            }

            if (livesAddButton != null)
            {
                livesAddButton.onClick.RemoveListener(HandleLivesRefillClicked);
                livesAddButton.onClick.AddListener(HandleLivesRefillClicked);
            }
        }

        private void UnregisterButtons()
        {
            if (goldAddButton != null)
            {
                goldAddButton.onClick.RemoveListener(HandleGoldAddClicked);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(HandleSettingsClicked);
            }

            if (livesDisplayButton != null)
            {
                livesDisplayButton.onClick.RemoveListener(HandleLivesRefillClicked);
            }

            if (livesAddButton != null)
            {
                livesAddButton.onClick.RemoveListener(HandleLivesRefillClicked);
            }
        }

        private void HandleGoldAddClicked()
        {
            if (bottomNavigation == null)
            {
                Debug.LogWarning(
                    $"{nameof(MenuTopHudController)} cannot select Shop because no {nameof(MenuBottomNavigationController)} is assigned.",
                    this);
                return;
            }

            bottomNavigation.SelectShop();
        }

        private void HandleSettingsClicked()
        {
            if (settingsPanelController == null)
            {
                Debug.LogWarning(
                    $"{nameof(MenuTopHudController)} cannot open Settings because no {nameof(SettingsPanelController)} is assigned.",
                    this);
                return;
            }

            settingsPanelController.Open();
        }

        private void HandleLivesRefillClicked()
        {
            if (!CanOpenLivesRefill())
            {
                return;
            }

            if (livesRefillHost == null)
            {
                Debug.LogWarning(
                    $"{nameof(MenuTopHudController)} cannot open Life Panel because no {nameof(MenuLivesRefillHost)} is assigned.",
                    this);
                return;
            }

            livesRefillHost.Open();
        }

        private void TryBindServices()
        {
            BindSaveManagerIfAvailable();
            BindLivesServiceIfAvailable();
        }

        private void BindSaveManagerIfAvailable()
        {
            SaveManager availableSaveManager = SaveManager.Instance;
            if (availableSaveManager == null || !availableSaveManager.IsInitialized)
            {
                return;
            }

            if (saveManager != availableSaveManager)
            {
                UnbindGold();
                saveManager = availableSaveManager;
            }

            if (!goldSubscribed)
            {
                saveManager.GoldChanged += HandleGoldChanged;
                goldSubscribed = true;
            }

            SetGold(saveManager.Gold);
        }

        private void BindLivesServiceIfAvailable()
        {
            LivesService availableLivesService = SharedSystemsBootstrap.Instance?.LivesService;
            if (availableLivesService == null || !availableLivesService.IsInitialized)
            {
                return;
            }

            if (livesService != availableLivesService)
            {
                UnbindLives();
                livesService = availableLivesService;
            }

            if (!livesSubscribed)
            {
                livesService.LivesChanged += HandleLivesChanged;
                livesService.RefillStateChanged += HandleRefillStateChanged;
                livesSubscribed = true;
            }

            RefreshLives();
        }

        private void UnbindServices()
        {
            UnbindGold();
            UnbindLives();
        }

        private void UnbindGold()
        {
            if (saveManager != null && goldSubscribed)
            {
                saveManager.GoldChanged -= HandleGoldChanged;
            }

            goldSubscribed = false;
            saveManager = null;
        }

        private void UnbindLives()
        {
            if (livesService != null && livesSubscribed)
            {
                livesService.LivesChanged -= HandleLivesChanged;
                livesService.RefillStateChanged -= HandleRefillStateChanged;
            }

            livesSubscribed = false;
            livesService = null;
            lastDisplayedRemainingSeconds = -1;
            RefreshLivesInteraction();
        }

        private void HandleGoldChanged(int gold)
        {
            SetGold(gold);
        }

        private void HandleLivesChanged(int currentLives, int maxLives)
        {
            SetLivesValue(currentLives);
            RefreshLivesStatus(currentLives, maxLives);
        }

        private void HandleRefillStateChanged()
        {
            RefreshLives();
        }

        private void RefreshLives()
        {
            if (livesService == null)
            {
                return;
            }

            RefreshLivesDisplayMode();

            int currentLives = livesService.CurrentLives;
            int maxLives = livesService.MaxLives;
            SetLivesValue(currentLives);
            RefreshLivesStatus(currentLives, maxLives);
        }

        private void RefreshLivesDisplayMode()
        {
            bool hasInfiniteLives = livesService.HasInfiniteLives;

            if (infiniteLivesIcon != null)
            {
                infiniteLivesIcon.SetActive(hasInfiniteLives);
            }

            if (livesValueText != null)
            {
                livesValueText.gameObject.SetActive(!hasInfiniteLives);
            }
        }

        private void RefreshLivesStatus(int currentLives, int maxLives)
        {
            lastDisplayedRemainingSeconds = -1;
            RefreshLivesInteraction();

            if (livesService.HasInfiniteLives)
            {
                RefreshCountdownIfNeeded();
                return;
            }

            if (currentLives >= maxLives)
            {
                if (livesStatusText != null)
                {
                    livesStatusText.text = localizedFullLivesLabel;
                }

                return;
            }

            RefreshCountdownIfNeeded();
        }

        private void RefreshLivesInteraction()
        {
            bool canOpen = CanOpenLivesRefill();

            if (livesDisplayButton != null)
            {
                livesDisplayButton.interactable = canOpen;
            }

            if (livesAddButton != null)
            {
                livesAddButton.gameObject.SetActive(canOpen);
                livesAddButton.interactable = canOpen;
            }
        }

        private bool CanOpenLivesRefill()
        {
            return livesService != null &&
                   !livesService.HasInfiniteLives &&
                   livesService.CurrentLives < livesService.MaxLives;
        }

        private void RefreshCountdownIfNeeded()
        {
            if (livesService == null || livesStatusText == null)
            {
                return;
            }

            bool hasInfiniteLives = livesService.HasInfiniteLives;
            if (!hasInfiniteLives && livesService.IsFull)
            {
                return;
            }

            long remainingSeconds;
            if (hasInfiniteLives)
            {
                DateTime? infiniteLivesEndUtc = livesService.InfiniteLivesEndUtc;
                remainingSeconds = infiniteLivesEndUtc.HasValue
                    ? Math.Max(
                        0L,
                        (long)Math.Ceiling(
                            (infiniteLivesEndUtc.Value - DateTime.UtcNow).TotalSeconds))
                    : 0L;
            }
            else
            {
                remainingSeconds = Math.Max(
                    0L,
                    (long)Math.Ceiling(livesService.RemainingRefillTime.TotalSeconds));
            }

            if (remainingSeconds == lastDisplayedRemainingSeconds)
            {
                return;
            }

            lastDisplayedRemainingSeconds = remainingSeconds;
            long minutes = remainingSeconds / 60L;
            long seconds = remainingSeconds % 60L;
            livesStatusText.SetText("{0:00}:{1:00}", minutes, seconds);
        }

        private void SetGold(int gold)
        {
            if (goldText != null)
            {
                goldText.SetText("{0}", Mathf.Max(0, gold));
            }
        }

        private void SetLivesValue(int currentLives)
        {
            // Keep the existing text until arrival, then read the authoritative model again.
            if (livesPresentationOwner != null) return;
            if (livesValueText != null)
            {
                livesValueText.SetText("{0}", Mathf.Max(0, currentLives));
            }
        }
    }
}

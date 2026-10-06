using System;
using Game.Shared.Ads.Core;
using Game.Shared.Save;
using Game.Shared.Store;
using Game.Shared.Support;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Game.Shared.UI.Settings
{
    public enum SettingsPanelContext
    {
        Menu,
        Gameplay
    }

    [DisallowMultipleComponent]
    public sealed class SettingsPanelController : MonoBehaviour
    {
        private const string PrivacyPolicyUrl =
            "https://example.com/privacy";

        [SerializeField] private PanelManager panelManager;
        [SerializeField] private UIPanel panel;
        [SerializeField] private SaveManager saveManager;
        [SerializeField] private SettingsPanelContext context;

        [Header("Animated Toggles")]
        [SerializeField] private SettingsAnimatedToggle soundToggle;
        [SerializeField] private SettingsAnimatedToggle hapticToggle;
        [SerializeField] private Sprite toggleOnSprite;
        [SerializeField] private Sprite toggleOffSprite;

        [Header("Sound")]
        [SerializeField] private Button soundButton;
        [SerializeField] private Image soundIcon;
        [SerializeField] private Sprite soundOnSprite;
        [SerializeField] private Sprite soundOffSprite;

        [Header("Haptic")]
        [SerializeField] private Button hapticButton;
        [SerializeField] private Image hapticIcon;
        [SerializeField] private Sprite hapticOnSprite;
        [SerializeField] private Sprite hapticOffSprite;

        [Header("Close")]
        [SerializeField] private Button closeButton;

        [Header("Privacy and Support")]
        [SerializeField] private Button privacyButton;
        [SerializeField] private Button reachUsButton;
        [SerializeField] private SupportMailService supportMailService;

        [Header("Restore Purchases")]
        [SerializeField] private GameObject restorePurchasesRoot;
        [SerializeField] private TweenButton restorePurchasesButton;
        [SerializeField] private string stringTable = "General";
        [SerializeField] private string restoreSucceededKey = "store_restore_succeeded";
        [SerializeField] private string nothingToRestoreKey = "store_restore_nothing";
        [SerializeField] private string restoreFailedKey = "store_restore_failed";

        [Header("Gameplay")]
        [SerializeField] private GameObject mainMenuRoot;
        [SerializeField] private Button mainMenuButton;

        private StoreManager storeManager;

        public SettingsPanelContext Context => context;
        public UIPanel Panel => panel;
        public event Action MainMenuRequested;

        private void Awake()
        {
            CacheReferences();
            ApplyContext();
        }

        private void Reset()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            ApplyContext();
            RegisterListeners();
            RegisterSaveEvents();
            TryBindStoreManager();
            RefreshView();
        }

        private void Update()
        {
            TryBindStoreManager();
            RefreshRestoreButton();
            RefreshPrivacyButton();
        }

        private void OnDisable()
        {
            UnregisterListeners();
            UnregisterSaveEvents();
            UnbindStoreManager();
        }

        private void OnDestroy()
        {
            MainMenuRequested = null;
        }

        public void Open()
        {
            CacheReferences();
            if (panelManager != null && panelManager.TryDeferOpen(panel, Open)) return;

            ApplyContext();
            RefreshView();

            if (panel == null)
            {
                Debug.LogWarning($"{nameof(SettingsPanelController)} cannot open because no {nameof(UIPanel)} is assigned.", this);
                return;
            }

            if (panelManager == null)
            {
                Debug.LogWarning($"{nameof(SettingsPanelController)} cannot open because no {nameof(PanelManager)} is assigned.", this);
                return;
            }

            if (panel.gameObject.activeInHierarchy)
            {
                return;
            }

            panelManager.OpenRoot(panel);
        }

        public void OnCloseClicked()
        {
            CacheReferences();

            if (panelManager != null && panel != null && panelManager.TryClose(panel))
            {
                return;
            }

            if (panel == null)
            {
                Debug.LogWarning($"{nameof(SettingsPanelController)} cannot close because no {nameof(UIPanel)} is assigned.", this);
                return;
            }

            Debug.LogWarning($"{nameof(SettingsPanelController)} close requested while panel was not registered with {nameof(PanelManager)}.", this);
        }

        private void RegisterListeners()
        {
            if (soundToggle == null && soundButton != null)
            {
                soundButton.onClick.RemoveListener(HandleSoundButtonClicked);
                soundButton.onClick.AddListener(HandleSoundButtonClicked);
            }

            if (hapticToggle == null && hapticButton != null)
            {
                hapticButton.onClick.RemoveListener(HandleHapticButtonClicked);
                hapticButton.onClick.AddListener(HandleHapticButtonClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
                closeButton.onClick.AddListener(OnCloseClicked);
            }

            if (restorePurchasesButton != null)
            {
                restorePurchasesButton.onClick.RemoveListener(OnRestorePurchasesClicked);
                restorePurchasesButton.onClick.AddListener(OnRestorePurchasesClicked);
            }

            if (privacyButton != null)
            {
                privacyButton.onClick.RemoveListener(HandlePrivacyClicked);
                privacyButton.onClick.AddListener(HandlePrivacyClicked);
            }

            if (reachUsButton != null)
            {
                reachUsButton.onClick.RemoveListener(HandleReachUsClicked);
                reachUsButton.onClick.AddListener(HandleReachUsClicked);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(HandleMainMenuClicked);
                mainMenuButton.onClick.AddListener(HandleMainMenuClicked);
            }
        }

        private void UnregisterListeners()
        {
            if (soundToggle == null && soundButton != null)
            {
                soundButton.onClick.RemoveListener(HandleSoundButtonClicked);
            }

            if (hapticToggle == null && hapticButton != null)
            {
                hapticButton.onClick.RemoveListener(HandleHapticButtonClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
            }

            if (restorePurchasesButton != null)
            {
                restorePurchasesButton.onClick.RemoveListener(OnRestorePurchasesClicked);
            }

            if (privacyButton != null)
            {
                privacyButton.onClick.RemoveListener(HandlePrivacyClicked);
            }

            if (reachUsButton != null)
            {
                reachUsButton.onClick.RemoveListener(HandleReachUsClicked);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(HandleMainMenuClicked);
            }
        }

        public void OnRestorePurchasesClicked()
        {
            TryBindStoreManager();
            if (storeManager == null || !storeManager.IsReady ||
                storeManager.IsPurchasing || storeManager.IsRestoringPurchases)
            {
                RefreshRestoreButton();
                return;
            }

            storeManager.RestorePurchases();
            RefreshRestoreButton();
        }

        private void HandlePrivacyClicked()
        {
            AdsService adsService = AdsService.Instance;
            if (adsService != null && adsService.IsPrivacyOptionsRequired)
            {
                RefreshPrivacyButton();
                adsService.ShowPrivacyOptions();
                return;
            }

            Application.OpenURL(PrivacyPolicyUrl);
            RefreshPrivacyButton();
        }

        private void HandleReachUsClicked()
        {
            if (supportMailService == null)
            {
                Debug.LogWarning(
                    $"{nameof(SettingsPanelController)} cannot open support mail because no {nameof(SupportMailService)} is assigned.",
                    this);
                return;
            }

            supportMailService.OpenFeedbackMail();
        }

        private void HandleMainMenuClicked()
        {
            if (context != SettingsPanelContext.Gameplay)
            {
                return;
            }

            MainMenuRequested?.Invoke();
        }

        private void RegisterSaveEvents()
        {
            if (saveManager == null)
            {
                return;
            }

            saveManager.OnSoundEnabledChanged -= HandleSoundEnabledChanged;
            saveManager.OnSoundEnabledChanged += HandleSoundEnabledChanged;
            saveManager.OnHapticEnabledChanged -= HandleHapticEnabledChanged;
            saveManager.OnHapticEnabledChanged += HandleHapticEnabledChanged;
        }

        private void UnregisterSaveEvents()
        {
            if (saveManager == null)
            {
                return;
            }

            saveManager.OnSoundEnabledChanged -= HandleSoundEnabledChanged;
            saveManager.OnHapticEnabledChanged -= HandleHapticEnabledChanged;
        }

        private void HandleSoundButtonClicked()
        {
            EnsureSaveManagerInitialized();

            if (saveManager == null)
            {
                Debug.LogError($"{nameof(SettingsPanelController)} cannot save sound setting because no {nameof(SaveManager)} is available.", this);
                return;
            }

            saveManager.SetSoundEnabled(!saveManager.SoundEnabled);
        }

        private void HandleSoundToggleChanged(bool enabled)
        {
            EnsureSaveManagerInitialized();
            if (saveManager == null)
            {
                Debug.LogError($"{nameof(SettingsPanelController)} cannot save sound setting because no {nameof(SaveManager)} is available.", this);
                return;
            }

            saveManager.SetSoundEnabled(enabled);
        }

        private void HandleHapticButtonClicked()
        {
            EnsureSaveManagerInitialized();

            if (saveManager == null)
            {
                Debug.LogError($"{nameof(SettingsPanelController)} cannot save haptic setting because no {nameof(SaveManager)} is available.", this);
                return;
            }

            saveManager.SetHapticEnabled(!saveManager.HapticEnabled);
        }

        private void HandleHapticToggleChanged(bool enabled)
        {
            EnsureSaveManagerInitialized();
            if (saveManager == null)
            {
                Debug.LogError($"{nameof(SettingsPanelController)} cannot save haptic setting because no {nameof(SaveManager)} is available.", this);
                return;
            }

            saveManager.SetHapticEnabled(enabled);
        }

        private void HandleSoundEnabledChanged(bool enabled)
        {
            UpdateSoundIcon(enabled);
            if (soundToggle != null && soundToggle.Value != enabled)
            {
                soundToggle.SetValueWithoutNotify(enabled, true);
            }
        }

        private void HandleHapticEnabledChanged(bool enabled)
        {
            UpdateHapticIcon(enabled);
            if (hapticToggle != null && hapticToggle.Value != enabled)
            {
                hapticToggle.SetValueWithoutNotify(enabled, true);
            }
        }

        private void RefreshView()
        {
            EnsureSaveManagerInitialized();

            bool soundEnabled = saveManager == null || saveManager.SoundEnabled;
            bool hapticEnabled = saveManager == null || saveManager.HapticEnabled;

            soundToggle?.Initialize(
                toggleOnSprite,
                toggleOffSprite,
                soundEnabled,
                HandleSoundToggleChanged);
            hapticToggle?.Initialize(
                toggleOnSprite,
                toggleOffSprite,
                hapticEnabled,
                HandleHapticToggleChanged);
            UpdateSoundIcon(soundEnabled);
            UpdateHapticIcon(hapticEnabled);
            ApplyContext();
            RefreshPrivacyButton();
            RefreshRestoreButton();
        }

        private void TryBindStoreManager()
        {
            StoreManager available = StoreManager.Instance;
            if (storeManager == available)
            {
                return;
            }

            UnbindStoreManager();
            storeManager = available;
            if (storeManager == null)
            {
                RefreshRestoreButton();
                return;
            }

            storeManager.StoreReady += HandleStoreReady;
            storeManager.RestoreCompleted += HandleRestoreCompleted;
            RefreshRestoreButton();
        }

        private void UnbindStoreManager()
        {
            if (storeManager == null)
            {
                return;
            }

            storeManager.StoreReady -= HandleStoreReady;
            storeManager.RestoreCompleted -= HandleRestoreCompleted;
            storeManager = null;
        }

        private void HandleStoreReady()
        {
            RefreshRestoreButton();
        }

        private void HandleRestoreCompleted(bool success, string _)
        {
            RefreshRestoreButton();
            if (!success)
            {
                ShowLocalizedMessage(restoreFailedKey);
                return;
            }

            ShowLocalizedMessage(
                storeManager != null && storeManager.LastRestoreFoundRestorablePurchase
                    ? restoreSucceededKey
                    : nothingToRestoreKey);
        }

        private void RefreshRestoreButton()
        {
            if (restorePurchasesButton == null)
            {
                return;
            }

            restorePurchasesButton.interactable =
                context == SettingsPanelContext.Menu &&
                storeManager != null &&
                storeManager.IsReady &&
                !storeManager.IsPurchasing &&
                !storeManager.IsRestoringPurchases;
        }

        private void RefreshPrivacyButton()
        {
            if (privacyButton == null)
            {
                return;
            }

            privacyButton.interactable = true;
        }

        private void ApplyContext()
        {
            bool isMenu = context == SettingsPanelContext.Menu;
            restorePurchasesRoot?.SetActive(isMenu);
            mainMenuRoot?.SetActive(!isMenu);
        }

        private void ShowLocalizedMessage(string localizationKey)
        {
            if (panelManager == null || string.IsNullOrWhiteSpace(localizationKey))
            {
                return;
            }

            string message = LocalizationSettings.StringDatabase.GetLocalizedString(
                stringTable,
                localizationKey);
            if (!string.IsNullOrWhiteSpace(message))
            {
                panelManager.ShowTemporaryMessage(message);
            }
        }

        private void UpdateSoundIcon(bool enabled)
        {
            if (soundIcon == null)
            {
                return;
            }

            soundIcon.sprite = enabled ? soundOnSprite : soundOffSprite;
        }

        private void UpdateHapticIcon(bool enabled)
        {
            if (hapticIcon == null)
            {
                return;
            }

            hapticIcon.sprite = enabled ? hapticOnSprite : hapticOffSprite;
        }

        private void CacheReferences()
        {
            if (panel == null)
            {
                panel = GetComponent<UIPanel>();
            }

            if (panelManager == null)
            {
                panelManager = GetComponentInParent<PanelManager>(true);
            }

            if (panelManager == null)
            {
                panelManager = FindFirstObjectByType<PanelManager>(FindObjectsInactive.Include);
            }

            if (saveManager == null)
            {
                saveManager = SaveManager.Instance;
            }

            if (saveManager == null)
            {
                saveManager = FindFirstObjectByType<SaveManager>(FindObjectsInactive.Include);
            }

            if (supportMailService == null)
            {
                supportMailService = GetComponent<SupportMailService>();
            }

            if (restorePurchasesRoot == null && restorePurchasesButton != null)
            {
                restorePurchasesRoot = restorePurchasesButton.gameObject;
            }

            if (mainMenuRoot == null && mainMenuButton != null)
            {
                mainMenuRoot = mainMenuButton.gameObject;
            }
        }

        private void EnsureSaveManagerInitialized()
        {
            CacheReferences();

            if (saveManager != null && !saveManager.IsInitialized)
            {
                saveManager.Initialize();
            }
        }
    }
}

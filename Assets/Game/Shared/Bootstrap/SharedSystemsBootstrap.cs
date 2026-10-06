using Game.Shared.Audio;
using System;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Ads.Core;
using Game.Shared.Haptics;
using Game.Shared.Lives;
using Game.Shared.Navigation;
using Game.Shared.Save;
using Game.Shared.Store;
using UnityEngine;

namespace Game.Shared.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class SharedSystemsBootstrap : MonoBehaviour
    {
        #region Inspector

        [Header("Save")]
        [SerializeField] private SaveManager saveManager = null;

        [Header("Audio")]
        [SerializeField] private AudioManager audioManager = null;

        [Header("Haptics")]
        [SerializeField] private HapticManager hapticManager = null;

        [Header("Ads")]
        [SerializeField] private AdsService adsService = null;
        [SerializeField] private AdsConfig adsConfig = null;

        [Header("Store")]
        [SerializeField] private StoreManager storeManager = null;

        [Header("Navigation")]
        [SerializeField] private TransitionScreenController transitionScreenController = null;
        [SerializeField] private SceneLoader sceneLoader = null;
        [SerializeField] private SceneLoadingConfig sceneLoadingConfig = null;
        [SerializeField] private bool loadInitialSceneAfterBootstrap = true;

        #endregion

        #region Singleton

        public static SharedSystemsBootstrap Instance { get; private set; }
        public LivesService LivesService { get; private set; }
        public LivesPurchaseService LivesPurchaseService { get; private set; }
        public StoreManager StoreManager => storeManager;

        #endregion

        private double bootStartedAt;
        private bool bootCompletedTracked;
        private bool bootFailedTracked;

        #region Unity Lifecycle

        private void Awake()
        {
            bootStartedAt = UnityEngine.Time.realtimeSinceStartupAsDouble;

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                ResolveServices();
                ConfigureServices();
                InitializeServices();
                StartInitialSceneIfNeeded();
            }
            catch (Exception)
            {
                TrackBootFailedIfNeeded(
                    BootFailureSteps.Initialization,
                    BootErrorCodes.InitializationFailed);
                throw;
            }
        }

        private void OnDestroy()
        {
            UnbindEvents();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            LivesService?.Tick();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                LivesService?.Refresh();
            }
        }

        #endregion

        #region Startup Flow

        private void ResolveServices()
        {
            saveManager = ResolveSaveManager();
            audioManager = ResolveAudioManager();
            hapticManager = ResolveHapticManager();
            adsService = ResolveAdsService();
            storeManager = ResolveStoreManager();
            transitionScreenController = ResolveTransitionScreenController();
            sceneLoader = ResolveSceneLoader();
        }

        private void ConfigureServices()
        {
            sceneLoader.Configure(sceneLoadingConfig, transitionScreenController);
            adsService.Configure(adsConfig);
        }

        private void InitializeServices()
        {
            saveManager.Initialize();
            LivesService = new LivesService(saveManager,
                sceneLoadingConfig != null ? sceneLoadingConfig.LivesConfig : null);
            LivesService.Initialize();
            LivesPurchaseService = new LivesPurchaseService(saveManager, LivesService);
            adsService.Initialize();
            try
            {
                storeManager.Initialize(saveManager, LivesService, adsService);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Store initialization could not start. The game will continue without IAP: {exception.Message}",
                    this);
            }
            BindEvents();
            ApplyAudioSoundState();
            ApplyHapticState();

            transitionScreenController?.Initialize();
            sceneLoader.Initialize();
        }

        private void StartInitialSceneIfNeeded()
        {
            TrackBootCompletedIfNeeded();

            if (loadInitialSceneAfterBootstrap)
            {
                SceneId initialSceneId = InitialSceneRouting.Resolve(saveManager.CurrentLevel);
                sceneLoader.LoadInitialScene(initialSceneId);
            }
        }

        private void TrackBootCompletedIfNeeded()
        {
            if (bootCompletedTracked || bootFailedTracked)
            {
                return;
            }

            bootCompletedTracked = true;
            AnalyticsBootstrap.Instance?.Track(
                AnalyticsEventFactory.CreateBootCompleted(GetBootDurationSeconds()));
        }

        private void TrackBootFailedIfNeeded(string failureStep, string errorCode)
        {
            if (bootFailedTracked || bootCompletedTracked)
            {
                return;
            }

            bootFailedTracked = true;
            AnalyticsBootstrap.Instance?.Track(
                AnalyticsEventFactory.CreateBootFailed(
                    GetBootDurationSeconds(),
                    failureStep,
                    errorCode));
        }

        private double GetBootDurationSeconds()
        {
            return Math.Max(0d, UnityEngine.Time.realtimeSinceStartupAsDouble - bootStartedAt);
        }

        #endregion

        #region Resolve Helpers

        private SaveManager ResolveSaveManager()
        {
            if (saveManager != null)
            {
                return saveManager;
            }

            SaveManager existing = SaveManager.Instance != null ? SaveManager.Instance : FindFirstObjectByType<SaveManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject(nameof(SaveManager));
            return managerObject.AddComponent<SaveManager>();
        }

        private AudioManager ResolveAudioManager()
        {
            if (audioManager != null)
            {
                return audioManager;
            }

            return AudioManager.Instance != null ? AudioManager.Instance : FindFirstObjectByType<AudioManager>();
        }

        private HapticManager ResolveHapticManager()
        {
            if (hapticManager != null)
            {
                return hapticManager;
            }

            return HapticManager.Instance != null ? HapticManager.Instance : FindFirstObjectByType<HapticManager>();
        }

        private AdsService ResolveAdsService()
        {
            if (adsService != null)
            {
                return adsService;
            }

            AdsService existing = AdsService.Instance != null
                ? AdsService.Instance
                : FindFirstObjectByType<AdsService>();
            return existing != null ? existing : gameObject.AddComponent<AdsService>();
        }

        private StoreManager ResolveStoreManager()
        {
            if (storeManager != null)
            {
                return storeManager;
            }

            StoreManager existing = StoreManager.Instance != null
                ? StoreManager.Instance
                : FindFirstObjectByType<StoreManager>();
            return existing != null ? existing : gameObject.AddComponent<StoreManager>();
        }

        private TransitionScreenController ResolveTransitionScreenController()
        {
            if (transitionScreenController != null)
            {
                return transitionScreenController;
            }

            return TransitionScreenController.Instance != null
                ? TransitionScreenController.Instance
                : FindFirstObjectByType<TransitionScreenController>();
        }

        private SceneLoader ResolveSceneLoader()
        {
            if (sceneLoader != null)
            {
                return sceneLoader;
            }

            SceneLoader existing = SceneLoader.Instance != null ? SceneLoader.Instance : FindFirstObjectByType<SceneLoader>();
            if (existing != null)
            {
                return existing;
            }

            return gameObject.AddComponent<SceneLoader>();
        }

        #endregion

        #region Event Binding

        private void BindEvents()
        {
            saveManager.OnSoundEnabledChanged += HandleSoundEnabledChanged;
            saveManager.OnHapticEnabledChanged += HandleHapticEnabledChanged;
        }

        private void UnbindEvents()
        {
            if (saveManager != null)
            {
                saveManager.OnSoundEnabledChanged -= HandleSoundEnabledChanged;
                saveManager.OnHapticEnabledChanged -= HandleHapticEnabledChanged;
            }
        }

        #endregion

        #region Audio Bridge

        private void ApplyAudioSoundState()
        {
            if (audioManager == null || saveManager == null || !saveManager.IsInitialized)
            {
                return;
            }

            audioManager.SetSoundEnabled(saveManager.SoundEnabled);
        }

        private void HandleSoundEnabledChanged(bool enabled)
        {
            if (audioManager == null)
            {
                return;
            }

            audioManager.SetSoundEnabled(enabled);
        }

        #endregion

        #region Haptic Bridge

        private void ApplyHapticState()
        {
            if (hapticManager == null || saveManager == null || !saveManager.IsInitialized)
            {
                return;
            }

            hapticManager.SetEnabled(saveManager.HapticEnabled);
        }

        private void HandleHapticEnabledChanged(bool enabled)
        {
            if (hapticManager == null)
            {
                return;
            }

            hapticManager.SetEnabled(enabled);
        }

        #endregion
    }
}

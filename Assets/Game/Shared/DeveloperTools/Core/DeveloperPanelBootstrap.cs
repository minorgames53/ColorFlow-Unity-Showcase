using System;
using System.Collections;
using System.Threading;
using Game.Integrations.RemoteConfig;
using Game.Shared.Config;
using LunarConsolePlugin;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Shared.DeveloperTools
{
    [DisallowMultipleComponent]
    public sealed class DeveloperPanelBootstrap : MonoBehaviour
    {
        [SerializeField] private DeveloperPanelRuntimeController developerPanelPrefab;
        [Tooltip("Lunar FREE component on this persistent root. Must be serialized disabled.")]
        [SerializeField] private LunarConsole lunarConsole;
        private const string CacheKey = "ColorFlow.DeveloperTools.test_devices.v1";
        private const float BootFetchTimeoutSeconds = 15f;

        private DeveloperPanelRuntimeController panelInstance;
        private static bool sessionUnlocked;
        private TestDeviceAccessSnapshot accessSnapshot;
        private string deviceHash;
        private bool bootFetchStarted;
        private bool accessReady;
        private CancellationTokenSource bootCancellation;
        private Coroutine bootRequest;
        private Coroutine cacheExpiry;

        public static DeveloperPanelBootstrap Instance { get; private set; }

        public bool IsUnlocked => Instance == this && sessionUnlocked;
        public string DeviceIdentifier { get; private set; } = string.Empty;
        public bool IsDeviceAuthorized => FeatureConfig.ExternalServicesEnabled &&
            accessReady && accessSnapshot != null &&
            accessSnapshot.ContainsHash(deviceHash, DateTime.UtcNow);
        public bool IsOpen => panelInstance != null && panelInstance.IsOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            // Also resets permission when entering Play Mode with domain reload disabled.
            sessionUnlocked = false;
            if (Instance != null)
            {
                Instance.ResetAccess();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeRetainedBootInstance()
        {
            // Awake may not run when both domain reload and scene reload are disabled.
            if (Instance != null && SceneManager.GetActiveScene().name == "Boot")
                Instance.InitializeAccessAtBoot();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            sessionUnlocked = false;
            ApplyLunarAccess();
            bool createdInBoot = gameObject.scene.name == "Boot";
            DontDestroyOnLoad(gameObject);
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            if (createdInBoot) InitializeAccessAtBoot();
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;

            if (Instance == this)
            {
                ResetAccess();
                Instance = null;
            }
        }

        private void OnEnable()
        {
            ApplyLunarAccess();
        }

        private void OnDisable()
        {
            // OnDisable in Lunar FREE destroys the native platform and unsubscribes logs.
            if (lunarConsole != null) lunarConsole.enabled = false;
        }

        private void ApplyLunarAccess()
        {
            if (lunarConsole == null) return;
            bool allowed = Application.isPlaying && Instance == this &&
                isActiveAndEnabled && IsDeviceAuthorized;
#if UNITY_EDITOR
            // Use Unity's Console in the Editor. No Editor permission bypass reaches players.
            allowed = false;
#endif
            // SetConsoleEnabled is FULL-only in Lunar 1.9.0; FREE exposes the same
            // native lifecycle through MonoBehaviour.enabled. Repeated decisions are no-ops.
            if (lunarConsole.enabled != allowed) lunarConsole.enabled = allowed;
        }

        private void InitializeAccessAtBoot()
        {
            if (bootFetchStarted) return;
            bootFetchStarted = true;
            if (!FeatureConfig.ExternalServicesEnabled)
            {
                DeviceIdentifier = string.Empty;
                deviceHash = string.Empty;
                accessSnapshot = null;
                accessReady = true;
                sessionUnlocked = false;
                ApplyLunarAccess();
                return;
            }

            DeviceIdentifier = SystemInfo.deviceUniqueIdentifier;
            deviceHash = DeviceIdentifier == SystemInfo.unsupportedIdentifier
                ? string.Empty : TestDeviceAccessSnapshot.HashUid(DeviceIdentifier);
            bootCancellation = new CancellationTokenSource();
            bootRequest = StartCoroutine(LoadAccessAtBoot(bootCancellation.Token));
        }

        private IEnumerator LoadAccessAtBoot(CancellationToken cancellation)
        {
            TestDeviceAccessSnapshot fallback = null;
            try
            {
                TestDeviceAccessSnapshot.TryReadCache(PlayerPrefs.GetString(CacheKey, string.Empty),
                    DateTime.UtcNow, out fallback);
            }
            catch (Exception)
            {
                Debug.LogWarning("Developer access cache could not be read.", this);
            }

            var fetch = FirebaseTestDevicesRemoteConfig.FetchAtBootAsync(cancellation);
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + BootFetchTimeoutSeconds;
            while (!fetch.IsCompleted && UnityEngine.Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;

            if (cancellation.IsCancellationRequested) yield break;

            TestDeviceAccessSnapshot fetched = null;
            if (fetch.IsCompleted && !fetch.IsFaulted && !fetch.IsCanceled)
            {
                var result = fetch.Result;
                if (!result.Succeeded)
                    Debug.LogWarning("Developer access: " + result.Failure, this);
                else if (!TestDeviceAccessSnapshot.TryParseRemote(result.Json, result.FetchedAtUtc,
                             DateTime.UtcNow, out fetched))
                    Debug.LogWarning("Developer access: test_devices has an invalid schema or fetch timestamp.", this);
            }
            else
            {
                bootCancellation.Cancel();
                Debug.LogWarning("Developer access: Boot fetch timed out; no retry this session.", this);
            }

            // Do not expose cached permission while a new Boot response could revoke it.
            accessSnapshot = fetched ?? (fallback != null && fallback.IsFresh(DateTime.UtcNow) ? fallback : null);
            accessReady = true;
            bootRequest = null;
            ApplyLunarAccess();
            if (fetched != null)
            {
                try
                {
                    PlayerPrefs.SetString(CacheKey, fetched.ToCacheJson());
                    PlayerPrefs.Save();
                }
                catch (Exception)
                {
                    Debug.LogWarning("Developer access cache could not be saved.", this);
                }
            }

            if (accessSnapshot != null) cacheExpiry = StartCoroutine(ExpireCachedAccess());
        }

        private IEnumerator ExpireCachedAccess()
        {
            while (accessSnapshot != null && accessSnapshot.IsFresh(DateTime.UtcNow))
            {
                double remaining = (accessSnapshot.VerifiedAtUtc + TestDeviceAccessSnapshot.MaximumAge -
                                    DateTime.UtcNow).TotalSeconds;
                yield return new WaitForSecondsRealtime((float)Math.Max(0.1, remaining));
            }
            cacheExpiry = null;
            RevokeExpiredAccess();
        }

        private void OnApplicationFocus(bool focused)
        {
            // Local expiry only. Never fetch on resume or when Settings opens.
            if (focused) RevokeExpiredAccess();
        }

        private void RevokeExpiredAccess()
        {
            if (accessSnapshot == null || accessSnapshot.IsFresh(DateTime.UtcNow)) return;
            accessSnapshot = null;
            sessionUnlocked = false;
            ApplyLunarAccess();
            panelInstance?.Close();
        }

        private void ResetAccess()
        {
            bootCancellation?.Cancel();
            bootCancellation?.Dispose();
            bootCancellation = null;
            if (bootRequest != null) StopCoroutine(bootRequest);
            if (cacheExpiry != null) StopCoroutine(cacheExpiry);
            bootRequest = null;
            cacheExpiry = null;
            accessSnapshot = null;
            accessReady = false;
            bootFetchStarted = false;
            sessionUnlocked = false;
            ApplyLunarAccess();
            DeviceIdentifier = FeatureConfig.ExternalServicesEnabled
                ? SystemInfo.deviceUniqueIdentifier : string.Empty;
            deviceHash = string.Empty;
            panelInstance?.Close();
        }

        internal void UnlockAndOpen()
        {
            if (!Application.isPlaying || Instance != this || !IsDeviceAuthorized)
            {
                return;
            }

            sessionUnlocked = true;
            Open();
        }

        public void Open()
        {
            if (!DeveloperPanelAvailability.IsAllowed)
            {
                return;
            }

            if (!EnsurePanel())
            {
                return;
            }

            panelInstance.Open();
        }

        public void Close()
        {
            panelInstance?.Close();
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        [ContextMenu("Open Developer Panel (Unlocked Session Only)")]
        private void OpenFromContextMenu()
        {
            Open();
        }

        [ContextMenu("Close Developer Panel")]
        private void CloseFromContextMenu()
        {
            Close();
        }

        private bool EnsurePanel()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return false;
            if (panelInstance != null)
            {
                return true;
            }

            if (developerPanelPrefab == null)
            {
                Debug.LogError("DeveloperPanelBootstrap cannot create the developer panel because its prefab is not assigned.", this);
                return false;
            }

            panelInstance = Instantiate(developerPanelPrefab, transform);
            panelInstance.name = developerPanelPrefab.name;
            panelInstance.Initialize();

            if (!panelInstance.HasRequiredReferences())
            {
                Debug.LogWarning("DeveloperPanelBootstrap created a developer panel with missing references.", panelInstance);
            }

            panelInstance.Close();
            return true;
        }

        private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            panelInstance?.Refresh();
        }
    }
}

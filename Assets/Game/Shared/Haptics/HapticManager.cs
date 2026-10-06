using UnityEngine;

namespace Game.Shared.Haptics
{
    [DisallowMultipleComponent]
    public sealed class HapticManager : MonoBehaviour
    {
        #region Singleton

        public static HapticManager Instance { get; private set; }

        #endregion

        #region Inspector

        [SerializeField]
        private HapticConfig config = null;

        #endregion

        #region State

        private IHapticProvider provider;
        private bool isEnabled;
        private float lastPlayRealtime = -999f;
        private HapticType lastPlayedType;
        private bool hasLastPlayedType;

        public bool IsEnabled => isEnabled;
        public string ActiveProviderName => provider == null ? "None" : provider.GetType().Name;
        public float MinimumInterval => config == null ? 0f : config.MinimumInterval;
        public bool HasLastPlayedType => hasLastPlayedType;
        public HapticType LastPlayedType => lastPlayedType;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            provider = CreateProvider();
            InitializeRuntimeState();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region Public API

        public void SetEnabled(bool enabled)
        {
            if (config == null)
            {
                isEnabled = false;
                return;
            }

            isEnabled = enabled;
        }

        public void Play()
        {
            if (config == null)
            {
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - lastPlayRealtime < config.MinimumInterval)
            {
                return;
            }

            Play(config.DefaultHapticType);
            lastPlayRealtime = now;
        }

        public void Play(HapticType type, bool ignoreCooldown = false)
        {
            if (!isEnabled || config == null || provider == null)
            {
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!ignoreCooldown && now - lastPlayRealtime < config.MinimumInterval)
            {
                return;
            }

            provider.Play(type);
            lastPlayedType = type;
            hasLastPlayedType = true;
            lastPlayRealtime = now;
        }

        #endregion

        #region Provider

        private static IHapticProvider CreateProvider()
        {
#if UNITY_EDITOR
            return new NullHapticProvider();
#elif UNITY_IOS || UNITY_ANDROID
            return new MobileHapticProvider();
#else
            return new NullHapticProvider();
#endif
        }

        #endregion

        #region Runtime State

        private void InitializeRuntimeState()
        {
            if (config == null)
            {
                isEnabled = false;
                Debug.LogError($"{nameof(HapticManager)} is missing {nameof(HapticConfig)}. Haptics will stay disabled.", this);
                return;
            }

            isEnabled = config.EnabledByDefault;
        }

        #endregion
    }
}

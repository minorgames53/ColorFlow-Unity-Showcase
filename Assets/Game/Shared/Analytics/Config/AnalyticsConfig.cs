using UnityEngine;
using UnityEngine.Serialization;

namespace Game.Shared.Analytics.Core
{
    [CreateAssetMenu(
        menuName = "Game/Analytics/Analytics Config",
        fileName = "AnalyticsConfig")]
    public sealed class AnalyticsConfig : ScriptableObject
    {
        [FormerlySerializedAs("analyticsEnabled")]
        [SerializeField] private bool collectionEnabled = true;
        [SerializeField] private bool enableDebugLogs = true;

        [SerializeField] private bool enableFirebaseAnalytics = true;

        [Header("Tenjin Attribution")]
        [SerializeField] private bool enableTenjin = true;
        [SerializeField] private string androidTenjinSdkKey = "";
        [SerializeField] private string iosTenjinSdkKey = "";
        [Tooltip("Safe [Tenjin] Unity/Lunar diagnostics. Works in release builds too; does not enable tracking or native payload logs.")]
        [SerializeField] private bool enableTenjinDebugLogs = false;

        public bool EnableTenjin => enableTenjin;
        public bool EnableTenjinDebugLogs => enableTenjinDebugLogs;
        public string AndroidTenjinSdkKey => androidTenjinSdkKey?.Trim() ?? string.Empty;
        public string IosTenjinSdkKey => iosTenjinSdkKey?.Trim() ?? string.Empty;

        public bool CollectionEnabled => collectionEnabled;
        public bool EnableDebugLogs => enableDebugLogs;

        public bool EnableFirebaseAnalytics => enableFirebaseAnalytics;
    }
}

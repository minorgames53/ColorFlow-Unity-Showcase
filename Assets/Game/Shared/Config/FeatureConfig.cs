using UnityEngine;

namespace Game.Shared.Config
{
    [CreateAssetMenu(
        fileName = "FeatureConfig",
        menuName = "Game/Shared/Feature Config")]
    public sealed class FeatureConfig : ScriptableObject
    {
        /// <summary>
        /// Disables external game services in the public showcase. Enable only after configuring private
        /// SDK credentials, native collection settings, and the intended store environment.
        /// </summary>
        public static bool ExternalServicesEnabled => false;

        [SerializeField] private bool enableShop = false;
        [SerializeField] private bool enableRemoveAds = false;
        [SerializeField] private bool enablePrivacySettings = false;
        [SerializeField] private bool enableCloudSave = false;
        [SerializeField] private bool enableAccountDeletion = false;
        [SerializeField] private bool enableNotifications = false;
        [SerializeField] private bool enableRateUs = false;

        public bool EnableShop => enableShop;
        public bool EnableRemoveAds => enableRemoveAds;
        public bool EnablePrivacySettings => enablePrivacySettings;
        public bool EnableCloudSave => enableCloudSave;
        public bool EnableAccountDeletion => enableAccountDeletion;
        public bool EnableNotifications => enableNotifications;
        public bool EnableRateUs => enableRateUs;
    }
}

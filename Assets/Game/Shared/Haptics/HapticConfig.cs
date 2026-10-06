using UnityEngine;

namespace Game.Shared.Haptics
{
    [CreateAssetMenu(
        fileName = "HapticConfig",
        menuName = "Game/Shared/Haptic Config")]
    public sealed class HapticConfig : ScriptableObject
    {
        [SerializeField]
        private HapticType defaultHapticType = HapticType.Light;

        [SerializeField, Min(0f)]
        private float minimumInterval = 0.05f;

        [SerializeField]
        private bool enabledByDefault = true;

        [SerializeField]
        private string playerPrefsKey = "haptic_enabled";

        public HapticType DefaultHapticType => defaultHapticType;
        public float MinimumInterval => minimumInterval;
        public bool EnabledByDefault => enabledByDefault;
        public string PlayerPrefsKey => playerPrefsKey;
    }
}

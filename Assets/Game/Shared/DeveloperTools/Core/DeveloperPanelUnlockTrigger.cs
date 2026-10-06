using Game.Shared.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools
{
    [DisallowMultipleComponent]
    public sealed class DeveloperPanelUnlockTrigger : MonoBehaviour
    {
        private const int RequiredTapCount = 3;

        [SerializeField] private TMP_Text uidText;
        [SerializeField] private Button triggerButton;

        private int currentTapCount;

        private void OnEnable()
        {
            currentTapCount = 0;
            string uid = string.Empty;
            if (FeatureConfig.ExternalServicesEnabled)
            {
                uid = DeveloperPanelBootstrap.Instance != null
                    ? DeveloperPanelBootstrap.Instance.DeviceIdentifier
                    : SystemInfo.deviceUniqueIdentifier;
            }
            if (uidText != null) uidText.text = $"UID: {uid}";
            if (triggerButton != null)
            {
                triggerButton.onClick.RemoveListener(RegisterTap);
                triggerButton.onClick.AddListener(RegisterTap);
            }
        }

        private void OnDisable()
        {
            if (triggerButton != null) triggerButton.onClick.RemoveListener(RegisterTap);
            currentTapCount = 0;
        }

        private void RegisterTap()
        {
            DeveloperPanelBootstrap bootstrap = DeveloperPanelBootstrap.Instance;
            if (!isActiveAndEnabled || triggerButton == null || !triggerButton.IsInteractable() ||
                bootstrap == null || !bootstrap.IsDeviceAuthorized)
            {
                currentTapCount = 0;
                return;
            }

            if (++currentTapCount < RequiredTapCount) return;

            currentTapCount = 0;
            bootstrap.UnlockAndOpen();
        }
    }
}

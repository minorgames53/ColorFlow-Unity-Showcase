using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.UI
{
    public sealed class AnalyticsProviderMiniCardView : MonoBehaviour
    {
        private static readonly Color UnavailableColor = new Color(0.42f, 0.45f, 0.49f, 1f);

        [SerializeField] private Image statusDot;
        [SerializeField] private TMP_Text providerNameText;
        [SerializeField] private TMP_Text summaryText;

        public void SetStatus(
            string providerName,
            bool isReady,
            bool isCollectionEnabled,
            int pendingQueue,
            string stateText,
            Color statusColor)
        {
            if (statusDot != null)
            {
                statusDot.color = statusColor;
            }

            if (providerNameText != null)
            {
                providerNameText.text = string.IsNullOrWhiteSpace(providerName) ? "Unknown" : providerName;
            }

            if (summaryText != null)
            {
                string resolvedState = string.IsNullOrWhiteSpace(stateText)
                    ? isReady ? "Ready" : "Unavailable"
                    : stateText;
                string collectionText = isCollectionEnabled ? "Enabled" : "Disabled";
                summaryText.text = $"{resolvedState} • {collectionText} • Queue {Mathf.Max(0, pendingQueue)}";
            }
        }

        public void SetUnavailable(string providerName)
        {
            if (statusDot != null)
            {
                statusDot.color = UnavailableColor;
            }

            if (providerNameText != null)
            {
                providerNameText.text = string.IsNullOrWhiteSpace(providerName) ? "Unknown" : providerName;
            }

            if (summaryText != null)
            {
                summaryText.text = "Unavailable";
            }
        }
    }
}

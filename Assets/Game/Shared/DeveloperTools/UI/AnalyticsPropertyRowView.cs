using TMPro;
using UnityEngine;

namespace Game.Shared.DeveloperTools.UI
{
    public sealed class AnalyticsPropertyRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text valueText;

        public void Set(string label, string value)
        {
            if (labelText != null)
            {
                labelText.text = string.IsNullOrWhiteSpace(label) ? "-" : label;
            }

            if (valueText != null)
            {
                valueText.text = string.IsNullOrWhiteSpace(value) ? "-" : value;
            }
        }
    }
}

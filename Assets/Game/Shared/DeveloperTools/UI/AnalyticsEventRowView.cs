using System;
using Game.Shared.Analytics.Debugging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.UI
{
    public sealed class AnalyticsEventRowView : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private GameObject selectedVisual;
        [SerializeField] private TMP_Text sequenceText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text eventNameText;
        [SerializeField] private TMP_Text providerSummaryText;

        private AnalyticsDebugRecord boundRecord;
        private Action<AnalyticsDebugRecord> onClicked;

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClicked);
            }
        }

        public void Bind(
            AnalyticsDebugRecord record,
            bool isSelected,
            Action<AnalyticsDebugRecord> clicked)
        {
            boundRecord = record;
            onClicked = clicked;

            if (button != null)
            {
                button.onClick.RemoveListener(HandleClicked);
                button.onClick.AddListener(HandleClicked);
            }

            if (sequenceText != null)
            {
                sequenceText.text = record == null ? "#---" : $"#{record.SequenceNumber:000}";
            }

            if (timeText != null)
            {
                timeText.text = record == null ? "--:--:--" : record.OccurredAtUtc.ToLocalTime().ToString("HH:mm:ss");
            }

            if (eventNameText != null)
            {
                eventNameText.text = sequenceText == null && timeText == null
                    ? BuildEventSummary(record)
                    : GetEventName(record);
            }

            if (providerSummaryText != null)
            {
                providerSummaryText.text = BuildProviderSummary(record);
            }

            SetSelected(isSelected);
        }

        public void SetSelected(bool selected)
        {
            if (selectedVisual != null)
            {
                selectedVisual.SetActive(selected);
            }
        }

        private void HandleClicked()
        {
            if (boundRecord != null)
            {
                onClicked?.Invoke(boundRecord);
            }
        }

        private static string BuildProviderSummary(AnalyticsDebugRecord record)
        {
            if (record == null)
            {
                return "FB: -";
            }

            return $"FB: {FindProviderStatus(record, "Firebase")}";
        }

        private static string BuildEventSummary(AnalyticsDebugRecord record)
        {
            if (record == null)
            {
                return "#---   --:--:--   -";
            }

            return $"#{record.SequenceNumber:000}   {record.OccurredAtUtc.ToLocalTime():HH:mm:ss}   {GetEventName(record)}";
        }

        private static string GetEventName(AnalyticsDebugRecord record)
        {
            return record == null || string.IsNullOrWhiteSpace(record.EventName)
                ? "-"
                : record.EventName;
        }

        private static string FindProviderStatus(AnalyticsDebugRecord record, string providerNamePart)
        {
            for (int i = 0; i < record.ProviderResults.Count; i++)
            {
                AnalyticsDebugProviderResult result = record.ProviderResults[i];
                if (result == null)
                {
                    continue;
                }

                if (Contains(result.ProviderDisplayName, providerNamePart)
                    || Contains(result.ProviderTypeName, providerNamePart))
                {
                    return FormatDispatchStatus(result.DispatchStatus);
                }
            }

            return "-";
        }

        private static bool Contains(string value, string part)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatDispatchStatus(AnalyticsDebugDispatchStatus status)
        {
            switch (status)
            {
                case AnalyticsDebugDispatchStatus.ForwardedToProvider:
                case AnalyticsDebugDispatchStatus.ForwardedToSdk:
                    return "Forwarded";
                case AnalyticsDebugDispatchStatus.QueuedByProvider:
                    return "Queued";
                case AnalyticsDebugDispatchStatus.ProviderException:
                    return "Exception";
                case AnalyticsDebugDispatchStatus.ProviderError:
                    return "Error";
                case AnalyticsDebugDispatchStatus.SkippedGlobalCollectionDisabled:
                case AnalyticsDebugDispatchStatus.SkippedProviderCollectionDisabled:
                case AnalyticsDebugDispatchStatus.SkippedInvalidEvent:
                case AnalyticsDebugDispatchStatus.SkippedUnsupportedEvent:
                case AnalyticsDebugDispatchStatus.SkippedProviderNotReady:
                case AnalyticsDebugDispatchStatus.SdkUnavailable:
                case AnalyticsDebugDispatchStatus.NoProviders:
                    return "Skipped";
                default:
                    return "-";
            }
        }
    }
}

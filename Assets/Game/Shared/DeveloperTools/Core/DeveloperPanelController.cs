using System;
using System.Collections.Generic;
using System.Text;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Analytics.Debugging;
using Game.Shared.DeveloperTools.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools
{
    public sealed class DeveloperPanelController : IDisposable
    {
        private static readonly Color ReadyColor = new Color(0.13f, 0.65f, 0.36f, 1f);
        private static readonly Color WarningColor = new Color(0.96f, 0.65f, 0.16f, 1f);
        private static readonly Color ErrorColor = new Color(0.85f, 0.25f, 0.25f, 1f);
        private static readonly Color UnavailableColor = new Color(0.42f, 0.45f, 0.49f, 1f);

        private readonly Action openRequested;
        private readonly Action closeRequested;
        private readonly List<AnalyticsEventRowView> eventRows = new List<AnalyticsEventRowView>();
        private readonly List<AnalyticsPropertyRowView> propertyRows = new List<AnalyticsPropertyRowView>();

        private DeveloperPanelView view;
        private AnalyticsDebugRecord selectedRecord;
        private bool isSubscribedToStore;

        public DeveloperPanelController(Action openRequested, Action closeRequested)
        {
            this.openRequested = openRequested;
            this.closeRequested = closeRequested;
        }

        public bool IsOpen => view != null
            && view.FullScreenPanel != null
            && view.FullScreenPanel.activeSelf;

        public void Initialize(DeveloperPanelView developerPanelView)
        {
            Dispose();
            view = developerPanelView;

            if (view == null)
            {
                return;
            }

            WireButton(view.CloseButton, closeRequested);
            WireButton(view.FloatingDevButton, openRequested);
            WireButton(view.AnalyticsTabButton, () => SelectTab(DeveloperPanelTab.Analytics));
            WireButton(view.CrashTabButton, () => SelectTab(DeveloperPanelTab.Crash));
            WireButton(view.SaveTabButton, () => SelectTab(DeveloperPanelTab.Save));
            WireButton(view.DeviceTabButton, () => SelectTab(DeveloperPanelTab.Device));
            WireButton(view.HapticTabButton, () => SelectTab(DeveloperPanelTab.Haptic));
            WireButton(view.RefreshStatusButton, Refresh);
            WireButton(view.SendSmokeTestButton, SendSmokeTest);
            WireButton(view.CopyAllButton, CopyAllEvents);
            WireButton(view.ClearLogButton, ClearLog);

            SelectTab(DeveloperPanelTab.Analytics);
            SetFloatingDevButtonVisible(false);
            SetPanelVisible(false);
            RefreshHeader();
        }

        public void Open()
        {
            if (view == null || !DeveloperPanelAvailability.IsAllowed)
            {
                return;
            }

            SetPanelVisible(true);
            SetFloatingDevButtonVisible(false);
            SubscribeToStore();
            Refresh();
        }

        public void Close()
        {
            if (view == null)
            {
                return;
            }

            SetPanelVisible(false);
            UnsubscribeFromStore();
        }

        public void Refresh()
        {
            if (view == null)
            {
                return;
            }

            RefreshHeader();
            RefreshProviderStatus();
            RefreshAnalyticsEvents();
            UpdateSelectedEventDetails();
            view.HapticPanelView?.Refresh();
        }

        public void SetFloatingDevButtonVisible(bool visible)
        {
            if (view?.FloatingDevButton != null)
            {
                view.FloatingDevButton.gameObject.SetActive(visible && DeveloperPanelAvailability.IsAllowed);
            }
        }

        public void Dispose()
        {
            UnsubscribeFromStore();

            if (view != null)
            {
                RemoveButtonListeners(view.CloseButton);
                RemoveButtonListeners(view.FloatingDevButton);
                RemoveButtonListeners(view.AnalyticsTabButton);
                RemoveButtonListeners(view.CrashTabButton);
                RemoveButtonListeners(view.SaveTabButton);
                RemoveButtonListeners(view.DeviceTabButton);
                RemoveButtonListeners(view.HapticTabButton);
                RemoveButtonListeners(view.RefreshStatusButton);
                RemoveButtonListeners(view.SendSmokeTestButton);
                RemoveButtonListeners(view.CopyAllButton);
                RemoveButtonListeners(view.ClearLogButton);
            }

            view = null;
            selectedRecord = null;
            eventRows.Clear();
            propertyRows.Clear();
        }

        private void SetPanelVisible(bool visible)
        {
            SetActive(view.BackgroundOverlay, visible);
            SetActive(view.EventBlocker, visible);
            SetActive(view.FullScreenPanel, visible);
        }

        private void SelectTab(DeveloperPanelTab tab)
        {
            if (view == null)
            {
                return;
            }

            SetActive(view.AnalyticsPage, tab == DeveloperPanelTab.Analytics);
            SetActive(view.CrashPage, tab == DeveloperPanelTab.Crash);
            SetActive(view.SavePage, tab == DeveloperPanelTab.Save);
            SetActive(view.DevicePage, tab == DeveloperPanelTab.Device);
            SetActive(view.HapticPage, tab == DeveloperPanelTab.Haptic);
            SetActive(view.AnalyticsTabActiveVisual, tab == DeveloperPanelTab.Analytics);
            SetActive(view.CrashTabActiveVisual, tab == DeveloperPanelTab.Crash);
            SetActive(view.SaveTabActiveVisual, tab == DeveloperPanelTab.Save);
            SetActive(view.DeviceTabActiveVisual, tab == DeveloperPanelTab.Device);
            SetActive(view.HapticTabActiveVisual, tab == DeveloperPanelTab.Haptic);

            if (tab == DeveloperPanelTab.Haptic)
            {
                view.HapticPanelView?.Refresh();
            }
        }

        private void RefreshHeader()
        {
            if (view.SceneValueText != null)
            {
                view.SceneValueText.text = SceneManager.GetActiveScene().name;
            }

            if (view.BuildValueText != null)
            {
                view.BuildValueText.text = string.IsNullOrWhiteSpace(Application.version)
                    ? "Not set"
                    : Application.version;
            }

            if (view.EnvironmentValueText != null)
            {
                view.EnvironmentValueText.text = GetEnvironmentLabel();
            }
        }

        private void RefreshProviderStatus()
        {
            IReadOnlyList<IAnalyticsProvider> providers = AnalyticsBootstrap.Instance?.Service?.Providers;

            BindProviderCard(view.FirebaseProviderCard, providers, "Firebase", "Firebase");
        }

        private static void BindProviderCard(
            AnalyticsProviderMiniCardView card,
            IReadOnlyList<IAnalyticsProvider> providers,
            string matchText,
            string fallbackName)
        {
            if (card == null)
            {
                return;
            }

            IAnalyticsProvider provider = FindProvider(providers, matchText);
            if (provider == null)
            {
                card.SetUnavailable(fallbackName);
                return;
            }

            IAnalyticsProviderDebugInfo debugInfo = provider as IAnalyticsProviderDebugInfo;
            bool isReady = SafeGetReady(provider);
            bool isCollectionEnabled = SafeGetCollectionEnabled(provider);
            int pendingQueue = SafeGetPendingQueue(debugInfo);
            AnalyticsProviderDebugState state = SafeGetDebugState(debugInfo, isReady, isCollectionEnabled);
            string providerName = SafeGetProviderDisplayName(provider, debugInfo, fallbackName);

            card.SetStatus(
                providerName,
                isReady,
                isCollectionEnabled,
                pendingQueue,
                FormatProviderState(state, isReady, isCollectionEnabled),
                GetStatusColor(state, isReady, isCollectionEnabled));
        }

        private void RefreshAnalyticsEvents()
        {
            IReadOnlyList<AnalyticsDebugRecord> records = AnalyticsDebugRecorder.Store.GetSnapshot();
            EnsureSelectedRecordExists(records);

            if (view.TotalEventsText != null)
            {
                view.TotalEventsText.text = $"Total: {records.Count}";
            }

            ClearEventRows();

            if (view.EventsListContainer == null || view.EventRowPrefab == null)
            {
                return;
            }

            for (int i = records.Count - 1; i >= 0; i--)
            {
                AnalyticsDebugRecord record = records[i];
                AnalyticsEventRowView row = UnityEngine.Object.Instantiate(
                    view.EventRowPrefab,
                    view.EventsListContainer);
                row.gameObject.SetActive(true);
                row.Bind(record, ReferenceEquals(record, selectedRecord), HandleEventRowClicked);
                eventRows.Add(row);
            }
        }

        private void EnsureSelectedRecordExists(IReadOnlyList<AnalyticsDebugRecord> records)
        {
            if (selectedRecord == null)
            {
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (ReferenceEquals(records[i], selectedRecord))
                {
                    return;
                }
            }

            selectedRecord = null;
        }

        private void HandleEventRowClicked(AnalyticsDebugRecord record)
        {
            selectedRecord = record;
            UpdateSelectedEventDetails();
            RefreshAnalyticsEvents();
        }

        private void UpdateSelectedEventDetails()
        {
            ClearPropertyRows();

            if (selectedRecord == null)
            {
                SetActive(view.EmptySelectionText == null ? null : view.EmptySelectionText.gameObject, true);
                SetText(view.SelectedEventNameValueText, "-");
                SetText(view.FirebaseResultValueText, "-");
                return;
            }

            SetActive(view.EmptySelectionText == null ? null : view.EmptySelectionText.gameObject, false);
            SetText(view.SelectedEventNameValueText, selectedRecord.EventName);

            if (view.PropertyRowsContainer != null && view.PropertyRowPrefab != null)
            {
                foreach (KeyValuePair<string, object> property in selectedRecord.Properties)
                {
                    AnalyticsPropertyRowView row = UnityEngine.Object.Instantiate(
                        view.PropertyRowPrefab,
                        view.PropertyRowsContainer);
                    row.gameObject.SetActive(true);
                    row.Set(property.Key, FormatValue(property.Value));
                    propertyRows.Add(row);
                }
            }

            SetText(view.FirebaseResultValueText, FindProviderResultText(selectedRecord, "Firebase"));
        }

        private void SendSmokeTest()
        {
            if (!DeveloperPanelAvailability.IsAllowed)
            {
                return;
            }

            AnalyticsBootstrap.Instance?.Track(
                "analytics_smoke_test",
                new Dictionary<string, object>
                {
                    { "source", "developer_panel" },
                    { "scene", SceneManager.GetActiveScene().name }
                });
        }

        private static void CopyAllEvents()
        {
            IReadOnlyList<AnalyticsDebugRecord> records = AnalyticsDebugRecorder.Store.GetSnapshot();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Total: {records.Count}");

            for (int i = records.Count - 1; i >= 0; i--)
            {
                AnalyticsDebugRecord record = records[i];
                builder.Append('#').Append(record.SequenceNumber.ToString("000"))
                    .Append(' ')
                    .Append(record.OccurredAtUtc.ToLocalTime().ToString("HH:mm:ss"))
                    .Append(' ')
                    .AppendLine(record.EventName);

                foreach (KeyValuePair<string, object> property in record.Properties)
                {
                    builder.Append("  ")
                        .Append(property.Key)
                        .Append(": ")
                        .AppendLine(FormatValue(property.Value));
                }

                for (int resultIndex = 0; resultIndex < record.ProviderResults.Count; resultIndex++)
                {
                    AnalyticsDebugProviderResult result = record.ProviderResults[resultIndex];
                    if (result == null)
                    {
                        continue;
                    }

                    builder.Append("  ")
                        .Append(result.ProviderDisplayName)
                        .Append(": ")
                        .AppendLine(FormatDispatchStatus(result.DispatchStatus));
                }
            }

            GUIUtility.systemCopyBuffer = builder.ToString();
        }

        private void ClearLog()
        {
            AnalyticsDebugRecorder.Clear();
            selectedRecord = null;
            Refresh();
        }

        private void SubscribeToStore()
        {
            if (isSubscribedToStore)
            {
                return;
            }

            AnalyticsDebugRecorder.Store.Changed += HandleStoreChanged;
            isSubscribedToStore = true;
        }

        private void UnsubscribeFromStore()
        {
            if (!isSubscribedToStore)
            {
                return;
            }

            AnalyticsDebugRecorder.Store.Changed -= HandleStoreChanged;
            isSubscribedToStore = false;
        }

        private void HandleStoreChanged()
        {
            RefreshAnalyticsEvents();
            UpdateSelectedEventDetails();
        }

        private void ClearEventRows()
        {
            for (int i = 0; i < eventRows.Count; i++)
            {
                if (eventRows[i] != null)
                {
                    UnityEngine.Object.Destroy(eventRows[i].gameObject);
                }
            }

            eventRows.Clear();
        }

        private void ClearPropertyRows()
        {
            for (int i = 0; i < propertyRows.Count; i++)
            {
                if (propertyRows[i] != null)
                {
                    UnityEngine.Object.Destroy(propertyRows[i].gameObject);
                }
            }

            propertyRows.Clear();
        }

        private static IAnalyticsProvider FindProvider(
            IReadOnlyList<IAnalyticsProvider> providers,
            string matchText)
        {
            if (providers == null)
            {
                return null;
            }

            for (int i = 0; i < providers.Count; i++)
            {
                IAnalyticsProvider provider = providers[i];
                if (provider == null)
                {
                    continue;
                }

                IAnalyticsProviderDebugInfo debugInfo = provider as IAnalyticsProviderDebugInfo;
                if (Contains(SafeGetProviderDisplayName(provider, debugInfo, null), matchText)
                    || Contains(provider.GetType().Name, matchText))
                {
                    return provider;
                }
            }

            return null;
        }

        private static string FindProviderResultText(AnalyticsDebugRecord record, string providerNamePart)
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

        private static string FormatProviderState(
            AnalyticsProviderDebugState state,
            bool isReady,
            bool isCollectionEnabled)
        {
            if (!isCollectionEnabled)
            {
                return "Disabled";
            }

            switch (state)
            {
                case AnalyticsProviderDebugState.Ready:
                case AnalyticsProviderDebugState.ForwardedToSdk:
                    return "Ready";
                case AnalyticsProviderDebugState.Initializing:
                    return "Initializing";
                case AnalyticsProviderDebugState.Queued:
                    return "Queued";
                case AnalyticsProviderDebugState.Error:
                    return "Error";
                case AnalyticsProviderDebugState.Disabled:
                    return "Disabled";
                default:
                    return isReady ? "Ready" : "Unknown";
            }
        }

        private static Color GetStatusColor(
            AnalyticsProviderDebugState state,
            bool isReady,
            bool isCollectionEnabled)
        {
            if (!isCollectionEnabled || state == AnalyticsProviderDebugState.Disabled)
            {
                return UnavailableColor;
            }

            if (state == AnalyticsProviderDebugState.Error)
            {
                return ErrorColor;
            }

            if (state == AnalyticsProviderDebugState.Initializing
                || state == AnalyticsProviderDebugState.Queued)
            {
                return WarningColor;
            }

            return isReady ? ReadyColor : UnavailableColor;
        }

        private static AnalyticsProviderDebugState SafeGetDebugState(
            IAnalyticsProviderDebugInfo debugInfo,
            bool isReady,
            bool isCollectionEnabled)
        {
            if (debugInfo != null)
            {
                try
                {
                    return debugInfo.DebugState;
                }
                catch
                {
                    return AnalyticsProviderDebugState.Unknown;
                }
            }

            if (!isCollectionEnabled)
            {
                return AnalyticsProviderDebugState.Disabled;
            }

            return isReady ? AnalyticsProviderDebugState.Ready : AnalyticsProviderDebugState.Unknown;
        }

        private static int SafeGetPendingQueue(IAnalyticsProviderDebugInfo debugInfo)
        {
            if (debugInfo == null)
            {
                return 0;
            }

            try
            {
                return Mathf.Max(0, debugInfo.PendingEventCount);
            }
            catch
            {
                return 0;
            }
        }

        private static string SafeGetProviderDisplayName(
            IAnalyticsProvider provider,
            IAnalyticsProviderDebugInfo debugInfo,
            string fallback)
        {
            if (debugInfo != null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(debugInfo.ProviderDisplayName))
                    {
                        return debugInfo.ProviderDisplayName;
                    }
                }
                catch
                {
                    return fallback ?? provider?.GetType().Name ?? "Unknown";
                }
            }

            return fallback ?? provider?.GetType().Name ?? "Unknown";
        }

        private static bool SafeGetReady(IAnalyticsProvider provider)
        {
            try
            {
                return provider != null && provider.IsReady;
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeGetCollectionEnabled(IAnalyticsProvider provider)
        {
            try
            {
                return provider != null && provider.IsCollectionEnabled;
            }
            catch
            {
                return false;
            }
        }

        private static bool Contains(string value, string part)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatValue(object value)
        {
            return value == null ? "-" : value.ToString();
        }

        private static string GetEnvironmentLabel()
        {
            if (Application.isEditor)
            {
                return "Editor";
            }

            return UnityEngine.Debug.isDebugBuild ? "Development Build" : "Release Build";
        }

        private static void SetText(TMPro.TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = string.IsNullOrWhiteSpace(value) ? "-" : value;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static void WireButton(Button button, Action action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }

        private static void RemoveButtonListeners(Button button)
        {
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
            }
        }

        private enum DeveloperPanelTab
        {
            Analytics,
            Crash,
            Save,
            Device,
            Haptic
        }
    }
}

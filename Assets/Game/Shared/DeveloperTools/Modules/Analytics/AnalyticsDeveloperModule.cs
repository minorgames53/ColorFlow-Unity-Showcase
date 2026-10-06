using System;
using System.Collections.Generic;
using System.Text;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Analytics.Debugging;
using Game.Shared.DeveloperTools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.Modules.Analytics
{
    public sealed class AnalyticsDeveloperModule : IDeveloperPanelModule
    {
        private readonly List<AnalyticsProviderStatusCardView> providerCards =
            new List<AnalyticsProviderStatusCardView>();
        private readonly List<AnalyticsEventRowView> eventRows = new List<AnalyticsEventRowView>();

        private DeveloperPanelContext context;
        private GameObject listPage;
        private GameObject detailsPage;
        private Transform providerCardsContainer;
        private Transform eventsContent;
        private AnalyticsEventDetailsView detailsView;
        private AnalyticsDebugRecord selectedRecord;
        private bool isSubscribed;
        private bool isBuilt;

        public string Id => "analytics";
        public string DisplayName => "Analytics";

        public void Build(Transform parent, DeveloperPanelContext context)
        {
            this.context = context;

            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            uiFactory.CreateVerticalLayout(
                parent.gameObject,
                0f,
                childAlignment: TextAnchor.UpperLeft
            );

            BuildListPage(parent, theme, uiFactory);
            BuildDetailsPage(parent, theme, uiFactory);
            ShowListPage();

            isBuilt = true;
            Refresh();
        }

        public void OnOpened()
        {
            SubscribeStore();
            Refresh();
        }

        public void OnClosed()
        {
            UnsubscribeStore();
        }

        public void Refresh()
        {
            if (!isBuilt)
            {
                return;
            }

            try
            {
                RefreshProviderStatus();
                RefreshEvents();
                if (detailsPage != null && detailsPage.activeSelf)
                {
                    detailsView.Show(selectedRecord);
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(
                    $"{nameof(AnalyticsDeveloperModule)} failed to refresh: {exception.Message}");
            }
        }

        private void BuildListPage(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            listPage = uiFactory.CreateUIObject("AnalyticsListPage", parent);
            uiFactory.CreateVerticalLayout(listPage, 0f);
            uiFactory.CreateLayoutElement(listPage, flexibleWidth: 1f, flexibleHeight: 1f);

            ScrollRect scrollRect = uiFactory.CreateScrollView(
                "RecentEventsScrollView",
                listPage.transform,
                new Color(0f, 0f, 0f, 0f),
                out RectTransform content
            );
            uiFactory.CreateLayoutElement(scrollRect.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
            uiFactory.CreateVerticalLayout(
                content.gameObject,
                16f,
                new RectOffset(0, 0, 2, 10)
            );

            BuildListPageTitle(content, theme, uiFactory);
            BuildMobileProviderStatusSection(content, theme, uiFactory);
            BuildMobileEventsSection(content, theme, uiFactory);
            BuildMobileActionsGrid(content, theme, uiFactory);
        }

        private void BuildDetailsPage(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            detailsPage = uiFactory.CreateUIObject("AnalyticsDetailsPage", parent);
            uiFactory.CreateVerticalLayout(detailsPage, 12f);
            uiFactory.CreateLayoutElement(detailsPage, flexibleWidth: 1f, flexibleHeight: 1f);

            RectTransform detailsHeader = uiFactory.CreateRectTransform("DetailsHeader", detailsPage.transform);
            uiFactory.CreateHorizontalLayout(
                detailsHeader.gameObject,
                10f,
                childAlignment: TextAnchor.MiddleLeft,
                childForceExpandWidth: false
            );
            uiFactory.CreateLayoutElement(detailsHeader.gameObject, preferredHeight: 64f, flexibleWidth: 1f);

            Button backButton = CreateActionButton(
                detailsHeader,
                "BackButton",
                "< Back",
                ShowListPage
            );
            uiFactory.CreateLayoutElement(backButton.gameObject, preferredWidth: 160f, preferredHeight: 64f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "Title",
                detailsHeader,
                "Event Details",
                theme.TextPrimary,
                28f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

            ScrollRect scrollRect = uiFactory.CreateScrollView(
                "DetailsScrollView",
                detailsPage.transform,
                theme.CardBackgroundSecondary,
                out RectTransform content
            );
            uiFactory.CreateLayoutElement(scrollRect.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
            detailsView = new AnalyticsEventDetailsView(content, context);

            Button copyButton = CreateActionButton(
                detailsPage.transform,
                "CopySelectedButton",
                "Copy Selected Event",
                HandleCopySelected
            );
            uiFactory.CreateLayoutElement(copyButton.gameObject, preferredHeight: 64f, flexibleWidth: 1f);
        }

        private void BuildListPageTitle(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            RectTransform header = uiFactory.CreateRectTransform("PageTitle", parent);
            uiFactory.CreateVerticalLayout(header.gameObject, 4f);
            uiFactory.CreateLayoutElement(header.gameObject, preferredHeight: 76f, flexibleWidth: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "TitleText",
                header,
                "Analytics",
                theme.TextPrimary,
                30f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 38f, flexibleWidth: 1f);

            TextMeshProUGUI description = uiFactory.CreateText(
                "DescriptionText",
                header,
                "Inspect provider status and recent analytics events.",
                theme.TextSecondary,
                19f
            );
            uiFactory.CreateLayoutElement(description.gameObject, preferredHeight: 30f, flexibleWidth: 1f);
        }

        private void BuildMobileProviderStatusSection(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            Image section = uiFactory.CreateSectionPanel(
                "ProviderStatusSection",
                parent,
                theme,
                14f,
                new RectOffset(14, 14, 14, 14)
            );
            section.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            uiFactory.CreateLayoutElement(section.gameObject, flexibleWidth: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "SectionTitle",
                section.transform,
                "Provider Status",
                theme.TextPrimary,
                24f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 34f, flexibleWidth: 1f);

            RectTransform cards = uiFactory.CreateRectTransform("ProviderCards", section.transform);
            uiFactory.CreateVerticalLayout(cards.gameObject, 14f);
            cards.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            uiFactory.CreateLayoutElement(cards.gameObject, flexibleWidth: 1f);
            providerCardsContainer = cards;
        }

        private void BuildMobileEventsSection(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            Image section = uiFactory.CreateSectionPanel(
                "RecentEventsSection",
                parent,
                theme,
                12f,
                new RectOffset(14, 14, 14, 14)
            );
            section.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            uiFactory.CreateLayoutElement(section.gameObject, flexibleWidth: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "RecentEventsHeader",
                section.transform,
                "Recent Events",
                theme.TextPrimary,
                24f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 34f, flexibleWidth: 1f);

            RectTransform content = uiFactory.CreateRectTransform("Content", section.transform);
            uiFactory.CreateVerticalLayout(content.gameObject, 10f);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            uiFactory.CreateLayoutElement(content.gameObject, flexibleWidth: 1f);
            eventsContent = content;
        }

        private void BuildMobileActionsGrid(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            Image actionsPanel = uiFactory.CreateSectionPanel(
                "ActionsRow",
                parent,
                theme,
                10f,
                new RectOffset(14, 14, 14, 14)
            );
            actionsPanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            uiFactory.CreateLayoutElement(actionsPanel.gameObject, flexibleWidth: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "SectionTitle",
                actionsPanel.transform,
                "Actions",
                theme.TextPrimary,
                24f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 34f, flexibleWidth: 1f);

            RectTransform rowOne = CreateActionGridRow(actionsPanel.transform);
            CreateActionButton(rowOne, "RefreshButton", "Refresh Status", Refresh);
            Button smokeTestButton = CreateActionButton(
                rowOne,
                "SendSmokeTestButton",
                "Send Smoke Test",
                HandleSendSmokeTest
            );
            smokeTestButton.interactable = DeveloperPanelAvailability.IsAllowed;

            RectTransform rowTwo = CreateActionGridRow(actionsPanel.transform);
            CreateActionButton(rowTwo, "CopyAllButton", "Copy All", HandleCopyAll);
            CreateActionButton(rowTwo, "ClearLogButton", "Clear Log", HandleClearLog);
        }

        private RectTransform CreateActionGridRow(Transform parent)
        {
            RectTransform row = context.UIFactory.CreateRectTransform("ActionGridRow", parent);
            context.UIFactory.CreateHorizontalLayout(
                row.gameObject,
                10f,
                childAlignment: TextAnchor.MiddleLeft,
                childForceExpandWidth: true
            );
            context.UIFactory.CreateLayoutElement(row.gameObject, preferredHeight: 66f, flexibleWidth: 1f);
            return row;
        }

        private void BuildHeader(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            RectTransform header = uiFactory.CreateRectTransform("Header", parent);
            uiFactory.CreateVerticalLayout(header.gameObject, 4f);
            uiFactory.CreateLayoutElement(header.gameObject, preferredHeight: 68f, flexibleWidth: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "TitleText",
                header,
                "Analytics",
                theme.TextPrimary,
                28f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 34f, flexibleWidth: 1f);

            TextMeshProUGUI description = uiFactory.CreateText(
                "DescriptionText",
                header,
                "Inspect local provider dispatch state and recent analytics debug records.",
                theme.TextSecondary,
                18f
            );
            uiFactory.CreateLayoutElement(description.gameObject, preferredHeight: 26f, flexibleWidth: 1f);
        }

        private void BuildProviderStatusSection(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            Image section = uiFactory.CreateSectionPanel("ProviderStatusSection", parent, theme);
            uiFactory.CreateLayoutElement(section.gameObject, preferredHeight: 178f, flexibleWidth: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "SectionTitle",
                section.transform,
                "Provider Status",
                theme.TextPrimary,
                20f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 28f, flexibleWidth: 1f);

            RectTransform row = uiFactory.CreateRectTransform("ProviderCardsRow", section.transform);
            uiFactory.CreateHorizontalLayout(
                row.gameObject,
                theme.CardSpacing,
                childForceExpandWidth: true
            );
            uiFactory.CreateLayoutElement(row.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
            providerCardsContainer = row;
        }

        private void BuildContentRow(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            RectTransform contentRow = uiFactory.CreateRectTransform("ContentRow", parent);
            uiFactory.CreateHorizontalLayout(
                contentRow.gameObject,
                theme.CardSpacing,
                childAlignment: TextAnchor.UpperLeft,
                childForceExpandWidth: true,
                childForceExpandHeight: true
            );
            uiFactory.CreateLayoutElement(contentRow.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

            BuildRecentEventsPanel(contentRow, theme, uiFactory);
            BuildDetailsPanel(contentRow, theme, uiFactory);
        }

        private void BuildRecentEventsPanel(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            Image panel = uiFactory.CreateSectionPanel("RecentEventsPanel", parent, theme);
            uiFactory.CreateLayoutElement(panel.gameObject, flexibleWidth: 0.9f, flexibleHeight: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "SectionTitle",
                panel.transform,
                "Recent Events",
                theme.TextPrimary,
                20f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 28f, flexibleWidth: 1f);

            ScrollRect scrollRect = uiFactory.CreateScrollView(
                "EventsScrollView",
                panel.transform,
                theme.CardBackgroundSecondary,
                out RectTransform content
            );
            uiFactory.CreateLayoutElement(scrollRect.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
            eventsContent = content;
        }

        private void BuildDetailsPanel(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            Image panel = uiFactory.CreateSectionPanel("EventDetailsPanel", parent, theme);
            uiFactory.CreateLayoutElement(panel.gameObject, flexibleWidth: 1.35f, flexibleHeight: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "SectionTitle",
                panel.transform,
                "Selected Event Details",
                theme.TextPrimary,
                20f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 28f, flexibleWidth: 1f);

            ScrollRect scrollRect = uiFactory.CreateScrollView(
                "DetailsScrollView",
                panel.transform,
                theme.CardBackgroundSecondary,
                out RectTransform content
            );
            uiFactory.CreateLayoutElement(scrollRect.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

            detailsView = new AnalyticsEventDetailsView(content, context);
        }

        private void BuildActionsRow(
            Transform parent,
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            RectTransform actionsRow = uiFactory.CreateRectTransform("ActionsRow", parent);
            uiFactory.CreateHorizontalLayout(
                actionsRow.gameObject,
                8f,
                childAlignment: TextAnchor.MiddleLeft,
                childForceExpandWidth: true
            );
            uiFactory.CreateLayoutElement(actionsRow.gameObject, preferredHeight: 56f, flexibleWidth: 1f);

            CreateActionButton(actionsRow, "ClearLogButton", "Clear Log", HandleClearLog);
            CreateActionButton(actionsRow, "CopySelectedButton", "Copy Selected", HandleCopySelected);
            CreateActionButton(actionsRow, "CopyAllButton", "Copy All", HandleCopyAll);

            Button smokeTestButton = CreateActionButton(
                actionsRow,
                "SendSmokeTestButton",
                "Send Smoke Test",
                HandleSendSmokeTest
            );
            smokeTestButton.interactable = DeveloperPanelAvailability.IsAllowed;

            CreateActionButton(actionsRow, "RefreshButton", "Refresh", Refresh);
        }

        private Button CreateActionButton(
            Transform parent,
            string name,
            string label,
            UnityEngine.Events.UnityAction onClick)
        {
            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            Button button = uiFactory.CreateButton(name, parent, theme.CardBackgroundSecondary);
            uiFactory.CreateLayoutElement(button.gameObject, preferredHeight: 64f, flexibleWidth: 1f);

            TextMeshProUGUI text = uiFactory.CreateText(
                "ButtonText",
                button.transform,
                label,
                theme.TextPrimary,
                19f,
                FontStyles.Bold,
                TextAlignmentOptions.Center
            );
            text.textWrappingMode = TextWrappingModes.NoWrap;
            uiFactory.StretchToParent(text.rectTransform);
            button.onClick.AddListener(onClick);

            return button;
        }

        private void RefreshProviderStatus()
        {
            ClearChildren(providerCardsContainer);
            providerCards.Clear();

            AnalyticsService service = AnalyticsBootstrap.Instance != null
                ? AnalyticsBootstrap.Instance.Service
                : null;

            if (service == null)
            {
                AnalyticsProviderStatusCardView unavailableCard =
                    new AnalyticsProviderStatusCardView(providerCardsContainer, "AnalyticsServiceCard", context);
                unavailableCard.SetMessage("Analytics service is not initialized.");
                providerCards.Add(unavailableCard);
                return;
            }

            IReadOnlyList<IAnalyticsProvider> providers = service.Providers;
            if (providers == null || providers.Count == 0)
            {
                AnalyticsProviderStatusCardView emptyCard =
                    new AnalyticsProviderStatusCardView(providerCardsContainer, "NoProvidersCard", context);
                emptyCard.SetMessage("No analytics providers are configured.");
                providerCards.Add(emptyCard);
                return;
            }

            for (int i = 0; i < providers.Count; i++)
            {
                IAnalyticsProvider provider = providers[i];
                AnalyticsProviderStatusCardView card = new AnalyticsProviderStatusCardView(
                    providerCardsContainer,
                    $"{AnalyticsDeveloperModuleFormat.GetProviderDisplayName(provider)}ProviderCard",
                    context
                );
                card.Refresh(provider);
                providerCards.Add(card);
            }
        }

        private void RefreshEvents()
        {
            IReadOnlyList<AnalyticsDebugRecord> snapshot = AnalyticsDebugRecorder.Store.GetSnapshot();
            ValidateSelection(snapshot);

            ClearChildren(eventsContent);
            eventRows.Clear();

            if (snapshot.Count == 0)
            {
                TextMeshProUGUI emptyText = context.UIFactory.CreateText(
                    "EmptyEventsText",
                    eventsContent,
                    "No analytics events recorded yet.",
                    context.Theme.TextSecondary,
                    18f,
                    FontStyles.Normal,
                    TextAlignmentOptions.Center
                );
                context.UIFactory.CreateLayoutElement(emptyText.gameObject, preferredHeight: 42f, flexibleWidth: 1f);
                return;
            }

            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                AnalyticsDebugRecord record = snapshot[i];
                AnalyticsEventRowView row = new AnalyticsEventRowView(
                    eventsContent,
                    context,
                    HandleEventSelected
                );
                row.Refresh(record, IsSelected(record));
                eventRows.Add(row);
            }
        }

        private void ValidateSelection(IReadOnlyList<AnalyticsDebugRecord> snapshot)
        {
            if (selectedRecord == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].SequenceNumber == selectedRecord.SequenceNumber)
                {
                    selectedRecord = snapshot[i];
                    return;
                }
            }

            selectedRecord = null;
        }

        private void HandleEventSelected(AnalyticsDebugRecord record)
        {
            selectedRecord = record;
            RefreshEvents();
            detailsView.Show(selectedRecord);
            ShowDetailsPage();
        }

        private void ShowListPage()
        {
            if (listPage != null)
            {
                listPage.SetActive(true);
            }

            if (detailsPage != null)
            {
                detailsPage.SetActive(false);
            }
        }

        private void ShowDetailsPage()
        {
            if (listPage != null)
            {
                listPage.SetActive(false);
            }

            if (detailsPage != null)
            {
                detailsPage.SetActive(true);
            }
        }

        private void HandleClearLog()
        {
            selectedRecord = null;
            AnalyticsDebugRecorder.Clear();
            ShowListPage();
            Refresh();
        }

        private void HandleCopySelected()
        {
            if (selectedRecord == null)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = BuildRecordText(selectedRecord);
        }

        private void HandleCopyAll()
        {
            IReadOnlyList<AnalyticsDebugRecord> snapshot = AnalyticsDebugRecorder.Store.GetSnapshot();
            if (snapshot.Count == 0)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                }

                builder.Append(BuildRecordText(snapshot[i]));
            }

            GUIUtility.systemCopyBuffer = builder.ToString();
        }

        private void HandleSendSmokeTest()
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
                }
            );
        }

        private void SubscribeStore()
        {
            if (isSubscribed)
            {
                return;
            }

            AnalyticsDebugRecorder.Store.Changed += HandleStoreChanged;
            isSubscribed = true;
        }

        private void UnsubscribeStore()
        {
            if (!isSubscribed)
            {
                return;
            }

            AnalyticsDebugRecorder.Store.Changed -= HandleStoreChanged;
            isSubscribed = false;
        }

        private void HandleStoreChanged()
        {
            Refresh();
        }

        private bool IsSelected(AnalyticsDebugRecord record)
        {
            return selectedRecord != null
                && record != null
                && selectedRecord.SequenceNumber == record.SequenceNumber;
        }

        private static string BuildRecordText(AnalyticsDebugRecord record)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Analytics Event #{record.SequenceNumber}");
            builder.AppendLine($"Name: {record.EventName}");
            builder.AppendLine($"Occurred At UTC: {record.OccurredAtUtc:yyyy-MM-ddTHH:mm:ssZ}");
            builder.AppendLine($"Realtime Since Startup: {record.RealtimeSinceStartup:0.00}");
            builder.AppendLine();
            builder.AppendLine("Properties:");

            if (record.Properties.Count == 0)
            {
                builder.AppendLine("-");
            }
            else
            {
                foreach (KeyValuePair<string, object> property in record.Properties)
                {
                    builder.AppendLine($"{property.Key} = {AnalyticsDeveloperModuleFormat.FormatValue(property.Value)}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("Provider Results:");

            if (record.ProviderResults.Count == 0)
            {
                builder.AppendLine("-");
            }
            else
            {
                for (int i = 0; i < record.ProviderResults.Count; i++)
                {
                    AnalyticsDebugProviderResult result = record.ProviderResults[i];
                    builder.AppendLine(
                        $"{result.ProviderDisplayName} = {result.DispatchStatus} / {result.ProviderState}"
                    );
                }
            }

            return builder.ToString();
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }

    internal static class AnalyticsDeveloperModuleFormat
    {
        public static string GetProviderDisplayName(IAnalyticsProvider provider)
        {
            IAnalyticsProviderDebugInfo debugInfo = provider as IAnalyticsProviderDebugInfo;
            if (debugInfo != null && !string.IsNullOrWhiteSpace(debugInfo.ProviderDisplayName))
            {
                return debugInfo.ProviderDisplayName;
            }

            return provider != null ? provider.GetType().Name : "Unknown Provider";
        }

        public static string FormatDispatchStatus(AnalyticsDebugDispatchStatus status)
        {
            switch (status)
            {
                case AnalyticsDebugDispatchStatus.ForwardedToProvider:
                    return "Forwarded To Provider";
                case AnalyticsDebugDispatchStatus.ForwardedToSdk:
                    return "Forwarded To SDK";
                case AnalyticsDebugDispatchStatus.QueuedByProvider:
                    return "Queued By Provider";
                case AnalyticsDebugDispatchStatus.SkippedGlobalCollectionDisabled:
                    return "Skipped - Global Collection Disabled";
                case AnalyticsDebugDispatchStatus.SkippedProviderCollectionDisabled:
                    return "Skipped - Provider Collection Disabled";
                case AnalyticsDebugDispatchStatus.SkippedInvalidEvent:
                    return "Skipped - Invalid Event";
                case AnalyticsDebugDispatchStatus.SkippedUnsupportedEvent:
                    return "Skipped - Unsupported Event";
                case AnalyticsDebugDispatchStatus.SkippedProviderNotReady:
                    return "Skipped - Provider Not Ready";
                case AnalyticsDebugDispatchStatus.SdkUnavailable:
                    return "SDK Unavailable";
                case AnalyticsDebugDispatchStatus.ProviderError:
                    return "Provider Error";
                case AnalyticsDebugDispatchStatus.ProviderException:
                    return "Provider Exception";
                case AnalyticsDebugDispatchStatus.NoProviders:
                    return "No Providers";
                default:
                    return status.ToString();
            }
        }

        public static string FormatProviderState(AnalyticsProviderDebugState state)
        {
            switch (state)
            {
                case AnalyticsProviderDebugState.ForwardedToSdk:
                    return "Forwarded To SDK";
                default:
                    return SplitPascalCase(state.ToString());
            }
        }

        public static string FormatBool(bool value)
        {
            return value ? "Yes" : "No";
        }

        public static string FormatValue(object value)
        {
            return value == null ? "-" : value.ToString();
        }

        public static string FormatProviderSummary(AnalyticsDebugRecord record)
        {
            if (record == null || record.ProviderResults.Count == 0)
            {
                return "No provider results";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < record.ProviderResults.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                AnalyticsDebugProviderResult result = record.ProviderResults[i];
                builder.Append(GetShortProviderName(result.ProviderDisplayName));
                builder.Append(": ");
                builder.Append(FormatShortDispatchStatus(result.DispatchStatus));
            }

            return builder.ToString();
        }

        public static Color GetProviderStateColor(
            AnalyticsProviderDebugState state,
            DeveloperPanelTheme theme)
        {
            switch (state)
            {
                case AnalyticsProviderDebugState.Ready:
                case AnalyticsProviderDebugState.ForwardedToSdk:
                    return theme.Success;
                case AnalyticsProviderDebugState.Initializing:
                case AnalyticsProviderDebugState.Queued:
                    return theme.Warning;
                case AnalyticsProviderDebugState.Error:
                    return theme.Error;
                case AnalyticsProviderDebugState.Disabled:
                    return theme.Disabled;
                default:
                    return theme.TextSecondary;
            }
        }

        private static string FormatShortDispatchStatus(AnalyticsDebugDispatchStatus status)
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
                    return "Skipped";
                default:
                    return SplitPascalCase(status.ToString());
            }
        }

        private static string GetShortProviderName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "Provider";
            }

            if (displayName.IndexOf("Firebase", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Firebase";
            }

            return displayName;
        }

        private static string SplitPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (i > 0 && char.IsUpper(character) && !char.IsWhiteSpace(value[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}

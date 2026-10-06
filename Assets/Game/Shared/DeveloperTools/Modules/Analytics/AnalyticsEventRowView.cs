using System;
using Game.Shared.Analytics.Debugging;
using Game.Shared.DeveloperTools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.Modules.Analytics
{
    public sealed class AnalyticsEventRowView
    {
        private readonly DeveloperPanelContext context;
        private readonly Action<AnalyticsDebugRecord> selected;
        private readonly Button button;
        private readonly Image background;
        private readonly TextMeshProUGUI metaText;
        private readonly TextMeshProUGUI eventNameText;
        private readonly TextMeshProUGUI summaryText;
        private AnalyticsDebugRecord record;

        public AnalyticsEventRowView(
            Transform parent,
            DeveloperPanelContext context,
            Action<AnalyticsDebugRecord> selected)
        {
            this.context = context;
            this.selected = selected;

            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            button = uiFactory.CreateButton("EventRow", parent, theme.CardBackground);
            background = button.GetComponent<Image>();
            uiFactory.CreateVerticalLayout(
                button.gameObject,
                4f,
                new RectOffset(14, 14, 12, 12)
            );
            uiFactory.CreateLayoutElement(button.gameObject, preferredHeight: 104f, flexibleWidth: 1f);

            metaText = uiFactory.CreateText(
                "MetaText",
                button.transform,
                string.Empty,
                theme.TextSecondary,
                17f,
                FontStyles.Bold
            );
            metaText.textWrappingMode = TextWrappingModes.NoWrap;
            uiFactory.CreateLayoutElement(metaText.gameObject, preferredHeight: 22f, flexibleWidth: 1f);

            eventNameText = uiFactory.CreateText(
                "EventNameText",
                button.transform,
                string.Empty,
                theme.TextPrimary,
                22f,
                FontStyles.Bold
            );
            eventNameText.textWrappingMode = TextWrappingModes.NoWrap;
            uiFactory.CreateLayoutElement(eventNameText.gameObject, preferredHeight: 30f, flexibleWidth: 1f);

            summaryText = uiFactory.CreateText(
                "ProviderSummaryText",
                button.transform,
                string.Empty,
                theme.TextSecondary,
                16f
            );
            summaryText.textWrappingMode = TextWrappingModes.NoWrap;
            uiFactory.CreateLayoutElement(summaryText.gameObject, preferredHeight: 22f, flexibleWidth: 1f);

            button.onClick.AddListener(HandleClicked);
        }

        public void Refresh(AnalyticsDebugRecord record, bool isSelected)
        {
            this.record = record;

            DeveloperPanelTheme theme = context.Theme;
            background.color = isSelected ? theme.Accent : theme.CardBackground;
            metaText.color = isSelected ? Color.black : theme.TextSecondary;
            eventNameText.color = isSelected ? Color.black : theme.TextPrimary;
            summaryText.color = isSelected ? Color.black : theme.TextSecondary;

            metaText.text = $"#{record.SequenceNumber:000}   {record.OccurredAtUtc:HH:mm:ss}";
            eventNameText.text = record.EventName;
            summaryText.text = AnalyticsDeveloperModuleFormat.FormatProviderSummary(record);
        }

        private void HandleClicked()
        {
            if (record != null)
            {
                selected?.Invoke(record);
            }
        }
    }
}

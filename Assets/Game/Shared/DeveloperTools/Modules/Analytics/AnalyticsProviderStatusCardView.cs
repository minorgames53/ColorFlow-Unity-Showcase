using Game.Shared.Analytics.Core;
using Game.Shared.Analytics.Debugging;
using Game.Shared.DeveloperTools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.Modules.Analytics
{
    public sealed class AnalyticsProviderStatusCardView
    {
        private readonly DeveloperPanelContext context;
        private readonly Image card;
        private readonly TextMeshProUGUI providerNameText;
        private readonly TextMeshProUGUI readyValueText;
        private readonly TextMeshProUGUI collectionValueText;
        private readonly TextMeshProUGUI stateValueText;
        private readonly TextMeshProUGUI pendingValueText;
        private readonly TextMeshProUGUI errorValueText;

        public AnalyticsProviderStatusCardView(
            Transform parent,
            string name,
            DeveloperPanelContext context)
        {
            this.context = context;

            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            card = uiFactory.CreateImage(name, parent, theme.CardBackgroundSecondary);
            uiFactory.CreateVerticalLayout(
                card.gameObject,
                8f,
                new RectOffset(14, 14, 14, 14)
            );
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            uiFactory.CreateLayoutElement(card.gameObject, flexibleWidth: 1f);

            providerNameText = uiFactory.CreateText(
                "ProviderNameText",
                card.transform,
                "-",
                theme.TextPrimary,
                22f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(providerNameText.gameObject, preferredHeight: 30f, flexibleWidth: 1f);

            readyValueText = CreateRow("Ready");
            collectionValueText = CreateRow("Collection Enabled");
            stateValueText = CreateRow("State");
            pendingValueText = CreateRow("Pending Queue");
            errorValueText = CreateRow("Last Error");
        }

        public void Refresh(IAnalyticsProvider provider)
        {
            DeveloperPanelTheme theme = context.Theme;
            IAnalyticsProviderDebugInfo debugInfo = provider as IAnalyticsProviderDebugInfo;
            AnalyticsProviderDebugState state = debugInfo != null
                ? debugInfo.DebugState
                : AnalyticsProviderDebugState.Unknown;

            providerNameText.text = AnalyticsDeveloperModuleFormat.GetProviderDisplayName(provider);
            readyValueText.text = provider != null
                ? AnalyticsDeveloperModuleFormat.FormatBool(provider.IsReady)
                : "No";
            collectionValueText.text = provider != null
                ? AnalyticsDeveloperModuleFormat.FormatBool(provider.IsCollectionEnabled)
                : "No";
            stateValueText.text = AnalyticsDeveloperModuleFormat.FormatProviderState(state);
            stateValueText.color = AnalyticsDeveloperModuleFormat.GetProviderStateColor(state, theme);
            pendingValueText.text = debugInfo != null ? debugInfo.PendingEventCount.ToString() : "-";

            string lastError = debugInfo != null ? debugInfo.LastError : null;
            errorValueText.text = string.IsNullOrWhiteSpace(lastError) ? "-" : lastError;
            errorValueText.color = string.IsNullOrWhiteSpace(lastError) ? theme.TextSecondary : theme.Error;
        }

        public void SetMessage(string message)
        {
            DeveloperPanelTheme theme = context.Theme;
            providerNameText.text = "Analytics";
            readyValueText.text = message;
            readyValueText.color = theme.Warning;
            collectionValueText.text = "-";
            stateValueText.text = AnalyticsDeveloperModuleFormat.FormatProviderState(AnalyticsProviderDebugState.Unknown);
            stateValueText.color = theme.TextSecondary;
            pendingValueText.text = "-";
            errorValueText.text = "-";
        }

        private TextMeshProUGUI CreateRow(string label)
        {
            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            RectTransform row = uiFactory.CreateRectTransform($"{label.Replace(" ", string.Empty)}Row", card.transform);
            uiFactory.CreateHorizontalLayout(row.gameObject, 8f);
            uiFactory.CreateLayoutElement(row.gameObject, preferredHeight: 28f, flexibleWidth: 1f);

            TextMeshProUGUI labelText = uiFactory.CreateText(
                "LabelText",
                row,
                label,
                theme.TextSecondary,
                18f,
                FontStyles.Normal
            );
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            uiFactory.CreateLayoutElement(labelText.gameObject, flexibleWidth: 0.9f, flexibleHeight: 1f);

            TextMeshProUGUI valueText = uiFactory.CreateText(
                "ValueText",
                row,
                "-",
                theme.TextPrimary,
                18f,
                FontStyles.Bold,
                TextAlignmentOptions.Right
            );
            valueText.textWrappingMode = TextWrappingModes.NoWrap;
            uiFactory.CreateLayoutElement(valueText.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

            return valueText;
        }
    }
}

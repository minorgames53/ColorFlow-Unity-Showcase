using System.Collections.Generic;
using Game.Shared.Analytics.Debugging;
using Game.Shared.DeveloperTools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.Modules.Analytics
{
    public sealed class AnalyticsEventDetailsView
    {
        private readonly Transform content;
        private readonly DeveloperPanelContext context;

        public AnalyticsEventDetailsView(
            Transform content,
            DeveloperPanelContext context)
        {
            this.content = content;
            this.context = context;
        }

        public void Show(AnalyticsDebugRecord record)
        {
            ClearContent();

            if (record == null)
            {
                TextMeshProUGUI emptyText = context.UIFactory.CreateText(
                    "EmptyDetailsText",
                    content,
                    "Select an event to inspect its properties.",
                    context.Theme.TextSecondary,
                    20f,
                    FontStyles.Normal,
                    TextAlignmentOptions.Center
                );
                context.UIFactory.CreateLayoutElement(emptyText.gameObject, preferredHeight: 72f, flexibleWidth: 1f);
                return;
            }

            AddSectionTitle("Event");
            AddKeyValue("Event Name", record.EventName);
            AddKeyValue("Sequence Number", $"#{record.SequenceNumber}");
            AddKeyValue("Occurred At UTC", record.OccurredAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            AddKeyValue("Realtime Since Startup", $"{record.RealtimeSinceStartup:0.00}");

            AddDivider();
            AddSectionTitle("Properties");

            if (record.Properties.Count == 0)
            {
                AddBodyText("-");
            }
            else
            {
                foreach (KeyValuePair<string, object> property in record.Properties)
                {
                    AddKeyValue(property.Key, AnalyticsDeveloperModuleFormat.FormatValue(property.Value));
                }
            }

            AddDivider();
            AddSectionTitle("Provider Results");

            if (record.ProviderResults.Count == 0)
            {
                AddBodyText("-");
                return;
            }

            for (int i = 0; i < record.ProviderResults.Count; i++)
            {
                AddProviderResult(record.ProviderResults[i]);
            }
        }

        private void AddProviderResult(AnalyticsDebugProviderResult result)
        {
            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            Image panel = uiFactory.CreateImage("ProviderResult", content, theme.CardBackground);
            uiFactory.CreateVerticalLayout(
                panel.gameObject,
                8f,
                new RectOffset(14, 14, 14, 14)
            );
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            uiFactory.CreateLayoutElement(panel.gameObject, flexibleWidth: 1f);

            TextMeshProUGUI title = uiFactory.CreateText(
                "ProviderNameText",
                panel.transform,
                result.ProviderDisplayName,
                theme.TextPrimary,
                22f,
                FontStyles.Bold
            );
            uiFactory.CreateLayoutElement(title.gameObject, preferredHeight: 30f, flexibleWidth: 1f);

            AddKeyValue(panel.transform, "Dispatch Status", AnalyticsDeveloperModuleFormat.FormatDispatchStatus(result.DispatchStatus));
            AddKeyValue(panel.transform, "Provider State", AnalyticsDeveloperModuleFormat.FormatProviderState(result.ProviderState));
            AddKeyValue(panel.transform, "Ready", AnalyticsDeveloperModuleFormat.FormatBool(result.IsReady));
            AddKeyValue(panel.transform, "Enabled", AnalyticsDeveloperModuleFormat.FormatBool(result.IsCollectionEnabled));
            AddKeyValue(panel.transform, "Pending Queue", result.PendingEventCount.ToString());
            AddKeyValue(panel.transform, "Detail", string.IsNullOrWhiteSpace(result.Detail) ? "-" : result.Detail);
        }

        private void AddSectionTitle(string text)
        {
            TextMeshProUGUI title = context.UIFactory.CreateText(
                "SectionTitle",
                content,
                text,
                context.Theme.TextPrimary,
                24f,
                FontStyles.Bold
            );
            context.UIFactory.CreateLayoutElement(title.gameObject, preferredHeight: 34f, flexibleWidth: 1f);
        }

        private void AddKeyValue(string label, string value)
        {
            AddKeyValue(content, label, value);
        }

        private void AddKeyValue(Transform parent, string label, string value)
        {
            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            RectTransform row = uiFactory.CreateRectTransform("DetailRow", parent);
            uiFactory.CreateVerticalLayout(row.gameObject, 2f);
            uiFactory.CreateLayoutElement(row.gameObject, preferredHeight: 76f, flexibleWidth: 1f);

            TextMeshProUGUI labelText = uiFactory.CreateText(
                "LabelText",
                row,
                label,
                theme.TextSecondary,
                17f,
                FontStyles.Bold
            );
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            uiFactory.CreateLayoutElement(labelText.gameObject, preferredHeight: 24f, flexibleWidth: 1f);

            TextMeshProUGUI valueText = uiFactory.CreateText(
                "ValueText",
                row,
                string.IsNullOrWhiteSpace(value) ? "-" : value,
                theme.TextPrimary,
                20f,
                FontStyles.Normal,
                TextAlignmentOptions.Left
            );
            valueText.textWrappingMode = TextWrappingModes.Normal;
            uiFactory.CreateLayoutElement(valueText.gameObject, preferredHeight: 46f, flexibleWidth: 1f);
        }

        private void AddBodyText(string text)
        {
            TextMeshProUGUI body = context.UIFactory.CreateText(
                "BodyText",
                content,
                text,
                context.Theme.TextSecondary,
                19f
            );
            context.UIFactory.CreateLayoutElement(body.gameObject, preferredHeight: 30f, flexibleWidth: 1f);
        }

        private void AddDivider()
        {
            context.UIFactory.CreateDivider("Divider", content, context.Theme.Border);
        }

        private void ClearContent()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(content.GetChild(i).gameObject);
            }
        }
    }
}

using Game.Shared.DeveloperTools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.Modules.Placeholder
{
    public sealed class PlaceholderDeveloperPanelModule : IDeveloperPanelModule
    {
        private readonly string id;
        private readonly string displayName;
        private readonly string message;

        public PlaceholderDeveloperPanelModule(
            string id,
            string displayName,
            string message)
        {
            this.id = id;
            this.displayName = displayName;
            this.message = message;
        }

        public string Id => id;
        public string DisplayName => displayName;

        public void Build(Transform parent, DeveloperPanelContext context)
        {
            DeveloperPanelTheme theme = context.Theme;
            DeveloperPanelUIFactory uiFactory = context.UIFactory;

            Image card = uiFactory.CreateImage("PlaceholderCard", parent, theme.CardBackground);
            uiFactory.CreateVerticalLayout(
                card.gameObject,
                theme.CardSpacing,
                new RectOffset(22, 22, 20, 20),
                TextAnchor.MiddleCenter
            );
            uiFactory.CreateLayoutElement(card.gameObject, preferredHeight: 150f, flexibleWidth: 1f);

            TextMeshProUGUI text = uiFactory.CreateText(
                "PlaceholderText",
                card.transform,
                message,
                theme.TextSecondary,
                28f,
                FontStyles.Normal,
                TextAlignmentOptions.Center
            );
            uiFactory.CreateLayoutElement(text.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
        }

        public void OnOpened()
        {
        }

        public void OnClosed()
        {
        }

        public void Refresh()
        {
        }
    }
}

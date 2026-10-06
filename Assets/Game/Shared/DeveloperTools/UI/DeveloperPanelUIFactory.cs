using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.UI
{
    public sealed class DeveloperPanelUIFactory
    {
        public GameObject CreateUIObject(string name, Transform parent = null)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));

            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            return gameObject;
        }

        public RectTransform CreateRectTransform(string name, Transform parent = null)
        {
            return CreateUIObject(name, parent).GetComponent<RectTransform>();
        }

        public Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject gameObject = CreateUIObject(name, parent);
            Image image = gameObject.AddComponent<Image>();
            image.color = color;

            return image;
        }

        public TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            string text,
            Color color,
            float fontSize,
            FontStyles fontStyle = FontStyles.Normal,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            GameObject gameObject = CreateUIObject(name, parent);
            TextMeshProUGUI textComponent = gameObject.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.color = color;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = fontStyle;
            textComponent.alignment = alignment;
            textComponent.textWrappingMode = TextWrappingModes.Normal;
            textComponent.overflowMode = TextOverflowModes.Ellipsis;
            textComponent.raycastTarget = false;

            return textComponent;
        }

        public Button CreateButton(string name, Transform parent, Color backgroundColor)
        {
            Image image = CreateImage(name, parent, backgroundColor);
            image.raycastTarget = true;

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = backgroundColor;
            colors.highlightedColor = Tint(backgroundColor, 1.15f);
            colors.pressedColor = Tint(backgroundColor, 0.88f);
            colors.selectedColor = Tint(backgroundColor, 1.05f);
            colors.disabledColor = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0.45f);
            button.colors = colors;

            return button;
        }

        public VerticalLayoutGroup CreateVerticalLayout(
            GameObject target,
            float spacing,
            RectOffset padding = null,
            TextAnchor childAlignment = TextAnchor.UpperLeft,
            bool childControlWidth = true,
            bool childControlHeight = true,
            bool childForceExpandWidth = true,
            bool childForceExpandHeight = false)
        {
            VerticalLayoutGroup layout = target.GetComponent<VerticalLayoutGroup>();

            if (layout == null)
            {
                layout = target.AddComponent<VerticalLayoutGroup>();
            }

            if (layout == null)
            {
                return null;
            }

            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childAlignment = childAlignment;
            layout.childControlWidth = childControlWidth;
            layout.childControlHeight = childControlHeight;
            layout.childForceExpandWidth = childForceExpandWidth;
            layout.childForceExpandHeight = childForceExpandHeight;

            return layout;
        }

        public HorizontalLayoutGroup CreateHorizontalLayout(
            GameObject target,
            float spacing,
            RectOffset padding = null,
            TextAnchor childAlignment = TextAnchor.MiddleLeft,
            bool childControlWidth = true,
            bool childControlHeight = true,
            bool childForceExpandWidth = false,
            bool childForceExpandHeight = false)
        {
            HorizontalLayoutGroup layout = target.GetComponent<HorizontalLayoutGroup>();

            if (layout == null)
            {
                layout = target.AddComponent<HorizontalLayoutGroup>();
            }

            if (layout == null)
            {
                return null;
            }

            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childAlignment = childAlignment;
            layout.childControlWidth = childControlWidth;
            layout.childControlHeight = childControlHeight;
            layout.childForceExpandWidth = childForceExpandWidth;
            layout.childForceExpandHeight = childForceExpandHeight;

            return layout;
        }

        public LayoutElement CreateLayoutElement(
            GameObject target,
            float preferredWidth = -1f,
            float preferredHeight = -1f,
            float flexibleWidth = -1f,
            float flexibleHeight = -1f)
        {
            LayoutElement layoutElement = target.GetComponent<LayoutElement>();

            if (layoutElement == null)
            {
                layoutElement = target.AddComponent<LayoutElement>();
            }

            if (layoutElement == null)
            {
                return null;
            }

            if (preferredWidth >= 0f)
            {
                layoutElement.preferredWidth = preferredWidth;
            }

            if (preferredHeight >= 0f)
            {
                layoutElement.preferredHeight = preferredHeight;
            }

            if (flexibleWidth >= 0f)
            {
                layoutElement.flexibleWidth = flexibleWidth;
            }

            if (flexibleHeight >= 0f)
            {
                layoutElement.flexibleHeight = flexibleHeight;
            }

            return layoutElement;
        }

        public ScrollRect CreateScrollView(
            string name,
            Transform parent,
            Color backgroundColor,
            out RectTransform content)
        {
            Image rootImage = CreateImage(name, parent, backgroundColor);
            ScrollRect scrollRect = rootImage.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = CreateUIObject("Viewport", rootImage.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            StretchToParent(viewportRect);

            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            content = CreateRectTransform("Content", viewport.transform);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = CreateVerticalLayout(
                content.gameObject,
                8f,
                new RectOffset(8, 8, 8, 8),
                TextAnchor.UpperLeft,
                true,
                true,
                true,
                false
            );
            layout.childForceExpandHeight = false;

            ContentSizeFitter contentSizeFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRect;
            scrollRect.content = content;

            return scrollRect;
        }

        public Image CreateSectionPanel(
            string name,
            Transform parent,
            DeveloperPanelTheme theme,
            float spacing = 10f,
            RectOffset padding = null)
        {
            Image panel = CreateImage(name, parent, theme.CardBackground);
            CreateVerticalLayout(
                panel.gameObject,
                spacing,
                padding ?? new RectOffset(14, 14, 12, 12),
                TextAnchor.UpperLeft,
                true,
                true,
                true,
                false
            );

            return panel;
        }

        public TextMeshProUGUI CreateStatusBadge(
            string name,
            Transform parent,
            string text,
            Color color)
        {
            Image badge = CreateImage(name, parent, new Color(color.r, color.g, color.b, 0.18f));
            CreateHorizontalLayout(
                badge.gameObject,
                0f,
                new RectOffset(8, 8, 3, 3),
                TextAnchor.MiddleCenter,
                true,
                true,
                true,
                true
            );
            CreateLayoutElement(badge.gameObject, preferredHeight: 28f);

            TextMeshProUGUI label = CreateText(
                "BadgeText",
                badge.transform,
                text,
                color,
                16f,
                FontStyles.Bold,
                TextAlignmentOptions.Center
            );
            label.textWrappingMode = TextWrappingModes.NoWrap;
            CreateLayoutElement(label.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

            return label;
        }

        public Image CreateDivider(string name, Transform parent, Color color)
        {
            Image divider = CreateImage(name, parent, color);
            CreateLayoutElement(divider.gameObject, preferredHeight: 1f, flexibleWidth: 1f);
            return divider;
        }

        public void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static Color Tint(Color color, float multiplier)
        {
            return new Color(
                Mathf.Clamp01(color.r * multiplier),
                Mathf.Clamp01(color.g * multiplier),
                Mathf.Clamp01(color.b * multiplier),
                color.a
            );
        }
    }
}

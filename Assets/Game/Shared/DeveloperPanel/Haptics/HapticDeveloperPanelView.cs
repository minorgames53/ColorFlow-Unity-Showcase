using Game.Shared.Haptics;
using Game.Shared.Save;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperPanel.Haptics
{
    [DisallowMultipleComponent]
    public sealed class HapticDeveloperPanelView : MonoBehaviour
    {
        [SerializeField] private RectTransform contentRoot;

        private Toggle hapticsEnabledToggle;
        private TMP_Text providerValueText;
        private TMP_Text minimumIntervalValueText;
        private TMP_Text lastPlayedValueText;
        private bool isBuilt;
        private bool isRefreshing;

        private static readonly Color CardBackground = Hex("FFFFFF");
        private static readonly Color SoftBackground = Hex("FAFBFD");
        private static readonly Color PrimaryText = Hex("151A21");
        private static readonly Color SecondaryText = Hex("68717D");
        private static readonly Color Border = Hex("D8DEE8");
        private static readonly Color Accent = Hex("1677FF");

        private void Awake()
        {
            BuildIfNeeded();
        }

        private void OnEnable()
        {
            BuildIfNeeded();
            Refresh();
        }

        private void OnDestroy()
        {
            if (hapticsEnabledToggle != null)
            {
                hapticsEnabledToggle.onValueChanged.RemoveListener(HandleHapticsEnabledChanged);
            }
        }

        public void Refresh()
        {
            BuildIfNeeded();

            HapticManager manager = HapticManager.Instance;
            isRefreshing = true;
            if (hapticsEnabledToggle != null)
            {
                hapticsEnabledToggle.isOn = GetHapticsEnabled(manager);
            }
            isRefreshing = false;

            SetText(providerValueText, manager == null ? "Unavailable" : manager.ActiveProviderName);
            SetText(minimumIntervalValueText, manager == null ? "-" : $"{manager.MinimumInterval:0.###}s");
            SetText(lastPlayedValueText, manager != null && manager.HasLastPlayedType ? manager.LastPlayedType.ToString() : "-");
        }

        private void BuildIfNeeded()
        {
            if (isBuilt)
            {
                return;
            }

            RectTransform root = contentRoot != null ? contentRoot : transform as RectTransform;
            if (root == null)
            {
                return;
            }

            ClearChildren(root);

            VerticalLayoutGroup rootLayout = root.gameObject.GetComponent<VerticalLayoutGroup>()
                ?? root.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.spacing = 12f;
            rootLayout.padding = new RectOffset(0, 0, 0, 0);
            rootLayout.childAlignment = TextAnchor.UpperCenter;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            BuildStatusSection(root);
            BuildTestButtonsSection(root);

            isBuilt = true;
        }

        private void BuildStatusSection(Transform parent)
        {
            GameObject section = CreateSection("HapticStatusSection", parent, 214f);
            CreateTitle("SectionTitle", section.transform, "Haptic Status", 30f, 42f);

            hapticsEnabledToggle = CreateToggleRow(section.transform, "Haptics Enabled");
            hapticsEnabledToggle.onValueChanged.AddListener(HandleHapticsEnabledChanged);
            providerValueText = CreateInfoRow(section.transform, "Active Provider", "-");
            minimumIntervalValueText = CreateInfoRow(section.transform, "Minimum Interval", "-");
            lastPlayedValueText = CreateInfoRow(section.transform, "Last Played", "-");
        }

        private void BuildTestButtonsSection(Transform parent)
        {
            GameObject section = CreateSection("HapticTestButtonsSection", parent, 0f, 1f);
            CreateTitle("SectionTitle", section.transform, "Test Haptics", 30f, 42f);

            GridLayoutGroup grid = CreateUI("ButtonsGrid", section.transform).AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(250f, 58f);
            grid.spacing = new Vector2(10f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;
            AddLayoutElement(grid.gameObject, preferredHeight: 214f, flexibleWidth: 1f);

            CreateTestButton(grid.transform, "SelectionButton", "Selection", HapticType.Selection);
            CreateTestButton(grid.transform, "LightImpactButton", "Light Impact", HapticType.Light);
            CreateTestButton(grid.transform, "MediumImpactButton", "Medium Impact", HapticType.Medium);
            CreateTestButton(grid.transform, "HeavyImpactButton", "Heavy Impact", HapticType.Heavy);
            CreateTestButton(grid.transform, "RigidImpactButton", "Rigid Impact", HapticType.Rigid);
            CreateTestButton(grid.transform, "SoftImpactButton", "Soft Impact", HapticType.Soft);
            CreateTestButton(grid.transform, "SuccessButton", "Success", HapticType.Success);
            CreateTestButton(grid.transform, "WarningButton", "Warning", HapticType.Warning);
            CreateTestButton(grid.transform, "FailureButton", "Failure", HapticType.Failure);
        }

        private void CreateTestButton(Transform parent, string name, string label, HapticType type)
        {
            Button button = CreateButton(name, parent, Accent, Color.white);
            button.onClick.AddListener(() => Play(type));
            AddLayoutElement(button.gameObject, preferredWidth: 250f, preferredHeight: 58f);

            TMP_Text text = CreateText("ButtonText", button.transform, label, 24f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch((RectTransform)text.transform, 8f, 0f, 8f, 0f);
        }

        private Toggle CreateToggleRow(Transform parent, string label)
        {
            GameObject row = CreateRow("HapticsEnabledRow", parent);
            TMP_Text labelText = CreateText("LabelText", row.transform, label, 26f, SecondaryText, TextAlignmentOptions.Left);
            AddLayoutElement(labelText.gameObject, flexibleWidth: 1f, preferredHeight: 38f);

            GameObject toggleObject = CreateUI("Toggle", row.transform);
            AddLayoutElement(toggleObject, preferredWidth: 72f, preferredHeight: 38f);
            Toggle toggle = toggleObject.AddComponent<Toggle>();

            Image background = CreateImage("Background", toggleObject.transform, SoftBackground, true);
            Stretch(background.rectTransform);
            AddOutline(background.gameObject, Border, 1.5f);

            Image checkmark = CreateImage("Checkmark", toggleObject.transform, Accent, true);
            RectTransform checkmarkRect = checkmark.rectTransform;
            checkmarkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
            checkmarkRect.sizeDelta = new Vector2(46f, 22f);
            checkmarkRect.anchoredPosition = Vector2.zero;

            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            return toggle;
        }

        private TMP_Text CreateInfoRow(Transform parent, string label, string value)
        {
            GameObject row = CreateRow(label.Replace(" ", string.Empty) + "Row", parent);
            TMP_Text labelText = CreateText("LabelText", row.transform, label, 26f, SecondaryText, TextAlignmentOptions.Left);
            AddLayoutElement(labelText.gameObject, flexibleWidth: 1f, preferredHeight: 38f);

            TMP_Text valueText = CreateText("ValueText", row.transform, value, 26f, PrimaryText, TextAlignmentOptions.Right, FontStyles.Bold);
            AddLayoutElement(valueText.gameObject, flexibleWidth: 1f, preferredHeight: 38f);
            return valueText;
        }

        private GameObject CreateRow(string name, Transform parent)
        {
            GameObject row = CreateUI(name, parent);
            AddLayoutElement(row, preferredHeight: 38f, flexibleWidth: 1f);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return row;
        }

        private GameObject CreateSection(string name, Transform parent, float preferredHeight, float flexibleHeight = 0f)
        {
            Image background = CreateImage(name, parent, CardBackground, true);
            AddOutline(background.gameObject, Border, 1.5f);
            AddLayoutElement(background.gameObject, preferredHeight: preferredHeight, flexibleWidth: 1f, flexibleHeight: flexibleHeight);

            VerticalLayoutGroup layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return background.gameObject;
        }

        private void CreateTitle(string name, Transform parent, string text, float fontSize, float height)
        {
            TMP_Text title = CreateText(name, parent, text, fontSize, PrimaryText, TextAlignmentOptions.Left, FontStyles.Bold);
            AddLayoutElement(title.gameObject, preferredHeight: height, flexibleWidth: 1f);
        }

        private void Play(HapticType type)
        {
            HapticManager.Instance?.Play(type);
            Refresh();
        }

        private void HandleHapticsEnabledChanged(bool enabled)
        {
            if (isRefreshing)
            {
                return;
            }

            SaveManager saveManager = SaveManager.Instance;
            if (saveManager != null && saveManager.IsInitialized)
            {
                saveManager.SetHapticEnabled(enabled);
            }
            else
            {
                HapticManager.Instance?.SetEnabled(enabled);
            }

            Refresh();
        }

        private static bool GetHapticsEnabled(HapticManager manager)
        {
            SaveManager saveManager = SaveManager.Instance;
            if (saveManager != null && saveManager.IsInitialized)
            {
                return saveManager.HapticEnabled;
            }

            return manager != null && manager.IsEnabled;
        }

        private static GameObject CreateUI(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget)
        {
            Image image = CreateUI(name, parent).AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string value,
            float size,
            Color color,
            TextAlignmentOptions alignment,
            FontStyles style = FontStyles.Normal)
        {
            TextMeshProUGUI text = CreateUI(name, parent).AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, Color backgroundColor, Color textColor)
        {
            Image image = CreateImage(name, parent, backgroundColor, true);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = backgroundColor;
            colors.highlightedColor = Tint(backgroundColor, 1.15f);
            colors.pressedColor = Tint(backgroundColor, 0.88f);
            colors.selectedColor = Tint(backgroundColor, 1.05f);
            colors.disabledColor = new Color(textColor.r, textColor.g, textColor.b, 0.45f);
            button.colors = colors;
            return button;
        }

        private static LayoutElement AddLayoutElement(
            GameObject target,
            float preferredWidth = -1f,
            float preferredHeight = -1f,
            float flexibleWidth = -1f,
            float flexibleHeight = -1f)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            if (preferredWidth >= 0f)
            {
                element.preferredWidth = preferredWidth;
            }

            if (preferredHeight >= 0f)
            {
                element.preferredHeight = preferredHeight;
            }

            if (flexibleWidth >= 0f)
            {
                element.flexibleWidth = flexibleWidth;
            }

            if (flexibleHeight >= 0f)
            {
                element.flexibleHeight = flexibleHeight;
            }

            return element;
        }

        private static void AddOutline(GameObject target, Color color, float distance)
        {
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
        }

        private static void Stretch(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = string.IsNullOrWhiteSpace(value) ? "-" : value;
            }
        }

        private static Color Tint(Color color, float multiplier)
        {
            return new Color(
                Mathf.Clamp01(color.r * multiplier),
                Mathf.Clamp01(color.g * multiplier),
                Mathf.Clamp01(color.b * multiplier),
                color.a);
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}

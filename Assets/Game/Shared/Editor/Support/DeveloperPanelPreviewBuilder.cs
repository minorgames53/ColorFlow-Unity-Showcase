using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class DeveloperPanelPreviewBuilder
{
    private const string ScenePath = "Assets/Game/Scenes/DeveloperPanelPreview.unity";
    private const string PrefabFolder = "Assets/Game/Shared/DeveloperTools/UI/Prefabs";
    private const string CanvasPrefabPath = PrefabFolder + "/DeveloperPanelCanvas.prefab";
    private const string EventRowPrefabPath = PrefabFolder + "/AnalyticsEventRow.prefab";
    private const string PropertyRowPrefabPath = PrefabFolder + "/AnalyticsPropertyRow.prefab";

    private static readonly Color BackgroundOverlay = new Color(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelBackground = Hex("F7F9FC");
    private static readonly Color CardBackground = Color.white;
    private static readonly Color PrimaryText = Hex("151A21");
    private static readonly Color SecondaryText = Hex("68717D");
    private static readonly Color Border = Hex("D8DEE8");
    private static readonly Color Accent = Hex("1677FF");
    private static readonly Color AccentSoft = Hex("EAF2FF");
    private static readonly Color Success = Hex("20A55D");
    private static readonly Color Warning = Hex("F5A524");
    private static readonly Color Error = Hex("D94141");

    private static Sprite roundedSprite;
    private static TMP_FontAsset font;

    public static void Build()
    {
        roundedSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/Shapes/rounded_square.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Font/Fredoka-SemiBold SDF.asset")
            ?? TMP_Settings.defaultFontAsset;

        EnsureFolder(PrefabFolder);

        Scene scene = File.Exists(ScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RemoveGeneratedRoot("DeveloperPanelPreviewRoot");
        RemoveGeneratedRoot("EventSystem");

        GameObject previewRoot = new GameObject("DeveloperPanelPreviewRoot");
        GameObject developerPanelCanvas = BuildDeveloperPanelCanvas();
        developerPanelCanvas.transform.SetParent(previewRoot.transform, false);
        Canvas.ForceUpdateCanvases();

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();

        PrefabUtility.SaveAsPrefabAsset(developerPanelCanvas, CanvasPrefabPath);

        GameObject eventRowPrefab = CreateEventRow("AnalyticsEventRow", "#012", "14:24:30", "level_completed", "FB: Forwarded   GA: Forwarded", false);
        PrefabUtility.SaveAsPrefabAsset(eventRowPrefab, EventRowPrefabPath);
        Object.DestroyImmediate(eventRowPrefab);

        GameObject propertyRowPrefab = CreatePropertyRow("AnalyticsPropertyRow", "moves_used", "18");
        PrefabUtility.SaveAsPrefabAsset(propertyRowPrefab, PropertyRowPrefabPath);
        Object.DestroyImmediate(propertyRowPrefab);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static GameObject BuildDeveloperPanelCanvas()
    {
        GameObject root = new GameObject("DeveloperPanelCanvas");

        GameObject canvasGo = CreateUI("Canvas", root.transform);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Stretch(canvasGo.GetComponent<RectTransform>());

        GameObject overlay = CreateImage("BackgroundOverlay", canvasGo.transform, BackgroundOverlay, true);
        Stretch(overlay.GetComponent<RectTransform>());

        GameObject blocker = CreateImage("EventBlocker", canvasGo.transform, new Color(1f, 1f, 1f, 0f), true);
        Stretch(blocker.GetComponent<RectTransform>());

        GameObject safeArea = CreateUI("SafeArea", canvasGo.transform);
        Stretch(safeArea.GetComponent<RectTransform>());

        BuildFloatingDevButton(safeArea.transform);
        BuildFullScreenPanel(safeArea.transform);

        return root;
    }

    private static void BuildFloatingDevButton(Transform parent)
    {
        GameObject button = CreateUI("FloatingDevButton", parent);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(72f, 72f);
        rect.anchoredPosition = new Vector2(-14f, 0f);
        Canvas floatingCanvas = button.AddComponent<Canvas>();
        floatingCanvas.overrideSorting = true;
        floatingCanvas.sortingOrder = 32001;
        button.AddComponent<GraphicRaycaster>();

        GameObject background = CreateImage("Background", button.transform, Accent, true);
        Stretch(background.GetComponent<RectTransform>());
        AddOutline(background, Hex("0D55C7"), 2f);

        GameObject text = CreateText("Text_DEV", button.transform, "DEV", 26f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(text.GetComponent<RectTransform>(), 4f, 4f, 4f, 4f);
    }

    private static void BuildFullScreenPanel(Transform parent)
    {
        GameObject panel = CreateUI("FullScreenPanel", parent);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(32f, 32f);
        rect.offsetMax = new Vector2(-32f, -32f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        GameObject background = CreateImage("PanelBackground", panel.transform, PanelBackground, false);
        Stretch(background.GetComponent<RectTransform>());
        background.GetComponent<LayoutElement>().ignoreLayout = true;
        background.transform.SetAsFirstSibling();

        BuildHeader(panel.transform);
        BuildTopInfoCard(panel.transform);
        BuildTabBar(panel.transform);
        BuildPageContainer(panel.transform);
        BuildFooter(panel.transform);
    }

    private static void BuildHeader(Transform parent)
    {
        GameObject header = CreateUI("Header", parent);
        AddLayoutElement(header, 108f, 108f, 0f, 0f);

        GameObject title = CreateText("TitleText", header.transform, "Developer Panel", 48f, PrimaryText, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(title.GetComponent<RectTransform>(), 90f, 0f, 90f, 0f);

        GameObject closeButton = CreateUI("CloseButton", header.transform);
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 0.5f);
        closeRect.anchorMax = new Vector2(1f, 0.5f);
        closeRect.pivot = new Vector2(1f, 0.5f);
        closeRect.sizeDelta = new Vector2(72f, 72f);
        closeRect.anchoredPosition = Vector2.zero;

        GameObject background = CreateImage("Background", closeButton.transform, Color.white, true);
        Stretch(background.GetComponent<RectTransform>());
        AddOutline(background, Border, 2f);

        GameObject closeText = CreateText("CloseText", closeButton.transform, "X", 44f, PrimaryText, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(closeText.GetComponent<RectTransform>());
    }

    private static void BuildTopInfoCard(Transform parent)
    {
        GameObject card = CreateImage("TopInfoCard", parent, CardBackground, true);
        AddOutline(card, Border, 2f);
        AddLayoutElement(card, 158f, 158f, 0f, 0f);
        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        BuildInfoRow(card.transform, "Scene", "MainMenu", PrimaryText);
        BuildInfoRow(card.transform, "Build", "1.4.2 (142)", PrimaryText);
        BuildInfoRow(card.transform, "Environment", "Development", Success);
    }

    private static void BuildInfoRow(Transform parent, string label, string value, Color valueColor)
    {
        GameObject row = CreateUI(label + "Row", parent);
        AddLayoutElement(row, 36f, 36f);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        GameObject labelText = CreateText("LabelText", row.transform, label, 30f, SecondaryText, TextAlignmentOptions.Left);
        AddLayoutElement(labelText, 250f, 0f, 0f);

        GameObject valueText = CreateText("ValueText", row.transform, value, 30f, valueColor, TextAlignmentOptions.Right, FontStyles.Bold);
        AddLayoutElement(valueText, 0f, 0f, 1f);
    }

    private static void BuildTabBar(Transform parent)
    {
        GameObject tabBar = CreateUI("TabBar", parent);
        AddLayoutElement(tabBar, 82f, 82f, 0f, 0f);
        HorizontalLayoutGroup layout = tabBar.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 0f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        BuildTab(tabBar.transform, "AnalyticsTab", "Analytics", true);
        BuildTab(tabBar.transform, "CrashTab", "Crash", false);
        BuildTab(tabBar.transform, "SaveTab", "Save", false);
        BuildTab(tabBar.transform, "DeviceTab", "Device", false);
    }

    private static void BuildTab(Transform parent, string name, string text, bool active)
    {
        GameObject tab = CreateUI(name, parent);
        AddLayoutElement(tab, 0f, 82f, 1f);

        GameObject label = CreateText("Text", tab.transform, text, 32f, active ? Accent : SecondaryText, TextAlignmentOptions.Center, active ? FontStyles.Bold : FontStyles.Normal);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.16f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        GameObject underline = CreateImage("ActiveUnderline", tab.transform, Accent, false);
        RectTransform underlineRect = underline.GetComponent<RectTransform>();
        underlineRect.anchorMin = new Vector2(0.16f, 0f);
        underlineRect.anchorMax = new Vector2(0.84f, 0f);
        underlineRect.pivot = new Vector2(0.5f, 0f);
        underlineRect.sizeDelta = new Vector2(0f, 6f);
        underlineRect.anchoredPosition = Vector2.zero;
        underline.SetActive(active);
    }

    private static void BuildPageContainer(Transform parent)
    {
        GameObject pageContainer = CreateUI("PageContainer", parent);
        AddLayoutElement(pageContainer, 0f, 0f, 0f, 1f);

        GameObject analyticsPage = CreateUI("AnalyticsPage", pageContainer.transform);
        Stretch(analyticsPage.GetComponent<RectTransform>());
        BuildAnalyticsPage(analyticsPage.transform);

        BuildPlaceholderPage(pageContainer.transform, "CrashPage", "Crash", "This module will be added later.");
        BuildPlaceholderPage(pageContainer.transform, "SavePage", "Save", "This module will be added later.");
        BuildPlaceholderPage(pageContainer.transform, "DevicePage", "Device", "This module will be added later.");
    }

    private static void BuildAnalyticsPage(Transform parent)
    {
        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        BuildProviderStatusSection(parent);
        BuildMainContentArea(parent);
        BuildActionsSection(parent);
    }

    private static void BuildProviderStatusSection(Transform parent)
    {
        GameObject section = CreateCompactSection("ProviderStatusSection", parent, 150f);
        CreateSectionTitle(section.transform, "Provider Status", 30f, 42f);

        GameObject row = CreateUI("ProviderMiniCardsRow", section.transform);
        AddLayoutElement(row, 0f, 80f, 0f, 0f);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        BuildProviderMiniCard(row.transform, "FirebaseMiniCard", "Firebase", "Ready \u2022 Enabled \u2022 Queue 0");
    }

    private static void BuildProviderMiniCard(Transform parent, string name, string providerName, string summary)
    {
        GameObject card = CreateImage(name, parent, Hex("FAFBFD"), true);
        AddOutline(card, Border, 2f);
        AddLayoutElement(card, 0f, 80f, 1f, 0f);

        GameObject dot = CreateImage("StatusDot", card.transform, Success, false);
        RectTransform dotRect = dot.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0f, 0.5f);
        dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot = new Vector2(0f, 0.5f);
        dotRect.sizeDelta = new Vector2(14f, 14f);
        dotRect.anchoredPosition = new Vector2(12f, 0f);

        GameObject providerText = CreateText("ProviderNameText", card.transform, providerName, 26f, PrimaryText, TextAlignmentOptions.Left, FontStyles.Bold);
        RectTransform providerRect = providerText.GetComponent<RectTransform>();
        providerRect.anchorMin = new Vector2(0f, 0.42f);
        providerRect.anchorMax = Vector2.one;
        providerRect.offsetMin = new Vector2(34f, 0f);
        providerRect.offsetMax = new Vector2(-10f, -4f);

        GameObject summaryText = CreateText("SummaryText", card.transform, summary, 21f, SecondaryText, TextAlignmentOptions.Left);
        RectTransform summaryRect = summaryText.GetComponent<RectTransform>();
        summaryRect.anchorMin = Vector2.zero;
        summaryRect.anchorMax = new Vector2(1f, 0.42f);
        summaryRect.offsetMin = new Vector2(34f, 7f);
        summaryRect.offsetMax = new Vector2(-10f, 0f);
    }

    private static void BuildMainContentArea(Transform parent)
    {
        GameObject mainContent = CreateUI("MainContentArea", parent);
        AddLayoutElement(mainContent, 0f, 0f, 0f, 1f);
        VerticalLayoutGroup layout = mainContent.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        BuildRecentEventsPanel(mainContent.transform);
        BuildSelectedEventDetailsPanel(mainContent.transform);
    }

    private static void BuildRecentEventsPanel(Transform parent)
    {
        GameObject section = CreateFlexibleSection("RecentEventsPanel", parent, 3f);

        GameObject headerRow = CreateUI("HeaderRow", section.transform);
        AddLayoutElement(headerRow, 0f, 38f);
        HorizontalLayoutGroup headerLayout = headerRow.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childControlHeight = true;

        GameObject sectionTitle = CreateText("SectionTitle", headerRow.transform, "Recent Events", 29f, PrimaryText, TextAlignmentOptions.Left, FontStyles.Bold);
        AddLayoutElement(sectionTitle, 0f, 38f, 1f);
        GameObject totalCount = CreateText("TotalCountText", headerRow.transform, "Total: 245", 24f, SecondaryText, TextAlignmentOptions.Right);
        AddLayoutElement(totalCount, 170f, 38f, 0f);

        GameObject content = CreateScrollView("EventsScrollView", section.transform, out _);
        CreateEventRow("EventRow_012", "#012", "14:24:30", "level_completed", "FB: Forwarded   GA: Forwarded", false).transform.SetParent(content.transform, false);
        CreateEventRow("EventRow_011", "#011", "14:24:12", "chest_opened", "FB: Forwarded   GA: Forwarded", true).transform.SetParent(content.transform, false);
        CreateEventRow("EventRow_010", "#010", "14:23:55", "currency_earned", "FB: Forwarded   GA: Forwarded", false).transform.SetParent(content.transform, false);
        CreateEventRow("EventRow_009", "#009", "14:22:42", "ad_reward_granted", "FB: Forwarded   GA: Forwarded", false).transform.SetParent(content.transform, false);
        CreateEventRow("EventRow_008", "#008", "14:22:10", "session_checkpoint", "FB: Forwarded   GA: Forwarded", false).transform.SetParent(content.transform, false);
    }

    private static GameObject CreateEventRow(string name, string sequence, string time, string eventName, string providerSummary, bool selected)
    {
        GameObject row = CreateUI(name, null);
        AddLayoutElement(row, 0f, 82f);

        GameObject background = CreateImage("Background", row.transform, selected ? AccentSoft : Color.white, true);
        Stretch(background.GetComponent<RectTransform>());
        AddOutline(background, selected ? Accent : Border, selected ? 3f : 1.5f);

        GameObject content = CreateUI("Content", row.transform);
        Stretch(content.GetComponent<RectTransform>(), 18f, 8f, 18f, 8f);
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        GameObject eventSummaryText = CreateText("EventSummaryText", content.transform, sequence + "   " + time + "   " + eventName, 25f, selected ? Accent : PrimaryText, TextAlignmentOptions.Left, FontStyles.Bold);
        AddLayoutElement(eventSummaryText, 0f, 34f);

        GameObject providerSummaryText = CreateText("ProviderSummaryText", content.transform, providerSummary, 21f, Success, TextAlignmentOptions.Left);
        AddLayoutElement(providerSummaryText, 0f, 28f);

        return row;
    }

    private static void BuildSelectedEventDetailsPanel(Transform parent)
    {
        GameObject section = CreateFlexibleSection("SelectedEventDetailsPanel", parent, 2f);
        CreateSectionTitle(section.transform, "Selected Event Details", 29f, 38f);

        GameObject content = CreateScrollView("DetailsScrollView", section.transform, out _);
        BuildDetailRow(content.transform, "EventNameRow", "Event Name", "chest_opened", PrimaryText);

        GameObject properties = CreateUI("PropertyRowsContainer", content.transform);
        AddLayoutElement(properties, 0f, 126f);
        VerticalLayoutGroup propertiesLayout = properties.AddComponent<VerticalLayoutGroup>();
        propertiesLayout.spacing = 6f;
        propertiesLayout.childControlWidth = true;
        propertiesLayout.childForceExpandWidth = true;
        propertiesLayout.childControlHeight = true;
        propertiesLayout.childForceExpandHeight = false;
        CreatePropertyRow("PropertyRow_level_number", "level_number", "12").transform.SetParent(properties.transform, false);
        CreatePropertyRow("PropertyRow_moves_used", "moves_used", "18").transform.SetParent(properties.transform, false);
        CreatePropertyRow("PropertyRow_duration_seconds", "duration_seconds", "47.32").transform.SetParent(properties.transform, false);

        GameObject providerResults = CreateUI("ProviderResultRows", content.transform);
        AddLayoutElement(providerResults, 0f, 86f);
        VerticalLayoutGroup providerLayout = providerResults.AddComponent<VerticalLayoutGroup>();
        providerLayout.spacing = 6f;
        providerLayout.childControlWidth = true;
        providerLayout.childForceExpandWidth = true;
        providerLayout.childControlHeight = true;
        providerLayout.childForceExpandHeight = false;
        BuildDetailRow(providerResults.transform, "FirebaseResultRow", "Firebase Result", "Forwarded", Success);
    }

    private static void BuildDetailRow(Transform parent, string name, string label, string value, Color valueColor)
    {
        GameObject row = CreateUI(name, parent);
        AddLayoutElement(row, 0f, 40f);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        GameObject labelText = CreateText("LabelText", row.transform, label, 28f, SecondaryText, TextAlignmentOptions.Left);
        AddLayoutElement(labelText, 0f, 40f, 1f);
        GameObject valueText = CreateText("ValueText", row.transform, value, 28f, valueColor, TextAlignmentOptions.Right, FontStyles.Bold);
        AddLayoutElement(valueText, 0f, 40f, 1f);
    }

    private static GameObject CreatePropertyRow(string name, string label, string value)
    {
        GameObject row = CreateUI(name, null);
        AddLayoutElement(row, 0f, 38f);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        GameObject labelText = CreateText("LabelText", row.transform, label, 28f, PrimaryText, TextAlignmentOptions.Left);
        AddLayoutElement(labelText, 0f, 38f, 1f);
        GameObject valueText = CreateText("ValueText", row.transform, value, 28f, PrimaryText, TextAlignmentOptions.Right, FontStyles.Bold);
        AddLayoutElement(valueText, 0f, 38f, 1f);
        return row;
    }

    private static void BuildActionsSection(Transform parent)
    {
        GameObject section = CreateSection("ActionsSection", parent, 188f);
        VerticalLayoutGroup sectionLayout = section.GetComponent<VerticalLayoutGroup>();
        sectionLayout.padding = new RectOffset(14, 14, 10, 10);
        sectionLayout.spacing = 8f;
        BuildActionButton(section.transform, "RefreshStatusButton", "Refresh Status", Accent, Color.white, Accent);
        BuildActionButton(section.transform, "SendSmokeTestButton", "Send Smoke Test", Color.white, Accent, Accent);

        GameObject bottomRow = CreateUI("BottomButtonRow", section.transform);
        AddLayoutElement(bottomRow, 0f, 48f, 0f, 0f);
        HorizontalLayoutGroup layout = bottomRow.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        BuildActionButton(bottomRow.transform, "CopyAllButton", "Copy All", Color.white, Accent, Accent, true);
        BuildActionButton(bottomRow.transform, "ClearLogButton", "Clear Log", Color.white, Error, Error, true);
    }

    private static void BuildActionButton(Transform parent, string name, string text, Color backgroundColor, Color textColor, Color borderColor, bool flexible = false)
    {
        GameObject button = CreateUI(name, parent);
        AddLayoutElement(button, flexible ? 0f : 0f, 48f, flexible ? 1f : 0f, 0f);
        button.AddComponent<Button>();

        GameObject background = CreateImage("Background", button.transform, backgroundColor, true);
        Stretch(background.GetComponent<RectTransform>());
        AddOutline(background, borderColor, 2f);
        button.GetComponent<Button>().targetGraphic = background.GetComponent<Image>();

        GameObject label = CreateText("ButtonText", button.transform, text, 24f, textColor, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(label.GetComponent<RectTransform>(), 8f, 0f, 8f, 0f);
    }

    private static void BuildFooter(Transform parent)
    {
        GameObject footer = CreateUI("Footer", parent);
        AddLayoutElement(footer, 0f, 58f, 0f, 0f);
        HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        GameObject icon = CreateText("IconText", footer.transform, "DEV", 22f, SecondaryText, TextAlignmentOptions.Center, FontStyles.Bold);
        AddLayoutElement(icon, 58f, 58f);
        GameObject footerText = CreateText("FooterText", footer.transform, "Developer Panel is only available in development builds.", 24f, SecondaryText, TextAlignmentOptions.Left);
        AddLayoutElement(footerText, 0f, 58f, 1f);
    }

    private static void BuildPlaceholderPage(Transform parent, string pageName, string title, string description)
    {
        GameObject page = CreateUI(pageName, parent);
        Stretch(page.GetComponent<RectTransform>());

        GameObject content = CreateUI("PlaceholderContent", page.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 0.5f);
        contentRect.anchorMax = new Vector2(1f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(0f, 150f);
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        GameObject titleText = CreateText("TitleText", content.transform, title, 36f, PrimaryText, TextAlignmentOptions.Center, FontStyles.Bold);
        AddLayoutElement(titleText, 0f, 54f);
        GameObject descriptionText = CreateText("DescriptionText", content.transform, description, 28f, SecondaryText, TextAlignmentOptions.Center);
        AddLayoutElement(descriptionText, 0f, 44f);

        page.SetActive(false);
    }

    private static GameObject CreateFlexibleSection(string name, Transform parent, float flexibleHeight)
    {
        GameObject section = CreateImage(name, parent, CardBackground, true);
        AddOutline(section, Border, 1.5f);
        AddLayoutElement(section, 0f, 0f, 0f, flexibleHeight);
        VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        return section;
    }

    private static GameObject CreateCompactSection(string name, Transform parent, float preferredHeight)
    {
        GameObject section = CreateImage(name, parent, CardBackground, true);
        AddOutline(section, Border, 1.5f);
        AddLayoutElement(section, 0f, preferredHeight);
        VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        return section;
    }

    private static GameObject CreateScrollView(string name, Transform parent, out ScrollRect scrollRect)
    {
        GameObject scrollView = CreateUI(name, parent);
        AddLayoutElement(scrollView, 0f, 0f, 0f, 1f);
        scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 34f;

        GameObject viewport = CreateImage("Viewport", scrollView.transform, new Color(1f, 1f, 1f, 0f), true);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect, 0f, 0f, 16f, 0f);
        viewport.AddComponent<RectMask2D>();

        GameObject content = CreateUI("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8f;
        contentLayout.childAlignment = TextAnchor.UpperCenter;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject scrollbar = CreateScrollbar(scrollView.transform);
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.verticalScrollbar = scrollbar.GetComponent<Scrollbar>();
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        return content;
    }

    private static GameObject CreateSection(string name, Transform parent, float preferredHeight)
    {
        GameObject section = CreateImage(name, parent, CardBackground, true);
        AddOutline(section, Border, 1.5f);
        AddLayoutElement(section, 0f, preferredHeight);
        VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        return section;
    }

    private static GameObject CreateSectionTitle(Transform parent, string text)
    {
        return CreateSectionTitle(parent, text, 32f, 42f);
    }

    private static GameObject CreateSectionTitle(Transform parent, string text, float fontSize, float height)
    {
        GameObject title = CreateText("SectionTitle", parent, text, fontSize, PrimaryText, TextAlignmentOptions.Left, FontStyles.Bold);
        AddLayoutElement(title, 0f, height);
        return title;
    }

    private static GameObject CreateScrollbar(Transform parent)
    {
        GameObject scrollbar = CreateUI("Scrollbar", parent);
        RectTransform rect = scrollbar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(12f, 0f);
        rect.anchoredPosition = Vector2.zero;

        Image background = scrollbar.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0f);
        background.raycastTarget = false;
        Scrollbar bar = scrollbar.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;

        GameObject slidingArea = CreateUI("Sliding Area", scrollbar.transform);
        Stretch(slidingArea.GetComponent<RectTransform>());
        GameObject handle = CreateImage("Handle", slidingArea.transform, Hex("B9C2CF"), true);
        Stretch(handle.GetComponent<RectTransform>());
        bar.targetGraphic = handle.GetComponent<Image>();
        bar.handleRect = handle.GetComponent<RectTransform>();
        return scrollbar;
    }

    private static GameObject CreateUI(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }
        return go;
    }

    private static GameObject CreateImage(string name, Transform parent, Color color, bool raycastTarget)
    {
        GameObject go = CreateUI(name, parent);
        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        if (roundedSprite != null)
        {
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
        }
        go.AddComponent<LayoutElement>();
        return go;
    }

    private static GameObject CreateText(string name, Transform parent, string value, float size, Color color, TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal)
    {
        GameObject go = CreateUI(name, parent);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = style;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return go;
    }

    private static void AddOutline(GameObject target, Color color, float distance)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    private static LayoutElement AddLayoutElement(GameObject go, float preferredWidth, float preferredHeight, float flexibleWidth = 0f, float flexibleHeight = -1f)
    {
        LayoutElement element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (preferredWidth > 0f)
        {
            element.preferredWidth = preferredWidth;
        }
        if (preferredHeight > 0f)
        {
            element.preferredHeight = preferredHeight;
        }
        if (flexibleWidth > 0f)
        {
            element.flexibleWidth = flexibleWidth;
        }
        if (flexibleHeight >= 0f)
        {
            element.flexibleHeight = flexibleHeight;
        }
        return element;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
    }

    private static void RemoveGeneratedRoot(string rootName)
    {
        GameObject existing = GameObject.Find(rootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }
    }

    private static Color Hex(string value)
    {
        ColorUtility.TryParseHtmlString("#" + value, out Color color);
        return color;
    }
}

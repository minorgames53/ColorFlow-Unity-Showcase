using System.Collections.Generic;
using Game.Shared.UI.Panels;
using Game.Shared.UI;
using Game.Shared.Save;
using Game.Shared.UI.Settings;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.UI.Shop;
using Gameplay.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Serialization;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gameplay.UI.HUD
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GameplayHudController : MonoBehaviour
    {
        private const int SafeAreaSearchIterations = 24;
        private const float LayoutComparisonEpsilon = 0.0001f;

        [SerializeField] private TMP_Text levelText;
        [SerializeField] private Button settingsButton;
        [SerializeField] private UIPanel settingsPanel;
        [SerializeField] private SettingsPanelController settingsPanelController;
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private BoosterUnlockTutorialController boosterUnlockTutorialController;

        [Header("Localization")]
        [SerializeField] private LocalizedString levelFormat;

        [Header("Gold Display / Shop")]
        [SerializeField] private GameObject goldDisplayRoot;
        [SerializeField] private Canvas goldDisplayCanvas;
        [SerializeField] private TMP_Text goldText;
        [SerializeField] private TweenButton goldAddButton;
        [SerializeField] private GameShopPanelController gameShopPanelController;
        [FormerlySerializedAs("failOfferGoldSortingOrder")]
        [SerializeField] private int foregroundGoldSortingOrder = 102;

        [Header("GameLayout")]
        [SerializeField, InspectorName("Target Camera")] private Camera gameplayCamera;
        [SerializeField, InspectorName("Target Canvas")] private Canvas targetCanvas;
        [SerializeField, InspectorName("Safe Area Line Top")] private RectTransform safeAreaLineTop;
        [FormerlySerializedAs("safeAreaBoard")]
        [SerializeField, InspectorName("Safe Area Board Top")] private Transform safeAreaBoardTop;
        [SerializeField, InspectorName("Safe Area Board Side")] private Transform safeAreaBoardSide;
        [SerializeField, InspectorName("Grid Controller")] private SourceBoxPlaceholderGridController gridController;
        [FormerlySerializedAs("boardTopOffsetPixels")]
        [SerializeField, InspectorName("Top Offset Pixels"), Min(0f)] private float topOffsetPixels;
        [FormerlySerializedAs("boardSideOffsetPixels")]
        [SerializeField, InspectorName("Side Offset Pixels"), Min(0f)] private float sideOffsetPixels = 30f;
        [SerializeField, Min(0.01f)] private float minOrthographicSize = 1f;
        [SerializeField, Min(0.01f)] private float maxOrthographicSize = 20f;
        [SerializeField] private bool livePreviewInEditor = true;

        private bool ownsSettingsPause;
        [SerializeField, HideInInspector] private float authoredOrthographicSize;
        [SerializeField, HideInInspector] private bool hasCachedAuthoredCameraSize;
        private bool safeAreaFitRequested = true;
        private bool editorPreviewApplied;
        private bool editorPreviewRefreshQueued;
        private bool hasLoggedCameraValidationError;
        private SafeAreaFitInputs lastSafeAreaInputs;
        private bool hasLastSafeAreaInputs;
        private SourceBoxPlaceholderGridController subscribedGridController;
        private SaveManager saveManager;
        private bool goldSubscribed;
        private int authoredGoldSortingLayerId;
        private int authoredGoldSortingOrder;
        private bool hasCachedGoldSortingState;
        private bool goldDisplayInForeground;
        private bool shopOpen;
        private readonly Dictionary<string, object> levelFormatValues =
            new Dictionary<string, object>();
        private object[] levelFormatArguments;
        private bool levelFormatSubscribed;
        private int currentDisplayedLevelNumber = 1;

        private struct SafeAreaFitInputs
        {
            public int ScreenWidth;
            public int ScreenHeight;
            public ScreenOrientation Orientation;
            public Rect SafeArea;
            public Rect CameraViewportRect;
            public bool CameraOrthographic;
            public Vector3 CameraPosition;
            public Quaternion CameraRotation;
            public Vector3 LineWorldPosition;
            public Vector3 BoardTopWorldPosition;
            public Vector3 BoardSideWorldPosition;
            public Vector3 GridRootPosition;
            public Quaternion GridRootRotation;
            public Vector3 GridRootScale;
            public RenderMode CanvasRenderMode;
            public Camera CanvasCamera;
            public float CanvasScaleFactor;
            public float TopOffsetPixels;
            public float SideOffsetPixels;
            public float MinSize;
            public float MaxSize;
        }

        private void Awake()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                CacheAuthoredCameraSize();
                return;
            }
#endif

            CacheReferences();
            CacheAuthoredCameraSize();
            CacheGoldDisplayPresentation();
        }

        private void Reset()
        {
            CacheReferences();
            CacheAuthoredCameraSize();
            ApplyGameLayout();
        }

        private void OnEnable()
        {
            CacheAuthoredCameraSize();
            safeAreaFitRequested = true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RegisterGridLayoutListener();
                ScheduleEditorPreviewRefresh();
                return;
            }
#endif

            CacheReferences();
            BindLocalization();
            RegisterListeners();
            TryBindGold();
            RefreshGoldDisplayPresentation();
            RefreshLevelText();
            ApplyGameLayout();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorApplication.delayCall -= ApplyScheduledEditorPreview;
                EditorApplication.update -= ApplyScheduledEditorPreview;
                editorPreviewRefreshQueued = false;
                UnregisterGridLayoutListener();
                RestoreAuthoredCameraSize();
                hasLastSafeAreaInputs = false;
                return;
            }
#endif

            UnregisterListeners();
            UnbindLocalization();
            UnbindGold();
            goldDisplayInForeground = false;
            shopOpen = false;
            RefreshGoldDisplayPresentation();
            ReleaseSettingsPauseIfNeeded();
            RestoreAuthoredCameraSize();
            hasLastSafeAreaInputs = false;
        }

        private void OnValidate()
        {
            topOffsetPixels = Mathf.Max(0f, topOffsetPixels);
            sideOffsetPixels = Mathf.Max(0f, sideOffsetPixels);
            minOrthographicSize = Mathf.Max(0.01f, minOrthographicSize);
            maxOrthographicSize = Mathf.Max(minOrthographicSize, maxOrthographicSize);
            CacheAuthoredCameraSize();
            safeAreaFitRequested = true;

#if UNITY_EDITOR
            hasLastSafeAreaInputs = false;

            if (!Application.isPlaying && isActiveAndEnabled)
            {
                RegisterGridLayoutListener();
            }

            if (!Application.isPlaying && !livePreviewInEditor)
            {
                EditorApplication.delayCall -= ApplyScheduledEditorPreview;
                EditorApplication.update -= ApplyScheduledEditorPreview;
                editorPreviewRefreshQueued = false;
                RestoreAuthoredCameraSize();
                editorPreviewApplied = false;
                hasLastSafeAreaInputs = false;
            }
            else if (!Application.isPlaying)
            {
                ScheduleEditorPreviewRefresh();
            }
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UpdateSafeAreaCameraFit();
                return;
            }
#endif

            RefreshSettingsPauseLifecycle();
            if (!goldSubscribed || saveManager != SaveManager.Instance)
            {
                TryBindGold();
            }

            UpdateSafeAreaCameraFit();
        }

        private void RegisterListeners()
        {
            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(HandleSettingsButtonClicked);
                settingsButton.onClick.AddListener(HandleSettingsButtonClicked);
            }

            if (goldAddButton != null)
            {
                goldAddButton.onClick.RemoveListener(HandleGoldAddButtonClicked);
                goldAddButton.onClick.AddListener(HandleGoldAddButtonClicked);
            }

            if (levelSessionController != null)
            {
                levelSessionController.DisplayedLevelNumberChanged -= HandleDisplayedLevelNumberChanged;
                levelSessionController.DisplayedLevelNumberChanged += HandleDisplayedLevelNumberChanged;
            }

            if (gameShopPanelController != null)
            {
                gameShopPanelController.Opened -= HandleShopOpened;
                gameShopPanelController.Opened += HandleShopOpened;
                gameShopPanelController.Closed -= HandleShopClosed;
                gameShopPanelController.Closed += HandleShopClosed;
                shopOpen = gameShopPanelController.IsOpen;
            }

            RegisterGridLayoutListener();
        }

        private void BindLocalization()
        {
            if (levelFormatSubscribed || levelFormat == null || levelFormat.IsEmpty)
            {
                return;
            }

            currentDisplayedLevelNumber = levelSessionController != null
                ? Mathf.Max(1, levelSessionController.DisplayedLevelNumber)
                : 1;
            levelFormatValues["level"] = currentDisplayedLevelNumber;
            if (levelFormatArguments == null)
            {
                levelFormatArguments = new object[] { levelFormatValues };
            }

            levelFormat.Arguments = levelFormatArguments;
            levelFormat.StringChanged += HandleLocalizedLevelChanged;
            levelFormatSubscribed = true;
        }

        private void UnbindLocalization()
        {
            if (!levelFormatSubscribed)
            {
                return;
            }

            levelFormat.StringChanged -= HandleLocalizedLevelChanged;
            levelFormatSubscribed = false;
        }

        private void HandleLocalizedLevelChanged(string localizedText)
        {
            if (levelText != null)
            {
                levelText.text = localizedText;
            }
        }

        private void UnregisterListeners()
        {
            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(HandleSettingsButtonClicked);
            }

            if (goldAddButton != null)
            {
                goldAddButton.onClick.RemoveListener(HandleGoldAddButtonClicked);
            }

            if (levelSessionController != null)
            {
                levelSessionController.DisplayedLevelNumberChanged -= HandleDisplayedLevelNumberChanged;
            }

            if (gameShopPanelController != null)
            {
                gameShopPanelController.Opened -= HandleShopOpened;
                gameShopPanelController.Closed -= HandleShopClosed;
            }

            UnregisterGridLayoutListener();
        }

        private void RegisterGridLayoutListener()
        {
            if (subscribedGridController != gridController)
            {
                if (subscribedGridController != null)
                {
                    subscribedGridController.BoardLayoutChanged -= RefreshFit;
                }

                subscribedGridController = gridController;
            }

            if (subscribedGridController == null)
            {
                return;
            }

            subscribedGridController.BoardLayoutChanged -= RefreshFit;
            subscribedGridController.BoardLayoutChanged += RefreshFit;
        }

        private void UnregisterGridLayoutListener()
        {
            if (subscribedGridController != null)
            {
                subscribedGridController.BoardLayoutChanged -= RefreshFit;
                subscribedGridController = null;
            }
        }

        private void HandleSettingsButtonClicked()
        {
            if (panelManager != null && panelManager.TryDeferOpen(settingsPanel, HandleSettingsButtonClicked)) return;

            if (boosterUnlockTutorialController != null && boosterUnlockTutorialController.IsRunning)
            {
                return;
            }

            CacheReferences();

            if (levelSessionController == null || !levelSessionController.TryPauseGameplay())
            {
                return;
            }

            ownsSettingsPause = true;

            if (settingsPanelController != null)
            {
                settingsPanelController.Open();
                return;
            }

            if (panelManager == null)
            {
                Debug.LogWarning($"{nameof(GameplayHudController)} cannot open settings because no {nameof(PanelManager)} is assigned.", this);
                return;
            }

            if (settingsPanel == null)
            {
                Debug.LogWarning($"{nameof(GameplayHudController)} cannot open settings because no {nameof(UIPanel)} is assigned.", this);
                return;
            }

            panelManager.OpenRoot(settingsPanel);
        }

        public void SetSettingsInteractionEnabled(bool enabled)
        {
            if (settingsButton != null)
            {
                settingsButton.interactable = enabled;
            }
        }

        public void BringGoldDisplayToFront()
        {
            goldDisplayInForeground = true;
            RefreshGoldDisplayPresentation();
        }

        public void RestoreGoldDisplaySortingOrder()
        {
            goldDisplayInForeground = false;
            RefreshGoldDisplayPresentation();
        }

        private void HandleShopOpened()
        {
            shopOpen = true;
            RefreshGoldDisplayPresentation();
        }

        private void HandleShopClosed()
        {
            shopOpen = false;
            RefreshGoldDisplayPresentation();
        }

        private void CacheGoldDisplayPresentation()
        {
            if (hasCachedGoldSortingState || goldDisplayCanvas == null)
            {
                return;
            }

            authoredGoldSortingLayerId = goldDisplayCanvas.sortingLayerID;
            authoredGoldSortingOrder = goldDisplayCanvas.sortingOrder;
            hasCachedGoldSortingState = true;
        }

        private void RefreshGoldDisplayPresentation()
        {
            CacheGoldDisplayPresentation();
            if (goldDisplayCanvas != null)
            {
                goldDisplayCanvas.sortingLayerID = goldDisplayInForeground && targetCanvas != null
                    ? targetCanvas.sortingLayerID
                    : authoredGoldSortingLayerId;
                goldDisplayCanvas.sortingOrder = goldDisplayInForeground
                    ? foregroundGoldSortingOrder
                    : authoredGoldSortingOrder;
            }

            if (goldDisplayRoot != null)
            {
                goldDisplayRoot.SetActive(!shopOpen);
            }
        }

        private void RefreshSettingsPauseLifecycle()
        {
            if (!ownsSettingsPause || levelSessionController == null)
            {
                return;
            }

            if (levelSessionController.GameplayState != GameplaySessionState.Paused)
            {
                ownsSettingsPause = false;
                return;
            }

            bool settingsStillManaged = settingsPanel != null &&
                                        (settingsPanel.gameObject.activeSelf ||
                                         panelManager != null && panelManager.ContainsPanel(settingsPanel));
            if (settingsStillManaged)
            {
                return;
            }

            levelSessionController.TryResumeGameplay();
            ownsSettingsPause = false;
        }

        private void ReleaseSettingsPauseIfNeeded()
        {
            if (ownsSettingsPause && levelSessionController != null &&
                levelSessionController.GameplayState == GameplaySessionState.Paused)
            {
                levelSessionController.TryResumeGameplay();
            }

            ownsSettingsPause = false;
        }

        private void HandleGoldAddButtonClicked()
        {
            if (boosterUnlockTutorialController != null && boosterUnlockTutorialController.IsRunning)
            {
                return;
            }

            if (gameShopPanelController == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameplayHudController)} cannot open Shop because no {nameof(GameShopPanelController)} is assigned.",
                    this);
                return;
            }

            gameShopPanelController.Open();
        }

        private void TryBindGold()
        {
            SaveManager availableSaveManager = SaveManager.Instance;
            if (availableSaveManager == null || !availableSaveManager.IsInitialized)
            {
                return;
            }

            if (saveManager != availableSaveManager)
            {
                UnbindGold();
                saveManager = availableSaveManager;
            }

            if (!goldSubscribed)
            {
                saveManager.GoldChanged += HandleGoldChanged;
                goldSubscribed = true;
            }

            SetGold(saveManager.Gold);
        }

        private void UnbindGold()
        {
            if (saveManager != null && goldSubscribed)
            {
                saveManager.GoldChanged -= HandleGoldChanged;
            }

            goldSubscribed = false;
            saveManager = null;
        }

        private void HandleGoldChanged(int gold)
        {
            SetGold(gold);
        }

        private void SetGold(int gold)
        {
            goldText?.SetText("{0}", Mathf.Max(0, gold));
        }

        private void HandleDisplayedLevelNumberChanged(int displayedLevelNumber)
        {
            SetLevelText(displayedLevelNumber);
            RefreshFit();
        }

        private void RefreshLevelText()
        {
            if (levelSessionController == null)
            {
                SetLevelText(1);
                return;
            }

            SetLevelText(Mathf.Max(1, levelSessionController.DisplayedLevelNumber));
        }

        private void SetLevelText(int displayedLevelNumber)
        {
            currentDisplayedLevelNumber = Mathf.Max(1, displayedLevelNumber);
            levelFormatValues["level"] = currentDisplayedLevelNumber;
            levelFormat?.RefreshString();
        }

        [ContextMenu("Apply Game Layout")]
        public void ApplyGameLayout()
        {
            Canvas.ForceUpdateCanvases();
            ApplySafeAreaCameraFit();
        }

        [ContextMenu("Refresh Safe Area Fit")]
        public void RefreshFit()
        {
            safeAreaFitRequested = true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                ScheduleEditorPreviewRefresh();
            }
#endif
        }

#if UNITY_EDITOR
        private void ScheduleEditorPreviewRefresh()
        {
            if (Application.isPlaying || !livePreviewInEditor)
            {
                return;
            }

            safeAreaFitRequested = true;
            hasLastSafeAreaInputs = false;
            editorPreviewRefreshQueued = true;
            EditorApplication.delayCall -= ApplyScheduledEditorPreview;
            EditorApplication.delayCall += ApplyScheduledEditorPreview;
            EditorApplication.update -= ApplyScheduledEditorPreview;
            EditorApplication.update += ApplyScheduledEditorPreview;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void ApplyScheduledEditorPreview()
        {
            EditorApplication.delayCall -= ApplyScheduledEditorPreview;
            EditorApplication.update -= ApplyScheduledEditorPreview;

            if (!editorPreviewRefreshQueued)
            {
                return;
            }

            editorPreviewRefreshQueued = false;

            if (this == null || Application.isPlaying || !livePreviewInEditor)
            {
                return;
            }

            CacheAuthoredCameraSize();
            safeAreaFitRequested = true;
            hasLastSafeAreaInputs = false;
            Canvas.ForceUpdateCanvases();
            ApplySafeAreaCameraFit();

            if (TryCaptureSafeAreaInputs(out SafeAreaFitInputs inputs))
            {
                lastSafeAreaInputs = inputs;
                hasLastSafeAreaInputs = true;
                safeAreaFitRequested = false;
            }

            editorPreviewApplied = true;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
#endif

        private void UpdateSafeAreaCameraFit()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && !livePreviewInEditor)
            {
                if (editorPreviewApplied)
                {
                    RestoreAuthoredCameraSize();
                    editorPreviewApplied = false;
                }

                hasLastSafeAreaInputs = false;
                return;
            }
#endif

            if (!TryCaptureSafeAreaInputs(out SafeAreaFitInputs inputs))
            {
                return;
            }

            if (!safeAreaFitRequested && hasLastSafeAreaInputs && SafeAreaInputsEqual(lastSafeAreaInputs, inputs))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            ApplySafeAreaCameraFit();
            lastSafeAreaInputs = inputs;
            hasLastSafeAreaInputs = true;
            safeAreaFitRequested = false;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                editorPreviewApplied = true;
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
#endif
        }

        private void ApplySafeAreaCameraFit()
        {
            if (!ValidateSafeAreaReferences())
            {
                return;
            }

            float minSize = Mathf.Max(0.01f, minOrthographicSize);
            float maxSize = Mathf.Max(minSize, maxOrthographicSize);
            float lineScreenY = CalculateSafeAreaLineScreenY();
            float topFitSize = ResolveOrthographicSizeForMargin(
                topOffsetPixels,
                minSize,
                maxSize,
                false,
                lineScreenY);
            float sideFitSize = ResolveOrthographicSizeForMargin(
                sideOffsetPixels,
                minSize,
                maxSize,
                true,
                lineScreenY);
            float resolvedSize = Mathf.Max(topFitSize, sideFitSize);

            gameplayCamera.orthographicSize = Mathf.Clamp(resolvedSize, minSize, maxSize);
            Canvas.ForceUpdateCanvases();
            hasLoggedCameraValidationError = false;
        }

        private float ResolveOrthographicSizeForMargin(
            float desiredMarginPixels,
            float minSize,
            float maxSize,
            bool useSideMarker,
            float lineScreenY)
        {
            float minMargin = EvaluateScreenMargin(minSize, useSideMarker, lineScreenY);
            float maxMargin = EvaluateScreenMargin(maxSize, useSideMarker, lineScreenY);

            if (desiredMarginPixels < Mathf.Min(minMargin, maxMargin) ||
                desiredMarginPixels > Mathf.Max(minMargin, maxMargin))
            {
                return Mathf.Abs(minMargin - desiredMarginPixels) <= Mathf.Abs(maxMargin - desiredMarginPixels)
                    ? minSize
                    : maxSize;
            }

            float lowSize = minSize;
            float highSize = maxSize;
            bool marginIncreasesWithSize = maxMargin > minMargin;

            for (int i = 0; i < SafeAreaSearchIterations; i++)
            {
                float middleSize = (lowSize + highSize) * 0.5f;
                float middleMargin = EvaluateScreenMargin(middleSize, useSideMarker, lineScreenY);
                bool searchUpperHalf = marginIncreasesWithSize
                    ? middleMargin < desiredMarginPixels
                    : middleMargin > desiredMarginPixels;

                if (searchUpperHalf)
                {
                    lowSize = middleSize;
                }
                else
                {
                    highSize = middleSize;
                }
            }

            return (lowSize + highSize) * 0.5f;
        }

        private float EvaluateScreenMargin(float orthographicSize, bool useSideMarker, float lineScreenY)
        {
            gameplayCamera.orthographicSize = orthographicSize;
            Canvas.ForceUpdateCanvases();

            if (useSideMarker)
            {
                Vector3 markerScreenPoint = gameplayCamera.WorldToScreenPoint(safeAreaBoardSide.position);
                bool markerIsOnLeft = Vector3.Dot(
                    safeAreaBoardSide.position - gameplayCamera.transform.position,
                    gameplayCamera.transform.right) <= 0f;
                Rect safeArea = Screen.safeArea;
                return markerIsOnLeft
                    ? markerScreenPoint.x - safeArea.xMin
                    : safeArea.xMax - markerScreenPoint.x;
            }

            float boardScreenY = gameplayCamera.WorldToScreenPoint(safeAreaBoardTop.position).y;
            return lineScreenY - boardScreenY;
        }

        private float CalculateSafeAreaLineScreenY()
        {
            RectTransform canvasRectTransform = targetCanvas.transform as RectTransform;
            if (canvasRectTransform == null || canvasRectTransform.rect.height <= 0f)
            {
                Camera canvasCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : targetCanvas.worldCamera != null
                        ? targetCanvas.worldCamera
                        : gameplayCamera;
                return RectTransformUtility.WorldToScreenPoint(canvasCamera, safeAreaLineTop.position).y;
            }

            float lineLocalY = canvasRectTransform.InverseTransformPoint(safeAreaLineTop.position).y;
            float normalizedY = Mathf.InverseLerp(
                canvasRectTransform.rect.yMin,
                canvasRectTransform.rect.yMax,
                lineLocalY);
            Rect canvasPixelRect = targetCanvas.pixelRect;
            return canvasPixelRect.yMin + normalizedY * canvasPixelRect.height;
        }

        private bool ValidateSafeAreaReferences()
        {
            if (!HasSafeAreaReferences())
            {
                return false;
            }

            if (!gameplayCamera.orthographic)
            {
                LogCameraValidationErrorOnce($"{nameof(GameplayHudController)} on '{name}' requires an orthographic Target Camera for safe-area fitting.");
                return false;
            }

            if (gameplayCamera.WorldToViewportPoint(safeAreaBoardTop.position).z <= 0f)
            {
                LogCameraValidationErrorOnce($"{nameof(GameplayHudController)} on '{name}' cannot fit because Safe Area Board Top is behind the Target Camera.");
                return false;
            }

            if (gameplayCamera.WorldToViewportPoint(safeAreaBoardSide.position).z <= 0f)
            {
                LogCameraValidationErrorOnce($"{nameof(GameplayHudController)} on '{name}' cannot fit because Safe Area Board Side is behind the Target Camera.");
                return false;
            }

            return true;
        }

        private void LogCameraValidationErrorOnce(string message)
        {
            if (hasLoggedCameraValidationError)
            {
                return;
            }

            Debug.LogError(message, this);
            hasLoggedCameraValidationError = true;
        }

        private bool HasSafeAreaReferences()
        {
            return gameplayCamera != null &&
                   targetCanvas != null &&
                   safeAreaLineTop != null &&
                   safeAreaBoardTop != null &&
                   safeAreaBoardSide != null &&
                   gridController != null;
        }

        private bool TryCaptureSafeAreaInputs(out SafeAreaFitInputs inputs)
        {
            inputs = default;
            if (!HasSafeAreaReferences())
            {
                return false;
            }

            inputs.ScreenWidth = Screen.width;
            inputs.ScreenHeight = Screen.height;
            inputs.Orientation = Screen.orientation;
            inputs.SafeArea = Screen.safeArea;
            inputs.CameraViewportRect = gameplayCamera.rect;
            inputs.CameraOrthographic = gameplayCamera.orthographic;
            inputs.CameraPosition = gameplayCamera.transform.position;
            inputs.CameraRotation = gameplayCamera.transform.rotation;
            inputs.LineWorldPosition = safeAreaLineTop.position;
            inputs.BoardTopWorldPosition = safeAreaBoardTop.position;
            inputs.BoardSideWorldPosition = safeAreaBoardSide.position;
            Transform gridRoot = gridController.GeneratedGridRoot;
            inputs.GridRootPosition = gridRoot.position;
            inputs.GridRootRotation = gridRoot.rotation;
            inputs.GridRootScale = gridRoot.lossyScale;
            inputs.CanvasRenderMode = targetCanvas.renderMode;
            inputs.CanvasCamera = targetCanvas.worldCamera;
            inputs.CanvasScaleFactor = targetCanvas.scaleFactor;
            inputs.TopOffsetPixels = topOffsetPixels;
            inputs.SideOffsetPixels = sideOffsetPixels;
            inputs.MinSize = minOrthographicSize;
            inputs.MaxSize = maxOrthographicSize;
            return true;
        }

        private static bool SafeAreaInputsEqual(SafeAreaFitInputs a, SafeAreaFitInputs b)
        {
            return a.ScreenWidth == b.ScreenWidth &&
                   a.ScreenHeight == b.ScreenHeight &&
                   a.Orientation == b.Orientation &&
                   RectApproximately(a.SafeArea, b.SafeArea) &&
                   RectApproximately(a.CameraViewportRect, b.CameraViewportRect) &&
                   a.CameraOrthographic == b.CameraOrthographic &&
                   Approximately(a.CameraPosition, b.CameraPosition) &&
                   Approximately(a.CameraRotation, b.CameraRotation) &&
                   Approximately(a.LineWorldPosition, b.LineWorldPosition) &&
                   Approximately(a.BoardTopWorldPosition, b.BoardTopWorldPosition) &&
                   Approximately(a.BoardSideWorldPosition, b.BoardSideWorldPosition) &&
                   Approximately(a.GridRootPosition, b.GridRootPosition) &&
                   Approximately(a.GridRootRotation, b.GridRootRotation) &&
                   Approximately(a.GridRootScale, b.GridRootScale) &&
                   a.CanvasRenderMode == b.CanvasRenderMode &&
                   a.CanvasCamera == b.CanvasCamera &&
                   Mathf.Abs(a.CanvasScaleFactor - b.CanvasScaleFactor) <= LayoutComparisonEpsilon &&
                   Mathf.Abs(a.TopOffsetPixels - b.TopOffsetPixels) <= LayoutComparisonEpsilon &&
                   Mathf.Abs(a.SideOffsetPixels - b.SideOffsetPixels) <= LayoutComparisonEpsilon &&
                   Mathf.Abs(a.MinSize - b.MinSize) <= LayoutComparisonEpsilon &&
                   Mathf.Abs(a.MaxSize - b.MaxSize) <= LayoutComparisonEpsilon;
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude <= LayoutComparisonEpsilon * LayoutComparisonEpsilon;
        }

        private static bool Approximately(Quaternion a, Quaternion b)
        {
            return 1f - Mathf.Abs(Quaternion.Dot(a, b)) <= LayoutComparisonEpsilon;
        }

        private static bool RectApproximately(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) <= LayoutComparisonEpsilon &&
                   Mathf.Abs(a.y - b.y) <= LayoutComparisonEpsilon &&
                   Mathf.Abs(a.width - b.width) <= LayoutComparisonEpsilon &&
                   Mathf.Abs(a.height - b.height) <= LayoutComparisonEpsilon;
        }

        private void CacheAuthoredCameraSize()
        {
            if (hasCachedAuthoredCameraSize || gameplayCamera == null)
            {
                return;
            }

            authoredOrthographicSize = gameplayCamera.orthographicSize;
            hasCachedAuthoredCameraSize = true;
        }

        private void RestoreAuthoredCameraSize()
        {
            if (hasCachedAuthoredCameraSize && gameplayCamera != null)
            {
                gameplayCamera.orthographicSize = authoredOrthographicSize;
            }
        }

        private void CacheReferences()
        {
            if (panelManager == null)
            {
                panelManager = GetComponentInParent<PanelManager>(true);
            }

            if (panelManager == null)
            {
                panelManager = FindFirstObjectByType<PanelManager>(FindObjectsInactive.Include);
            }

            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>();
            }

            if (settingsPanel == null && settingsPanelController != null)
            {
                settingsPanel = settingsPanelController.Panel;
            }
        }
    }
}

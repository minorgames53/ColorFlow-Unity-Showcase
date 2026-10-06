using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.UI.Panels
{
    [DisallowMultipleComponent]
    public sealed partial class PanelManager : MonoBehaviour
    {
        public static UIConfig CurrentConfig { get; private set; }

        [Header("Panels")]
        [SerializeField] private UIConfig uiConfig;
        [SerializeField] private GameObject backgroundBlocker;
        [SerializeField] private Button backgroundBlockerButton;

        [Header("Temporary Message")]
        [SerializeField] private GameObject temporaryMessagePanel;
        [SerializeField] private TMP_Text temporaryMessageText;
        [SerializeField] private CanvasGroup temporaryMessageCanvasGroup;
        [SerializeField] private RectTransform temporaryMessageRect;
        [SerializeField, Min(0f)] private float messageHoldDuration = 1.5f;
        [SerializeField, Min(0f)] private float messageFadeMoveDuration = 1f;
        [SerializeField, Min(0f)] private float messageMoveUpDistance = 120f;

        private readonly Stack<UIPanel> panelStack = new Stack<UIPanel>();
        private readonly HashSet<UIPanel> overlayPanels = new HashSet<UIPanel>();
        private readonly HashSet<object> externalInputBlockerOwners = new HashSet<object>();
        private readonly object legacyExternalInputBlockerOwner = new object();
        private int transitionVersion;
        private int closingPanelCount;
        private bool externalInputBlockerVisible;
        private bool hasBackgroundBlockerOriginalPlacement;
        private Canvas backgroundBlockerSortingCanvas;
        private UIPanel backgroundBlockerOverlayPanel;
        private Canvas backgroundBlockerOverlayCanvas;
        private bool originalOverlayOverrideSorting;
        private int originalOverlaySortingOrder;
        private int originalOverlaySortingLayer;
        private Camera originalOverlayCamera;
        private bool restoreBackgroundBlockerPlacementPending;
        private int restoreBackgroundBlockerPlacementAfterFrame;
        private bool hierarchyShutdownInProgress;
        private int suppressBackgroundDismissThroughFrame = -1;
        private bool warnedMissingBackgroundBlocker;
        private bool warnedMissingTemporaryMessagePanel;
        private bool warnedMissingTemporaryMessageText;
        private bool hasTemporaryMessageStartPosition;
        private Vector2 temporaryMessageStartPosition;
        private Sequence temporaryMessageSequence;

        public bool HasOpenPanel => panelStack.Count > 0;
        public bool IsPanelInputBlocked => HasOpenPanel || closingPanelCount > 0;
        public GameObject BackgroundBlocker => backgroundBlocker;

        public bool ContainsPanel(UIPanel panel)
        {
            return panel != null && panelStack.Contains(panel);
        }

        public void SetExternalInputBlockerVisible(bool isVisible)
        {
            SetExternalInputBlockerVisible(legacyExternalInputBlockerOwner, isVisible);
        }

        public void SetExternalInputBlockerVisible(object owner, bool isVisible)
        {
            if (owner == null)
            {
                return;
            }

            bool wasVisible = externalInputBlockerVisible;

            if (isVisible)
            {
                externalInputBlockerOwners.Add(owner);
            }
            else
            {
                externalInputBlockerOwners.Remove(owner);
            }

            externalInputBlockerVisible = externalInputBlockerOwners.Count > 0;
            if (wasVisible && !externalInputBlockerVisible)
            {
                // A native purchase result can release the blocker on the same
                // pointer sequence that opened it. Do not let that stale click
                // dismiss the Shop through the blocker's generic close action.
                suppressBackgroundDismissThroughFrame = UnityEngine.Time.frameCount + 1;
            }

            UpdateBackgroundBlockerInteraction();
            KeepBackgroundBlockerAtBack();
            SetBackgroundBlockerVisible(IsPanelInputBlocked);
        }

        private void Awake()
        {
            CurrentConfig = uiConfig;
            CacheBackgroundBlockerReferences();
            RegisterBackgroundBlockerButton();
            CacheTemporaryMessageReferences();
            CacheTemporaryMessageStartPosition();
            ResetTemporaryMessageState();
        }

        private void OnEnable()
        {
            hierarchyShutdownInProgress = false;
            CurrentConfig = uiConfig;
            RegisterBackgroundBlockerButton();
            ScheduleBackgroundBlockerPlacementRestoreIfNeeded();
        }

        private void LateUpdate()
        {
            UpdateBlockingPanelAndQueue();
            if (backgroundBlocker != null && backgroundBlocker.transform.GetSiblingIndex() != 0)
                KeepBackgroundBlockerAtBack();
            if (!restoreBackgroundBlockerPlacementPending ||
                UnityEngine.Time.frameCount < restoreBackgroundBlockerPlacementAfterFrame)
            {
                return;
            }

            RestoreBackgroundBlockerPlacement();
        }

        private void OnDisable()
        {
            hierarchyShutdownInProgress = true;
            UnregisterBackgroundBlockerButton();
            KillTemporaryMessageSequence(true);
            CloseAll();

            if (CurrentConfig == uiConfig)
            {
                CurrentConfig = null;
            }
        }

        private void OnDestroy()
        {
            hierarchyShutdownInProgress = true;
            UnregisterBackgroundBlockerButton();
            KillTemporaryMessageSequence(true);
            CloseAll();

            if (CurrentConfig == uiConfig)
            {
                CurrentConfig = null;
            }
        }

        public void OpenRoot(UIPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            if (TryDeferOpen(panel, () => OpenRoot(panel))) return;

            if (panelStack.Count == 1 && panelStack.Peek() == panel && panel.gameObject.activeSelf)
            {
                SetBackgroundBlockerVisible(true);
                BringPanelToFront(panel);
                ApplyConfig(panel);
                return;
            }

            CloseAll();

            panelStack.Push(panel);
            SetBackgroundBlockerVisible(true);
            BringPanelToFront(panel);
            ApplyConfig(panel);
            panel.OpenFromManager();
            SetBackgroundBlockerVisible(true);
        }

        public void Push(UIPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            if (TryDeferOpen(panel, () => Push(panel))) return;

            if (panelStack.Count > 0 && panelStack.Peek() == panel)
            {
                return;
            }

            if (panelStack.Contains(panel))
            {
                return;
            }

            transitionVersion++;
            RestoreBackgroundBlockerPlacement();

            if (panelStack.Count > 0)
            {
                UIPanel currentPanel = panelStack.Peek();
                ApplyConfig(currentPanel);
                currentPanel.CloseFromManager();
            }

            panelStack.Push(panel);
            SetBackgroundBlockerVisible(true);
            BringPanelToFront(panel);
            ApplyConfig(panel);
            panel.OpenFromManager();
            SetBackgroundBlockerVisible(true);
        }

        public void PushOverlay(UIPanel panel)
        {
            if (panel != null && TryDeferOpen(panel, () => PushOverlay(panel))) return;

            PushOverlayImmediate(panel);
        }

        private void PushOverlayImmediate(UIPanel panel)
        {
            if (panel == null || panelStack.Contains(panel))
            {
                return;
            }

            transitionVersion++;
            overlayPanels.Add(panel);
            panelStack.Push(panel);
            SetBackgroundBlockerVisible(true);
            BringPanelToFront(panel);
            PositionBackgroundBlockerBehind(panel);
            ApplyConfig(panel);
            panel.OpenFromManager();
            SetBackgroundBlockerVisible(true);
        }

        public void CloseCurrent()
        {
            TryDismissCurrentFromBackground();
        }

        public void CloseCurrentFromButton()
        {
            TryCloseActivePanel();
        }

        public bool TryCloseCurrent()
        {
            return TryCloseCurrent(null);
        }

        public bool TryCloseCurrent(Action onClosed)
        {
            // A generic dismiss must never release an owned blocking modal.
            if (HasBlockingPanel) return true;

            if (panelStack.Count == 0)
            {
                SetBackgroundBlockerVisible(false);
                return false;
            }

            int closeVersion = ++transitionVersion;
            UIPanel closingPanel = panelStack.Pop();
            bool wasOverlay = closingPanel != null && overlayPanels.Remove(closingPanel);

            if (closingPanel == null)
            {
                OpenPreviousOrHideBlocker(closeVersion, wasOverlay);
                onClosed?.Invoke();
                return true;
            }

            ApplyConfig(closingPanel);
            closingPanelCount++;
            closingPanel.CloseFromManager(true, () =>
            {
                closingPanelCount = Mathf.Max(0, closingPanelCount - 1);
                OpenPreviousOrHideBlocker(closeVersion, wasOverlay);
                onClosed?.Invoke();
            });
            return true;
        }

        public bool TryCloseActivePanel()
        {
            if (closingPanelCount > 0)
            {
                return false;
            }

            return TryCloseCurrent();
        }

        public bool TryClose(UIPanel panel)
        {
            return TryClose(panel, null);
        }

        public bool TryClose(UIPanel panel, Action onClosed)
        {
            if (panel == null)
            {
                return false;
            }

            if (HasBlockingPanel)
            {
                if (panel != blockingPanel)
                    deferredPanelRequests.Enqueue(new DeferredPanelRequest(panel, () => TryClose(panel, onClosed), true));
                return true;
            }

            if (panelStack.Count == 0)
            {
                SetBackgroundBlockerVisible(false);
                return false;
            }

            if (panelStack.Peek() != panel)
            {
                Debug.LogWarning($"{nameof(PanelManager)} cannot close {panel.name} because it is not the current panel.", panel);
                return true;
            }

            return TryCloseCurrent(onClosed);
        }

        public void CloseAll()
        {
            if (HasBlockingPanel && !hierarchyShutdownInProgress) return;
            if (hierarchyShutdownInProgress) ResetBlockingPanelForShutdown();

            transitionVersion++;
            closingPanelCount = 0;

            while (panelStack.Count > 0)
            {
                UIPanel panel = panelStack.Pop();

                if (panel != null)
                {
                    panel.HideImmediately();
                }
            }

            overlayPanels.Clear();
            RestoreBackgroundBlockerPlacement();

            SetBackgroundBlockerVisible(false);
        }

#if UNITY_EDITOR
        public void ResetForDebugLevelLoad()
        {
            if (HasBlockingPanel) return; // The debug caller must respect modal ownership.
            deferredPanelRequests.Clear();
            deferredPresentedPanel = null;
            CloseAll();
            // Include panels already popped from the stack but still closing; cancel
            // their old completion callbacks through UIPanel's existing immediate cleanup.
            foreach (var panel in GetComponentsInChildren<UIPanel>(true))
                if (panel.GetComponentInParent<PanelManager>(true) == this) panel.HideImmediately();
            KillTemporaryMessageSequence(true);
        }
#endif

        public void ShowTemporaryMessage(string message)
        {
            if (TryDeferOpen(null, () => ShowTemporaryMessage(message))) return;

            CacheTemporaryMessageReferences();
            CacheTemporaryMessageStartPosition();
            KillTemporaryMessageSequence(true);

            if (temporaryMessagePanel == null)
            {
                if (!warnedMissingTemporaryMessagePanel)
                {
                    warnedMissingTemporaryMessagePanel = true;
                    Debug.LogWarning($"{nameof(PanelManager)} cannot show temporary message because no message panel is assigned.", this);
                }

                return;
            }

            if (temporaryMessageText != null)
            {
                temporaryMessageText.text = message;
            }
            else if (!warnedMissingTemporaryMessageText)
            {
                warnedMissingTemporaryMessageText = true;
                Debug.LogWarning($"{nameof(PanelManager)} cannot set temporary message text because no TMP_Text is assigned.", this);
            }

            SetBackgroundBlockerVisible(true);
            temporaryMessagePanel.SetActive(true);
            temporaryMessagePanel.transform.SetAsLastSibling();

            if (temporaryMessageCanvasGroup != null)
            {
                temporaryMessageCanvasGroup.alpha = 1f;
            }

            if (temporaryMessageRect != null)
            {
                temporaryMessageRect.anchoredPosition = temporaryMessageStartPosition;
            }

            Sequence sequence = DOTween.Sequence();
            sequence.SetUpdate(true);
            sequence.AppendInterval(Mathf.Max(0f, messageHoldDuration));

            float fadeMoveDuration = Mathf.Max(0f, messageFadeMoveDuration);
            if (temporaryMessageRect != null)
            {
                sequence.Append(temporaryMessageRect.DOAnchorPos(
                    temporaryMessageStartPosition + Vector2.up * Mathf.Max(0f, messageMoveUpDistance),
                    fadeMoveDuration));
            }

            if (temporaryMessageCanvasGroup != null)
            {
                Tween fadeTween = temporaryMessageCanvasGroup.DOFade(0f, fadeMoveDuration);
                if (temporaryMessageRect != null)
                {
                    sequence.Join(fadeTween);
                }
                else
                {
                    sequence.Append(fadeTween);
                }
            }

            temporaryMessageSequence = sequence;
            sequence.OnComplete(() =>
            {
                if (temporaryMessageSequence != sequence)
                {
                    return;
                }

                temporaryMessageSequence = null;
                ResetTemporaryMessageState();
            });
        }

        private void OpenPreviousOrHideBlocker(int closeVersion, bool previousWasKeptVisible = false)
        {
            if (closeVersion != transitionVersion)
            {
                return;
            }

            if (panelStack.Count == 0)
            {
                RestoreBackgroundBlockerPlacement();
                SetBackgroundBlockerVisible(false);
                return;
            }

            UIPanel previousPanel = panelStack.Peek();

            if (previousPanel == null)
            {
                panelStack.Pop();
                OpenPreviousOrHideBlocker(closeVersion, previousWasKeptVisible);
                return;
            }

            if (overlayPanels.Contains(previousPanel))
            {
                PositionBackgroundBlockerBehind(previousPanel);
            }
            else
            {
                RestoreBackgroundBlockerPlacement();
            }

            SetBackgroundBlockerVisible(true);
            BringPanelToFront(previousPanel);
            ApplyConfig(previousPanel);
            if (!previousWasKeptVisible)
            {
                previousPanel.OpenFromManager();
            }
            SetBackgroundBlockerVisible(true);
        }

        private void ApplyConfig(UIPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            panel.SetConfig(uiConfig);
            panel.SetManagerOwner(this);
        }

        private void BringPanelToFront(UIPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            panel.transform.SetAsLastSibling();
        }

        private void SetBackgroundBlockerVisible(bool isVisible)
        {
            CacheBackgroundBlockerReferences();

            if (backgroundBlocker == null)
            {
                if (isVisible && !warnedMissingBackgroundBlocker)
                {
                    warnedMissingBackgroundBlocker = true;
                    Debug.LogWarning($"{nameof(PanelManager)} cannot show background blocker because no blocker is assigned.", this);
                }

                return;
            }

            backgroundBlocker.SetActive(isVisible || externalInputBlockerVisible || HasBlockingPanel);
        }

        private void CacheBackgroundBlockerReferences()
        {
            if (backgroundBlocker == null)
            {
                return;
            }

            if (!hasBackgroundBlockerOriginalPlacement)
            {
                hasBackgroundBlockerOriginalPlacement = true;
            }

            if (backgroundBlockerButton == null)
            {
                backgroundBlockerButton = backgroundBlocker.GetComponent<Button>();
            }

            UpdateBackgroundBlockerInteraction();
        }

        private void KeepBackgroundBlockerAtBack()
        {
            CacheBackgroundBlockerReferences();
            if (backgroundBlocker == null)
            {
                return;
            }

            if (hierarchyShutdownInProgress || !isActiveAndEnabled ||
                !gameObject.activeInHierarchy)
            {
                ScheduleBackgroundBlockerPlacementRestoreIfNeeded();
                return;
            }

            Transform blockerTransform = backgroundBlocker.transform;
            if (blockerTransform.parent != null && blockerTransform.GetSiblingIndex() != 0)
            {
                blockerTransform.SetSiblingIndex(0);
            }
        }

        private void PositionBackgroundBlockerBehind(UIPanel panel)
        {
            CacheBackgroundBlockerReferences();
            if (backgroundBlocker == null || panel == null || panel.transform.parent == null)
            {
                return;
            }

            Canvas overlayCanvas = panel.OverlaySortingCanvas;
            if (overlayCanvas == null || panelCanvas == null)
            {
                Debug.LogError($"{nameof(PanelManager)} requires a serialized overlay Canvas on {panel.name} and its panel Canvas reference.", panel);
                return;
            }

            if (backgroundBlockerOverlayCanvas != overlayCanvas)
            {
                RestoreOverlayCanvasSorting();
                backgroundBlockerOverlayCanvas = overlayCanvas;
                originalOverlayOverrideSorting = overlayCanvas.overrideSorting;
                originalOverlaySortingOrder = overlayCanvas.sortingOrder;
                originalOverlaySortingLayer = overlayCanvas.sortingLayerID;
                originalOverlayCamera = overlayCanvas.worldCamera;
            }

            // Normal overlays supply their own dimmer. Only an owned blocking modal
            // needs the shared full-screen blocker above the still-visible panels.
            // Use Canvas sorting for that case without moving the blocker's hierarchy.
            if (HasBlockingPanel && backgroundBlockerSortingCanvas == null)
            {
                backgroundBlockerSortingCanvas = backgroundBlocker.GetComponent<Canvas>();
                if (backgroundBlockerSortingCanvas == null)
                    backgroundBlockerSortingCanvas = backgroundBlocker.AddComponent<Canvas>();
                if (backgroundBlocker.GetComponent<GraphicRaycaster>() == null)
                    backgroundBlocker.AddComponent<GraphicRaycaster>();
            }
            if (backgroundBlockerSortingCanvas != null)
            {
                backgroundBlockerSortingCanvas.overrideSorting = HasBlockingPanel;
                if (HasBlockingPanel)
                {
                    backgroundBlockerSortingCanvas.sortingLayerID = panelCanvas.sortingLayerID;
                    backgroundBlockerSortingCanvas.sortingOrder = panelCanvas.sortingOrder + 1;
                    backgroundBlockerSortingCanvas.worldCamera = panelCanvas.worldCamera;
                }
            }
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingLayerID = panelCanvas.sortingLayerID;
            overlayCanvas.sortingOrder = panelCanvas.sortingOrder + (HasBlockingPanel ? 2 : 1);
            overlayCanvas.worldCamera = panelCanvas.worldCamera;
            Transform blockerTransform = backgroundBlocker.transform;
            if (blockerTransform.parent != null && blockerTransform.GetSiblingIndex() != 0)
                blockerTransform.SetSiblingIndex(0);
            if (panel.transform.GetSiblingIndex() != panel.transform.parent.childCount - 1)
                panel.transform.SetAsLastSibling();
            backgroundBlockerOverlayPanel = panel;
            restoreBackgroundBlockerPlacementPending = false;
        }

        private void RestoreOverlayCanvasSorting()
        {
            if (backgroundBlockerSortingCanvas != null)
                backgroundBlockerSortingCanvas.overrideSorting = false;
            if (backgroundBlockerOverlayCanvas != null)
            {
                backgroundBlockerOverlayCanvas.overrideSorting = originalOverlayOverrideSorting;
                backgroundBlockerOverlayCanvas.sortingOrder = originalOverlaySortingOrder;
                backgroundBlockerOverlayCanvas.sortingLayerID = originalOverlaySortingLayer;
                backgroundBlockerOverlayCanvas.worldCamera = originalOverlayCamera;
            }
            backgroundBlockerOverlayCanvas = null;
            backgroundBlockerOverlayPanel = null;
            originalOverlayCamera = null;
        }

        private void RestoreBackgroundBlockerPlacement()
        {
            RestoreOverlayCanvasSorting();
            if (!hasBackgroundBlockerOriginalPlacement || backgroundBlocker == null)
            {
                backgroundBlockerOverlayPanel = null;
                return;
            }

            if (hierarchyShutdownInProgress || !isActiveAndEnabled ||
                !gameObject.activeInHierarchy)
            {
                ScheduleBackgroundBlockerPlacementRestoreIfNeeded();
                return;
            }

            Transform blockerTransform = backgroundBlocker.transform;
            if (blockerTransform.parent != null && blockerTransform.GetSiblingIndex() != 0)
            {
                blockerTransform.SetSiblingIndex(0);
            }

            backgroundBlockerOverlayPanel = null;
            restoreBackgroundBlockerPlacementPending = false;
        }

        private void ScheduleBackgroundBlockerPlacementRestoreIfNeeded()
        {
            if (!hasBackgroundBlockerOriginalPlacement || backgroundBlocker == null)
            {
                return;
            }

            Transform blockerTransform = backgroundBlocker.transform;
            bool placementChanged = blockerTransform.GetSiblingIndex() != 0;
            if (!placementChanged)
            {
                backgroundBlockerOverlayPanel = null;
                restoreBackgroundBlockerPlacementPending = false;
                return;
            }

            restoreBackgroundBlockerPlacementPending = true;
            restoreBackgroundBlockerPlacementAfterFrame =
                UnityEngine.Time.frameCount + 1;
        }

        private void UpdateBackgroundBlockerInteraction()
        {
            if (backgroundBlockerButton != null)
            {
                backgroundBlockerButton.interactable = !externalInputBlockerVisible && !HasBlockingPanel;
            }
        }

        private void RegisterBackgroundBlockerButton()
        {
            CacheBackgroundBlockerReferences();

            if (backgroundBlockerButton == null)
            {
                return;
            }

            backgroundBlockerButton.onClick.RemoveListener(HandleBackgroundBlockerClicked);
            backgroundBlockerButton.onClick.AddListener(HandleBackgroundBlockerClicked);
        }

        private void UnregisterBackgroundBlockerButton()
        {
            if (backgroundBlockerButton == null)
            {
                return;
            }

            backgroundBlockerButton.onClick.RemoveListener(HandleBackgroundBlockerClicked);
        }

        private void HandleBackgroundBlockerClicked()
        {
            TryDismissCurrentFromBackground();
        }

        private bool TryDismissCurrentFromBackground()
        {
            if (externalInputBlockerVisible ||
                UnityEngine.Time.frameCount <= suppressBackgroundDismissThroughFrame)
            {
                return false;
            }

            if (panelStack.Count > 0)
            {
                UIPanel currentPanel = panelStack.Peek();
                if (currentPanel != null && !currentPanel.AllowBackgroundDismiss)
                {
                    return false;
                }
            }

            return TryCloseActivePanel();
        }

        private void CacheTemporaryMessageReferences()
        {
            if (temporaryMessagePanel == null)
            {
                return;
            }

            if (temporaryMessageCanvasGroup == null)
            {
                temporaryMessageCanvasGroup = temporaryMessagePanel.GetComponent<CanvasGroup>();
            }

            if (temporaryMessageRect == null)
            {
                temporaryMessageRect = temporaryMessagePanel.transform as RectTransform;
            }
        }

        private void CacheTemporaryMessageStartPosition()
        {
            if (hasTemporaryMessageStartPosition || temporaryMessageRect == null)
            {
                return;
            }

            temporaryMessageStartPosition = temporaryMessageRect.anchoredPosition;
            hasTemporaryMessageStartPosition = true;
        }

        private void KillTemporaryMessageSequence(bool resetVisuals)
        {
            if (temporaryMessageSequence != null)
            {
                temporaryMessageSequence.Kill(false);
                temporaryMessageSequence = null;
            }

            if (resetVisuals)
            {
                ResetTemporaryMessageState();
            }
        }

        private void ResetTemporaryMessageState()
        {
            if (temporaryMessageRect != null && hasTemporaryMessageStartPosition)
            {
                temporaryMessageRect.anchoredPosition = temporaryMessageStartPosition;
            }

            if (temporaryMessageCanvasGroup != null)
            {
                temporaryMessageCanvasGroup.alpha = 1f;
            }

            if (temporaryMessagePanel != null)
            {
                temporaryMessagePanel.SetActive(false);
            }

            SetBackgroundBlockerVisible(IsPanelInputBlocked);
        }
    }
}

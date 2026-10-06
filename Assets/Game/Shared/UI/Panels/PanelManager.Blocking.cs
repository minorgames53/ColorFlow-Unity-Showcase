using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Shared.UI.Panels
{
    public sealed partial class PanelManager
    {
        [Header("Blocking Modal")]
        [SerializeField] private Canvas panelCanvas;

        private readonly struct DeferredPanelRequest
        {
            public readonly UIPanel Panel;
            public readonly Action Open;
            public readonly bool HasPanel;
            public readonly bool IsClose;

            public DeferredPanelRequest(UIPanel panel, Action open, bool isClose = false)
            {
                Panel = panel;
                Open = open;
                HasPanel = panel != null;
                IsClose = isClose;
            }
        }

        private readonly Queue<DeferredPanelRequest> deferredPanelRequests = new Queue<DeferredPanelRequest>();
        private UIPanel blockingPanel;
        private object blockingPanelOwner;
        private bool blockingPanelClosing;
        private UIPanel deferredPresentedPanel;
        private int originalCanvasOrder;
        private bool originalCanvasOverrideSorting;
        private bool canvasOrderOverridden;

        public event Action BlockingPanelStateChanged;
        public bool HasBlockingPanel => blockingPanelOwner != null;
        public UIConfig Config => uiConfig;

        // Call before changing a reusable panel's context. Generic opening paths also
        // enforce this gate, so callers cannot replace the modal by skipping this API.
        public bool TryDeferOpen(UIPanel panel, Action openRequest)
        {
            if (hierarchyShutdownInProgress || openRequest == null) return true;
            if (HasBlockingPanel && panel != null && panel == blockingPanel) return true;
            // Outside the blocking phase retain normal replace/push behavior, including
            // child dialogs opened by a panel that has just come out of the queue.
            if (!HasBlockingPanel) return false;

            deferredPanelRequests.Enqueue(new DeferredPanelRequest(panel, openRequest));
            return true;
        }

        public bool OpenBlockingPanel(UIPanel panel, object owner)
        {
            if (panel == null || owner == null || hierarchyShutdownInProgress || !isActiveAndEnabled)
                return false;
            if (HasBlockingPanel) return blockingPanel == panel && ReferenceEquals(blockingPanelOwner, owner);

            // Keep the current panel's lifecycle/context intact underneath the blocker.
            blockingPanel = panel;
            blockingPanelOwner = owner;
            blockingPanelClosing = false;
            panel.SetAllowBackgroundDismiss(false);
            if (panelCanvas != null)
            {
                originalCanvasOrder = panelCanvas.sortingOrder;
                originalCanvasOverrideSorting = panelCanvas.overrideSorting;
                canvasOrderOverridden = true;
                panelCanvas.overrideSorting = true;
                // Reserve separate orders for the full-screen blocker and modal.
                panelCanvas.sortingOrder = short.MaxValue - 2;
            }
            PushOverlayImmediate(panel);
            KeepBlockingPanelInFront();
            BlockingPanelStateChanged?.Invoke();
            return true;
        }

        public bool CloseBlockingPanel(UIPanel panel, object owner, bool animated = true)
        {
            if (!HasBlockingPanel || panel != blockingPanel || !ReferenceEquals(owner, blockingPanelOwner))
                return false;
            if (panel == null)
            {
                FinishBlockingPanelClose();
                return true;
            }
            if (blockingPanelClosing && animated) return true;
            blockingPanelClosing = true;
            // Keep ownership and the stack entry until the animation really finishes.
            panel.CloseFromManager(animated, FinishBlockingPanelClose);
            return true;
        }

        internal bool IsProtectedBlockingPanel(UIPanel panel)
        {
            return HasBlockingPanel && panel == blockingPanel && !hierarchyShutdownInProgress;
        }

        private void UpdateBlockingPanelAndQueue()
        {
            if (HasBlockingPanel)
            {
                // Includes SetActive(false), a killed closing tween, or component cleanup.
                if (blockingPanel == null || !blockingPanel.gameObject.activeInHierarchy)
                    FinishBlockingPanelClose();
                else
                    KeepBlockingPanelInFront();
                return;
            }

            if (deferredPresentedPanel != null)
            {
                if (ContainsPanel(deferredPresentedPanel) || deferredPresentedPanel.gameObject.activeInHierarchy)
                {
                    // A queued close must not wait for the very panel it is meant to
                    // close. Only close actions bypass this wait; opens remain FIFO.
                    TryRunDeferredCloseForPresentedPanel();
                    return;
                }
                deferredPresentedPanel = null;
            }
            if (closingPanelCount > 0 || deferredPanelRequests.Count == 0 || hierarchyShutdownInProgress)
                return;

            // One request per lifecycle/frame, never instantiate queued panels early.
            DeferredPanelRequest request = deferredPanelRequests.Dequeue();
            if (request.HasPanel && request.Panel == null) return;
            request.Open();
            if (request.Panel != null && (ContainsPanel(request.Panel) || request.Panel.gameObject.activeInHierarchy))
                deferredPresentedPanel = request.Panel;
            else if (!request.HasPanel && panelStack.Count > 0)
                deferredPresentedPanel = panelStack.Peek();
        }

        private void KeepBlockingPanelInFront()
        {
            PositionBackgroundBlockerBehind(blockingPanel);
            SetBackgroundBlockerVisible(true);
            UpdateBackgroundBlockerInteraction();
        }

        private void TryRunDeferredCloseForPresentedPanel()
        {
            if (closingPanelCount > 0 || panelStack.Count == 0 || panelStack.Peek() != deferredPresentedPanel)
                return;
            Action closeRequest = null;
            int count = deferredPanelRequests.Count;
            for (int i = 0; i < count; i++)
            {
                DeferredPanelRequest request = deferredPanelRequests.Dequeue();
                if (closeRequest == null && request.IsClose && request.Panel == deferredPresentedPanel)
                    closeRequest = request.Open;
                else
                    deferredPanelRequests.Enqueue(request);
            }
            closeRequest?.Invoke();
        }

        private void FinishBlockingPanelClose()
        {
            if (!HasBlockingPanel) return;
            if (panelStack.Count > 0 && panelStack.Peek() == blockingPanel) panelStack.Pop();
            overlayPanels.Remove(blockingPanel);
            blockingPanel = null;
            blockingPanelOwner = null;
            blockingPanelClosing = false;
            RestoreModalCanvasOrder();
            bool previousStillVisible = panelStack.Count > 0 && panelStack.Peek() != null &&
                                        panelStack.Peek().gameObject.activeInHierarchy;
            OpenPreviousOrHideBlocker(++transitionVersion, previousStillVisible);
            UpdateBackgroundBlockerInteraction();
            BlockingPanelStateChanged?.Invoke();
        }

        private void RestoreModalCanvasOrder()
        {
            if (!canvasOrderOverridden) return;
            if (panelCanvas != null)
            {
                panelCanvas.sortingOrder = originalCanvasOrder;
                panelCanvas.overrideSorting = originalCanvasOverrideSorting;
            }
            canvasOrderOverridden = false;
        }

        private void ResetBlockingPanelForShutdown()
        {
            deferredPanelRequests.Clear();
            deferredPresentedPanel = null;
            blockingPanelOwner = null;
            blockingPanel = null;
            blockingPanelClosing = false;
            RestoreModalCanvasOrder();
            BlockingPanelStateChanged?.Invoke();
        }
    }
}

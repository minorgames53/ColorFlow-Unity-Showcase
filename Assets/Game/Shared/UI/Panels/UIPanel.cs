using System;
using DG.Tweening;
using Game.Shared.UI;
using UnityEngine;

namespace Game.Shared.UI.Panels
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UIPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform animationTarget;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private bool allowBackgroundDismiss = true;
        [SerializeField] private Canvas overlaySortingCanvas;

        private UIConfig uiConfig;
        private Tween activeTween;
        private bool warnedMissingPanelManager;
        private PanelManager managerOwner;

        public bool AllowBackgroundDismiss => allowBackgroundDismiss;
        internal Canvas OverlaySortingCanvas => overlaySortingCanvas;

        private void Reset()
        {
            animationTarget = transform as RectTransform;
            canvasGroup = GetComponent<CanvasGroup>();
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void OnDisable()
        {
            KillCurrentTween();
        }

        private void OnDestroy()
        {
            KillCurrentTween();
        }

        public void Open(bool animated = true)
        {
            PanelManager panelManager = ResolvePanelManager();
            if (panelManager != null)
            {
                panelManager.OpenRoot(this);
                return;
            }

            if (!warnedMissingPanelManager)
            {
                warnedMissingPanelManager = true;
                Debug.LogWarning($"{nameof(UIPanel)} opened directly because no {nameof(PanelManager)} was found.", this);
            }

            OpenInternal(animated);
        }

        public void Close(bool animated = true, Action onComplete = null)
        {
            PanelManager panelManager = ResolvePanelManager();
            if (panelManager != null && panelManager.TryClose(this, onComplete))
            {
                return;
            }

            CloseInternal(animated, onComplete);
        }

        internal void OpenFromManager(bool animated = true)
        {
            OpenInternal(animated);
        }

        internal void CloseFromManager(bool animated = true, Action onComplete = null)
        {
            CloseInternal(animated, onComplete);
        }

        private void OpenInternal(bool animated)
        {
            gameObject.SetActive(true);
            CacheReferences();
            KillCurrentTween();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            if (animationTarget == null)
            {
                return;
            }

            if (!animated)
            {
                animationTarget.localScale = Vector3.one;
                return;
            }

            animationTarget.localScale = Vector3.one * InitialScale;

            activeTween = DOTween.Sequence()
                .Append(
                    animationTarget
                        .DOScale(Vector3.one * OvershootScale, GrowDuration)
                        .SetEase(GrowEase)
                )
                .Append(
                    animationTarget
                        .DOScale(Vector3.one, SettleDuration)
                        .SetEase(SettleEase)
                )
                .SetUpdate(true)
                .OnComplete(() => activeTween = null);
        }

        private void CloseInternal(bool animated, Action onComplete)
        {
            CacheReferences();
            KillCurrentTween();

            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            if (!animated || animationTarget == null)
            {
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 0f;
                }

                if (animationTarget != null)
                {
                    animationTarget.localScale = Vector3.one;
                }

                gameObject.SetActive(false);
                onComplete?.Invoke();
                return;
            }

            animationTarget.localScale = Vector3.one;

            Sequence closeSequence = DOTween.Sequence()
                .Join(
                    animationTarget
                        .DOScale(Vector3.one * InitialScale, CloseDuration)
                        .SetEase(CloseEase)
                );

            if (canvasGroup != null)
            {
                closeSequence.Join(canvasGroup.DOFade(0f, CloseDuration));
            }

            activeTween = closeSequence
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    activeTween = null;
                    gameObject.SetActive(false);
                    onComplete?.Invoke();
                });
        }

        public void HideImmediately()
        {
            if (managerOwner != null && managerOwner.IsProtectedBlockingPanel(this)) return;

            CacheReferences();
            KillCurrentTween();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            if (animationTarget != null)
            {
                animationTarget.localScale = Vector3.one;
            }

            gameObject.SetActive(false);
        }

        public void SetConfig(UIConfig config)
        {
            uiConfig = config;
        }

        internal void SetManagerOwner(PanelManager manager)
        {
            managerOwner = manager;
        }

        public void SetAllowBackgroundDismiss(bool allow)
        {
            allowBackgroundDismiss = allow;
        }

        private void CacheReferences()
        {
            if (animationTarget == null)
            {
                animationTarget = transform as RectTransform;
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }
        }

        private void KillCurrentTween()
        {
            if (activeTween == null)
            {
                return;
            }

            activeTween.Kill();
            activeTween = null;
        }

        private PanelManager ResolvePanelManager()
        {
            if (managerOwner != null) return managerOwner;

            PanelManager panelManager = GetComponentInParent<PanelManager>(true);
            if (panelManager != null)
            {
                return panelManager;
            }

            return FindFirstObjectByType<PanelManager>(FindObjectsInactive.Include);
        }

        private float InitialScale => uiConfig != null ? uiConfig.PanelInitialScale : 0.95f;
        private float OvershootScale => uiConfig != null ? uiConfig.PanelOvershootScale : 1.10f;
        private float GrowDuration => uiConfig != null ? uiConfig.PanelGrowDuration : 0.14f;
        private float SettleDuration => uiConfig != null ? uiConfig.PanelSettleDuration : 0.10f;
        private float CloseDuration => uiConfig != null ? uiConfig.PanelCloseDuration : 0.10f;
        private Ease GrowEase => uiConfig != null ? uiConfig.PanelGrowEase : Ease.OutQuad;
        private Ease SettleEase => uiConfig != null ? uiConfig.PanelSettleEase : Ease.OutBack;
        private Ease CloseEase => uiConfig != null ? uiConfig.PanelCloseEase : Ease.InQuad;
    }
}

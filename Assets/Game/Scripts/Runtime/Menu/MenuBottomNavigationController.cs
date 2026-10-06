using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Menu
{
    public enum MenuTab
    {
        Home,
        Shop,
        Leaderboard
    }

    [DisallowMultipleComponent]
    public sealed class MenuBottomNavigationController : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private GameObject canvasMain;
        [SerializeField] private GameObject canvasShop;
        [SerializeField] private GameObject canvasNavigation;

        [Header("Navigation")]
        [SerializeField] private RectTransform highlight;
        [SerializeField] private Button shopButton;
        [SerializeField] private RectTransform shopRoot;
        [SerializeField] private Button homeButton;
        [SerializeField] private RectTransform homeRoot;
        [SerializeField] private Button leaderboardButton;
        [SerializeField] private RectTransform leaderboardRoot;

        [Header("Animation")]
        [SerializeField] private float selectedAnchoredY = 25f;
        [SerializeField] private float selectedScaleMultiplier = 1.2f;
        [SerializeField, Min(0f)] private float animationDuration = 0.2f;
        [SerializeField] private Ease animationEase = Ease.OutQuad;
        [SerializeField, Min(1f)] private float highlightScaleMultiplier = 1.1f;
        [SerializeField, Min(0f)] private float highlightScaleDuration = 0.12f;

        private RootTransformState shopDefaultState;
        private RootTransformState homeDefaultState;
        private RootTransformState leaderboardDefaultState;
        private float shopHighlightX;
        private float homeHighlightX;
        private Vector3 highlightDefaultScale;
        private bool defaultsCached;
        private bool stateInitialized;
        private Sequence rootTween;
        private Sequence highlightScaleTween;

        public MenuTab CurrentTab { get; private set; } = MenuTab.Home;

        private void Awake()
        {
            CacheDefaults();
        }

        private void OnEnable()
        {
            CacheDefaults();
            BindButtons();

            CurrentTab = MenuTab.Home;
            stateInitialized = true;
            ApplyContentState(MenuTab.Home);
            ApplyVisualStateImmediate(MenuTab.Home);
        }

        private void OnDisable()
        {
            UnbindButtons();
            KillRootTween();
            KillHighlightScaleTween(true);
            stateInitialized = false;
        }

        private void OnDestroy()
        {
            KillRootTween();
            KillHighlightScaleTween(false);
        }

        public void SelectHome()
        {
            SelectTab(MenuTab.Home);
        }

        public void SelectShop()
        {
            SelectTab(MenuTab.Shop);
        }

        public void SelectTab(MenuTab tab)
        {
            if (tab == MenuTab.Leaderboard ||
                (stateInitialized && tab == CurrentTab))
            {
                return;
            }

            CurrentTab = tab;
            stateInitialized = true;
            ApplyContentState(tab);
            AnimateVisualState(tab);
        }

        private void CacheDefaults()
        {
            if (defaultsCached)
            {
                return;
            }

            shopDefaultState = RootTransformState.Capture(shopRoot);
            homeDefaultState = RootTransformState.Capture(homeRoot);
            leaderboardDefaultState = RootTransformState.Capture(leaderboardRoot);
            highlightDefaultScale = highlight != null
                ? highlight.localScale
                : Vector3.one;

            Canvas.ForceUpdateCanvases();
            shopHighlightX = GetHighlightTargetX(shopButton);
            homeHighlightX = GetHighlightTargetX(homeButton);
            defaultsCached = true;
        }

        private void BindButtons()
        {
            if (shopButton != null)
            {
                shopButton.onClick.RemoveListener(SelectShop);
                shopButton.onClick.AddListener(SelectShop);
            }

            if (homeButton != null)
            {
                homeButton.onClick.RemoveListener(SelectHome);
                homeButton.onClick.AddListener(SelectHome);
            }

            if (leaderboardButton != null)
            {
                leaderboardButton.interactable = false;
            }
        }

        private void UnbindButtons()
        {
            if (shopButton != null)
            {
                shopButton.onClick.RemoveListener(SelectShop);
            }

            if (homeButton != null)
            {
                homeButton.onClick.RemoveListener(SelectHome);
            }
        }

        private void ApplyContentState(MenuTab tab)
        {
            if (canvasNavigation != null && !canvasNavigation.activeSelf)
            {
                canvasNavigation.SetActive(true);
            }

            if (canvasMain != null)
            {
                canvasMain.SetActive(tab == MenuTab.Home);
            }

            if (canvasShop != null)
            {
                canvasShop.SetActive(tab == MenuTab.Shop);
            }
        }

        private void ApplyVisualStateImmediate(MenuTab tab)
        {
            KillRootTween();
            KillHighlightScaleTween(true);

            ApplyRootVisualStateImmediate(tab);
            SetHighlightXImmediate(tab);
        }

        private void ApplyRootVisualStateImmediate(MenuTab tab)
        {

            ApplyRootStateImmediate(
                shopRoot,
                shopDefaultState,
                tab == MenuTab.Shop);
            ApplyRootStateImmediate(
                homeRoot,
                homeDefaultState,
                tab == MenuTab.Home);
            ApplyRootStateImmediate(
                leaderboardRoot,
                leaderboardDefaultState,
                false);
        }

        private void SetHighlightXImmediate(MenuTab tab)
        {
            if (highlight != null)
            {
                Vector2 position = highlight.anchoredPosition;
                position.x = tab == MenuTab.Shop ? shopHighlightX : homeHighlightX;
                highlight.anchoredPosition = position;
            }
        }

        private void AnimateVisualState(MenuTab tab)
        {
            KillRootTween();
            SetHighlightXImmediate(tab);
            RestartHighlightScaleAnimation();

            rootTween = DOTween.Sequence().SetUpdate(true);
            JoinRootAnimation(
                rootTween,
                shopRoot,
                shopDefaultState,
                tab == MenuTab.Shop);
            JoinRootAnimation(
                rootTween,
                homeRoot,
                homeDefaultState,
                tab == MenuTab.Home);
            JoinRootAnimation(
                rootTween,
                leaderboardRoot,
                leaderboardDefaultState,
                false);

            MenuTab targetTab = tab;
            rootTween.OnComplete(() =>
            {
                rootTween = null;
                ApplyRootVisualStateImmediate(targetTab);
            });
        }

        private void RestartHighlightScaleAnimation()
        {
            KillHighlightScaleTween(true);
            if (highlight == null || highlightScaleDuration <= 0f)
            {
                return;
            }

            Vector3 scaleUpTarget = highlightDefaultScale;
            scaleUpTarget.x *= highlightScaleMultiplier;
            scaleUpTarget.y *= highlightScaleMultiplier;

            float halfDuration = highlightScaleDuration * 0.5f;
            highlightScaleTween = DOTween.Sequence()
                .Append(
                    highlight
                        .DOScale(scaleUpTarget, halfDuration)
                        .SetEase(Ease.OutQuad))
                .Append(
                    highlight
                        .DOScale(highlightDefaultScale, halfDuration)
                        .SetEase(Ease.InOutQuad))
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    highlight.localScale = highlightDefaultScale;
                    highlightScaleTween = null;
                });
        }

        private void JoinRootAnimation(
            Sequence sequence,
            RectTransform root,
            RootTransformState defaultState,
            bool selected)
        {
            if (sequence == null || root == null)
            {
                return;
            }

            Vector2 targetPosition = defaultState.AnchoredPosition;
            Vector3 targetScale = defaultState.LocalScale;
            if (selected)
            {
                targetPosition.y = selectedAnchoredY;
                targetScale.x *= selectedScaleMultiplier;
                targetScale.y *= selectedScaleMultiplier;
            }

            sequence.Join(
                root
                    .DOAnchorPos(targetPosition, animationDuration)
                    .SetEase(animationEase));
            sequence.Join(
                root
                    .DOScale(targetScale, animationDuration)
                    .SetEase(animationEase));
        }

        private void ApplyRootStateImmediate(
            RectTransform root,
            RootTransformState defaultState,
            bool selected)
        {
            if (root == null)
            {
                return;
            }

            Vector2 position = defaultState.AnchoredPosition;
            Vector3 scale = defaultState.LocalScale;
            if (selected)
            {
                position.y = selectedAnchoredY;
                scale.x *= selectedScaleMultiplier;
                scale.y *= selectedScaleMultiplier;
            }

            root.anchoredPosition = position;
            root.localScale = scale;
        }

        private float GetHighlightTargetX(Button button)
        {
            if (highlight == null || highlight.parent == null || button == null)
            {
                return highlight != null ? highlight.anchoredPosition.x : 0f;
            }

            RectTransform target = button.transform as RectTransform;
            if (target == null)
            {
                return highlight.anchoredPosition.x;
            }

            Vector3 targetWorldCenter = target.TransformPoint(target.rect.center);
            Vector3 targetInHighlightParent =
                highlight.parent.InverseTransformPoint(targetWorldCenter);
            float anchoredToLocalOffset =
                highlight.anchoredPosition.x - highlight.localPosition.x;
            return targetInHighlightParent.x + anchoredToLocalOffset;
        }

        private void KillRootTween()
        {
            if (rootTween == null)
            {
                return;
            }

            rootTween.Kill(false);
            rootTween = null;
        }

        private void KillHighlightScaleTween(bool restoreDefaultScale)
        {
            if (highlightScaleTween != null)
            {
                highlightScaleTween.Kill(false);
                highlightScaleTween = null;
            }

            if (restoreDefaultScale && highlight != null)
            {
                highlight.localScale = highlightDefaultScale;
            }
        }

        private readonly struct RootTransformState
        {
            private RootTransformState(Vector2 anchoredPosition, Vector3 localScale)
            {
                AnchoredPosition = anchoredPosition;
                LocalScale = localScale;
            }

            public Vector2 AnchoredPosition { get; }
            public Vector3 LocalScale { get; }

            public static RootTransformState Capture(RectTransform root)
            {
                return root != null
                    ? new RootTransformState(root.anchoredPosition, root.localScale)
                    : new RootTransformState(Vector2.zero, Vector3.one);
            }
        }
    }
}

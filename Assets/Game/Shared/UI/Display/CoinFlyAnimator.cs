using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Game.Shared.Save;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.UI
{
    [DisallowMultipleComponent]
    public sealed class CoinFlyAnimator : MonoBehaviour
    {
        [Header("Required References")]
        [SerializeField] private Canvas animationCanvas;
        [SerializeField] private RectTransform animationRoot;
        [SerializeField] private RectTransform coinPrefab;
        [SerializeField] private RectTransform targetGoldHud;
        [SerializeField] private GoldDisplayPresenter goldDisplay;

        [Header("Optional Spawn Point")]
        [Tooltip("Default spawn rect. A source passed to Play takes priority; if both are empty, the Canvas center is used.")]
        [SerializeField] private RectTransform startRect;

        [Header("Presentation")]
        [SerializeField, Range(8, 10)] private int visualCoinCount = 10;
        [SerializeField, Min(0f)] private float spreadRadius = 105f;
        [SerializeField, Min(0f)] private float dipDistance = 32f;
        [SerializeField, Min(0f)] private float popDuration = 0.18f;
        [SerializeField, Min(0f)] private float dipDuration = 0.12f;
        [SerializeField, Min(0f)] private float flyDuration = 0.52f;
        [SerializeField, Min(0f)] private float launchStagger = 0.045f;
        [SerializeField, Min(0f)] private float targetPunchDuration = 0.18f;
        [SerializeField, Min(0f)] private float targetPunchStrength = 0.08f;

        private readonly List<RectTransform> activeCoins = new List<RectTransform>(10);
        private Sequence activeSequence;
        private Tween targetPunchTween;
        private RectTransform activeTarget;
        private GoldDisplayPresenter activeGoldDisplay;
        private Vector3 targetBaseScale;
        private bool hasTargetBaseScale;
        private int animationVersion;
        private int arrivedCoinCount;
        private int presentationStartAmount;
        private int authoritativeFinalAmount;
        private Action activeCompletionCallbacks;
        private bool naturalCompletionPending;

        public bool IsPlaying =>
            activeSequence != null && activeSequence.IsActive() ||
            targetPunchTween != null && targetPunchTween.IsActive() ||
            naturalCompletionPending;

        public void Play(
            int amount,
            RectTransform source = null,
            RectTransform target = null,
            GoldDisplayPresenter displayPresenter = null,
            Action onComplete = null,
            bool playRewardSound = true)
        {
            CancelActiveAnimation();
            activeCompletionCallbacks = onComplete;

            try
            {
                activeTarget = target != null ? target : targetGoldHud;
                activeGoldDisplay = displayPresenter != null ? displayPresenter : goldDisplay;
                if (amount <= 0 || !CanPlay())
                {
                    activeCompletionCallbacks = null;
                    SnapAndReleaseActiveDisplay();
                    return;
                }

                SaveManager saveManager = SaveManager.Instance;
                if (saveManager == null || !saveManager.IsInitialized)
                {
                    activeCompletionCallbacks = null;
                    SnapAndReleaseActiveDisplay();
                    return;
                }

                // Measure once, before arrivals can punch the target's scale.
                if (!TryGetTargetCoinSize(activeTarget, out Vector2 visualCoinSize))
                {
                    activeCompletionCallbacks = null;
                    SnapAndReleaseActiveDisplay();
                    return;
                }

                authoritativeFinalAmount = saveManager.Gold;
                presentationStartAmount = (int)Math.Max(
                    0L,
                    (long)authoritativeFinalAmount - amount);
                arrivedCoinCount = 0;
                int version = ++animationVersion;

                targetBaseScale = activeTarget.localScale;
                hasTargetBaseScale = true;
                activeGoldDisplay.BeginPresentation(presentationStartAmount);

                Vector2 spawnPoint = GetSpawnPoint(source);
                Vector2 targetPoint = WorldToAnimationRootPoint(
                    activeTarget.TransformPoint(activeTarget.rect.center),
                    GetCanvasCamera(activeTarget));

                int coinCount = Mathf.Clamp(visualCoinCount, 8, 10);
                activeSequence = DOTween.Sequence()
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

                for (int i = 0; i < coinCount; i++)
                {
                    RectTransform coin = CreateCoin(spawnPoint, visualCoinSize);
                    if (coin == null)
                    {
                        continue;
                    }

                    activeCoins.Add(coin);
                    Vector2 spreadPoint = spawnPoint + UnityEngine.Random.insideUnitCircle * spreadRadius;
                    Vector2 dipPoint = spreadPoint + Vector2.down * dipDistance;
                    float delay = i * launchStagger;

                    Sequence coinSequence = DOTween.Sequence()
                        .AppendInterval(delay)
                        .Append(coin.DOScale(Vector3.one, popDuration).SetEase(Ease.OutBack))
                        .Join(coin.DOAnchorPos(spreadPoint, popDuration).SetEase(Ease.OutQuad))
                        .Append(coin.DOAnchorPos(dipPoint, dipDuration).SetEase(Ease.InQuad))
                        .Append(coin.DOAnchorPos(targetPoint, flyDuration).SetEase(Ease.InCubic))
                        .OnComplete(() => HandleCoinArrived(version, coin, coinCount));

                    activeSequence.Join(coinSequence);
                }

                if (activeCoins.Count == 0)
                {
                    CancelActiveAnimation();
                    return;
                }

                int spawnedCoinCount = activeCoins.Count;
                activeSequence.OnComplete(() => CompleteAnimation(version, spawnedCoinCount));
                if (playRewardSound)
                {
                    AudioManager.Instance?.PlaySfx(AudioKey.CoinReward);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(CoinFlyAnimator)} on '{name}' cancelled a failed presentation: {exception.Message}",
                    this);
                CancelActiveAnimation();
            }
        }

        public void CancelActiveAnimation()
        {
            animationVersion++;
            naturalCompletionPending = false;
            activeCompletionCallbacks = null;

            activeSequence?.Kill(false);
            activeSequence = null;

            targetPunchTween?.Kill(false);
            targetPunchTween = null;
            RestoreTargetScale();

            for (int i = 0; i < activeCoins.Count; i++)
            {
                if (activeCoins[i] != null)
                {
                    Destroy(activeCoins[i].gameObject);
                }
            }

            activeCoins.Clear();
            arrivedCoinCount = 0;
            SnapAndReleaseActiveDisplay();
        }

        public bool TryAddCompletionCallback(Action onComplete)
        {
            if (onComplete == null || !IsPlaying)
            {
                return false;
            }

            activeCompletionCallbacks += onComplete;
            return true;
        }

        public void RemoveCompletionCallback(Action onComplete)
        {
            activeCompletionCallbacks -= onComplete;
        }

        private void OnDisable()
        {
            CancelActiveAnimation();
        }

        private void OnDestroy()
        {
            CancelActiveAnimation();
        }

        private bool CanPlay()
        {
            if (animationCanvas != null && animationRoot != null && coinPrefab != null &&
                activeTarget != null && activeGoldDisplay != null)
            {
                return true;
            }

            Debug.LogWarning(
                $"{nameof(CoinFlyAnimator)} on '{name}' skipped presentation because its serialized references are incomplete.",
                this);
            return false;
        }

        private RectTransform CreateCoin(Vector2 spawnPoint, Vector2 visualCoinSize)
        {
            RectTransform coin = Instantiate(coinPrefab, animationRoot, false);
            if (coin == null)
            {
                return null;
            }

            coin.name = coinPrefab.name;
            coin.anchorMin = new Vector2(0.5f, 0.5f);
            coin.anchorMax = new Vector2(0.5f, 0.5f);
            coin.pivot = new Vector2(0.5f, 0.5f);
            coin.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, visualCoinSize.x);
            coin.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, visualCoinSize.y);
            coin.anchoredPosition = spawnPoint;
            coin.localRotation = Quaternion.identity;
            coin.localScale = Vector3.zero;

            Canvas nestedCanvas = coin.GetComponent<Canvas>();
            if (nestedCanvas != null)
            {
                nestedCanvas.overrideSorting = false;
            }

            Graphic[] graphics = coin.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                graphics[i].raycastTarget = false;
            }

            return coin;
        }

        private bool TryGetTargetCoinSize(RectTransform target, out Vector2 size)
        {
            // A RectTransform's local size alone cannot account for different Canvas
            // scalers, cameras or parent scales. Project its edges into the same
            // coordinate space in which the flying coins will be rendered.
            Rect rect = target.rect;
            Camera camera = GetCanvasCamera(target);
            Vector2 bottomLeft = WorldToAnimationRootPoint(
                target.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f)), camera);
            Vector2 bottomRight = WorldToAnimationRootPoint(
                target.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f)), camera);
            Vector2 topLeft = WorldToAnimationRootPoint(
                target.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f)), camera);

            size = new Vector2(
                Vector2.Distance(bottomLeft, bottomRight),
                Vector2.Distance(bottomLeft, topLeft));

            // A collapsed/unprojectable target has no usable visual size. Let Play's
            // existing skip path snap the counter rather than spawn oversized coins.
            return size.x > Mathf.Epsilon && size.y > Mathf.Epsilon &&
                   !float.IsNaN(size.x) && !float.IsNaN(size.y) &&
                   !float.IsInfinity(size.x) && !float.IsInfinity(size.y);
        }

        private Vector2 GetSpawnPoint(RectTransform source)
        {
            if (source == null)
            {
                source = startRect;
            }

            if (source != null)
            {
                return WorldToAnimationRootPoint(
                    source.TransformPoint(source.rect.center),
                    GetCanvasCamera(source));
            }

            RectTransform canvasRect = animationCanvas.transform as RectTransform;
            if (canvasRect == null)
            {
                return Vector2.zero;
            }

            return WorldToAnimationRootPoint(
                canvasRect.TransformPoint(canvasRect.rect.center),
                animationCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : animationCanvas.worldCamera);
        }

        private Vector2 WorldToAnimationRootPoint(Vector3 worldPoint, Camera sourceCamera)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(sourceCamera, worldPoint);
            Camera animationCamera = animationCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : animationCanvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                animationRoot,
                screenPoint,
                animationCamera,
                out Vector2 localPoint)
                ? localPoint
                : Vector2.zero;
        }

        private static Camera GetCanvasCamera(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
            Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
            return rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : rootCanvas.worldCamera;
        }

        private void HandleCoinArrived(int version, RectTransform coin, int coinCount)
        {
            if (version != animationVersion || coin == null || !activeCoins.Remove(coin))
            {
                return;
            }

            AudioManager.Instance?.PlaySfx(AudioKey.CoinCollect);
            arrivedCoinCount++;
            float progress = Mathf.Clamp01(arrivedCoinCount / (float)Mathf.Max(1, coinCount));
            long delta = (long)authoritativeFinalAmount - presentationStartAmount;
            int displayedAmount = (int)Math.Min(
                int.MaxValue,
                presentationStartAmount + (long)Math.Round(delta * (double)progress));
            activeGoldDisplay.SetPresentationAmount(displayedAmount);
            PunchTarget(version);

            Destroy(coin.gameObject);
        }

        private void PunchTarget(int version)
        {
            targetPunchTween?.Kill(false);
            RectTransform punchedTarget = activeTarget;
            if (punchedTarget == null)
            {
                return;
            }

            punchedTarget.localScale = targetBaseScale;
            targetPunchTween = punchedTarget
                .DOPunchScale(targetBaseScale * targetPunchStrength, targetPunchDuration, 5, 0.55f)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    if (version == animationVersion)
                    {
                        if (punchedTarget != null)
                        {
                            punchedTarget.localScale = targetBaseScale;
                        }

                        targetPunchTween = null;
                        ReleaseCompletedTarget();
                    }
                });
        }

        private void CompleteAnimation(int version, int spawnedCoinCount)
        {
            if (version != animationVersion)
            {
                return;
            }

            activeSequence = null;
            if (arrivedCoinCount < spawnedCoinCount)
            {
                activeGoldDisplay.SetPresentationAmount(authoritativeFinalAmount);
            }

            SnapAndReleaseActiveDisplay();
            activeCoins.Clear();
            naturalCompletionPending = true;
            ReleaseCompletedTarget();
        }

        private void SnapAndReleaseActiveDisplay()
        {
            activeGoldDisplay?.SnapToAuthoritativeAmount();
            activeGoldDisplay = null;
        }

        private void ReleaseCompletedTarget()
        {
            if (activeSequence != null || targetPunchTween != null)
            {
                return;
            }

            hasTargetBaseScale = false;
            activeTarget = null;
            if (!naturalCompletionPending)
            {
                return;
            }

            naturalCompletionPending = false;
            Action completion = activeCompletionCallbacks;
            activeCompletionCallbacks = null;
            try
            {
                completion?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(CoinFlyAnimator)} on '{name}' completion callback failed: {exception.Message}",
                    this);
            }
        }

        private void RestoreTargetScale()
        {
            if (hasTargetBaseScale && activeTarget != null)
            {
                activeTarget.localScale = targetBaseScale;
            }

            hasTargetBaseScale = false;
            activeTarget = null;
        }
    }
}

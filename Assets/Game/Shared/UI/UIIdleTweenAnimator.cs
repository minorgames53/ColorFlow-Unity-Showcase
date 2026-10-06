using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class UIIdleTweenAnimator : MonoBehaviour
    {
        private enum ScaleIdleMode
        {
            None,
            PulseLoop,
            BounceEveryInterval,
            PunchEveryInterval
        }

        [Header("Target")]
        [SerializeField] private RectTransform target;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool resetOnDisable = true;

        [Header("Start Delay")]
        [SerializeField] private bool useStartDelay = true;
        [SerializeField] private float startDelay = 0.5f;
        [SerializeField] private bool randomizeStartDelay;
        [SerializeField] private Vector2 randomStartDelayRange = new Vector2(0f, 1f);

        [Header("Scale Idle")]
        [SerializeField] private ScaleIdleMode scaleMode = ScaleIdleMode.BounceEveryInterval;

        [Header("Pulse Loop - No Wait")]
        [SerializeField] private float pulseScale = 1.06f;
        [SerializeField] private float pulseDuration = 0.65f;
        [SerializeField] private Ease pulseEase = Ease.InOutSine;

        [Header("Bounce Every Interval")]
        [SerializeField] private float bounceInterval = 2f;
        [SerializeField] private float bounceScale = 1.14f;
        [SerializeField] private float bounceUpDuration = 0.16f;
        [SerializeField] private float bounceDownDuration = 0.22f;
        [SerializeField] private Ease bounceUpEase = Ease.OutBack;
        [SerializeField] private Ease bounceDownEase = Ease.OutQuad;

        [Header("Punch Every Interval")]
        [SerializeField] private float punchInterval = 2f;
        [SerializeField] private float punchStrength = 0.18f;
        [SerializeField] private float punchDuration = 0.35f;
        [SerializeField] private int punchVibrato = 8;
        [SerializeField] private float punchElasticity = 0.8f;

        [Header("Vertical Float Loop")]
        [SerializeField] private bool enableVerticalFloat;
        [SerializeField] private float floatDistance = 18f;
        [SerializeField] private float floatDuration = 0.85f;
        [SerializeField] private Ease floatEase = Ease.InOutSine;

        [Header("Rotation Wiggle")]
        [SerializeField] private bool enableRotationWiggle;
        [SerializeField] private float wiggleInterval = 2.5f;
        [SerializeField] private float wiggleAngle = 7f;
        [SerializeField] private float wiggleDuration = 0.35f;
        [SerializeField] private int wiggleVibrato = 8;
        [SerializeField] private float wiggleElasticity = 0.6f;

        [Header("Fade Pulse")]
        [SerializeField] private bool enableFadePulse;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float minAlpha = 0.75f;
        [SerializeField] private float fadeDuration = 0.7f;
        [SerializeField] private Ease fadeEase = Ease.InOutSine;

        private Vector3 initialScale;
        private Vector3 initialEulerAngles;
        private Vector2 initialAnchoredPosition;
        private float initialAlpha = 1f;

        private Sequence scaleSequence;
        private Sequence positionSequence;
        private Sequence rotationSequence;
        private Sequence fadeSequence;

        private void Reset()
        {
            target = transform as RectTransform;
            canvasGroup = GetComponent<CanvasGroup>();
        }

        private void Awake()
        {
            if (target == null)
                target = transform as RectTransform;

            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            CacheInitialValues();
        }

        private void OnEnable()
        {
            CacheInitialValues();

            if (playOnEnable)
                Play();
        }

        private void OnDisable()
        {
            KillTweens();

            if (resetOnDisable)
                ResetToInitialValues();
        }

        [Button]
        public void Play()
        {
            if (target == null)
                return;

            KillTweens();

            float delay = GetStartDelay();

            PlayScaleAnimation(delay);

            if (enableVerticalFloat)
                PlayVerticalFloat(delay);

            if (enableRotationWiggle)
                PlayRotationWiggle(delay);

            if (enableFadePulse)
                PlayFadePulse(delay);
        }

        [Button]
        public void Stop()
        {
            KillTweens();
            ResetToInitialValues();
        }

        private void PlayScaleAnimation(float delay)
        {
            switch (scaleMode)
            {
                case ScaleIdleMode.None:
                    return;

                case ScaleIdleMode.PulseLoop:
                    // Pulse loop özellikle beklemesiz çalışır.
                    scaleSequence = DOTween.Sequence();
                    scaleSequence
                        .SetUpdate(useUnscaledTime)
                        .Append(target.DOScale(initialScale * pulseScale, pulseDuration).SetEase(pulseEase))
                        .Append(target.DOScale(initialScale, pulseDuration).SetEase(pulseEase))
                        .SetLoops(-1, LoopType.Restart);
                    break;

                case ScaleIdleMode.BounceEveryInterval:
                    scaleSequence = DOTween.Sequence();
                    scaleSequence
                        .SetUpdate(useUnscaledTime)
                        .SetDelay(delay)
                        .AppendInterval(bounceInterval)
                        .Append(target.DOScale(initialScale * bounceScale, bounceUpDuration).SetEase(bounceUpEase))
                        .Append(target.DOScale(initialScale, bounceDownDuration).SetEase(bounceDownEase))
                        .SetLoops(-1, LoopType.Restart);
                    break;

                case ScaleIdleMode.PunchEveryInterval:
                    scaleSequence = DOTween.Sequence();
                    scaleSequence
                        .SetUpdate(useUnscaledTime)
                        .SetDelay(delay)
                        .AppendInterval(punchInterval)
                        .Append(target.DOPunchScale(
                            Vector3.one * punchStrength,
                            punchDuration,
                            punchVibrato,
                            punchElasticity))
                        .SetLoops(-1, LoopType.Restart);
                    break;
            }
        }

        private void PlayVerticalFloat(float delay)
        {
            Vector2 upPosition = initialAnchoredPosition + Vector2.up * floatDistance;

            positionSequence = DOTween.Sequence();
            positionSequence
                .SetUpdate(useUnscaledTime)
                .SetDelay(delay)
                .Append(target.DOAnchorPos(upPosition, floatDuration).SetEase(floatEase))
                .Append(target.DOAnchorPos(initialAnchoredPosition, floatDuration).SetEase(floatEase))
                .SetLoops(-1, LoopType.Restart);
        }

        private void PlayRotationWiggle(float delay)
        {
            rotationSequence = DOTween.Sequence();
            rotationSequence
                .SetUpdate(useUnscaledTime)
                .SetDelay(delay)
                .AppendInterval(wiggleInterval)
                .Append(target.DOPunchRotation(
                    new Vector3(0f, 0f, wiggleAngle),
                    wiggleDuration,
                    wiggleVibrato,
                    wiggleElasticity))
                .SetLoops(-1, LoopType.Restart);
        }

        private void PlayFadePulse(float delay)
        {
            if (canvasGroup == null)
            {
                Debug.LogWarning($"{nameof(UIIdleTweenAnimator)} on {name} has Fade Pulse enabled but no CanvasGroup was assigned.");
                return;
            }

            fadeSequence = DOTween.Sequence();
            fadeSequence
                .SetUpdate(useUnscaledTime)
                .SetDelay(delay)
                .Append(canvasGroup.DOFade(minAlpha, fadeDuration).SetEase(fadeEase))
                .Append(canvasGroup.DOFade(initialAlpha, fadeDuration).SetEase(fadeEase))
                .SetLoops(-1, LoopType.Restart);
        }

        private float GetStartDelay()
        {
            if (!useStartDelay)
                return 0f;

            if (!randomizeStartDelay)
                return Mathf.Max(0f, startDelay);

            float min = Mathf.Min(randomStartDelayRange.x, randomStartDelayRange.y);
            float max = Mathf.Max(randomStartDelayRange.x, randomStartDelayRange.y);

            return Random.Range(min, max);
        }

        private void CacheInitialValues()
        {
            if (target != null)
            {
                initialScale = target.localScale;
                initialEulerAngles = target.localEulerAngles;
                initialAnchoredPosition = target.anchoredPosition;
            }

            if (canvasGroup != null)
                initialAlpha = canvasGroup.alpha;
        }

        private void ResetToInitialValues()
        {
            if (target != null)
            {
                target.localScale = initialScale;
                target.localEulerAngles = initialEulerAngles;
                target.anchoredPosition = initialAnchoredPosition;
            }

            if (canvasGroup != null)
                canvasGroup.alpha = initialAlpha;
        }

        private void KillTweens()
        {
            scaleSequence?.Kill();
            positionSequence?.Kill();
            rotationSequence?.Kill();
            fadeSequence?.Kill();

            scaleSequence = null;
            positionSequence = null;
            rotationSequence = null;
            fadeSequence = null;

            if (target != null)
                DOTween.Kill(target);

            if (canvasGroup != null)
                DOTween.Kill(canvasGroup);
        }
    }
}
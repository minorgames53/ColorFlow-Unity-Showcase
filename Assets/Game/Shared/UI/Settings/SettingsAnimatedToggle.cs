using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Shared.UI.Settings
{
    [DisallowMultipleComponent]
    public sealed class SettingsAnimatedToggle : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image backgroundBase;
        [SerializeField] private Image backgroundTransition;
        [SerializeField] private RectTransform knob;

        [SerializeField] private float enabledKnobX = 65f;
        [SerializeField] private float disabledKnobX = -65f;

        [SerializeField] private float tweenDuration = 0.20f;
        [SerializeField] private Ease tweenEase = Ease.OutQuad;

        private Sprite enabledBackgroundSprite;
        private Sprite disabledBackgroundSprite;
        private Action<bool> onValueChanged;
        private Tween activeTween;
        private bool currentValue;
        private bool isInitialized;

        public bool Value => currentValue;

        private void OnDisable()
        {
            KillCurrentTween();
        }

        private void OnDestroy()
        {
            KillCurrentTween();
        }

        public void Initialize(
            Sprite enabledBackgroundSprite,
            Sprite disabledBackgroundSprite,
            bool initialValue,
            Action<bool> onValueChanged)
        {
            this.enabledBackgroundSprite = enabledBackgroundSprite;
            this.disabledBackgroundSprite = disabledBackgroundSprite;
            this.onValueChanged = onValueChanged;
            isInitialized = true;

            ApplyValue(initialValue, false, false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Toggle();
        }

        public void Toggle()
        {
            if (!isInitialized)
            {
                return;
            }

            SetValue(!currentValue, true);
        }

        public void SetValue(bool value, bool animated)
        {
            ApplyValue(value, animated, true);
        }

        public void SetValueWithoutNotify(bool value, bool animated)
        {
            ApplyValue(value, animated, false);
        }

        private void ApplyValue(bool value, bool animated, bool notify)
        {
            bool valueChanged = currentValue != value;
            currentValue = value;

            Sprite targetSprite = currentValue ? enabledBackgroundSprite : disabledBackgroundSprite;
            float targetKnobX = currentValue ? enabledKnobX : disabledKnobX;

            KillCurrentTween();

            if (notify && valueChanged)
            {
                onValueChanged?.Invoke(currentValue);
            }

            if (!animated)
            {
                SetBaseSprite(targetSprite);
                SetTransitionAlpha(0f);
                SetKnobX(targetKnobX);

                return;
            }

            if (backgroundTransition != null)
            {
                backgroundTransition.sprite = targetSprite;
                SetTransitionAlpha(0f);
            }
            else
            {
                SetBaseSprite(targetSprite);
            }

            Sequence sequence = DOTween.Sequence();

            if (backgroundTransition != null)
            {
                sequence.Join(backgroundTransition.DOFade(1f, tweenDuration).SetEase(tweenEase));
            }

            if (knob != null)
            {
                sequence.Join(knob.DOAnchorPosX(targetKnobX, tweenDuration).SetEase(tweenEase));
            }

            activeTween = sequence
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    activeTween = null;
                    SetBaseSprite(targetSprite);
                    SetTransitionAlpha(0f);
                });
        }

        private void SetBaseSprite(Sprite sprite)
        {
            if (backgroundBase == null)
            {
                return;
            }

            backgroundBase.sprite = sprite;
        }

        private void SetTransitionAlpha(float alpha)
        {
            if (backgroundTransition == null)
            {
                return;
            }

            Color color = backgroundTransition.color;
            color.a = alpha;
            backgroundTransition.color = color;
        }

        private void SetKnobX(float x)
        {
            if (knob == null)
            {
                return;
            }

            Vector2 position = knob.anchoredPosition;
            position.x = x;
            knob.anchoredPosition = position;
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
    }
}

using System;
using DG.Tweening;
using UnityEngine;

namespace Gameplay.BoardFeatures.KeyLocks
{
    public sealed class LockView : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField, Min(0f)] private float disappearDuration = 0.12f;
        [SerializeField] private Ease disappearEase = Ease.InBack;

        [Header("Unlock Impact")]
        [SerializeField, Min(0f)] private float shakeDuration = 0.1f;
        [SerializeField, Min(0f)] private float shakeStrength = 0.035f;
        [SerializeField, Min(1)] private int shakeVibrato = 8;
        [SerializeField, Range(0f, 90f)] private float shakeRandomness = 25f;
        [SerializeField, Min(1f)] private float keyPopScaleMultiplier = 1.2f;
        [SerializeField, Range(0f, 1f)] private float keySettleScaleMultiplier = 0.9f;
        [SerializeField, Min(0f)] private float keyPopDuration = 0.06f;
        [SerializeField, Min(0f)] private float keySettleDuration = 0.05f;
        [SerializeField, Min(0f)] private float keyConsumeDuration = 0.1f;
        [SerializeField, Min(1f)] private float lockScaleUpMultiplier = 1.15f;
        [SerializeField, Min(0f)] private float lockScaleUpDuration = 0.08f;

        private Vector3 authoredScale;
        private Vector3 authoredLocalPosition;
        private Sequence disappearTween;
        private KeyView activeKeyView;

        public bool HasRequiredReferences => view != null;

        private void OnValidate()
        {
            disappearDuration = Mathf.Max(0f, disappearDuration);
            shakeDuration = Mathf.Max(0f, shakeDuration);
            shakeStrength = Mathf.Max(0f, shakeStrength);
            shakeVibrato = Mathf.Max(1, shakeVibrato);
            shakeRandomness = Mathf.Clamp(shakeRandomness, 0f, 90f);
            keyPopScaleMultiplier = Mathf.Max(1f, keyPopScaleMultiplier);
            keySettleScaleMultiplier = Mathf.Clamp01(keySettleScaleMultiplier);
            keyPopDuration = Mathf.Max(0f, keyPopDuration);
            keySettleDuration = Mathf.Max(0f, keySettleDuration);
            keyConsumeDuration = Mathf.Max(0f, keyConsumeDuration);
            lockScaleUpMultiplier = Mathf.Max(1f, lockScaleUpMultiplier);
            lockScaleUpDuration = Mathf.Max(0f, lockScaleUpDuration);
        }

        public void Initialize()
        {
            disappearTween?.Kill(false);
            authoredScale = view != null ? view.localScale : Vector3.one;
            authoredLocalPosition = view != null ? view.localPosition : Vector3.zero;
            if (view != null)
            {
                view.localScale = authoredScale;
                view.localPosition = authoredLocalPosition;
            }
        }

        public void PlayUnlockImpact(KeyView keyView, Action onCompleted)
        {
            KillTween();
            if (view == null || keyView?.ViewTransform == null)
            {
                if (view != null)
                {
                    view.localScale = Vector3.zero;
                }

                onCompleted?.Invoke();
                return;
            }

            activeKeyView = keyView;
            Transform keyVisual = keyView.ViewTransform;
            Vector3 keyScale = keyView.AuthoredViewScale;
            view.localPosition = authoredLocalPosition;
            view.localScale = authoredScale;
            keyView.ResetVisualScale();

            disappearTween = DOTween.Sequence()
                .Append(keyVisual.DOScale(keyScale * keyPopScaleMultiplier, keyPopDuration).SetEase(Ease.OutQuad))
                .Join(view.DOShakePosition(
                    shakeDuration,
                    shakeStrength,
                    shakeVibrato,
                    shakeRandomness,
                    false,
                    true))
                .Append(keyVisual.DOScale(keyScale * keySettleScaleMultiplier, keySettleDuration).SetEase(Ease.InQuad))
                .Append(view.DOScale(authoredScale * lockScaleUpMultiplier, lockScaleUpDuration).SetEase(Ease.OutQuad))
                .Append(view.DOScale(Vector3.zero, disappearDuration).SetEase(disappearEase))
                .Join(keyVisual.DOScale(Vector3.zero, keyConsumeDuration).SetEase(Ease.InQuad))
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    disappearTween = null;
                    activeKeyView = null;
                    view.localPosition = authoredLocalPosition;
                    onCompleted?.Invoke();
                });
        }

        public void KillTween()
        {
            disappearTween?.Kill(false);
            disappearTween = null;
            if (activeKeyView != null)
            {
                activeKeyView.ResetVisualScale();
            }

            activeKeyView = null;
            if (view != null)
            {
                view.localPosition = authoredLocalPosition;
                view.localScale = authoredScale;
            }
        }

        private void OnDestroy()
        {
            KillTween();
        }

        private void OnDisable()
        {
            KillTween();
        }
    }
}

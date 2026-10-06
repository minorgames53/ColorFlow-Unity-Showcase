using System;
using DG.Tweening;
using UnityEngine;

namespace Gameplay.BoardFeatures.KeyLocks
{
    public sealed class KeyView : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField, Min(0f)] private float sourceToWaitingDuration = 0.32f;
        [SerializeField, Min(0f)] private float waitingToTargetDuration = 0.27f;
        [SerializeField, Range(0f, 0.5f)] private float waitingRadius = 0.5f;

        [Header("Target Approach")]
        [SerializeField, Range(0.15f, 0.2f)] private float finalApproachDistanceRatio = 0.18f;
        [SerializeField, Range(0.05f, 0.25f)] private float finalApproachDurationRatio = 0.12f;
        [SerializeField, Range(0.8f, 1f)] private float approachScaleMultiplier = 0.92f;
        [SerializeField] private Ease approachEase = Ease.InQuad;
        [SerializeField] private Ease finalApproachEase = Ease.InQuad;

        private Vector3 authoredViewScale;
        private bool isInitialized;

        public float SourceToWaitingDuration => sourceToWaitingDuration;
        public float WaitingToTargetDuration => waitingToTargetDuration;
        public float WaitingRadius => waitingRadius;
        public bool HasRequiredReferences => view != null;
        public Transform ViewTransform => view;
        public Vector3 AuthoredViewScale => authoredViewScale;

        public void Initialize()
        {
            if (view == null)
            {
                return;
            }

            authoredViewScale = view.localScale;
            isInitialized = true;
            view.localScale = authoredViewScale;
        }

        public Tween PlayTargetApproach(Vector3 targetPosition, Action onReached)
        {
            EnsureInitialized();
            if (view == null || waitingToTargetDuration <= 0f)
            {
                transform.position = targetPosition;
                ResetVisualScale();
                onReached?.Invoke();
                return null;
            }

            Vector3 startPosition = transform.position;
            Vector3 approachPosition = Vector3.Lerp(
                startPosition,
                targetPosition,
                1f - finalApproachDistanceRatio);
            float finalDuration = waitingToTargetDuration * finalApproachDurationRatio;
            float approachDuration = Mathf.Max(0f, waitingToTargetDuration - finalDuration);

            Sequence sequence = DOTween.Sequence();
            sequence.Append(transform.DOMove(approachPosition, approachDuration).SetEase(approachEase));
            sequence.Join(view.DOScale(authoredViewScale * approachScaleMultiplier, approachDuration).SetEase(approachEase));
            sequence.Append(transform.DOMove(targetPosition, finalDuration).SetEase(finalApproachEase));
            sequence.Join(view.DOScale(authoredViewScale, finalDuration).SetEase(Ease.OutQuad));
            sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.OnComplete(() =>
            {
                ResetVisualScale();
                onReached?.Invoke();
            });
            return sequence;
        }

        public void ResetVisualScale()
        {
            EnsureInitialized();
            if (view != null)
            {
                view.localScale = authoredViewScale;
            }
        }

        private void OnValidate()
        {
            sourceToWaitingDuration = Mathf.Max(0f, sourceToWaitingDuration);
            waitingToTargetDuration = Mathf.Max(0f, waitingToTargetDuration);
            waitingRadius = Mathf.Clamp(waitingRadius, 0f, 0.5f);
            finalApproachDistanceRatio = Mathf.Clamp(finalApproachDistanceRatio, 0.15f, 0.2f);
            finalApproachDurationRatio = Mathf.Clamp(finalApproachDurationRatio, 0.05f, 0.25f);
            approachScaleMultiplier = Mathf.Clamp(approachScaleMultiplier, 0.8f, 1f);
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                Initialize();
            }
        }
    }
}

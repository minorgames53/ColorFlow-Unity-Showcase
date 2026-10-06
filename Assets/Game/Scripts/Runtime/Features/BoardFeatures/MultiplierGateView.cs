using DG.Tweening;
using UnityEngine;

namespace Gameplay.BoardFeatures.MultiplierGates
{
    public sealed class MultiplierGateView : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField] private Collider2D gateCollider;
        [SerializeField] private MultiplierGateTrigger trigger;

        [Header("Marble Pulse")]
        [SerializeField, Min(1f)] private float pulseScaleMultiplier = 1.1f;
        [SerializeField, Min(0.01f)] private float pulseScaleUpDuration = 0.06f;
        [SerializeField, Min(0.01f)] private float pulseScaleDownDuration = 0.08f;
        [SerializeField] private Ease pulseScaleUpEase = Ease.OutQuad;
        [SerializeField] private Ease pulseScaleDownEase = Ease.InQuad;

        private Vector3 originalViewScale;
        private Sequence pulseSequence;
        private bool hasCapturedViewScale;

        public bool HasRequiredReferences => view != null && gateCollider != null && trigger != null && gateCollider.isTrigger;

        public bool Initialize(MultiplierGateBoardController controller, int runtimeId)
        {
            if (!HasRequiredReferences || controller == null)
            {
                Debug.LogError($"{nameof(MultiplierGateView)} on '{name}' is missing View, trigger Collider2D, or MultiplierGateTrigger references.", this);
                return false;
            }

            StopPulseAndReset();
            originalViewScale = view.localScale;
            hasCapturedViewScale = true;
            trigger.Initialize(controller, runtimeId, this);
            return true;
        }

        public void PlayMarblePulse()
        {
            if (view == null || !hasCapturedViewScale)
            {
                return;
            }

            pulseSequence?.Kill(false);
            pulseSequence = null;
            view.localScale = originalViewScale;

            Sequence sequence = DOTween.Sequence();
            pulseSequence = sequence;
            sequence
                .Append(view.DOScale(originalViewScale * pulseScaleMultiplier, pulseScaleUpDuration)
                    .SetEase(pulseScaleUpEase))
                .Append(view.DOScale(originalViewScale, pulseScaleDownDuration)
                    .SetEase(pulseScaleDownEase))
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() => CompletePulse(sequence))
                .OnKill(() => ClearPulseReference(sequence));
        }

        private void OnValidate()
        {
            pulseScaleMultiplier = Mathf.Max(1f, pulseScaleMultiplier);
            pulseScaleUpDuration = Mathf.Max(0.01f, pulseScaleUpDuration);
            pulseScaleDownDuration = Mathf.Max(0.01f, pulseScaleDownDuration);
        }

        private void OnDisable()
        {
            StopPulseAndReset();
        }

        private void OnDestroy()
        {
            StopPulseAndReset();
        }

        private void CompletePulse(Sequence sequence)
        {
            if (pulseSequence != sequence)
            {
                return;
            }

            pulseSequence = null;
            if (view != null)
            {
                view.localScale = originalViewScale;
            }
        }

        private void ClearPulseReference(Sequence sequence)
        {
            if (pulseSequence == sequence)
            {
                pulseSequence = null;
            }
        }

        private void StopPulseAndReset()
        {
            pulseSequence?.Kill(false);
            pulseSequence = null;
            if (view != null && hasCapturedViewScale)
            {
                view.localScale = originalViewScale;
            }
        }
    }
}

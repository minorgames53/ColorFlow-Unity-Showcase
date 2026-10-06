using System;
using DG.Tweening;
using UnityEngine;

namespace Gameplay.TargetBoxes
{
    public sealed class TargetBoxRevealView : MonoBehaviour
    {
        [SerializeField] private GameObject activeVisual;
        [SerializeField] private Transform passiveCover;

        [SerializeField] private Vector3 closedScale = Vector3.one;
        [SerializeField] private Vector3 revealedScale = Vector3.zero;
        [SerializeField, Min(0f)] private float revealDuration = 0.2f;
        [SerializeField] private Ease revealEase = Ease.InBack;

        private Tween activeTween;
        private bool isRevealed;
        private bool isRevealing;

        private void Reset()
        {
            activeVisual = transform.Find("ActiveViewRoot")?.gameObject;
            Transform passiveRoot = transform.Find("PassiveViewRoot");
            passiveCover = passiveRoot != null ? passiveRoot : transform.Find("PassiveCover");
        }

        private void OnDisable()
        {
            KillTween();
            isRevealing = false;
        }

        private void OnValidate()
        {
            revealDuration = Mathf.Max(0f, revealDuration);
        }

        public void SetRevealedImmediate(bool revealed, bool keepActiveVisualWhileCovered = true)
        {
            KillTween();
            isRevealing = false;
            isRevealed = revealed;

            if (activeVisual != null)
            {
                activeVisual.SetActive(revealed || keepActiveVisualWhileCovered);
            }

            if (passiveCover == null)
            {
                return;
            }

            passiveCover.gameObject.SetActive(!revealed);
            passiveCover.localScale = revealed ? revealedScale : closedScale;
        }

        public void Reveal(Action onCompleted = null)
        {
            if (isRevealed)
            {
                onCompleted?.Invoke();
                return;
            }

            if (isRevealing)
            {
                return;
            }

            if (!ValidateReferences())
            {
                onCompleted?.Invoke();
                return;
            }

            isRevealing = true;
            activeVisual.SetActive(true);
            passiveCover.gameObject.SetActive(true);
            passiveCover.localScale = closedScale;

            activeTween = passiveCover
                .DOScale(revealedScale, revealDuration)
                .SetEase(revealEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    activeTween = null;
                    isRevealing = false;
                    isRevealed = true;

                    if (passiveCover != null)
                    {
                        passiveCover.gameObject.SetActive(false);
                    }

                    onCompleted?.Invoke();
                });
        }

        public bool TryCompleteReveal()
        {
            if (!isRevealing || activeTween == null || !activeTween.IsActive())
            {
                return false;
            }

            activeTween.Complete(true);
            return true;
        }

        private bool ValidateReferences()
        {
            bool isValid = true;

            if (activeVisual == null)
            {
                Debug.LogError($"{nameof(TargetBoxRevealView)} on '{name}' is missing ActiveVisual reference.", this);
                isValid = false;
            }

            if (passiveCover == null)
            {
                Debug.LogError($"{nameof(TargetBoxRevealView)} on '{name}' is missing PassiveCover reference.", this);
                isValid = false;
            }

            return isValid;
        }

        private void KillTween()
        {
            if (activeTween == null)
            {
                return;
            }

            activeTween.Kill(false);
            activeTween = null;
        }
    }
}

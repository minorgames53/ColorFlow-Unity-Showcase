using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Gameplay.BoardFeatures.Panels
{
    [DisallowMultipleComponent]
    public sealed class PanelView : MonoBehaviour
    {
        public const float DefaultSourceBoxRevealDuration = 0.22f;

        private readonly struct CounterUpdate
        {
            public readonly int Remaining;
            public readonly Action FinalPresentationCompleted;

            public CounterUpdate(int remaining, Action finalPresentationCompleted)
            {
                Remaining = remaining;
                FinalPresentationCompleted = finalPresentationCompleted;
            }
        }

        [Header("References")]
        [SerializeField] private Transform visual;
        [SerializeField] private TMP_Text numberText;

        [Header("Number Pulse")]
        [SerializeField, Min(1f)] private float pulseScaleMultiplier = 1.15f;
        [SerializeField, Min(0f)] private float pulseScaleUpDuration = 0.08f;
        [SerializeField, Min(0f)] private float pulseScaleDownDuration = 0.09f;
        [SerializeField] private Ease pulseScaleUpEase = Ease.OutQuad;
        [SerializeField] private Ease pulseScaleDownEase = Ease.InOutQuad;
        [SerializeField, Range(0f, 0.08f)] private float zeroHoldDuration = 0.04f;

        [Header("Final Open")]
        [SerializeField, Min(1f)] private float finalScaleUpMultiplier = 1.1f;
        [SerializeField, Min(0f)] private float finalScaleUpDuration = 0.12f;
        [SerializeField, Min(0f)] private float finalScaleDownDuration = 0.2f;
        [SerializeField] private Ease finalScaleUpEase = Ease.OutQuad;
        [SerializeField] private Ease finalScaleDownEase = Ease.InBack;

        private readonly Queue<CounterUpdate> pendingUpdates = new Queue<CounterUpdate>();
        private Sequence activeSequence;
        private Vector3 visualInitialScale;
        private Vector3 textInitialScale;
        private bool initialized;
        private bool terminalQueued;

        public bool HasRequiredReferences
        {
            get
            {
                ResolveReferences();
                return visual != null && numberText != null;
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            pulseScaleMultiplier = Mathf.Max(1f, pulseScaleMultiplier);
            pulseScaleUpDuration = Mathf.Max(0f, pulseScaleUpDuration);
            pulseScaleDownDuration = Mathf.Max(0f, pulseScaleDownDuration);
            zeroHoldDuration = Mathf.Clamp(zeroHoldDuration, 0f, 0.08f);
            finalScaleUpMultiplier = Mathf.Max(1f, finalScaleUpMultiplier);
            finalScaleUpDuration = Mathf.Max(0f, finalScaleUpDuration);
            finalScaleDownDuration = Mathf.Max(0f, finalScaleDownDuration);
            ResolveReferences();
        }

        private void OnDisable()
        {
            ClearPresentation();
        }

        public bool Initialize(int initialNumber)
        {
            ResolveReferences();
            if (!HasRequiredReferences || initialNumber < 1)
            {
                Debug.LogError($"{nameof(PanelView)} on '{name}' requires Visual, Text/TMP_Text, and an initial number of at least 1.", this);
                return false;
            }

            visualInitialScale = visual.localScale;
            textInitialScale = numberText.transform.localScale;
            initialized = true;
            ResetPresentation(initialNumber);
            return true;
        }

        public bool EnqueueRemaining(int remaining, Action finalPresentationCompleted = null)
        {
            if (!initialized || terminalQueued || remaining < 0)
            {
                return false;
            }

            terminalQueued = remaining == 0;
            pendingUpdates.Enqueue(new CounterUpdate(remaining, finalPresentationCompleted));
            TryPlayNext();
            return true;
        }

        public void ResetPresentation(int remaining)
        {
            KillActiveSequence();
            pendingUpdates.Clear();
            terminalQueued = false;
            if (visual != null)
            {
                visual.localScale = visualInitialScale;
                visual.gameObject.SetActive(true);
            }

            if (numberText != null)
            {
                numberText.transform.localScale = textInitialScale;
                numberText.gameObject.SetActive(true);
                numberText.text = Mathf.Max(0, remaining).ToString();
            }
        }

        private void TryPlayNext()
        {
            if (activeSequence != null || pendingUpdates.Count == 0 || !isActiveAndEnabled)
            {
                return;
            }

            CounterUpdate update = pendingUpdates.Dequeue();
            numberText.text = update.Remaining.ToString();
            numberText.transform.localScale = textInitialScale;

            Sequence sequence = DOTween.Sequence().SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.Append(numberText.transform
                .DOScale(textInitialScale * pulseScaleMultiplier, pulseScaleUpDuration)
                .SetEase(pulseScaleUpEase));
            sequence.Append(numberText.transform
                .DOScale(textInitialScale, pulseScaleDownDuration)
                .SetEase(pulseScaleDownEase));

            if (update.Remaining == 0)
            {
                if (zeroHoldDuration > 0f)
                {
                    sequence.AppendInterval(zeroHoldDuration);
                }

                sequence.Append(visual.DOScale(visualInitialScale * finalScaleUpMultiplier, finalScaleUpDuration).SetEase(finalScaleUpEase));
                sequence.Join(numberText.transform.DOScale(textInitialScale * finalScaleUpMultiplier, finalScaleUpDuration).SetEase(finalScaleUpEase));
                sequence.Append(visual.DOScale(Vector3.zero, finalScaleDownDuration).SetEase(finalScaleDownEase));
                sequence.Join(numberText.transform.DOScale(Vector3.zero, finalScaleDownDuration).SetEase(finalScaleDownEase));
            }

            activeSequence = sequence;
            sequence.OnComplete(() =>
            {
                if (activeSequence != sequence)
                {
                    return;
                }

                activeSequence = null;
                if (update.Remaining == 0)
                {
                    update.FinalPresentationCompleted?.Invoke();
                    return;
                }

                TryPlayNext();
            });
            sequence.OnKill(() =>
            {
                if (activeSequence == sequence)
                {
                    activeSequence = null;
                }
            });
        }

        private void ClearPresentation()
        {
            KillActiveSequence();
            pendingUpdates.Clear();
        }

        private void KillActiveSequence()
        {
            Sequence sequence = activeSequence;
            activeSequence = null;
            sequence?.Kill(false);
        }

        private void ResolveReferences()
        {
            if (visual == null)
            {
                visual = transform.Find("Visual");
            }

            if (numberText == null)
            {
                Transform textTransform = transform.Find("Text");
                numberText = textTransform != null ? textTransform.GetComponent<TMP_Text>() : null;
            }
        }
    }
}

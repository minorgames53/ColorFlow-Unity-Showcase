using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using UnityEngine;

namespace Gameplay.BoardFeatures.Crates
{
    [DisallowMultipleComponent]
    public sealed class CrateView : MonoBehaviour
    {
        public const float DefaultSourceBoxRevealDuration = 0.22f;

        private readonly struct PendingHit
        {
            public readonly int Progress;
            public readonly Action FinalBreakStarted;
            public readonly Action PresentationCompleted;

            public PendingHit(int progress, Action finalBreakStarted, Action presentationCompleted)
            {
                Progress = progress;
                FinalBreakStarted = finalBreakStarted;
                PresentationCompleted = presentationCompleted;
            }
        }

        [Header("References")]
        [SerializeField] private Transform viewRoot;
        [SerializeField] private GameObject part1;
        [SerializeField] private GameObject part2;
        [SerializeField] private GameObject part3;
        [SerializeField] private GameObject crateDebrisFx;

        [Header("Hit Shake")]
        [SerializeField, Min(0f)] private float shakeDuration = 0.14f;
        [SerializeField, Min(0f)] private float shakeStrength = 0.06f;
        [SerializeField, Min(1)] private int shakeVibrato = 10;
        [SerializeField, Range(0f, 90f)] private float shakeRandomness = 25f;

        [Header("Part Break")]
        [SerializeField, Min(1f)] private float partScaleUpMultiplier = 1.12f;
        [SerializeField, Min(0f)] private float partScaleUpDuration = 0.08f;
        [SerializeField, Min(0f)] private float partScaleDownDuration = 0.12f;
        [SerializeField] private Ease partScaleUpEase = Ease.OutQuad;
        [SerializeField] private Ease partScaleDownEase = Ease.InBack;

        [Header("Final Break")]
        [SerializeField, Range(0f, 0.08f)] private float sourceBoxRevealDelay = 0.04f;

        [Header("Covered SourceBox Reveal")]
        [SerializeField, Min(0.0001f)] private float sourceBoxRevealDuration = DefaultSourceBoxRevealDuration;
        [SerializeField] private Ease sourceBoxRevealEase = Ease.OutBack;

        private readonly Queue<PendingHit> pendingHits = new Queue<PendingHit>(3);
        private ParticleSystem[] debrisParticleSystems = Array.Empty<ParticleSystem>();
        private Sequence activeSequence;
        private Vector3 initialViewLocalPosition;
        private Vector3 part1InitialLocalScale;
        private Vector3 part2InitialLocalScale;
        private Vector3 part3InitialLocalScale;
        private int lastQueuedProgress;
        private bool initialized;
        private bool finalBreakQueued;
        private bool shuttingDown;

        public bool HasRequiredReferences
        {
            get
            {
                ResolveReferences();
                return viewRoot != null && part1 != null && part2 != null && part3 != null &&
                       crateDebrisFx != null && debrisParticleSystems.Length > 0;
            }
        }

        public float SourceBoxRevealDuration => Mathf.Max(0.0001f, sourceBoxRevealDuration);
        public Ease SourceBoxRevealEase => sourceBoxRevealEase;

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            shakeDuration = Mathf.Max(0f, shakeDuration);
            shakeStrength = Mathf.Max(0f, shakeStrength);
            shakeVibrato = Mathf.Max(1, shakeVibrato);
            shakeRandomness = Mathf.Clamp(shakeRandomness, 0f, 90f);
            partScaleUpMultiplier = Mathf.Max(1f, partScaleUpMultiplier);
            partScaleUpDuration = Mathf.Max(0f, partScaleUpDuration);
            partScaleDownDuration = Mathf.Max(0f, partScaleDownDuration);
            sourceBoxRevealDelay = Mathf.Clamp(sourceBoxRevealDelay, 0f, 0.08f);
            sourceBoxRevealDuration = Mathf.Max(0.0001f, sourceBoxRevealDuration);
            ResolveReferences();
        }

        private void OnDisable()
        {
            ShutdownPresentation();
        }

        private void OnDestroy()
        {
            ShutdownPresentation();
        }

        public bool Initialize()
        {
            ResolveReferences();
            if (!HasRequiredReferences)
            {
                Debug.LogError($"{nameof(CrateView)} on '{name}' requires View, Part 1, Part 2, Part 3, and CrateDebrisFX with at least one ParticleSystem.", this);
                return false;
            }

            for (int i = 0; i < debrisParticleSystems.Length; i++)
            {
                ParticleSystem particleSystem = debrisParticleSystems[i];
                if (particleSystem != null && particleSystem.main.loop)
                {
                    Debug.LogError($"{nameof(CrateView)} on '{name}' requires non-looping CrateDebrisFX ParticleSystems so cleanup can follow their authored lifetime.", this);
                    return false;
                }
            }

            initialViewLocalPosition = viewRoot.localPosition;
            part1InitialLocalScale = part1.transform.localScale;
            part2InitialLocalScale = part2.transform.localScale;
            part3InitialLocalScale = part3.transform.localScale;
            initialized = true;
            ResetPresentation(0);
            return true;
        }

        public bool EnqueueProgress(int progress, Action finalBreakStarted = null, Action presentationCompleted = null)
        {
            if (!initialized || shuttingDown || progress < 1 || progress > 3 || finalBreakQueued || progress != lastQueuedProgress + 1)
            {
                return false;
            }

            lastQueuedProgress = progress;
            finalBreakQueued |= progress == 3;
            pendingHits.Enqueue(new PendingHit(progress, finalBreakStarted, presentationCompleted));
            TryPlayNextHit();
            return true;
        }

        public void ResetPresentation(int progress)
        {
            KillActiveSequence();
            pendingHits.Clear();
            StopDebris();

            int clampedProgress = Mathf.Clamp(progress, 0, 2);
            lastQueuedProgress = clampedProgress;
            finalBreakQueued = false;
            shuttingDown = false;

            if (viewRoot != null)
            {
                viewRoot.localPosition = initialViewLocalPosition;
            }

            ResetPart(part1, part1InitialLocalScale, clampedProgress < 1);
            ResetPart(part2, part2InitialLocalScale, clampedProgress < 2);
            ResetPart(part3, part3InitialLocalScale, true);
        }

        private void TryPlayNextHit()
        {
            if (activeSequence != null || pendingHits.Count == 0 || shuttingDown)
            {
                return;
            }

            PendingHit hit = pendingHits.Dequeue();
            activeSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            if (shakeDuration > 0f && shakeStrength > 0f)
            {
                activeSequence.Append(viewRoot.DOShakePosition(
                    shakeDuration,
                    shakeStrength,
                    shakeVibrato,
                    shakeRandomness,
                    false,
                    true));
            }

            activeSequence.AppendCallback(RestoreViewPosition);
            if (hit.Progress == 1)
            {
                activeSequence.AppendCallback(PlayCrateHitSfx);
                AppendPartBreak(activeSequence, part1, part1InitialLocalScale);
            }
            else if (hit.Progress == 2)
            {
                activeSequence.AppendCallback(PlayCrateHitSfx);
                AppendPartBreak(activeSequence, part2, part2InitialLocalScale);
            }
            else
            {
                AppendFinalBreak(activeSequence, hit);
            }

            Sequence playingSequence = activeSequence;
            playingSequence.OnComplete(() =>
            {
                if (activeSequence != playingSequence)
                {
                    return;
                }

                activeSequence = null;
                RestoreViewPosition();
                hit.PresentationCompleted?.Invoke();
                TryPlayNextHit();
            });
            playingSequence.OnKill(() =>
            {
                if (activeSequence == playingSequence)
                {
                    activeSequence = null;
                }
            });
        }

        private void AppendPartBreak(Sequence sequence, GameObject part, Vector3 initialScale)
        {
            Transform partTransform = part.transform;
            part.SetActive(true);
            partTransform.localScale = initialScale;
            sequence.Append(partTransform
                .DOScale(initialScale * partScaleUpMultiplier, partScaleUpDuration)
                .SetEase(partScaleUpEase));
            sequence.Append(partTransform
                .DOScale(Vector3.zero, partScaleDownDuration)
                .SetEase(partScaleDownEase));
            sequence.AppendCallback(() => part.SetActive(false));
        }

        private void AppendFinalBreak(Sequence sequence, PendingHit hit)
        {
            float debrisLifetime = CalculateDebrisLifetime();
            sequence.AppendCallback(() =>
            {
                part3.SetActive(false);
                AudioManager.Instance?.PlaySfx(AudioKey.CrateDestroy);
                PlayDebris();
            });

            if (sourceBoxRevealDelay > 0f)
            {
                sequence.AppendInterval(sourceBoxRevealDelay);
            }

            sequence.AppendCallback(() => hit.FinalBreakStarted?.Invoke());
            float remainingDebrisLifetime = Mathf.Max(0f, debrisLifetime - sourceBoxRevealDelay);
            if (remainingDebrisLifetime > 0f)
            {
                sequence.AppendInterval(remainingDebrisLifetime);
            }
        }

        private void PlayDebris()
        {
            crateDebrisFx.SetActive(true);
            for (int i = 0; i < debrisParticleSystems.Length; i++)
            {
                ParticleSystem particleSystem = debrisParticleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Play(false);
            }
        }

        private static void PlayCrateHitSfx()
        {
            AudioManager.Instance?.PlaySfx(AudioKey.CrateHit);
        }

        private void StopDebris()
        {
            for (int i = 0; i < debrisParticleSystems.Length; i++)
            {
                ParticleSystem particleSystem = debrisParticleSystems[i];
                if (particleSystem != null && particleSystem.gameObject.activeInHierarchy)
                {
                    particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            crateDebrisFx?.SetActive(false);
        }

        private float CalculateDebrisLifetime()
        {
            float maxLifetime = 0f;
            for (int i = 0; i < debrisParticleSystems.Length; i++)
            {
                ParticleSystem particleSystem = debrisParticleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = particleSystem.main;
                float simulationSpeed = Mathf.Max(0.0001f, main.simulationSpeed);
                float lifetime = GetMaxCurveValue(main.startDelay) + main.duration + GetMaxCurveValue(main.startLifetime);
                maxLifetime = Mathf.Max(maxLifetime, lifetime / simulationSpeed);
            }

            return maxLifetime;
        }

        private static float GetMaxCurveValue(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return Mathf.Max(0f, curve.constant);
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(0f, curve.constantMax);
                case ParticleSystemCurveMode.Curve:
                    return Mathf.Max(0f, GetCurveMaximum(curve.curve) * curve.curveMultiplier);
                case ParticleSystemCurveMode.TwoCurves:
                    return Mathf.Max(0f, Mathf.Max(GetCurveMaximum(curve.curveMin), GetCurveMaximum(curve.curveMax)) * curve.curveMultiplier);
                default:
                    return 0f;
            }
        }

        private static float GetCurveMaximum(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                return 0f;
            }

            float maximum = float.MinValue;
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                maximum = Mathf.Max(maximum, keys[i].value);
            }

            return maximum;
        }

        private void ResolveReferences()
        {
            if (viewRoot == null)
            {
                viewRoot = transform.Find("View");
            }

            Transform searchRoot = viewRoot != null ? viewRoot : transform;
            part1 ??= searchRoot.Find("Part 1")?.gameObject;
            part2 ??= searchRoot.Find("Part 2")?.gameObject;
            part3 ??= searchRoot.Find("Part 3")?.gameObject;
            crateDebrisFx ??= searchRoot.Find("CrateDebrisFX")?.gameObject;
            debrisParticleSystems = crateDebrisFx != null
                ? crateDebrisFx.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();
        }

        private void RestoreViewPosition()
        {
            if (viewRoot != null)
            {
                viewRoot.localPosition = initialViewLocalPosition;
            }
        }

        private static void ResetPart(GameObject part, Vector3 initialScale, bool visible)
        {
            if (part == null)
            {
                return;
            }

            part.transform.localScale = initialScale;
            part.SetActive(visible);
        }

        private void KillActiveSequence()
        {
            if (activeSequence == null)
            {
                return;
            }

            Sequence sequence = activeSequence;
            activeSequence = null;
            sequence.Kill();
        }

        private void ShutdownPresentation()
        {
            if (shuttingDown)
            {
                return;
            }

            shuttingDown = true;
            pendingHits.Clear();
            KillActiveSequence();
        }
    }
}

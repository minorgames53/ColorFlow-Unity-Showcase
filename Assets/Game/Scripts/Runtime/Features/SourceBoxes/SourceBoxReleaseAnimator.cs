using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Game.Shared.Haptics;
using Gameplay.Levels;
using UnityEngine;
using UnityEngine.Serialization;

namespace Gameplay.SourceBoxes
{
    public sealed class SourceBoxReleaseAnimator : MonoBehaviour, IGameplaySpeedTarget
    {
        [Header("References")]
        [SerializeField] private Transform sourceBoxRoot;

        [Header("Marble Timing")]
        [SerializeField, Min(0f)] private float releaseInterval = 0.05f;
        [InspectorName("growDuration")]
        [SerializeField, Min(0.01f)] private float marbleGrowDuration = 0.1f;

        [Header("Marble Scale")]
        [FormerlySerializedAs("startScale")]
        [FormerlySerializedAs("startScaleMultiplier")]
        [SerializeField, Min(0.01f)] private float finalScale = 1f;
        [InspectorName("growEase")]
        [SerializeField] private Ease marbleGrowEase = Ease.OutBack;

        [Header("SourceBox Scale")]
        [SerializeField, Min(0f)] private float sourceBoxScaleStartDelay;
        [SerializeField, Min(1f)] private float growScaleMultiplier = 1.08f;
        [FormerlySerializedAs("growDuration")]
        [InspectorName("growDuration")]
        [SerializeField, Min(0.01f)] private float sourceBoxGrowDuration = 0.07f;
        [SerializeField, Min(0.01f)] private float shrinkDuration = 0.18f;
        [InspectorName("growEase")]
        [SerializeField] private Ease sourceBoxGrowEase = Ease.OutBack;
        [SerializeField] private Ease shrinkEase = Ease.InBack;

        [Header("Sorting")]
        [SerializeField, Min(0f)] private float sameRowTolerance = 0.02f;

        [Header("Haptic")]
        [SerializeField] private bool enableHaptic = true;
        [SerializeField, Min(0)] private int sourceBoxReleaseHapticCount = 5;
        [SerializeField, Min(0f)] private float sourceBoxReleaseHapticDelay = 0.04f;
        [SerializeField] private HapticType sourceBoxReleaseHapticType = HapticType.Selection;

        [Header("Audio")]
        [SerializeField, Min(0)] private int marbleReleaseSfxPlayCount = 1;
        [SerializeField, Min(0f)] private float marbleReleaseSfxDelay = 0.04f;

        private readonly List<MarbleAnimationState> animatedMarbles = new List<MarbleAnimationState>();
        private Sequence activeSequence;
        private Vector3 sourceBoxInitialLocalScale = Vector3.one;
        private bool hasCapturedSourceBoxState;
        private GameplaySpeedController gameplaySpeedController;
        private float gameplaySpeedMultiplier = 1f;
        private bool ufoMovementFrozen;
        private bool resumeSequenceAfterUfoFreeze;

        private struct MarbleAnimationState
        {
            public Marble Marble;
            public Transform Transform;
            public Vector3 InitialScale;
        }

        private void Awake()
        {
            CaptureSourceBoxInitialState();
        }

        private void OnEnable()
        {
            SubscribeToSpeedController();
        }

        private void Reset()
        {
            sourceBoxRoot = transform.Find("Root");
        }

        private void OnDisable()
        {
            UnsubscribeFromSpeedController();
            ResetVisuals();
        }

        private void OnValidate()
        {
            releaseInterval = Mathf.Max(0f, releaseInterval);
            marbleGrowDuration = Mathf.Max(0.01f, marbleGrowDuration);
            finalScale = Mathf.Max(0.01f, finalScale);
            sourceBoxScaleStartDelay = Mathf.Max(0f, sourceBoxScaleStartDelay);
            growScaleMultiplier = Mathf.Max(1f, growScaleMultiplier);
            sourceBoxGrowDuration = Mathf.Max(0.01f, sourceBoxGrowDuration);
            shrinkDuration = Mathf.Max(0.01f, shrinkDuration);
            sameRowTolerance = Mathf.Max(0f, sameRowTolerance);
            sourceBoxReleaseHapticCount = Mathf.Max(0, sourceBoxReleaseHapticCount);
            sourceBoxReleaseHapticDelay = Mathf.Max(0f, sourceBoxReleaseHapticDelay);
            marbleReleaseSfxPlayCount = Mathf.Max(0, marbleReleaseSfxPlayCount);
            marbleReleaseSfxDelay = Mathf.Max(0f, marbleReleaseSfxDelay);
        }

        public bool CanPlay(IReadOnlyList<Marble> marbles, Transform releasedMarbleContainer)
        {
            return ValidatePlayInput(marbles, releasedMarbleContainer, true);
        }

        public bool Play(
            IReadOnlyList<Marble> marbles,
            Transform releasedMarbleContainer,
            Action onCompleted)
        {
            ResetVisuals();

            if (!ValidatePlayInput(marbles, releasedMarbleContainer, true))
            {
                return false;
            }

            List<Marble> sortedMarbles = new List<Marble>(marbles.Count);
            CollectReleaseOrderedMarbles(marbles, sortedMarbles);
            ReparentAndPrepareMarbles(sortedMarbles, releasedMarbleContainer);

            activeSequence = DOTween.Sequence();
            activeSequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            InsertMarbleReleaseSfx(activeSequence);
            InsertSourceBoxReleaseHaptics(activeSequence);
            activeSequence.Insert(0f, CreateSourceBoxSequence());

            float lastMarbleEndTime = 0f;
            for (int i = 0; i < animatedMarbles.Count; i++)
            {
                float startTime = i * releaseInterval;
                Sequence marbleSequence = CreateMarbleSequence(animatedMarbles[i]);
                float effectiveStartTime = GetEffectiveDuration(startTime);
                activeSequence.Insert(effectiveStartTime, marbleSequence);
                lastMarbleEndTime = Mathf.Max(lastMarbleEndTime, effectiveStartTime + GetEffectiveDuration(marbleGrowDuration));
            }

            float sourceBoxEndTime = GetSourceBoxScaleAnimationEndTime();
            activeSequence.InsertCallback(
                Mathf.Max(lastMarbleEndTime, sourceBoxEndTime),
                () =>
                {
                    // Released marbles now belong to the normal field pipeline. Do not let
                    // this animator's later OnDisable reset their scale or physics state.
                    animatedMarbles.Clear();
                    activeSequence = null;
                    onCompleted?.Invoke();
                });

            PauseActiveSequenceForUfoIfNeeded();

            return true;
        }

        public bool PlaySourceOnly(Action onCompleted)
        {
            ResetVisuals();
            if (sourceBoxRoot == null)
            {
                return false;
            }

            activeSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            activeSequence.Append(CreateSourceBoxSequence());
            activeSequence.OnComplete(() =>
            {
                activeSequence = null;
                onCompleted?.Invoke();
            });
            PauseActiveSequenceForUfoIfNeeded();
            return true;
        }

        public void SetGameplaySpeedMultiplier(float multiplier)
        {
            gameplaySpeedMultiplier = Mathf.Max(0.01f, multiplier);
        }

        public void SetUfoMovementFrozen(bool isFrozen)
        {
            if (ufoMovementFrozen == isFrozen)
            {
                return;
            }

            ufoMovementFrozen = isFrozen;
#if UNITY_EDITOR
            Gameplay.MarbleDebug.MarbleDebugTracker.SuspendTransfers(GetComponent<SourceBox>(), isFrozen);
#endif
            if (activeSequence == null || !activeSequence.IsActive())
            {
                resumeSequenceAfterUfoFreeze = false;
                return;
            }

            if (isFrozen)
            {
                resumeSequenceAfterUfoFreeze = activeSequence.IsPlaying();
                activeSequence.Pause();
            }
            else if (resumeSequenceAfterUfoFreeze)
            {
                resumeSequenceAfterUfoFreeze = false;
                activeSequence.Play();
            }
        }

        public void ResetVisuals()
        {
            KillActiveSequence();
            CaptureSourceBoxInitialState();

            if (sourceBoxRoot != null)
            {
                sourceBoxRoot.DOKill(false);
                sourceBoxRoot.localScale = sourceBoxInitialLocalScale;
                sourceBoxRoot.gameObject.SetActive(true);
            }

            for (int i = 0; i < animatedMarbles.Count; i++)
            {
                MarbleAnimationState state = animatedMarbles[i];
                if (state.Transform != null)
                {
                    state.Transform.DOKill(false);
                    state.Transform.localScale = state.InitialScale;
                }

                if (state.Marble != null)
                {
                    state.Marble.SetOutlineActive(false);
                    state.Marble.PrepareForReleaseAnimation();
                }
            }

            animatedMarbles.Clear();
        }

        private void ReparentAndPrepareMarbles(IReadOnlyList<Marble> marbles, Transform releasedMarbleContainer)
        {
            for (int i = 0; i < marbles.Count; i++)
            {
                Marble marble = marbles[i];
                marble.PrepareForReleaseAnimation();
                marble.transform.SetParent(releasedMarbleContainer, true);
                marble.SetOutlineActive(false);

                animatedMarbles.Add(new MarbleAnimationState
                {
                    Marble = marble,
                    Transform = marble.transform,
                    InitialScale = marble.transform.localScale
                });
            }
        }

        private Sequence CreateMarbleSequence(MarbleAnimationState state)
        {
            return CreateMarbleReleaseSequence(
                state.Marble,
                state.Transform,
                () =>
                {
                    if (state.Transform != null)
                    {
                        state.Transform.localScale = state.InitialScale;
                    }
                });
        }

        public Sequence CreateMarbleReleaseSequence(Marble marble, Action beforeRelease)
        {
            return marble == null
                ? null
                : CreateMarbleReleaseSequence(marble, marble.transform, beforeRelease);
        }

        private Sequence CreateMarbleReleaseSequence(
            Marble marble,
            Transform marbleTransform,
            Action beforeRelease)
        {
            Sequence sequence = DOTween.Sequence();
            sequence.AppendCallback(() =>
            {
                beforeRelease?.Invoke();
                marble.SetOutlineActive(true);
                marble.Release();
            });

            if (marbleTransform != null)
            {
                sequence.Append(
                    marbleTransform
                        .DOScale(Vector3.one * finalScale, GetEffectiveDuration(marbleGrowDuration))
                        .SetEase(marbleGrowEase));
            }

            return sequence;
        }

        private Sequence CreateSourceBoxSequence()
        {
            Sequence sequence = DOTween.Sequence();
            if (sourceBoxRoot == null)
            {
                return sequence;
            }

            AppendSourceBoxScaleStartDelay(sequence);

            sequence.Append(
                sourceBoxRoot
                    .DOScale(sourceBoxInitialLocalScale * growScaleMultiplier, GetEffectiveDuration(sourceBoxGrowDuration))
                    .SetEase(sourceBoxGrowEase));

            sequence.Append(
                sourceBoxRoot
                    .DOScale(Vector3.zero, GetEffectiveDuration(shrinkDuration))
                    .SetEase(shrinkEase));

            return sequence;
        }

        private void InsertMarbleReleaseSfx(Sequence sequence)
        {
            if (sequence == null || marbleReleaseSfxPlayCount <= 0)
            {
                return;
            }

            for (int i = 0; i < marbleReleaseSfxPlayCount; i++)
            {
                float playTime = GetEffectiveDuration(i * marbleReleaseSfxDelay);
                sequence.InsertCallback(playTime, () => AudioManager.Instance?.PlaySfx(AudioKey.MarbleRelease));
            }
        }

        private void InsertSourceBoxReleaseHaptics(Sequence sequence)
        {
            if (!enableHaptic || sequence == null || sourceBoxReleaseHapticCount <= 0)
            {
                return;
            }

            for (int i = 0; i < sourceBoxReleaseHapticCount; i++)
            {
                float playTime = GetEffectiveDuration(i * sourceBoxReleaseHapticDelay);
                sequence.InsertCallback(playTime, () => HapticManager.Instance?.Play(sourceBoxReleaseHapticType, true));
            }
        }

        private void AppendSourceBoxScaleStartDelay(Sequence sequence)
        {
            float delay = GetSourceBoxScaleStartDelay();
            if (delay > 0f)
            {
                sequence.AppendInterval(GetEffectiveDuration(delay));
            }
        }

        private float GetSourceBoxScaleAnimationEndTime()
        {
            return GetEffectiveDuration(GetSourceBoxScaleStartDelay()) +
                   GetEffectiveDuration(sourceBoxGrowDuration) +
                   GetEffectiveDuration(shrinkDuration);
        }

        private float GetSourceBoxScaleStartDelay()
        {
            return Mathf.Max(0f, sourceBoxScaleStartDelay);
        }

        private float GetEffectiveDuration(float duration)
        {
            return Mathf.Max(0f, duration) / gameplaySpeedMultiplier;
        }

        private void SubscribeToSpeedController()
        {
            if (gameplaySpeedController != null)
            {
                return;
            }

            gameplaySpeedController = FindFirstObjectByType<GameplaySpeedController>();
            if (gameplaySpeedController == null)
            {
                SetGameplaySpeedMultiplier(1f);
                return;
            }

            SetGameplaySpeedMultiplier(gameplaySpeedController.CurrentMultiplier);
            gameplaySpeedController.SpeedMultiplierChanged += SetGameplaySpeedMultiplier;
        }

        private void UnsubscribeFromSpeedController()
        {
            if (gameplaySpeedController == null)
            {
                return;
            }

            gameplaySpeedController.SpeedMultiplierChanged -= SetGameplaySpeedMultiplier;
            gameplaySpeedController = null;
        }

        private bool ValidatePlayInput(IReadOnlyList<Marble> marbles, Transform releasedMarbleContainer, bool logErrors)
        {
            if (marbles == null)
            {
                LogError("cannot play because marble list is null.", logErrors);
                return false;
            }

            if (releasedMarbleContainer == null)
            {
                LogError("cannot play because ReleasedMarbleContainer reference is missing.", logErrors);
                return false;
            }

            if (sourceBoxRoot == null)
            {
                LogError("cannot play because SourceBox Root reference is missing.", logErrors);
                return false;
            }

            return true;
        }

        public void CollectReleaseOrderedMarbles(IReadOnlyList<Marble> marbles, List<Marble> result)
        {
            result.Clear();
            for (int i = 0; i < marbles.Count; i++)
            {
                if (marbles[i] != null)
                {
                    result.Add(marbles[i]);
                }
            }

            result.Sort(CompareMarblesByWorldPosition);
        }

        private int CompareMarblesByWorldPosition(Marble left, Marble right)
        {
            Vector3 leftPosition = left.transform.position;
            Vector3 rightPosition = right.transform.position;
            float yDelta = leftPosition.y - rightPosition.y;

            if (Mathf.Abs(yDelta) > sameRowTolerance)
            {
                return yDelta < 0f ? -1 : 1;
            }

            float xDelta = leftPosition.x - rightPosition.x;
            if (Mathf.Approximately(xDelta, 0f))
            {
                return 0;
            }

            return xDelta < 0f ? -1 : 1;
        }

        private void CaptureSourceBoxInitialState()
        {
            if (hasCapturedSourceBoxState || sourceBoxRoot == null)
            {
                return;
            }

            sourceBoxInitialLocalScale = sourceBoxRoot.localScale;
            hasCapturedSourceBoxState = true;
        }

        private void KillActiveSequence()
        {
            if (activeSequence == null)
            {
                return;
            }

            activeSequence.Kill(false);
            activeSequence = null;
            resumeSequenceAfterUfoFreeze = false;
        }

        private void PauseActiveSequenceForUfoIfNeeded()
        {
            if (!ufoMovementFrozen || activeSequence == null || !activeSequence.IsActive())
            {
                return;
            }

            resumeSequenceAfterUfoFreeze = true;
            activeSequence.Pause();
        }

        private void LogError(string message, bool shouldLog)
        {
            if (shouldLog)
            {
                Debug.LogError($"{nameof(SourceBoxReleaseAnimator)} on '{name}' {message}", this);
            }
        }

    }
}

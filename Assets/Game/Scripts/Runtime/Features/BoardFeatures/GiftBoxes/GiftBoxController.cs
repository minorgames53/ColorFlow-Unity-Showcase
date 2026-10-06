using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay.BoardFeatures.GiftBoxes
{
    [DisallowMultipleComponent]
    public sealed class GiftBoxController : MonoBehaviour, IGameplaySpeedTarget
    {
        [Header("Prefab References")]
        [SerializeField] private Transform boxTopPart;
        [SerializeField] private Transform boxBottomPart;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private TMP_Text numberText;

        [Header("Open Animation")]
        [SerializeField, Min(0f)] private float topScaleUpDuration = 0.12f;
        [SerializeField, Min(0f)] private float topMoveDuration = 0.12f;
        [SerializeField, Min(0f)] private float topScaleDownDuration = 0.12f;
        [SerializeField] private float topScaleUpMultiplier = 1.12f;
        [SerializeField] private float topMoveY = 0.35f;
        [SerializeField] private Ease topScaleUpEase = Ease.OutBack;
        [SerializeField] private Ease topMoveEase = Ease.OutQuad;
        [SerializeField] private Ease topScaleDownEase = Ease.InBack;

        [Header("SourceBox Spawn")]
        [SerializeField, Min(0f)] private float sourceBoxSpawnInterval = 0.08f;
        [SerializeField, Min(0f)] private float sourceBoxFlightDuration = 0.35f;
        [SerializeField, Min(0f)] private float sourceBoxScaleDuration = 0.22f;
        [SerializeField, Min(0f)] private float sourceBoxArcHeight = 0.75f;
        [SerializeField] private Ease sourceBoxFlightEase = Ease.OutQuad;
        [SerializeField] private Ease sourceBoxScaleEase = Ease.OutBack;

        [Header("Bottom Feedback")]
        [SerializeField] private float bottomPunchScale = 0.08f;
        [SerializeField, Min(0f)] private float bottomPunchDuration = 0.14f;

        [Header("Close Animation")]
        [SerializeField, Min(0f)] private float bottomScaleUpDuration = 0.1f;
        [SerializeField, Min(0f)] private float bottomMoveDuration = 0.12f;
        [SerializeField, Min(0f)] private float bottomScaleDownDuration = 0.14f;
        [SerializeField] private float bottomScaleUpMultiplier = 1.1f;
        [SerializeField] private float bottomMoveY = 0.25f;
        [SerializeField] private Ease bottomScaleUpEase = Ease.OutBack;
        [SerializeField] private Ease bottomMoveEase = Ease.OutQuad;
        [SerializeField] private Ease bottomScaleDownEase = Ease.InBack;

        private Sequence activeSequence;
        private readonly Dictionary<SourceBox, SortingOrderRestore> sourceBoxSortingOrderRestores = new Dictionary<SourceBox, SortingOrderRestore>();
        private GameplaySpeedController gameplaySpeedController;
        private Vector3 topInitialLocalPosition;
        private Vector3 topInitialLocalScale;
        private Vector3 bottomInitialLocalPosition;
        private Vector3 bottomInitialLocalScale;
        private SourceBox[] animatingSourceBoxes;
        private Vector3[] animatingTargetPositions;
        private Action sourceBoxesArrivedCallback;
        private Action completedCallback;
        private float gameplaySpeedMultiplier = 1f;
        private bool capturedInitialState;
        private bool sourceBoxesArrived;
        private bool resolutionCompleted;
        private bool suppressKillCompletion;

        private readonly struct SortingOrderRestore
        {
            public readonly SortingGroup SortingGroup;
            public readonly int OriginalSortingOrder;

            public SortingOrderRestore(SortingGroup sortingGroup, int originalSortingOrder)
            {
                SortingGroup = sortingGroup;
                OriginalSortingOrder = originalSortingOrder;
            }
        }

        public int CellIndex { get; private set; } = -1;
        public Vector2Int Coordinate { get; private set; }
        public Transform SpawnPoint => spawnPoint;
        public bool IsResolving => activeSequence != null && activeSequence.IsActive();
        public bool HasRequiredReferences =>
            boxTopPart != null && boxBottomPart != null && spawnPoint != null && numberText != null;
        public float SourceBoxScaleDuration => Mathf.Max(0.0001f, sourceBoxScaleDuration);
        public Ease SourceBoxScaleEase => sourceBoxScaleEase;

        private void Awake()
        {
            CaptureInitialState();
        }

        private void OnEnable()
        {
            CaptureInitialState();
            SubscribeToSpeedController();
        }

        private void OnDisable()
        {
            KillAnimation();
            UnsubscribeFromSpeedController();
        }

        private void OnValidate()
        {
            topScaleUpDuration = Mathf.Max(0f, topScaleUpDuration);
            topMoveDuration = Mathf.Max(0f, topMoveDuration);
            topScaleDownDuration = Mathf.Max(0f, topScaleDownDuration);
            sourceBoxSpawnInterval = Mathf.Max(0f, sourceBoxSpawnInterval);
            sourceBoxFlightDuration = Mathf.Max(0f, sourceBoxFlightDuration);
            sourceBoxScaleDuration = Mathf.Max(0f, sourceBoxScaleDuration);
            sourceBoxArcHeight = Mathf.Max(0f, sourceBoxArcHeight);
            bottomPunchDuration = Mathf.Max(0f, bottomPunchDuration);
            bottomScaleUpDuration = Mathf.Max(0f, bottomScaleUpDuration);
            bottomMoveDuration = Mathf.Max(0f, bottomMoveDuration);
            bottomScaleDownDuration = Mathf.Max(0f, bottomScaleDownDuration);
        }

        public bool Initialize(int cellIndex, Vector2Int coordinate, int giftContentCount)
        {
            CellIndex = cellIndex;
            Coordinate = coordinate;
            CaptureInitialState();
            ResetVisuals();
            if (!ValidateReferences())
            {
                return false;
            }

            numberText.text = giftContentCount.ToString();
            return true;
        }

        public bool PlayResolve(IReadOnlyList<SourceBox> sourceBoxes, Action onSourceBoxesArrived, Action onCompleted)
        {
            KillAnimation();
            ResetVisuals();

            int sourceBoxCount = sourceBoxes?.Count ?? 0;
            if (!ValidateReferences() ||
                sourceBoxCount == 0)
            {
                return false;
            }

            Vector3 spawnPosition = spawnPoint.position;
            Vector3[] targetPositions = new Vector3[sourceBoxes.Count];
            for (int i = 0; i < sourceBoxes.Count; i++)
            {
                if (sourceBoxes[i] == null)
                {
                    return false;
                }

                targetPositions[i] = sourceBoxes[i].transform.position;
                sourceBoxes[i].transform.position = spawnPosition;
                sourceBoxes[i].transform.localScale = Vector3.zero;
                sourceBoxes[i].SetInteractionBlocked(true);
                sourceBoxes[i].gameObject.SetActive(true);
            }

            animatingSourceBoxes = new SourceBox[sourceBoxes.Count];
            for (int i = 0; i < sourceBoxes.Count; i++)
            {
                animatingSourceBoxes[i] = sourceBoxes[i];
            }

            animatingTargetPositions = targetPositions;
            sourceBoxesArrivedCallback = onSourceBoxesArrived;
            completedCallback = onCompleted;
            sourceBoxesArrived = false;
            resolutionCompleted = false;

            activeSequence = DOTween.Sequence().SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            AppendOpenAnimation(activeSequence);

            float sourceBoxStartTime = GetOpenDuration();
            float finalFlightEnd = sourceBoxStartTime;
            for (int i = 0; i < sourceBoxes.Count; i++)
            {
                float startTime = sourceBoxStartTime + GetEffectiveDuration(i * sourceBoxSpawnInterval);
                SourceBox sourceBox = sourceBoxes[i];
                Transform sourceTransform = sourceBox.transform;
                Vector3 targetPosition = targetPositions[i];
                float flightDuration = GetEffectiveDuration(sourceBoxFlightDuration);
                float scaleDuration = GetEffectiveDuration(sourceBoxScaleDuration);
                float flightEnd = startTime + Mathf.Max(Mathf.Max(0.0001f, flightDuration), Mathf.Max(0.0001f, scaleDuration));
                finalFlightEnd = Mathf.Max(finalFlightEnd, flightEnd);

                SourceBox sourceBoxForFlight = sourceBox;
                activeSequence.InsertCallback(startTime, () =>
                {
                    AudioManager.Instance?.PlaySfx(AudioKey.GiftBoxDropSourceBox);
                    BeginSourceBoxFlightSorting(sourceBoxForFlight);
                    PlayBottomFeedback();
                });
                activeSequence.Insert(startTime, CreateSourceBoxFlight(sourceTransform, spawnPosition, targetPosition, flightDuration));
                activeSequence.Insert(startTime, sourceTransform.DOScale(Vector3.one, scaleDuration).SetEase(sourceBoxScaleEase));
                activeSequence.InsertCallback(flightEnd, () => RestoreSourceBoxFlightSorting(sourceBoxForFlight));
            }

            activeSequence.InsertCallback(finalFlightEnd, CompleteSourceBoxArrival);
            InsertCloseAnimation(activeSequence, finalFlightEnd);
            activeSequence.OnComplete(() =>
            {
                activeSequence = null;
                CompleteResolution();
            });
            activeSequence.OnKill(() =>
            {
                bool shouldCompleteResolution = !suppressKillCompletion && gameObject.activeInHierarchy;
                activeSequence = null;
                if (shouldCompleteResolution)
                {
                    CompleteResolution();
                }
            });
            return true;
        }

        public void SetGameplaySpeedMultiplier(float multiplier)
        {
            gameplaySpeedMultiplier = Mathf.Max(0.01f, multiplier);
        }

        private Tween CreateSourceBoxFlight(Transform sourceTransform, Vector3 startPosition, Vector3 targetPosition, float duration)
        {
            Vector3 arcPosition = Vector3.Lerp(startPosition, targetPosition, 0.5f) + Vector3.up * sourceBoxArcHeight;
            return sourceTransform
                .DOPath(new[] { startPosition, arcPosition, targetPosition }, Mathf.Max(0.0001f, duration), PathType.CatmullRom)
                .SetEase(sourceBoxFlightEase);
        }

        private void AppendOpenAnimation(Sequence sequence)
        {
            sequence.Append(boxTopPart.DOScale(topInitialLocalScale * topScaleUpMultiplier, GetEffectiveDuration(topScaleUpDuration)).SetEase(topScaleUpEase));
            sequence.Append(boxTopPart.DOLocalMoveY(topInitialLocalPosition.y + topMoveY, GetEffectiveDuration(topMoveDuration)).SetEase(topMoveEase));
            sequence.Append(boxTopPart.DOScale(Vector3.zero, GetEffectiveDuration(topScaleDownDuration)).SetEase(topScaleDownEase));
        }

        private void InsertCloseAnimation(Sequence sequence, float startTime)
        {
            sequence.Insert(startTime, boxBottomPart.DOScale(bottomInitialLocalScale * bottomScaleUpMultiplier, GetEffectiveDuration(bottomScaleUpDuration)).SetEase(bottomScaleUpEase));
            sequence.Insert(startTime + GetEffectiveDuration(bottomScaleUpDuration), boxBottomPart.DOLocalMoveY(bottomInitialLocalPosition.y + bottomMoveY, GetEffectiveDuration(bottomMoveDuration)).SetEase(bottomMoveEase));
            sequence.Insert(startTime + GetEffectiveDuration(bottomScaleUpDuration + bottomMoveDuration), boxBottomPart.DOScale(Vector3.zero, GetEffectiveDuration(bottomScaleDownDuration)).SetEase(bottomScaleDownEase));
        }

        private void PlayBottomFeedback()
        {
            if (boxBottomPart == null || bottomPunchDuration <= 0f || bottomPunchScale <= 0f)
            {
                return;
            }

            boxBottomPart.DOPunchScale(Vector3.one * bottomPunchScale, GetEffectiveDuration(bottomPunchDuration), 1, 0.5f);
        }

        private float GetOpenDuration()
        {
            return GetEffectiveDuration(topScaleUpDuration + topMoveDuration + topScaleDownDuration);
        }

        private float GetEffectiveDuration(float duration)
        {
            return Mathf.Max(0f, duration) / gameplaySpeedMultiplier;
        }

        private bool ValidateReferences()
        {
            if (HasRequiredReferences)
            {
                return true;
            }

            Debug.LogError($"{nameof(GiftBoxController)} on '{name}' is missing Box Top Part, Box Bottom Part, SpawnPoint, or Number TMP_Text reference.", this);
            return false;
        }

        private void CaptureInitialState()
        {
            if (capturedInitialState)
            {
                return;
            }

            if (boxTopPart != null)
            {
                topInitialLocalPosition = boxTopPart.localPosition;
                topInitialLocalScale = boxTopPart.localScale;
            }

            if (boxBottomPart != null)
            {
                bottomInitialLocalPosition = boxBottomPart.localPosition;
                bottomInitialLocalScale = boxBottomPart.localScale;
            }

            capturedInitialState = true;
        }

        private void ResetVisuals()
        {
            if (boxTopPart != null)
            {
                boxTopPart.DOKill(false);
                boxTopPart.localPosition = topInitialLocalPosition;
                boxTopPart.localScale = topInitialLocalScale;
            }

            if (boxBottomPart != null)
            {
                boxBottomPart.DOKill(false);
                boxBottomPart.localPosition = bottomInitialLocalPosition;
                boxBottomPart.localScale = bottomInitialLocalScale;
            }
        }

        private void KillAnimation()
        {
            if (activeSequence != null)
            {
                suppressKillCompletion = true;
                activeSequence.Kill(false);
                suppressKillCompletion = false;
                activeSequence = null;
            }

            RestoreAllSourceBoxFlightSorting();
            ClearResolutionCallbacks();
        }

        private void CompleteSourceBoxArrival()
        {
            if (sourceBoxesArrived)
            {
                return;
            }

            sourceBoxesArrived = true;
            if (animatingSourceBoxes != null && animatingTargetPositions != null)
            {
                int count = Mathf.Min(animatingSourceBoxes.Length, animatingTargetPositions.Length);
                for (int i = 0; i < count; i++)
                {
                    SourceBox sourceBox = animatingSourceBoxes[i];
                    if (sourceBox == null)
                    {
                        continue;
                    }

                    sourceBox.transform.position = animatingTargetPositions[i];
                    sourceBox.transform.localScale = Vector3.one;
                    RestoreSourceBoxFlightSorting(sourceBox);
                }
            }

            sourceBoxesArrivedCallback?.Invoke();
        }

        private void CompleteResolution()
        {
            if (resolutionCompleted)
            {
                return;
            }

            resolutionCompleted = true;
            CompleteSourceBoxArrival();
            Action callback = completedCallback;
            ClearResolutionCallbacks();
            callback?.Invoke();
        }

        private void ClearResolutionCallbacks()
        {
            RestoreAllSourceBoxFlightSorting();
            animatingSourceBoxes = null;
            animatingTargetPositions = null;
            sourceBoxesArrivedCallback = null;
            completedCallback = null;
        }

        private void BeginSourceBoxFlightSorting(SourceBox sourceBox)
        {
            if (sourceBox == null || sourceBoxSortingOrderRestores.ContainsKey(sourceBox))
            {
                return;
            }

            if (!sourceBox.TryGetLockedVisualSortingGroup(out SortingGroup sortingGroup))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"{nameof(GiftBoxController)} on '{name}' could not raise sorting order for Gift SourceBox '{sourceBox.name}' because its LockedVisual has no {nameof(SortingGroup)}.", sourceBox);
#endif
                return;
            }

            sourceBoxSortingOrderRestores.Add(sourceBox, new SortingOrderRestore(sortingGroup, sortingGroup.sortingOrder));
            sortingGroup.sortingOrder += 1;
        }

        private void RestoreSourceBoxFlightSorting(SourceBox sourceBox)
        {
            if (sourceBox == null || !sourceBoxSortingOrderRestores.TryGetValue(sourceBox, out SortingOrderRestore restore))
            {
                return;
            }

            if (restore.SortingGroup != null)
            {
                restore.SortingGroup.sortingOrder = restore.OriginalSortingOrder;
            }

            sourceBoxSortingOrderRestores.Remove(sourceBox);
        }

        private void RestoreAllSourceBoxFlightSorting()
        {
            foreach (KeyValuePair<SourceBox, SortingOrderRestore> pair in sourceBoxSortingOrderRestores)
            {
                if (pair.Value.SortingGroup != null)
                {
                    pair.Value.SortingGroup.sortingOrder = pair.Value.OriginalSortingOrder;
                }
            }

            sourceBoxSortingOrderRestores.Clear();
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
    }
}

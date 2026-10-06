using System;
using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using TMPro;
using UnityEngine;

namespace Gameplay.Spawners
{
    public sealed class SourceBoxSpawner : MonoBehaviour
    {
        private struct RendererState
        {
            public SpriteRenderer Renderer;
            public int SortingLayerId;
            public int SortingOrder;
            public SpriteMaskInteraction MaskInteraction;
        }

        [Header("Visual")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("Count")]
        [SerializeField] private TMP_Text countText;

        [Header("Spawn Mask")]
        [SerializeField] private SpriteMask spriteMask;
        [SerializeField] private Transform sourceBoxAnimationRoot;
        [SerializeField] private SpawnerOrientation orientation;
        [SerializeField] private SpawnerOrientationProfile horizontalProfile = new SpawnerOrientationProfile();
        [SerializeField] private SpawnerOrientationProfile verticalProfile = new SpawnerOrientationProfile();
        [SerializeField] private string maskedSortingLayerName = "Default";

        [Header("Count Animation")]
        [SerializeField] private Vector3 countNormalScale = Vector3.one;
        [SerializeField] private Vector3 countChangeScale = new Vector3(1.15f, 1.15f, 1f);
        [SerializeField, Min(0f)] private float countGrowDuration = 0.08f;
        [SerializeField, Min(0f)] private float countReturnDuration = 0.12f;
        [SerializeField, Min(0f)] private float spawnDelay;
        [SerializeField, Min(0f)] private float spawnMoveDuration = 0.25f;
        [SerializeField] private Ease countGrowEase = Ease.OutBack;
        [SerializeField] private Ease countReturnEase = Ease.OutQuad;
        [SerializeField] private Ease spawnMoveEase = Ease.OutBack;

        [Header("Placeholder Move")]
        [SerializeField] private Ease placeholderMoveEase = Ease.OutQuad;

        [Header("SourceBox Scale")]
        [SerializeField, Min(0f)] private float sourceBoxScaleDelay = 0.1f;
        [SerializeField, Min(0.0001f)] private float sourceBoxScaleUpDuration = 0.15f;

        private readonly List<MarbleColorId> spawnSequence = new List<MarbleColorId>();
        private readonly List<RendererState> rendererStates = new List<RendererState>();
        private const float VisualApplyFrameDelay = 1f / 60f;
        private Vector2Int gridCoordinate;
        private Vector2Int targetCoordinate;
        private SpawnerDirection direction;
        private int currentSequenceIndex;
        private int currentCount = int.MinValue;
        private Sequence countSequence;
        private Sequence spawnSequenceTween;
        private Tween spawnScaleTween;
        private SourceBox activeSpawnSourceBox;

        public Vector2Int GridCoordinate => gridCoordinate;
        public Vector2Int TargetCoordinate => targetCoordinate;
        public SpawnerDirection Direction => direction;
        public int CurrentSequenceIndex => currentSequenceIndex;
        public int RemainingCount => Mathf.Max(0, spawnSequence.Count - currentSequenceIndex);
        public int CurrentCount => currentCount == int.MinValue ? 0 : currentCount;
        public bool IsAnimating => spawnSequenceTween != null && spawnSequenceTween.IsActive();

        private void Reset()
        {
            Transform root = transform.Find("Root");
            Transform searchRoot = root != null ? root : transform;

            Transform visualRoot = transform.Find("VisualRoot");
            spriteRenderer = visualRoot != null
                ? visualRoot.GetComponentInChildren<SpriteRenderer>()
                : GetComponentInChildren<SpriteRenderer>();

            Transform textTransform = searchRoot.Find("Text");
            countText = textTransform != null ? textTransform.GetComponent<TMP_Text>() : GetComponentInChildren<TMP_Text>();
            spriteMask = searchRoot.Find("Sprite Mask")?.GetComponent<SpriteMask>();
            sourceBoxAnimationRoot = searchRoot.Find("SourceBoxAnimationRoot");
        }

        private void OnDisable()
        {
            KillCountTween();
            KillSpawnTween();
            RestoreRendererStates();

            if (activeSpawnSourceBox != null)
            {
                activeSpawnSourceBox.SetInteractionBlocked(false);
                activeSpawnSourceBox = null;
            }
        }

        public void Initialize(SpawnerOrientation newOrientation, int initialCount)
        {
            SetOrientation(newOrientation);
            currentCount = int.MinValue;
            SetCount(initialCount, false);
        }

        public bool Initialize(
            Vector2Int newGridCoordinate,
            SpawnerDirection newDirection,
            IReadOnlyList<SpawnerSourceBoxData> newSpawnSequence,
            Sprite downSprite,
            Sprite horizontalSprite)
        {
            if (!ValidateInitializeInput(newDirection, newSpawnSequence, downSprite, horizontalSprite))
            {
                return false;
            }

            gridCoordinate = newGridCoordinate;
            direction = newDirection;
            targetCoordinate = CalculateTargetCoordinate(newGridCoordinate, newDirection);
            currentSequenceIndex = 0;

            spawnSequence.Clear();
            for (int i = 0; i < newSpawnSequence.Count; i++)
            {
                spawnSequence.Add(newSpawnSequence[i].ColorId);
            }

            ApplyVisual(downSprite, horizontalSprite);
            Initialize(GetOrientationForDirection(newDirection), RemainingCount);
            return true;
        }

        public void SetOrientation(SpawnerOrientation newOrientation)
        {
            orientation = newOrientation;
            SpawnerOrientationProfile profile = GetCurrentProfile();

            if (spriteMask != null)
            {
                Transform maskTransform = spriteMask.transform;
                maskTransform.localPosition = GetProfileLocalPosition(profile.MaskLocalPosition);
                maskTransform.localEulerAngles = profile.MaskLocalEulerAngles;
            }

            if (countText != null)
            {
                countText.transform.localPosition = GetProfileLocalPosition(profile.CountLocalPosition);
            }
        }

        public void SetCount(int value, bool animated = true)
        {
            if (currentCount == value)
            {
                return;
            }

            currentCount = value;
            KillCountTween();

            Transform countTransform = countText != null ? countText.transform : null;
            if (!animated || countTransform == null)
            {
                UpdateCountText();
                if (countTransform != null)
                {
                    countTransform.localScale = countNormalScale;
                }

                return;
            }

            countSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            countSequence.Append(countTransform.DOScale(countChangeScale, countGrowDuration).SetEase(countGrowEase));
            countSequence.AppendCallback(UpdateCountText);
            countSequence.Append(countTransform.DOScale(countNormalScale, countReturnDuration).SetEase(countReturnEase));
            countSequence.OnKill(() => countSequence = null);
        }

        public bool TryPlaySpawn(SourceBox sourceBox, Action onCompleted = null)
        {
            Transform placeholderAnchor = sourceBox != null ? sourceBox.transform.parent : null;
            return TryPlaySpawn(sourceBox, placeholderAnchor, onCompleted);
        }

        public bool TryPlaySpawn(SourceBox sourceBox, Transform placeholderAnchor, Action onCompleted = null)
        {
            if (IsAnimating || sourceBox == null)
            {
                return false;
            }

            if (!ValidateSpawnReferences())
            {
                return false;
            }

            if (!TryGetSortingLayerId(maskedSortingLayerName, out int maskedSortingLayerId))
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' cannot play spawn because Sorting Layer '{maskedSortingLayerName}' does not exist.", this);
                return false;
            }

            SpawnerOrientationProfile profile = GetCurrentProfile();
            activeSpawnSourceBox = sourceBox;
            sourceBox.gameObject.SetActive(true);
            sourceBox.SetInteractionBlocked(true);

            Transform sourceBoxTransform = sourceBox.transform;
            sourceBoxTransform.SetParent(sourceBoxAnimationRoot, false);
            sourceBoxTransform.localPosition = GetProfileLocalPosition(profile.SourceBoxHiddenLocalPosition);
            sourceBoxTransform.localRotation = Quaternion.identity;
            sourceBoxTransform.localScale = profile.SourceBoxScale;
            sourceBox.RefreshVisuals();

            Transform maskTransform = spriteMask.transform;
            maskTransform.localPosition = GetProfileLocalPosition(profile.MaskLocalPosition);
            maskTransform.localEulerAngles = profile.MaskLocalEulerAngles;

            CacheAndApplyRendererMask(sourceBox, maskedSortingLayerId);

            spawnSequenceTween = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            spawnSequenceTween.AppendInterval(VisualApplyFrameDelay);

            if (spawnDelay > 0f)
            {
                spawnSequenceTween.AppendInterval(spawnDelay);
            }

            Vector3 visibleLocalPosition = GetProfileLocalPosition(profile.SourceBoxVisibleLocalPosition);
            Vector3 spawnStartScale = profile.SourceBoxScale;
            float moveDuration = Mathf.Max(0.0001f, spawnMoveDuration);
            float effectiveScaleDelay = Mathf.Clamp(sourceBoxScaleDelay, 0f, moveDuration);
            float scaleDuration = Mathf.Max(0.0001f, sourceBoxScaleUpDuration);

            spawnSequenceTween.Append(sourceBoxTransform
                .DOLocalMove(visibleLocalPosition, moveDuration)
                .SetEase(spawnMoveEase));

            spawnScaleTween = DOVirtual
                .Float(0f, 1f, scaleDuration, value =>
                {
                    sourceBoxTransform.localScale = Vector3.LerpUnclamped(spawnStartScale, Vector3.one, value);
                })
                .SetDelay(VisualApplyFrameDelay + spawnDelay + effectiveScaleDelay)
                .SetEase(spawnMoveEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnKill(() => spawnScaleTween = null);

            if (placeholderAnchor != null)
            {
                spawnSequenceTween.Append(sourceBoxTransform
                    .DOMove(placeholderAnchor.position, moveDuration)
                    .SetEase(placeholderMoveEase));
            }

            float scaleEndTime = spawnDelay + effectiveScaleDelay + scaleDuration;
            float sequenceEndTime = spawnDelay + moveDuration + (placeholderAnchor != null ? moveDuration : 0f);
            if (scaleEndTime > sequenceEndTime)
            {
                spawnSequenceTween.AppendInterval(scaleEndTime - sequenceEndTime);
            }

            spawnSequenceTween.OnComplete(() =>
            {
                sourceBoxTransform.localScale = Vector3.one;
                CompleteSpawnAnimation(sourceBox, placeholderAnchor, onCompleted);
            });
            spawnSequenceTween.OnKill(() => spawnSequenceTween = null);
            return true;
        }

        public bool TryConsumeNextColor(out MarbleColorId colorId)
        {
            colorId = MarbleColorId.None;

            if (currentSequenceIndex < 0 || currentSequenceIndex >= spawnSequence.Count)
            {
                return false;
            }

            colorId = spawnSequence[currentSequenceIndex];
            currentSequenceIndex++;
            SetCount(RemainingCount, true);
            return MarbleColorCatalog.IsGameplayColor(colorId);
        }

        public static Vector2Int CalculateTargetCoordinate(Vector2Int coordinate, SpawnerDirection direction)
        {
            switch (direction)
            {
                case SpawnerDirection.Right:
                    return new Vector2Int(coordinate.x, coordinate.y + 1);
                case SpawnerDirection.Down:
                    return new Vector2Int(coordinate.x - 1, coordinate.y);
                case SpawnerDirection.Left:
                    return new Vector2Int(coordinate.x, coordinate.y - 1);
                default:
                    return coordinate;
            }
        }

        private void CompleteSpawnAnimation(SourceBox sourceBox, Transform placeholderAnchor, Action onCompleted)
        {
            RestoreRendererStates();

            Transform sourceBoxTransform = sourceBox.transform;
            if (placeholderAnchor != null)
            {
                sourceBoxTransform.SetParent(placeholderAnchor, false);
                sourceBoxTransform.localPosition = Vector3.zero;
                sourceBoxTransform.localRotation = Quaternion.identity;
                sourceBoxTransform.localScale = Vector3.one;
            }

            sourceBox.SetInteractionBlocked(false);
            sourceBox.RefreshVisuals();
            if (activeSpawnSourceBox == sourceBox)
            {
                activeSpawnSourceBox = null;
            }

            onCompleted?.Invoke();
        }

        private void CacheAndApplyRendererMask(SourceBox sourceBox, int sortingLayerId)
        {
            rendererStates.Clear();
            SpriteRenderer[] renderers = sourceBox.GetComponentsInChildren<SpriteRenderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer.GetComponentInParent<Marble>() != null)
                {
                    continue;
                }

                rendererStates.Add(new RendererState
                {
                    Renderer = renderer,
                    SortingLayerId = renderer.sortingLayerID,
                    SortingOrder = renderer.sortingOrder,
                    MaskInteraction = renderer.maskInteraction
                });

                renderer.sortingLayerID = sortingLayerId;
                renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            }
        }

        private void RestoreRendererStates()
        {
            for (int i = 0; i < rendererStates.Count; i++)
            {
                RendererState state = rendererStates[i];
                if (state.Renderer == null)
                {
                    continue;
                }

                state.Renderer.sortingLayerID = state.SortingLayerId;
                state.Renderer.sortingOrder = state.SortingOrder;
                state.Renderer.maskInteraction = state.MaskInteraction;
            }

            rendererStates.Clear();
        }

        private void KillCountTween()
        {
            if (countSequence == null)
            {
                return;
            }

            countSequence.Kill();
            countSequence = null;
        }

        private void KillSpawnTween()
        {
            if (spawnScaleTween != null)
            {
                spawnScaleTween.Kill();
                spawnScaleTween = null;
            }

            if (spawnSequenceTween == null)
            {
                return;
            }

            spawnSequenceTween.Kill();
            spawnSequenceTween = null;
        }

        private void UpdateCountText()
        {
            if (countText != null)
            {
                countText.text = CurrentCount.ToString();
            }
        }

        private bool ValidateSpawnReferences()
        {
            if (spriteMask == null)
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' cannot play spawn because SpriteMask reference is missing.", this);
                return false;
            }

            if (sourceBoxAnimationRoot == null)
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' cannot play spawn because SourceBoxAnimationRoot reference is missing.", this);
                return false;
            }

            if (string.IsNullOrWhiteSpace(maskedSortingLayerName))
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' cannot play spawn because masked Sorting Layer name is empty.", this);
                return false;
            }

            return true;
        }

        private bool ValidateInitializeInput(
            SpawnerDirection newDirection,
            IReadOnlyList<SpawnerSourceBoxData> newSpawnSequence,
            Sprite downSprite,
            Sprite horizontalSprite)
        {
            if (spriteRenderer == null)
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' is missing SpriteRenderer reference.", this);
                return false;
            }

            if (newDirection == SpawnerDirection.Up)
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' cannot initialize with unsupported direction '{newDirection}'.", this);
                return false;
            }

            if (newDirection != SpawnerDirection.Down && newDirection != SpawnerDirection.Right && newDirection != SpawnerDirection.Left)
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' received invalid direction '{newDirection}'.", this);
                return false;
            }

            if (newSpawnSequence == null || newSpawnSequence.Count < LevelCellData.MinMarbleCount || newSpawnSequence.Count > LevelCellData.MaxMarbleCount)
            {
                int count = newSpawnSequence?.Count ?? 0;
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' received invalid spawn sequence count {count}.", this);
                return false;
            }

            for (int i = 0; i < newSpawnSequence.Count; i++)
            {
                if (newSpawnSequence[i] == null || !MarbleColorCatalog.IsGameplayColor(newSpawnSequence[i].ColorId))
                {
                    Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' has invalid spawn sequence entry at index {i}.", this);
                    return false;
                }
            }

            if (newDirection == SpawnerDirection.Down && downSprite == null)
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' cannot initialize because down sprite is missing.", this);
                return false;
            }

            if ((newDirection == SpawnerDirection.Right || newDirection == SpawnerDirection.Left) && horizontalSprite == null)
            {
                Debug.LogError($"{nameof(SourceBoxSpawner)} on '{name}' cannot initialize because horizontal sprite is missing.", this);
                return false;
            }

            return true;
        }

        private void ApplyVisual(Sprite downSprite, Sprite horizontalSprite)
        {
            if (direction == SpawnerDirection.Down)
            {
                spriteRenderer.sprite = downSprite;
                spriteRenderer.flipX = false;
                spriteRenderer.transform.localRotation = Quaternion.identity;
                return;
            }

            spriteRenderer.sprite = horizontalSprite;
            spriteRenderer.flipX = false;
            spriteRenderer.transform.localRotation = direction == SpawnerDirection.Left
                ? Quaternion.Euler(0f, 0f, 180f)
                : Quaternion.identity;
        }

        private SpawnerOrientationProfile GetCurrentProfile()
        {
            return orientation == SpawnerOrientation.Vertical
                ? verticalProfile
                : horizontalProfile;
        }

        private Vector3 GetProfileLocalPosition(Vector3 localPosition)
        {
            if (orientation != SpawnerOrientation.Horizontal || direction != SpawnerDirection.Left)
            {
                return localPosition;
            }

            return new Vector3(-localPosition.x, localPosition.y, localPosition.z);
        }

        private static SpawnerOrientation GetOrientationForDirection(SpawnerDirection spawnDirection)
        {
            return spawnDirection == SpawnerDirection.Down
                ? SpawnerOrientation.Vertical
                : SpawnerOrientation.Horizontal;
        }

        private static bool TryGetSortingLayerId(string sortingLayerName, out int sortingLayerId)
        {
            SortingLayer[] layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name != sortingLayerName)
                {
                    continue;
                }

                sortingLayerId = layers[i].id;
                return true;
            }

            sortingLayerId = 0;
            return false;
        }
    }
}

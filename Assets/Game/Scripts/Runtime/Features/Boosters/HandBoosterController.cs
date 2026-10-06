using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using Gameplay.Tutorial;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay.Boosters
{
    [DisallowMultipleComponent]
    public sealed class HandBoosterController : MonoBehaviour
    {
        private const string BoosterSortingLayerName = "Booster";

        [Header("Runtime References")]
        [SerializeField] private BoosterController boosterController;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private TargetLaneController targetLaneController;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private BoosterUnlockTutorialController boosterUnlockTutorialController;

        [Header("Target Preview")]
        [SerializeField] private GameObject handIndicatorPrefab;
        [SerializeField] private Vector3 handIndicatorLocalPosition = new Vector3(0f, 0.65f, -0.1f);
        [SerializeField, Min(0f)] private float indicatorBobDistance = 0.12f;
        [SerializeField, Min(0.01f)] private float indicatorBobDuration = 0.5f;
        [SerializeField, Min(0f)] private float featureSourceYOffset = 0.15f;
        [SerializeField, Min(0f)] private float featureOffsetDuration = 0.12f;

        private sealed class PreviewRecord
        {
            public SourceBox SourceBox;
            public Transform PresentationRoot;
            public Vector3 OriginalLocalPosition;
            public Tween OffsetTween;
            public Transform IndicatorAnchor;
            public GameObject Indicator;
            public Tween IndicatorTween;
        }

        private readonly Dictionary<SourceBox, PreviewRecord> previewRecords = new Dictionary<SourceBox, PreviewRecord>();
        private readonly HashSet<SourceBox> observedSources = new HashSet<SourceBox>();
        private readonly List<SourceBox> transferSources = new List<SourceBox>(2);
        private readonly List<SourceBox> cleanupBuffer = new List<SourceBox>();
        private bool targetingPresentationActive;
        private bool resolvingMysterySelection;
        private bool ownsActiveHandTransfer;
        private SourceBox deferredTransferSourceBox;
        private int deferredTransferLifecycleVersion;
        private Transform handIndicatorsRoot; 

        public bool IsTargeting => boosterController != null &&
                                   boosterController.ActiveBooster == BoosterType.Hand &&
                                   boosterController.State == BoosterState.Targeting;

        private void Awake()
        {
            CacheMissingReferences();
        }

        private void OnValidate()
        {
            indicatorBobDistance = Mathf.Max(0f, indicatorBobDistance);
            indicatorBobDuration = Mathf.Max(0.01f, indicatorBobDuration);
            featureSourceYOffset = Mathf.Max(0f, featureSourceYOffset);
            featureOffsetDuration = Mathf.Max(0f, featureOffsetDuration);
        }

        private void OnEnable()
        {
            CacheMissingReferences();
            Subscribe();
            RefreshTargetingState();
        }

        private void OnDisable()
        {
            CleanupTargetingPresentation();
            ClearDeferredTransfer();
            sourceBoxBoardController?.SetHandBoosterTargetingActive(false);
            Unsubscribe();

            if (IsTargeting)
            {
                boosterController.NotifyCancelled(BoosterType.Hand, boosterController.ActiveLifecycleVersion);
            }
        }

        private void LateUpdate()
        {
            if (handIndicatorsRoot == null)
            {
                return;
            }

            foreach (PreviewRecord record in previewRecords.Values)
            {
                if (record?.IndicatorAnchor == null || record.PresentationRoot == null)
                {
                    continue;
                }

                record.IndicatorAnchor.position = record.PresentationRoot.position;
            }
        }

        public SourceBoxReleaseResult TryUseOnSourceBox(SourceBox sourceBox)
        {
            if (!IsTargeting || resolvingMysterySelection || deferredTransferSourceBox != null ||
                !IsValidTarget(sourceBox))
            {
                return SourceBoxReleaseResult.InvalidState;
            }

            int expectedLifecycleVersion = boosterController.ActiveLifecycleVersion;
            if (!sourceBox.IsMysterySourceBox)
            {
                return TryStartDirectTransfer(sourceBox, expectedLifecycleVersion)
                    ? SourceBoxReleaseResult.Success
                    : SourceBoxReleaseResult.InvalidState;
            }

            resolvingMysterySelection = true;
            RemoveIndicator(sourceBox);
            if (!sourceBox.PlayHandMysteryRevealPreview(() =>
                HandleMysteryRevealCompleted(sourceBox, expectedLifecycleVersion)))
            {
                resolvingMysterySelection = false;
                RefreshSourcePreview(sourceBox);
                return SourceBoxReleaseResult.InvalidState;
            }

            return SourceBoxReleaseResult.Success;
        }

        private void HandleMysteryRevealCompleted(SourceBox sourceBox, int expectedLifecycleVersion)
        {
            resolvingMysterySelection = false;
            if (!IsTargeting || boosterController.ActiveLifecycleVersion != expectedLifecycleVersion || sourceBox == null)
            {
                return;
            }

            if (TryStartDirectTransfer(sourceBox, expectedLifecycleVersion))
            {
                return;
            }

            sourceBox.SetHandOpenViewPreview(false);
            RefreshSourcePreview(sourceBox);
        }

        private bool TryStartDirectTransfer(SourceBox selectedSourceBox, int expectedLifecycleVersion)
        {
            if (sourceBoxBoardController == null || targetLaneController == null ||
                !sourceBoxBoardController.TryGetHandDirectTransferSources(selectedSourceBox, transferSources))
            {
                return false;
            }

            SourceBox[] transactionSources = transferSources.ToArray();
            transferSources.Clear();
            if (!targetLaneController.TryBeginHandDirectTransfer(
                    transactionSources,
                    succeeded => HandleHandTransferCompleted(
                        succeeded,
                        expectedLifecycleVersion),
                    out bool blockedByActiveTransfer))
            {
                if (blockedByActiveTransfer)
                {
                    deferredTransferSourceBox = selectedSourceBox;
                    deferredTransferLifecycleVersion = expectedLifecycleVersion;
                    RemoveIndicator(selectedSourceBox);
                    return true;
                }

                return false;
            }

            ClearDeferredTransfer();
            ownsActiveHandTransfer = true;
            sourceBoxBoardController.NotifyHandDirectTransferCommitted(transactionSources);
            levelSessionController?.MarkPlayerMoveCommitted();
            boosterController.NotifyExecutionStarted(BoosterType.Hand, expectedLifecycleVersion);
            return true;
        }

        private void HandleHandTransferCompleted(bool succeeded, int expectedLifecycleVersion)
        {
            if (!ownsActiveHandTransfer)
            {
                return;
            }

            ownsActiveHandTransfer = false;
            if (succeeded)
            {
                boosterController?.NotifyCompleted(BoosterType.Hand, expectedLifecycleVersion);
            }
            else
            {
                boosterController?.NotifyCancelled(BoosterType.Hand, expectedLifecycleVersion);
            }
        }

        public bool IsValidTarget(SourceBox sourceBox)
        {
            if (boosterUnlockTutorialController != null &&
                !boosterUnlockTutorialController.CanPresentHandTarget(sourceBox))
            {
                return false;
            }

            if (sourceBox == null || sourceBoxBoardController == null || targetLaneController == null ||
                !sourceBoxBoardController.IsValidHandBoosterTarget(sourceBox) ||
                !sourceBoxBoardController.TryGetHandDirectTransferSources(sourceBox, transferSources))
            {
                transferSources.Clear();
                return false;
            }

            bool canTransferAllMarbles = targetLaneController.CanBeginHandDirectTransfer(transferSources);
            transferSources.Clear();
            return canTransferAllMarbles;
        }

        private void HandleBoosterStateChanged(BoosterType type, BoosterState state)
        {
            if (ownsActiveHandTransfer &&
                (type != BoosterType.Hand || state != BoosterState.Running))
            {
                ownsActiveHandTransfer = false;
                targetLaneController?.AbortRuntimeTransactions();
            }

            if (type != BoosterType.Hand || state != BoosterState.Targeting)
            {
                ClearDeferredTransfer();
            }

            RefreshTargetingState();
        }

        private void HandleTargetTransferStateChanged()
        {
            if (deferredTransferSourceBox == null)
            {
                return;
            }

            if (!IsTargeting ||
                boosterController.ActiveLifecycleVersion != deferredTransferLifecycleVersion)
            {
                ClearDeferredTransfer();
                return;
            }

            if (targetLaneController == null || targetLaneController.HasUfoTargetTransitionActivity)
            {
                return;
            }

            SourceBox sourceBox = deferredTransferSourceBox;
            int expectedLifecycleVersion = deferredTransferLifecycleVersion;
            if (TryStartDirectTransfer(sourceBox, expectedLifecycleVersion))
            {
                return;
            }

            ClearDeferredTransfer();
            RefreshSourcePreview(sourceBox);
        }

        private void ClearDeferredTransfer()
        {
            deferredTransferSourceBox = null;
            deferredTransferLifecycleVersion = 0;
        }

        private void HandleSourceBoxSpawned(SourceBox sourceBox)
        {
            sourceBox?.SetBoosterTargetingInputOverride(
                IsTargeting && sourceBoxBoardController != null &&
                sourceBoxBoardController.IsValidHandBoosterTarget(sourceBox));
            if (IsTargeting)
            {
                ObserveSource(sourceBox);
                RefreshSourcePreview(sourceBox);
            }
        }

        private void HandlePreviewSourceStateChanged(SourceBox sourceBox)
        {
            if (IsTargeting)
            {
                RefreshSourcePreview(sourceBox);
            }
            else
            {
                RemovePreviewRecord(sourceBox);
            }
        }

        private void RefreshTargetingState()
        {
            bool isTargeting = IsTargeting;
            sourceBoxBoardController?.SetHandBoosterTargetingActive(isTargeting);

            if (isTargeting)
            {
                BeginTargetingPresentation();
            }
            else
            {
                CleanupTargetingPresentation();
            }
        }

        private void BeginTargetingPresentation()
        {
            if (targetingPresentationActive || sourceBoxBoardController == null)
            {
                return;
            }

            targetingPresentationActive = true;
            CreateIndicatorsRoot();
            IReadOnlyList<SourceBox> sourceBoxes = sourceBoxBoardController.SpawnedSourceBoxes;
            for (int i = 0; i < sourceBoxes.Count; i++)
            {
                ObserveSource(sourceBoxes[i]);
                RefreshSourcePreview(sourceBoxes[i]);
            }
        }

        private void ObserveSource(SourceBox sourceBox)
        {
            if (sourceBox == null || !observedSources.Add(sourceBox))
            {
                return;
            }

            sourceBox.StateChanged -= HandlePreviewSourceStateChanged;
            sourceBox.StateChanged += HandlePreviewSourceStateChanged;
        }

        private void RefreshSourcePreview(SourceBox sourceBox)
        {
            RemovePreviewRecord(sourceBox);
            bool isValid = targetingPresentationActive && IsValidTarget(sourceBox);
            sourceBox?.SetBoosterTargetingInputOverride(isValid);
            if (!isValid)
            {
                return;
            }

            Transform presentationRoot = sourceBox.PresentationRoot;
            PreviewRecord record = new PreviewRecord
            {
                SourceBox = sourceBox,
                PresentationRoot = presentationRoot,
                OriginalLocalPosition = presentationRoot != null ? presentationRoot.localPosition : Vector3.zero
            };

            previewRecords.Add(sourceBox, record);
            sourceBox.SetHandOpenViewPreview(!sourceBox.IsMysterySourceBox);

            if (presentationRoot != null && sourceBoxBoardController.IsFeatureSourceBox(sourceBox) && featureSourceYOffset > 0f)
            {
                Vector3 targetPosition = record.OriginalLocalPosition + Vector3.up * featureSourceYOffset;
                if (featureOffsetDuration <= 0f)
                {
                    presentationRoot.localPosition = targetPosition;
                }
                else
                {
                    record.OffsetTween = presentationRoot
                        .DOLocalMove(targetPosition, featureOffsetDuration)
                        .SetEase(Ease.OutQuad)
                        .SetLink(sourceBox.gameObject, LinkBehaviour.KillOnDestroy);
                }
            }

            CreateIndicator(record);
        }

        private void CreateIndicator(PreviewRecord record)
        {
            if (record == null || record.SourceBox == null || handIndicatorPrefab == null || handIndicatorsRoot == null)
            {
                return;
            }

            GameObject anchorObject = new GameObject($"IndicatorAnchor_{record.SourceBox.CellIndex}");
            record.IndicatorAnchor = anchorObject.transform;
            record.IndicatorAnchor.SetParent(handIndicatorsRoot, false);
            record.IndicatorAnchor.position = record.PresentationRoot != null
                ? record.PresentationRoot.position
                : record.SourceBox.transform.position;

            record.Indicator = Instantiate(handIndicatorPrefab, record.IndicatorAnchor, false);
            record.Indicator.name = $"HandIndicator_{record.SourceBox.CellIndex}";
            Transform indicatorTransform = record.Indicator.transform;
            indicatorTransform.localPosition = handIndicatorLocalPosition;
            indicatorTransform.localRotation = Quaternion.identity;

            Collider2D[] colliders = record.Indicator.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            if (indicatorBobDistance <= 0f)
            {
                return;
            }

            record.IndicatorTween = indicatorTransform
                .DOLocalMoveY(handIndicatorLocalPosition.y + indicatorBobDistance, indicatorBobDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(record.Indicator, LinkBehaviour.KillOnDestroy);
        }

        private void RemoveIndicator(SourceBox sourceBox)
        {
            if (sourceBox == null || !previewRecords.TryGetValue(sourceBox, out PreviewRecord record))
            {
                return;
            }

            KillAndDestroyIndicator(record);
        }

        private void RemovePreviewRecord(SourceBox sourceBox, bool destroyIndicator = true)
        {
            if (sourceBox == null || !previewRecords.TryGetValue(sourceBox, out PreviewRecord record))
            {
                return;
            }

            previewRecords.Remove(sourceBox);
            record.OffsetTween?.Kill(false);
            record.OffsetTween = null;
            if (record.PresentationRoot != null)
            {
                record.PresentationRoot.localPosition = record.OriginalLocalPosition;
            }

            KillAndDestroyIndicator(record, destroyIndicator);
            sourceBox.SetHandOpenViewPreview(false);
        }

        private void CleanupTargetingPresentation()
        {
            targetingPresentationActive = false;
            resolvingMysterySelection = false;
            cleanupBuffer.Clear();
            foreach (SourceBox sourceBox in previewRecords.Keys)
            {
                cleanupBuffer.Add(sourceBox);
            }

            for (int i = 0; i < cleanupBuffer.Count; i++)
            {
                RemovePreviewRecord(cleanupBuffer[i], false);
            }

            cleanupBuffer.Clear();
            transferSources.Clear();
            DestroyIndicatorsRoot();

            foreach (SourceBox sourceBox in observedSources)
            {
                if (sourceBox != null)
                {
                    sourceBox.StateChanged -= HandlePreviewSourceStateChanged;
                }
            }

            observedSources.Clear();
        }

        private static void KillAndDestroyIndicator(PreviewRecord record, bool destroyRuntimeObject = true)
        {
            if (record == null)
            {
                return;
            }

            record.IndicatorTween?.Kill(false);
            record.IndicatorTween = null;
            if (!destroyRuntimeObject)
            {
                record.Indicator = null;
                record.IndicatorAnchor = null;
                return;
            }

            GameObject target = record.IndicatorAnchor != null
                ? record.IndicatorAnchor.gameObject
                : record.Indicator;
            if (target != null && Application.isPlaying)
            {
                Destroy(target);
            }
            else if (target != null)
            {
                DestroyImmediate(target);
            }

            record.Indicator = null;
            record.IndicatorAnchor = null;
        }

        private void CreateIndicatorsRoot()
        {
            DestroyIndicatorsRoot();
            GameObject rootObject = new GameObject("HandIndicatorsRoot");
            SortingGroup sortingGroup = rootObject.AddComponent<SortingGroup>();
            sortingGroup.sortingLayerName = BoosterSortingLayerName;
            handIndicatorsRoot = rootObject.transform;
            handIndicatorsRoot.position = Vector3.zero;
            handIndicatorsRoot.rotation = Quaternion.identity;
            handIndicatorsRoot.localScale = Vector3.one;
        }

        private void DestroyIndicatorsRoot()
        {
            if (handIndicatorsRoot == null)
            {
                return;
            }

            GameObject rootObject = handIndicatorsRoot.gameObject;
            handIndicatorsRoot = null;
            if (Application.isPlaying)
            {
                Destroy(rootObject);
            }
            else
            {
                DestroyImmediate(rootObject);
            }
        }

        private void Subscribe()
        {
            if (boosterController != null)
            {
                boosterController.BoosterStateChanged -= HandleBoosterStateChanged;
                boosterController.BoosterStateChanged += HandleBoosterStateChanged;
            }

            if (sourceBoxBoardController != null)
            {
                sourceBoxBoardController.SourceBoxSpawned -= HandleSourceBoxSpawned;
                sourceBoxBoardController.SourceBoxSpawned += HandleSourceBoxSpawned;
            }

            if (targetLaneController != null)
            {
                targetLaneController.TargetTransferStateChanged -= HandleTargetTransferStateChanged;
                targetLaneController.TargetTransferStateChanged += HandleTargetTransferStateChanged;
            }
        }

        private void Unsubscribe()
        {
            if (boosterController != null)
            {
                boosterController.BoosterStateChanged -= HandleBoosterStateChanged;
            }

            if (sourceBoxBoardController != null)
            {
                sourceBoxBoardController.SourceBoxSpawned -= HandleSourceBoxSpawned;
            }

            if (targetLaneController != null)
            {
                targetLaneController.TargetTransferStateChanged -= HandleTargetTransferStateChanged;
            }
        }

        private void CacheMissingReferences()
        {
            if (boosterController == null)
            {
                boosterController = FindFirstObjectByType<BoosterController>(FindObjectsInactive.Include);
            }

            if (sourceBoxBoardController == null)
            {
                sourceBoxBoardController = FindFirstObjectByType<SourceBoxBoardController>(FindObjectsInactive.Include);
            }

            if (targetLaneController == null)
            {
                targetLaneController = FindFirstObjectByType<TargetLaneController>(FindObjectsInactive.Include);
            }

            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>(FindObjectsInactive.Include);
            }
        }
    }
}

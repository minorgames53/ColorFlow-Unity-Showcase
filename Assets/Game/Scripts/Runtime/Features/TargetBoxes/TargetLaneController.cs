using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Game.Shared.Haptics;
using Gameplay.Analytics;
using Gameplay.Conveyor;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.Tutorial;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif
using UnityEngine.Serialization;

namespace Gameplay.TargetBoxes
{
    public sealed partial class TargetLaneController : MonoBehaviour, IGameplaySpeedTarget
    {
        [Header("Build References")]
        [SerializeField] private TargetBox targetBoxPrefab;
        [SerializeField] private Transform[] laneSlots = new Transform[LevelDefinition.TargetBoxLaneCount];
        [SerializeField] private MarbleColorCatalog colorCatalog;
        [SerializeField] private ConnectedTargetGroupController connectedTargetGroupController;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private Sprite mysteryPassiveSprite;
        [SerializeField] private float targetBoxYOffset;
        [SerializeField, Min(0f)] private float boxSpacing = 1f;

        [Header("Capture References")]
        [SerializeField] private ConveyorController conveyorController;
        [SerializeField] private Transform[] captureOrigins = new Transform[LevelDefinition.TargetBoxLaneCount];

        [Header("Capture Area")]
        [SerializeField] private Vector2 captureDirection = Vector2.right;
        [SerializeField, Min(0f)] private float captureDistance = 2f;
        [SerializeField, Min(0.01f)] private float captureWidth = 0.6f;
        [SerializeField] private LayerMask marbleLayerMask = Physics2D.DefaultRaycastLayers;
        [SerializeField, Min(1)] private int maxRayHits = 16;
        [SerializeField] private bool showCaptureGizmos = true;
        [SerializeField, Min(0.01f)] private float captureOriginGizmoRadius = 0.08f;

        [Header("Marble Transfer")]
        [SerializeField, Min(0f)] private float transferDuration = 0.22f;
        [FormerlySerializedAs("transferStagger")]
        [SerializeField, Min(0f)] private float marbleToBoxTweenSlowdown = 0.035f;
        [SerializeField, Min(0f)] private float transferArcHeight = 0.35f;
        [SerializeField] private Ease transferEase = Ease.OutQuad;
        [SerializeField] private Vector3 targetMarbleFinalScale = Vector3.one;
        [SerializeField, Min(0f)] private float arrivalPunchScale = 0.12f;
        [SerializeField, Min(0f)] private float arrivalPunchDuration = 0.1f;

        [Header("Lane Shift")]
        [SerializeField, Min(0f)] private float boxCompleteDelay = 0.12f;
        [SerializeField, Min(0f)] private float nextBoxDelay = 0.04f;
        [SerializeField, Min(0f)] private float laneShiftDuration = 0.18f;
        [SerializeField] private Ease laneShiftEase = Ease.OutQuad;

        [Header("Audio")]
        [SerializeField, Min(1f)] private float fillBoxMaxPitch = 1.35f;
        [SerializeField, Min(1)] private int fillBoxPitchIncreaseSteps = 5;
        [SerializeField, Min(0f)] private float fillBoxPitchResetDelay = 2f;

        [Header("Debug")]
        [SerializeField] private bool logTransferOwnership = true;

        private readonly List<TargetBox> spawnedTargetBoxes = new List<TargetBox>();
        private readonly List<LaneRuntime> lanes = new List<LaneRuntime>(LevelDefinition.TargetBoxLaneCount);
        private readonly List<TargetTransfer> activeTransfers = new List<TargetTransfer>();
        private readonly List<Tween> activeTweens = new List<Tween>();
        private readonly Dictionary<Marble, TargetTransfer> transferByMarble = new Dictionary<Marble, TargetTransfer>();
        private readonly HashSet<TargetBox> firstFullNotifiedTargets = new HashSet<TargetBox>();
        private readonly List<Marble> warningConveyorMarbles = new List<Marble>();
        private readonly Dictionary<TargetBox, int> warningClaimsByTarget = new Dictionary<TargetBox, int>();
        private Marble[] conveyorRayMarbles;
        private float[] conveyorRayDistances;
        private int fillBoxPitchStep;
        private float lastFillBoxSfxTime = -999f;
        private int nextTransferId = 1;
        private float gameplaySpeedMultiplier = 1f;
        private bool ufoGameplayMovementFrozen;
        private int pendingCompletionPresentationCount;
        private bool allLanesCompletedNotified;
        private bool allLanesCompletionPresentationCompletedNotified;

        public IReadOnlyList<TargetBox> SpawnedTargetBoxes => spawnedTargetBoxes;
        public event Action AllLanesCompleted;
        public event Action AllLanesCompletionPresentationCompleted;
        public event Action<TargetBox> TargetBoxFirstFilled;
        public event Action<TargetBox, int, bool> TargetBoxSpawned;
        public event Action<TargetBox, int> TargetBoxReachedFront;
        public event Action TargetTransferStateChanged;

        internal sealed class LaneRuntime
        {
            public int LaneIndex;
            public Transform LaneSlot;
            public Transform CaptureOrigin;
            public readonly List<TargetBox> Boxes = new List<TargetBox>();
            public bool IsTransitioning;
            public Tween TransitionTween;
            public TargetBox RevealingBox;
            public int ConnectedGroupId;
            public int ConnectedTransactionId;
        }

        internal sealed class TargetTransfer
        {
            public int TransferId;
            public Marble Marble;
            public TargetBox TargetBox;
            public Transform Slot;
            public LaneRuntime Lane;
            public int ReservedSlotIndex;
            public Tween Tween;
            public SourceBox DirectSourceBox;
            public Action ArrivalCommitted;
            public float ArrivalPunchScaleOverride = -1f;
            public float ArrivalDurationSpeedMultiplier = 1f;
            public bool IsUfoTransfer;
            public Vector3 HandTargetEndScale;
            public bool HasHandTargetEndScale;
        }

        private void OnValidate()
        {
            boxSpacing = Mathf.Max(0f, boxSpacing);
            captureDistance = Mathf.Max(0f, captureDistance);
            captureWidth = Mathf.Max(0.01f, captureWidth);
            maxRayHits = Mathf.Max(1, maxRayHits);
            captureOriginGizmoRadius = Mathf.Max(0.01f, captureOriginGizmoRadius);
            transferDuration = Mathf.Max(0f, transferDuration);
            marbleToBoxTweenSlowdown = Mathf.Max(0f, marbleToBoxTweenSlowdown);
            transferArcHeight = Mathf.Max(0f, transferArcHeight);
            arrivalPunchScale = Mathf.Max(0f, arrivalPunchScale);
            arrivalPunchDuration = Mathf.Max(0f, arrivalPunchDuration);
            ValidateHandTransferSettings();
            ValidateShuffleSettings();
            boxCompleteDelay = Mathf.Max(0f, boxCompleteDelay);
            nextBoxDelay = Mathf.Max(0f, nextBoxDelay);
            laneShiftDuration = Mathf.Max(0f, laneShiftDuration);
            fillBoxMaxPitch = Mathf.Max(1f, fillBoxMaxPitch);
            fillBoxPitchIncreaseSteps = Mathf.Max(1, fillBoxPitchIncreaseSteps);
            fillBoxPitchResetDelay = Mathf.Max(0f, fillBoxPitchResetDelay);
            EnsureFixedArraySize(ref laneSlots);
            EnsureFixedArraySize(ref captureOrigins);
        }

        private void Update()
        {
            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>(FindObjectsInactive.Include);
            }

            if (levelSessionController == null || !levelSessionController.IsPlaying ||
                ufoGameplayMovementFrozen || shuffleInProgress || lanes.Count == 0 || conveyorController == null ||
                BoosterUnlockTutorialController.BlocksAutomaticGameplay)
            {
                return;
            }

            EnsureConveyorRayBuffers();
            Vector2 direction = GetCaptureDirection();
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                ScanLane(lanes[laneIndex], direction);
            }
        }

        private void OnEnable()
        {
            conveyorController?.SetWarningTargetSource(this);
        }

        private void OnDisable()
        {
            conveyorController?.ClearWarningTargetSource(this);
            connectedTargetGroupController?.ResetRuntime();
            KillRuntimeTweens();
            ResetLaneCompletionSignals();
        }

        public void SetGameplaySpeedMultiplier(float multiplier)
        {
            gameplaySpeedMultiplier = Mathf.Max(0.01f, multiplier);
            connectedTargetGroupController?.SetGameplaySpeedMultiplier(gameplaySpeedMultiplier);
        }

        public void SetUfoGameplayMovementFrozen(bool isFrozen)
        {
            // UFO freezes new conveyor-to-target acquisition, but transfers that already own
            // a TargetBox reservation must keep their tween and reservation lifecycle intact.
            ufoGameplayMovementFrozen = isFrozen;
        }

        private void OnDrawGizmos()
        {
            if (!showCaptureGizmos || captureOrigins == null)
            {
                return;
            }

            Vector3 direction = captureDirection.sqrMagnitude > 0f
                ? (Vector3)captureDirection.normalized
                : Vector3.right;
            Vector3 side = new Vector3(-direction.y, direction.x, 0f);
            float halfWidth = captureWidth * 0.5f;

            Color previousColor = Gizmos.color;
            for (int i = 0; i < captureOrigins.Length; i++)
            {
                Transform captureOrigin = captureOrigins[i];
                if (captureOrigin == null)
                {
                    continue;
                }

                Vector3 origin = captureOrigin.position;
                Vector3 end = origin + direction * captureDistance;
                Vector3 startLeft = origin - side * halfWidth;
                Vector3 startRight = origin + side * halfWidth;
                Vector3 endLeft = end - side * halfWidth;
                Vector3 endRight = end + side * halfWidth;

                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(origin, captureOriginGizmoRadius);
                Gizmos.DrawLine(startLeft, endLeft);
                Gizmos.DrawLine(startRight, endRight);
                Gizmos.DrawLine(startLeft, startRight);
                Gizmos.DrawLine(endLeft, endRight);

                Gizmos.color = Color.white;
                DrawArrowHead(end, direction);
            }

            Gizmos.color = previousColor;
        }

        public void Build(LevelDefinition levelDefinition)
        {
            if (!CanBuild(levelDefinition))
            {
                return;
            }

            Clear();
            fillBoxPitchStep = 0;

            IReadOnlyList<TargetBoxLaneData> targetBoxLanes = levelDefinition.TargetBoxLanes;
            int laneCount = Mathf.Min(LevelDefinition.TargetBoxLaneCount, targetBoxLanes.Count);
            for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
            {
                BuildLane(laneIndex, targetBoxLanes[laneIndex]);
            }

            if (connectedTargetGroupController != null &&
                !connectedTargetGroupController.Build(this, colorCatalog))
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' could not build Connected Target groups.", this);
                Clear();
            }
        }

        public bool CanBuild(LevelDefinition levelDefinition)
        {
            return ValidateBuildInput(levelDefinition) &&
                   ValidateCatalogEntries(levelDefinition) &&
                   (connectedTargetGroupController == null || connectedTargetGroupController.ValidateConfiguration(levelDefinition));
        }

        public bool CanConveyorStillFillAnyTarget(ConveyorController conveyor)
        {
            return CanConveyorStillFillAnyTarget(conveyor, null);
        }

        internal bool CanConveyorStillFillAnyTarget(
            ConveyorController conveyor,
            ConveyorEntryZone conveyorEntryZone)
        {
            if (conveyor == null)
            {
                return false;
            }

            if (activeTransfers.Count > 0)
            {
                return true;
            }

            for (int i = 0; i < lanes.Count; i++)
            {
                LaneRuntime lane = lanes[i];
                if (lane == null)
                {
                    continue;
                }

                if (lane.IsTransitioning)
                {
                    bool isWaitingForRuntimeUnlock =
                        lane.TransitionTween == null &&
                        lane.RevealingBox != null &&
                        lane.RevealingBox.IsRuntimeLocked &&
                        lane.Boxes.Count > 0 &&
                        lane.Boxes[0] == lane.RevealingBox;
                    if (!isWaitingForRuntimeUnlock)
                    {
                        return true;
                    }
                }

                if (lane.Boxes.Count == 0 || lane.Boxes[0] == null)
                {
                    continue;
                }

                MarbleColorId activeColor = lane.Boxes[0].ColorId;
                if (HasReservableConsecutiveBox(lane, activeColor) &&
                    (conveyor.HasCapturableMarbleWithColor(activeColor) ||
                     conveyorEntryZone != null &&
                     conveyorEntryZone.HasActiveTransferMarbleWithColor(activeColor)))
                {
                    return true;
                }
            }

            return false;
        }


        public void Clear()
        {
            connectedTargetGroupController?.ResetRuntime();
            KillRuntimeTweens();
            ResetLaneCompletionSignals();

            for (int i = 0; i < spawnedTargetBoxes.Count; i++)
            {
                TargetBox targetBox = spawnedTargetBoxes[i];
                if (targetBox == null)
                {
                    continue;
                }

                DestroyGameObject(targetBox.gameObject);
            }

            spawnedTargetBoxes.Clear();
            lanes.Clear();
            activeTransfers.Clear();
            firstFullNotifiedTargets.Clear();
            pendingHandArrivalsBySource.Clear();
            handCompletionBatchByTarget.Clear();
            fillBoxPitchStep = 0;
            lastFillBoxSfxTime = -999f;
        }

        public void AbortRuntimeTransactions()
        {
            KillRuntimeTweens();
        }

        private void BuildLane(int laneIndex, TargetBoxLaneData laneData)
        {
            if (laneData == null)
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build lane {laneIndex} because lane data is null.", this);
                return;
            }

            LaneRuntime lane = new LaneRuntime
            {
                LaneIndex = laneIndex,
                LaneSlot = laneSlots[laneIndex],
                CaptureOrigin = captureOrigins != null && laneIndex < captureOrigins.Length ? captureOrigins[laneIndex] : null
            };

            lanes.Add(lane);

            IReadOnlyList<TargetBoxData> boxes = laneData.Boxes;
            for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
            {
                TargetBoxData boxData = boxes[boxIndex];
                if (boxData == null)
                {
                    Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build lane {laneIndex} box {boxIndex} because box data is null.", this);
                    continue;
                }

                if (!colorCatalog.TryGetEntry(boxData.ColorId, out MarbleColorCatalog.Entry colorEntry))
                {
                    continue;
                }

                bool isActive = boxIndex == 0 && !boxData.HasConnectedTargetGroup;
                TargetBox targetBox = Instantiate(targetBoxPrefab, laneSlots[laneIndex]);
                targetBox.name = $"TargetBox_{laneIndex}_{boxIndex}";
                targetBox.transform.localPosition = GetLaneBoxLocalPosition(boxIndex);
                targetBox.transform.localRotation = Quaternion.identity;
                targetBox.transform.localScale = Vector3.one;

                targetBox.Initialize(
                    boxData.ColorId,
                    boxData.IsMystery,
                    isActive,
                    colorEntry.TargetBoxActiveSprite,
                    colorEntry.TargetBoxPassiveSprite,
                    mysteryPassiveSprite,
                    boxData.ConnectedTargetGroupId,
                    boxData.IsLocked);

                spawnedTargetBoxes.Add(targetBox);
                lane.Boxes.Add(targetBox);
#if UNITY_EDITOR
                MarbleDebugTracker.RegisterTarget(targetBox, laneIndex, boxIndex);
#endif
                TargetBoxSpawned?.Invoke(targetBox, laneIndex, boxIndex == 0);
            }
        }

        private void ScanLane(LaneRuntime lane, Vector2 direction)
        {
            if (levelSessionController == null || !levelSessionController.IsPlaying ||
                lane == null || lane.CaptureOrigin == null || lane.Boxes.Count == 0)
            {
                return;
            }

            if (lane.IsTransitioning)
            {
                TryAccelerateTransitionForMatchingMarble(lane, direction);
                return;
            }

            TargetBox activeBox = lane.Boxes[0];
            if (activeBox == null || activeBox.IsCompleting || !HasReservableConsecutiveBox(lane, activeBox.ColorId))
            {
                return;
            }

            int hitCount = conveyorController.GetMarblesInCaptureArea(
                lane.CaptureOrigin.position,
                direction,
                captureDistance,
                captureWidth,
                marbleLayerMask,
                conveyorRayMarbles,
                conveyorRayDistances);

            if (hitCount <= 0)
            {
                return;
            }

            for (int i = 0; i < hitCount; i++)
            {
                if (activeBox.IsCompleting || !HasReservableConsecutiveBox(lane, activeBox.ColorId))
                {
                    return;
                }

                Marble marble = GetValidConveyorMarble(conveyorRayMarbles[i]);
                if (marble == null)
                {
                    continue;
                }

                TryAssignMarbleToLane(lane, activeBox.ColorId, marble);
            }
        }

        internal int CountMatchableConveyorMarbles()
        {
            warningClaimsByTarget.Clear();
            warningConveyorMarbles.Clear();
            if (levelSessionController == null || !levelSessionController.IsPlaying ||
                ufoGameplayMovementFrozen || shuffleInProgress || conveyorController == null ||
                BoosterUnlockTutorialController.BlocksAutomaticGameplay)
            {
                return 0;
            }

            conveyorController.GetOccupiedMarbles(warningConveyorMarbles);
            int matchable = 0;
            foreach (Marble candidate in warningConveyorMarbles)
            {
                Marble marble = GetValidConveyorMarble(candidate);
                if (marble == null || marble.IsOwnedByUfo || transferByMarble.ContainsKey(marble)) continue;
                foreach (LaneRuntime lane in lanes)
                {
                    if (lane == null || lane.IsTransitioning || lane.CaptureOrigin == null || lane.Boxes.Count == 0)
                        continue;
                    TargetBox activeBox = lane.Boxes[0];
                    if (activeBox == null || activeBox.IsCompleting || marble.ColorId != activeBox.ColorId ||
                        !TryFindConsecutiveTargetForNormalClaim(lane, marble.ColorId, warningClaimsByTarget, out TargetBox target))
                        continue;

                    // Reuse normal gameplay's non-mutating claim budget. No target reservations
                    // are created, and one remaining target slot cannot accept multiple marbles.
                    warningClaimsByTarget.TryGetValue(target, out int claims);
                    warningClaimsByTarget[target] = claims + 1;
                    matchable++;
                    break;
                }
            }

            // Position along the conveyor is deliberately irrelevant: capture-area arrival is
            // a movement concern, whereas this warning asks which current targets can accept.
            warningConveyorMarbles.Clear();
            warningClaimsByTarget.Clear();
            return matchable;
        }

        private void GetImminentNormalConveyorClaims(
            Dictionary<TargetBox, int> claimsByTarget)
        {
            claimsByTarget.Clear();
            if (levelSessionController == null || !levelSessionController.IsPlaying ||
                ufoGameplayMovementFrozen || shuffleInProgress || conveyorController == null ||
                BoosterUnlockTutorialController.BlocksAutomaticGameplay)
            {
                return;
            }

            EnsureConveyorRayBuffers();
            Vector2 direction = GetCaptureDirection();
            HashSet<Marble> claimedConveyorMarbles = new HashSet<Marble>();
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                LaneRuntime lane = lanes[laneIndex];
                if (lane == null || lane.IsTransitioning || lane.CaptureOrigin == null ||
                    lane.Boxes.Count == 0)
                {
                    continue;
                }

                TargetBox activeBox = lane.Boxes[0];
                if (activeBox == null || activeBox.IsCompleting ||
                    !HasReservableConsecutiveBox(lane, activeBox.ColorId))
                {
                    continue;
                }

                int hitCount = conveyorController.GetMarblesInCaptureArea(
                    lane.CaptureOrigin.position,
                    direction,
                    captureDistance,
                    captureWidth,
                    marbleLayerMask,
                    conveyorRayMarbles,
                    conveyorRayDistances);
                for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    Marble marble = GetValidConveyorMarble(conveyorRayMarbles[hitIndex]);
                    if (marble == null || marble.ColorId != activeBox.ColorId ||
                        marble.IsOwnedByUfo || transferByMarble.ContainsKey(marble) ||
                        claimedConveyorMarbles.Contains(marble) ||
                        !TryFindConsecutiveTargetForNormalClaim(
                            lane,
                            activeBox.ColorId,
                            claimsByTarget,
                            out TargetBox targetBox))
                    {
                        continue;
                    }

                    claimsByTarget.TryGetValue(targetBox, out int currentClaimCount);
                    claimsByTarget[targetBox] = currentClaimCount + 1;
                    claimedConveyorMarbles.Add(marble);
                }
            }
        }

        private static bool TryFindConsecutiveTargetForNormalClaim(
            LaneRuntime lane,
            MarbleColorId colorId,
            IReadOnlyDictionary<TargetBox, int> claimsByTarget,
            out TargetBox targetBox)
        {
            targetBox = null;
            for (int depth = 0; depth < lane.Boxes.Count; depth++)
            {
                TargetBox candidate = lane.Boxes[depth];
                if (candidate == null || candidate.ColorId != colorId)
                {
                    return false;
                }

                claimsByTarget.TryGetValue(candidate, out int currentClaimCount);
                if (candidate.CanReserve(colorId) &&
                    candidate.AvailableReservationCount > currentClaimCount)
                {
                    targetBox = candidate;
                    return true;
                }
            }

            return false;
        }

        private void TryAccelerateTransitionForMatchingMarble(LaneRuntime lane, Vector2 direction)
        {
            if (lane == null ||
                lane.CaptureOrigin == null ||
                !TryGetTransitionTargetColor(lane, out MarbleColorId targetColor) ||
                !HasMatchingCapturableMarble(lane, direction, targetColor))
            {
                return;
            }

            if (lane.TransitionTween != null &&
                lane.TransitionTween.IsActive() &&
                lane.TransitionTween.IsPlaying())
            {
                lane.TransitionTween.Complete(true);
                if (!lane.IsTransitioning)
                {
                    ScanLane(lane, direction);
                }

                return;
            }

            if (lane.RevealingBox != null && lane.RevealingBox.TryCompleteReveal() && !lane.IsTransitioning)
            {
                ScanLane(lane, direction);
            }
        }

        private bool TryGetTransitionTargetColor(LaneRuntime lane, out MarbleColorId colorId)
        {
            colorId = MarbleColorId.None;
            if (lane == null || lane.Boxes.Count == 0)
            {
                return false;
            }

            if (lane.RevealingBox != null)
            {
                colorId = lane.RevealingBox.ColorId;
                return MarbleColorCatalog.IsGameplayColor(colorId);
            }

            TargetBox firstBox = lane.Boxes[0];
            if (firstBox != null && firstBox.IsCompleting)
            {
                if (lane.Boxes.Count < 2 || lane.Boxes[1] == null)
                {
                    return false;
                }

                colorId = lane.Boxes[1].ColorId;
                return MarbleColorCatalog.IsGameplayColor(colorId);
            }

            if (firstBox == null)
            {
                return false;
            }

            colorId = firstBox.ColorId;
            return MarbleColorCatalog.IsGameplayColor(colorId);
        }

        private bool HasMatchingCapturableMarble(LaneRuntime lane, Vector2 direction, MarbleColorId colorId)
        {
            int hitCount = conveyorController.GetMarblesInCaptureArea(
                lane.CaptureOrigin.position,
                direction,
                captureDistance,
                captureWidth,
                marbleLayerMask,
                conveyorRayMarbles,
                conveyorRayDistances);

            for (int i = 0; i < hitCount; i++)
            {
                Marble marble = GetValidConveyorMarble(conveyorRayMarbles[i]);
                if (marble != null && marble.ColorId == colorId)
                {
                    return true;
                }
            }

            return false;
        }

        private Marble GetValidConveyorMarble(Marble marble)
        {
            if (marble == null || marble.IsTransferringToTarget)
            {
                return null;
            }

            int layerMask = 1 << marble.gameObject.layer;
            if ((marbleLayerMask.value & layerMask) == 0)
            {
                return null;
            }

            return conveyorController != null && conveyorController.IsMarbleOnConveyor(marble) ? marble : null;
        }

        private bool TryAssignMarbleToLane(LaneRuntime lane, MarbleColorId activeColorId, Marble marble)
        {
            if (lane == null || marble == null || marble.ColorId != activeColorId)
            {
                return false;
            }

            if (transferByMarble.TryGetValue(marble, out TargetTransfer existingTransfer))
            {
                LogTransferOwnership($"Reject marble '{marble.name}' ({marble.GetInstanceID()}) because it is already owned by {DescribeTransfer(existingTransfer)}.");
                return false;
            }

            if (!TryFindConsecutiveReservableBox(lane, activeColorId, out TargetBox targetBox))
            {
                LogTransferOwnership($"Reject marble '{marble.name}' ({marble.GetInstanceID()}) for lane {lane.LaneIndex}: no reservable consecutive '{activeColorId}' TargetBox.");
                return false;
            }

            if (!marble.TryBeginTargetTransfer())
            {
                LogTransferOwnership($"Reject marble '{marble.name}' ({marble.GetInstanceID()}) for '{targetBox.name}' because Marble.TryBeginTargetTransfer failed.");
                return false;
            }

            int reservedSlotIndex = targetBox.ReservedSlotCount;
            if (!targetBox.TryReserveSlot(marble.ColorId, out Transform slot))
            {
                marble.CancelTargetTransfer();
                LogTransferOwnership($"Reject marble '{marble.name}' ({marble.GetInstanceID()}) for '{targetBox.name}' because TargetBox.TryReserveSlot failed. Reserved {targetBox.ReservedSlotCount}, Arrived {targetBox.ArrivedMarbleCount}.");
                return false;
            }

            if (conveyorController == null || !conveyorController.RemoveMarble(marble))
            {
                targetBox.CancelLastReservation();
                marble.CancelTargetTransfer();
                LogTransferOwnership($"Reject marble '{marble.name}' ({marble.GetInstanceID()}) for '{targetBox.name}' slot {reservedSlotIndex} because Conveyor.RemoveMarble failed.");
                return false;
            }

            marble.transform.SetParent(transform, true);
            marble.PrepareForTargetTransfer();

            TargetTransfer transfer = new TargetTransfer
            {
                TransferId = nextTransferId++,
                Marble = marble,
                TargetBox = targetBox,
                Slot = slot,
                Lane = lane,
                ReservedSlotIndex = reservedSlotIndex
            };

            activeTransfers.Add(transfer);
            transferByMarble.Add(marble, transfer);
#if UNITY_EDITOR
            DebugTrackTransfer(transfer, "Conveyor -> Target transfer committed");
#endif
            LogTransferOwnership($"Reserve {DescribeTransfer(transfer)}. Box reserved {targetBox.ReservedSlotCount}, arrived {targetBox.ArrivedMarbleCount}.");
            StartTransferToSlot(transfer, reservedSlotIndex * marbleToBoxTweenSlowdown);

            return true;
        }

        private bool HasReservableConsecutiveBox(LaneRuntime lane, MarbleColorId colorId)
        {
            return TryFindConsecutiveReservableBox(lane, colorId, out _);
        }

        private bool TryFindConsecutiveReservableBox(LaneRuntime lane, MarbleColorId colorId, out TargetBox targetBox)
        {
            targetBox = null;
            if (lane == null)
            {
                return false;
            }

            for (int i = 0; i < lane.Boxes.Count; i++)
            {
                TargetBox candidate = lane.Boxes[i];
                if (candidate == null || candidate.ColorId != colorId)
                {
                    return false;
                }

                if (candidate.CanReserve(colorId))
                {
                    targetBox = candidate;
                    return true;
                }
            }

            return false;
        }

        private void StartTransferToSlot(
            TargetTransfer transfer,
            float durationSlowdown,
            float startDelay = 0f)
        {
            if (transfer == null || transfer.Marble == null || transfer.Slot == null)
            {
                return;
            }

            if (!IsCurrentTransfer(transfer))
            {
                LogTransferOwnership($"Skip starting stale transfer {DescribeTransfer(transfer)}.");
                return;
            }

            transfer.Tween?.Kill(false);

            Transform marbleTransform = transfer.Marble.transform;
            Vector3 startPosition = marbleTransform.position;
            bool isHandTransfer = transfer.DirectSourceBox != null;

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            if (startDelay > 0f)
            {
                sequence.AppendInterval(GetEffectiveDuration(startDelay));
            }

            sequence.AppendCallback(() =>
            {
                AudioManager.Instance?.PlaySfx(AudioKey.MarbleToBox);
                HapticManager.Instance?.Play(HapticType.Rigid, true);
            });

            if (isHandTransfer)
            {
                AppendHandTransferAnimation(sequence, transfer, marbleTransform, transfer.Marble);
            }
            else
            {
                float progress = 0f;
                Tween movementTween = DOTween
                    .To(
                        () => progress,
                        value =>
                        {
                            progress = value;
                            if (marbleTransform != null)
                            {
                                marbleTransform.position = EvaluateTransferPosition(
                                    startPosition,
                                    transfer.Slot,
                                    value,
                                    transferArcHeight,
                                    0.5f);
                            }
                        },
                        1f,
                        GetEffectiveDuration(transferDuration + Mathf.Max(0f, durationSlowdown)))
                    .SetEase(transferEase);
                sequence.Append(movementTween);
            }

            sequence.OnComplete(() => CompleteSlotTransfer(transfer));

            transfer.Tween = sequence;
            TrackTween(sequence);
        }

        private void CompleteSlotTransfer(TargetTransfer transfer)
        {
            if (transfer == null || transfer.Marble == null || transfer.TargetBox == null || transfer.Slot == null)
            {
                return;
            }

            if (!IsCurrentTransfer(transfer))
            {
                LogTransferOwnership($"Ignore stale slot completion for {DescribeTransfer(transfer)}.");
                return;
            }

            Transform marbleTransform = transfer.Marble.transform;
            bool isHandTransfer = transfer.DirectSourceBox != null;
            Vector3 finalScale = isHandTransfer && transfer.HasHandTargetEndScale
                ? transfer.HandTargetEndScale
                : targetMarbleFinalScale;
            float activePunchScale = isHandTransfer ? handArrivalPunchScale : arrivalPunchScale;
            if (transfer.ArrivalPunchScaleOverride >= 0f)
            {
                activePunchScale = transfer.ArrivalPunchScaleOverride;
            }
            float activePunchDuration = isHandTransfer ? handArrivalPunchDuration : arrivalPunchDuration;
            activePunchDuration /= Mathf.Max(0.01f, transfer.ArrivalDurationSpeedMultiplier);
            int activePunchVibrato = isHandTransfer ? handArrivalPunchVibrato : 10;
            float activePunchElasticity = isHandTransfer ? handArrivalPunchElasticity : 1f;
            marbleTransform.position = transfer.Slot.position;
            marbleTransform.SetParent(transfer.Slot, true);
            marbleTransform.localPosition = Vector3.zero;
            marbleTransform.localRotation = Quaternion.identity;
            marbleTransform.localScale = finalScale;

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            if (activePunchDuration > 0f && activePunchScale > 0f)
            {
                sequence.Append(marbleTransform.DOPunchScale(
                    Vector3.one * activePunchScale,
                    GetEffectiveDuration(activePunchDuration),
                    activePunchVibrato,
                    activePunchElasticity));
            }

            sequence.OnComplete(() =>
            {
                if (marbleTransform != null)
                {
                    marbleTransform.localScale = finalScale;
                }

                ConfirmTransferArrival(transfer);
            });

            transfer.Tween = sequence;
            TrackTween(sequence);
        }

        private void ConfirmTransferArrival(TargetTransfer transfer)
        {
            if (!IsCurrentTransfer(transfer))
            {
                LogTransferOwnership($"Ignore stale arrival for {DescribeTransfer(transfer)}.");
                return;
            }

            activeTransfers.Remove(transfer);
            transferByMarble.Remove(transfer.Marble);
            if (transfer.TargetBox == null)
            {
                Action missingTargetArrivalCommitted = transfer.ArrivalCommitted;
                transfer.ArrivalCommitted = null;
                missingTargetArrivalCommitted?.Invoke();
                TargetTransferStateChanged?.Invoke();
                return;
            }

            transfer.TargetBox.ConfirmMarbleArrival();
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(transfer.Marble, MarbleDebugLocation.TargetBox, transfer.TargetBox,
                MarbleDebugTracker.TargetDetail(transfer.TargetBox, transfer.ReservedSlotIndex), "Target arrival committed");
#endif
            ConfirmHandSourceArrival(transfer.DirectSourceBox);
            LogTransferOwnership($"Arrive {DescribeTransfer(transfer)}. Box reserved {transfer.TargetBox.ReservedSlotCount}, arrived {transfer.TargetBox.ArrivedMarbleCount}.");
            LevelAnalyticsTracker.Instance?.UpdateProgressSnapshot();

            if (transfer.TargetBox.IsComplete && firstFullNotifiedTargets.Add(transfer.TargetBox))
            {
                TargetBoxFirstFilled?.Invoke(transfer.TargetBox);
            }

            if (handCompletionBatchByTarget.TryGetValue(transfer.TargetBox, out HandCompletionBatch handBatch))
            {
                if (transfer.TargetBox.IsComplete)
                {
                    TryBeginHandTargetCompletion(handBatch, transfer.TargetBox);
                }

                TryFinalizeHandCompletionBatchIfReady(handBatch);
            }
            else if (transfer.IsUfoTransfer &&
                     transfer.TargetBox.IsComplete &&
                     !transfer.TargetBox.HasConnectedTargetGroup &&
                     !IsFrontTarget(transfer.Lane, transfer.TargetBox) &&
                     transfer.TargetBox.TryBeginCompletion())
            {
                StartCompleteUfoOffFrontBox(transfer.Lane, transfer.TargetBox);
            }
            else if (transfer.TargetBox.IsActive &&
                     transfer.TargetBox.IsComplete &&
                     transfer.TargetBox.TryBeginCompletion())
            {
                RouteCompletedTarget(transfer.Lane, transfer.TargetBox);
            }

            Action arrivalCommitted = transfer.ArrivalCommitted;
            transfer.ArrivalCommitted = null;
            arrivalCommitted?.Invoke();
            TargetTransferStateChanged?.Invoke();
        }


        private void RouteCompletedTarget(LaneRuntime lane, TargetBox targetBox)
        {
            if (targetBox == null)
            {
                return;
            }

            if (!targetBox.HasConnectedTargetGroup)
            {
                StartCompleteBox(lane, targetBox);
                return;
            }

            if (connectedTargetGroupController == null ||
                !connectedTargetGroupController.TryHandleTargetFull(targetBox))
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' could not route full Connected Target '{targetBox.name}' (group {targetBox.ConnectedTargetGroupId}). The target will remain full and locked to avoid a partial normal completion.", this);
            }
        }

        private void StartCompleteBox(LaneRuntime lane, TargetBox targetBox)
        {
            if (lane == null || targetBox == null)
            {
                return;
            }

            StartTargetCompletionAnimation(
                lane,
                targetBox,
                deactivateCompletedBox => CompleteActiveBox(
                    lane,
                    targetBox,
                    deactivateCompletedBox));
        }

        private void StartTargetCompletionAnimation(
            LaneRuntime lane,
            TargetBox targetBox,
            Action<bool> onReadyToRemove)
        {
            if (lane == null || targetBox == null || onReadyToRemove == null)
            {
                return;
            }

            PlayFillBoxSfx();
            lane.IsTransitioning = true;
            lane.RevealingBox = null;

            pendingCompletionPresentationCount++;
            bool presentationCompleted = false;
            Action<bool> completePresentation = deactivateTargetBox =>
            {
                if (presentationCompleted)
                {
                    return;
                }

                presentationCompleted = true;
                if (deactivateTargetBox && targetBox != null)
                {
                    targetBox.gameObject.SetActive(false);
                }

                pendingCompletionPresentationCount = Mathf.Max(0, pendingCompletionPresentationCount - 1);
                NotifyLaneCompletionSignals();
            };

            bool fillAnimationStarted = targetBox.PlayFillAnimation(() => completePresentation(true));
            if (!fillAnimationStarted)
            {
                completePresentation(false);
            }

            Tween tween = DOVirtual
                .DelayedCall(
                    GetEffectiveDuration(boxCompleteDelay),
                    () => onReadyToRemove(!fillAnimationStarted))
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            lane.TransitionTween = tween;
            TrackTween(tween);
        }

        private static bool IsFrontTarget(LaneRuntime lane, TargetBox targetBox)
        {
            return lane != null && targetBox != null &&
                   lane.Boxes.Count > 0 && lane.Boxes[0] == targetBox;
        }

        private void CompleteActiveBox(LaneRuntime lane, TargetBox completedBox, bool deactivateCompletedBox)
        {
            if (lane == null || completedBox == null || lane.Boxes.Count == 0 || lane.Boxes[0] != completedBox)
            {
                if (lane != null)
                {
                    lane.IsTransitioning = false;
                    lane.TransitionTween = null;
                    lane.RevealingBox = null;
                }

                TargetTransferStateChanged?.Invoke();
                return;
            }

            if (deactivateCompletedBox)
            {
                completedBox.gameObject.SetActive(false);
            }

            lane.Boxes.RemoveAt(0);

            if (lane.Boxes.Count == 0)
            {
                NotifyLaneCompletionSignals();

                lane.IsTransitioning = false;
                lane.TransitionTween = null;
                lane.RevealingBox = null;
                TargetTransferStateChanged?.Invoke();
                return;
            }

            StartLaneShiftTween(lane, () =>
            {
                if (lane.Boxes.Count > 0 && lane.Boxes[0] != null)
                {
                    TargetBox nextBox = lane.Boxes[0];
                    TargetBoxReachedFront?.Invoke(nextBox, lane.LaneIndex);
                    if (nextBox.HasConnectedTargetGroup)
                    {
                        lane.RevealingBox = null;
                        lane.IsTransitioning = false;
                        connectedTargetGroupController?.NotifyTargetReachedFront(nextBox);
                        TargetTransferStateChanged?.Invoke();
                        return;
                    }

                    lane.RevealingBox = nextBox;
                    nextBox.SetActiveView(() =>
                    {
                        lane.RevealingBox = null;
                        lane.IsTransitioning = false;
                        connectedTargetGroupController?.NotifyTargetRevealed(nextBox);
                        if (nextBox.IsComplete && nextBox.TryBeginCompletion())
                        {
                            RouteCompletedTarget(lane, nextBox);
                        }
                        else
                        {
                            TargetTransferStateChanged?.Invoke();
                            EnsureConveyorRayBuffers();
                            ScanLane(lane, GetCaptureDirection());
                        }
                    });
                }
                else
                {
                    lane.IsTransitioning = false;
                    lane.RevealingBox = null;
                    TargetTransferStateChanged?.Invoke();
                }
            });
        }

        private void StartLaneShiftTween(LaneRuntime lane, Action onCompleted)
        {
            if (lane == null)
            {
                return;
            }

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            if (nextBoxDelay > 0f)
            {
                sequence.AppendInterval(GetEffectiveDuration(nextBoxDelay));
            }

            for (int i = 0; i < lane.Boxes.Count; i++)
            {
                TargetBox targetBox = lane.Boxes[i];
                if (targetBox == null)
                {
                    continue;
                }

                sequence.Join(targetBox.transform
                    .DOLocalMove(GetLaneBoxLocalPosition(i), GetEffectiveDuration(laneShiftDuration))
                    .SetEase(laneShiftEase));
            }

            sequence.OnComplete(() =>
            {
                lane.TransitionTween = null;
                onCompleted?.Invoke();
            });

            lane.TransitionTween = sequence;
            TrackTween(sequence);
        }

        internal bool TryGetLaneIndex(TargetBox target, out int laneIndex)
        {
            laneIndex = -1;
            if (target == null)
            {
                return false;
            }

            for (int i = 0; i < lanes.Count; i++)
            {
                LaneRuntime lane = lanes[i];
                if (lane != null && lane.Boxes.Contains(target))
                {
                    laneIndex = lane.LaneIndex;
                    return true;
                }
            }

            return false;
        }

        internal bool IsCurrentTarget(int laneIndex, TargetBox target)
        {
            return target != null &&
                   TryGetLane(laneIndex, out LaneRuntime lane) &&
                   lane.Boxes.Count > 0 &&
                   lane.Boxes[0] == target &&
                   !lane.IsTransitioning;
        }

        internal bool TryGetCurrentTarget(int laneIndex, out TargetBox target)
        {
            target = null;
            if (!TryGetLane(laneIndex, out LaneRuntime lane) || lane.Boxes.Count == 0)
            {
                return false;
            }

            target = lane.Boxes[0];
            return target != null;
        }

        internal bool TryLockConnectedTarget(int laneIndex, TargetBox target)
        {
            return TryGetLane(laneIndex, out LaneRuntime lane) &&
                   lane.Boxes.Count > 0 &&
                   lane.Boxes[0] == target &&
                   !lane.IsTransitioning &&
                   target != null &&
                   target.IsActive &&
                   target.IsComplete &&
                   target.IsCompleting;
        }

        internal bool CanBeginConnectedTransition(int laneIndex, TargetBox target)
        {
            return TryLockConnectedTarget(laneIndex, target) &&
                   TryGetLane(laneIndex, out LaneRuntime lane) &&
                   lane.TransitionTween == null &&
                   lane.RevealingBox == null;
        }

        internal bool BeginConnectedTransition(
            int laneIndex,
            TargetBox target,
            int groupId,
            int transactionId)
        {
            if (groupId <= 0 || transactionId <= 0 || !CanBeginConnectedTransition(laneIndex, target) ||
                !TryGetLane(laneIndex, out LaneRuntime lane))
            {
                return false;
            }

            lane.IsTransitioning = true;
            lane.ConnectedGroupId = groupId;
            lane.ConnectedTransactionId = transactionId;
            lane.TransitionTween = null;
            lane.RevealingBox = null;
            return true;
        }

        internal bool CanPrepareConnectedCompletion(int laneIndex, TargetBox target)
        {
            return TryLockConnectedTarget(laneIndex, target);
        }

        internal bool PrepareConnectedCompletion(
            int laneIndex,
            TargetBox target,
            int groupId,
            int transactionId)
        {
            return IsConnectedTransaction(laneIndex, groupId, transactionId, out LaneRuntime lane) &&
                   lane.Boxes.Count > 0 &&
                   lane.Boxes[0] == target &&
                   target != null &&
                   target.IsComplete &&
                   target.IsCompleting;
        }

        internal void PlayConnectedCompletion(
            int laneIndex,
            TargetBox target,
            int groupId,
            int transactionId,
            Action onCompleted)
        {
            if (!PrepareConnectedCompletion(laneIndex, target, groupId, transactionId) ||
                !target.PlayFillAnimation(onCompleted))
            {
                onCompleted?.Invoke();
            }
        }

        internal bool CanFinalizeConnectedRemoval(
            int laneIndex,
            TargetBox target,
            int groupId,
            int transactionId)
        {
            return IsConnectedTransaction(laneIndex, groupId, transactionId, out LaneRuntime lane) &&
                   lane.TransitionTween == null &&
                   lane.RevealingBox == null &&
                   lane.Boxes.Count > 0 &&
                   lane.Boxes[0] == target &&
                   target != null &&
                   target.IsComplete &&
                   target.IsCompleting;
        }

        internal bool FinalizeConnectedRemoval(
            int laneIndex,
            TargetBox target,
            int groupId,
            int transactionId)
        {
            if (!CanFinalizeConnectedRemoval(laneIndex, target, groupId, transactionId) ||
                !TryGetLane(laneIndex, out LaneRuntime lane))
            {
                return false;
            }

            target.gameObject.SetActive(false);
            lane.Boxes.RemoveAt(0);
            return true;
        }

        internal bool CanBeginConnectedShiftAndReveal(int laneIndex, int groupId, int transactionId)
        {
            return IsConnectedTransaction(laneIndex, groupId, transactionId, out LaneRuntime lane) &&
                   lane.TransitionTween == null &&
                   lane.RevealingBox == null;
        }

        internal void BeginConnectedShiftAndReveal(
            int laneIndex,
            int groupId,
            int transactionId,
            Action onReady)
        {
            if (!CanBeginConnectedShiftAndReveal(laneIndex, groupId, transactionId) ||
                !TryGetLane(laneIndex, out LaneRuntime lane))
            {
                return;
            }

            if (lane.Boxes.Count == 0)
            {
                onReady?.Invoke();
                return;
            }

            StartLaneShiftTween(lane, () =>
            {
                if (!IsConnectedTransaction(laneIndex, groupId, transactionId, out _))
                {
                    return;
                }

                onReady?.Invoke();
            });
        }

        internal bool CanActivateConnectedNextTarget(int laneIndex, int groupId, int transactionId)
        {
            return IsConnectedTransaction(laneIndex, groupId, transactionId, out LaneRuntime lane) &&
                   lane.TransitionTween == null &&
                   lane.RevealingBox == null;
        }

        internal void ActivateConnectedNextTarget(
            int laneIndex,
            int groupId,
            int transactionId,
            Action onActivated)
        {
            if (!CanActivateConnectedNextTarget(laneIndex, groupId, transactionId) ||
                !TryGetLane(laneIndex, out LaneRuntime lane))
            {
                return;
            }

            if (lane.Boxes.Count == 0 || lane.Boxes[0] == null)
            {
                onActivated?.Invoke();
                return;
            }

            TargetBox nextBox = lane.Boxes[0];
            TargetBoxReachedFront?.Invoke(nextBox, laneIndex);
            if (nextBox.HasConnectedTargetGroup)
            {
                onActivated?.Invoke();
                return;
            }

            lane.RevealingBox = nextBox;
            nextBox.SetActiveView(() =>
            {
                if (!IsConnectedTransaction(laneIndex, groupId, transactionId, out LaneRuntime currentLane) ||
                    currentLane.RevealingBox != nextBox)
                {
                    return;
                }

                currentLane.RevealingBox = null;
                connectedTargetGroupController?.NotifyTargetRevealed(nextBox);
                onActivated?.Invoke();
            });
        }

        internal bool CompleteConnectedTransition(int laneIndex, int groupId, int transactionId)
        {
            if (!CanCompleteConnectedTransition(laneIndex, groupId, transactionId) ||
                !TryGetLane(laneIndex, out LaneRuntime lane))
            {
                return false;
            }

            lane.IsTransitioning = false;
            lane.ConnectedGroupId = 0;
            lane.ConnectedTransactionId = 0;
            TargetTransferStateChanged?.Invoke();
            return true;
        }

        internal bool CanCompleteConnectedTransition(int laneIndex, int groupId, int transactionId)
        {
            return IsConnectedTransaction(laneIndex, groupId, transactionId, out LaneRuntime lane) &&
                   lane.TransitionTween == null &&
                   lane.RevealingBox == null;
        }

        internal void AbortConnectedTransition(int laneIndex, int groupId, int transactionId)
        {
            if (!IsConnectedTransaction(laneIndex, groupId, transactionId, out LaneRuntime lane))
            {
                return;
            }

            lane.TransitionTween?.Kill(false);
            lane.TransitionTween = null;
            lane.RevealingBox = null;
            lane.IsTransitioning = false;
            lane.ConnectedGroupId = 0;
            lane.ConnectedTransactionId = 0;
            TargetTransferStateChanged?.Invoke();
        }

        internal void ResumeLaneAfterConnectedTransition(int laneIndex)
        {
            if (!TryGetLane(laneIndex, out LaneRuntime lane) || lane.IsTransitioning || lane.Boxes.Count == 0)
            {
                return;
            }

            TargetBox nextBox = lane.Boxes[0];
            if (nextBox != null && nextBox.HasConnectedTargetGroup && !nextBox.IsActive)
            {
                connectedTargetGroupController?.NotifyTargetReachedFront(nextBox);
                TargetTransferStateChanged?.Invoke();
                return;
            }

            if (nextBox != null && nextBox.IsActive && nextBox.IsComplete && nextBox.TryBeginCompletion())
            {
                RouteCompletedTarget(lane, nextBox);
                TargetTransferStateChanged?.Invoke();
                return;
            }

            TargetTransferStateChanged?.Invoke();
            EnsureConveyorRayBuffers();
            ScanLane(lane, GetCaptureDirection());
        }

        internal void PlayConnectedCompletionFeedback()
        {
            PlayFillBoxSfx();
        }

        internal void NotifyConnectedTransactionCompleted()
        {
            NotifyLaneCompletionSignals();
        }

        private void NotifyLaneCompletionSignals()
        {
            if (!AreAllLanesEmpty())
            {
                return;
            }

            if (!allLanesCompletedNotified)
            {
                allLanesCompletedNotified = true;
                AllLanesCompleted?.Invoke();
            }

            if (pendingCompletionPresentationCount > 0 ||
                allLanesCompletionPresentationCompletedNotified)
            {
                return;
            }

            allLanesCompletionPresentationCompletedNotified = true;
            AllLanesCompletionPresentationCompleted?.Invoke();
        }

        private void ResetLaneCompletionSignals()
        {
            pendingCompletionPresentationCount = 0;
            allLanesCompletedNotified = false;
            allLanesCompletionPresentationCompletedNotified = false;
        }

        private bool IsConnectedTransaction(
            int laneIndex,
            int groupId,
            int transactionId,
            out LaneRuntime lane)
        {
            return TryGetLane(laneIndex, out lane) &&
                   lane.IsTransitioning &&
                   lane.ConnectedGroupId == groupId &&
                   lane.ConnectedTransactionId == transactionId;
        }

        private bool TryGetLane(int laneIndex, out LaneRuntime lane)
        {
            lane = null;
            if (laneIndex < 0 || laneIndex >= lanes.Count)
            {
                return false;
            }

            lane = lanes[laneIndex];
            return lane != null && lane.LaneIndex == laneIndex;
        }

        private bool ValidateBuildInput(LevelDefinition levelDefinition)
        {
            if (levelDefinition == null)
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build because LevelDefinition is null.", this);
                return false;
            }

            if (targetBoxPrefab == null)
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' is missing TargetBox prefab reference.", this);
                return false;
            }

            if (colorCatalog == null)
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' is missing MarbleColorCatalog reference.", this);
                return false;
            }

            if (laneSlots == null || laneSlots.Length != LevelDefinition.TargetBoxLaneCount)
            {
                int slotCount = laneSlots?.Length ?? 0;
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' must have exactly {LevelDefinition.TargetBoxLaneCount} lane slots, but has {slotCount}.", this);
                return false;
            }

            for (int i = 0; i < laneSlots.Length; i++)
            {
                if (laneSlots[i] == null)
                {
                    Debug.LogError($"{nameof(TargetLaneController)} on '{name}' is missing lane slot at index {i}.", this);
                    return false;
                }
            }

            IReadOnlyList<TargetBoxLaneData> targetBoxLanes = levelDefinition.TargetBoxLanes;
            if (targetBoxLanes == null || targetBoxLanes.Count != LevelDefinition.TargetBoxLaneCount)
            {
                int laneCount = targetBoxLanes?.Count ?? 0;
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build level '{levelDefinition.name}' because it has {laneCount} target lanes. Expected {LevelDefinition.TargetBoxLaneCount}.", this);
                return false;
            }

            if (ContainsConnectedTargetData(targetBoxLanes) && connectedTargetGroupController == null)
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' requires a Connected Target Group Controller for level '{levelDefinition.name}'.", this);
                return false;
            }

            return true;
        }

        private bool ValidateCatalogEntries(LevelDefinition levelDefinition)
        {
            bool isValid = true;
            IReadOnlyList<TargetBoxLaneData> targetBoxLanes = levelDefinition.TargetBoxLanes;

            for (int laneIndex = 0; laneIndex < targetBoxLanes.Count; laneIndex++)
            {
                TargetBoxLaneData lane = targetBoxLanes[laneIndex];
                if (lane == null)
                {
                    continue;
                }

                IReadOnlyList<TargetBoxData> boxes = lane.Boxes;
                for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                {
                    TargetBoxData box = boxes[boxIndex];
                    if (box == null)
                    {
                        continue;
                    }

                    if (!colorCatalog.TryGetEntry(box.ColorId, out MarbleColorCatalog.Entry colorEntry))
                    {
                        Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build level '{levelDefinition.name}' because target lane {laneIndex + 1}, box {boxIndex + 1} has no valid color catalog entry for '{box.ColorId}'.", this);
                        isValid = false;
                        continue;
                    }

                    if (colorEntry.TargetBoxActiveSprite == null)
                    {
                        Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build level '{levelDefinition.name}' because target lane {laneIndex + 1}, box {boxIndex + 1} color '{box.ColorId}' is missing TargetBox active sprite in MarbleColorCatalog.", this);
                        isValid = false;
                    }

                    if (colorEntry.TargetBoxPassiveSprite == null)
                    {
                        Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build level '{levelDefinition.name}' because target lane {laneIndex + 1}, box {boxIndex + 1} color '{box.ColorId}' is missing TargetBox passive sprite in MarbleColorCatalog.", this);
                        isValid = false;
                    }

                    if (box.HasConnectedTargetGroup && !colorEntry.HasConnectedBoxConnectionTint)
                    {
                        Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build level '{levelDefinition.name}' because Connected Target lane {laneIndex + 1}, box {boxIndex + 1} color '{box.ColorId}' is missing a connection tint in MarbleColorCatalog.", this);
                        isValid = false;
                    }

                    if (box.IsMystery && boxIndex > 0 && mysteryPassiveSprite == null)
                    {
                        Debug.LogError($"{nameof(TargetLaneController)} on '{name}' cannot build level '{levelDefinition.name}' because target lane {laneIndex + 1}, box {boxIndex + 1} is passive mystery and Mystery Passive Sprite is missing.", this);
                        isValid = false;
                    }
                }
            }

            return isValid;
        }

        private static bool ContainsConnectedTargetData(IReadOnlyList<TargetBoxLaneData> targetBoxLanes)
        {
            if (targetBoxLanes == null)
            {
                return false;
            }

            for (int laneIndex = 0; laneIndex < targetBoxLanes.Count; laneIndex++)
            {
                IReadOnlyList<TargetBoxData> boxes = targetBoxLanes[laneIndex]?.Boxes;
                if (boxes == null)
                {
                    continue;
                }

                for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                {
                    if (boxes[boxIndex]?.HasConnectedTargetGroup == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureConveyorRayBuffers()
        {
            if (conveyorRayMarbles == null || conveyorRayMarbles.Length != maxRayHits)
            {
                conveyorRayMarbles = new Marble[maxRayHits];
            }

            if (conveyorRayDistances == null || conveyorRayDistances.Length != maxRayHits)
            {
                conveyorRayDistances = new float[maxRayHits];
            }
        }

        private void KillRuntimeTweens()
        {
            ResetShuffleRuntime(false);
            ClearUfoTargetPreviews();

            HashSet<HandCompletionBatch> handBatches = new HashSet<HandCompletionBatch>(handCompletionBatchByTarget.Values);
            foreach (HandCompletionBatch batch in handBatches)
            {
                ReleaseHandCompletionBatch(batch);
            }

            handCompletionBatchByTarget.Clear();

            for (int i = 0; i < activeTweens.Count; i++)
            {
                activeTweens[i]?.Kill(false);
            }

            for (int i = 0; i < activeTransfers.Count; i++)
            {
                Marble marble = activeTransfers[i]?.Marble;
                if (marble == null)
                {
                    continue;
                }

                marble.CancelTargetTransfer();
                DestroyGameObject(marble.gameObject);
            }

            activeTweens.Clear();
            activeTransfers.Clear();
            transferByMarble.Clear();
            pendingHandArrivalsBySource.Clear();
        }

        private void TrackTween(Tween tween)
        {
            if (tween != null)
            {
                activeTweens.Add(tween);
            }
        }

#if UNITY_EDITOR
        private void DebugTrackTransfer(TargetTransfer transfer, string reason)
        {
            MarbleDebugTracker.SetLocation(transfer.Marble, MarbleDebugLocation.InTransit, this,
                $"Transfer {transfer.TransferId} -> {MarbleDebugTracker.TargetDetail(transfer.TargetBox, transfer.ReservedSlotIndex)}", reason);
        }

        public void DebugCollectMarbles(List<MarbleDebugObservation> result)
        {
            foreach (var transfer in activeTransfers)
                MarbleDebugTracker.Observe(result, transfer.Marble, MarbleDebugLocation.InTransit, this,
                    $"Transfer {transfer.TransferId} -> {MarbleDebugTracker.TargetDetail(transfer.TargetBox, transfer.ReservedSlotIndex)}");
            // Hand holds committed marbles while waiting for target reveal, before activeTransfers.
            var batches = new HashSet<HandCompletionBatch>(handCompletionBatchByTarget.Values);
            foreach (var batch in batches)
                if (!batch.TransfersStarted)
                    foreach (var pending in batch.Transfers)
                        MarbleDebugTracker.Observe(result, pending.Marble, MarbleDebugLocation.InTransit,
                            pending.SourceBox, "Hand committed / awaiting target reveal");
            foreach (var target in spawnedTargetBoxes)
                if (target != null)
                {
                    string position = "Completed / outside active queue";
                    foreach (var lane in lanes)
                    {
                        int depth = lane.Boxes.IndexOf(target);
                        if (depth >= 0) position = $"Current Lane {lane.LaneIndex + 1} / Depth {depth + 1}";
                    }
                    target.DebugCollectArrivedMarbles(result, marble => transferByMarble.ContainsKey(marble), position);
                }
        }
#endif

        private bool IsCurrentTransfer(TargetTransfer transfer)
        {
            return transfer != null &&
                   transfer.Marble != null &&
                   transferByMarble.TryGetValue(transfer.Marble, out TargetTransfer currentTransfer) &&
                   ReferenceEquals(currentTransfer, transfer);
        }

        private void LogTransferOwnership(string message)
        {
            if (!logTransferOwnership)
            {
                return;
            }

            Debug.Log($"{nameof(TargetLaneController)} on '{name}': {message}", this);
        }

        private static string DescribeTransfer(TargetTransfer transfer)
        {
            if (transfer == null)
            {
                return "<null transfer>";
            }

            string marbleName = transfer.Marble != null ? transfer.Marble.name : "<null marble>";
            int marbleId = transfer.Marble != null ? transfer.Marble.GetInstanceID() : 0;
            string boxName = transfer.TargetBox != null ? transfer.TargetBox.name : "<null box>";
            string slotName = transfer.Slot != null ? transfer.Slot.name : "<null slot>";
            int laneIndex = transfer.Lane != null ? transfer.Lane.LaneIndex : -1;
            return $"transfer #{transfer.TransferId}, marble '{marbleName}' ({marbleId}), lane {laneIndex}, box '{boxName}', slot {transfer.ReservedSlotIndex} '{slotName}'";
        }

        private void PlayFillBoxSfx()
        {
            float now = Time.unscaledTime;
            if (fillBoxPitchResetDelay > 0f && now - lastFillBoxSfxTime >= fillBoxPitchResetDelay)
            {
                fillBoxPitchStep = 0;
            }

            float pitch = 1f;
            if (fillBoxPitchIncreaseSteps <= 1)
            {
                pitch = fillBoxMaxPitch;
            }
            else
            {
                float progress = Mathf.Clamp01((float)fillBoxPitchStep / (fillBoxPitchIncreaseSteps - 1));
                pitch = Mathf.Lerp(1f, fillBoxMaxPitch, progress);
            }

            fillBoxPitchStep = Mathf.Min(fillBoxPitchStep + 1, fillBoxPitchIncreaseSteps - 1);
            lastFillBoxSfxTime = now;
            AudioManager.Instance?.PlaySfx(AudioKey.FillBox, pitch);
        }

        private float GetEffectiveDuration(float duration)
        {
            return Mathf.Max(0f, duration) / gameplaySpeedMultiplier;
        }

        private bool AreAllLanesEmpty()
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] != null && lanes[i].Boxes.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }

        private Vector2 GetCaptureDirection()
        {
            return captureDirection.sqrMagnitude > 0f ? captureDirection.normalized : Vector2.right;
        }

        private static Vector3 EvaluateTransferPosition(
            Vector3 startPosition,
            Transform slot,
            float progress,
            float arcHeight,
            float arcPosition)
        {
            if (slot == null)
            {
                return startPosition;
            }

            Vector3 endPosition = slot.position;
            Vector3 controlPosition = Vector3.Lerp(startPosition, endPosition, arcPosition) + Vector3.up * arcHeight;
            return EvaluateQuadraticBezier(startPosition, controlPosition, endPosition, progress);
        }

        private static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * start +
                   2f * oneMinusT * t * control +
                   t * t * end;
        }

        private static void DrawArrowHead(Vector3 position, Vector3 direction)
        {
            if (direction.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 normalizedDirection = direction.normalized;
            Vector3 perpendicular = new Vector3(-normalizedDirection.y, normalizedDirection.x, 0f);
            const float ArrowLength = 0.16f;
            const float ArrowWidth = 0.08f;

            Vector3 back = position - normalizedDirection * ArrowLength;
            Gizmos.DrawLine(position, back + perpendicular * ArrowWidth);
            Gizmos.DrawLine(position, back - perpendicular * ArrowWidth);
        }

        private Vector3 GetLaneBoxLocalPosition(int boxIndex)
        {
            return new Vector3(0f, targetBoxYOffset - (boxIndex * boxSpacing), -boxIndex);
        }

        private static void EnsureFixedArraySize(ref Transform[] array)
        {
            if (array == null)
            {
                array = new Transform[LevelDefinition.TargetBoxLaneCount];
                return;
            }

            if (array.Length == LevelDefinition.TargetBoxLaneCount)
            {
                return;
            }

            Transform[] resized = new Transform[LevelDefinition.TargetBoxLaneCount];
            int copyCount = Mathf.Min(array.Length, resized.Length);
            for (int i = 0; i < copyCount; i++)
            {
                resized[i] = array[i];
            }

            array = resized;
        }

        private static void DestroyGameObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}

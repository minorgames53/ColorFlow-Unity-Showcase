using System.Collections.Generic;
using System;
using DG.Tweening;
using Gameplay.SourceBoxes;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif

namespace Gameplay.TargetBoxes
{
    public sealed partial class TargetLaneController
    {
        [Header("Hand Direct Transfer")]
        [SerializeField, Min(0f)] private float handTransferInitialDelay;
        [InspectorName("perBallReleaseDelay")]
        [SerializeField, Min(0f)] private float handTransferStagger = 0.1f;

        [Header("Hand Transfer Physics Phase")]
        [InspectorName("physicsDuration")]
        [SerializeField, Min(0f)] private float handReleasePhysicsDuration = 0.25f;

        [Header("Hand Transfer Bounce")]
        [InspectorName("bounceDuration")]
        [SerializeField, Min(0f)] private float handBounceDuration = 0.15f;
        [InspectorName("bounceOffset")]
        [SerializeField] private Vector3 handBounceOffset = new Vector3(0.12f, 0.35f, 0f);
        [InspectorName("bounceScaleMultiplier")]
        [SerializeField, Min(0.01f)] private float handBounceScaleMultiplier = 1.2f;
        [InspectorName("bounceEase")]
        [SerializeField] private Ease handBounceEase = Ease.OutBack;

        [Header("Hand Transfer Fly")]
        [InspectorName("targetFlightDuration")]
        [SerializeField, Min(0f)] private float handTargetFlightDuration = 0.3f;
        [SerializeField, Min(0f)] private float handTransferArcHeight = 0.35f;
        [SerializeField, Range(0f, 1f)] private float handTransferArcPosition = 0.5f;
        [InspectorName("targetEndScaleMultiplier")]
        [SerializeField, Min(0.01f)] private float handTargetEndScaleMultiplier = 0.75f;
        [InspectorName("targetFlightEase")]
        [SerializeField] private Ease handTargetFlightEase = Ease.InOutQuad;

        [Header("Hand Transfer Arrival")]
        [SerializeField, Min(0f)] private float handArrivalPunchScale = 0.12f;
        [SerializeField, Min(0f)] private float handArrivalPunchDuration = 0.1f;
        [SerializeField, Min(1)] private int handArrivalPunchVibrato = 10;
        [SerializeField, Range(0f, 1f)] private float handArrivalPunchElasticity = 1f;

        private readonly Dictionary<SourceBox, int> pendingHandArrivalsBySource = new Dictionary<SourceBox, int>();
        private readonly Dictionary<TargetBox, HandCompletionBatch> handCompletionBatchByTarget = new Dictionary<TargetBox, HandCompletionBatch>();

        private sealed class PendingHandTransfer
        {
            public SourceBox SourceBox;
            public Marble Marble;
            public TargetBox TargetBox;
            public Transform Slot;
            public LaneRuntime Lane;
            public int ReservedSlotIndex;
        }

        private sealed class HandCompletionBatch
        {
            public readonly HashSet<TargetBox> Targets = new HashSet<TargetBox>();
            public readonly HashSet<TargetBox> RevealedTargets = new HashSet<TargetBox>();
            public readonly HashSet<TargetBox> CompletedAnimations = new HashSet<TargetBox>();
            public readonly Dictionary<TargetBox, LaneRuntime> LanesByTarget = new Dictionary<TargetBox, LaneRuntime>();
            public readonly HashSet<LaneRuntime> LockedLanes = new HashSet<LaneRuntime>();
            public readonly List<PendingHandTransfer> Transfers = new List<PendingHandTransfer>();
            public bool IsStartingReveals;
            public bool TransfersStarted;
            public bool IsFinalizing;
            public Action<bool> CompletionCallback;
        }

        private void ValidateHandTransferSettings()
        {
            handTransferInitialDelay = Mathf.Max(0f, handTransferInitialDelay);
            handTransferStagger = Mathf.Max(0f, handTransferStagger);
            handTargetFlightDuration = Mathf.Max(0f, handTargetFlightDuration);
            handTransferArcHeight = Mathf.Max(0f, handTransferArcHeight);
            handTransferArcPosition = Mathf.Clamp01(handTransferArcPosition);
            handTargetEndScaleMultiplier = Mathf.Max(0.01f, handTargetEndScaleMultiplier);
            handReleasePhysicsDuration = Mathf.Max(0f, handReleasePhysicsDuration);
            handBounceDuration = Mathf.Max(0f, handBounceDuration);
            handBounceScaleMultiplier = Mathf.Max(0.01f, handBounceScaleMultiplier);
            handArrivalPunchScale = Mathf.Max(0f, handArrivalPunchScale);
            handArrivalPunchDuration = Mathf.Max(0f, handArrivalPunchDuration);
            handArrivalPunchVibrato = Mathf.Max(1, handArrivalPunchVibrato);
            handArrivalPunchElasticity = Mathf.Clamp01(handArrivalPunchElasticity);
        }

        public bool TryBeginHandDirectTransfer(
            IReadOnlyList<SourceBox> sourceBoxes,
            Action<bool> onCompleted = null)
        {
            return TryBeginHandDirectTransfer(sourceBoxes, onCompleted, out _);
        }

        public bool TryBeginHandDirectTransfer(
            IReadOnlyList<SourceBox> sourceBoxes,
            Action<bool> onCompleted,
            out bool blockedByActiveTransfer)
        {
            blockedByActiveTransfer = false;
            List<PendingHandTransfer> transferPlan = new List<PendingHandTransfer>();
            if (!TryBuildHandTransferPlan(sourceBoxes, transferPlan))
            {
                return false;
            }

            if (HasActiveTransferInHandPlanLane(transferPlan))
            {
                blockedByActiveTransfer = true;
                return false;
            }

            for (int transferIndex = 0; transferIndex < transferPlan.Count; transferIndex++)
            {
                PendingHandTransfer pending = transferPlan[transferIndex];
                if (pending?.Marble == null || pending.TargetBox == null ||
                    !pending.Marble.TryBeginTargetTransfer())
                {
                    RollbackHandTransferReservations(transferPlan, transferIndex);
                    return false;
                }

                pending.ReservedSlotIndex = pending.TargetBox.ReservedSlotCount;
                if (!pending.TargetBox.TryReserveSlotForHand(pending.Marble.ColorId, out Transform slot))
                {
                    pending.Marble.CancelTargetTransfer();
                    RollbackHandTransferReservations(transferPlan, transferIndex);
                    return false;
                }

                pending.Slot = slot;
            }

            HandCompletionBatch completionBatch = CreateHandCompletionBatch(transferPlan);
            if (completionBatch == null)
            {
                RollbackHandTransferReservations(transferPlan, transferPlan.Count);
                return false;
            }

            completionBatch.CompletionCallback = onCompleted;

            for (int sourceIndex = 0; sourceIndex < sourceBoxes.Count; sourceIndex++)
            {
                SourceBox sourceBox = sourceBoxes[sourceIndex];
                bool bypassAvailability = sourceIndex > 0;
                int transferCount = sourceBox.RuntimeMarbles.Count;
                if (!sourceBox.TryBeginHandDirectTransfer(bypassAvailability, out _))
                {
                    Debug.LogError($"{nameof(TargetLaneController)} on '{name}' could not commit a prevalidated Hand direct transfer for '{sourceBox.name}'.", this);
                    RollbackHandTransferReservations(transferPlan, transferPlan.Count);
                    ReleaseHandCompletionBatch(completionBatch, false);
                    return false;
                }

                pendingHandArrivalsBySource[sourceBox] = transferCount;
            }

            completionBatch.Transfers.AddRange(transferPlan);
            completionBatch.IsStartingReveals = true;
            foreach (TargetBox targetBox in completionBatch.Targets)
            {
                TargetBox capturedTarget = targetBox;
                capturedTarget.RevealForHandTransfer(() =>
                    HandleHandTargetRevealed(completionBatch, capturedTarget));
            }

            completionBatch.IsStartingReveals = false;
            TryStartHandTransferBatch(completionBatch);

            return true;
        }

        private bool HasActiveTransferInHandPlanLane(IReadOnlyList<PendingHandTransfer> transferPlan)
        {
            // A completed conveyor transfer can shift the whole lane, even when the Hand
            // plan chose the next consecutive TargetBox. Do not let both flows own that
            // lane's reservations/completion callbacks at the same time.
            for (int activeIndex = 0; activeIndex < activeTransfers.Count; activeIndex++)
            {
                TargetTransfer activeTransfer = activeTransfers[activeIndex];
                if (activeTransfer?.Lane == null)
                {
                    continue;
                }

                for (int pendingIndex = 0; pendingIndex < transferPlan.Count; pendingIndex++)
                {
                    if (transferPlan[pendingIndex]?.Lane == activeTransfer.Lane)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void AppendHandTransferAnimation(
            Sequence sequence,
            TargetTransfer transfer,
            Transform marbleTransform,
            Marble marble)
        {
            Vector3 releaseScale = marbleTransform.localScale;
            Vector3 physicsEndPosition = marbleTransform.position;
            Vector3 bounceEndPosition = physicsEndPosition;
            Vector3 bouncePeakScale = releaseScale;
            Vector3 targetEndScale = releaseScale;

            Sequence sourceReleaseSequence =
                transfer.DirectSourceBox?.CreateHandMarbleReleaseSequence(marble);
            float sourceReleaseDuration = sourceReleaseSequence != null
                ? sourceReleaseSequence.Duration(false)
                : 0f;
            if (sourceReleaseSequence != null)
            {
                sequence.Append(sourceReleaseSequence);
            }

            // Rigidbody2D is the sole position owner for this entire phase.
            float physicsDuration = GetEffectiveDuration(handReleasePhysicsDuration);
            float remainingPhysicsDuration = Mathf.Max(0f, physicsDuration - sourceReleaseDuration);
            if (remainingPhysicsDuration > 0f)
            {
                sequence.AppendInterval(remainingPhysicsDuration);
            }

            sequence.AppendCallback(() =>
            {
                if (marbleTransform == null || marble == null)
                {
                    return;
                }

                Vector3 currentTransformPosition = marbleTransform.position;
                Rigidbody2D body = marble.Rigidbody;
                physicsEndPosition = body != null
                    ? new Vector3(body.position.x, body.position.y, currentTransformPosition.z)
                    : currentTransformPosition;
                // Preserve the physics result, then zero velocity and disable simulation
                // before any position tween takes ownership.
                marbleTransform.position = physicsEndPosition;
                marble.PrepareForTargetTransfer();
                marble.SetShadowActive(false);
                marbleTransform.position = physicsEndPosition;
                releaseScale = marbleTransform.localScale;
                bouncePeakScale = releaseScale * handBounceScaleMultiplier;
                targetEndScale = releaseScale * handTargetEndScaleMultiplier;
                transfer.HandTargetEndScale = targetEndScale;
                transfer.HasHandTargetEndScale = true;
                bounceEndPosition = physicsEndPosition + handBounceOffset;
            });

            float bounceProgress = 0f;
            sequence.Append(DOTween
                .To(
                    () => bounceProgress,
                    value =>
                    {
                        bounceProgress = value;
                        if (marbleTransform != null)
                        {
                            marbleTransform.position = Vector3.LerpUnclamped(
                                physicsEndPosition,
                                bounceEndPosition,
                                value);
                        }
                    },
                    1f,
                    GetEffectiveDuration(handBounceDuration))
                .SetEase(handBounceEase));

            float bounceScaleProgress = 0f;
            sequence.Join(DOTween
                .To(
                    () => bounceScaleProgress,
                    value =>
                    {
                        bounceScaleProgress = value;
                        if (marbleTransform != null)
                        {
                            marbleTransform.localScale = Vector3.LerpUnclamped(
                                releaseScale,
                                bouncePeakScale,
                                value);
                        }
                    },
                    1f,
                    GetEffectiveDuration(handBounceDuration))
                .SetEase(handBounceEase));

            float flyProgress = 0f;
            sequence.Append(DOTween
                .To(
                    () => flyProgress,
                    value =>
                    {
                        flyProgress = value;
                        if (marbleTransform != null)
                        {
                            marbleTransform.position = EvaluateTransferPosition(
                                bounceEndPosition,
                                transfer.Slot,
                                value,
                                handTransferArcHeight,
                                handTransferArcPosition);
                        }
                    },
                    1f,
                    GetEffectiveDuration(handTargetFlightDuration))
                .SetEase(handTargetFlightEase));

            float flightScaleProgress = 0f;
            sequence.Join(DOTween
                .To(
                    () => flightScaleProgress,
                    value =>
                    {
                        flightScaleProgress = value;
                        if (marbleTransform != null)
                        {
                            marbleTransform.localScale = Vector3.LerpUnclamped(
                                bouncePeakScale,
                                targetEndScale,
                                value);
                        }
                    },
                    1f,
                    GetEffectiveDuration(handTargetFlightDuration))
                .SetEase(handTargetFlightEase));
        }

        private void HandleHandTargetRevealed(HandCompletionBatch batch, TargetBox targetBox)
        {
            if (batch == null || targetBox == null || batch.IsFinalizing ||
                !batch.RevealedTargets.Add(targetBox) || batch.IsStartingReveals)
            {
                return;
            }

            TryStartHandTransferBatch(batch);
        }

        private void TryStartHandTransferBatch(HandCompletionBatch batch)
        {
            if (batch == null || batch.IsFinalizing || batch.TransfersStarted ||
                batch.IsStartingReveals || batch.RevealedTargets.Count != batch.Targets.Count)
            {
                return;
            }

            batch.TransfersStarted = true;
            for (int transferIndex = 0; transferIndex < batch.Transfers.Count; transferIndex++)
            {
                PendingHandTransfer pending = batch.Transfers[transferIndex];

                TargetTransfer transfer = new TargetTransfer
                {
                    TransferId = nextTransferId++,
                    Marble = pending.Marble,
                    TargetBox = pending.TargetBox,
                    Slot = pending.Slot,
                    Lane = pending.Lane,
                    ReservedSlotIndex = pending.ReservedSlotIndex,
                    DirectSourceBox = pending.SourceBox
                };

                activeTransfers.Add(transfer);
                transferByMarble.Add(transfer.Marble, transfer);
#if UNITY_EDITOR
                DebugTrackTransfer(transfer, "Hand -> Target transfer registered");
#endif
                LogTransferOwnership($"Reserve Hand {DescribeTransfer(transfer)}. Box reserved {transfer.TargetBox.ReservedSlotCount}, arrived {transfer.TargetBox.ArrivedMarbleCount}.");
                StartTransferToSlot(
                    transfer,
                    0f,
                    handTransferInitialDelay + (transferIndex * handTransferStagger));
            }
        }

        public bool CanBeginHandDirectTransfer(IReadOnlyList<SourceBox> sourceBoxes)
        {
            return TryBuildHandTransferPlan(sourceBoxes, null);
        }

        private bool TryBuildHandTransferPlan(
            IReadOnlyList<SourceBox> sourceBoxes,
            List<PendingHandTransfer> result)
        {
            result?.Clear();
            if (sourceBoxes == null || sourceBoxes.Count == 0 || lanes.Count == 0)
            {
                return false;
            }

            List<PendingHandTransfer> unassigned = new List<PendingHandTransfer>();
            List<Marble> releaseOrderedMarbles = new List<Marble>();
            for (int sourceIndex = 0; sourceIndex < sourceBoxes.Count; sourceIndex++)
            {
                SourceBox sourceBox = sourceBoxes[sourceIndex];
                bool validSourceState = sourceIndex == 0
                    ? sourceBox != null && sourceBox.CanBeginHandDirectTransfer
                    : sourceBox != null && sourceBox.CanBeginForcedHandDirectTransfer;
                if (!validSourceState || sourceBox.RuntimeMarbles == null || sourceBox.RuntimeMarbles.Count == 0)
                {
                    return false;
                }

                sourceBox.CollectReleaseOrderedMarbles(releaseOrderedMarbles);
                for (int marbleIndex = 0; marbleIndex < releaseOrderedMarbles.Count; marbleIndex++)
                {
                    Marble marble = releaseOrderedMarbles[marbleIndex];
                    if (marble == null || marble.ColorId != sourceBox.ColorId || marble.IsTransferringToTarget)
                    {
                        return false;
                    }

                    unassigned.Add(new PendingHandTransfer
                    {
                        SourceBox = sourceBox,
                        Marble = marble
                    });
                }
            }

            int maximumDepth = 0;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                maximumDepth = Mathf.Max(maximumDepth, lanes[laneIndex]?.Boxes.Count ?? 0);
            }

            Dictionary<TargetBox, int> plannedReservations = new Dictionary<TargetBox, int>();
            Dictionary<TargetBox, int> normalConveyorClaims = new Dictionary<TargetBox, int>();
            GetImminentNormalConveyorClaims(normalConveyorClaims);
            for (int depth = 0; depth < maximumDepth && unassigned.Count > 0; depth++)
            {
                for (int laneIndex = 0; laneIndex < lanes.Count && unassigned.Count > 0; laneIndex++)
                {
                    LaneRuntime lane = lanes[laneIndex];
                    if (lane == null || lane.IsTransitioning || depth >= lane.Boxes.Count)
                    {
                        continue;
                    }

                    TargetBox targetBox = lane.Boxes[depth];
                    if (targetBox == null || targetBox.IsRuntimeLocked || targetBox.IsCompleting)
                    {
                        continue;
                    }

                    if (!targetBox.CanReserveForHand(targetBox.ColorId))
                    {
                        continue;
                    }

                    plannedReservations.TryGetValue(targetBox, out int plannedCount);
                    normalConveyorClaims.TryGetValue(targetBox, out int normalClaimCount);
                    int availableSlots = targetBox.AvailableReservationCount -
                                         plannedCount -
                                         normalClaimCount;
                    while (availableSlots > 0)
                    {
                        int matchingIndex = FindFirstMatchingHandMarble(unassigned, targetBox.ColorId);
                        if (matchingIndex < 0)
                        {
                            break;
                        }

                        PendingHandTransfer assignment = unassigned[matchingIndex];
                        unassigned.RemoveAt(matchingIndex);
                        assignment.TargetBox = targetBox;
                        assignment.Lane = lane;
                        result?.Add(assignment);
                        plannedCount++;
                        availableSlots--;
                    }

                    if (plannedCount > 0)
                    {
                        plannedReservations[targetBox] = plannedCount;
                    }
                }
            }

            if (unassigned.Count == 0)
            {
                return true;
            }

            result?.Clear();
            return false;
        }

        private static int FindFirstMatchingHandMarble(
            IReadOnlyList<PendingHandTransfer> candidates,
            MarbleColorId colorId)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                PendingHandTransfer candidate = candidates[i];
                if (candidate?.Marble != null && candidate.Marble.ColorId == colorId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RollbackHandTransferReservations(
            IReadOnlyList<PendingHandTransfer> transferPlan,
            int reservedCount)
        {
            int count = Mathf.Min(reservedCount, transferPlan?.Count ?? 0);
            for (int i = count - 1; i >= 0; i--)
            {
                PendingHandTransfer pending = transferPlan[i];
                pending?.TargetBox?.CancelLastReservation();
                pending?.Marble?.CancelTargetTransfer();
            }
        }

        private HandCompletionBatch CreateHandCompletionBatch(IReadOnlyList<PendingHandTransfer> transferPlan)
        {
            HandCompletionBatch batch = new HandCompletionBatch();
            for (int i = 0; i < transferPlan.Count; i++)
            {
                PendingHandTransfer pending = transferPlan[i];
                if (pending?.TargetBox == null || pending.Lane == null ||
                    handCompletionBatchByTarget.ContainsKey(pending.TargetBox))
                {
                    return null;
                }

                batch.Targets.Add(pending.TargetBox);
                batch.LanesByTarget[pending.TargetBox] = pending.Lane;
                batch.LockedLanes.Add(pending.Lane);
            }

            foreach (LaneRuntime lane in batch.LockedLanes)
            {
                if (lane == null || lane.IsTransitioning)
                {
                    return null;
                }
            }

            foreach (LaneRuntime lane in batch.LockedLanes)
            {
                lane.IsTransitioning = true;
            }

            foreach (TargetBox targetBox in batch.Targets)
            {
                handCompletionBatchByTarget[targetBox] = batch;
            }

            return batch;
        }

        private void ReleaseHandCompletionBatch(HandCompletionBatch batch, bool completed = false)
        {
            if (batch == null)
            {
                return;
            }

            foreach (TargetBox targetBox in batch.Targets)
            {
                handCompletionBatchByTarget.Remove(targetBox);
            }

            foreach (LaneRuntime lane in batch.LockedLanes)
            {
                if (lane != null)
                {
                    lane.IsTransitioning = false;
                    lane.TransitionTween = null;
                    lane.RevealingBox = null;
                }
            }

            Action<bool> completionCallback = batch.CompletionCallback;
            batch.CompletionCallback = null;
            completionCallback?.Invoke(completed);
        }

        private void TryBeginHandTargetCompletion(HandCompletionBatch batch, TargetBox targetBox)
        {
            if (batch == null || targetBox == null || batch.IsFinalizing ||
                batch.CompletedAnimations.Contains(targetBox) ||
                !targetBox.TryBeginCompletion())
            {
                return;
            }

            PlayFillBoxSfx();
            bool animationStarted = targetBox.PlayFillAnimation(() =>
                HandleHandTargetCompletionAnimationFinished(batch, targetBox));
            if (!animationStarted)
            {
                HandleHandTargetCompletionAnimationFinished(batch, targetBox);
            }
        }

        private void HandleHandTargetCompletionAnimationFinished(
            HandCompletionBatch batch,
            TargetBox targetBox)
        {
            if (batch == null || targetBox == null || batch.IsFinalizing ||
                !batch.CompletedAnimations.Add(targetBox))
            {
                return;
            }

            targetBox.gameObject.SetActive(false);
            TryFinalizeHandCompletionBatchIfReady(batch);
        }

        private void TryFinalizeHandCompletionBatchIfReady(HandCompletionBatch batch)
        {
            if (batch == null || batch.IsFinalizing || HasPendingHandTransfers(batch))
            {
                return;
            }

            int completedTargetCount = 0;
            foreach (TargetBox targetBox in batch.Targets)
            {
                if (targetBox != null && targetBox.IsComplete)
                {
                    completedTargetCount++;
                }
            }

            if (batch.CompletedAnimations.Count != completedTargetCount)
            {
                return;
            }

            FinalizeHandCompletionBatch(batch);
        }

        private bool HasPendingHandTransfers(HandCompletionBatch batch)
        {
            for (int i = 0; i < batch.Transfers.Count; i++)
            {
                PendingHandTransfer pending = batch.Transfers[i];
                if (pending?.Marble != null && transferByMarble.ContainsKey(pending.Marble))
                {
                    return true;
                }
            }

            return false;
        }

        private void FinalizeHandCompletionBatch(HandCompletionBatch batch)
        {
            if (batch == null || batch.IsFinalizing)
            {
                return;
            }

            batch.IsFinalizing = true;
            HashSet<LaneRuntime> shiftedLanes = new HashSet<LaneRuntime>();
            foreach (TargetBox targetBox in batch.CompletedAnimations)
            {
                if (targetBox == null || !batch.LanesByTarget.TryGetValue(targetBox, out LaneRuntime lane))
                {
                    continue;
                }

                lane.Boxes.Remove(targetBox);
                handCompletionBatchByTarget.Remove(targetBox);
                shiftedLanes.Add(lane);
            }

            int pendingLaneShifts = shiftedLanes.Count;
            if (pendingLaneShifts == 0)
            {
                CompleteHandCompletionBatch(batch);
                return;
            }

            foreach (LaneRuntime lane in shiftedLanes)
            {
                StartLaneShiftTween(lane, () =>
                {
                    pendingLaneShifts--;
                    if (pendingLaneShifts > 0)
                    {
                        return;
                    }

                    CompleteHandCompletionBatch(batch);
                });
            }
        }

        private void CompleteHandCompletionBatch(HandCompletionBatch batch)
        {
            // Hand can distribute one source across both completed and partially filled targets.
            // Restore surviving off-front targets after the temporary Hand reveal before unlocking lanes.
            foreach (TargetBox targetBox in batch.Targets)
            {
                if (targetBox != null &&
                    !batch.CompletedAnimations.Contains(targetBox) &&
                    !targetBox.IsActive)
                {
                    targetBox.SetPassiveView();
                }
            }

            ReleaseHandCompletionBatch(batch, true);
            foreach (LaneRuntime affectedLane in batch.LockedLanes)
            {
                ResumeLaneAfterHandCompletion(affectedLane);
            }

            if (AreAllLanesEmpty())
            {
                NotifyLaneCompletionSignals();
            }
        }

        private void ResumeLaneAfterHandCompletion(LaneRuntime lane)
        {
            if (lane == null || lane.Boxes.Count == 0)
            {
                return;
            }

            TargetBox frontTarget = lane.Boxes[0];
            if (frontTarget == null)
            {
                return;
            }

            if (frontTarget.IsActive)
            {
                EnsureConveyorRayBuffers();
                ScanLane(lane, GetCaptureDirection());
                return;
            }

            TargetBoxReachedFront?.Invoke(frontTarget, lane.LaneIndex);
            if (frontTarget.HasConnectedTargetGroup)
            {
                connectedTargetGroupController?.NotifyTargetReachedFront(frontTarget);
                return;
            }

            lane.RevealingBox = frontTarget;
            frontTarget.SetActiveView(() =>
            {
                lane.RevealingBox = null;
                connectedTargetGroupController?.NotifyTargetRevealed(frontTarget);
                EnsureConveyorRayBuffers();
                ScanLane(lane, GetCaptureDirection());
            });
        }

        private void ConfirmHandSourceArrival(SourceBox sourceBox)
        {
            if (sourceBox == null || !pendingHandArrivalsBySource.TryGetValue(sourceBox, out int remaining))
            {
                return;
            }

            remaining--;
            if (remaining > 0)
            {
                pendingHandArrivalsBySource[sourceBox] = remaining;
                return;
            }

            pendingHandArrivalsBySource.Remove(sourceBox);
            sourceBox.CompleteHandDirectTransfer();
        }
    }
}

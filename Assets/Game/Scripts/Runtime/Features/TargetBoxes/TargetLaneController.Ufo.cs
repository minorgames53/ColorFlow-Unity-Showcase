using System;
using System.Collections.Generic;
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
        private sealed class UfoTargetPreviewState
        {
            public bool WasActive;
        }

        private readonly Dictionary<TargetBox, UfoTargetPreviewState> ufoTargetPreviews =
            new Dictionary<TargetBox, UfoTargetPreviewState>();

        public sealed class UfoTransferReservation
        {
            private readonly TargetLaneController owner;
            private readonly TargetTransfer transfer;

            internal UfoTransferReservation(TargetLaneController owner, TargetTransfer transfer)
            {
                this.owner = owner;
                this.transfer = transfer;
            }

            public TargetBox Target => transfer?.TargetBox;
            public Transform Slot => transfer?.Slot;
            public Vector3 TargetMarbleScale => owner.targetMarbleFinalScale;
            public bool IsPending => owner != null && owner.IsCurrentTransfer(transfer);

            internal TargetTransfer Transfer => transfer;
        }

        public bool IsStableForUfoFire
        {
            get
            {
                if (shuffleInProgress || HasUfoRelevantActiveTransfer() ||
                    connectedTargetGroupController != null &&
                    connectedTargetGroupController.HasActiveCompletionTransaction)
                {
                    return false;
                }

                for (int i = 0; i < lanes.Count; i++)
                {
                    LaneRuntime lane = lanes[i];
                    if (lane != null &&
                        (lane.IsTransitioning || lane.TransitionTween != null || lane.RevealingBox != null))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool HasUfoTargetTransitionActivity
        {
            get
            {
                if (shuffleInProgress || HasUfoRelevantActiveTransfer() ||
                    connectedTargetGroupController != null &&
                    connectedTargetGroupController.HasActiveCompletionTransaction)
                {
                    return true;
                }

                for (int i = 0; i < lanes.Count; i++)
                {
                    LaneRuntime lane = lanes[i];
                    if (lane != null &&
                        (lane.IsTransitioning || lane.TransitionTween != null || lane.RevealingBox != null))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool TryGetUfoAimTarget(
            MarbleColorId colorId,
            out TargetBox target)
        {
            target = null;
            int maximumDepth = 0;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                maximumDepth = Mathf.Max(maximumDepth, lanes[laneIndex]?.Boxes.Count ?? 0);
            }

            for (int depth = 0; depth < maximumDepth; depth++)
            {
                for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
                {
                    LaneRuntime lane = lanes[laneIndex];
                    if (!IsLaneAccessibleForUfo(lane) || depth >= lane.Boxes.Count)
                    {
                        continue;
                    }

                    TargetBox candidate = lane.Boxes[depth];
                    if (candidate == null || !candidate.gameObject.activeInHierarchy ||
                        !candidate.CanReserveForUfo(colorId) ||
                        HasConflictingTransferInLane(lane, candidate))
                    {
                        continue;
                    }

                    target = candidate;
                    return true;
                }
            }

            return false;
        }

        internal void GetAvailableUfoCapacityByColor(
            Dictionary<MarbleColorId, int> capacityByColor)
        {
            if (capacityByColor == null)
            {
                return;
            }

            capacityByColor.Clear();
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                LaneRuntime lane = lanes[laneIndex];
                if (!IsLaneAccessibleForUfo(lane))
                {
                    continue;
                }

                for (int depth = 0; depth < lane.Boxes.Count; depth++)
                {
                    TargetBox candidate = lane.Boxes[depth];
                    if (candidate == null || !candidate.gameObject.activeInHierarchy ||
                        !candidate.CanReserveForUfo(candidate.ColorId) ||
                        HasConflictingTransferInLane(lane, candidate))
                    {
                        continue;
                    }

                    int availableCapacity = candidate.AvailableReservationCount;
                    if (availableCapacity <= 0)
                    {
                        continue;
                    }

                    capacityByColor.TryGetValue(candidate.ColorId, out int currentCapacity);
                    capacityByColor[candidate.ColorId] = currentCapacity + availableCapacity;
                }
            }
        }

        private static bool IsLaneAccessibleForUfo(LaneRuntime lane)
        {
            return lane != null && !lane.IsTransitioning && lane.Boxes.Count > 0 &&
                   lane.Boxes[0] != null && !lane.Boxes[0].IsRuntimeLocked;
        }

        private bool HasConflictingTransferInLane(LaneRuntime lane, TargetBox candidate)
        {
            for (int i = 0; i < activeTransfers.Count; i++)
            {
                TargetTransfer transfer = activeTransfers[i];
                if (transfer?.Lane != lane)
                {
                    continue;
                }

                if (!transfer.IsUfoTransfer || transfer.TargetBox != candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasUfoRelevantActiveTransfer()
        {
            for (int i = 0; i < activeTransfers.Count; i++)
            {
                TargetTransfer transfer = activeTransfers[i];
                if (transfer != null)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryReserveUfoTransfer(
            Marble marble,
            TargetBox expectedTarget,
            float arrivalPunchScale,
            float fireSpeedMultiplier,
            Action onArrivalCommitted,
            out UfoTransferReservation reservation)
        {
            reservation = null;
            if (marble != null && expectedTarget != null && marble.ColorId != expectedTarget.ColorId)
            {
                Debug.LogError(
                    $"{nameof(TargetLaneController)} on '{name}' rejected UFO reservation because marble '{marble.name}' " +
                    $"({marble.ColorId}) does not match TargetBox '{expectedTarget.name}' ({expectedTarget.ColorId}).",
                    this);
                return false;
            }

            if (marble == null || expectedTarget == null ||
                !expectedTarget.gameObject.activeInHierarchy ||
                transferByMarble.ContainsKey(marble) ||
                !TryGetLaneIndex(expectedTarget, out int laneIndex) ||
                !TryGetLane(laneIndex, out LaneRuntime lane) || !IsLaneAccessibleForUfo(lane) ||
                HasConflictingTransferInLane(lane, expectedTarget) ||
                !expectedTarget.CanReserveForUfo(marble.ColorId) ||
                !marble.TryBeginTargetTransferFromUfo())
            {
                return false;
            }

            int reservedSlotIndex = expectedTarget.ReservedSlotCount;
            if (!expectedTarget.TryReserveSlotForUfo(marble.ColorId, out Transform slot))
            {
                marble.RestoreUfoOwnershipAfterCancelledTargetTransfer();
                return false;
            }

            marble.PrepareForTargetTransfer();
            TargetTransfer transfer = new TargetTransfer
            {
                TransferId = nextTransferId++,
                Marble = marble,
                TargetBox = expectedTarget,
                Slot = slot,
                Lane = lane,
                ReservedSlotIndex = reservedSlotIndex,
                ArrivalCommitted = onArrivalCommitted,
                ArrivalPunchScaleOverride = Mathf.Max(0f, arrivalPunchScale),
                ArrivalDurationSpeedMultiplier = Mathf.Max(0.01f, fireSpeedMultiplier),
                IsUfoTransfer = true
            };

            activeTransfers.Add(transfer);
            transferByMarble.Add(marble, transfer);
#if UNITY_EDITOR
            DebugTrackTransfer(transfer, "UFO -> Target transfer ownership committed (reserved / aiming)");
#endif
            BeginUfoTargetPreview(expectedTarget);
            reservation = new UfoTransferReservation(this, transfer);
            LogTransferOwnership($"Reserve UFO {DescribeTransfer(transfer)}. Box reserved {expectedTarget.ReservedSlotCount}, arrived {expectedTarget.ArrivedMarbleCount}.");
            return true;
        }

        private void StartCompleteUfoOffFrontBox(LaneRuntime lane, TargetBox targetBox)
        {
            if (lane == null || targetBox == null || IsFrontTarget(lane, targetBox))
            {
                return;
            }

            StartTargetCompletionAnimation(
                lane,
                targetBox,
                deactivateCompletedBox => CompleteUfoOffFrontBox(
                    lane,
                    targetBox,
                    deactivateCompletedBox));
        }

        private void CompleteUfoOffFrontBox(
            LaneRuntime lane,
            TargetBox completedBox,
            bool deactivateCompletedBox)
        {
            if (lane == null || completedBox == null ||
                IsFrontTarget(lane, completedBox) || !lane.Boxes.Contains(completedBox))
            {
                if (lane != null)
                {
                    lane.IsTransitioning = false;
                    lane.TransitionTween = null;
                    lane.RevealingBox = null;
                }

                return;
            }

            if (deactivateCompletedBox)
            {
                completedBox.gameObject.SetActive(false);
            }

            lane.Boxes.Remove(completedBox);
            StartLaneShiftTween(lane, () =>
            {
                lane.IsTransitioning = false;
                lane.TransitionTween = null;
                lane.RevealingBox = null;

                if (lane.Boxes.Count > 0 && lane.Boxes[0] != null && lane.Boxes[0].IsActive)
                {
                    EnsureConveyorRayBuffers();
                    ScanLane(lane, GetCaptureDirection());
                }
            });
        }

        public bool CommitUfoTransferArrival(UfoTransferReservation reservation)
        {
            TargetTransfer transfer = reservation?.Transfer;
            if (transfer == null || !IsCurrentTransfer(transfer) || transfer.Marble == null ||
                transfer.TargetBox == null || transfer.Slot == null)
            {
                return false;
            }

            if (transfer.Marble.ColorId != transfer.TargetBox.ColorId)
            {
                Debug.LogError(
                    $"{nameof(TargetLaneController)} on '{name}' rejected UFO arrival because marble '{transfer.Marble.name}' " +
                    $"({transfer.Marble.ColorId}) does not match TargetBox '{transfer.TargetBox.name}' ({transfer.TargetBox.ColorId}).",
                    this);
                return false;
            }

            CompleteSlotTransfer(transfer);
            return true;
        }

        public bool CancelUfoTransferReservation(UfoTransferReservation reservation)
        {
            TargetTransfer transfer = reservation?.Transfer;
            if (transfer == null || !IsCurrentTransfer(transfer))
            {
                return false;
            }

            transfer.Tween?.Kill(false);
            transfer.Tween = null;
            activeTransfers.Remove(transfer);
            transferByMarble.Remove(transfer.Marble);
            transfer.TargetBox?.CancelLastReservation();
            transfer.ArrivalCommitted = null;
            transfer.Marble?.RestoreUfoOwnershipAfterCancelledTargetTransfer();
#if UNITY_EDITOR
            MarbleDebugTracker.RestoreUfo(transfer.Marble);
#endif
            return true;
        }

        private void BeginUfoTargetPreview(TargetBox targetBox)
        {
            if (targetBox == null)
            {
                return;
            }

            if (ufoTargetPreviews.ContainsKey(targetBox))
            {
                return;
            }

            UfoTargetPreviewState preview = new UfoTargetPreviewState
            {
                WasActive = targetBox.IsActive
            };
            ufoTargetPreviews.Add(targetBox, preview);
            if (!preview.WasActive)
            {
                targetBox.RevealForHandTransfer(null);
            }
        }

        public void BeginUfoTargetPreviewTransaction()
        {
            ClearUfoTargetPreviews();
        }

        public void EndUfoTargetPreviewTransaction()
        {
            ClearUfoTargetPreviews();
        }

        private void ClearUfoTargetPreviews()
        {
            foreach (KeyValuePair<TargetBox, UfoTargetPreviewState> pair in ufoTargetPreviews)
            {
                TargetBox targetBox = pair.Key;
                UfoTargetPreviewState preview = pair.Value;
                if (targetBox != null && preview != null && !preview.WasActive &&
                    !targetBox.IsComplete && targetBox.gameObject.activeInHierarchy)
                {
                    targetBox.SetShuffleClosedPresentation();
                }
            }

            ufoTargetPreviews.Clear();
        }
    }
}

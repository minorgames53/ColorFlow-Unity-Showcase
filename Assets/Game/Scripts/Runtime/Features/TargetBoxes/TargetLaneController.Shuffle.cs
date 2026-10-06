using System;
using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay.TargetBoxes
{
    public sealed partial class TargetLaneController
    {
        [Header("Shuffle Booster")]
        [SerializeField, Min(0f)] private float shuffleGatherDuration = 0.3f;
        [SerializeField, Min(0f)] private float shuffleGatherStagger = 0.1f;
        [SerializeField] private Ease shuffleGatherEase = Ease.InOutQuad;
        [SerializeField, Min(0f)] private float shuffleStackYOffset = 0.5f;
        [SerializeField, Min(1f)] private float shuffleFeedbackScale = 1.05f;
        [SerializeField, Min(0f)] private float shuffleFeedbackUpDuration = 0.07f;
        [SerializeField, Min(0f)] private float shuffleFeedbackDownDuration = 0.08f;
        [SerializeField, Min(0f)] private float shuffleRedistributeDuration = 0.3f;
        [SerializeField] private Ease shuffleRedistributeEase = Ease.OutQuad;

        private bool shuffleInProgress;
        private int shuffleLifecycleVersion;
        private Sequence shuffleSequence;
        private Transform shufflingTargetBoxRoot;
        private ShufflePlan activeShufflePlan;
        private Action<bool> shuffleCompletionCallback;

        public bool IsShuffleInProgress => shuffleInProgress;

        private sealed class ShuffleLanePlan
        {
            public LaneRuntime Lane;
            public readonly List<TargetBox> OriginalOrder = new List<TargetBox>();
            public readonly List<TargetBox> NewOrder = new List<TargetBox>();
        }

        private sealed class ShuffleParticipant
        {
            public TargetBox Target;
            public LaneRuntime Lane;
            public int DestinationLaneIndex;
            public int DestinationDepth;
            public Quaternion OriginalLocalRotation;
            public Vector3 OriginalLocalScale;
            public SortingGroup SortingGroup;
            public bool CreatedTemporarySortingGroup;
            public bool OriginalSortingGroupEnabled;
            public int OriginalSortingLayerId;
            public int OriginalSortingOrder;
            public bool SortingRestored;
            public Sequence GatherTween;
        }

        private sealed class ShufflePlan
        {
            public readonly List<ShuffleLanePlan> Lanes = new List<ShuffleLanePlan>();
            public readonly List<ShuffleParticipant> Participants = new List<ShuffleParticipant>();
            public readonly HashSet<TargetBox> ParticipantTargets = new HashSet<TargetBox>();
            public bool QueueCommitted;
            public bool RedistributeCompleted;
            public bool Finalized;
            public int PendingFrontRevealCount;
        }

        private void ValidateShuffleSettings()
        {
            shuffleGatherDuration = Mathf.Max(0f, shuffleGatherDuration);
            shuffleGatherStagger = Mathf.Max(0f, shuffleGatherStagger);
            shuffleStackYOffset = Mathf.Max(0f, shuffleStackYOffset);
            shuffleFeedbackScale = Mathf.Max(1f, shuffleFeedbackScale);
            shuffleFeedbackUpDuration = Mathf.Max(0f, shuffleFeedbackUpDuration);
            shuffleFeedbackDownDuration = Mathf.Max(0f, shuffleFeedbackDownDuration);
            shuffleRedistributeDuration = Mathf.Max(0f, shuffleRedistributeDuration);
        }

        public bool TryBeginShuffle(
            ISet<MarbleColorId> playableSourceColors,
            Action<bool> onCompleted)
        {
            if (!TryBuildShufflePlan(playableSourceColors, out ShufflePlan plan))
            {
                onCompleted?.Invoke(false);
                return false;
            }

            shuffleInProgress = true;
            int expectedLifecycleVersion = ++shuffleLifecycleVersion;
            activeShufflePlan = plan;
            shuffleCompletionCallback = onCompleted;

            connectedTargetGroupController?.ResetRuntime();
            CreateShufflingTargetBoxRoot();
            if (shufflingTargetBoxRoot == null)
            {
                ResetShuffleRuntime(true, false);
                return false;
            }

            PlayShuffleSequence(plan, expectedLifecycleVersion);
            return true;
        }

        public bool CanBeginShuffle(ISet<MarbleColorId> playableSourceColors)
        {
            return TryBuildShufflePlan(playableSourceColors, out _);
        }

        public void CancelShuffle()
        {
            if (!shuffleInProgress && activeShufflePlan == null)
            {
                return;
            }

            ResetShuffleRuntime(true, true);
        }

        private bool TryBuildShufflePlan(
            ISet<MarbleColorId> playableSourceColors,
            out ShufflePlan plan)
        {
            plan = null;
            if (shuffleInProgress || playableSourceColors == null || playableSourceColors.Count == 0 ||
                lanes.Count != LevelDefinition.TargetBoxLaneCount || activeTransfers.Count > 0 ||
                pendingHandArrivalsBySource.Count > 0 || handCompletionBatchByTarget.Count > 0 ||
                (connectedTargetGroupController != null && connectedTargetGroupController.HasActiveCompletionTransaction))
            {
                return false;
            }

            ShufflePlan candidatePlan = new ShufflePlan();
            bool hasOrderChange = false;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                LaneRuntime lane = lanes[laneIndex];
                if (IsLaneExcludedFromShuffle(lane))
                {
                    continue;
                }

                if (!IsLaneStableForShuffle(lane) || lane.Boxes.Count < 2)
                {
                    return false;
                }

                TargetBox front = lane.Boxes[0];
                int selectedIndex = FindShuffleTargetIndex(lane, playableSourceColors);
                TargetBox selected = lane.Boxes[selectedIndex >= 1 ? selectedIndex : 1];

                ShuffleLanePlan lanePlan = new ShuffleLanePlan
                {
                    Lane = lane
                };
                lanePlan.OriginalOrder.AddRange(lane.Boxes);
                lanePlan.NewOrder.AddRange(lane.Boxes);

                if (selectedIndex >= 1)
                {
                    lanePlan.NewOrder.RemoveAt(selectedIndex);
                    lanePlan.NewOrder.Insert(0, selected);
                    hasOrderChange = true;
                }

                candidatePlan.Lanes.Add(lanePlan);
                AddShuffleParticipant(candidatePlan, lane, front);
                AddShuffleParticipant(candidatePlan, lane, selected);
            }

            if (!hasOrderChange ||
                candidatePlan.Participants.Count != candidatePlan.Lanes.Count * 2)
            {
                return false;
            }

            ResolveShuffleDestinations(candidatePlan);

            plan = candidatePlan;
            return true;
        }

        private static bool IsLaneExcludedFromShuffle(LaneRuntime lane)
        {
            if (lane == null || lane.Boxes.Count == 0)
            {
                return false;
            }

            TargetBox front = lane.Boxes[0];
            return front != null &&
                   (front.IsRuntimeLocked || front.HasConnectedTargetGroup);
        }

        private bool IsLaneStableForShuffle(LaneRuntime lane)
        {
            if (lane == null || lane.IsTransitioning || lane.TransitionTween != null || lane.RevealingBox != null)
            {
                return false;
            }

            for (int i = 0; i < lane.Boxes.Count; i++)
            {
                TargetBox targetBox = lane.Boxes[i];
                if (targetBox == null || targetBox.IsCompleting ||
                    targetBox.ReservedSlotCount != targetBox.ArrivedMarbleCount)
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindShuffleTargetIndex(
            LaneRuntime lane,
            ISet<MarbleColorId> playableSourceColors)
        {
            TargetBox front = lane.Boxes[0];
            if (front == null || front.IsRuntimeLocked || front.HasConnectedTargetGroup)
            {
                return -1;
            }

            for (int boxIndex = 1; boxIndex < lane.Boxes.Count; boxIndex++)
            {
                TargetBox candidate = lane.Boxes[boxIndex];
                if (candidate == null || candidate.IsRuntimeLocked ||
                    candidate.IsMystery && !candidate.IsActive ||
                    !playableSourceColors.Contains(candidate.ColorId))
                {
                    continue;
                }

                return boxIndex;
            }

            return -1;
        }

        private static void AddShuffleParticipant(ShufflePlan plan, LaneRuntime lane, TargetBox target)
        {
            if (target == null || !plan.ParticipantTargets.Add(target))
            {
                return;
            }

            plan.Participants.Add(new ShuffleParticipant
            {
                Target = target,
                Lane = lane,
                OriginalLocalRotation = target.transform.localRotation,
                OriginalLocalScale = target.transform.localScale
            });
        }

        private static void ResolveShuffleDestinations(ShufflePlan plan)
        {
            for (int participantIndex = 0; participantIndex < plan.Participants.Count; participantIndex++)
            {
                ShuffleParticipant participant = plan.Participants[participantIndex];
                participant.DestinationLaneIndex = participant.Lane.LaneIndex;
                participant.DestinationDepth = -1;

                for (int laneIndex = 0; laneIndex < plan.Lanes.Count; laneIndex++)
                {
                    ShuffleLanePlan lanePlan = plan.Lanes[laneIndex];
                    int destinationDepth = lanePlan.NewOrder.IndexOf(participant.Target);
                    if (destinationDepth < 0)
                    {
                        continue;
                    }

                    participant.DestinationLaneIndex = lanePlan.Lane.LaneIndex;
                    participant.DestinationDepth = destinationDepth;
                    break;
                }
            }
        }

        private void PlayShuffleSequence(ShufflePlan plan, int expectedLifecycleVersion)
        {
            List<ShuffleParticipant> gatherOrder = new List<ShuffleParticipant>(plan.Participants);
            gatherOrder.Sort((first, second) =>
            {
                float firstDistance = first.Target != null ? first.Target.transform.position.sqrMagnitude : float.MaxValue;
                float secondDistance = second.Target != null ? second.Target.transform.position.sqrMagnitude : float.MaxValue;
                int comparison = firstDistance.CompareTo(secondDistance);
                return comparison != 0
                    ? comparison
                    : first.Lane.LaneIndex.CompareTo(second.Lane.LaneIndex);
            });

            List<ShuffleParticipant> stackOrder = new List<ShuffleParticipant>(plan.Participants);
            stackOrder.Sort((first, second) =>
            {
                int depthComparison = second.DestinationDepth.CompareTo(first.DestinationDepth);
                return depthComparison != 0
                    ? depthComparison
                    : first.DestinationLaneIndex.CompareTo(second.DestinationLaneIndex);
            });

            for (int stackIndex = 0; stackIndex < stackOrder.Count; stackIndex++)
            {
                ShuffleParticipant participant = stackOrder[stackIndex];
                participant.Target.SetShuffleClosedPresentation();
            }

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            float gatherEndTime = 0f;
            for (int i = 0; i < gatherOrder.Count; i++)
            {
                ShuffleParticipant participant = gatherOrder[i];
                float startTime = i * shuffleGatherStagger;
                int stackIndex = stackOrder.IndexOf(participant);
                Vector3 stackPosition = new Vector3(
                    0f,
                    stackIndex * shuffleStackYOffset,
                    0f);
                sequence.InsertCallback(
                    GetEffectiveDuration(startTime),
                    () => BeginShuffleGather(
                        plan,
                        participant,
                        stackOrder,
                        stackIndex,
                        stackPosition,
                        expectedLifecycleVersion));
                gatherEndTime = Mathf.Max(gatherEndTime, startTime + shuffleGatherDuration);
            }

            Vector3 rootScale = shufflingTargetBoxRoot.localScale;
            float feedbackStartTime = gatherEndTime;
            sequence.Insert(
                GetEffectiveDuration(feedbackStartTime),
                shufflingTargetBoxRoot
                    .DOScale(rootScale * shuffleFeedbackScale, GetEffectiveDuration(shuffleFeedbackUpDuration))
                    .SetEase(Ease.OutQuad));
            sequence.Insert(
                GetEffectiveDuration(feedbackStartTime + shuffleFeedbackUpDuration),
                shufflingTargetBoxRoot
                    .DOScale(rootScale, GetEffectiveDuration(shuffleFeedbackDownDuration))
                    .SetEase(Ease.InQuad));

            float redistributeStartTime = feedbackStartTime + shuffleFeedbackUpDuration + shuffleFeedbackDownDuration;
            sequence.InsertCallback(
                GetEffectiveDuration(redistributeStartTime),
                () => CommitShuffleQueue(plan, expectedLifecycleVersion));

            for (int i = 0; i < stackOrder.Count; i++)
            {
                ShuffleParticipant participant = stackOrder[i];
                float startTime = redistributeStartTime + i * shuffleGatherStagger;
                Vector3 destination = GetShuffleDestinationWorldPosition(plan, participant.Target);
                float movementDuration = GetEffectiveDuration(shuffleRedistributeDuration);
                sequence.Insert(
                    GetEffectiveDuration(startTime),
                    participant.Target.transform
                        .DOMoveX(destination.x, movementDuration)
                        .SetEase(shuffleRedistributeEase));
                sequence.Insert(
                    GetEffectiveDuration(startTime),
                    participant.Target.transform
                        .DOMoveY(destination.y, movementDuration)
                        .SetEase(shuffleRedistributeEase));
                sequence.Insert(
                    GetEffectiveDuration(startTime),
                    participant.Target.transform
                        .DOScale(participant.OriginalLocalScale, movementDuration)
                        .SetEase(shuffleRedistributeEase));
                sequence.InsertCallback(
                    GetEffectiveDuration(startTime + shuffleRedistributeDuration * 0.5f),
                    () =>
                    {
                        if (!IsCurrentShuffle(plan, expectedLifecycleVersion) || participant.Target == null)
                        {
                            return;
                        }

                        Vector3 position = participant.Target.transform.position;
                        position.z = destination.z;
                        participant.Target.transform.position = position;
                    });
                sequence.InsertCallback(
                    GetEffectiveDuration(startTime + shuffleRedistributeDuration),
                    () => HandleShuffleParticipantArrived(plan, participant, expectedLifecycleVersion));
            }

            sequence.OnComplete(() => HandleShuffleRedistributeCompleted(plan, expectedLifecycleVersion));
            shuffleSequence = sequence;
            TrackTween(sequence);
        }

        private void BeginShuffleGather(
            ShufflePlan plan,
            ShuffleParticipant participant,
            List<ShuffleParticipant> stackOrder,
            int stackIndex,
            Vector3 stackPosition,
            int expectedLifecycleVersion)
        {
            if (!IsCurrentShuffle(plan, expectedLifecycleVersion) ||
                participant?.Target == null ||
                shufflingTargetBoxRoot == null)
            {
                return;
            }

            Transform targetTransform = participant.Target.transform;
            Vector3 worldPosition = targetTransform.position;
            Quaternion worldRotation = targetTransform.rotation;

            targetTransform.SetParent(shufflingTargetBoxRoot, true);
            targetTransform.position = worldPosition;
            targetTransform.rotation = worldRotation;
            targetTransform.SetSiblingIndex(Mathf.Clamp(stackIndex, 0, shufflingTargetBoxRoot.childCount - 1));
            ReorderGatheredShuffleTargets(stackOrder);
            ApplyShuffleSorting(participant, stackIndex);

            Sequence gatherTween = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            gatherTween.Join(
                targetTransform
                    .DOLocalMove(stackPosition, GetEffectiveDuration(shuffleGatherDuration))
                    .SetEase(shuffleGatherEase));
            gatherTween.Join(
                targetTransform
                    .DOScale(participant.OriginalLocalScale * 1.3f, GetEffectiveDuration(shuffleGatherDuration))
                    .SetEase(shuffleGatherEase));
            gatherTween.OnComplete(() =>
            {
                if (participant.GatherTween == gatherTween)
                {
                    participant.GatherTween = null;
                }
            });

            participant.GatherTween = gatherTween;
            TrackTween(gatherTween);
        }

        private void ReorderGatheredShuffleTargets(List<ShuffleParticipant> stackOrder)
        {
            if (shufflingTargetBoxRoot == null || stackOrder == null)
            {
                return;
            }

            int siblingIndex = 0;
            for (int i = 0; i < stackOrder.Count; i++)
            {
                ShuffleParticipant participant = stackOrder[i];
                if (participant?.Target == null ||
                    participant.Target.transform.parent != shufflingTargetBoxRoot)
                {
                    continue;
                }

                participant.Target.transform.SetSiblingIndex(siblingIndex);
                siblingIndex++;
            }
        }

        private void CommitShuffleQueue(ShufflePlan plan, int expectedLifecycleVersion)
        {
            if (!IsCurrentShuffle(plan, expectedLifecycleVersion) || plan.QueueCommitted)
            {
                return;
            }

            plan.QueueCommitted = true;
            for (int laneIndex = 0; laneIndex < plan.Lanes.Count; laneIndex++)
            {
                ShuffleLanePlan lanePlan = plan.Lanes[laneIndex];
                lanePlan.Lane.Boxes.Clear();
                lanePlan.Lane.Boxes.AddRange(lanePlan.NewOrder);
                SnapNonParticipantsToQueue(plan, lanePlan);
            }
        }

        private void SnapNonParticipantsToQueue(ShufflePlan plan, ShuffleLanePlan lanePlan)
        {
            for (int boxIndex = 0; boxIndex < lanePlan.Lane.Boxes.Count; boxIndex++)
            {
                TargetBox targetBox = lanePlan.Lane.Boxes[boxIndex];
                if (targetBox == null || plan.ParticipantTargets.Contains(targetBox))
                {
                    continue;
                }

                targetBox.transform.localPosition = GetLaneBoxLocalPosition(boxIndex);
            }
        }

        private Vector3 GetShuffleDestinationWorldPosition(ShufflePlan plan, TargetBox target)
        {
            for (int laneIndex = 0; laneIndex < plan.Lanes.Count; laneIndex++)
            {
                ShuffleLanePlan lanePlan = plan.Lanes[laneIndex];
                int boxIndex = lanePlan.NewOrder.IndexOf(target);
                if (boxIndex >= 0)
                {
                    return lanePlan.Lane.LaneSlot.TransformPoint(GetLaneBoxLocalPosition(boxIndex));
                }
            }

            return target != null ? target.transform.position : Vector3.zero;
        }

        private void HandleShuffleParticipantArrived(
            ShufflePlan plan,
            ShuffleParticipant participant,
            int expectedLifecycleVersion)
        {
            if (!IsCurrentShuffle(plan, expectedLifecycleVersion) || participant?.Target == null)
            {
                return;
            }

            CommitShuffleQueue(plan, expectedLifecycleVersion);
            RestoreParticipantToLaneParent(plan, participant);
            if (participant.DestinationDepth != 0 || !CanPlayShuffleFrontReveal(participant.Target))
            {
                return;
            }

            plan.PendingFrontRevealCount++;
            participant.Target.PlayShuffleOpenPresentation(() =>
            {
                if (!IsCurrentShuffle(plan, expectedLifecycleVersion))
                {
                    return;
                }

                plan.PendingFrontRevealCount = Mathf.Max(0, plan.PendingFrontRevealCount - 1);
                TryFinalizeShuffle(plan, expectedLifecycleVersion);
            });
        }

        private void HandleShuffleRedistributeCompleted(ShufflePlan plan, int expectedLifecycleVersion)
        {
            if (!IsCurrentShuffle(plan, expectedLifecycleVersion))
            {
                return;
            }

            CommitShuffleQueue(plan, expectedLifecycleVersion);
            RestoreTargetsToLaneParents(plan);
            plan.RedistributeCompleted = true;
            TryFinalizeShuffle(plan, expectedLifecycleVersion);
        }

        private void TryFinalizeShuffle(ShufflePlan plan, int expectedLifecycleVersion)
        {
            if (!IsCurrentShuffle(plan, expectedLifecycleVersion) || plan.Finalized ||
                !plan.RedistributeCompleted || plan.PendingFrontRevealCount > 0)
            {
                return;
            }

            plan.Finalized = true;
            FinalizeShuffle(plan, expectedLifecycleVersion);
        }

        private bool CanPlayShuffleFrontReveal(TargetBox target)
        {
            if (target == null || target.IsRuntimeLocked)
            {
                return false;
            }

            if (!target.HasConnectedTargetGroup)
            {
                return true;
            }

            int groupId = target.ConnectedTargetGroupId;
            for (int targetIndex = 0; targetIndex < spawnedTargetBoxes.Count; targetIndex++)
            {
                TargetBox member = spawnedTargetBoxes[targetIndex];
                if (member == null || member.ConnectedTargetGroupId != groupId)
                {
                    continue;
                }

                if (member.IsRuntimeLocked || !TryGetLaneIndex(member, out int laneIndex) ||
                    !TryGetLane(laneIndex, out LaneRuntime lane) || lane.Boxes.Count == 0 || lane.Boxes[0] != member)
                {
                    return false;
                }
            }

            return true;
        }

        private void FinalizeShuffle(ShufflePlan plan, int expectedLifecycleVersion)
        {
            if (!IsCurrentShuffle(plan, expectedLifecycleVersion))
            {
                return;
            }

            ApplyPostShuffleLaneState();

            Action<bool> callback = shuffleCompletionCallback;
            shuffleCompletionCallback = null;
            shuffleSequence = null;
            activeShufflePlan = null;
            shuffleInProgress = false;
            DestroyShufflingTargetBoxRoot();
            callback?.Invoke(true);
        }

        private void RestoreTargetsToLaneParents(ShufflePlan plan)
        {
            for (int laneIndex = 0; laneIndex < plan.Lanes.Count; laneIndex++)
            {
                ShuffleLanePlan lanePlan = plan.Lanes[laneIndex];
                for (int boxIndex = 0; boxIndex < lanePlan.Lane.Boxes.Count; boxIndex++)
                {
                    TargetBox targetBox = lanePlan.Lane.Boxes[boxIndex];
                    if (targetBox == null)
                    {
                        continue;
                    }

                    targetBox.transform.SetParent(lanePlan.Lane.LaneSlot, false);
                    targetBox.transform.localPosition = GetLaneBoxLocalPosition(boxIndex);
                    ShuffleParticipant participant = plan.Participants.Find(item => item.Target == targetBox);
                    if (participant != null)
                    {
                        targetBox.transform.localRotation = participant.OriginalLocalRotation;
                        targetBox.transform.localScale = participant.OriginalLocalScale;
                        RestoreShuffleSorting(participant);
                    }
                }
            }
        }

        private void RestoreParticipantToLaneParent(ShufflePlan plan, ShuffleParticipant participant)
        {
            if (participant?.Target == null)
            {
                return;
            }

            for (int laneIndex = 0; laneIndex < plan.Lanes.Count; laneIndex++)
            {
                ShuffleLanePlan lanePlan = plan.Lanes[laneIndex];
                int boxIndex = lanePlan.Lane.Boxes.IndexOf(participant.Target);
                if (boxIndex < 0)
                {
                    continue;
                }

                participant.Target.transform.SetParent(lanePlan.Lane.LaneSlot, false);
                participant.Target.transform.localPosition = GetLaneBoxLocalPosition(boxIndex);
                participant.Target.transform.localRotation = participant.OriginalLocalRotation;
                participant.Target.transform.localScale = participant.OriginalLocalScale;
                RestoreShuffleSorting(participant);
                return;
            }
        }

        private static void ApplyShuffleSorting(ShuffleParticipant participant, int stackIndex)
        {
            if (participant?.Target == null)
            {
                return;
            }

            SortingGroup sortingGroup = participant.Target.GetComponent<SortingGroup>();
            if (sortingGroup == null)
            {
                sortingGroup = participant.Target.gameObject.AddComponent<SortingGroup>();
                participant.CreatedTemporarySortingGroup = true;
            }

            participant.SortingGroup = sortingGroup;
            participant.OriginalSortingGroupEnabled = sortingGroup.enabled;
            participant.OriginalSortingLayerId = sortingGroup.sortingLayerID;
            participant.OriginalSortingOrder = sortingGroup.sortingOrder;
            participant.SortingRestored = false;

            sortingGroup.enabled = true;
            sortingGroup.sortingLayerID = 0;
            sortingGroup.sortingOrder = stackIndex;
        }

        private static void RestoreShuffleSorting(ShuffleParticipant participant)
        {
            if (participant == null || participant.SortingRestored)
            {
                return;
            }

            participant.SortingRestored = true;
            SortingGroup sortingGroup = participant.SortingGroup;
            participant.SortingGroup = null;
            if (sortingGroup == null)
            {
                return;
            }

            if (!participant.CreatedTemporarySortingGroup)
            {
                sortingGroup.sortingLayerID = participant.OriginalSortingLayerId;
                sortingGroup.sortingOrder = participant.OriginalSortingOrder;
                sortingGroup.enabled = participant.OriginalSortingGroupEnabled;
                return;
            }

            sortingGroup.enabled = false;
            if (Application.isPlaying)
            {
                Destroy(sortingGroup);
            }
            else
            {
                DestroyImmediate(sortingGroup);
            }
        }

        private void ApplyPostShuffleLaneState()
        {
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                LaneRuntime lane = lanes[laneIndex];
                for (int boxIndex = 1; boxIndex < lane.Boxes.Count; boxIndex++)
                {
                    lane.Boxes[boxIndex]?.SetPassiveView();
                }

                if (lane.Boxes.Count == 0 || lane.Boxes[0] == null)
                {
                    continue;
                }

                TargetBox frontTarget = lane.Boxes[0];
                if (!frontTarget.HasConnectedTargetGroup)
                {
                    frontTarget.SetActiveViewImmediate();
                }
                else if (!CanPlayShuffleFrontReveal(frontTarget))
                {
                    frontTarget.SetPassiveView();
                }
            }

            if (connectedTargetGroupController != null &&
                !connectedTargetGroupController.Build(this, colorCatalog))
            {
                Debug.LogError($"{nameof(TargetLaneController)} on '{name}' could not rebuild Connected Target groups after Shuffle.", this);
            }

            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                LaneRuntime lane = lanes[laneIndex];
                if (lane.Boxes.Count > 0 && lane.Boxes[0] != null)
                {
                    TargetBoxReachedFront?.Invoke(lane.Boxes[0], lane.LaneIndex);
                }
            }
        }

        private bool IsCurrentShuffle(ShufflePlan plan, int expectedLifecycleVersion)
        {
            return shuffleInProgress && activeShufflePlan == plan &&
                   shuffleLifecycleVersion == expectedLifecycleVersion;
        }

        private void CreateShufflingTargetBoxRoot()
        {
            DestroyShufflingTargetBoxRoot();
            GameObject rootObject = new GameObject("ShufflingTargetBox");
            SortingGroup sortingGroup = rootObject.AddComponent<SortingGroup>();
            sortingGroup.sortingLayerName = "Booster";
            shufflingTargetBoxRoot = rootObject.transform;
            Vector3 rootPosition = shufflingTargetBoxRoot.position;
            rootPosition.x = 0f;
            rootPosition.y = -2.75f;
            shufflingTargetBoxRoot.position = rootPosition;
            shufflingTargetBoxRoot.rotation = Quaternion.identity;
            shufflingTargetBoxRoot.localScale = new Vector3(1.365f, 1.365f, 1.365f);
        }

        private void DestroyShufflingTargetBoxRoot()
        {
            if (shufflingTargetBoxRoot == null)
            {
                return;
            }

            GameObject rootObject = shufflingTargetBoxRoot.gameObject;
            shufflingTargetBoxRoot = null;
            DestroyGameObject(rootObject);
        }

        private void ResetShuffleRuntime(bool restoreOriginalOrder)
        {
            ResetShuffleRuntime(restoreOriginalOrder, false);
        }

        private void ResetShuffleRuntime(bool restoreOriginalOrder, bool refreshLaneState)
        {
            shuffleLifecycleVersion++;
            shuffleSequence?.Kill(false);
            shuffleSequence = null;
            Action<bool> callback = shuffleCompletionCallback;

            ShufflePlan plan = activeShufflePlan;
            if (plan != null)
            {
                for (int participantIndex = 0; participantIndex < plan.Participants.Count; participantIndex++)
                {
                    ShuffleParticipant participant = plan.Participants[participantIndex];
                    participant.GatherTween?.Kill(false);
                    participant.GatherTween = null;
                }

                if (restoreOriginalOrder)
                {
                    for (int laneIndex = 0; laneIndex < plan.Lanes.Count; laneIndex++)
                    {
                        ShuffleLanePlan lanePlan = plan.Lanes[laneIndex];
                        lanePlan.Lane.Boxes.Clear();
                        lanePlan.Lane.Boxes.AddRange(lanePlan.OriginalOrder);
                    }
                }

                RestoreTargetsToLaneParents(plan);
            }

            activeShufflePlan = null;
            shuffleCompletionCallback = null;
            shuffleInProgress = false;
            DestroyShufflingTargetBoxRoot();

            if (refreshLaneState && lanes.Count > 0)
            {
                ApplyPostShuffleLaneState();
            }

            callback?.Invoke(false);
        }
    }
}

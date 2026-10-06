using System.Collections.Generic;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.TargetBoxes
{
    public enum ConnectedTargetGroupState
    {
        Waiting = 0,
        Completing = 1,
        Completed = 2
    }

    [DisallowMultipleComponent]
    public sealed class ConnectedTargetGroupController : MonoBehaviour
    {
        [SerializeField] private ConnectedTargetPresentationSystem presentationSystem;

        private readonly Dictionary<int, RuntimeGroup> groupsById = new Dictionary<int, RuntimeGroup>();
        private readonly Dictionary<TargetBox, RuntimeGroup> groupsByTarget = new Dictionary<TargetBox, RuntimeGroup>();
        private TargetLaneController laneController;
        private int lifecycleVersion;
        private int nextTransactionId;

        public bool HasActiveCompletionTransaction
        {
            get
            {
                foreach (KeyValuePair<int, RuntimeGroup> pair in groupsById)
                {
                    RuntimeGroup group = pair.Value;
                    if (group != null &&
                        (group.State == ConnectedTargetGroupState.Completing ||
                         group.IsStartingFrontActivations ||
                         (group.MembersActivated && !group.FrontActivationBarrierCompleted)))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class RuntimeMember
        {
            public int LaneIndex;
            public TargetBox Target;
        }

        private sealed class RuntimeGroup
        {
            public int GroupId;
            public ConnectedTargetGroupState State = ConnectedTargetGroupState.Waiting;
            public readonly List<RuntimeMember> Members = new List<RuntimeMember>();
            public readonly HashSet<TargetBox> CompletedAnimations = new HashSet<TargetBox>();
            public readonly HashSet<int> ShiftReadyLanes = new HashSet<int>();
            public readonly HashSet<int> ActivatedLanes = new HashSet<int>();
            public readonly HashSet<int> FrontActivationCompletedLanes = new HashSet<int>();
            public int TransactionId;
            public bool MembersActivated;
            public bool FrontActivationBarrierCompleted;
            public bool IsStartingFrontActivations;
            public bool LineCollapseCompleted;
            public bool IsStartingAnimations;
            public bool IsStartingShifts;
            public bool IsStartingActivations;
            public bool CompletionFeedbackPlayed;
        }

        private void OnDisable()
        {
            ResetRuntime();
        }

        public bool ValidateConfiguration(LevelDefinition levelDefinition)
        {
            if (!ContainsConnectedTargets(levelDefinition))
            {
                return true;
            }

            if (presentationSystem == null)
            {
                Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' requires a Connected Target Presentation System for levels containing Connected Targets.", this);
                return false;
            }

            return presentationSystem.ValidateConfiguration();
        }

        public void SetGameplaySpeedMultiplier(float multiplier)
        {
            presentationSystem?.SetGameplaySpeedMultiplier(multiplier);
        }

        internal bool Build(TargetLaneController owner, MarbleColorCatalog colorCatalog)
        {
            ResetRuntime();
            laneController = owner;
            if (laneController == null)
            {
                Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' cannot build without a TargetLaneController.", this);
                return false;
            }

            IReadOnlyList<TargetBox> targets = laneController.SpawnedTargetBoxes;
            for (int i = 0; i < targets.Count; i++)
            {
                TargetBox target = targets[i];
                if (target == null || !target.HasConnectedTargetGroup)
                {
                    continue;
                }

                if (!laneController.TryGetLaneIndex(target, out int laneIndex))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' could not resolve the lane for '{target.name}'.", this);
                    ResetRuntime();
                    return false;
                }

                if (groupsByTarget.ContainsKey(target))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' tried to register '{target.name}' more than once.", this);
                    ResetRuntime();
                    return false;
                }

                if (!groupsById.TryGetValue(target.ConnectedTargetGroupId, out RuntimeGroup group))
                {
                    group = new RuntimeGroup { GroupId = target.ConnectedTargetGroupId };
                    groupsById.Add(group.GroupId, group);
                }

                group.Members.Add(new RuntimeMember { LaneIndex = laneIndex, Target = target });
                groupsByTarget.Add(target, group);
            }

            if (groupsById.Count == 0)
            {
                return true;
            }

            if (presentationSystem == null || !presentationSystem.ValidateConfiguration())
            {
                ResetRuntime();
                return false;
            }

            foreach (KeyValuePair<int, RuntimeGroup> pair in groupsById)
            {
                if (!ValidateRuntimeGroup(pair.Value))
                {
                    ResetRuntime();
                    return false;
                }
            }

            presentationSystem.BeginBuild(colorCatalog);
            foreach (KeyValuePair<int, RuntimeGroup> pair in groupsById)
            {
                RuntimeGroup group = pair.Value;
                List<TargetBox> memberTargets = new List<TargetBox>(group.Members.Count);
                for (int i = 0; i < group.Members.Count; i++)
                {
                    memberTargets.Add(group.Members[i].Target);
                }

                if (!presentationSystem.BuildGroup(group.GroupId, memberTargets))
                {
                    ResetRuntime();
                    return false;
                }
            }

            foreach (KeyValuePair<int, RuntimeGroup> pair in groupsById)
            {
                TryActivateGroupWhenAllMembersAreCurrent(pair.Value, true);
            }

            return true;
        }

        internal void NotifyTargetReachedFront(TargetBox target)
        {
            if (target == null || !groupsByTarget.TryGetValue(target, out RuntimeGroup group) ||
                group.State != ConnectedTargetGroupState.Waiting || group.MembersActivated)
            {
                return;
            }

            RuntimeMember member = FindMember(group, target);
            if (member == null || !laneController.IsCurrentTarget(member.LaneIndex, target))
            {
                return;
            }

            TryActivateGroupWhenAllMembersAreCurrent(group, false);
        }

        internal bool TryHandleTargetFull(TargetBox target)
        {
            if (target == null || !groupsByTarget.TryGetValue(target, out RuntimeGroup group))
            {
                Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' received an unregistered full Connected Target.", this);
                return false;
            }

            if (group.State == ConnectedTargetGroupState.Completed)
            {
                return false;
            }

            if (group.State == ConnectedTargetGroupState.Completing)
            {
                return true;
            }

            RuntimeMember member = FindMember(group, target);
            if (member == null || !laneController.TryLockConnectedTarget(member.LaneIndex, target))
            {
                Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' could not lock full member '{target.name}' in group {group.GroupId}.", this);
                return false;
            }

            presentationSystem?.RefreshTarget(target);
            if (!AreAllMembersFullAndActive(group))
            {
                return true;
            }

            return BeginGroupCompletion(group);
        }

        internal void NotifyTargetRevealed(TargetBox target)
        {
            presentationSystem?.RefreshTarget(target);
        }

        private void TryActivateGroupWhenAllMembersAreCurrent(RuntimeGroup group, bool immediate)
        {
            if (group == null || group.State != ConnectedTargetGroupState.Waiting || group.MembersActivated ||
                !AreAllMembersCurrent(group))
            {
                return;
            }

            group.MembersActivated = true;
            group.FrontActivationCompletedLanes.Clear();
            group.FrontActivationBarrierCompleted = false;

            if (immediate && AreAllMembersRuntimeUnlocked(group))
            {
                for (int i = 0; i < group.Members.Count; i++)
                {
                    RuntimeMember member = group.Members[i];
                    member.Target.SetActiveViewImmediate();
                    presentationSystem?.RefreshTarget(member.Target);
                }

                group.FrontActivationBarrierCompleted = true;
                return;
            }

            int expectedLifecycleVersion = lifecycleVersion;
            group.IsStartingFrontActivations = true;
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                member.Target.SetActiveView(() =>
                    OnFrontMemberActivated(group, member, expectedLifecycleVersion));
            }

            group.IsStartingFrontActivations = false;
            TryFinalizeFrontActivationBarrier(group, expectedLifecycleVersion);
        }

        private static bool AreAllMembersRuntimeUnlocked(RuntimeGroup group)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (group.Members[i]?.Target == null || group.Members[i].Target.IsRuntimeLocked)
                {
                    return false;
                }
            }

            return true;
        }

        private void OnFrontMemberActivated(RuntimeGroup group, RuntimeMember member, int expectedLifecycleVersion)
        {
            if (!IsWaitingGroupCurrent(group, expectedLifecycleVersion) || member == null)
            {
                return;
            }

            group.FrontActivationCompletedLanes.Add(member.LaneIndex);
            presentationSystem?.RefreshTarget(member.Target);
            if (!group.IsStartingFrontActivations)
            {
                TryFinalizeFrontActivationBarrier(group, expectedLifecycleVersion);
            }
        }

        private void TryFinalizeFrontActivationBarrier(RuntimeGroup group, int expectedLifecycleVersion)
        {
            if (!IsWaitingGroupCurrent(group, expectedLifecycleVersion) ||
                group.FrontActivationBarrierCompleted ||
                group.FrontActivationCompletedLanes.Count != group.Members.Count)
            {
                return;
            }

            group.FrontActivationBarrierCompleted = true;
            for (int i = 0; i < group.Members.Count; i++)
            {
                laneController.ResumeLaneAfterConnectedTransition(group.Members[i].LaneIndex);
            }
        }

        private bool IsWaitingGroupCurrent(RuntimeGroup group, int expectedLifecycleVersion)
        {
            return group != null &&
                   expectedLifecycleVersion == lifecycleVersion &&
                   group.State == ConnectedTargetGroupState.Waiting &&
                   group.MembersActivated &&
                   groupsById.TryGetValue(group.GroupId, out RuntimeGroup currentGroup) &&
                   ReferenceEquals(currentGroup, group);
        }

        public void ResetRuntime()
        {
            lifecycleVersion++;

            if (laneController != null)
            {
                foreach (KeyValuePair<int, RuntimeGroup> pair in groupsById)
                {
                    RuntimeGroup group = pair.Value;
                    if (group.State != ConnectedTargetGroupState.Completing)
                    {
                        continue;
                    }

                    for (int i = 0; i < group.Members.Count; i++)
                    {
                        laneController.AbortConnectedTransition(
                            group.Members[i].LaneIndex,
                            group.GroupId,
                            group.TransactionId);
                    }
                }
            }

            presentationSystem?.Clear();
            groupsByTarget.Clear();
            groupsById.Clear();
            laneController = null;
        }

        private bool BeginGroupCompletion(RuntimeGroup group)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.CanBeginConnectedTransition(member.LaneIndex, member.Target) ||
                    !laneController.CanPrepareConnectedCompletion(member.LaneIndex, member.Target))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' cannot begin atomic completion for group {group.GroupId}; lane {member.LaneIndex + 1} is not ready.", this);
                    return false;
                }
            }

            group.State = ConnectedTargetGroupState.Completing;
            group.TransactionId = ++nextTransactionId;
            group.CompletedAnimations.Clear();
            group.ShiftReadyLanes.Clear();
            group.ActivatedLanes.Clear();
            group.LineCollapseCompleted = false;
            group.CompletionFeedbackPlayed = false;

            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.BeginConnectedTransition(member.LaneIndex, member.Target, group.GroupId, group.TransactionId))
                {
                    AbortGroupTransaction(group);
                    return false;
                }
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.PrepareConnectedCompletion(member.LaneIndex, member.Target, group.GroupId, group.TransactionId))
                {
                    AbortGroupTransaction(group);
                    return false;
                }
            }

            int expectedLifecycleVersion = lifecycleVersion;
            int expectedTransactionId = group.TransactionId;
            presentationSystem.PlayCollapse(group.GroupId, () =>
                OnLineCollapseCompleted(group, expectedLifecycleVersion, expectedTransactionId));

            if (!group.CompletionFeedbackPlayed)
            {
                group.CompletionFeedbackPlayed = true;
                laneController.PlayConnectedCompletionFeedback();
            }

            group.IsStartingAnimations = true;
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                laneController.PlayConnectedCompletion(
                    member.LaneIndex,
                    member.Target,
                    group.GroupId,
                    group.TransactionId,
                    () => OnMemberAnimationCompleted(group, member.Target, expectedLifecycleVersion, expectedTransactionId));
            }

            group.IsStartingAnimations = false;
            TryFinalizeAnimationBarrier(group, expectedLifecycleVersion, expectedTransactionId);
            return true;
        }

        private void OnMemberAnimationCompleted(RuntimeGroup group, TargetBox target, int expectedLifecycleVersion, int expectedTransactionId)
        {
            if (!IsCurrentTransaction(group, expectedLifecycleVersion, expectedTransactionId))
            {
                return;
            }

            group.CompletedAnimations.Add(target);
            if (!group.IsStartingAnimations)
            {
                TryFinalizeAnimationBarrier(group, expectedLifecycleVersion, expectedTransactionId);
            }
        }

        private void OnLineCollapseCompleted(RuntimeGroup group, int expectedLifecycleVersion, int expectedTransactionId)
        {
            if (!IsCurrentTransaction(group, expectedLifecycleVersion, expectedTransactionId))
            {
                return;
            }

            group.LineCollapseCompleted = true;
            if (!group.IsStartingAnimations)
            {
                TryFinalizeAnimationBarrier(group, expectedLifecycleVersion, expectedTransactionId);
            }
        }

        private void TryFinalizeAnimationBarrier(RuntimeGroup group, int expectedLifecycleVersion, int expectedTransactionId)
        {
            if (!IsCurrentTransaction(group, expectedLifecycleVersion, expectedTransactionId) ||
                !group.LineCollapseCompleted ||
                group.CompletedAnimations.Count != group.Members.Count)
            {
                return;
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.CanFinalizeConnectedRemoval(
                        member.LaneIndex,
                        member.Target,
                        group.GroupId,
                        expectedTransactionId))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' cannot atomically remove group {group.GroupId}; lane {member.LaneIndex + 1} failed prevalidation.", this);
                    return;
                }
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.FinalizeConnectedRemoval(
                        member.LaneIndex,
                        member.Target,
                        group.GroupId,
                        expectedTransactionId))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' encountered an unexpected removal failure after prevalidation for group {group.GroupId}.", this);
                    return;
                }
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                groupsByTarget.Remove(group.Members[i].Target);
            }

            presentationSystem.RemoveGroup(group.GroupId);
            BeginShiftBarrier(group, expectedLifecycleVersion, expectedTransactionId);
        }

        private void BeginShiftBarrier(RuntimeGroup group, int expectedLifecycleVersion, int expectedTransactionId)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.CanBeginConnectedShiftAndReveal(member.LaneIndex, group.GroupId, expectedTransactionId))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' cannot begin the shift barrier for group {group.GroupId}; lane {member.LaneIndex + 1} failed prevalidation.", this);
                    return;
                }
            }

            group.ShiftReadyLanes.Clear();
            group.IsStartingShifts = true;
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                laneController.BeginConnectedShiftAndReveal(
                    member.LaneIndex,
                    group.GroupId,
                    expectedTransactionId,
                    () => OnLaneShiftReady(group, member.LaneIndex, expectedLifecycleVersion, expectedTransactionId));
            }

            group.IsStartingShifts = false;
            TryActivateNextTargets(group, expectedLifecycleVersion, expectedTransactionId);
        }

        private void OnLaneShiftReady(RuntimeGroup group, int laneIndex, int expectedLifecycleVersion, int expectedTransactionId)
        {
            if (!IsCurrentTransaction(group, expectedLifecycleVersion, expectedTransactionId))
            {
                return;
            }

            group.ShiftReadyLanes.Add(laneIndex);
            if (!group.IsStartingShifts)
            {
                TryActivateNextTargets(group, expectedLifecycleVersion, expectedTransactionId);
            }
        }

        private void TryActivateNextTargets(RuntimeGroup group, int expectedLifecycleVersion, int expectedTransactionId)
        {
            if (!IsCurrentTransaction(group, expectedLifecycleVersion, expectedTransactionId) ||
                group.ShiftReadyLanes.Count != group.Members.Count)
            {
                return;
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.CanActivateConnectedNextTarget(member.LaneIndex, group.GroupId, expectedTransactionId))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' cannot activate next targets for group {group.GroupId}; lane {member.LaneIndex + 1} failed prevalidation.", this);
                    return;
                }
            }

            group.ActivatedLanes.Clear();
            group.IsStartingActivations = true;
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                laneController.ActivateConnectedNextTarget(
                    member.LaneIndex,
                    group.GroupId,
                    expectedTransactionId,
                    () => OnLaneActivated(group, member.LaneIndex, expectedLifecycleVersion, expectedTransactionId));
            }

            group.IsStartingActivations = false;
            TryCompleteTransaction(group, expectedLifecycleVersion, expectedTransactionId);
        }

        private void OnLaneActivated(RuntimeGroup group, int laneIndex, int expectedLifecycleVersion, int expectedTransactionId)
        {
            if (!IsCurrentTransaction(group, expectedLifecycleVersion, expectedTransactionId))
            {
                return;
            }

            group.ActivatedLanes.Add(laneIndex);
            if (!group.IsStartingActivations)
            {
                TryCompleteTransaction(group, expectedLifecycleVersion, expectedTransactionId);
            }
        }

        private void TryCompleteTransaction(RuntimeGroup group, int expectedLifecycleVersion, int expectedTransactionId)
        {
            if (!IsCurrentTransaction(group, expectedLifecycleVersion, expectedTransactionId) ||
                group.ActivatedLanes.Count != group.Members.Count)
            {
                return;
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.CanCompleteConnectedTransition(member.LaneIndex, group.GroupId, expectedTransactionId))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' cannot release the lane barrier for group {group.GroupId}; lane {member.LaneIndex + 1} failed prevalidation.", this);
                    return;
                }
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (!laneController.CompleteConnectedTransition(member.LaneIndex, group.GroupId, expectedTransactionId))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' could not release lane {member.LaneIndex + 1} from group {group.GroupId} transaction {expectedTransactionId}.", this);
                    return;
                }
            }

            group.State = ConnectedTargetGroupState.Completed;
            groupsById.Remove(group.GroupId);
            laneController.NotifyConnectedTransactionCompleted();

            for (int i = 0; i < group.Members.Count; i++)
            {
                laneController.ResumeLaneAfterConnectedTransition(group.Members[i].LaneIndex);
            }

            group.Members.Clear();
        }

        private void AbortGroupTransaction(RuntimeGroup group)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                laneController.AbortConnectedTransition(
                    group.Members[i].LaneIndex,
                    group.GroupId,
                    group.TransactionId);
            }

            group.State = ConnectedTargetGroupState.Waiting;
            group.CompletedAnimations.Clear();
            group.ShiftReadyLanes.Clear();
            group.ActivatedLanes.Clear();
        }

        private bool ValidateRuntimeGroup(RuntimeGroup group)
        {
            if (group == null || group.GroupId <= 0 || group.Members.Count < 2 || group.Members.Count > LevelDefinition.TargetBoxLaneCount)
            {
                Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' found an invalid Connected Target group.", this);
                return false;
            }

            HashSet<int> lanes = new HashSet<int>();
            HashSet<TargetBox> targets = new HashSet<TargetBox>();
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (member.Target == null || !targets.Add(member.Target) || !lanes.Add(member.LaneIndex))
                {
                    Debug.LogError($"{nameof(ConnectedTargetGroupController)} on '{name}' found duplicate target or lane membership in group {group.GroupId}.", this);
                    return false;
                }
            }

            return true;
        }

        private static RuntimeMember FindMember(RuntimeGroup group, TargetBox target)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (group.Members[i].Target == target)
                {
                    return group.Members[i];
                }
            }

            return null;
        }

        private bool AreAllMembersCurrent(RuntimeGroup group)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                RuntimeMember member = group.Members[i];
                if (member.Target == null || !laneController.IsCurrentTarget(member.LaneIndex, member.Target))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreAllMembersFullAndActive(RuntimeGroup group)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                TargetBox target = group.Members[i].Target;
                if (target == null || !target.IsActive || !target.IsComplete || !target.IsCompleting)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsCurrentTransaction(RuntimeGroup group, int expectedLifecycleVersion, int expectedTransactionId)
        {
            return group != null &&
                   expectedLifecycleVersion == lifecycleVersion &&
                   group.State == ConnectedTargetGroupState.Completing &&
                   group.TransactionId == expectedTransactionId &&
                   groupsById.TryGetValue(group.GroupId, out RuntimeGroup currentGroup) &&
                   ReferenceEquals(currentGroup, group);
        }

        private static bool ContainsConnectedTargets(LevelDefinition levelDefinition)
        {
            if (levelDefinition?.TargetBoxLanes == null)
            {
                return false;
            }

            IReadOnlyList<TargetBoxLaneData> lanes = levelDefinition.TargetBoxLanes;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                IReadOnlyList<TargetBoxData> boxes = lanes[laneIndex]?.Boxes;
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
    }
}

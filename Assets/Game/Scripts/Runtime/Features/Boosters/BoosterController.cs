using System;
using System.Collections.Generic;
using Gameplay.Levels;
using UnityEngine;

namespace Gameplay.Boosters
{
    [DisallowMultipleComponent]
    public sealed class BoosterController : MonoBehaviour
    {
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private LevelResultFlowController levelResultFlowController;

        public BoosterType ActiveBooster { get; private set; } = BoosterType.None;
        public BoosterState State { get; private set; } = BoosterState.Idle;
        public bool IsBoosterActive => ActiveBooster != BoosterType.None && State != BoosterState.Idle;
        public bool IsInputOverrideActive => ActiveBooster == BoosterType.Hand && State == BoosterState.Targeting;
        public bool IsGameplayInputBlocked => IsBoosterActive && State == BoosterState.Running;
        public bool IsGameplayPlaying => levelSessionController != null && levelSessionController.IsPlaying;
        public bool CanStartNewBoosterActivation
        {
            get
            {
                CacheRuntimeReferences();
                return !IsBoosterActive &&
                       IsGameplayPlaying &&
                       (levelResultFlowController == null ||
                        !levelResultFlowController.IsLogicFailConditionMet);
            }
        }
        public bool CanCancelActiveBooster => IsBoosterActive && CanCancel(ActiveBooster);

        /// <summary>
        /// Child booster controllers capture this value when activation begins and include it in later notifications.
        /// A reset, completion, cancellation, or newer activation invalidates callbacks carrying an older value.
        /// </summary>
        public int ActiveLifecycleVersion => lifecycleVersion;

        public event Action<BoosterType> BoosterActivated;
        public event Action<BoosterType> BoosterCompleted;
        public event Action<BoosterType> BoosterCancelled;
        public event Action<BoosterType, BoosterState> BoosterStateChanged;
        public event Action BoosterAvailabilityChanged;

        private int lifecycleVersion;
        private readonly Dictionary<BoosterType, Func<bool>> activationGuards =
            new Dictionary<BoosterType, Func<bool>>();
        private readonly Dictionary<BoosterType, Func<bool>> cancellationGuards =
            new Dictionary<BoosterType, Func<bool>>();

        private void Awake()
        {
            CacheRuntimeReferences();
        }

        private void OnEnable()
        {
            CacheRuntimeReferences();
            if (levelSessionController != null)
            {
                levelSessionController.GameplayStateChanged -= HandleGameplayStateChanged;
                levelSessionController.GameplayStateChanged += HandleGameplayStateChanged;
            }

            if (levelResultFlowController != null)
            {
                levelResultFlowController.LogicFailConditionChanged -= HandleLogicFailConditionChanged;
                levelResultFlowController.LogicFailConditionChanged += HandleLogicFailConditionChanged;
            }
        }

        public bool TryActivateBooster(BoosterType type)
        {
            if (!CanActivateBooster(type))
            {
                return false;
            }

            lifecycleVersion++;
            ActiveBooster = type;
            State = GetInitialState(type);
            BoosterActivated?.Invoke(type);
            BoosterStateChanged?.Invoke(ActiveBooster, State);
            return true;
        }

        public bool CanActivateBooster(BoosterType type)
        {
            CacheRuntimeReferences();
            return IsSupportedBooster(type) &&
                   CanStartNewBoosterActivation &&
                   CanActivate(type);
        }

        public void NotifyAvailabilityChanged()
        {
            BoosterAvailabilityChanged?.Invoke();
        }

        public bool RegisterActivationGuard(BoosterType type, Func<bool> activationGuard)
        {
            if (!IsSupportedBooster(type) || activationGuard == null)
            {
                return false;
            }

            activationGuards[type] = activationGuard;
            BoosterAvailabilityChanged?.Invoke();
            return true;
        }

        public void UnregisterActivationGuard(BoosterType type, Func<bool> activationGuard)
        {
            if (activationGuard == null ||
                !activationGuards.TryGetValue(type, out Func<bool> registeredGuard) ||
                registeredGuard != activationGuard)
            {
                return;
            }

            activationGuards.Remove(type);
            BoosterAvailabilityChanged?.Invoke();
        }

        public bool RegisterCancellationGuard(BoosterType type, Func<bool> cancellationGuard)
        {
            if (!IsSupportedBooster(type) || cancellationGuard == null)
            {
                return false;
            }

            cancellationGuards[type] = cancellationGuard;
            BoosterAvailabilityChanged?.Invoke();
            return true;
        }

        public void UnregisterCancellationGuard(BoosterType type, Func<bool> cancellationGuard)
        {
            if (cancellationGuard == null ||
                !cancellationGuards.TryGetValue(type, out Func<bool> registeredGuard) ||
                registeredGuard != cancellationGuard)
            {
                return;
            }

            cancellationGuards.Remove(type);
            BoosterAvailabilityChanged?.Invoke();
        }

        public bool CancelActiveBooster()
        {
            if (!CanCancel(ActiveBooster))
            {
                return false;
            }

            return TryFinishActiveBooster(ActiveBooster, lifecycleVersion, false);
        }

        public bool NotifyTargetingStarted(BoosterType type, int expectedLifecycleVersion)
        {
            return TrySetActiveState(type, expectedLifecycleVersion, BoosterState.Targeting);
        }

        public bool NotifyExecutionStarted(BoosterType type, int expectedLifecycleVersion)
        {
            return TrySetActiveState(type, expectedLifecycleVersion, BoosterState.Running);
        }

        public bool NotifyCompleted(BoosterType type, int expectedLifecycleVersion)
        {
            return TryFinishActiveBooster(type, expectedLifecycleVersion, true);
        }

        public bool NotifyCancelled(BoosterType type, int expectedLifecycleVersion)
        {
            return TryFinishActiveBooster(type, expectedLifecycleVersion, false);
        }

        public void ResetRuntime()
        {
            if (IsBoosterActive)
            {
                TryFinishActiveBooster(ActiveBooster, lifecycleVersion, false);
                return;
            }

            lifecycleVersion++;
            ActiveBooster = BoosterType.None;
            State = BoosterState.Idle;
        }

        private void OnDisable()
        {
            if (levelSessionController != null)
            {
                levelSessionController.GameplayStateChanged -= HandleGameplayStateChanged;
            }

            if (levelResultFlowController != null)
            {
                levelResultFlowController.LogicFailConditionChanged -= HandleLogicFailConditionChanged;
            }

            ResetRuntime();
        }

        private void HandleLogicFailConditionChanged()
        {
            NotifyAvailabilityChanged();
        }

        private void HandleGameplayStateChanged(GameplaySessionState state)
        {
            if (state == GameplaySessionState.Recovering ||
                state == GameplaySessionState.Won ||
                state == GameplaySessionState.Failed ||
                state == GameplaySessionState.Exiting)
            {
                ResetRuntime();
            }
        }

        private bool TrySetActiveState(
            BoosterType type,
            int expectedLifecycleVersion,
            BoosterState nextState)
        {
            if (!MatchesActiveLifecycle(type, expectedLifecycleVersion) || nextState == BoosterState.Idle)
            {
                return false;
            }

            if (State == nextState)
            {
                return true;
            }

            State = nextState;
            BoosterStateChanged?.Invoke(ActiveBooster, State);
            return true;
        }

        private bool TryFinishActiveBooster(
            BoosterType type,
            int expectedLifecycleVersion,
            bool completed)
        {
            if (!MatchesActiveLifecycle(type, expectedLifecycleVersion))
            {
                return false;
            }

            BoosterType finishedBooster = ActiveBooster;
            lifecycleVersion++;
            ActiveBooster = BoosterType.None;
            State = BoosterState.Idle;
            BoosterStateChanged?.Invoke(ActiveBooster, State);

            if (completed)
            {
                BoosterCompleted?.Invoke(finishedBooster);
            }
            else
            {
                BoosterCancelled?.Invoke(finishedBooster);
            }

            return true;
        }

        private bool MatchesActiveLifecycle(BoosterType type, int expectedLifecycleVersion)
        {
            return IsBoosterActive &&
                   ActiveBooster == type &&
                   lifecycleVersion == expectedLifecycleVersion;
        }

        private bool CanActivate(BoosterType type)
        {
            if (!activationGuards.TryGetValue(type, out Func<bool> activationGuard))
            {
                return type != BoosterType.Ufo;
            }

            return activationGuard();
        }

        private bool CanCancel(BoosterType type)
        {
            return !cancellationGuards.TryGetValue(type, out Func<bool> cancellationGuard) ||
                   cancellationGuard();
        }

        private static bool IsSupportedBooster(BoosterType type)
        {
            return type == BoosterType.Hand || type == BoosterType.Shuffle || type == BoosterType.Ufo;
        }

        private static BoosterState GetInitialState(BoosterType type)
        {
            return type == BoosterType.Hand ? BoosterState.Targeting : BoosterState.Running;
        }

        private void CacheRuntimeReferences()
        {
            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>(FindObjectsInactive.Include);
            }

            if (levelResultFlowController == null)
            {
                levelResultFlowController = FindFirstObjectByType<LevelResultFlowController>(FindObjectsInactive.Include);
            }
        }
    }
}

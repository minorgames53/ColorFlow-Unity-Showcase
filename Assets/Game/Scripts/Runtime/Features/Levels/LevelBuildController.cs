using System;
using System.Collections.Generic;
using Game.Shared.Audio;
using Game.Shared.Haptics;
using Gameplay.Conveyor;
using Gameplay.BoardFeatures.KeyLocks;
using Gameplay.Boosters;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using Gameplay.UI.World;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Gameplay.Levels
{
    public sealed class LevelBuildController : MonoBehaviour
    {
        [SerializeField] private LevelDefinition initialLevel;
        [SerializeField] private bool buildOnStart;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private TargetLaneController targetLaneController;
        [SerializeField] private ConveyorController conveyorController;
        [SerializeField] private ConveyorEntryZone conveyorEntryZone;
        [SerializeField] private MarbleCapacityController capacityController;
        [SerializeField] private SourceBoxBoardFullMessageView boardFullMessageView;
        [SerializeField] private Transform lockWaitingArea;
        [SerializeField] private BoosterController boosterController;

        internal ConveyorEntryZone ConveyorEntryZone => conveyorEntryZone;

        private readonly List<string> validationErrors = new List<string>();
        private readonly KeyLockFeatureController keyLockFeatureController = new KeyLockFeatureController();

        public LevelDefinition CurrentLevel { get; private set; }
        public event Action LevelClearing;

        private void OnEnable()
        {
            if (boosterController == null)
            {
                boosterController = FindFirstObjectByType<BoosterController>(FindObjectsInactive.Include);
            }

            if (sourceBoxBoardController != null)
            {
                sourceBoxBoardController.SourceBoxSpawned -= HandleSourceBoxSpawned;
                sourceBoxBoardController.SourceBoxSpawned += HandleSourceBoxSpawned;
            }

            if (targetLaneController != null)
            {
                targetLaneController.TargetBoxFirstFilled -= HandleTargetBoxFirstFilled;
                targetLaneController.TargetBoxFirstFilled += HandleTargetBoxFirstFilled;
                targetLaneController.TargetBoxSpawned -= HandleTargetBoxSpawned;
                targetLaneController.TargetBoxSpawned += HandleTargetBoxSpawned;
                targetLaneController.TargetBoxReachedFront -= HandleTargetBoxReachedFront;
                targetLaneController.TargetBoxReachedFront += HandleTargetBoxReachedFront;
            }
        }

        private void OnDisable()
        {
            if (sourceBoxBoardController != null)
            {
                sourceBoxBoardController.SourceBoxSpawned -= HandleSourceBoxSpawned;
            }

            if (targetLaneController != null)
            {
                targetLaneController.TargetBoxFirstFilled -= HandleTargetBoxFirstFilled;
                targetLaneController.TargetBoxSpawned -= HandleTargetBoxSpawned;
                targetLaneController.TargetBoxReachedFront -= HandleTargetBoxReachedFront;
            }

            keyLockFeatureController.Clear();
            boosterController?.ResetRuntime();
        }

        private void Start()
        {
            if (!buildOnStart)
            {
                return;
            }

            if (initialLevel == null)
            {
                Debug.LogError($"{nameof(LevelBuildController)} on '{name}' cannot build on start because Initial Level reference is missing.", this);
                return;
            }

            BuildLevel(initialLevel);
        }

        public void BuildLevel(LevelDefinition levelDefinition)
        {
            if (!ValidateBuildInput(levelDefinition))
            {
                return;
            }

            ClearLevel();
#if UNITY_EDITOR
            Gameplay.MarbleDebug.MarbleDebugTracker.BeginAttempt(levelDefinition, this);
#endif
            CurrentLevel = levelDefinition;
            keyLockFeatureController.Prepare(
                levelDefinition,
                sourceBoxBoardController.FeatureCatalog,
                lockWaitingArea,
                targetLaneController,
                this);
            sourceBoxBoardController.BuildBoard(levelDefinition);
            targetLaneController.Build(levelDefinition);
            SeedInitialConveyor(levelDefinition);
#if UNITY_EDITOR
            Gameplay.MarbleDebug.MarbleDebugTracker.CompleteBuild();
#endif
            AudioManager.Instance?.PlaySfx(AudioKey.GameStart);
            HapticManager.Instance?.Play(HapticType.Soft);
        }

        private void SeedInitialConveyor(LevelDefinition levelDefinition)
        {
            IReadOnlyList<MarbleColorId> colors = levelDefinition?.InitialConveyorMarbles;
            if (colors == null || colors.Count == 0 || conveyorController == null ||
                capacityController == null || sourceBoxBoardController == null)
            {
                return;
            }

            int seedCount = Mathf.Min(colors.Count, conveyorController.AvailableCapacity);
            for (int entryIndex = 0; entryIndex < seedCount; entryIndex++)
            {
                // Keep the authored color/slot mapping while registering the front marble first.
                int i = conveyorController.GetInitialMarbleSlotIndex(entryIndex, seedCount);
                MarbleColorId colorId = colors[i];
                if (!MarbleColorCatalog.IsGameplayColor(colorId) || !capacityController.TryReserve(1))
                {
                    continue;
                }

                if (!sourceBoxBoardController.TryCreateFieldMarble(colorId, out Marble marble))
                {
                    capacityController.CancelReservation(1);
                    continue;
                }

                if (!conveyorController.TryAddMarble(marble, i))
                {
                    capacityController.CancelReservation(1);
                    sourceBoxBoardController.DestroyRuntimeBoardObject(marble.gameObject);
                    continue;
                }

                capacityController.CommitReservation(1);
            }
        }

#if UNITY_EDITOR
        public bool CanBuildForEditor(LevelDefinition levelDefinition) => ValidateBuildInput(levelDefinition);

        [Button]
        public void BuildLevel()
        {
            BuildLevel(initialLevel);
        }
#endif

        public void ClearLevel()
        {
#if UNITY_EDITOR
            Gameplay.MarbleDebug.MarbleDebugTracker.EndAttempt("Level clear / retry / rebuild");
#endif
            LevelClearing?.Invoke();
            boosterController?.ResetRuntime();
            keyLockFeatureController.Clear();
            conveyorEntryZone?.ClearQueue();
            boardFullMessageView?.ResetImmediate();
            conveyorController?.Clear();
            sourceBoxBoardController?.ClearBoard();
            targetLaneController?.Clear();
            capacityController?.ResetCounts();
            CurrentLevel = null;
        }

        private bool ValidateBuildInput(LevelDefinition levelDefinition)
        {
            if (sourceBoxBoardController == null)
            {
                Debug.LogError($"{nameof(LevelBuildController)} on '{name}' is missing SourceBoxBoardController reference.", this);
                return false;
            }

            if (targetLaneController == null)
            {
                Debug.LogError($"{nameof(LevelBuildController)} on '{name}' is missing TargetLaneController reference.", this);
                return false;
            }

            validationErrors.Clear();
            if (!LevelDefinitionValidator.ValidateForRuntimeBuild(levelDefinition, validationErrors))
            {
                for (int i = 0; i < validationErrors.Count; i++)
                {
                    Debug.LogError($"{nameof(LevelBuildController)} on '{name}' cannot build level: {validationErrors[i]}", this);
                }

                return false;
            }

            if (!sourceBoxBoardController.CanBuild(levelDefinition))
            {
                return false;
            }

            if (!targetLaneController.CanBuild(levelDefinition))
            {
                return false;
            }

            if (!KeyLockFeatureController.ValidateConfiguration(
                    levelDefinition,
                    sourceBoxBoardController.FeatureCatalog,
                    lockWaitingArea,
                    this))
            {
                return false;
            }

            return true;
        }

        private void HandleTargetBoxFirstFilled(TargetBox targetBox)
        {
            sourceBoxBoardController?.NotifyTargetBoxFirstFilled(targetBox);
        }

        private void HandleSourceBoxSpawned(SourceBox sourceBox)
        {
            keyLockFeatureController.RegisterSourceBox(sourceBox);
        }

        private void HandleTargetBoxSpawned(TargetBox targetBox, int laneIndex, bool isFront)
        {
            keyLockFeatureController.RegisterTargetBox(targetBox, laneIndex, isFront);
        }

        private void HandleTargetBoxReachedFront(TargetBox targetBox, int laneIndex)
        {
            keyLockFeatureController.NotifyTargetReachedFront(targetBox, laneIndex);
        }
    }
}

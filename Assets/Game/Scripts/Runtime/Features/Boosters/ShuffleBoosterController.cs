using System.Collections.Generic;
using Game.Shared.Audio;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using UnityEngine;

namespace Gameplay.Boosters
{
    [DisallowMultipleComponent]
    public sealed class ShuffleBoosterController : MonoBehaviour
    {
        [SerializeField] private BoosterController boosterController;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private TargetLaneController targetLaneController;
        [SerializeField] private LevelSessionController levelSessionController;

        private readonly HashSet<MarbleColorId> playableSourceColors = new HashSet<MarbleColorId>();
        private int activeLifecycleVersion;
        private bool ownsActiveShuffle;

        private void Awake()
        {
            CacheMissingReferences();
        }

        private void OnEnable()
        {
            CacheMissingReferences();
            Subscribe();
            boosterController?.RegisterActivationGuard(BoosterType.Shuffle, CanActivateShuffle);
            TryStartActiveShuffle();
        }

        private void OnDisable()
        {
            boosterController?.UnregisterActivationGuard(BoosterType.Shuffle, CanActivateShuffle);
            Unsubscribe();
            targetLaneController?.CancelShuffle();
            ownsActiveShuffle = false;

            if (boosterController != null && boosterController.ActiveBooster == BoosterType.Shuffle)
            {
                boosterController.NotifyCancelled(BoosterType.Shuffle, boosterController.ActiveLifecycleVersion);
            }
        }

        private void HandleBoosterActivated(BoosterType type)
        {
            if (type == BoosterType.Shuffle)
            {
                TryStartActiveShuffle();
            }
        }

        private void HandleBoosterStateChanged(BoosterType type, BoosterState state)
        {
            if (ownsActiveShuffle && (type != BoosterType.Shuffle || state != BoosterState.Running))
            {
                targetLaneController?.CancelShuffle();
                ownsActiveShuffle = false;
            }
        }

        private void TryStartActiveShuffle()
        {
            if (ownsActiveShuffle || boosterController == null || sourceBoxBoardController == null ||
                targetLaneController == null || boosterController.ActiveBooster != BoosterType.Shuffle ||
                boosterController.State != BoosterState.Running)
            {
                return;
            }

            activeLifecycleVersion = boosterController.ActiveLifecycleVersion;
            CollectPlayableSourceColors();
            ownsActiveShuffle = true;
            if (!targetLaneController.TryBeginShuffle(
                    playableSourceColors,
                    succeeded => HandleShuffleCompleted(succeeded, activeLifecycleVersion)))
            {
                ownsActiveShuffle = false;
                boosterController.NotifyCancelled(BoosterType.Shuffle, activeLifecycleVersion);
                return;
            }

            levelSessionController?.MarkPlayerMoveCommitted();
            AudioManager.Instance?.PlaySfx(AudioKey.Shuffle);
        }

        private bool CanActivateShuffle()
        {
            if (sourceBoxBoardController == null || targetLaneController == null)
            {
                return false;
            }

            CollectPlayableSourceColors();
            return targetLaneController.CanBeginShuffle(playableSourceColors);
        }

        private void CollectPlayableSourceColors()
        {
            playableSourceColors.Clear();
            IReadOnlyList<SourceBox> sourceBoxes = sourceBoxBoardController.SpawnedSourceBoxes;
            for (int i = 0; i < sourceBoxes.Count; i++)
            {
                SourceBox sourceBox = sourceBoxes[i];
                if (sourceBox != null && sourceBox.CanAttemptReleaseByUser)
                {
                    playableSourceColors.Add(sourceBox.ColorId);
                }
            }
        }

        private void HandleShuffleCompleted(bool succeeded, int expectedLifecycleVersion)
        {
            if (!ownsActiveShuffle || boosterController == null ||
                boosterController.ActiveLifecycleVersion != expectedLifecycleVersion)
            {
                return;
            }

            ownsActiveShuffle = false;
            if (succeeded)
            {
                boosterController.NotifyCompleted(BoosterType.Shuffle, expectedLifecycleVersion);
            }
            else
            {
                boosterController.NotifyCancelled(BoosterType.Shuffle, expectedLifecycleVersion);
            }
        }

        private void Subscribe()
        {
            if (boosterController == null)
            {
                return;
            }

            boosterController.BoosterActivated -= HandleBoosterActivated;
            boosterController.BoosterActivated += HandleBoosterActivated;
            boosterController.BoosterStateChanged -= HandleBoosterStateChanged;
            boosterController.BoosterStateChanged += HandleBoosterStateChanged;
        }

        private void Unsubscribe()
        {
            if (boosterController == null)
            {
                return;
            }

            boosterController.BoosterActivated -= HandleBoosterActivated;
            boosterController.BoosterStateChanged -= HandleBoosterStateChanged;
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

using System;
using Game.Shared.Developer.LevelTesting;
using Gameplay.Analytics;
using Gameplay.Levels;
using UnityEngine;

namespace Gameplay.Developer
{
    [DisallowMultipleComponent]
    public sealed class GameDevLevelTestAdapter : MonoBehaviour, IDevLevelTestAdapter
    {
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private LevelResultFlowController levelResultFlowController;
        [SerializeField] private LevelAnalyticsTracker levelAnalyticsTracker;

        public int CurrentLevelNumber => levelSessionController != null
            ? Mathf.Max(1, levelSessionController.DisplayedLevelNumber)
            : 1;

        public event Action<int> LevelChanged;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public bool TryLoadLevel(int oneBasedLevelNumber)
        {
            if (oneBasedLevelNumber < 1)
            {
                return false;
            }

            CacheReferences();
            if (levelSessionController == null)
            {
                return false;
            }

            levelResultFlowController?.ResetForDebugLevelLoad();
            levelAnalyticsTracker?.SuppressCurrentAttemptForDebug();
            bool loaded = levelSessionController.TryLoadDisplayedLevelForDebug(oneBasedLevelNumber);
            if (loaded)
            {
                levelResultFlowController?.ResetForDebugLevelLoad();
            }

            return loaded;
        }

        public bool TryTriggerWin()
        {
            CacheReferences();
            if (levelResultFlowController == null)
            {
                return false;
            }

            levelAnalyticsTracker?.SuppressCurrentAttemptForDebug();
            levelResultFlowController.TriggerWin();
            return true;
        }

        public bool TryTriggerLose()
        {
            CacheReferences();
            if (levelResultFlowController == null)
            {
                return false;
            }

            levelAnalyticsTracker?.SuppressCurrentAttemptForDebug();
            levelResultFlowController.TriggerLose();
            return true;
        }

        private void CacheReferences()
        {
            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>();
            }

            if (levelResultFlowController == null)
            {
                levelResultFlowController = FindFirstObjectByType<LevelResultFlowController>();
            }

            if (levelAnalyticsTracker == null)
            {
                levelAnalyticsTracker = LevelAnalyticsTracker.Instance != null
                    ? LevelAnalyticsTracker.Instance
                    : FindFirstObjectByType<LevelAnalyticsTracker>();
            }
        }

        private void Subscribe()
        {
            if (levelSessionController == null)
            {
                return;
            }

            levelSessionController.DisplayedLevelNumberChanged -= HandleDisplayedLevelNumberChanged;
            levelSessionController.DisplayedLevelNumberChanged += HandleDisplayedLevelNumberChanged;
        }

        private void Unsubscribe()
        {
            if (levelSessionController == null)
            {
                return;
            }

            levelSessionController.DisplayedLevelNumberChanged -= HandleDisplayedLevelNumberChanged;
        }

        private void HandleDisplayedLevelNumberChanged(int levelNumber)
        {
            LevelChanged?.Invoke(Mathf.Max(1, levelNumber));
        }
    }
}

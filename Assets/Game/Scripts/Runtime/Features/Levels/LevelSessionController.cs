using System;
using Game.Shared.Audio;
using Game.Shared.Bootstrap;
using Game.Shared.Save;
using Gameplay.Analytics;
using Gameplay.SourceBoxes;
using Gameplay.Tutorial;
using UnityEngine;

namespace Gameplay.Levels
{
    public enum GameplaySessionState
    {
        Playing,
        Paused,
        Recovering,
        Won,
        Failed,
        Exiting
    }

    [DisallowMultipleComponent]
    public sealed class LevelSessionController : MonoBehaviour
    {
        [SerializeField] private LevelCatalog levelCatalog;
        [SerializeField] private LevelBuildController levelBuildController;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private LevelAnalyticsTracker levelAnalyticsTracker;
        [SerializeField] private LevelOneTapTutorialController levelOneTapTutorialController;
        [SerializeField] private BoosterUnlockTutorialController boosterUnlockTutorialController;

        private LevelProgressController progressController;
        private SaveManager saveManager;
        private float timeScaleBeforeSuspension = 1f;
        private bool ownsTimeScaleSuspension;

#if UNITY_EDITOR
        private LevelDefinition editorLevelOverride;

        public static bool TryPlayLevelForEditor(LevelDefinition definition)
        {
            if (!Application.isPlaying || definition == null) return false;
            var session = FindFirstObjectByType<LevelSessionController>();
            var loader = Game.Shared.Navigation.SceneLoader.Instance;
            if (session == null || !session.EnsureReady() || session.levelBuildController == null)
            {
                Debug.LogWarning("Level Management needs an active Game scene with a LevelSessionController and LevelBuildController. Open Game first; Play does not load scenes.");
                return false;
            }

            var result = FindFirstObjectByType<LevelResultFlowController>();
            if (session.GameplayState == GameplaySessionState.Exiting ||
                loader != null && loader.State != Game.Shared.Navigation.SceneTransitionState.Idle ||
                result != null && result.IsFailOfferPurchaseLocked ||
                Game.Shared.Store.StoreManager.Instance != null && Game.Shared.Store.StoreManager.Instance.IsPurchasing)
            {
                Debug.LogWarning("Level Management cannot replace a session while exiting or fulfilling a purchase.");
                return false;
            }

            var panelManagers = FindObjectsByType<Game.Shared.UI.Panels.PanelManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var panels in panelManagers)
            {
                if (panels.gameObject.scene == session.gameObject.scene && panels.HasBlockingPanel)
                {
                    Debug.LogWarning("Close the blocking modal before playing another level from Level Management.");
                    return false;
                }
            }
            // Validate all build prerequisites before discarding the current attempt.
            if (!session.levelBuildController.CanBuildForEditor(definition)) return false;

            session.levelAnalyticsTracker?.SuppressCurrentAttemptForDebug();
            result?.ResetForDebugLevelLoad();
            session.TryBeginExit(); // Existing transfer, booster, recovery and pause cleanup only.
            foreach (var view in FindObjectsByType<Gameplay.UI.HUD.LevelCompleteCanvasView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (view.gameObject.scene == session.gameObject.scene) view.ResetForDebugLevelLoad();
            foreach (var panels in panelManagers)
                if (panels.gameObject.scene == session.gameObject.scene) panels.ResetForDebugLevelLoad();
            bool loaded = session.TryStartEditorLevel(definition);
            result?.ResetForDebugLevelLoad();
            return loaded;
        }

        private bool TryStartEditorLevel(LevelDefinition definition)
        {
            if (definition == null || !EnsureReady()) return false;
            editorLevelOverride = definition;
            DisplayedLevelNumber = Mathf.Max(1, definition.LevelNumber);
            CurrentLevelDefinition = definition;
            BuildCurrentLevel(true);
            return levelBuildController == null || levelBuildController.CurrentLevel == definition;
        }
#endif

        public int DisplayedLevelNumber { get; private set; }
        public LevelDefinition CurrentLevelDefinition { get; private set; }
        public bool HasCommittedPlayerMove { get; private set; }
        public bool HasSpentLifeForCurrentAttempt { get; private set; }
        public GameplaySessionState GameplayState { get; private set; } = GameplaySessionState.Playing;
        public bool IsPlaying => GameplayState == GameplaySessionState.Playing;
        public bool IsSimulationRunning => GameplayState == GameplaySessionState.Playing ||
                                           GameplayState == GameplaySessionState.Recovering ||
                                           GameplayState == GameplaySessionState.Failed;
        public event Action<int> DisplayedLevelNumberChanged;
        public event Action<GameplaySessionState> GameplayStateChanged;

        private void Start()
        {
            StartCurrentLevel();
        }

        private void OnDisable()
        {
            ReleaseTimeScaleSuspension();
        }

        public void StartCurrentLevel()
        {
#if UNITY_EDITOR
            editorLevelOverride = null;
#endif
            if (!EnsureReady())
            {
                return;
            }

            DisplayedLevelNumber = Mathf.Max(1, saveManager.CurrentLevel);
            BuildDisplayedLevel(false);
        }

        public void CompleteCurrentLevel()
        {
            MarkCurrentLevelCompleted();
            ContinueToCurrentLevel();
        }

        public void MarkCurrentLevelCompleted()
        {
            if (!EnsureReady())
            {
                return;
            }

            int completedLevelNumber = Mathf.Max(1, DisplayedLevelNumber);
            int nextLevelNumber = completedLevelNumber + 1;
            if (saveManager.CurrentLevel < nextLevelNumber)
            {
                saveManager.SetCurrentLevel(nextLevelNumber);
            }
        }

        public void ContinueToCurrentLevel()
        {
            TryContinueToCurrentLevel();
        }

        public bool CanContinueCurrentLevel()
        {
            return EnsureReady();
        }

        public bool TryContinueToCurrentLevel()
        {
            if (!EnsureReady())
            {
                return false;
            }

#if UNITY_EDITOR
            editorLevelOverride = null;
#endif
            DisplayedLevelNumber = Mathf.Max(1, saveManager.CurrentLevel);
            BuildDisplayedLevel(false);
            return CurrentLevelDefinition != null;
        }

        public void RetryCurrentLevel()
        {
#if UNITY_EDITOR
            if (editorLevelOverride != null)
            {
                TryStartEditorLevel(editorLevelOverride);
                return;
            }
#endif
            if (!EnsureReady())
            {
                return;
            }

            DisplayedLevelNumber = Mathf.Max(1, saveManager.CurrentLevel);
            BuildDisplayedLevel(false);
        }

        public bool TryLoadDisplayedLevelForDebug(int oneBasedLevelNumber)
        {
            if (oneBasedLevelNumber < 1 || !EnsureReady())
            {
                return false;
            }

#if UNITY_EDITOR
            editorLevelOverride = null;
#endif
            DisplayedLevelNumber = oneBasedLevelNumber;
            BuildDisplayedLevel(true);
            return CurrentLevelDefinition != null;
        }

        public void MarkPlayerMoveCommitted()
        {
            if (IsPlaying)
            {
                HasCommittedPlayerMove = true;
            }
        }

        public bool TryPauseGameplay()
        {
            if (GameplayState != GameplaySessionState.Playing)
            {
                return false;
            }

            SetGameplayState(GameplaySessionState.Paused);
            return true;
        }

        public bool TryResumeGameplay()
        {
            if (GameplayState != GameplaySessionState.Paused)
            {
                return false;
            }

            SetGameplayState(GameplaySessionState.Playing);
            return true;
        }

        public bool TryEnterWonState()
        {
            return TryEnterTerminalState(GameplaySessionState.Won);
        }

        public bool TryEnterFailedState()
        {
            return TryEnterTerminalState(GameplaySessionState.Failed);
        }

        public bool TryEnterRecoveryState()
        {
            if (GameplayState != GameplaySessionState.Playing)
            {
                return false;
            }

            SetGameplayState(GameplaySessionState.Recovering);
            return true;
        }

        public bool TryResumeFromRecovery()
        {
            if (GameplayState != GameplaySessionState.Recovering)
            {
                return false;
            }

            SetGameplayState(GameplaySessionState.Playing);
            return true;
        }

        public bool TryFinalizeRecoveryAsFailed()
        {
            if (GameplayState != GameplaySessionState.Recovering)
            {
                return false;
            }

            SetGameplayState(GameplaySessionState.Failed);
            return true;
        }

        public bool TryBeginExit()
        {
            if (GameplayState == GameplaySessionState.Exiting)
            {
                return false;
            }

            SetGameplayState(GameplaySessionState.Exiting);

            if (levelBuildController == null)
            {
                levelBuildController = FindFirstObjectByType<LevelBuildController>();
            }

            if (levelBuildController != null)
            {
                levelBuildController.ClearLevel();
            }
            else
            {
#if UNITY_EDITOR
                Gameplay.MarbleDebug.MarbleDebugTracker.EndAttempt("Fallback board exit");
#endif
                sourceBoxBoardController?.ClearBoard();
                Debug.LogWarning(
                    $"{nameof(LevelSessionController)} on '{name}' could only clear the SourceBox board while exiting because no {nameof(LevelBuildController)} is available.",
                    this);
            }

            AudioManager.Instance?.StopAllSfx();
            ReleaseTimeScaleSuspension();
            return true;
        }

        public bool TrySpendLifeForCurrentAttempt()
        {
            if (HasSpentLifeForCurrentAttempt)
            {
                return false;
            }

            Game.Shared.Lives.LivesService livesService =
                SharedSystemsBootstrap.Instance?.LivesService;
            if (livesService == null)
            {
                Debug.LogWarning(
                    $"{nameof(LevelSessionController)} on '{name}' cannot spend a life because {nameof(Game.Shared.Lives.LivesService)} is unavailable.",
                    this);
                return false;
            }

            if (!livesService.TrySpendLife())
            {
                return false;
            }

            HasSpentLifeForCurrentAttempt = true;
            return true;
        }

        private bool EnsureReady()
        {
            if (levelCatalog == null)
            {
                Debug.LogError($"{nameof(LevelSessionController)} on '{name}' is missing {nameof(LevelCatalog)} reference.", this);
                return false;
            }

            if (levelBuildController == null)
            {
                levelBuildController = FindFirstObjectByType<LevelBuildController>();
            }

            if (sourceBoxBoardController == null && levelBuildController == null)
            {
                sourceBoxBoardController = FindFirstObjectByType<SourceBoxBoardController>();
            }

            if (sourceBoxBoardController == null && levelBuildController == null)
            {
                Debug.LogError($"{nameof(LevelSessionController)} on '{name}' needs either {nameof(LevelBuildController)} or {nameof(SourceBoxBoardController)} reference.", this);
                return false;
            }

            if (saveManager == null)
            {
                saveManager = SaveManager.Instance != null
                    ? SaveManager.Instance
                    : FindFirstObjectByType<SaveManager>();
            }

            if (saveManager == null)
            {
                Debug.LogError($"{nameof(LevelSessionController)} on '{name}' cannot start because no {nameof(SaveManager)} is available.", this);
                return false;
            }

            if (!saveManager.IsInitialized)
            {
                saveManager.Initialize();
            }

            progressController = progressController ?? new LevelProgressController(levelCatalog);

            if (levelAnalyticsTracker == null)
            {
                levelAnalyticsTracker = LevelAnalyticsTracker.Instance != null
                    ? LevelAnalyticsTracker.Instance
                    : FindFirstObjectByType<LevelAnalyticsTracker>();
            }

            if (levelOneTapTutorialController == null)
            {
                levelOneTapTutorialController = FindFirstObjectByType<LevelOneTapTutorialController>();
            }

            if (boosterUnlockTutorialController == null)
            {
                boosterUnlockTutorialController = FindFirstObjectByType<BoosterUnlockTutorialController>(FindObjectsInactive.Include);
            }

            return true;
        }

        private void BuildDisplayedLevel(bool suppressAnalytics)
        {
            CurrentLevelDefinition = progressController.ResolveLevel(DisplayedLevelNumber);
            if (CurrentLevelDefinition == null)
            {
                Debug.LogError($"{nameof(LevelSessionController)} on '{name}' could not resolve displayed level {DisplayedLevelNumber}.", this);
                return;
            }

            BuildCurrentLevel(suppressAnalytics);
        }

        private void BuildCurrentLevel(bool suppressAnalytics)
        {
            SetGameplayState(GameplaySessionState.Playing);
            HasCommittedPlayerMove = false;
            HasSpentLifeForCurrentAttempt = false;

            if (levelBuildController != null)
            {
                levelBuildController.BuildLevel(CurrentLevelDefinition);
            }
            else
            {
#if UNITY_EDITOR
                Gameplay.MarbleDebug.MarbleDebugTracker.EndAttempt("Fallback board rebuild");
                Gameplay.MarbleDebug.MarbleDebugTracker.BeginAttempt(CurrentLevelDefinition, this);
#endif
                sourceBoxBoardController.BuildBoard(CurrentLevelDefinition);
#if UNITY_EDITOR
                Gameplay.MarbleDebug.MarbleDebugTracker.CompleteBuild();
#endif
                AudioManager.Instance?.PlaySfx(AudioKey.GameStart);
            }

            DisplayedLevelNumberChanged?.Invoke(DisplayedLevelNumber);
            levelOneTapTutorialController?.TryStartTutorial(DisplayedLevelNumber);
            boosterUnlockTutorialController?.TryStartTutorial(DisplayedLevelNumber, CurrentLevelDefinition);
            levelAnalyticsTracker?.BeginLevel(
                DisplayedLevelNumber,
                CurrentLevelDefinition.LevelNumber,
                suppressAnalytics);
        }

        private bool TryEnterTerminalState(GameplaySessionState terminalState)
        {
            if (GameplayState != GameplaySessionState.Playing ||
                terminalState != GameplaySessionState.Won && terminalState != GameplaySessionState.Failed)
            {
                return false;
            }

            SetGameplayState(terminalState);
            return true;
        }

        private void SetGameplayState(GameplaySessionState nextState)
        {
            if (GameplayState == nextState)
            {
                if (nextState == GameplaySessionState.Playing)
                {
                    ReleaseTimeScaleSuspension();
                }

                return;
            }

            if (nextState == GameplaySessionState.Paused)
            {
                AcquireTimeScaleSuspension();
            }
            else if (nextState == GameplaySessionState.Playing)
            {
                ReleaseTimeScaleSuspension();
            }

            GameplayState = nextState;
#if UNITY_EDITOR
            Gameplay.MarbleDebug.MarbleDebugTracker.SessionStateChanged(nextState);
#endif
            GameplayStateChanged?.Invoke(GameplayState);
        }

        private void AcquireTimeScaleSuspension()
        {
            if (!ownsTimeScaleSuspension)
            {
                timeScaleBeforeSuspension = Time.timeScale > 0f ? Time.timeScale : 1f;
                ownsTimeScaleSuspension = true;
            }

            Time.timeScale = 0f;
        }

        private void ReleaseTimeScaleSuspension()
        {
            if (!ownsTimeScaleSuspension)
            {
                return;
            }

            Time.timeScale = Mathf.Max(0.01f, timeScaleBeforeSuspension);
            ownsTimeScaleSuspension = false;
        }
    }
}

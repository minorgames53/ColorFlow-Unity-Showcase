using Game.Shared.Bootstrap;
using Game.Shared.Navigation;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using Game.Shared.UI.Settings;
using Gameplay.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay.UI.HUD
{
    [DisallowMultipleComponent]
    public sealed class GameplayGiveUpPanelController : MonoBehaviour
    {
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private SettingsPanelController settingsPanelController;
        [SerializeField] private UIPanel giveUpPanel;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private CoinFlyAnimator coinFlyAnimator;

        private bool isNavigating;

        private void OnEnable()
        {
            RegisterListeners();
        }

        private void OnDisable()
        {
            UnregisterListeners();
            isNavigating = false;
        }

        private void RegisterListeners()
        {
            if (settingsPanelController != null)
            {
                settingsPanelController.MainMenuRequested -= HandleMainMenuRequested;
                settingsPanelController.MainMenuRequested += HandleMainMenuRequested;
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmClicked);
                confirmButton.onClick.AddListener(HandleConfirmClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleCloseClicked);
                closeButton.onClick.AddListener(HandleCloseClicked);
            }
        }

        private void UnregisterListeners()
        {
            if (settingsPanelController != null)
            {
                settingsPanelController.MainMenuRequested -= HandleMainMenuRequested;
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleCloseClicked);
            }
        }

        private void HandleMainMenuRequested()
        {
            if (panelManager != null && panelManager.TryDeferOpen(giveUpPanel, HandleMainMenuRequested)) return;

            if (isNavigating)
            {
                return;
            }

            if (levelSessionController == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameplayGiveUpPanelController)} cannot evaluate the current attempt because no {nameof(LevelSessionController)} is assigned.",
                    this);
                return;
            }

            Game.Shared.Lives.LivesService livesService =
                SharedSystemsBootstrap.Instance?.LivesService;
            if (!levelSessionController.HasCommittedPlayerMove ||
                (livesService != null && (!livesService.IsEnabled || livesService.HasInfiniteLives)))
            {
                LoadMenu();
                return;
            }

            if (panelManager == null || giveUpPanel == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameplayGiveUpPanelController)} cannot open GiveUp because its panel references are incomplete.",
                    this);
                return;
            }

            if (panelManager.HasOpenPanel)
            {
                panelManager.Push(giveUpPanel);
            }
            else
            {
                panelManager.OpenRoot(giveUpPanel);
            }
        }

        private void HandleConfirmClicked()
        {
            if (isNavigating)
            {
                return;
            }

            levelSessionController?.TrySpendLifeForCurrentAttempt();
            LoadMenu();
        }

        private void HandleCloseClicked()
        {
            if (isNavigating)
            {
                return;
            }

            if (panelManager == null || giveUpPanel == null ||
                !panelManager.TryClose(giveUpPanel))
            {
                Debug.LogWarning(
                    $"{nameof(GameplayGiveUpPanelController)} cannot close GiveUp because it is not registered with {nameof(PanelManager)}.",
                    this);
            }
        }

        private void LoadMenu()
        {
            SceneLoader sceneLoader = SceneLoader.Instance;
            if (sceneLoader == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameplayGiveUpPanelController)} cannot load Menu because {nameof(SceneLoader)} is unavailable.",
                    this);
                return;
            }

            if (sceneLoader.State != SceneTransitionState.Idle)
            {
                return;
            }

            if (levelSessionController == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameplayGiveUpPanelController)} cannot exit gameplay because no {nameof(LevelSessionController)} is assigned.",
                    this);
                return;
            }

            if (levelSessionController.GameplayState != GameplaySessionState.Exiting &&
                !levelSessionController.TryBeginExit())
            {
                return;
            }

            isNavigating = true;
            coinFlyAnimator?.CancelActiveAnimation();
            sceneLoader.LoadMenu();
        }
    }
}

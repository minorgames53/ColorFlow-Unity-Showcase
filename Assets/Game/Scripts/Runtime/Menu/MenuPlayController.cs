using Game.Shared.Bootstrap;
using Game.Shared.Navigation;
using Game.Shared.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuPlayController : MonoBehaviour
    {
        [SerializeField] private Button playButton;
        [SerializeField] private Button currentLevelButton;
        [SerializeField] private MenuLivesRefillHost livesRefillHost;
        [SerializeField] private CoinFlyAnimator coinFlyAnimator;

        private bool isLoadingGame;

        private void OnEnable()
        {
            BindButtons();
        }

        private void OnDisable()
        {
            UnbindButtons();
        }

        public void OpenCurrentLevel()
        {
            if (isLoadingGame)
            {
                return;
            }

            Game.Shared.Lives.LivesService livesService = SharedSystemsBootstrap.Instance?.LivesService;
            if (livesService != null && !livesService.HasLives)
            {
                if (livesRefillHost == null)
                {
                    Debug.LogWarning(
                        $"{nameof(MenuPlayController)} cannot open Life Panel because no {nameof(MenuLivesRefillHost)} is assigned.",
                        this);
                }
                else
                {
                    livesRefillHost.Open();
                }

                return;
            }

            SceneLoader sceneLoader = SceneLoader.Instance;
            if (sceneLoader == null)
            {
                Debug.LogWarning(
                    $"{nameof(MenuPlayController)} cannot open the current level because {nameof(SceneLoader)} is not initialized.",
                    this);
                return;
            }

            if (sceneLoader.State != SceneTransitionState.Idle)
            {
                return;
            }

            isLoadingGame = true;
            coinFlyAnimator?.CancelActiveAnimation();
            sceneLoader.LoadGame();
        }

        private void BindButtons()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveListener(OpenCurrentLevel);
                playButton.onClick.AddListener(OpenCurrentLevel);
            }

            if (currentLevelButton != null)
            {
                currentLevelButton.onClick.RemoveListener(OpenCurrentLevel);
                currentLevelButton.onClick.AddListener(OpenCurrentLevel);
            }
        }

        private void UnbindButtons()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveListener(OpenCurrentLevel);
            }

            if (currentLevelButton != null)
            {
                currentLevelButton.onClick.RemoveListener(OpenCurrentLevel);
            }
        }
    }
}

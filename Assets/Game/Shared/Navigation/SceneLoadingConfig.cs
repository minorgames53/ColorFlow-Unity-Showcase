using UnityEngine;

namespace Game.Shared.Navigation
{
    [CreateAssetMenu(
        fileName = "SceneLoadingConfig",
        menuName = "Game/Shared/Scene Loading Config")]
    public sealed class SceneLoadingConfig : ScriptableObject
    {
        [SerializeField] private string mainMenuSceneName = "Menu";
        [SerializeField] private string gamePlaySceneName = "Game";
        [SerializeField, Min(0f)] private float minimumVisibleDuration = 3f;
        [SerializeField, Min(0f)] private float bootMinimumVisibleDuration = 6f;
        [SerializeField, Min(0f)] private float readyTimeoutSeconds = 10f;

        [Header("Bootstrap Services")]
        [SerializeField, Tooltip("Lives policy supplied to the shared bootstrap without additional scene wiring.")]
        private Game.Shared.Lives.LivesConfig livesConfig;

        public string MainMenuSceneName => mainMenuSceneName;
        public string GamePlaySceneName => gamePlaySceneName;
        public float MinimumVisibleDuration => minimumVisibleDuration;
        public float BootMinimumVisibleDuration => bootMinimumVisibleDuration;
        public float ReadyTimeoutSeconds => readyTimeoutSeconds;
        public Game.Shared.Lives.LivesConfig LivesConfig => livesConfig;

        public string GetSceneName(SceneId sceneId)
        {
            switch (sceneId)
            {
                case SceneId.Menu:
                    return mainMenuSceneName;
                case SceneId.Game:
                    return gamePlaySceneName;
                default:
                    return string.Empty;
            }
        }
    }
}

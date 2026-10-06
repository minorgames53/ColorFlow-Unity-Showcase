using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.Navigation
{
    [RequireComponent(typeof(Button))]
    public sealed class SceneLoadButton : MonoBehaviour
    {
        [SerializeField] private SceneId targetScene = SceneId.Game;

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            button.onClick.AddListener(LoadTargetScene);
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(LoadTargetScene);
            }
        }

        public void LoadTargetScene()
        {
            if (SceneLoader.Instance == null)
            {
                Debug.LogWarning($"{nameof(SceneLoadButton)} cannot load {targetScene} because {nameof(SceneLoader)} is not initialized.", this);
                return;
            }

            switch (targetScene)
            {
                case SceneId.Menu:
                    SceneLoader.Instance.LoadMenu();
                    break;
                case SceneId.Game:
                    SceneLoader.Instance.LoadGame();
                    break;
            }
        }
    }
}

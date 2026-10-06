using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Shared.UI.Panels;

namespace Game.Shared.Developer.LevelTesting
{
    [DisallowMultipleComponent]
    public sealed class DevLevelTestPanel : MonoBehaviour
    {
        [Header("Controls")]
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_InputField levelNumberInput;
        [SerializeField] private Button playButton;
        [SerializeField] private Button winButton;
        [SerializeField] private Button loseButton;
        [SerializeField] private UIPanel uiPanel;

        private IDevLevelTestAdapter adapter;
        private bool listenersRegistered;
        private bool isProcessingAction;

        public UIPanel UIPanel => uiPanel;

        private void Reset()
        {
            uiPanel = GetComponent<UIPanel>();
        }

        private void OnEnable()
        {
            RegisterListeners();
            Refresh();
        }

        private void OnDisable()
        {
            UnregisterListeners();
        }

        private void OnDestroy()
        {
            UnregisterListeners();
            if (adapter != null)
            {
                adapter.LevelChanged -= HandleLevelChanged;
            }
        }

        public void Initialize(IDevLevelTestAdapter newAdapter)
        {
            if (adapter == newAdapter)
            {
                Refresh();
                return;
            }

            if (adapter != null)
            {
                adapter.LevelChanged -= HandleLevelChanged;
            }

            adapter = newAdapter;

            if (adapter != null)
            {
                adapter.LevelChanged += HandleLevelChanged;
            }

            RegisterListeners();
            Refresh();
        }

        public void Refresh()
        {
            int currentLevel = adapter != null ? Mathf.Max(1, adapter.CurrentLevelNumber) : 1;
 
            if (levelNumberInput != null)
            {
                levelNumberInput.SetTextWithoutNotify(currentLevel.ToString());
            }

            if (previousButton != null)
            {
                previousButton.interactable = adapter != null && !isProcessingAction && currentLevel > 1;
            }

            if (nextButton != null)
            {
                nextButton.interactable = adapter != null && !isProcessingAction;
            }

            if (playButton != null)
            {
                playButton.interactable = adapter != null && !isProcessingAction;
            }

            if (winButton != null)
            {
                winButton.interactable = adapter != null && !isProcessingAction;
            }

            if (loseButton != null)
            {
                loseButton.interactable = adapter != null && !isProcessingAction;
            }
        }

        private void RegisterListeners()
        {
            if (listenersRegistered)
            {
                return;
            }

            previousButton?.onClick.AddListener(HandlePreviousClicked);
            nextButton?.onClick.AddListener(HandleNextClicked);
            playButton?.onClick.AddListener(HandlePlayClicked);
            winButton?.onClick.AddListener(HandleWinClicked);
            loseButton?.onClick.AddListener(HandleLoseClicked);
            listenersRegistered = true;
        }

        private void UnregisterListeners()
        {
            if (!listenersRegistered)
            {
                return;
            }

            previousButton?.onClick.RemoveListener(HandlePreviousClicked);
            nextButton?.onClick.RemoveListener(HandleNextClicked);
            playButton?.onClick.RemoveListener(HandlePlayClicked);
            winButton?.onClick.RemoveListener(HandleWinClicked);
            loseButton?.onClick.RemoveListener(HandleLoseClicked);
            listenersRegistered = false;
        }

        private void HandlePreviousClicked()
        {
            if (adapter == null || isProcessingAction)
            {
                return;
            }

            int targetLevel = Mathf.Max(1, adapter.CurrentLevelNumber) - 1;
            if (targetLevel < 1)
            {
                Refresh();
                return;
            }

            RunAction(() => adapter.TryLoadLevel(targetLevel));
        }

        private void HandleNextClicked()
        {
            if (adapter == null || isProcessingAction)
            {
                return;
            }

            RunAction(() => adapter.TryLoadLevel(Mathf.Max(1, adapter.CurrentLevelNumber) + 1));
        }

        private void HandlePlayClicked()
        {
            if (adapter == null || isProcessingAction || levelNumberInput == null)
            {
                return;
            }

            string rawValue = levelNumberInput.text;
            if (!int.TryParse(rawValue?.Trim(), out int targetLevel) || targetLevel < 1)
            {
                Refresh();
                return;
            }

            RunAction(() => adapter.TryLoadLevel(targetLevel));
        }

        private void HandleWinClicked()
        {
            if (adapter == null || isProcessingAction)
            {
                return;
            }

            RunAction(adapter.TryTriggerWin);
        }

        private void HandleLoseClicked()
        {
            if (adapter == null || isProcessingAction)
            {
                return;
            }

            RunAction(adapter.TryTriggerLose);
        }

        private void RunAction(System.Func<bool> action)
        {
            isProcessingAction = true;
            Refresh();

            try
            {
                action?.Invoke();
            }
            finally
            {
                isProcessingAction = false;
                Refresh();
            }
        }

        private void HandleLevelChanged(int levelNumber)
        {
            Refresh();
        }
    }
}

using Game.Shared.Analytics;
using Game.Shared.Save;
using Gameplay.Levels;
using UnityEngine;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuLevelProgressController : MonoBehaviour
    {
        [SerializeField] private LevelCatalog levelCatalog;
        [SerializeField] private MenuLevelNodeView[] nodes;

        [Header("Difficulty Sprites")]
        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite hardSprite;
        [SerializeField] private Sprite veryHardSprite;
        [SerializeField] private Sprite hardLabelSprite;
        [SerializeField] private Sprite veryHardLabelSprite;

        private SaveManager saveManager;
        private bool isSubscribed;

        private void OnEnable()
        {
            TryBindSaveManager();
        }

        private void Update()
        {
            if (!isSubscribed)
            {
                TryBindSaveManager();
            }
        }

        private void OnDisable()
        {
            ClearDefaultLevelContext("menu_progress_disabled");
            UnbindSaveManager();
        }

        private void OnDestroy()
        {
            ClearDefaultLevelContext("menu_progress_destroyed");
        }

        public void Refresh()
        {
            if (saveManager == null || !saveManager.IsInitialized)
            {
                return;
            }

            PublishDefaultLevelContext("menu_progress_refresh");
            Populate(saveManager.CurrentLevel);
        }

        public bool TryGetCurrentLevelContext(
            out int displayedLevelNumber,
            out int internalLevelNumber)
        {
            displayedLevelNumber = 0;
            internalLevelNumber = 0;
            if (saveManager == null || !saveManager.IsInitialized)
            {
                TryBindSaveManager();
            }

            if (saveManager == null || !saveManager.IsInitialized || levelCatalog == null)
            {
                return false;
            }

            displayedLevelNumber = Mathf.Max(1, saveManager.CurrentLevel);
            LevelDefinition definition =
                LevelProgressController.ResolveLevel(levelCatalog, displayedLevelNumber);
            if (definition == null)
            {
                displayedLevelNumber = 0;
                return false;
            }

            internalLevelNumber = definition.LevelNumber;
            return internalLevelNumber > 0;
        }

        private void TryBindSaveManager()
        {
            SaveManager availableSaveManager = SaveManager.Instance;
            if (availableSaveManager == null || !availableSaveManager.IsInitialized)
            {
                return;
            }

            if (saveManager != availableSaveManager)
            {
                UnbindSaveManager();
                saveManager = availableSaveManager;
            }

            if (!isSubscribed)
            {
                saveManager.OnSaveLoaded += HandleSaveLoaded;
                saveManager.CurrentLevelChanged += HandleCurrentLevelChanged;
                isSubscribed = true;
            }

            Refresh();
        }

        private void UnbindSaveManager()
        {
            if (saveManager != null && isSubscribed)
            {
                saveManager.OnSaveLoaded -= HandleSaveLoaded;
                saveManager.CurrentLevelChanged -= HandleCurrentLevelChanged;
            }

            isSubscribed = false;
            saveManager = null;
        }

        private void HandleCurrentLevelChanged(int currentLevel)
        {
            PublishDefaultLevelContext("menu_progression_changed");
            Populate(currentLevel);
        }

        private void HandleSaveLoaded()
        {
            Refresh();
        }

        private void PublishDefaultLevelContext(string source)
        {
            if (TryGetCurrentLevelContext(
                    out int displayedLevelNumber,
                    out int internalLevelNumber))
            {
                AnalyticsBootstrap.Instance?.SetDefaultLevelContext(
                    displayedLevelNumber,
                    internalLevelNumber,
                    this,
                    source);
                return;
            }

            ClearDefaultLevelContext($"{source}_unavailable");
        }

        private void ClearDefaultLevelContext(string source)
        {
            AnalyticsBootstrap.Instance?.ClearDefaultLevelContext(this, source);
        }

        private void Populate(int currentLevel)
        {
            if (nodes == null || levelCatalog == null)
            {
                return;
            }

            int normalizedCurrentLevel = Mathf.Max(1, currentLevel);
            int lastNodeIndex = nodes.Length - 1;

            for (int i = 0; i < nodes.Length; i++)
            {
                MenuLevelNodeView node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                int displayedLevelNumber = normalizedCurrentLevel + lastNodeIndex - i;
                LevelDefinition definition =
                    LevelProgressController.ResolveLevel(levelCatalog, displayedLevelNumber);

                if (definition == null)
                {
                    node.Hide();
                    continue;
                }

                node.SetData(
                    displayedLevelNumber,
                    definition.Difficulty,
                    i == lastNodeIndex,
                    normalSprite,
                    hardSprite,
                    veryHardSprite,
                    hardLabelSprite,
                    veryHardLabelSprite);
            }
        }
    }
}

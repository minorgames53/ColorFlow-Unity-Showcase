using Gameplay.Levels;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuLevelNodeView : MonoBehaviour
    {
        [SerializeField] private Image difficultyImage;
        [SerializeField] private Image labelImage;
        [SerializeField] private TMP_Text levelNumberText;
        [SerializeField] private TMP_Text difficultyText;
        [SerializeField] private Button button;

        [Header("Localization")]
        [SerializeField] private LocalizedString hardDifficultyLabel;
        [SerializeField] private LocalizedString veryHardDifficultyLabel;

        private LevelDifficulty currentDifficulty;
        private string localizedHardLabel = string.Empty;
        private string localizedVeryHardLabel = string.Empty;
        private bool hardLabelSubscribed;
        private bool veryHardLabelSubscribed;

        public Button Button => button;

        private void OnEnable()
        {
            BindLocalization();
        }

        private void OnDisable()
        {
            UnbindLocalization();
        }

        public void SetData(
            int displayedLevelNumber,
            LevelDifficulty difficulty,
            bool isCurrentLevel,
            Sprite normalSprite,
            Sprite hardSprite,
            Sprite veryHardSprite,
            Sprite hardLabelSprite,
            Sprite veryHardLabelSprite)
        {
            currentDifficulty = difficulty;
            gameObject.SetActive(true);

            if (levelNumberText != null)
            {
                levelNumberText.SetText("{0}", Mathf.Max(1, displayedLevelNumber));
            }

            if (button != null)
            {
                button.interactable = isCurrentLevel;
            }

            switch (difficulty)
            {
                case LevelDifficulty.Hard:
                    ApplyDifficulty(hardSprite, hardLabelSprite, localizedHardLabel);
                    break;

                case LevelDifficulty.VeryHard:
                    ApplyDifficulty(veryHardSprite, veryHardLabelSprite, localizedVeryHardLabel);
                    break;

                default:
                    ApplyNormal(normalSprite);
                    break;
            }
        }

        private void BindLocalization()
        {
            if (!hardLabelSubscribed && hardDifficultyLabel != null && !hardDifficultyLabel.IsEmpty)
            {
                hardDifficultyLabel.StringChanged += HandleHardLabelChanged;
                hardLabelSubscribed = true;
            }

            if (!veryHardLabelSubscribed && veryHardDifficultyLabel != null && !veryHardDifficultyLabel.IsEmpty)
            {
                veryHardDifficultyLabel.StringChanged += HandleVeryHardLabelChanged;
                veryHardLabelSubscribed = true;
            }
        }

        private void UnbindLocalization()
        {
            if (hardLabelSubscribed)
            {
                hardDifficultyLabel.StringChanged -= HandleHardLabelChanged;
                hardLabelSubscribed = false;
            }

            if (veryHardLabelSubscribed)
            {
                veryHardDifficultyLabel.StringChanged -= HandleVeryHardLabelChanged;
                veryHardLabelSubscribed = false;
            }
        }

        private void HandleHardLabelChanged(string localizedText)
        {
            localizedHardLabel = localizedText;

            if (currentDifficulty == LevelDifficulty.Hard && difficultyText != null)
            {
                difficultyText.text = localizedHardLabel;
            }
        }

        private void HandleVeryHardLabelChanged(string localizedText)
        {
            localizedVeryHardLabel = localizedText;

            if (currentDifficulty == LevelDifficulty.VeryHard && difficultyText != null)
            {
                difficultyText.text = localizedVeryHardLabel;
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void ApplyNormal(Sprite normalSprite)
        {
            if (difficultyImage != null)
            {
                difficultyImage.sprite = normalSprite;
            }

            if (labelImage != null)
            {
                labelImage.gameObject.SetActive(false);
            }

            if (difficultyText != null)
            {
                difficultyText.text = string.Empty;
            }
        }

        private void ApplyDifficulty(Sprite backgroundSprite, Sprite labelSprite, string label)
        {
            if (difficultyImage != null)
            {
                difficultyImage.sprite = backgroundSprite;
            }

            if (labelImage != null)
            {
                labelImage.sprite = labelSprite;
                labelImage.gameObject.SetActive(true);
            }

            if (difficultyText != null)
            {
                difficultyText.text = label;
            }
        }
    }
}

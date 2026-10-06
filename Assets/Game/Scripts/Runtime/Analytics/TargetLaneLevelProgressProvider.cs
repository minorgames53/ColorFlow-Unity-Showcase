using Gameplay.Levels;
using Gameplay.TargetBoxes;
using UnityEngine;

namespace Gameplay.Analytics
{
    [DisallowMultipleComponent]
    public sealed class TargetLaneLevelProgressProvider : MonoBehaviour, ILevelProgressProvider
    {
        [SerializeField] private TargetLaneController targetLaneController;

        private void Awake()
        {
            CacheReferences();
        }

        public int GetProgressPercent()
        {
            CacheReferences();
            if (targetLaneController == null || targetLaneController.SpawnedTargetBoxes == null)
            {
                return 0;
            }

            int totalRequired = targetLaneController.SpawnedTargetBoxes.Count * LevelDefinition.TargetBoxCapacity;
            if (totalRequired <= 0)
            {
                return 0;
            }

            int completed = 0;
            for (int i = 0; i < targetLaneController.SpawnedTargetBoxes.Count; i++)
            {
                TargetBox targetBox = targetLaneController.SpawnedTargetBoxes[i];
                if (targetBox != null)
                {
                    completed += Mathf.Clamp(targetBox.ArrivedMarbleCount, 0, LevelDefinition.TargetBoxCapacity);
                }
            }

            return Mathf.Clamp(Mathf.RoundToInt(completed / (float)totalRequired * 100f), 0, 100);
        }

        private void CacheReferences()
        {
            if (targetLaneController == null)
            {
                targetLaneController = FindFirstObjectByType<TargetLaneController>();
            }
        }
    }
}

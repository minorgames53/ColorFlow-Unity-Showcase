using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Levels
{
    [CreateAssetMenu(
        fileName = "LevelCatalog",
        menuName = "Gameplay/Levels/Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [SerializeField] private List<LevelDefinition> levels = new List<LevelDefinition>();
        [SerializeField] private int loopSeed;

        public IReadOnlyList<LevelDefinition> Levels => levels;
        public int LoopSeed => loopSeed;
        public int LevelCount => GetOrderedLevels().Count;

        private void OnValidate()
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            ValidateCatalog(errors, warnings);

            for (int i = 0; i < errors.Count; i++)
            {
                Debug.LogError($"{nameof(LevelCatalog)} '{name}': {errors[i]}", this);
            }

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning($"{nameof(LevelCatalog)} '{name}': {warnings[i]}", this);
            }
        }

        public List<LevelDefinition> GetOrderedLevels()
        {
            List<LevelDefinition> orderedLevels = new List<LevelDefinition>();
            if (levels == null)
            {
                return orderedLevels;
            }

            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null)
                {
                    orderedLevels.Add(levels[i]);
                }
            }

            orderedLevels.Sort(CompareLevels);
            return orderedLevels;
        }

        public List<LevelDefinition> GetLoopLevels()
        {
            List<LevelDefinition> orderedLevels = GetOrderedLevels();
            List<LevelDefinition> loopLevels = new List<LevelDefinition>();
            for (int i = 0; i < orderedLevels.Count; i++)
            {
                LevelDefinition level = orderedLevels[i];
                if (level != null && level.IncludeInLoop)
                {
                    loopLevels.Add(level);
                }
            }

            return loopLevels;
        }

        public bool ValidateCatalog(List<string> errors, List<string> warnings)
        {
            if (errors == null || warnings == null)
            {
                return false;
            }

            bool isValid = true;
            if (levels == null)
            {
                errors.Add("Level list is null.");
                return false;
            }

            Dictionary<int, LevelDefinition> levelsByNumber = new Dictionary<int, LevelDefinition>();
            for (int i = 0; i < levels.Count; i++)
            {
                LevelDefinition level = levels[i];
                if (level == null)
                {
                    errors.Add($"Level list entry {i} is null.");
                    isValid = false;
                    continue;
                }

                int levelNumber = level.LevelNumber;
                if (levelsByNumber.TryGetValue(levelNumber, out LevelDefinition duplicate))
                {
                    errors.Add($"Duplicate level number {levelNumber} on '{duplicate.name}' and '{level.name}'.");
                    isValid = false;
                    continue;
                }

                levelsByNumber.Add(levelNumber, level);
            }

            List<LevelDefinition> orderedLevels = GetOrderedLevels();
            for (int i = 0; i < orderedLevels.Count; i++)
            {
                int expectedLevelNumber = i + 1;
                if (orderedLevels[i].LevelNumber != expectedLevelNumber)
                {
                    errors.Add($"Level numbers must be contiguous from 1..N. Expected {expectedLevelNumber} but found {orderedLevels[i].LevelNumber} on '{orderedLevels[i].name}'.");
                    isValid = false;
                    break;
                }
            }

            int loopLevelCount = 0;
            for (int i = 0; i < orderedLevels.Count; i++)
            {
                if (orderedLevels[i].IncludeInLoop)
                {
                    loopLevelCount++;
                }
            }

            if (loopLevelCount < 2)
            {
                warnings.Add($"At least two levels should have Include In Loop enabled. Current loop level count: {loopLevelCount}.");
            }

            return isValid;
        }

        private static int CompareLevels(LevelDefinition left, LevelDefinition right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int numberCompare = left.LevelNumber.CompareTo(right.LevelNumber);
            return numberCompare != 0
                ? numberCompare
                : string.Compare(left.name, right.name, System.StringComparison.Ordinal);
        }
    }
}

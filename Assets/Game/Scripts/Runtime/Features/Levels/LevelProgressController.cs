using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Levels
{
    public sealed class LevelProgressController
    {
        private readonly LevelCatalog catalog;

        public LevelProgressController(LevelCatalog catalog)
        {
            this.catalog = catalog;
        }

        public LevelDefinition ResolveLevel(int displayedLevelNumber)
        {
            return ResolveLevel(catalog, displayedLevelNumber);
        }

        public static LevelDefinition ResolveLevel(LevelCatalog catalog, int displayedLevelNumber)
        {
            if (catalog == null)
            {
                Debug.LogError($"{nameof(LevelProgressController)} cannot resolve level because {nameof(LevelCatalog)} is null.");
                return null;
            }

            int normalizedDisplayedLevel = Mathf.Max(1, displayedLevelNumber);
            List<LevelDefinition> orderedLevels = catalog.GetOrderedLevels();
            if (orderedLevels.Count == 0)
            {
                Debug.LogError($"{nameof(LevelProgressController)} cannot resolve level because catalog '{catalog.name}' has no valid levels.");
                return null;
            }

            if (normalizedDisplayedLevel <= orderedLevels.Count)
            {
                return orderedLevels[normalizedDisplayedLevel - 1];
            }

            List<LevelDefinition> loopLevels = catalog.GetLoopLevels();
            if (loopLevels.Count == 0)
            {
                Debug.LogWarning($"{nameof(LevelProgressController)} catalog '{catalog.name}' has no loop levels. Falling back to the final ordered level.");
                return orderedLevels[orderedLevels.Count - 1];
            }

            if (loopLevels.Count == 1)
            {
                Debug.LogWarning($"{nameof(LevelProgressController)} catalog '{catalog.name}' has only one loop level. Consecutive repeats cannot be avoided.");
                return loopLevels[0];
            }

            int loopIndex = normalizedDisplayedLevel - orderedLevels.Count - 1;
            int cycleIndex = loopIndex / loopLevels.Count;
            int indexInCycle = loopIndex % loopLevels.Count;
            int previousLastLevelNumber = orderedLevels[orderedLevels.Count - 1].LevelNumber;
            List<LevelDefinition> cycle = null;

            for (int i = 0; i <= cycleIndex; i++)
            {
                cycle = CreateCycle(loopLevels, catalog.LoopSeed, i, previousLastLevelNumber);
                previousLastLevelNumber = cycle[cycle.Count - 1].LevelNumber;
            }

            return cycle[indexInCycle];
        }

        private static List<LevelDefinition> CreateCycle(
            IReadOnlyList<LevelDefinition> loopLevels,
            int loopSeed,
            int cycleIndex,
            int forbiddenFirstLevelNumber)
        {
            List<LevelDefinition> cycle = new List<LevelDefinition>(loopLevels);
            System.Random random = new System.Random(loopSeed + cycleIndex);

            for (int i = cycle.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                LevelDefinition temporary = cycle[i];
                cycle[i] = cycle[swapIndex];
                cycle[swapIndex] = temporary;
            }

            if (cycle.Count > 1 && cycle[0].LevelNumber == forbiddenFirstLevelNumber)
            {
                int swapIndex = FindFirstDifferentLevelIndex(cycle, forbiddenFirstLevelNumber);
                if (swapIndex > 0)
                {
                    LevelDefinition temporary = cycle[0];
                    cycle[0] = cycle[swapIndex];
                    cycle[swapIndex] = temporary;
                }
            }

            return cycle;
        }

        private static int FindFirstDifferentLevelIndex(IReadOnlyList<LevelDefinition> levels, int levelNumber)
        {
            for (int i = 1; i < levels.Count; i++)
            {
                if (levels[i] != null && levels[i].LevelNumber != levelNumber)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Game.Shared.Save
{
    [Serializable]
    public sealed class LevelSaveData
    {
        public int currentLevel = GameSaveDataFactory.InitialCurrentLevel;

        public bool hasPendingNextLevel;
        public int pendingNextLevel;
        public int lastLoopContentLevelNumber;

        public List<LevelProgressSaveData> levels = new List<LevelProgressSaveData>();
    }
}

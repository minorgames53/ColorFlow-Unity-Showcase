using System;
using System.Collections.Generic;

namespace Game.Shared.Save
{
    [Serializable]
    public sealed class LevelProgressSaveData
    {
        public int levelNumber;
        public bool completed;

        public List<SaveValueData> values = new List<SaveValueData>();
    }
}

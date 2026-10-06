using System;
using System.Collections.Generic;

namespace Game.Shared.Save
{
    [Serializable]
    public sealed class BoosterSaveData
    {
        public string id = string.Empty;
        public int amount;
        public bool unlocked;

        public List<SaveValueData> values = new List<SaveValueData>();
    }
}

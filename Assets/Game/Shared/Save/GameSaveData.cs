using System;
using System.Collections.Generic;

namespace Game.Shared.Save
{
    [Serializable]
    public sealed class GameSaveData
    {
        public int version = SaveVersion.Current;

        public LevelSaveData level = new LevelSaveData();
        public LivesSaveData lives = new LivesSaveData();
        public GoldSaveData gold = new GoldSaveData();
        public StoreSaveData store = new StoreSaveData();

        public List<BoosterSaveData> boosters = new List<BoosterSaveData>();
        public List<SettingSaveData> settings = new List<SettingSaveData>();
    }
}

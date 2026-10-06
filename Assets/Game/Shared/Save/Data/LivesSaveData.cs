using System;

namespace Game.Shared.Save
{
    [Serializable]
    public sealed class LivesSaveData
    {
        public int current = GameSaveDataFactory.InitialLives;
        public int max = GameSaveDataFactory.InitialMaxLives;
        public long nextRefillUtc;
    }
}

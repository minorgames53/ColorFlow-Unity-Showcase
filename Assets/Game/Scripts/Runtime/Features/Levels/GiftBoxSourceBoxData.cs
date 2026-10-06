using System;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Levels
{
    [Serializable]
    public sealed class GiftBoxSourceBoxData
    {
        [SerializeField] private MarbleColorId colorId = MarbleColorId.Blue;
        [SerializeField, Range(LevelCellData.MinMarbleCount, LevelCellData.MaxMarbleCount)]
        private int marbleCount = LevelCellData.MinMarbleCount;

        public MarbleColorId ColorId => colorId;
        public int MarbleCount => marbleCount;
    }
}

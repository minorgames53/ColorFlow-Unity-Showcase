using System;
using System.Collections.Generic;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Levels
{
    public enum LevelCellType
    {
        Empty = 0,
        SourceBox = 1,
        Spawner = 2,
        GiftBox = 3,
        Blocked = 4
    }

    [Serializable]
    public sealed class LevelCellData
    {
        public const int MinMarbleCount = 1;
        public const int MaxMarbleCount = 9;

        [SerializeField] private LevelCellType cellType = LevelCellType.Empty;
        [SerializeField] private MarbleColorId colorId = MarbleColorId.None;
        [SerializeField, Range(MinMarbleCount, MaxMarbleCount)] private int marbleCount = MinMarbleCount;
        [SerializeField] private bool isMysterySourceBox;
        [SerializeField] private SpawnerDirection spawnDirection = SpawnerDirection.Up;
        [SerializeField] private List<SpawnerSourceBoxData> spawnSequence = new List<SpawnerSourceBoxData>();
        [SerializeField] private List<GiftBoxSourceBoxData> giftBoxContents = new List<GiftBoxSourceBoxData>();
        [SerializeField] private int connectedBoxPairId = -1;
        [SerializeField] private ArrowDirection arrowDirection = ArrowDirection.None;
        [SerializeField] private bool hasKey;

        public LevelCellType CellType => cellType;
        public MarbleColorId ColorId => colorId;
        public int MarbleCount => marbleCount;
        public bool IsMysterySourceBox => isMysterySourceBox;
        public SpawnerDirection SpawnDirection => spawnDirection;
        public int ConnectedBoxPairId => connectedBoxPairId;
        public bool HasConnectedBoxPair => connectedBoxPairId >= 0;
        public ArrowDirection ArrowDirection => arrowDirection;
        public bool HasArrow => arrowDirection != ArrowDirection.None;
        public bool HasKey => hasKey;

        public IReadOnlyList<SpawnerSourceBoxData> SpawnSequence
        {
            get
            {
                EnsureData();
                return spawnSequence;
            }
        }

        public IReadOnlyList<GiftBoxSourceBoxData> GiftBoxContents
        {
            get
            {
                EnsureData();
                return giftBoxContents;
            }
        }

        public void EnsureData()
        {
            if (spawnSequence == null)
            {
                spawnSequence = new List<SpawnerSourceBoxData>();
            }

            if (giftBoxContents == null)
            {
                giftBoxContents = new List<GiftBoxSourceBoxData>();
            }
        }
    }
}

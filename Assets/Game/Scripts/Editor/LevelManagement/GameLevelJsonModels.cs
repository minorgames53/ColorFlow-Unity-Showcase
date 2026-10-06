using System;
using System.Collections.Generic;
using Gameplay.Levels;
using Gameplay.SourceBoxes;

namespace Gameplay.Editor.LevelManagement
{
    [Serializable]
    public sealed class GameLevelBatchJsonDto
    {
        public int schemaVersion = 1;
        public List<GameLevelJsonDto> levels = new List<GameLevelJsonDto>();
    }

    [Serializable]
    public sealed class GameLevelJsonDto
    {
        public int version = GameLevelJsonConstants.CurrentVersion;
        public string levelId;
        public int levelNumber;
        public string difficulty;
        public string levelType;
        public int rowCount;
        public int columnCount;
        public List<GameLevelCellJsonDto> cells = new List<GameLevelCellJsonDto>();
        public List<GameCrateJsonDto> crates = new List<GameCrateJsonDto>();
        public List<GamePanelJsonDto> panels = new List<GamePanelJsonDto>();
        public List<GameMultiplierGateJsonDto> gates = new List<GameMultiplierGateJsonDto>();
        public List<GameTargetLaneJsonDto> targetBoxLanes = new List<GameTargetLaneJsonDto>();

        [NonSerialized] public bool isExternalAuthoringFormat;
        [NonSerialized] public int? externalSchemaVersion;
        [NonSerialized] public List<string> normalizationErrors = new List<string>();
    }

    [Serializable]
    public sealed class GameCrateJsonDto
    {
        public int row;
        public int col;
    }

    [Serializable]
    public sealed class GamePanelJsonDto
    {
        public int row;
        public int col;
        public int number;
    }

    [Serializable]
    public sealed class GameMultiplierGateJsonDto
    {
        public int row;
        public int col;
        public int mult;
    }

    [Serializable]
    public sealed class GameLevelCellJsonDto
    {
        public LevelCellType cellType;
        public MarbleColorId colorId;
        public int marbleCount;
        public bool isMysterySourceBox;
        public int connectedBoxPairId = -1;
        public int arrowDir = -1;
        public bool hasKey;
        public SpawnerDirection spawnDirection;
        public int spawnCount;
        public List<GameSpawnerSourceBoxJsonDto> spawnSequence = new List<GameSpawnerSourceBoxJsonDto>();
        public List<GameGiftBoxSourceBoxJsonDto> giftBoxContents = new List<GameGiftBoxSourceBoxJsonDto>();
    }

    [Serializable]
    public sealed class GameSpawnerSourceBoxJsonDto
    {
        public MarbleColorId colorId;
    }

    [Serializable]
    public sealed class GameGiftBoxSourceBoxJsonDto
    {
        public MarbleColorId colorId;
        public int marbleCount;
    }

    [Serializable]
    public sealed class GameTargetLaneJsonDto
    {
        public List<GameTargetBoxJsonDto> boxes = new List<GameTargetBoxJsonDto>();
    }

    [Serializable]
    public sealed class GameTargetBoxJsonDto
    {
        public MarbleColorId colorId;
        public bool isMystery;
        public int connectedTargetGroupId;
        public bool isLocked;
    }

    public static class GameLevelJsonConstants
    {
        public const int ConnectedBoxesIntroducedVersion = 5;
        public const int SpawnCountIntroducedVersion = 5;
        public const int RuntimeAvailabilityIntroducedVersion = 6;
        public const int ConnectedTargetsIntroducedVersion = 7;
        public const int CratesIntroducedVersion = 8;
        public const int PanelsIntroducedVersion = 9;
        public const int ArrowBoxesIntroducedVersion = 10;
        public const int KeyLockedTargetsIntroducedVersion = 11;
        public const int MultiplierGatesIntroducedVersion = 12;
        public const int CurrentVersion = MultiplierGatesIntroducedVersion;
        public const int MinimumSupportedVersion = 1;
    }
}

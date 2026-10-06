using System.Collections.Generic;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Levels
{
    [CreateAssetMenu(menuName = "Gameplay/Levels/Level Definition")]
    public sealed class LevelDefinition : ScriptableObject
    {
        public const int TargetBoxLaneCount = 4;
        public const int TargetBoxCapacity = 3;

        [SerializeField, Min(1)] private int levelNumber = 1;
        [SerializeField] private LevelDifficulty difficulty = LevelDifficulty.Normal;
        [SerializeField, Min(1)] private int rowCount = 6;
        [SerializeField, Min(1)] private int columnCount = 6;
        [SerializeField] private bool includeInLoop = true;
        [SerializeField] private List<LevelCellData> cells = new List<LevelCellData>();
        [SerializeField] private List<CrateData> crates = new List<CrateData>();
        [SerializeField] private List<PanelData> panels = new List<PanelData>();
        [SerializeField] private List<MultiplierGateData> gates = new List<MultiplierGateData>();
        [SerializeField] private List<TargetBoxLaneData> targetBoxLanes = new List<TargetBoxLaneData>();
        [SerializeField] private List<MarbleColorId> initialConveyorMarbles = new List<MarbleColorId>();

        public int LevelNumber => levelNumber;
        public LevelDifficulty Difficulty => difficulty;
        public int RowCount => rowCount;
        public int ColumnCount => columnCount;
        public bool IncludeInLoop => includeInLoop;
        public IReadOnlyList<LevelCellData> Cells => cells;
        public IReadOnlyList<CrateData> Crates
        {
            get
            {
                EnsureCrateList();
                return crates;
            }
        }
        public IReadOnlyList<PanelData> Panels
        {
            get
            {
                EnsurePanelList();
                return panels;
            }
        }
        public IReadOnlyList<MultiplierGateData> Gates
        {
            get
            {
                EnsureGateList();
                return gates;
            }
        }
        public IReadOnlyList<TargetBoxLaneData> TargetBoxLanes => targetBoxLanes;
        public IReadOnlyList<MarbleColorId> InitialConveyorMarbles => initialConveyorMarbles;
        public int CellCount => cells?.Count ?? 0;

        private void OnValidate()
        {
            levelNumber = Mathf.Max(1, levelNumber);
            rowCount = Mathf.Max(1, rowCount);
            columnCount = Mathf.Max(1, columnCount);
            EnsureCellListSize();
            EnsureCrateList();
            EnsurePanelList();
            EnsureGateList();
            EnsureTargetBoxLaneListSize();
            initialConveyorMarbles ??= new List<MarbleColorId>();
        }

        public bool TryGetCell(int row, int column, out LevelCellData cell)
        {
            cell = null;

            if (row < 0 || row >= rowCount || column < 0 || column >= columnCount || cells == null)
            {
                return false;
            }

            int index = row * columnCount + column;
            if (index < 0 || index >= cells.Count)
            {
                return false;
            }

            cell = cells[index];
            return cell != null;
        }

        private void EnsureCellListSize()
        {
            int targetCount = rowCount * columnCount;
            if (cells == null)
            {
                cells = new List<LevelCellData>(targetCount);
            }

            while (cells.Count < targetCount)
            {
                cells.Add(new LevelCellData());
            }

            if (cells.Count > targetCount)
            {
                cells.RemoveRange(targetCount, cells.Count - targetCount);
            }

            for (int i = 0; i < cells.Count; i++)
            {
                cells[i]?.EnsureData();
            }
        }

        private void EnsureTargetBoxLaneListSize()
        {
            if (targetBoxLanes == null)
            {
                targetBoxLanes = new List<TargetBoxLaneData>(TargetBoxLaneCount);
            }

            while (targetBoxLanes.Count < TargetBoxLaneCount)
            {
                targetBoxLanes.Add(new TargetBoxLaneData());
            }

            if (targetBoxLanes.Count > TargetBoxLaneCount)
            {
                targetBoxLanes.RemoveRange(TargetBoxLaneCount, targetBoxLanes.Count - TargetBoxLaneCount);
            }

            for (int i = 0; i < targetBoxLanes.Count; i++)
            {
                targetBoxLanes[i]?.EnsureData();
            }
        }

        private void EnsureCrateList()
        {
            if (crates == null)
            {
                crates = new List<CrateData>();
            }
        }

        private void EnsurePanelList()
        {
            if (panels == null)
            {
                panels = new List<PanelData>();
            }
        }

        private void EnsureGateList()
        {
            if (gates == null)
            {
                gates = new List<MultiplierGateData>();
            }
        }
    }
}

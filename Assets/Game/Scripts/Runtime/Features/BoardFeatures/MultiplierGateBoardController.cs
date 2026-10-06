using System.Collections.Generic;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.MultiplierGates
{
    public sealed class MultiplierGateBoardController
    {
        private sealed class RuntimeGate
        {
            public int RuntimeId;
            public int Row;
            public int LeftColumn;
            public int Multiplier;
            public MultiplierGateView View;
        }

        private readonly List<RuntimeGate> gates = new List<RuntimeGate>();
        private readonly HashSet<Vector2Int> footprintCells = new HashSet<Vector2Int>();
        private SourceBoxBoardController boardController;

        public bool IsCellReserved(Vector2Int coordinate)
        {
            return footprintCells.Contains(coordinate);
        }

        public void Prepare(LevelDefinition level)
        {
            Clear();
            if (level?.Gates == null)
            {
                return;
            }

            for (int gateIndex = 0; gateIndex < level.Gates.Count; gateIndex++)
            {
                MultiplierGateData data = level.Gates[gateIndex];
                if (data == null)
                {
                    continue;
                }

                RuntimeGate runtime = new RuntimeGate
                {
                    RuntimeId = gateIndex,
                    Row = data.Row,
                    LeftColumn = data.Col,
                    Multiplier = data.Multiplier
                };
                gates.Add(runtime);
                footprintCells.Add(new Vector2Int(data.Row, data.Col));
                footprintCells.Add(new Vector2Int(data.Row, data.Col + 1));
            }
        }

        public bool BuildViews(
            SourceBoxBoardController owner,
            BoardFeatureCatalog catalog,
            SourceBoxPlaceholderGridController placeholderGridController)
        {
            boardController = owner;
            if (gates.Count == 0)
            {
                return true;
            }

            if (owner == null || catalog?.MultiplierGatePrefab == null || placeholderGridController?.GeneratedGridRoot == null)
            {
                return false;
            }

            for (int i = 0; i < gates.Count; i++)
            {
                RuntimeGate runtime = gates[i];
                MultiplierGateView view = Object.Instantiate(catalog.MultiplierGatePrefab, placeholderGridController.GeneratedGridRoot);
                view.name = $"MultiplierGate_{runtime.Row}_{runtime.LeftColumn}";
                view.transform.localPosition = placeholderGridController.CalculateCellLocalPosition(
                    owner.CurrentLevel.RowCount,
                    owner.CurrentLevel.ColumnCount,
                    runtime.Row,
                    runtime.LeftColumn,
                    catalog.MultiplierGatePrefab.transform.localPosition.z);
                view.transform.localRotation = catalog.MultiplierGatePrefab.transform.localRotation;
                view.transform.localScale = catalog.MultiplierGatePrefab.transform.localScale;

                if (!view.Initialize(this, runtime.RuntimeId))
                {
                    DestroyRuntimeObject(view.gameObject);
                    return false;
                }

                runtime.View = view;
                placeholderGridController.SetSpriteVisible(runtime.Row * owner.CurrentLevel.ColumnCount + runtime.LeftColumn, false);
                placeholderGridController.SetSpriteVisible(runtime.Row * owner.CurrentLevel.ColumnCount + runtime.LeftColumn + 1, false);
            }

            return true;
        }

        public int GetMultiplierForSourceCell(int cellIndex, LevelDefinition level)
        {
            if (level == null || cellIndex < 0 || cellIndex >= level.CellCount)
            {
                return 1;
            }

            int row = cellIndex / level.ColumnCount;
            int column = cellIndex % level.ColumnCount;
            for (int i = 0; i < gates.Count; i++)
            {
                RuntimeGate gate = gates[i];
                if (row > gate.Row && (column == gate.LeftColumn || column == gate.LeftColumn + 1))
                {
                    return gate.Multiplier;
                }
            }

            return 1;
        }

        public bool IsMultiplierAffectedCell(Vector2Int coordinate)
        {
            for (int i = 0; i < gates.Count; i++)
            {
                RuntimeGate gate = gates[i];
                if (coordinate.x > gate.Row &&
                    (coordinate.y == gate.LeftColumn || coordinate.y == gate.LeftColumn + 1))
                {
                    return true;
                }
            }

            return false;
        }

        public bool BlocksInitialEntry(Vector2Int sourceCoordinate)
        {
            return IsMultiplierAffectedCell(sourceCoordinate);
        }

        public bool NotifyMarbleCrossed(int gateRuntimeId, Marble marble)
        {
            RuntimeGate gate = FindGate(gateRuntimeId);
            if (gate == null || marble == null || !marble.IsReleased || marble.IsTransferringToTarget ||
                marble.HasProcessedMultiplierGate(gateRuntimeId) || boardController?.CurrentLevel == null)
            {
                return false;
            }

            int sourceCell = marble.SourceCellIndex;
            if (sourceCell < 0 || GetMultiplierForSourceCell(sourceCell, boardController.CurrentLevel) != gate.Multiplier)
            {
                return false;
            }

            int row = sourceCell / boardController.CurrentLevel.ColumnCount;
            int column = sourceCell % boardController.CurrentLevel.ColumnCount;
            if (row <= gate.Row || (column != gate.LeftColumn && column != gate.LeftColumn + 1))
            {
                return false;
            }

            float duplicateDirection = column == gate.LeftColumn ? 1f : -1f;
            marble.MarkMultiplierGateProcessed(gateRuntimeId);
            if (!boardController.TryCreateMultiplierGateDuplicate(marble, gateRuntimeId, duplicateDirection))
            {
                Debug.LogError($"Multiplier Gate {gateRuntimeId} could not duplicate marble '{marble.name}'.", boardController);
            }

            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < gates.Count; i++)
            {
                if (gates[i].View != null)
                {
                    DestroyRuntimeObject(gates[i].View.gameObject);
                }
            }

            gates.Clear();
            footprintCells.Clear();
            boardController = null;
        }

        private RuntimeGate FindGate(int runtimeId)
        {
            for (int i = 0; i < gates.Count; i++)
            {
                if (gates[i].RuntimeId == runtimeId)
                {
                    return gates[i];
                }
            }

            return null;
        }

        private static void DestroyRuntimeObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

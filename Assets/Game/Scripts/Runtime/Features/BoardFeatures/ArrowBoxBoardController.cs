using System.Collections.Generic;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.ArrowBoxes
{
    /// <summary>Owns Arrow target lookups, permanent runtime resolution, and Arrow views.</summary>
    public sealed class ArrowBoxBoardController
    {
        private sealed class RuntimeArrow
        {
            public int SourceCellIndex;
            public int TargetCellIndex;
            public ArrowDirection Direction;
            public bool Resolved;
            public bool Released;
            public ArrowBoxView View;
        }

        private readonly Dictionary<int, RuntimeArrow> arrowsBySourceCell = new Dictionary<int, RuntimeArrow>();
        private readonly Dictionary<int, List<RuntimeArrow>> arrowsByTargetCell = new Dictionary<int, List<RuntimeArrow>>();

        public void Prepare(LevelDefinition level)
        {
            Clear();
            if (level == null)
            {
                return;
            }

            for (int sourceCellIndex = 0; sourceCellIndex < level.Cells.Count; sourceCellIndex++)
            {
                LevelCellData cell = level.Cells[sourceCellIndex];
                if (cell == null || !cell.HasArrow ||
                    !ArrowDirectionUtility.TryGetDelta(cell.ArrowDirection, out Vector2Int delta))
                {
                    continue;
                }

                int row = sourceCellIndex / level.ColumnCount;
                int column = sourceCellIndex % level.ColumnCount;
                Vector2Int target = new Vector2Int(row, column) + delta;
                int targetCellIndex = target.x * level.ColumnCount + target.y;
                RuntimeArrow runtime = new RuntimeArrow
                {
                    SourceCellIndex = sourceCellIndex,
                    TargetCellIndex = targetCellIndex,
                    Direction = cell.ArrowDirection
                };

                arrowsBySourceCell.Add(sourceCellIndex, runtime);
                if (!arrowsByTargetCell.TryGetValue(targetCellIndex, out List<RuntimeArrow> targetArrows))
                {
                    targetArrows = new List<RuntimeArrow>();
                    arrowsByTargetCell.Add(targetCellIndex, targetArrows);
                }

                targetArrows.Add(runtime);
            }
        }

        public bool BuildViews(
            LevelDefinition level,
            SourceBoxBoardController boardController,
            BoardFeatureCatalog catalog,
            MarbleColorCatalog colorCatalog)
        {
            if (arrowsBySourceCell.Count == 0)
            {
                return true;
            }

            if (level == null || boardController == null || catalog == null || catalog.ArrowBoxPrefab == null || colorCatalog == null)
            {
                return false;
            }

            foreach (RuntimeArrow runtime in arrowsBySourceCell.Values)
            {
                if (!boardController.TryGetSourceBoxByCellIndex(runtime.SourceCellIndex, out SourceBox sourceBox) ||
                    sourceBox.LockedRoot == null)
                {
                    return false;
                }

                LevelCellData cell = level.Cells[runtime.SourceCellIndex];
                if (cell == null || !colorCatalog.TryGetEntry(cell.ColorId, out MarbleColorCatalog.Entry colorEntry) ||
                    !colorEntry.HasArrowStrokeTint)
                {
                    return false;
                }

                ArrowBoxView view = Object.Instantiate(catalog.ArrowBoxPrefab, sourceBox.LockedRoot, false);
                view.name = $"Arrow_{runtime.SourceCellIndex}_{runtime.Direction}";
                if (!view.Initialize(runtime.Direction, colorEntry.ArrowStrokeTint))
                {
                    DestroyGameObject(view.gameObject);
                    return false;
                }

                runtime.View = view;
            }

            return true;
        }

        public bool ConstrainAvailability(int sourceCellIndex, bool normalAvailability)
        {
            if (!arrowsBySourceCell.TryGetValue(sourceCellIndex, out RuntimeArrow runtime) || runtime.Released)
            {
                return normalAvailability;
            }

            return runtime.Resolved;
        }

        public void NotifySourceBoxReleaseStarted(int releasedCellIndex)
        {
            if (arrowsByTargetCell.TryGetValue(releasedCellIndex, out List<RuntimeArrow> targetArrows))
            {
                for (int i = 0; i < targetArrows.Count; i++)
                {
                    RuntimeArrow runtime = targetArrows[i];
                    if (runtime != null && !runtime.Released)
                    {
                        runtime.Resolved = true;
                    }
                }
            }

            if (arrowsBySourceCell.TryGetValue(releasedCellIndex, out RuntimeArrow releasedArrow))
            {
                releasedArrow.Released = true;
            }
        }

        public void Clear()
        {
            foreach (RuntimeArrow runtime in arrowsBySourceCell.Values)
            {
                if (runtime?.View != null)
                {
                    DestroyGameObject(runtime.View.gameObject);
                    runtime.View = null;
                }
            }

            arrowsBySourceCell.Clear();
            arrowsByTargetCell.Clear();
        }

        private static void DestroyGameObject(GameObject target)
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

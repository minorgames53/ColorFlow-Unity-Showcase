using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.Crates
{
    /// <summary>Owns Crate seals, perimeter-removal progress, presentation, and covered SourceBox reveal.</summary>
    public sealed class CrateBoardController
    {
        private sealed class RuntimeCrate
        {
            public int Index;
            public Vector2Int Anchor;
            public Vector2Int[] Footprint;
            public Vector2Int[] Perimeter;
            public readonly HashSet<SourceBox> ProcessedRemovals = new HashSet<SourceBox>();
            public CrateView View;
            public Tween RevealTween;
            public int Progress;
            public float SourceBoxRevealDuration;
            public Ease SourceBoxRevealEase = Ease.OutBack;
        }

        private readonly List<RuntimeCrate> crates = new List<RuntimeCrate>();
        private readonly Dictionary<Vector2Int, List<RuntimeCrate>> cratesByPerimeterCell = new Dictionary<Vector2Int, List<RuntimeCrate>>();
        private readonly HashSet<Vector2Int> sealedCells = new HashSet<Vector2Int>();
        private int lifecycleVersion;

        public bool HasSealedCells => sealedCells.Count > 0;

        public bool IsCellSealed(Vector2Int coordinate)
        {
            return sealedCells.Contains(coordinate);
        }

        public void Prepare(LevelDefinition level)
        {
            Clear();
            if (level?.Crates == null)
            {
                return;
            }

            for (int crateIndex = 0; crateIndex < level.Crates.Count; crateIndex++)
            {
                CrateData data = level.Crates[crateIndex];
                if (data == null)
                {
                    continue;
                }

                RuntimeCrate runtime = new RuntimeCrate
                {
                    Index = crateIndex,
                    Anchor = new Vector2Int(data.Row, data.Col),
                    Footprint = CreateFootprint(data.Row, data.Col),
                    Perimeter = CreatePerimeter(data.Row, data.Col, level.RowCount, level.ColumnCount)
                };

                crates.Add(runtime);
                for (int cellIndex = 0; cellIndex < runtime.Footprint.Length; cellIndex++)
                {
                    sealedCells.Add(runtime.Footprint[cellIndex]);
                }

                for (int cellIndex = 0; cellIndex < runtime.Perimeter.Length; cellIndex++)
                {
                    Vector2Int coordinate = runtime.Perimeter[cellIndex];
                    if (!cratesByPerimeterCell.TryGetValue(coordinate, out List<RuntimeCrate> neighboringCrates))
                    {
                        neighboringCrates = new List<RuntimeCrate>();
                        cratesByPerimeterCell.Add(coordinate, neighboringCrates);
                    }

                    neighboringCrates.Add(runtime);
                }
            }
        }

        public bool BuildViews(
            SourceBoxBoardController boardController,
            BoardFeatureCatalog catalog,
            SourceBoxPlaceholderGridController placeholderGridController)
        {
            if (crates.Count == 0)
            {
                return true;
            }

            if (boardController == null || catalog == null || catalog.CratePrefab == null || placeholderGridController == null ||
                placeholderGridController.GeneratedGridRoot == null)
            {
                return false;
            }

            CrateView prefab = catalog.CratePrefab;
            for (int i = 0; i < crates.Count; i++)
            {
                RuntimeCrate runtime = crates[i];
                CrateView view = Object.Instantiate(prefab, placeholderGridController.GeneratedGridRoot);
                view.name = $"Crate_{runtime.Anchor.x}_{runtime.Anchor.y}";
                view.transform.localPosition = placeholderGridController.CalculateCellLocalPosition(
                    boardController.CurrentLevel.RowCount,
                    boardController.CurrentLevel.ColumnCount,
                    runtime.Anchor.x,
                    runtime.Anchor.y,
                    prefab.transform.localPosition.z);
                view.transform.localRotation = Quaternion.identity;
                view.transform.localScale = Vector3.one;

                if (!view.Initialize())
                {
                    DestroyGameObject(view.gameObject);
                    return false;
                }

                runtime.View = view;
                runtime.SourceBoxRevealDuration = catalog.GiftBoxPrefab != null
                    ? catalog.GiftBoxPrefab.SourceBoxScaleDuration
                    : view.SourceBoxRevealDuration;
                runtime.SourceBoxRevealEase = catalog.GiftBoxPrefab != null
                    ? catalog.GiftBoxPrefab.SourceBoxScaleEase
                    : view.SourceBoxRevealEase;
                for (int cellIndex = 0; cellIndex < runtime.Footprint.Length; cellIndex++)
                {
                    Vector2Int coordinate = runtime.Footprint[cellIndex];
                    int flatIndex = coordinate.x * boardController.CurrentLevel.ColumnCount + coordinate.y;
                    placeholderGridController.SetSpriteVisible(flatIndex, false);
                }
            }

            return true;
        }

        public void NotifySourceBoxRemoved(Vector2Int coordinate, SourceBox sourceBox, SourceBoxBoardController boardController)
        {
            if (sourceBox == null || boardController == null || !cratesByPerimeterCell.TryGetValue(coordinate, out List<RuntimeCrate> neighboringCrates))
            {
                return;
            }

            for (int i = 0; i < neighboringCrates.Count; i++)
            {
                RuntimeCrate runtime = neighboringCrates[i];
                if (runtime == null || runtime.Progress >= 3 || runtime.ProcessedRemovals.Contains(sourceBox))
                {
                    continue;
                }

                if (runtime.Progress == 2 && !boardController.CanBeginCrateSourceReveal(runtime.Footprint, out string error))
                {
                    Debug.LogError($"Crate {runtime.Index} at row {runtime.Anchor.x}, column {runtime.Anchor.y} cannot reveal its covered SourceBoxes: {error}", boardController);
                    continue;
                }

                runtime.ProcessedRemovals.Add(sourceBox);
                runtime.Progress++;
                int transactionVersion = lifecycleVersion;
                bool presentationQueued = runtime.View != null && runtime.View.EnqueueProgress(
                    runtime.Progress,
                    runtime.Progress == 3
                        ? () =>
                        {
                            if (transactionVersion == lifecycleVersion && runtime.Progress == 3)
                            {
                                BeginFinalReveal(runtime, boardController);
                            }
                        }
                        : null,
                    runtime.Progress == 3
                        ? () =>
                        {
                            if (transactionVersion == lifecycleVersion && runtime.Progress == 3)
                            {
                                CleanupCrateView(runtime);
                            }
                        }
                        : null);

                if (!presentationQueued)
                {
                    Debug.LogError($"Crate {runtime.Index} at row {runtime.Anchor.x}, column {runtime.Anchor.y} could not queue presentation for progress {runtime.Progress}.", boardController);
                    if (runtime.Progress == 3)
                    {
                        BeginFinalReveal(runtime, boardController);
                    }
                }
            }
        }

        public void Clear()
        {
            lifecycleVersion++;
            for (int i = 0; i < crates.Count; i++)
            {
                RuntimeCrate runtime = crates[i];
                runtime.RevealTween?.Kill();
                runtime.RevealTween = null;
                if (runtime.View != null)
                {
                    DestroyGameObject(runtime.View.gameObject);
                    runtime.View = null;
                }
            }

            crates.Clear();
            cratesByPerimeterCell.Clear();
            sealedCells.Clear();
        }

        private void BeginFinalReveal(RuntimeCrate runtime, SourceBoxBoardController boardController)
        {
            float revealDuration = runtime.SourceBoxRevealDuration > 0f
                ? runtime.SourceBoxRevealDuration
                : CrateView.DefaultSourceBoxRevealDuration;
            Ease revealEase = runtime.SourceBoxRevealEase;

            for (int i = 0; i < runtime.Footprint.Length; i++)
            {
                sealedCells.Remove(runtime.Footprint[i]);
            }

            if (!boardController.TryBeginCrateSourceReveal(runtime.Footprint, out List<SourceBox> revealedSourceBoxes))
            {
                for (int i = 0; i < runtime.Footprint.Length; i++)
                {
                    sealedCells.Add(runtime.Footprint[i]);
                }

                runtime.Progress = 2;
                runtime.View?.ResetPresentation(runtime.Progress);
                Debug.LogError($"Crate {runtime.Index} at row {runtime.Anchor.x}, column {runtime.Anchor.y} failed to spawn its covered SourceBoxes. The footprint remains sealed.", boardController);
                return;
            }

            int transactionVersion = lifecycleVersion;
            Sequence sequence = DOTween.Sequence().SetLink(boardController.gameObject, LinkBehaviour.KillOnDestroy);
            for (int i = 0; i < revealedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = revealedSourceBoxes[i];
                if (sourceBox != null)
                {
                    sequence.Insert(0f, sourceBox.transform.DOScale(Vector3.one, revealDuration).SetEase(revealEase));
                }
            }

            runtime.RevealTween = sequence;
            sequence.OnComplete(() =>
            {
                if (transactionVersion != lifecycleVersion || runtime.Progress != 3)
                {
                    return;
                }

                runtime.RevealTween = null;
                boardController.FinalizeCrateSourceReveal(revealedSourceBoxes);
            });
            sequence.OnKill(() =>
            {
                if (runtime.RevealTween == sequence)
                {
                    runtime.RevealTween = null;
                }
            });
        }

        private static void CleanupCrateView(RuntimeCrate runtime)
        {
            if (runtime?.View == null)
            {
                return;
            }

            DestroyGameObject(runtime.View.gameObject);
            runtime.View = null;
        }

        private static Vector2Int[] CreateFootprint(int row, int column)
        {
            return new[]
            {
                new Vector2Int(row, column),
                new Vector2Int(row, column + 1),
                new Vector2Int(row + 1, column),
                new Vector2Int(row + 1, column + 1)
            };
        }

        private static Vector2Int[] CreatePerimeter(int row, int column, int rowCount, int columnCount)
        {
            Vector2Int[] candidates =
            {
                new Vector2Int(row - 1, column),
                new Vector2Int(row - 1, column + 1),
                new Vector2Int(row + 2, column),
                new Vector2Int(row + 2, column + 1),
                new Vector2Int(row, column - 1),
                new Vector2Int(row + 1, column - 1),
                new Vector2Int(row, column + 2),
                new Vector2Int(row + 1, column + 2)
            };

            List<Vector2Int> result = new List<Vector2Int>(8);
            for (int i = 0; i < candidates.Length; i++)
            {
                Vector2Int candidate = candidates[i];
                if (candidate.x >= 0 && candidate.x < rowCount && candidate.y >= 0 && candidate.y < columnCount)
                {
                    result.Add(candidate);
                }
            }

            return result.ToArray();
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

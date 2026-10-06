using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.Panels
{
    /// <summary>Owns Panel seals, target-full progress, presentation, and covered SourceBox reveal.</summary>
    public sealed class PanelBoardController
    {
        private sealed class RuntimePanel
        {
            public int Index;
            public Vector2Int Anchor;
            public Vector2Int[] Footprint;
            public int InitialNumber;
            public int Remaining;
            public bool Opening;
            public PanelView View;
            public Tween RevealTween;
            public float SourceBoxRevealDuration;
            public Ease SourceBoxRevealEase = Ease.OutBack;
        }

        private readonly List<RuntimePanel> panels = new List<RuntimePanel>();
        private readonly List<RuntimePanel> activePanels = new List<RuntimePanel>();
        private readonly HashSet<Vector2Int> sealedCells = new HashSet<Vector2Int>();
        private readonly HashSet<TargetBox> processedFullTargets = new HashSet<TargetBox>();
        private int lifecycleVersion;

        public bool HasSealedCells => sealedCells.Count > 0;

        public bool IsCellSealed(Vector2Int coordinate)
        {
            return sealedCells.Contains(coordinate);
        }

        public void Prepare(LevelDefinition level)
        {
            Clear();
            if (level?.Panels == null)
            {
                return;
            }

            for (int panelIndex = 0; panelIndex < level.Panels.Count; panelIndex++)
            {
                PanelData data = level.Panels[panelIndex];
                if (data == null)
                {
                    continue;
                }

                RuntimePanel runtime = new RuntimePanel
                {
                    Index = panelIndex,
                    Anchor = new Vector2Int(data.Row, data.Col),
                    Footprint = CreateFootprint(data.Row, data.Col),
                    InitialNumber = data.Number,
                    Remaining = data.Number
                };

                panels.Add(runtime);
                activePanels.Add(runtime);
                for (int cellIndex = 0; cellIndex < runtime.Footprint.Length; cellIndex++)
                {
                    sealedCells.Add(runtime.Footprint[cellIndex]);
                }
            }
        }

        public bool BuildViews(
            SourceBoxBoardController boardController,
            BoardFeatureCatalog catalog,
            SourceBoxPlaceholderGridController placeholderGridController)
        {
            if (panels.Count == 0)
            {
                return true;
            }

            if (boardController == null || catalog == null || catalog.PanelPrefab == null || placeholderGridController == null ||
                placeholderGridController.GeneratedGridRoot == null)
            {
                return false;
            }

            PanelView prefab = catalog.PanelPrefab;
            for (int i = 0; i < panels.Count; i++)
            {
                RuntimePanel runtime = panels[i];
                PanelView view = Object.Instantiate(prefab, placeholderGridController.GeneratedGridRoot);
                view.name = $"Panel_{runtime.Anchor.x}_{runtime.Anchor.y}";
                view.transform.localPosition = placeholderGridController.CalculateCellLocalPosition(
                    boardController.CurrentLevel.RowCount,
                    boardController.CurrentLevel.ColumnCount,
                    runtime.Anchor.x,
                    runtime.Anchor.y,
                    prefab.transform.localPosition.z);
                view.transform.localRotation = Quaternion.identity;
                view.transform.localScale = Vector3.one;

                if (!view.Initialize(runtime.InitialNumber))
                {
                    DestroyGameObject(view.gameObject);
                    return false;
                }

                runtime.View = view;
                runtime.SourceBoxRevealDuration = catalog.GiftBoxPrefab != null
                    ? catalog.GiftBoxPrefab.SourceBoxScaleDuration
                    : PanelView.DefaultSourceBoxRevealDuration;
                runtime.SourceBoxRevealEase = catalog.GiftBoxPrefab != null
                    ? catalog.GiftBoxPrefab.SourceBoxScaleEase
                    : Ease.OutBack;

                for (int cellIndex = 0; cellIndex < runtime.Footprint.Length; cellIndex++)
                {
                    Vector2Int coordinate = runtime.Footprint[cellIndex];
                    int flatIndex = coordinate.x * boardController.CurrentLevel.ColumnCount + coordinate.y;
                    placeholderGridController.SetSpriteVisible(flatIndex, false);
                }
            }

            return true;
        }

        public void NotifyTargetBoxFirstFilled(TargetBox target, SourceBoxBoardController boardController)
        {
            if (target == null || boardController == null || !processedFullTargets.Add(target) || activePanels.Count == 0)
            {
                return;
            }

            RuntimePanel[] snapshot = activePanels.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                RuntimePanel runtime = snapshot[i];
                if (runtime == null || runtime.Opening || runtime.Remaining <= 0)
                {
                    continue;
                }

                if (runtime.Remaining == 1 && !boardController.CanBeginPanelSourceReveal(runtime.Footprint, out string error))
                {
                    Debug.LogError($"Panel {runtime.Index} at row {runtime.Anchor.x}, column {runtime.Anchor.y} cannot reveal its covered SourceBoxes: {error}", boardController);
                    continue;
                }

                runtime.Remaining = Mathf.Max(0, runtime.Remaining - 1);
                if (runtime.Remaining == 0)
                {
                    runtime.Opening = true;
                    activePanels.Remove(runtime);
                }

                int transactionVersion = lifecycleVersion;
                bool queued = runtime.View != null && runtime.View.EnqueueRemaining(
                    runtime.Remaining,
                    runtime.Remaining == 0
                        ? () =>
                        {
                            if (transactionVersion == lifecycleVersion && runtime.Opening)
                            {
                                BeginFinalReveal(runtime, boardController);
                            }
                        }
                        : null);

                if (!queued)
                {
                    Debug.LogError($"Panel {runtime.Index} at row {runtime.Anchor.x}, column {runtime.Anchor.y} could not queue number {runtime.Remaining} presentation.", boardController);
                    if (runtime.Remaining == 0)
                    {
                        BeginFinalReveal(runtime, boardController);
                    }
                }
            }
        }

        public void Clear()
        {
            lifecycleVersion++;
            for (int i = 0; i < panels.Count; i++)
            {
                RuntimePanel runtime = panels[i];
                runtime.RevealTween?.Kill(false);
                runtime.RevealTween = null;
                if (runtime.View != null)
                {
                    DestroyGameObject(runtime.View.gameObject);
                    runtime.View = null;
                }
            }

            panels.Clear();
            activePanels.Clear();
            sealedCells.Clear();
            processedFullTargets.Clear();
        }

        private void BeginFinalReveal(RuntimePanel runtime, SourceBoxBoardController boardController)
        {
            for (int i = 0; i < runtime.Footprint.Length; i++)
            {
                sealedCells.Remove(runtime.Footprint[i]);
            }

            if (!boardController.TryBeginPanelSourceReveal(runtime.Footprint, out List<SourceBox> revealedSourceBoxes))
            {
                for (int i = 0; i < runtime.Footprint.Length; i++)
                {
                    sealedCells.Add(runtime.Footprint[i]);
                }

                runtime.Opening = false;
                runtime.Remaining = 1;
                activePanels.Add(runtime);
                runtime.View?.ResetPresentation(runtime.Remaining);
                Debug.LogError($"Panel {runtime.Index} at row {runtime.Anchor.x}, column {runtime.Anchor.y} failed to spawn its covered SourceBoxes. The footprint remains sealed.", boardController);
                return;
            }

            int transactionVersion = lifecycleVersion;
            float revealDuration = Mathf.Max(0.0001f, runtime.SourceBoxRevealDuration);
            Sequence sequence = DOTween.Sequence().SetLink(boardController.gameObject, LinkBehaviour.KillOnDestroy);
            for (int i = 0; i < revealedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = revealedSourceBoxes[i];
                if (sourceBox != null)
                {
                    sequence.Insert(0f, sourceBox.transform.DOScale(Vector3.one, revealDuration).SetEase(runtime.SourceBoxRevealEase));
                }
            }

            runtime.RevealTween = sequence;
            sequence.OnComplete(() =>
            {
                if (transactionVersion != lifecycleVersion || !runtime.Opening)
                {
                    return;
                }

                runtime.RevealTween = null;
                boardController.FinalizePanelSourceReveal(revealedSourceBoxes);
                CleanupPanelView(runtime);
            });
            sequence.OnKill(() =>
            {
                if (runtime.RevealTween == sequence)
                {
                    runtime.RevealTween = null;
                }
            });
        }

        private static Vector2Int[] CreateFootprint(int row, int column)
        {
            Vector2Int[] result = new Vector2Int[9];
            int index = 0;
            for (int rowOffset = 0; rowOffset < 3; rowOffset++)
            {
                for (int columnOffset = 0; columnOffset < 3; columnOffset++)
                {
                    result[index++] = new Vector2Int(row + rowOffset, column + columnOffset);
                }
            }

            return result;
        }

        private static void CleanupPanelView(RuntimePanel runtime)
        {
            if (runtime?.View == null)
            {
                return;
            }

            DestroyGameObject(runtime.View.gameObject);
            runtime.View = null;
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

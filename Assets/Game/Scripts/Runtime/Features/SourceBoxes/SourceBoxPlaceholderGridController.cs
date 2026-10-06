using Sirenix.OdinInspector;
using Gameplay.Levels;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Gameplay.SourceBoxes
{
    public sealed class SourceBoxPlaceholderGridController : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private SourceBoxPlaceholder placeholderPrefab;
        [SerializeField] private Transform generatedGridRoot;

        [Header("Grid")]
        [SerializeField, Min(1), OnValueChanged(nameof(ScheduleRefreshGeneratedPositions))]
        private int rowCount = 6;

        [SerializeField, Min(1), OnValueChanged(nameof(ScheduleRefreshGeneratedPositions))]
        private int columnCount = 6;

        [SerializeField, OnValueChanged(nameof(ScheduleRefreshGeneratedPositions))]
        private Vector2 cellSize = Vector2.one;

        [SerializeField, OnValueChanged(nameof(ScheduleRefreshGeneratedPositions))]
        private Vector2 spacing = Vector2.zero;

        [SerializeField, OnValueChanged(nameof(ScheduleRefreshGeneratedPositions))]
        private Vector2 boardOffset = Vector2.zero;

        [Header("Empty Cells")]
        [SerializeField, Range(0f, 1f)] private float emptyCellSpriteValue = 0.9f;

        private SourceBoxPlaceholder[] placeholdersByCell;
        private int builtRowCount;
        private int builtColumnCount;
        private Vector3 authoredLocalPosition;
        private Vector3 authoredLocalScale;
        private Quaternion authoredLocalRotation;
        private bool hasCachedAuthoredTransform;

        public Transform GeneratedGridRoot => generatedGridRoot != null ? generatedGridRoot : transform;
        public event System.Action BoardLayoutChanged;

        private void Awake()
        {
            CacheAuthoredTransform();
        }

        private void Reset()
        {
            generatedGridRoot = transform;
        }

        private void OnValidate()
        {
            rowCount = Mathf.Max(1, rowCount);
            columnCount = Mathf.Max(1, columnCount);
            spacing = new Vector2(Mathf.Max(0f, spacing.x), Mathf.Max(0f, spacing.y));
            cellSize = new Vector2(Mathf.Max(0f, cellSize.x), Mathf.Max(0f, cellSize.y));
            emptyCellSpriteValue = Mathf.Clamp01(emptyCellSpriteValue);
            ScheduleRefreshGeneratedPositions();
        }

        [Button]
        public void BuildGrids()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning($"{nameof(SourceBoxPlaceholderGridController)} on '{name}' cannot build placeholder grids from the Inspector during Play Mode.", this);
                return;
            }

            BuildGrid(rowCount, columnCount);
        }

        [Button]
        public void ClearGrids()
        {
            ClearGridInternal(true);
            builtRowCount = 0;
            builtColumnCount = 0;
            placeholdersByCell = null;
        }

        public bool BuildGrid(int rows, int columns)
        {
            if (!CanBuild(rows, columns))
            {
                return false;
            }

            ApplyBoardSizeTransform(rows, columns);
            ClearGridInternal(!Application.isPlaying);

            builtRowCount = rows;
            builtColumnCount = columns;
            placeholdersByCell = new SourceBoxPlaceholder[rows * columns];

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int cellIndex = row * columns + column;

                    SourceBoxPlaceholder placeholder = InstantiatePlaceholder();
                    if (placeholder == null)
                    {
                        continue;
                    }

                    placeholder.name = $"Placeholder_{row}_{column}";
                    placeholder.transform.localPosition = CalculateCellLocalPosition(rows, columns, row, column, placeholder.transform.localPosition.z);
                    placeholder.transform.localRotation = Quaternion.identity;
                    placeholder.Initialize(cellIndex);
                    placeholdersByCell[cellIndex] = placeholder;
                }
            }

            MarkRootDirty();
            BoardLayoutChanged?.Invoke();
            return true;
        }

        public void ApplyBoardSizeTransform(int rows, int columns)
        {
            CacheAuthoredTransform();

            if (rows == 7 && columns == 7)
            {
                Vector3 position = authoredLocalPosition;
                position.y = -0.3f;
                transform.localPosition = position;
                transform.localScale = Vector3.one * 0.9f;
                transform.localRotation = authoredLocalRotation;
                return;
            }

            transform.localPosition = authoredLocalPosition;
            transform.localScale = authoredLocalScale;
            transform.localRotation = authoredLocalRotation;
        }

        public void ApplyLevelCellStates(LevelDefinition levelDefinition)
        {
            if (levelDefinition == null || placeholdersByCell == null)
            {
                return;
            }

            int cellCount = Mathf.Min(levelDefinition.CellCount, placeholdersByCell.Length);
            for (int i = 0; i < cellCount; i++)
            {
                SourceBoxPlaceholder placeholder = placeholdersByCell[i];
                if (placeholder == null)
                {
                    continue;
                }

                LevelCellData cell = levelDefinition.Cells[i];
                if (cell == null)
                {
                    placeholder.SetSpriteVisible(true);
                    placeholder.SetEmptyTintValue(emptyCellSpriteValue);
                }
                else if (cell.CellType == LevelCellType.Empty || cell.CellType == LevelCellType.Blocked)
                {
                    placeholder.SetSpriteVisible(false);
                }
                else
                {
                    placeholder.ResetTint();
                }
            }
        }

        public void ClearRuntimeGrid()
        {
            ClearGridInternal(false);
            builtRowCount = 0;
            builtColumnCount = 0;
            placeholdersByCell = null;
        }

        public bool TryGetPlaceholder(int cellIndex, out SourceBoxPlaceholder placeholder)
        {
            placeholder = null;

            if (cellIndex < 0)
            {
                return false;
            }

            if (placeholdersByCell != null && cellIndex < placeholdersByCell.Length)
            {
                placeholder = placeholdersByCell[cellIndex];
                if (placeholder != null)
                {
                    return true;
                }
            }

            placeholder = FindGeneratedPlaceholder(cellIndex);
            return placeholder != null;
        }

        public bool TryGetSourceBoxAnchor(int cellIndex, out Transform anchor)
        {
            anchor = null;

            if (!TryGetPlaceholder(cellIndex, out SourceBoxPlaceholder placeholder) || placeholder.SourceBoxAnchor == null)
            {
                return false;
            }

            anchor = placeholder.SourceBoxAnchor;
            return true;
        }

        public void SetSpriteVisible(int cellIndex, bool visible)
        {
            if (TryGetPlaceholder(cellIndex, out SourceBoxPlaceholder placeholder))
            {
                placeholder.SetSpriteVisible(visible);
            }
        }

        public Vector3 CalculateCellLocalPosition(int rows, int columns, int row, int column, float z = 0f)
        {
            float stepX = cellSize.x + spacing.x;
            float stepY = cellSize.y + spacing.y;
            float startX = -((columns - 1) * stepX) / 2f;
            float startY = -((rows - 1) * stepY) / 2f;

            return new Vector3(
                startX + column * stepX + boardOffset.x,
                startY + row * stepY + boardOffset.y,
                z);
        }

        public void RefreshGeneratedPositions()
        {
            if (this == null)
            {
                return;
            }

            int rows = builtRowCount > 0 ? builtRowCount : rowCount;
            int columns = builtColumnCount > 0 ? builtColumnCount : columnCount;
            int cellCount = rows * columns;
            if (generatedGridRoot == null)
            {
                return;
            }

            for (int i = 0; i < generatedGridRoot.childCount; i++)
            {
                Transform child = generatedGridRoot.GetChild(i);
                SourceBoxPlaceholder placeholder = child.GetComponent<SourceBoxPlaceholder>();
                if (placeholder == null)
                {
                    continue;
                }

                int cellIndex = placeholder.CellIndex;
                if (cellIndex < 0 || cellIndex >= cellCount)
                {
                    continue;
                }

                int row = cellIndex / columns;
                int column = cellIndex % columns;
                child.localPosition = CalculateCellLocalPosition(rows, columns, row, column, child.localPosition.z);
            }

            MarkRootDirty();
            BoardLayoutChanged?.Invoke();
        }

        private bool CanBuild(int rows, int columns)
        {
            if (rows < 1 || columns < 1)
            {
                Debug.LogError($"{nameof(SourceBoxPlaceholderGridController)} on '{name}' cannot build because grid size is invalid. Rows: {rows}, Columns: {columns}.", this);
                return false;
            }

            if (generatedGridRoot == null)
            {
                Debug.LogError($"{nameof(SourceBoxPlaceholderGridController)} on '{name}' cannot build because Generated Grid Root is missing.", this);
                return false;
            }

            if (placeholderPrefab == null)
            {
                Debug.LogError($"{nameof(SourceBoxPlaceholderGridController)} on '{name}' cannot build because Placeholder Prefab is missing.", this);
                return false;
            }

            return true;
        }

        private void CacheAuthoredTransform()
        {
            if (hasCachedAuthoredTransform)
            {
                return;
            }

            authoredLocalPosition = transform.localPosition;
            authoredLocalScale = transform.localScale;
            authoredLocalRotation = transform.localRotation;
            hasCachedAuthoredTransform = true;
        }

        private SourceBoxPlaceholder InstantiatePlaceholder()
        {
            if (placeholderPrefab == null || generatedGridRoot == null)
            {
                return null;
            }

            SourceBoxPlaceholder placeholder;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject prefabInstance = PrefabUtility.InstantiatePrefab(placeholderPrefab.gameObject, generatedGridRoot) as GameObject;
                placeholder = prefabInstance != null ? prefabInstance.GetComponent<SourceBoxPlaceholder>() : null;
                if (placeholder != null)
                {
                    Undo.RegisterCreatedObjectUndo(placeholder.gameObject, "Create SourceBox Placeholder");
                }

                return placeholder;
            }
#endif

            placeholder = Instantiate(placeholderPrefab, generatedGridRoot);
            return placeholder;
        }

        private void ClearGridInternal(bool useUndo)
        {
            if (generatedGridRoot == null)
            {
                return;
            }

            for (int i = generatedGridRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = generatedGridRoot.GetChild(i);
                if (child.GetComponent<SourceBoxPlaceholder>() == null)
                {
                    continue;
                }

                DestroyPlaceholder(child.gameObject, useUndo);
            }

            MarkRootDirty();
        }

        private void DestroyPlaceholder(GameObject target, bool useUndo)
        {
            if (target == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && useUndo)
            {
                Undo.DestroyObjectImmediate(target);
                return;
            }
#endif

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private SourceBoxPlaceholder FindGeneratedPlaceholder(int cellIndex)
        {
            if (generatedGridRoot == null)
            {
                return null;
            }

            for (int i = 0; i < generatedGridRoot.childCount; i++)
            {
                SourceBoxPlaceholder placeholder = generatedGridRoot.GetChild(i).GetComponent<SourceBoxPlaceholder>();
                if (placeholder != null && placeholder.CellIndex == cellIndex)
                {
                    return placeholder;
                }
            }

            return null;
        }

        private void ScheduleRefreshGeneratedPositions()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorApplication.delayCall -= RefreshGeneratedPositions;
                EditorApplication.delayCall += RefreshGeneratedPositions;
            }
#endif
        }

        private void MarkRootDirty()
        {
#if UNITY_EDITOR
            if (Application.isPlaying || generatedGridRoot == null)
            {
                return;
            }

            EditorUtility.SetDirty(generatedGridRoot);
            if (generatedGridRoot.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(generatedGridRoot.gameObject.scene);
            }
#endif
        }
    }
}

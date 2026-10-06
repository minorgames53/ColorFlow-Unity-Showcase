using System.Collections.Generic;
using Gameplay.BoardFeatures.MultiplierGates;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardTiles
{
    [DisallowMultipleComponent]
    public sealed class BoardTileController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BoardTileView boardTilePrefab;
        [SerializeField] private GameObject horizontalNeighborConnectorPrefab;
        [SerializeField] private GameObject verticalNeighborConnectorPrefab;
        [SerializeField] private GameObject cornerConnectorPrefab;
        [SerializeField] private BoardTileInnerCornerConnectorView innerCornerConnectorPrefab;
        [SerializeField] private Transform tilesParent;
        [SerializeField] private SourceBoxPlaceholderGridController placeholderGridController;

        [Header("Z Layering")]
        [SerializeField] private float boardTileZ = -3f;
        [SerializeField] private float horizontalConnectorZ = -6f;
        [SerializeField] private float verticalConnectorZ = -6f;
        [SerializeField] private float cornerConnectorZ = -9f;
        [SerializeField] private float innerCornerConnectorZ = -12f;

        private readonly Dictionary<Vector2Int, BoardTileView> tilesByCoordinate =
            new Dictionary<Vector2Int, BoardTileView>();
        private readonly List<GameObject> horizontalNeighborConnectors = new List<GameObject>();
        private readonly List<GameObject> verticalNeighborConnectors = new List<GameObject>();
        private readonly List<GameObject> cornerConnectors = new List<GameObject>();
        private readonly List<GameObject> innerCornerConnectors = new List<GameObject>();

        public IReadOnlyDictionary<Vector2Int, BoardTileView> TilesByCoordinate => tilesByCoordinate;

        public bool CanBuild(LevelDefinition levelDefinition)
        {
            bool canBuild = true;

            if (levelDefinition == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' cannot build because LevelDefinition is null.", this);
                canBuild = false;
            }

            if (boardTilePrefab == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' is missing BoardTile prefab reference.", this);
                canBuild = false;
            }
            else if (!boardTilePrefab.HasRequiredReferences)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' has a BoardTile prefab with missing view references.", this);
                canBuild = false;
            }

            if (horizontalNeighborConnectorPrefab == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' is missing Horizontal Neighbor Connector prefab reference.", this);
                canBuild = false;
            }

            if (verticalNeighborConnectorPrefab == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' is missing Vertical Neighbor Connector prefab reference.", this);
                canBuild = false;
            }

            if (cornerConnectorPrefab == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' is missing Corner Connector prefab reference.", this);
                canBuild = false;
            }

            if (innerCornerConnectorPrefab == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' is missing Inner Corner Connector prefab reference.", this);
                canBuild = false;
            }
            else if (!innerCornerConnectorPrefab.HasRequiredReferences)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' has an Inner Corner Connector prefab with missing view references.", this);
                canBuild = false;
            }

            if (tilesParent == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' is missing Tiles Parent reference.", this);
                canBuild = false;
            }

            if (placeholderGridController == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' is missing SourceBoxPlaceholderGridController reference.", this);
                canBuild = false;
            }

            return canBuild;
        }

        public bool Build(
            LevelDefinition levelDefinition,
            MultiplierGateBoardController multiplierGateBoardController)
        {
            Clear();
            if (!CanBuild(levelDefinition))
            {
                return false;
            }

            if (multiplierGateBoardController == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' cannot build without MultiplierGateBoardController footprint data.", this);
                return false;
            }

            int rowCount = levelDefinition.RowCount;
            int columnCount = levelDefinition.ColumnCount;

            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    Vector2Int coordinate = new Vector2Int(row, column);
                    if (!levelDefinition.TryGetCell(row, column, out LevelCellData cell) ||
                        !IsTerrainCell(cell) ||
                        multiplierGateBoardController.IsCellReserved(coordinate))
                    {
                        continue;
                    }

                    if (!TrySpawnTile(rowCount, columnCount, row, column))
                    {
                        Clear();
                        return false;
                    }
                }
            }

            if (!BuildHorizontalNeighborConnectors(rowCount, columnCount))
            {
                Clear();
                return false;
            }

            if (!BuildVerticalNeighborConnectors(rowCount, columnCount))
            {
                Clear();
                return false;
            }

            if (!BuildJunctionConnectors(rowCount, columnCount))
            {
                Clear();
                return false;
            }

            if (!BuildInnerCornerConnectors(
                    levelDefinition,
                    multiplierGateBoardController,
                    rowCount,
                    columnCount))
            {
                Clear();
                return false;
            }

            return true;
        }

        public bool TryGetTile(Vector2Int coordinate, out BoardTileView tile)
        {
            return tilesByCoordinate.TryGetValue(coordinate, out tile) && tile != null;
        }

        public void Clear()
        {
            DestroyConnectorInstances(horizontalNeighborConnectors);
            DestroyConnectorInstances(verticalNeighborConnectors);
            DestroyConnectorInstances(cornerConnectors);
            DestroyConnectorInstances(innerCornerConnectors);

            foreach (BoardTileView tile in tilesByCoordinate.Values)
            {
                if (tile == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(tile.gameObject);
                }
                else
                {
                    DestroyImmediate(tile.gameObject);
                }
            }

            tilesByCoordinate.Clear();
        }

        private bool TrySpawnTile(int rowCount, int columnCount, int row, int column)
        {
            BoardTileView tile = Instantiate(boardTilePrefab, tilesParent);
            if (tile == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' failed to instantiate a BoardTile at [{row},{column}].", this);
                return false;
            }

            Vector2Int coordinate = new Vector2Int(row, column);
            tile.name = $"BoardTile [{row},{column}]";
            ApplyCellTransform(tile.transform, rowCount, columnCount, row, column);
            tilesByCoordinate.Add(coordinate, tile);
            return true;
        }

        private bool BuildHorizontalNeighborConnectors(int rowCount, int columnCount)
        {
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    Vector2Int coordinate = new Vector2Int(row, column);
                    if (!tilesByCoordinate.TryGetValue(coordinate, out BoardTileView currentTile) || currentTile == null)
                    {
                        continue;
                    }

                    Vector2Int rightCoordinate = new Vector2Int(row, column + 1);
                    if (tilesByCoordinate.TryGetValue(rightCoordinate, out BoardTileView rightTile) && rightTile != null &&
                        !TrySpawnNeighborConnector(
                            horizontalNeighborConnectorPrefab,
                            currentTile,
                            rightTile,
                            horizontalNeighborConnectors,
                            horizontalConnectorZ,
                            $"Horizontal Connector [{row},{column}]-[{row},{column + 1}]"))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool BuildVerticalNeighborConnectors(int rowCount, int columnCount)
        {
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    Vector2Int coordinate = new Vector2Int(row, column);
                    if (!tilesByCoordinate.TryGetValue(coordinate, out BoardTileView currentTile) || currentTile == null)
                    {
                        continue;
                    }

                    Vector2Int verticalCoordinate = new Vector2Int(row + 1, column);
                    if (tilesByCoordinate.TryGetValue(verticalCoordinate, out BoardTileView verticalTile) && verticalTile != null &&
                        !TrySpawnNeighborConnector(
                            verticalNeighborConnectorPrefab,
                            currentTile,
                            verticalTile,
                            verticalNeighborConnectors,
                            verticalConnectorZ,
                            $"Vertical Connector [{row},{column}]-[{row + 1},{column}]"))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool BuildJunctionConnectors(int rowCount, int columnCount)
        {
            for (int row = 0; row < rowCount - 1; row++)
            {
                for (int column = 0; column < columnCount - 1; column++)
                {
                    Vector2Int currentCoordinate = new Vector2Int(row, column);
                    Vector2Int rightCoordinate = new Vector2Int(row, column + 1);
                    Vector2Int verticalCoordinate = new Vector2Int(row + 1, column);
                    Vector2Int diagonalCoordinate = new Vector2Int(row + 1, column + 1);

                    if (!TryGetTile(currentCoordinate, out BoardTileView currentTile) ||
                        !TryGetTile(rightCoordinate, out BoardTileView rightTile) ||
                        !TryGetTile(verticalCoordinate, out BoardTileView verticalTile) ||
                        !TryGetTile(diagonalCoordinate, out BoardTileView diagonalTile))
                    {
                        continue;
                    }

                    if (!TrySpawnCornerConnector(
                            currentTile,
                            rightTile,
                            verticalTile,
                            diagonalTile,
                            $"Corner Connector [{row},{column}]-[{row + 1},{column + 1}]"))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool BuildInnerCornerConnectors(
            LevelDefinition levelDefinition,
            MultiplierGateBoardController multiplierGateBoardController,
            int rowCount,
            int columnCount)
        {
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    if (!levelDefinition.TryGetCell(row, column, out LevelCellData cell))
                    {
                        continue;
                    }

                    Vector2Int coordinate = new Vector2Int(row, column);
                    bool isMultiplierGateCell =
                        multiplierGateBoardController.IsCellReserved(coordinate);
                    if (TryGetTile(coordinate, out _) ||
                        (IsTerrainCell(cell) && !isMultiplierGateCell))
                    {
                        continue;
                    }

                    bool hasTop = HasTile(row + 1, column);
                    bool hasRight = HasTile(row, column + 1);
                    bool hasBottom = HasTile(row - 1, column);
                    bool hasLeft = HasTile(row, column - 1);

                    bool topLeft = hasTop && hasLeft && HasTile(row + 1, column - 1);
                    bool topRight = hasTop && hasRight && HasTile(row + 1, column + 1);
                    bool bottomLeft = hasBottom && hasLeft && HasTile(row - 1, column - 1);
                    bool bottomRight = hasBottom && hasRight && HasTile(row - 1, column + 1);

                    if (!topLeft && !topRight && !bottomLeft && !bottomRight)
                    {
                        continue;
                    }

                    Vector3 cellCenterWorldPosition = CalculateCellWorldPosition(
                        rowCount,
                        columnCount,
                        row,
                        column);
                    if (!TrySpawnInnerCornerConnector(
                            cellCenterWorldPosition,
                            topLeft,
                            topRight,
                            bottomLeft,
                            bottomRight,
                            $"Inner Corner Connector [{row},{column}]"))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TrySpawnNeighborConnector(
            GameObject connectorPrefab,
            BoardTileView firstTile,
            BoardTileView secondTile,
            List<GameObject> runtimeConnectors,
            float connectorZ,
            string instanceName)
        {
            GameObject connector = Instantiate(connectorPrefab, tilesParent);
            if (connector == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' failed to instantiate '{instanceName}'.", this);
                return false;
            }

            connector.name = instanceName;
            Vector3 midpointWorldPosition = (firstTile.transform.position + secondTile.transform.position) * 0.5f;
            ApplyWorldPositionWithLocalZ(connector.transform, midpointWorldPosition, connectorZ);
            ApplyGridRotationAndScale(connector.transform);
            runtimeConnectors.Add(connector);
            return true;
        }

        private bool TrySpawnCornerConnector(
            BoardTileView currentTile,
            BoardTileView rightTile,
            BoardTileView verticalTile,
            BoardTileView diagonalTile,
            string instanceName)
        {
            GameObject connector = Instantiate(cornerConnectorPrefab, tilesParent);
            if (connector == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' failed to instantiate '{instanceName}'.", this);
                return false;
            }

            connector.name = instanceName;
            Vector3 centerWorldPosition =
                (currentTile.transform.position +
                 rightTile.transform.position +
                 verticalTile.transform.position +
                 diagonalTile.transform.position) * 0.25f;
            ApplyWorldPositionWithLocalZ(connector.transform, centerWorldPosition, cornerConnectorZ);
            ApplyGridRotationAndScale(connector.transform);
            cornerConnectors.Add(connector);
            return true;
        }

        private bool TrySpawnInnerCornerConnector(
            Vector3 cellCenterWorldPosition,
            bool topLeft,
            bool topRight,
            bool bottomLeft,
            bool bottomRight,
            string instanceName)
        {
            BoardTileInnerCornerConnectorView connector = Instantiate(innerCornerConnectorPrefab, tilesParent);
            if (connector == null)
            {
                Debug.LogError($"{nameof(BoardTileController)} on '{name}' failed to instantiate '{instanceName}'.", this);
                return false;
            }

            connector.name = instanceName;
            ApplyWorldPositionWithLocalZ(connector.transform, cellCenterWorldPosition, innerCornerConnectorZ);
            ApplyGridRotationAndScale(connector.transform);
            connector.ApplyCorners(topLeft, topRight, bottomLeft, bottomRight);
            innerCornerConnectors.Add(connector.gameObject);
            return true;
        }

        private bool HasTile(int row, int column)
        {
            return TryGetTile(new Vector2Int(row, column), out _);
        }

        private void ApplyCellTransform(
            Transform tileTransform,
            int rowCount,
            int columnCount,
            int row,
            int column)
        {
            Vector3 worldPosition = CalculateCellWorldPosition(rowCount, columnCount, row, column);

            ApplyWorldPositionWithLocalZ(tileTransform, worldPosition, boardTileZ);
            ApplyGridRotationAndScale(tileTransform);
        }

        private Vector3 CalculateCellWorldPosition(
            int rowCount,
            int columnCount,
            int row,
            int column)
        {
            Transform gridRoot = placeholderGridController.GeneratedGridRoot;
            Vector3 gridLocalPosition = placeholderGridController.CalculateCellLocalPosition(
                rowCount,
                columnCount,
                row,
                column);
            return gridRoot.TransformPoint(gridLocalPosition);
        }

        private void ApplyWorldPositionWithLocalZ(Transform instanceTransform, Vector3 worldPosition, float localZ)
        {
            Vector3 localPosition = tilesParent.InverseTransformPoint(worldPosition);
            localPosition.z = localZ;
            instanceTransform.localPosition = localPosition;
        }

        private void ApplyGridRotationAndScale(Transform instanceTransform)
        {
            Transform gridRoot = placeholderGridController.GeneratedGridRoot;
            Vector3 authoredScale = instanceTransform.localScale;
            Quaternion authoredRotation = instanceTransform.localRotation;

            instanceTransform.localRotation = Quaternion.Inverse(tilesParent.rotation) * gridRoot.rotation * authoredRotation;

            Vector3 gridWorldScale = gridRoot.lossyScale;
            Vector3 tilesParentWorldScale = tilesParent.lossyScale;
            instanceTransform.localScale = new Vector3(
                authoredScale.x * SafeScaleRatio(gridWorldScale.x, tilesParentWorldScale.x),
                authoredScale.y * SafeScaleRatio(gridWorldScale.y, tilesParentWorldScale.y),
                authoredScale.z * SafeScaleRatio(gridWorldScale.z, tilesParentWorldScale.z));
        }

        private void DestroyConnectorInstances(List<GameObject> runtimeConnectors)
        {
            for (int i = 0; i < runtimeConnectors.Count; i++)
            {
                GameObject connector = runtimeConnectors[i];
                if (connector == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(connector);
                }
                else
                {
                    DestroyImmediate(connector);
                }
            }

            runtimeConnectors.Clear();
        }

        private static float SafeScaleRatio(float sourceScale, float targetScale)
        {
            return Mathf.Approximately(targetScale, 0f) ? 1f : sourceScale / targetScale;
        }

        internal static bool IsTerrainCell(LevelCellData cell)
        {
            return cell != null &&
                   (cell.CellType == LevelCellType.Empty || cell.CellType == LevelCellType.Blocked);
        }

    }
}

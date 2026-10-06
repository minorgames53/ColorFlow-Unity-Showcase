using UnityEngine;

namespace Gameplay.Levels
{
    public enum ArrowDirection
    {
        None = -1,
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3
    }

    public static class ArrowDirectionUtility
    {
        public static bool TryGetDelta(ArrowDirection direction, out Vector2Int delta)
        {
            switch (direction)
            {
                case ArrowDirection.Up:
                    delta = new Vector2Int(1, 0);
                    return true;
                case ArrowDirection.Right:
                    delta = new Vector2Int(0, 1);
                    return true;
                case ArrowDirection.Down:
                    delta = new Vector2Int(-1, 0);
                    return true;
                case ArrowDirection.Left:
                    delta = new Vector2Int(0, -1);
                    return true;
                default:
                    delta = default;
                    return false;
            }
        }
    }
}

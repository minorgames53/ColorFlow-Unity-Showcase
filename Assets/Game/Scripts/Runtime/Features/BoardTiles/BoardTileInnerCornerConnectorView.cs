using UnityEngine;

namespace Gameplay.BoardTiles
{
    [DisallowMultipleComponent]
    public sealed class BoardTileInnerCornerConnectorView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer innerCornerTopLeft;
        [SerializeField] private SpriteRenderer innerCornerTopRight;
        [SerializeField] private SpriteRenderer innerCornerBottomLeft;
        [SerializeField] private SpriteRenderer innerCornerBottomRight;

        public bool HasRequiredReferences =>
            innerCornerTopLeft != null &&
            innerCornerTopRight != null &&
            innerCornerBottomLeft != null &&
            innerCornerBottomRight != null;

        public void ApplyCorners(
            bool topLeft,
            bool topRight,
            bool bottomLeft,
            bool bottomRight)
        {
            SetActive(innerCornerTopLeft, topLeft);
            SetActive(innerCornerTopRight, topRight);
            SetActive(innerCornerBottomLeft, bottomLeft);
            SetActive(innerCornerBottomRight, bottomRight);
        }

        private static void SetActive(SpriteRenderer corner, bool isActive)
        {
            if (corner != null)
            {
                corner.gameObject.SetActive(isActive);
            }
        }
    }
}

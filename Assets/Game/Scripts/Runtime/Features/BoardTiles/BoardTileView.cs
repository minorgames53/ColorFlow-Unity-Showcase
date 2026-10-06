using UnityEngine;

namespace Gameplay.BoardTiles
{
    [DisallowMultipleComponent,SelectionBase]
    public sealed class BoardTileView : MonoBehaviour
    {
        [Header("Fill")]
        [SerializeField] private SpriteRenderer fill;

        [Header("Edges")]
        [SerializeField] private SpriteRenderer edgeTop;
        [SerializeField] private SpriteRenderer edgeRight;
        [SerializeField] private SpriteRenderer edgeBottom;
        [SerializeField] private SpriteRenderer edgeLeft;

        [Header("Outer Corners")]
        [SerializeField] private SpriteRenderer outerCornerTopLeft;
        [SerializeField] private SpriteRenderer outerCornerTopRight;
        [SerializeField] private SpriteRenderer outerCornerBottomLeft;
        [SerializeField] private SpriteRenderer outerCornerBottomRight;

        [Header("Inner Corners")]
        [SerializeField] private SpriteRenderer innerCornerTopLeft;
        [SerializeField] private SpriteRenderer innerCornerTopRight;
        [SerializeField] private SpriteRenderer innerCornerBottomLeft;
        [SerializeField] private SpriteRenderer innerCornerBottomRight;

        public SpriteRenderer Fill => fill;
        public SpriteRenderer EdgeTop => edgeTop;
        public SpriteRenderer EdgeRight => edgeRight;
        public SpriteRenderer EdgeBottom => edgeBottom;
        public SpriteRenderer EdgeLeft => edgeLeft;
        public SpriteRenderer OuterCornerTopLeft => outerCornerTopLeft;
        public SpriteRenderer OuterCornerTopRight => outerCornerTopRight;
        public SpriteRenderer OuterCornerBottomLeft => outerCornerBottomLeft;
        public SpriteRenderer OuterCornerBottomRight => outerCornerBottomRight;
        public SpriteRenderer InnerCornerTopLeft => innerCornerTopLeft;
        public SpriteRenderer InnerCornerTopRight => innerCornerTopRight;
        public SpriteRenderer InnerCornerBottomLeft => innerCornerBottomLeft;
        public SpriteRenderer InnerCornerBottomRight => innerCornerBottomRight;

        public bool HasRequiredReferences =>
            fill != null &&
            edgeTop != null &&
            edgeRight != null &&
            edgeBottom != null &&
            edgeLeft != null &&
            outerCornerTopLeft != null &&
            outerCornerTopRight != null &&
            outerCornerBottomLeft != null &&
            outerCornerBottomRight != null;
    }
}

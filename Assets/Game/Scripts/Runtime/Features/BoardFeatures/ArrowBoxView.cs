using Gameplay.Levels;
using UnityEngine;

namespace Gameplay.BoardFeatures.ArrowBoxes
{
    [DisallowMultipleComponent]
    public sealed class ArrowBoxView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer arrowStrokeRenderer;

        public bool HasRequiredReferences
        {
            get
            {
                ResolveReferences();
                return arrowStrokeRenderer != null;
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        public bool Initialize(ArrowDirection direction, Color strokeColor)
        {
            ResolveReferences();
            if (arrowStrokeRenderer == null || !ArrowDirectionUtility.TryGetDelta(direction, out _))
            {
                Debug.LogError($"{nameof(ArrowBoxView)} on '{name}' requires an arrow_stroke SpriteRenderer and a valid direction.", this);
                return false;
            }

            transform.localRotation = Quaternion.Euler(0f, 0f, GetRotationDegrees(direction));
            arrowStrokeRenderer.color = strokeColor;
            return true;
        }

        private static float GetRotationDegrees(ArrowDirection direction)
        {
            switch (direction)
            {
                case ArrowDirection.Up:
                    return 90f;
                case ArrowDirection.Down:
                    return -90f;
                case ArrowDirection.Left:
                    return 180f;
                default:
                    return 0f;
            }
        }

        private void ResolveReferences()
        {
            if (arrowStrokeRenderer != null)
            {
                return;
            }

            Transform stroke = transform.Find("arrow_stroke");
            arrowStrokeRenderer = stroke != null ? stroke.GetComponent<SpriteRenderer>() : null;
        }
    }
}

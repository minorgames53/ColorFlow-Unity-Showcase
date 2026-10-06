using UnityEngine;

namespace Gameplay.BoardFeatures.ConnectedBoxes
{
    /// <summary>Renders one level-defined connection between two adjacent SourceBoxes.</summary>
    [DisallowMultipleComponent]
    public sealed class ConnectionController : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer part1SpriteRenderer;
        [SerializeField] private SpriteRenderer part2SpriteRenderer;
        private Transform firstSource;
        private Transform secondSource;

        public bool HasRequiredReferences => part1SpriteRenderer != null && part2SpriteRenderer != null;

        public bool Initialize(Vector3 firstSourcePosition, Vector3 secondSourcePosition, Color firstTint, Color secondTint)
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError($"{nameof(ConnectionController)} on '{name}' is missing Part 1/Sprite or Part 2/Sprite reference.", this);
                return false;
            }

            Vector3 direction = secondSourcePosition - firstSourcePosition;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                Debug.LogError($"{nameof(ConnectionController)} on '{name}' cannot connect SourceBoxes at the same position.", this);
                return false;
            }

            transform.position = Vector3.Lerp(firstSourcePosition, secondSourcePosition, 0.5f);
            transform.rotation = Quaternion.FromToRotation(Vector3.right, direction.normalized);
            part1SpriteRenderer.color = firstTint;
            part2SpriteRenderer.color = secondTint;
            return true;
        }

        public bool Initialize(Transform firstSourceTransform, Transform secondSourceTransform, Color firstTint, Color secondTint)
        {
            if (firstSourceTransform == null || secondSourceTransform == null ||
                !Initialize(firstSourceTransform.position, secondSourceTransform.position, firstTint, secondTint))
            {
                return false;
            }

            firstSource = firstSourceTransform;
            secondSource = secondSourceTransform;
            return true;
        }

        private void LateUpdate()
        {
            if (firstSource == null || secondSource == null)
            {
                return;
            }

            Vector3 direction = secondSource.position - firstSource.position;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            transform.position = Vector3.Lerp(firstSource.position, secondSource.position, 0.5f);
            transform.rotation = Quaternion.FromToRotation(Vector3.right, direction.normalized);
        }
    }
}

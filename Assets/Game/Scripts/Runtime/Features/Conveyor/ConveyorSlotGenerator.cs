using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Gameplay.Conveyor
{
    public sealed class ConveyorSlotGenerator : MonoBehaviour
    {
        private const int SlotCount = 30;

        [Header("References")]
        [SerializeField] private Transform slotRoot;
        [SerializeField] private GameObject topPlaceholderPrefab;
        [SerializeField] private GameObject bottomPlaceholderPrefab;

        [Header("Placeholder Pattern")]
        [SerializeField, Min(1)] private int placeholderRepeatCount = 3;

        [Header("Shape")]
        [SerializeField] private Vector2 centerOffset;
        [SerializeField, Min(0.01f)] private float straightLength = 5f;
        [SerializeField, Min(0.01f)] private float radius = 1f;
        [SerializeField, Min(0f)] private float startDistance;

        [Header("Slot Settings")]
        [SerializeField, Min(0f)] private float colliderRadius = 0.2f;
        [SerializeField] private bool showGizmos = true;

        private void OnValidate()
        {
            straightLength = Mathf.Max(0.01f, straightLength);
            radius = Mathf.Max(0.01f, radius);
            colliderRadius = Mathf.Max(0f, colliderRadius);
            startDistance = Mathf.Max(0f, startDistance);
            placeholderRepeatCount = Mathf.Max(1, placeholderRepeatCount);
        }

        [Button("Spawn Slots")]
        public void SpawnSlots()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.LogWarning($"{nameof(ConveyorSlotGenerator)} on '{name}' cannot spawn slots in Play Mode.", this);
                return;
            }

            if (!ValidateSpawnInput())
            {
                return;
            }

            Undo.SetCurrentGroupName("Spawn Conveyor Slots");
            int undoGroup = Undo.GetCurrentGroup();

            ClearExistingSlots();
            CreateSlots();
            MarkDirty();

            Undo.CollapseUndoOperations(undoGroup);
#else
            Debug.LogWarning($"{nameof(ConveyorSlotGenerator)} on '{name}' can only spawn slots in the Unity Editor.", this);
#endif
        }

#if UNITY_EDITOR
        private bool ValidateSpawnInput()
        {
            if (slotRoot == null)
            {
                Debug.LogError($"{nameof(ConveyorSlotGenerator)} on '{name}' cannot spawn slots because Slot Root is missing.", this);
                return false;
            }

            if (straightLength <= 0f)
            {
                Debug.LogError($"{nameof(ConveyorSlotGenerator)} on '{name}' cannot spawn slots because Straight Length must be greater than zero.", this);
                return false;
            }

            if (radius <= 0f)
            {
                Debug.LogError($"{nameof(ConveyorSlotGenerator)} on '{name}' cannot spawn slots because Radius must be greater than zero.", this);
                return false;
            }

            if (topPlaceholderPrefab == null)
            {
                Debug.LogError($"{nameof(ConveyorSlotGenerator)} on '{name}' cannot spawn slots because Top Placeholder Prefab is missing.", this);
                return false;
            }

            if (bottomPlaceholderPrefab == null)
            {
                Debug.LogError($"{nameof(ConveyorSlotGenerator)} on '{name}' cannot spawn slots because Bottom Placeholder Prefab is missing.", this);
                return false;
            }

            float slotSpacing = GetTotalLength() / SlotCount;
            if (slotSpacing < colliderRadius * 2f)
            {
                Debug.LogWarning($"{nameof(ConveyorSlotGenerator)} on '{name}' slot spacing is {slotSpacing:0.###}, but collider diameter is {(colliderRadius * 2f):0.###}. Slot colliders will overlap.", this);
            }

            return true;
        }

        private void ClearExistingSlots()
        {
            for (int i = slotRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = slotRoot.GetChild(i);
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private void CreateSlots()
        {
            float totalLength = GetTotalLength();
            float slotSpacing = totalLength / SlotCount;

            for (int i = 0; i < SlotCount; i++)
            {
                GameObject slot = new GameObject($"Slot_{i:00}");
                Undo.RegisterCreatedObjectUndo(slot, "Create Conveyor Slot");
                Undo.SetTransformParent(slot.transform, slotRoot, "Parent Conveyor Slot");

                Transform slotTransform = slot.transform;
                slotTransform.SetSiblingIndex(i);
                slotTransform.localPosition = EvaluatePositionAtDistance(startDistance + i * slotSpacing);
                slotTransform.localRotation = Quaternion.identity;
                slotTransform.localScale = Vector3.one;

                CircleCollider2D circleCollider = Undo.AddComponent<CircleCollider2D>(slot);
                circleCollider.radius = colliderRadius;
                circleCollider.isTrigger = true;
                circleCollider.offset = Vector2.zero;

                Transform marbleAnchor = CreatePlaceholder(slotTransform, i);
                ConveyorSlot conveyorSlot = Undo.AddComponent<ConveyorSlot>(slot);
                conveyorSlot.Initialize(i, marbleAnchor);
                EditorUtility.SetDirty(conveyorSlot);
            }
        }

        private Transform CreatePlaceholder(Transform slotTransform, int slotIndex)
        {
            GameObject prefab = ShouldUseFirstPlaceholder(slotIndex)
                ? topPlaceholderPrefab
                : bottomPlaceholderPrefab;

            GameObject placeholder = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slotTransform);
            if (placeholder == null)
            {
                Debug.LogError($"{nameof(ConveyorSlotGenerator)} on '{name}' could not instantiate placeholder prefab '{prefab.name}'.", this);
                return null;
            }

            Undo.RegisterCreatedObjectUndo(placeholder, "Create Conveyor Placeholder");
            placeholder.transform.localPosition = Vector3.zero;
            placeholder.transform.localRotation = Quaternion.identity;

            Transform marbleAnchor = placeholder.transform.Find("MarbleAnchor");
            if (marbleAnchor == null)
            {
                Debug.LogError($"{nameof(ConveyorSlotGenerator)} on '{name}' spawned placeholder '{placeholder.name}' without a MarbleAnchor child.", this);
            }

            return marbleAnchor;
        }

        private bool ShouldUseFirstPlaceholder(int slotIndex)
        {
            int groupIndex = slotIndex / placeholderRepeatCount;
            return groupIndex % 2 == 0;
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(slotRoot);

            if (slotRoot.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(slotRoot.gameObject.scene);
            }
        }
#endif

        private Vector3 EvaluatePositionAtDistance(float distance)
        {
            float normalizedDistance = NormalizeDistance(distance);
            float halfStraight = straightLength * 0.5f;

            if (normalizedDistance <= straightLength)
            {
                float t = normalizedDistance / straightLength;
                return new Vector3(
                    Mathf.Lerp(halfStraight, -halfStraight, t),
                    radius,
                    0f) + (Vector3)centerOffset;
            }

            normalizedDistance -= straightLength;
            float halfCircleLength = Mathf.PI * radius;
            if (normalizedDistance <= halfCircleLength)
            {
                float angle = Mathf.Lerp(90f, 270f, normalizedDistance / halfCircleLength) * Mathf.Deg2Rad;
                return new Vector3(
                    -halfStraight + Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f) + (Vector3)centerOffset;
            }

            normalizedDistance -= halfCircleLength;
            if (normalizedDistance <= straightLength)
            {
                float t = normalizedDistance / straightLength;
                return new Vector3(
                    Mathf.Lerp(-halfStraight, halfStraight, t),
                    -radius,
                    0f) + (Vector3)centerOffset;
            }

            normalizedDistance -= straightLength;
            float rightAngle = Mathf.Lerp(270f, 450f, normalizedDistance / halfCircleLength) * Mathf.Deg2Rad;
            return new Vector3(
                halfStraight + Mathf.Cos(rightAngle) * radius,
                Mathf.Sin(rightAngle) * radius,
                0f) + (Vector3)centerOffset;
        }

        private float NormalizeDistance(float distance)
        {
            float totalLength = GetTotalLength();
            if (totalLength <= 0f)
            {
                return 0f;
            }

            distance %= totalLength;
            if (distance < 0f)
            {
                distance += totalLength;
            }

            return distance;
        }

        private float GetTotalLength()
        {
            return 2f * straightLength + 2f * Mathf.PI * radius;
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos)
            {
                return;
            }

            DrawPathGizmos();
            DrawSlotGizmos();
        }

        private void DrawPathGizmos()
        {
            const int SegmentCount = 96;
            Gizmos.color = Color.cyan;

            Vector3 previousPosition = transform.TransformPoint(EvaluatePositionAtDistance(0f));
            for (int i = 1; i <= SegmentCount; i++)
            {
                float distance = GetTotalLength() * i / SegmentCount;
                Vector3 nextPosition = transform.TransformPoint(EvaluatePositionAtDistance(distance));
                Gizmos.DrawLine(previousPosition, nextPosition);
                previousPosition = nextPosition;
            }
        }

        private void DrawSlotGizmos()
        {
            float slotSpacing = GetTotalLength() / SlotCount;

            for (int i = 0; i < SlotCount; i++)
            {
                Vector3 localPosition = EvaluatePositionAtDistance(startDistance + i * slotSpacing);
                Vector3 worldPosition = transform.TransformPoint(localPosition);
                Gizmos.color = i == 0 ? Color.yellow : Color.white;
                Gizmos.DrawWireSphere(worldPosition, Mathf.Max(0.04f, colliderRadius));

                if (i % 5 != 0)
                {
                    continue;
                }

                Vector3 nextLocalPosition = EvaluatePositionAtDistance(startDistance + (i + 1) * slotSpacing);
                Vector3 nextWorldPosition = transform.TransformPoint(nextLocalPosition);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(worldPosition, nextWorldPosition);
            }
        }
    }
}

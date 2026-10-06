using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif
using UnityEngine.Serialization;
using System.Collections.Generic;

namespace Gameplay.SourceBoxes
{
    [SelectionBase]
    public sealed class Marble : MonoBehaviour
    {
        [FormerlySerializedAs("body")]
        [SerializeField] private Rigidbody2D marbleRigidbody;
        [SerializeField] private CircleCollider2D circleCollider;
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private GameObject outlineObject;
        [SerializeField] private GameObject shadowObject;

        private MarbleColorId colorId = MarbleColorId.None;
        private bool isReleased;
        private bool isTransferringToTarget;
        private bool isOwnedByUfo;
        private int sourceCellIndex = -1;
        private SpriteRenderer outlineRenderer;
        private readonly HashSet<int> processedMultiplierGateIds = new HashSet<int>();

        public MarbleColorId ColorId => colorId;
        public bool IsReleased => isReleased;
        public bool IsTransferringToTarget => isTransferringToTarget;
        public bool IsOwnedByUfo => isOwnedByUfo;
        public bool CanBeOwnedByUfo => !isTransferringToTarget && !isOwnedByUfo;
        public Rigidbody2D Rigidbody => marbleRigidbody;
        public CircleCollider2D CircleCollider => circleCollider;
        public float ColliderDiameter => CalculateColliderDiameter();
        public int SourceCellIndex => sourceCellIndex;
        public int VisualSortingOrder => visualRenderer != null ? visualRenderer.sortingOrder : 0;

        private void Awake()
        {
            DisablePhysicsSafely();
            SetOutlineColor(Color.black);
            SetOutlineActive(false);
            SetShadowActive(false);
        }

        private void Reset()
        {
            marbleRigidbody = GetComponent<Rigidbody2D>();
            circleCollider = GetComponent<CircleCollider2D>();
            visualRenderer = GetComponentInChildren<SpriteRenderer>(true);
            outlineObject = transform.Find("Outline")?.gameObject;
            shadowObject = transform.Find("Shadow")?.gameObject;
        }

        public void Initialize(MarbleColorId newColorId, Sprite marbleSprite, int newSourceCellIndex = -1)
        {
            if (!ValidateReferences())
            {
                return;
            }

            if (!MarbleColorCatalog.IsGameplayColor(newColorId))
            {
                Debug.LogError($"{nameof(Marble)} on '{name}' cannot initialize with color '{newColorId}'.", this);
                return;
            }

            if (marbleSprite == null)
            {
                Debug.LogError($"{nameof(Marble)} on '{name}' cannot initialize because the Marble sprite is missing.", this);
                return;
            }

            colorId = newColorId;
            isReleased = false;
            isTransferringToTarget = false;
            isOwnedByUfo = false;
            sourceCellIndex = newSourceCellIndex;
            processedMultiplierGateIds.Clear();
#if UNITY_EDITOR
            MarbleDebugTracker.Register(this);
#endif
            ApplyVisualSprites(marbleSprite, false);
            circleCollider.enabled = true;
            SetOutlineColor(Color.black);
            SetOutlineActive(false);
            SetShadowActive(false);
            ResetPhysicsState();
        }

        public bool HasProcessedMultiplierGate(int gateRuntimeId)
        {
            return processedMultiplierGateIds.Contains(gateRuntimeId);
        }

        public void MarkMultiplierGateProcessed(int gateRuntimeId)
        {
            processedMultiplierGateIds.Add(gateRuntimeId);
        }

        public void CopyMultiplierGateStateFrom(Marble source)
        {
            processedMultiplierGateIds.Clear();
            if (source == null)
            {
                return;
            }

            foreach (int gateId in source.processedMultiplierGateIds)
            {
                processedMultiplierGateIds.Add(gateId);
            }
        }

        public void ApplyVisual(Sprite marbleSprite)
        {
            if (!ValidateReferences())
            {
                return;
            }

            if (marbleSprite == null)
            {
                Debug.LogError($"{nameof(Marble)} on '{name}' cannot apply visual because the Marble sprite is missing.", this);
                return;
            }

            ApplyVisualSprites(marbleSprite, true);
        }

        private void ApplyVisualSprites(Sprite marbleSprite, bool refreshRenderers)
        {
            visualRenderer.sprite = marbleSprite;
            if (refreshRenderers)
            {
                RefreshRenderer(visualRenderer);
            }
        }

        private static void RefreshRenderer(SpriteRenderer spriteRenderer)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            bool wasEnabled = spriteRenderer.enabled;
            spriteRenderer.enabled = false;
            spriteRenderer.enabled = wasEnabled;
        }

        public void PrepareForReleaseAnimation()
        {
            if (!ValidateReferences())
            {
                return;
            }

            isReleased = false;
            isTransferringToTarget = false;
            isOwnedByUfo = false;
            circleCollider.enabled = true;
            SetOutlineColor(Color.black);
            SetOutlineActive(false);
            SetShadowActive(false);
            ResetPhysicsState();
        }

        public void SetOutlineActive(bool isActive)
        {
            if (outlineObject != null)
            {
                outlineObject.SetActive(isActive);
            }
        }

        public void SetOutlineColor(Color color)
        {
            if (outlineRenderer == null && outlineObject != null)
            {
                outlineObject.TryGetComponent(out outlineRenderer);
            }

            if (outlineRenderer != null)
            {
                outlineRenderer.color = color;
            }
        }

        public void SetVisualSortingOrder(int sortingOrder)
        {
            if (visualRenderer != null)
            {
                visualRenderer.sortingOrder = sortingOrder;
            }
        }

        public void SetRendererSortingOrders(int visualSortingOrder, int outlineSortingOrder)
        {
            if (visualRenderer != null)
            {
                visualRenderer.sortingOrder = visualSortingOrder;
            }

            if (outlineRenderer == null && outlineObject != null)
            {
                outlineObject.TryGetComponent(out outlineRenderer);
            }

            if (outlineRenderer != null)
            {
                outlineRenderer.sortingOrder = outlineSortingOrder;
            }
        }

        public void SetShadowActive(bool isActive)
        {
            if (shadowObject != null)
            {
                shadowObject.SetActive(isActive);
            }
        }

        public void Release()
        {
            if (isReleased)
            {
                return;
            }

            if (!ValidateReferences())
            {
                return;
            }

            isReleased = true;
            SetOutlineActive(true);
            SetShadowActive(true);
            marbleRigidbody.position = transform.position;
            marbleRigidbody.linearVelocity = Vector2.zero;
            marbleRigidbody.angularVelocity = 0f;
            marbleRigidbody.simulated = true;
            marbleRigidbody.WakeUp();
            visualRenderer.sortingOrder = 2;
#if UNITY_EDITOR
            MarbleDebugTracker.ReleasedToField(this);
#endif
        }

        public void PrepareForConveyorControl()
        {
            if (!ValidateReferences())
            {
                return;
            }

            isReleased = true;
            isTransferringToTarget = false;
            isOwnedByUfo = false;
            SetShadowActive(false);
            marbleRigidbody.position = transform.position;
            ResetPhysicsState();
        }

        public void PrepareForRecoveryControl()
        {
            if (!ValidateReferences())
            {
                return;
            }

            isReleased = false;
            isTransferringToTarget = false;
            isOwnedByUfo = false;
            circleCollider.enabled = false;
            SetOutlineActive(false);
            SetShadowActive(false);
            marbleRigidbody.position = transform.position;
            ResetPhysicsState();
        }

        public bool TryBeginTargetTransfer()
        {
            if (isTransferringToTarget || isOwnedByUfo)
            {
                return false;
            }

            isTransferringToTarget = true;
            return true;
        }

        public bool TryBeginTargetTransferFromUfo()
        {
            if (isTransferringToTarget || !isOwnedByUfo)
            {
                return false;
            }

            isOwnedByUfo = false;
            isTransferringToTarget = true;
            return true;
        }

        public void RestoreUfoOwnershipAfterCancelledTargetTransfer()
        {
            isTransferringToTarget = false;
            isOwnedByUfo = true;
        }

        public void CancelTargetTransfer()
        {
            isTransferringToTarget = false;
        }

        public bool TryBeginUfoTransfer()
        {
            if (!CanBeOwnedByUfo)
            {
                return false;
            }

            isOwnedByUfo = true;
            SetOutlineActive(true);
            return true;
        }

        public void CancelUfoTransfer()
        {
            isOwnedByUfo = false;
        }

        public void PrepareForUfoControl()
        {
            if (!ValidateReferences())
            {
                return;
            }

            isReleased = true;
            isTransferringToTarget = false;
            isOwnedByUfo = true;
            circleCollider.enabled = false;
            SetOutlineActive(true);
            SetShadowActive(false);
            marbleRigidbody.position = transform.position;
            ResetPhysicsState();
        }

        public void PrepareForTargetTransfer()
        {
            if (!ValidateReferences())
            {
                return;
            }

            isReleased = true;
            marbleRigidbody.position = transform.position;
            ResetPhysicsState();
        }

        public void EnableFieldPhysics()
        {
            if (!ValidateReferences())
            {
                return;
            }

            isReleased = true;
            marbleRigidbody.position = transform.position;
            marbleRigidbody.linearVelocity = Vector2.zero;
            marbleRigidbody.angularVelocity = 0f;
            marbleRigidbody.simulated = true;
            marbleRigidbody.WakeUp();
        }

        private void ResetPhysicsState()
        {
            marbleRigidbody.linearVelocity = Vector2.zero;
            marbleRigidbody.angularVelocity = 0f;
            marbleRigidbody.simulated = false;
            marbleRigidbody.Sleep();
        }

#if UNITY_EDITOR
        private void OnEnable() => MarbleDebugTracker.Lifecycle(this, false, "Enabled");
        private void OnDisable() => MarbleDebugTracker.Lifecycle(this, false, "Disabled");
        private void OnDestroy() => MarbleDebugTracker.Lifecycle(this, true, "Marble destroyed");
#endif

        private void DisablePhysicsSafely()
        {
            if (marbleRigidbody == null)
            {
                Debug.LogError($"{nameof(Marble)} on '{name}' is missing Rigidbody2D reference.", this);
                return;
            }

            ResetPhysicsState();
        }

        private float CalculateColliderDiameter()
        {
            if (circleCollider == null)
            {
                return 0f;
            }

            Vector3 boundsSize = circleCollider.bounds.size;
            float boundsDiameter = Mathf.Max(boundsSize.x, boundsSize.y);
            if (boundsDiameter > 0f)
            {
                return boundsDiameter;
            }

            Vector3 scale = transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            return circleCollider.radius * 2f * maxScale;
        }

        private bool ValidateReferences()
        {
            if (marbleRigidbody == null)
            {
                Debug.LogError($"{nameof(Marble)} on '{name}' is missing Rigidbody2D reference.", this);
                return false;
            }

            if (circleCollider == null)
            {
                Debug.LogError($"{nameof(Marble)} on '{name}' is missing CircleCollider2D reference.", this);
                return false;
            }

            if (visualRenderer == null)
            {
                Debug.LogError($"{nameof(Marble)} on '{name}' is missing Visual SpriteRenderer reference.", this);
                return false;
            }

            return true;
        }
    }
}

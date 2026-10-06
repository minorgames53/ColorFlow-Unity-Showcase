using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.SourceBoxes
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class MarbleMagnetZone : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [SerializeField] private Collider2D zoneCollider;
        [SerializeField] private LayerMask marbleLayerMask;

        [Header("Horizontal Attraction")]
        [SerializeField, Min(0f)] private float attractionStrength = 10f;
        [SerializeField, Min(0f)] private float horizontalDamping = 3f;
        [SerializeField, Min(0f)] private float maximumForce = 10f;

        [Header("Distribution")]
        [Tooltip("Misketlerin tek çizgide üst üste binmesini engelleyen yatay dağılım.")]
        [SerializeField, Min(0f)] private float centerSpread = 0.08f;

        [Tooltip("Merkeze yeterince yaklaşınca çekimin duracağı mesafe.")]
        [SerializeField, Min(0f)] private float deadZone = 0.025f;

        #endregion

        #region Private Fields

        private readonly List<Rigidbody2D> trackedBodies = new();
        private readonly Dictionary<Rigidbody2D, float> bodyOffsets = new();

        #endregion

        #region Unity Lifecycle

        private void Reset()
        {
            zoneCollider = GetComponent<Collider2D>();

            if (zoneCollider != null)
            {
                zoneCollider.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (zoneCollider == null)
            {
                zoneCollider = GetComponent<Collider2D>();
            }

            if (zoneCollider == null)
            {
                Debug.LogError(
                    $"{nameof(MarbleMagnetZone)} on '{name}' requires a Collider2D.",
                    this);

                enabled = false;
                return;
            }

            if (!zoneCollider.isTrigger)
            {
                Debug.LogError(
                    $"{nameof(MarbleMagnetZone)} collider on '{name}' must be marked as Trigger.",
                    this);

                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            ApplyMagnetForces();
        }

        private void OnDisable()
        {
            trackedBodies.Clear();
            bodyOffsets.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Rigidbody2D body = other.attachedRigidbody;

            if (!CanTrack(body))
            {
                return;
            }

            if (bodyOffsets.ContainsKey(body))
            {
                return;
            }

            trackedBodies.Add(body);
            bodyOffsets.Add(body, CalculateStableOffset(body));
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            Rigidbody2D body = other.attachedRigidbody;

            if (body == null)
            {
                return;
            }

            trackedBodies.Remove(body);
            bodyOffsets.Remove(body);
        }

        private void OnValidate()
        {
            attractionStrength = Mathf.Max(0f, attractionStrength);
            horizontalDamping = Mathf.Max(0f, horizontalDamping);
            maximumForce = Mathf.Max(0f, maximumForce);
            centerSpread = Mathf.Max(0f, centerSpread);
            deadZone = Mathf.Max(0f, deadZone);
        }

        #endregion

        #region Private Methods

        private void ApplyMagnetForces()
        {
            if (zoneCollider == null)
            {
                return;
            }

            float centerX = zoneCollider.bounds.center.x;

            for (int index = trackedBodies.Count - 1; index >= 0; index--)
            {
                Rigidbody2D body = trackedBodies[index];

                if (body == null)
                {
                    RemoveAt(index, body);
                    continue;
                }

                if (!body.simulated || body.bodyType != RigidbodyType2D.Dynamic)
                {
                    continue;
                }

                float offset = bodyOffsets.TryGetValue(body, out float storedOffset)
                    ? storedOffset
                    : 0f;

                float targetX = centerX + offset;
                float distanceX = targetX - body.position.x;

                float attraction = Mathf.Abs(distanceX) > deadZone
                    ? distanceX * attractionStrength
                    : 0f;

                float damping = -body.linearVelocity.x * horizontalDamping;
                float forceX = Mathf.Clamp(
                    attraction + damping,
                    -maximumForce,
                    maximumForce);

                body.AddForce(Vector2.right * forceX, ForceMode2D.Force);
            }
        }

        private bool CanTrack(Rigidbody2D body)
        {
            if (body == null)
            {
                return false;
            }

            int bodyLayerMask = 1 << body.gameObject.layer;

            return (marbleLayerMask.value & bodyLayerMask) != 0;
        }

        private float CalculateStableOffset(Rigidbody2D body)
        {
            if (centerSpread <= 0f)
            {
                return 0f;
            }

            uint hash = unchecked((uint)body.GetInstanceID());
            float normalizedValue = (hash % 1000u) / 999f;

            return Mathf.Lerp(
                -centerSpread,
                centerSpread,
                normalizedValue);
        }

        private void RemoveAt(int index, Rigidbody2D body)
        {
            trackedBodies.RemoveAt(index);

            if (body != null)
            {
                bodyOffsets.Remove(body);
            }
        }

        #endregion
    }
}
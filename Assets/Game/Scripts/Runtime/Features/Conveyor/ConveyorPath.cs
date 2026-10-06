using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Conveyor
{
    public sealed class ConveyorPath : MonoBehaviour
    {
        [SerializeField] private EdgeCollider2D pathSource;
        [SerializeField, Min(1)] private int samplesPerSegment = 16;
        [SerializeField, Min(0f)] private float entryDistance;

        private readonly List<Vector3> controlPoints = new List<Vector3>();
        private readonly List<Vector3> sampledPositions = new List<Vector3>();
        private readonly List<float> cumulativeDistances = new List<float>();
        private float length;
        private bool cacheValid;

        public float Length
        {
            get
            {
                EnsureCache();
                return length;
            }
        }

        public float EntryDistance
        {
            get
            {
                EnsureCache();
                return NormalizeDistance(entryDistance);
            }
        }

        private void Reset()
        {
            pathSource = GetComponent<EdgeCollider2D>();
            EnsurePathColliderIsTrigger();
        }

        private void Awake()
        {
            EnsurePathColliderIsTrigger();
            RebuildCache();
        }

        private void OnValidate()
        {
            samplesPerSegment = Mathf.Max(1, samplesPerSegment);
            entryDistance = Mathf.Max(0f, entryDistance);
            EnsurePathColliderIsTrigger();
            RebuildCache();
        }

        public float NormalizeDistance(float distance)
        {
            EnsureCache();
            if (length <= 0f)
            {
                return 0f;
            }

            distance %= length;
            if (distance < 0f)
            {
                distance += length;
            }

            return distance;
        }

        public Vector3 GetPositionAtDistance(float distance)
        {
            EnsureCache();
            if (sampledPositions.Count == 0)
            {
                return transform.position;
            }

            if (sampledPositions.Count == 1 || length <= 0f)
            {
                return sampledPositions[0];
            }

            float normalizedDistance = NormalizeDistance(distance);
            for (int i = 1; i < cumulativeDistances.Count; i++)
            {
                float segmentEndDistance = cumulativeDistances[i];
                if (normalizedDistance > segmentEndDistance)
                {
                    continue;
                }

                float segmentStartDistance = cumulativeDistances[i - 1];
                float segmentLength = segmentEndDistance - segmentStartDistance;
                float t = segmentLength <= 0f ? 0f : (normalizedDistance - segmentStartDistance) / segmentLength;
                return Vector3.Lerp(sampledPositions[i - 1], sampledPositions[i], t);
            }

            return sampledPositions[0];
        }

        public void RebuildCache()
        {
            cacheValid = false;
            controlPoints.Clear();
            sampledPositions.Clear();
            cumulativeDistances.Clear();
            length = 0f;

            if (pathSource == null || pathSource.pointCount < 2)
            {
                entryDistance = 0f;
                return;
            }

            Vector2[] points = pathSource.points;
            for (int i = 0; i < points.Length; i++)
            {
                controlPoints.Add(pathSource.transform.TransformPoint(points[i]));
            }

            RemoveDuplicateClosingPoint();

            if (controlPoints.Count < 2)
            {
                entryDistance = 0f;
                cacheValid = true;
                return;
            }

            for (int segmentIndex = 0; segmentIndex < controlPoints.Count; segmentIndex++)
            {
                for (int sampleIndex = 0; sampleIndex < samplesPerSegment; sampleIndex++)
                {
                    float t = sampleIndex / (float)samplesPerSegment;
                    AddSample(EvaluateClosedCatmullRom(segmentIndex, t));
                }
            }

            if (sampledPositions.Count > 1)
            {
                AddClosingSample();
            }

            ClampEntryDistanceToLength();
            cacheValid = true;
        }

        private void EnsureCache()
        {
            if (!cacheValid)
            {
                RebuildCache();
            }
        }

        private Vector3 EvaluateClosedCatmullRom(int segmentIndex, float t)
        {
            int pointCount = controlPoints.Count;
            Vector3 p0 = controlPoints[Mod(segmentIndex - 1, pointCount)];
            Vector3 p1 = controlPoints[segmentIndex];
            Vector3 p2 = controlPoints[Mod(segmentIndex + 1, pointCount)];
            Vector3 p3 = controlPoints[Mod(segmentIndex + 2, pointCount)];

            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private void AddSample(Vector3 position)
        {
            if (sampledPositions.Count > 0)
            {
                length += Vector3.Distance(sampledPositions[sampledPositions.Count - 1], position);
            }

            sampledPositions.Add(position);
            cumulativeDistances.Add(length);
        }

        private void AddClosingSample()
        {
            length += Vector3.Distance(sampledPositions[sampledPositions.Count - 1], sampledPositions[0]);
            sampledPositions.Add(sampledPositions[0]);
            cumulativeDistances.Add(length);
        }

        private void RemoveDuplicateClosingPoint()
        {
            if (controlPoints.Count < 2)
            {
                return;
            }

            Vector3 first = controlPoints[0];
            Vector3 last = controlPoints[controlPoints.Count - 1];
            if ((first - last).sqrMagnitude <= 0.0001f)
            {
                controlPoints.RemoveAt(controlPoints.Count - 1);
            }
        }

        private void ClampEntryDistanceToLength()
        {
            if (length <= 0f)
            {
                entryDistance = 0f;
                return;
            }

            entryDistance %= length;
            if (entryDistance < 0f)
            {
                entryDistance += length;
            }
        }

        private void EnsurePathColliderIsTrigger()
        {
            if (pathSource != null)
            {
                pathSource.isTrigger = true;
            }
        }

        private static int Mod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private void OnDrawGizmos()
        {
            RebuildCache();
            if (sampledPositions.Count < 2)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < sampledPositions.Count - 1; i++)
            {
                Gizmos.DrawLine(sampledPositions[i], sampledPositions[i + 1]);
            }

            Vector3 entryPosition = GetPositionAtDistance(EntryDistance);
            Vector3 aheadPosition = GetPositionAtDistance(EntryDistance + Mathf.Max(0.1f, Length * 0.015f));

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(entryPosition, 0.08f);
            Gizmos.DrawLine(entryPosition, aheadPosition);
        }
    }
}

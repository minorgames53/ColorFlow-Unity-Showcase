using System;
using System.Collections.Generic;
using DG.Tweening;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.TargetBoxes
{
    [DisallowMultipleComponent]
    public sealed class ConnectedTargetPresentationSystem : MonoBehaviour
    {
        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int SplitId = Shader.PropertyToID("_Split");

        [Header("Connection")]
        [SerializeField] private GameObject connectionPrefab;
        [SerializeField] private Transform connectionRoot;
        [SerializeField, Min(0.001f)] private float lineBaseLength = 1f;
        [SerializeField, Min(0f)] private float horizontalAnchorOffset = 0.35f;

        [Header("Color")]
        [SerializeField] private Color mysteryConnectionColor = Color.gray;
        [SerializeField, Range(0f, 1f)] private float colorSplit = 0.5f;

        [Header("Completion")]
        [SerializeField, Min(0f)] private float lineCollapseDuration = 0.2f;
        [SerializeField] private Ease lineCollapseEase = Ease.InBack;

        private readonly Dictionary<int, GroupView> groupViews = new Dictionary<int, GroupView>();
        private int lifecycleVersion;
        private float gameplaySpeedMultiplier = 1f;
        private MarbleColorCatalog colorCatalog;

        private sealed class GroupView
        {
            public int GroupId;
            public readonly List<LineView> Lines = new List<LineView>();
            public Tween CollapseTween;
            public float CollapseScale = 1f;
        }

        private sealed class LineView
        {
            public TargetBox First;
            public TargetBox Second;
            public GameObject Root;
            public Transform RootTransform;
            public Transform LineTransform;
            public SpriteRenderer Renderer;
            public float PrefabLocalPositionZ;
            public Vector3 PrefabLocalEulerAngles;
            public Vector3 PrefabLocalScale;
            public Vector3 PrefabLineLocalScale;
            public readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
        }

        public bool HasRequiredReferences => connectionPrefab != null && connectionRoot != null && lineBaseLength > 0f;

        private void OnValidate()
        {
            lineBaseLength = Mathf.Max(0.001f, lineBaseLength);
            horizontalAnchorOffset = Mathf.Max(0f, horizontalAnchorOffset);
            lineCollapseDuration = Mathf.Max(0f, lineCollapseDuration);
            colorSplit = Mathf.Clamp01(colorSplit);
        }

        private void LateUpdate()
        {
            foreach (KeyValuePair<int, GroupView> pair in groupViews)
            {
                GroupView groupView = pair.Value;
                for (int i = 0; i < groupView.Lines.Count; i++)
                {
                    UpdateLineGeometry(groupView.Lines[i], groupView.CollapseScale);
                }
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        public bool ValidateConfiguration()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError($"{nameof(ConnectedTargetPresentationSystem)} on '{name}' requires a Connection Prefab, Connection Root, and a positive Line Base Length.", this);
                return false;
            }

            SpriteRenderer[] renderers = connectionPrefab.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length != 1)
            {
                Debug.LogError($"{nameof(ConnectedTargetPresentationSystem)} on '{name}' requires its Connection Prefab to contain exactly one SpriteRenderer, but found {renderers.Length}.", this);
                return false;
            }

            if (renderers[0].transform == connectionPrefab.transform ||
                !renderers[0].transform.IsChildOf(connectionPrefab.transform))
            {
                Debug.LogError($"{nameof(ConnectedTargetPresentationSystem)} on '{name}' requires the prefab SpriteRenderer to be on a child line transform.", this);
                return false;
            }

            Material material = renderers[0].sharedMaterial;
            if (material == null || !material.HasProperty(ColorAId) || !material.HasProperty(ColorBId) || !material.HasProperty(SplitId))
            {
                Debug.LogError($"{nameof(ConnectedTargetPresentationSystem)} on '{name}' requires a connection material with _ColorA, _ColorB, and _Split properties.", this);
                return false;
            }

            return true;
        }

        public void SetGameplaySpeedMultiplier(float multiplier)
        {
            gameplaySpeedMultiplier = Mathf.Max(0.01f, multiplier);
        }

        public void BeginBuild(MarbleColorCatalog catalog)
        {
            Clear();
            colorCatalog = catalog;
        }

        public bool BuildGroup(int groupId, IReadOnlyList<TargetBox> members)
        {
            if (groupId <= 0 || members == null || members.Count < 2 || groupViews.ContainsKey(groupId))
            {
                Debug.LogError($"{nameof(ConnectedTargetPresentationSystem)} on '{name}' cannot build Connected Target group {groupId}.", this);
                return false;
            }

            List<TargetBox> orderedMembers = new List<TargetBox>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] == null)
                {
                    Debug.LogError($"{nameof(ConnectedTargetPresentationSystem)} on '{name}' cannot build group {groupId} because member {i} is null.", this);
                    return false;
                }

                orderedMembers.Add(members[i]);
            }

            orderedMembers.Sort(CompareTargetsByWorldX);
            GroupView groupView = new GroupView { GroupId = groupId };
            Transform parent = connectionRoot;

            for (int i = 0; i < orderedMembers.Count - 1; i++)
            {
                GameObject lineObject = Instantiate(connectionPrefab, parent, false);
                lineObject.name = $"ConnectedTarget_{groupId}_Line_{i + 1}";
                SpriteRenderer[] renderers = lineObject.GetComponentsInChildren<SpriteRenderer>(true);
                if (renderers.Length != 1)
                {
                    DestroyGameObject(lineObject);
                    DestroyGroupView(groupView);
                    Debug.LogError($"{nameof(ConnectedTargetPresentationSystem)} on '{name}' could not create line {i + 1} for group {groupId}: runtime prefab has {renderers.Length} SpriteRenderers.", this);
                    return false;
                }

                LineView lineView = new LineView
                {
                    First = orderedMembers[i],
                    Second = orderedMembers[i + 1],
                    Root = lineObject,
                    RootTransform = lineObject.transform,
                    LineTransform = renderers[0].transform,
                    Renderer = renderers[0],
                    PrefabLocalPositionZ = lineObject.transform.localPosition.z,
                    PrefabLocalEulerAngles = lineObject.transform.localEulerAngles,
                    PrefabLocalScale = lineObject.transform.localScale,
                    PrefabLineLocalScale = renderers[0].transform.localScale
                };

                groupView.Lines.Add(lineView);
                RefreshLineColors(lineView);
                UpdateLineGeometry(lineView, 1f);
            }

            groupViews.Add(groupId, groupView);
            return true;
        }

        public void RefreshTarget(TargetBox target)
        {
            if (target == null || !target.HasConnectedTargetGroup ||
                !groupViews.TryGetValue(target.ConnectedTargetGroupId, out GroupView groupView))
            {
                return;
            }

            for (int i = 0; i < groupView.Lines.Count; i++)
            {
                LineView line = groupView.Lines[i];
                if (line.First == target || line.Second == target)
                {
                    RefreshLineColors(line);
                }
            }
        }

        public void PlayCollapse(int groupId, Action onCompleted)
        {
            if (!groupViews.TryGetValue(groupId, out GroupView groupView))
            {
                onCompleted?.Invoke();
                return;
            }

            groupView.CollapseTween?.Kill(false);
            groupView.CollapseScale = 1f;
            int expectedLifecycleVersion = lifecycleVersion;
            float duration = lineCollapseDuration / gameplaySpeedMultiplier;
            if (duration <= 0f || groupView.Lines.Count == 0)
            {
                groupView.CollapseScale = 0f;
                onCompleted?.Invoke();
                return;
            }

            Tween tween = DOTween.To(
                    () => groupView.CollapseScale,
                    value => groupView.CollapseScale = value,
                    0f,
                    duration)
                .SetEase(lineCollapseEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    groupView.CollapseTween = null;
                    if (expectedLifecycleVersion == lifecycleVersion && groupViews.ContainsKey(groupId))
                    {
                        onCompleted?.Invoke();
                    }
                });

            groupView.CollapseTween = tween;
        }

        public void RemoveGroup(int groupId)
        {
            if (!groupViews.TryGetValue(groupId, out GroupView groupView))
            {
                return;
            }

            groupViews.Remove(groupId);
            DestroyGroupView(groupView);
        }

        public void Clear()
        {
            lifecycleVersion++;
            foreach (KeyValuePair<int, GroupView> pair in groupViews)
            {
                DestroyGroupView(pair.Value);
            }

            groupViews.Clear();
            colorCatalog = null;
        }

        private void RefreshLineColors(LineView line)
        {
            if (line?.Renderer == null)
            {
                return;
            }

            line.Renderer.GetPropertyBlock(line.PropertyBlock);
            line.PropertyBlock.SetColor(ColorAId, ResolveTargetColor(line.First));
            line.PropertyBlock.SetColor(ColorBId, ResolveTargetColor(line.Second));
            line.PropertyBlock.SetFloat(SplitId, colorSplit);
            line.Renderer.SetPropertyBlock(line.PropertyBlock);
        }

        private Color ResolveTargetColor(TargetBox target)
        {
            if (target == null)
            {
                return mysteryConnectionColor;
            }

            if (target.IsMystery && !target.IsActive)
            {
                return mysteryConnectionColor;
            }

            return colorCatalog != null && colorCatalog.TryGetEntry(target.ColorId, out MarbleColorCatalog.Entry entry)
                ? entry.ConnectedBoxConnectionTint
                : mysteryConnectionColor;
        }

        private void UpdateLineGeometry(LineView line, float collapseScale)
        {
            if (line?.RootTransform == null || line.LineTransform == null || line.First == null || line.Second == null)
            {
                return;
            }

            Vector3 firstPosition = line.First.transform.position;
            Vector3 secondPosition = line.Second.transform.position;
            Vector3 startPosition = new Vector3(
                firstPosition.x + horizontalAnchorOffset,
                firstPosition.y,
                firstPosition.z);
            Vector3 endPosition = new Vector3(
                secondPosition.x - horizontalAnchorOffset,
                secondPosition.y,
                secondPosition.z);
            Vector2 worldDelta = new Vector2(
                endPosition.x - startPosition.x,
                endPosition.y - startPosition.y);
            if (worldDelta.sqrMagnitude <= Mathf.Epsilon)
            {
                line.Root.SetActive(false);
                return;
            }

            line.Root.SetActive(true);
            Transform parent = line.RootTransform.parent;
            Vector3 localFirst = parent != null ? parent.InverseTransformPoint(startPosition) : startPosition;
            Vector3 localSecond = parent != null ? parent.InverseTransformPoint(endPosition) : endPosition;
            Vector2 localDirection = new Vector2(
                localSecond.x - localFirst.x,
                localSecond.y - localFirst.y);
            float localDistance = localDirection.magnitude;
            if (localDistance <= Mathf.Epsilon)
            {
                line.Root.SetActive(false);
                return;
            }

            line.RootTransform.localPosition = new Vector3(localFirst.x, localFirst.y, line.PrefabLocalPositionZ);
            line.RootTransform.localEulerAngles = new Vector3(
                line.PrefabLocalEulerAngles.x,
                line.PrefabLocalEulerAngles.y,
                Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg);
            line.RootTransform.localScale = new Vector3(
                localDistance / lineBaseLength,
                line.PrefabLocalScale.y,
                line.PrefabLocalScale.z);
            Vector3 lineScale = line.PrefabLineLocalScale;
            lineScale.x *= Mathf.Clamp01(collapseScale);
            line.LineTransform.localScale = lineScale;
        }

        private static int CompareTargetsByWorldX(TargetBox first, TargetBox second)
        {
            int xComparison = first.transform.position.x.CompareTo(second.transform.position.x);
            return xComparison != 0 ? xComparison : first.GetInstanceID().CompareTo(second.GetInstanceID());
        }

        private static void DestroyGroupView(GroupView groupView)
        {
            if (groupView == null)
            {
                return;
            }

            groupView.CollapseTween?.Kill(false);
            groupView.CollapseTween = null;
            for (int i = 0; i < groupView.Lines.Count; i++)
            {
                DestroyGameObject(groupView.Lines[i]?.Root);
            }

            groupView.Lines.Clear();
        }

        private static void DestroyGameObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}

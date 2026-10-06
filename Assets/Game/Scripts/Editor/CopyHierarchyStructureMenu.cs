#if UNITY_EDITOR

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Shared.Editor.MinorTools
{
    internal static class CopyHierarchyStructureMenu
    {
        private const string MenuPath = "GameObject/Copy Hierarchy Structure";

        [MenuItem(MenuPath, false, 20)]
        private static void CopyHierarchyStructure()
        {
            Transform[] selectedTransforms = Selection.transforms;
            if (selectedTransforms == null || selectedTransforms.Length == 0)
            {
                return;
            }

            var selectedSet = new HashSet<Transform>(selectedTransforms);
            var selectedRoots = new List<Transform>();

            foreach (Transform selected in selectedTransforms)
            {
                if (selected == null || HasSelectedAncestor(selected, selectedSet))
                {
                    continue;
                }

                selectedRoots.Add(selected);
            }

            selectedRoots.Sort(CompareHierarchyOrder);

            var builder = new StringBuilder();

            for (int i = 0; i < selectedRoots.Count; i++)
            {
                AppendHierarchy(
                    selectedRoots[i],
                    builder,
                    prefix: string.Empty,
                    isLast: true,
                    isRoot: true);

                if (i < selectedRoots.Count - 1)
                {
                    builder.AppendLine();
                }
            }

            EditorGUIUtility.systemCopyBuffer = builder.ToString();

            Debug.Log(
                $"Copied hierarchy structure for {selectedRoots.Count} root object(s):\n{builder}");
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateCopyHierarchyStructure()
        {
            return Selection.transforms is { Length: > 0 };
        }

        private static void AppendHierarchy(
            Transform transform,
            StringBuilder builder,
            string prefix,
            bool isLast,
            bool isRoot)
        {
            if (transform == null)
            {
                return;
            }

            if (isRoot)
            {
                builder.AppendLine(transform.name);
            }
            else
            {
                builder
                    .Append(prefix)
                    .Append(isLast ? "└── " : "├── ")
                    .AppendLine(transform.name);
            }

            string childPrefix = isRoot
                ? string.Empty
                : prefix + (isLast ? "    " : "│   ");

            int childCount = transform.childCount;

            for (int i = 0; i < childCount; i++)
            {
                AppendHierarchy(
                    transform.GetChild(i),
                    builder,
                    childPrefix,
                    i == childCount - 1,
                    isRoot: false);
            }
        }

        private static bool HasSelectedAncestor(
            Transform transform,
            HashSet<Transform> selectedTransforms)
        {
            Transform parent = transform.parent;

            while (parent != null)
            {
                if (selectedTransforms.Contains(parent))
                {
                    return true;
                }

                parent = parent.parent;
            }

            return false;
        }

        private static int CompareHierarchyOrder(Transform left, Transform right)
        {
            if (left == null)
            {
                return right == null ? 0 : 1;
            }

            if (right == null)
            {
                return -1;
            }

            int sceneComparison = string.CompareOrdinal(
                left.gameObject.scene.path,
                right.gameObject.scene.path);

            if (sceneComparison != 0)
            {
                return sceneComparison;
            }

            return string.CompareOrdinal(
                GetHierarchyPath(left),
                GetHierarchyPath(right));
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var builder = new StringBuilder();

            while (transform != null)
            {
                builder.Insert(0, $"/{transform.GetSiblingIndex():D5}_{transform.name}");
                transform = transform.parent;
            }

            return builder.ToString();
        }
    }
}

#endif

using Game.Shared.Audio;
using UnityEditor;
using UnityEngine;

namespace Game.Shared.Editor.Audio
{
    [CustomEditor(typeof(AudioCueCatalog))]
    [CanEditMultipleObjects]
    internal sealed class AudioCueCatalogEditor : UnityEditor.Editor
    {
        private const int ItemsPerPage = 15;

        private int currentPage;
        private bool showAll;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptReference();

            SerializedProperty cuesProperty = serializedObject.FindProperty("cues");
            if (cuesProperty == null || !cuesProperty.isArray)
            {
                EditorGUILayout.HelpBox(
                    "AudioCueCatalog.cues could not be resolved as a serialized list.",
                    MessageType.Error);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawCueList(cuesProperty);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptReference()
        {
            SerializedProperty scriptProperty = serializedObject.FindProperty("m_Script");
            if (scriptProperty == null)
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(scriptProperty);
            }
        }

        private void DrawCueList(SerializedProperty cuesProperty)
        {
            int itemCount = cuesProperty.arraySize;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)ItemsPerPage));
            currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);

            cuesProperty.isExpanded = EditorGUILayout.Foldout(
                cuesProperty.isExpanded,
                $"Cues ({itemCount})",
                true,
                EditorStyles.foldoutHeader);

            if (!cuesProperty.isExpanded)
            {
                return;
            }

            DrawPagingToolbar(pageCount);

            int firstIndex = showAll ? 0 : currentPage * ItemsPerPage;
            int lastIndexExclusive = showAll
                ? itemCount
                : Mathf.Min(itemCount, firstIndex + ItemsPerPage);

            EditorGUI.indentLevel++;
            for (int index = firstIndex; index < lastIndexExclusive; index++)
            {
                SerializedProperty cueProperty = cuesProperty.GetArrayElementAtIndex(index);
                EditorGUILayout.PropertyField(
                    cueProperty,
                    new GUIContent($"Element {index}"),
                    true);
            }
            EditorGUI.indentLevel--;

            DrawListControls(cuesProperty);
        }

        private void DrawPagingToolbar(int pageCount)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (showAll)
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Paged", EditorStyles.toolbarButton))
                    {
                        showAll = false;
                    }

                    return;
                }

                using (new EditorGUI.DisabledScope(currentPage == 0))
                {
                    if (GUILayout.Button("<", EditorStyles.toolbarButton, GUILayout.Width(28f)))
                    {
                        currentPage--;
                        GUI.FocusControl(null);
                    }
                }

                GUILayout.Label(
                    $"Page {currentPage + 1} / {pageCount}",
                    EditorStyles.centeredGreyMiniLabel);

                using (new EditorGUI.DisabledScope(currentPage >= pageCount - 1))
                {
                    if (GUILayout.Button(">", EditorStyles.toolbarButton, GUILayout.Width(28f)))
                    {
                        currentPage++;
                        GUI.FocusControl(null);
                    }
                }

                if (GUILayout.Button("Show All", EditorStyles.toolbarButton))
                {
                    showAll = true;
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawListControls(SerializedProperty cuesProperty)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
                {
                    if (GUILayout.Button("+", GUILayout.Width(28f)))
                    {
                        AddCue(cuesProperty);
                    }

                    using (new EditorGUI.DisabledScope(cuesProperty.arraySize == 0))
                    {
                        if (GUILayout.Button("-", GUILayout.Width(28f)))
                        {
                            cuesProperty.DeleteArrayElementAtIndex(cuesProperty.arraySize - 1);
                            currentPage = Mathf.Max(0, currentPage);
                        }
                    }
                }
            }
        }

        private void AddCue(SerializedProperty cuesProperty)
        {
            int newIndex = cuesProperty.arraySize;
            cuesProperty.arraySize++;

            SerializedProperty cueProperty = cuesProperty.GetArrayElementAtIndex(newIndex);
            SetEnumValue(cueProperty.FindPropertyRelative("key"), 0);
            SetObjectValue(cueProperty.FindPropertyRelative("clip"), null);
            SetFloatValue(cueProperty.FindPropertyRelative("volume"), 1f);
            SetFloatValue(cueProperty.FindPropertyRelative("minimumInterval"), 0f);

            if (!showAll)
            {
                currentPage = newIndex / ItemsPerPage;
            }
        }

        private static void SetEnumValue(SerializedProperty property, int value)
        {
            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetObjectValue(
            SerializedProperty property,
            Object value)
        {
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetFloatValue(SerializedProperty property, float value)
        {
            if (property != null)
            {
                property.floatValue = value;
            }
        }
    }
}

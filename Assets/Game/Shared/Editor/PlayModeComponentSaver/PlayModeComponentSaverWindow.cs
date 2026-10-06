#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlayModeComponentSaverTool
{
    internal sealed class PlayModeComponentSaverWindow : EditorWindow
    {
        private Vector2 scrollPosition;

        [MenuItem("Tools/Play Mode Component Saver")]
        internal static void OpenWindow()
        {
            PlayModeComponentSaverWindow window = GetWindow<PlayModeComponentSaverWindow>();
            window.titleContent = new GUIContent("Play Mode Saves");
            window.minSize = new Vector2(540f, 260f);
            window.Show();
        }

        private void OnEnable()
        {
            PlayModeComponentSaver.Changed += Repaint;
        }

        private void OnDisable()
        {
            PlayModeComponentSaver.Changed -= Repaint;
        }

        private void OnGUI()
        {
            DrawToolbar();

            IReadOnlyList<SavedComponentSnapshot> snapshots = PlayModeComponentSaver.GetSnapshots();
            if (snapshots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Henüz kayıt yok. Play Mode sırasında bir component'ın üç nokta menüsünden " +
                    "Save Play Mode Changes seçeneğini kullan.",
                    MessageType.Info);
                return;
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Play Mode açık. Pending kayıtlar Play Mode durduğunda otomatik uygulanacak.",
                    MessageType.Info);
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (SavedComponentSnapshot snapshot in snapshots
                         .OrderByDescending(item => item.savedUtc)
                         .ToList())
            {
                DrawSnapshot(snapshot);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label(
                "Pending: " + PlayModeComponentSaver.GetPendingCount(),
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(
                       EditorApplication.isPlaying || PlayModeComponentSaver.GetPendingCount() == 0))
            {
                if (GUILayout.Button("Apply Pending", EditorStyles.toolbarButton))
                    PlayModeComponentSaver.ApplyAllPendingNow();
            }

            if (GUILayout.Button("Clear History", EditorStyles.toolbarButton))
                PlayModeComponentSaver.ClearHistory(false);

            if (GUILayout.Button("Clear All", EditorStyles.toolbarButton))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Play Mode Component Saver",
                    "Pending kayıtlar dahil bütün liste silinsin mi?",
                    "Sil",
                    "Vazgeç");

                if (confirmed)
                    PlayModeComponentSaver.ClearHistory(true);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSnapshot(SavedComponentSnapshot snapshot)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                GetStatusText(snapshot.status) + "  " + snapshot.componentDisplayName,
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(FormatDate(snapshot.savedUtc), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Scene", snapshot.sceneName + "  (" + snapshot.scenePath + ")");
            EditorGUILayout.LabelField("Object", snapshot.hierarchyNamePath);

            if (!string.IsNullOrEmpty(snapshot.message))
            {
                MessageType messageType = snapshot.status == SavedComponentStatus.Failed
                    ? MessageType.Error
                    : MessageType.Warning;

                EditorGUILayout.HelpBox(snapshot.message, messageType);
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select", GUILayout.Width(72f)))
            {
                Object target = PlayModeComponentSaver.ResolveSnapshotForSelection(snapshot);
                if (target == null)
                {
                    EditorUtility.DisplayDialog(
                        "Play Mode Component Saver",
                        "Hedef şu anda bulunamadı. İlgili sahnenin açık olduğundan emin ol.",
                        "Tamam");
                }
                else
                {
                    Selection.activeObject = target;
                    EditorGUIUtility.PingObject(target);
                }
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (snapshot.status == SavedComponentStatus.Failed &&
                    GUILayout.Button("Retry", GUILayout.Width(72f)))
                {
                    PlayModeComponentSaver.RetrySnapshot(snapshot.id);
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Remove", GUILayout.Width(72f)))
            {
                PlayModeComponentSaver.RemoveSnapshot(snapshot.id);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static string GetStatusText(SavedComponentStatus status)
        {
            switch (status)
            {
                case SavedComponentStatus.Pending:
                    return "PENDING";
                case SavedComponentStatus.Applied:
                    return "APPLIED";
                case SavedComponentStatus.Failed:
                    return "FAILED";
                default:
                    return status.ToString().ToUpperInvariant();
            }
        }

        private static string FormatDate(string utcText)
        {
            DateTime utc;
            if (!DateTime.TryParse(
                    utcText,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out utc))
            {
                return utcText;
            }

            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text;
using Gameplay.MarbleDebug;
using Gameplay.SourceBoxes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gameplay.EditorTools
{
    [InitializeOnLoad]
    internal static class MarbleTrackerEditorLifecycle
    {
        private static string PreferenceKey => "ColorFlow.MarbleTracker.Enabled." + Application.dataPath;

        internal static void SetTrackingEnabled(bool enabled)
        {
            EditorPrefs.SetBool(PreferenceKey, enabled);
            MarbleDebugTracker.SetTrackingEnabled(enabled);
        }

        static MarbleTrackerEditorLifecycle()
        {
            MarbleDebugTracker.SetTrackingEnabled(EditorPrefs.GetBool(PreferenceKey, true));
            // Also works when Enter Play Mode disables domain reload.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode)
                    MarbleDebugTracker.ResetSession();
            };
            SceneManager.sceneUnloaded += scene =>
            {
                if (scene.handle == MarbleDebugTracker.SceneHandle) MarbleDebugTracker.Clear();
            };
            SceneManager.activeSceneChanged += (previous, current) =>
            {
                if (previous.handle == MarbleDebugTracker.SceneHandle && previous != current)
                    MarbleDebugTracker.Clear();
            };
        }
    }

    public sealed class MarbleTrackerWindow : EditorWindow
    {
        private enum LocationFilter { All, MissingSuspicious, SourceBox, DropZone, Conveyor, TargetBox, InTransit, Recovery, Ufo, Destroyed, Removed, Unknown }
        private LocationFilter filter;
        private MarbleColorId color;
        private Vector2 scroll, historyScroll;
        private MarbleDebugRecord selected;
        private bool showInventory = true;

        [MenuItem("Tools/Color Flow/Marble Tracker")]
        private static void Open() => GetWindow<MarbleTrackerWindow>("Marble Tracker");

        private void OnInspectorUpdate() => Repaint(); // Repaint only; never scan containers here.

        private void OnGUI()
        {
            bool enabled = EditorGUILayout.ToggleLeft("Enable Tracking", MarbleDebugTracker.TrackingEnabled);
            if (enabled != MarbleDebugTracker.TrackingEnabled)
            {
                MarbleTrackerEditorLifecycle.SetTrackingEnabled(enabled);
                selected = null;
            }
            if (!MarbleDebugTracker.TrackingEnabled)
            {
                selected = null;
                EditorGUILayout.HelpBox("Tracking is disabled. No history, container validation or automatic warnings are recorded. Current records were cleared. This setting is saved for this project in Editor preferences.", MessageType.Info);
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode and build a level to inspect runtime marbles. Tracking is Editor-only.", MessageType.Info);
                return;
            }
            if (MarbleDebugTracker.Level == 0)
            {
                selected = null;
                EditorGUILayout.HelpBox("Tracking is enabled and waiting for a new attempt. Start/rebuild a level. Enabling tracking or reloading scripts during an attempt requires a fresh build for complete history.", MessageType.Info);
                return;
            }

            var records = MarbleDebugTracker.Records;
            if (selected != null && !records.Contains(selected)) selected = null;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!MarbleDebugTracker.IsActive))
                    if (GUILayout.Button("Validate Now", EditorStyles.toolbarButton, GUILayout.Width(110)))
                        MarbleDebugTracker.Validate("Validate Now", false);
                if (GUILayout.Button("Copy report", EditorStyles.toolbarButton, GUILayout.Width(100))) CopyReport();
                GUILayout.FlexibleSpace();
                GUILayout.Label("Transit timeout (scaled seconds)");
                MarbleDebugTracker.TransitTimeout = Mathf.Max(0.5f,
                    EditorGUILayout.FloatField(MarbleDebugTracker.TransitTimeout, GUILayout.Width(55)));
            }
            EditorGUILayout.LabelField($"Level {MarbleDebugTracker.Level} / Content {MarbleDebugTracker.InternalLevel} / Attempt {MarbleDebugTracker.Attempt}", EditorStyles.boldLabel);
            int suspicious = records.Count(record => MarbleDebugTracker.Status(record).Length > 0);
            EditorGUILayout.LabelField($"Registered: {records.Count}    Accounted: {records.Count(MarbleDebugTracker.IsAccounted)}    In Transit: {records.Count(record => record.Location == MarbleDebugLocation.InTransit)}    Missing / Suspicious: {suspicious}");
            EditorGUILayout.LabelField($"Last validation: {MarbleDebugTracker.LastCheckpoint} / Frame {MarbleDebugTracker.LastValidationFrame}");
            EditorGUILayout.HelpBox("Accounted excludes suspicious records; Missing is a debug candidate count, not proof of loss. Collection checks are snapshots; Validate Now refreshes them. Coordinates are 1-based; conveyor/entry indices are 0-based.", MessageType.None);
            if (!MarbleDebugTracker.IsActive)
                EditorGUILayout.HelpBox("Attempt cleared. Cleanup records remain visible until the next attempt / scene change.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                filter = (LocationFilter)EditorGUILayout.EnumPopup("Location", filter);
                color = (MarbleColorId)EditorGUILayout.EnumPopup("Color (None = All)", color);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            showInventory = EditorGUILayout.Foldout(showInventory, "Color inventory and initial snapshot", true);
            if (showInventory) DrawInventory();
            foreach (string issue in MarbleDebugTracker.InventoryIssues.Concat(MarbleDebugTracker.LastScanIssues))
                EditorGUILayout.HelpBox(issue, MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                Cell("ID", 90); Cell("Color", 75); Cell("Location / State", 140);
                Cell("Owner (last commit)", 285); Cell("Origin", 285); Cell("Last transition", 380);
            }
            foreach (var record in records)
            {
                if (!Matches(record)) continue;
                string status = MarbleDebugTracker.Status(record);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(record.Label, selected == record ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Width(90))) selected = record;
                    Cell(record.Color.ToString(), 75);
                    Cell($"{record.Location} / {record.State}", 140);
                    Cell(record.OwnerDetail, 285); Cell(record.Origin, 285);
                    Cell(record.Last?.ToString(), 380);
                }
                if (status.Length > 0) EditorGUILayout.LabelField($"    {record.Label}: {status}", EditorStyles.boldLabel);
            }
            EditorGUILayout.EndScrollView();
            if (selected != null) DrawDetail(selected);
        }

        private bool Matches(MarbleDebugRecord record)
        {
            if (color != MarbleColorId.None && record.Color != color) return false;
            return filter == LocationFilter.All ||
                (filter == LocationFilter.MissingSuspicious ? MarbleDebugTracker.Status(record).Length > 0 :
                    filter.ToString() == record.Location.ToString());
        }

        private static void Cell(string value, float width) => GUILayout.Label(new GUIContent(value ?? "—", value ?? ""), GUILayout.Width(width));

        private void DrawInventory()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                Cell("Color", 85);
                foreach (string label in new[] { "Initial", "Later", "Source", "Drop", "Conveyor", "Target", "Transit", "Recovery", "UFO", "Accounted", "Missing" }) Cell(label, 70);
            }
            foreach (MarbleColorId id in Enum.GetValues(typeof(MarbleColorId)))
            {
                if (id == MarbleColorId.None || color != MarbleColorId.None && color != id) continue;
                var group = MarbleDebugTracker.Records.Where(record => record.Color == id).ToList();
                MarbleDebugTracker.Requirements.TryGetValue(id, out int required);
                MarbleDebugTracker.Supply.TryGetValue(id, out int authored);
                if (group.Count == 0 && required == 0 && authored == 0) continue;
                MarbleDebugTracker.Initial.TryGetValue(id, out int initial);
                int accounted = group.Count(MarbleDebugTracker.IsAccounted);
                using (new EditorGUILayout.HorizontalScope())
                {
                    Cell(id.ToString(), 85); Cell(initial.ToString(), 70); Cell((group.Count - initial).ToString(), 70);
                    foreach (var location in new[] { MarbleDebugLocation.SourceBox, MarbleDebugLocation.DropZone, MarbleDebugLocation.Conveyor,
                        MarbleDebugLocation.TargetBox, MarbleDebugLocation.InTransit, MarbleDebugLocation.Recovery, MarbleDebugLocation.Ufo })
                        Cell(group.Count(record => MarbleDebugTracker.IsAccounted(record) &&
                            (record.CompletedTarget ? location == MarbleDebugLocation.TargetBox : record.Location == location)).ToString(), 70);
                    Cell(accounted.ToString(), 70);
                    Cell(group.Count(record => MarbleDebugTracker.Status(record).Length > 0).ToString(), 70);
                }
                EditorGUILayout.LabelField($"    Authored supply: {authored}   Target requirement: {required}   Not yet registered (deferred/gates or build shortfall): {Math.Max(0, authored - group.Count)}   Requirement − accounted: {Math.Max(0, required - accounted)}");
            }
            EditorGUILayout.HelpBox("Initial = runtime objects at build completion. Authored supply includes sealed/spawner/gift contents, multiplier output and initial conveyor seeds. Completed targets remain accounted even when their presentation is disabled.", MessageType.None);
        }

        private void DrawDetail(MarbleDebugRecord record)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{record.Label} — {record.Color} / Initial color {record.InitialColor} / Instance #{record.InstanceId}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Origin", record.Origin ?? "Unknown origin");
            EditorGUILayout.LabelField("Current", $"{record.Location} / {record.OwnerDetail}");
            EditorGUILayout.LabelField("Previous / Last known", $"{record.PreviousLocation} / {record.LastKnownLocation}: {record.LastKnownOwner}");
            if (record.Location == MarbleDebugLocation.InTransit)
                EditorGUILayout.LabelField("Transfer age", $"{MarbleDebugTracker.TransitAge(record):F2}s (excluding pause / UFO freeze)");
            EditorGUILayout.LabelField("Observed at last scan", record.ObservedOwners.Length == 0 ? "No container" : record.ObservedOwners);
            EditorGUILayout.LabelField("Status", MarbleDebugTracker.Status(record) is string status && status.Length > 0 ? status : "No current anomaly");
            if (record.ValidatedRevision != record.Revision)
                EditorGUILayout.LabelField("Collection validation is stale for this record. Use Validate Now.");
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Runtime marble", record.Marble, typeof(Marble), true);
            historyScroll = EditorGUILayout.BeginScrollView(historyScroll, GUILayout.Height(180));
            foreach (var transition in record.History) EditorGUILayout.SelectableLabel(transition.ToString(), GUILayout.Height(18), GUILayout.MinWidth(1300));
            EditorGUILayout.EndScrollView();
        }

        private static void CopyReport()
        {
            var report = new StringBuilder($"[MarbleTracker] Level {MarbleDebugTracker.Level} / Attempt {MarbleDebugTracker.Attempt}\nCheckpoint: {MarbleDebugTracker.LastCheckpoint} / Frame {MarbleDebugTracker.LastValidationFrame}\n");
            foreach (var issue in MarbleDebugTracker.InventoryIssues.Concat(MarbleDebugTracker.LastScanIssues)) report.AppendLine(issue);
            foreach (var record in MarbleDebugTracker.Records)
            {
                report.AppendLine($"\n{record.Label} {record.Color} / {record.State} / {MarbleDebugTracker.Status(record)}\nOrigin: {record.Origin}\nCurrent: {record.Location} / {record.OwnerDetail}\nActual: {record.ObservedOwners}");
                foreach (var transition in record.History) report.AppendLine(transition.ToString());
            }
            EditorGUIUtility.systemCopyBuffer = report.ToString();
        }
    }
}
#endif

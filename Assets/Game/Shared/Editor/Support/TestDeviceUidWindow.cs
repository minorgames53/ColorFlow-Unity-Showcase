using Game.Shared.DeveloperTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Shared.Editor.Support
{
    public sealed class TestDeviceUidWindow : EditorWindow
    {
        private string deviceName = "Test Device";
        private string uid = string.Empty;

        [MenuItem("Game/Developer Tools/Test Device UID Converter")]
        private static void Open()
        {
            GetWindow<TestDeviceUidWindow>("Test Device UID");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Paste the UID from Settings or the support email. Copy the resulting entry " +
                                    "into the Firebase Remote Config test_devices > devices array. " +
                                    "Publishing takes effect on the next app launch.", MessageType.Info);
            deviceName = EditorGUILayout.TextField("Device label", deviceName);
            uid = EditorGUILayout.TextField("UID", uid);
            string value = uid.Trim();
            if (value.StartsWith("UID:", System.StringComparison.OrdinalIgnoreCase)) value = value.Substring(4).Trim();
            string hash = value == SystemInfo.unsupportedIdentifier ? string.Empty : TestDeviceAccessSnapshot.HashUid(value);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(hash)))
            {
                if (GUILayout.Button("Copy Remote Config Device Entry"))
                    EditorGUIUtility.systemCopyBuffer = new JObject
                    {
                        ["deviceName"] = deviceName,
                        ["uidSha256"] = hash
                    }.ToString(Formatting.Indented);
            }
            EditorGUILayout.LabelField("SHA-256", hash, EditorStyles.wordWrappedLabel);
        }

        private void OnDisable()
        {
            uid = string.Empty;
        }
    }
}

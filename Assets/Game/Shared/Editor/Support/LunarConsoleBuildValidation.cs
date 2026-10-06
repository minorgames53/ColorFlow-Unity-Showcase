using Game.Shared.DeveloperTools;
using LunarConsolePlugin;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Game.Shared.Editor.Support
{
    // Keep scene serialization fail-closed even if the SDK's default enabled prefab
    // is accidentally installed later through its menu.
    internal sealed class LunarConsoleBuildValidation : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            ValidateScene(scene);
        }

        internal static void ValidateScene(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var console in root.GetComponentsInChildren<LunarConsole>(true))
                {
                    var owner = console.GetComponent<DeveloperPanelBootstrap>();
                    var ownerData = owner != null ? new SerializedObject(owner) : null;
                    if (console.enabled || owner == null || console.transform.parent != null ||
                        ownerData.FindProperty("lunarConsole").objectReferenceValue != console)
                        throw new BuildFailedException(
                            $"Lunar Console in '{scene.path}' must be disabled and referenced by " +
                            "DeveloperPanelBootstrap on the same persistent root.");
                }

                foreach (var owner in root.GetComponentsInChildren<DeveloperPanelBootstrap>(true))
                {
                    var console = new SerializedObject(owner).FindProperty("lunarConsole")
                        .objectReferenceValue as LunarConsole;
                    if (console == null || console.gameObject != owner.gameObject)
                        throw new BuildFailedException(
                            $"DeveloperPanelBootstrap in '{scene.path}' is missing its local Lunar Console reference.");
                }
            }
        }
    }
}

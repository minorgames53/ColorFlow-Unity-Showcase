using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Game.Shared.Editor.Support
{
    internal sealed class TenjinBuildValidation : IProcessSceneWithReport
    {
        public int callbackOrder => 10;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var owner in root.GetComponentsInChildren<AnalyticsBootstrap>(true))
            {
                var serialized = new SerializedObject(owner);
                var config = serialized.FindProperty("config").objectReferenceValue as AnalyticsConfig;
                if (config == null) throw new BuildFailedException("AnalyticsBootstrap is missing Analytics config.");
                if (!config.EnableTenjin) continue;
                if (serialized.FindProperty("consentOwner").objectReferenceValue == null)
                    throw new BuildFailedException("Tenjin requires AnalyticsBootstrap.consentOwner (existing AdsService).");
                if ((report.summary.options & BuildOptions.Development) != 0) continue;
                string key = report.summary.platform == BuildTarget.Android ? config.AndroidTenjinSdkKey :
                    report.summary.platform == BuildTarget.iOS ? config.IosTenjinSdkKey : "not-mobile";
                ValidateReleaseKey(key);
            }
        }

        internal static void ValidateReleaseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new BuildFailedException("Tenjin enabled but platform SDK key is empty. Set it on Analytics.asset.");
        }
    }
}

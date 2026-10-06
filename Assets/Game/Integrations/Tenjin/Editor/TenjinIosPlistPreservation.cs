#if UNITY_EDITOR && UNITY_IOS
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace Game.Integrations.Tenjin.Editor
{
    // Tenjin 1.21.0 unconditionally writes its sample ATT text. Preserve this project's
    // existing text around that official postprocessor; keep the upstream package immutable.
    internal sealed class TenjinIosPlistSnapshot : IPostprocessBuildWithReport
    {
        internal static string Description;
        public int callbackOrder => -100;
        public void OnPostprocessBuild(BuildReport report)
        {
            Description = null;
            if (report.summary.platform != UnityEditor.BuildTarget.iOS) return;
            var plist = new PlistDocument();
            plist.ReadFromFile(Path.Combine(report.summary.outputPath, "Info.plist"));
            if (plist.root.values.TryGetValue("NSUserTrackingUsageDescription", out var value)) Description = value.AsString();
            // AdMob and Tenjin both use callback order 0. Capture AdMob's configured
            // text before either runs; their relative order must not choose ATT copy.
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:GoogleMobileAdsSettings"))
            {
                var settings = UnityEditor.AssetDatabase.LoadMainAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                var property = new UnityEditor.SerializedObject(settings).FindProperty("userTrackingUsageDescription");
                if (property != null && !string.IsNullOrEmpty(property.stringValue)) Description = property.stringValue;
            }
        }
    }
    internal sealed class TenjinIosPlistPreservation : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != UnityEditor.BuildTarget.iOS) return;
            string path = Path.Combine(report.summary.outputPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(path);
            if (!Game.Shared.Config.FeatureConfig.ExternalServicesEnabled)
            {
                // Native SDKs may run before Unity's managed bootstrap. Keep the
                // exported showcase disabled after vendor postprocessors finish.
                plist.root.SetBoolean("FirebaseDataCollectionDefaultEnabled", false);
                plist.root.SetBoolean("FIREBASE_ANALYTICS_COLLECTION_ENABLED", false);
                plist.root.SetBoolean("FIREBASE_ANALYTICS_COLLECTION_DEACTIVATED", true);
                plist.root.SetBoolean("FirebaseCrashlyticsCollectionEnabled", false);
                plist.root.SetBoolean("FirebaseMessagingAutoInitEnabled", false);
                plist.root.SetBoolean("FacebookAutoInitEnabled", false);
                plist.root.SetBoolean("FacebookAutoLogAppEventsEnabled", false);
                plist.root.SetBoolean("FacebookAdvertiserIDCollectionEnabled", false);
                plist.root.SetString("FacebookAppID", "0");
                plist.root.SetString("FacebookClientToken", string.Empty);
                plist.root.values.Remove("NSUserTrackingUsageDescription");
            }
            else if (!string.IsNullOrEmpty(TenjinIosPlistSnapshot.Description))
            {
                plist.root.SetString("NSUserTrackingUsageDescription", TenjinIosPlistSnapshot.Description);
            }
            plist.WriteToFile(path);
        }
    }
}
#endif

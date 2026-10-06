#if UNITY_EDITOR && UNITY_ANDROID
using System.IO;
using UnityEditor.Android;
using UnityEditor.Build;

namespace Game.Integrations.Tenjin.Editor
{
    internal sealed class TenjinAndroidBuildPostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // Unity's existing template already consumes this generated file. No
            // PlayerSettings override or extra Android library is needed for R8.
            string rulesPath = Path.Combine(path, "proguard-unity.txt");
            if (!File.Exists(rulesPath))
                throw new BuildFailedException("Tenjin R8 setup: generated proguard-unity.txt is missing.");
            const string marker = "# Color Flow Tenjin keep rules";
            if (File.ReadAllText(rulesPath).Contains(marker)) return;
            File.AppendAllText(rulesPath, "\n" + marker + @"
-keep class com.tenjin.** { *; }
-keep public class com.google.android.gms.ads.identifier.** { *; }
-keep public class com.google.android.gms.common.** { *; }
-keep public class com.android.installreferrer.** { *; }
-keep class * extends java.util.ListResourceBundle {
    protected java.lang.Object[][] getContents();
}
-keepattributes *Annotation*
");
        }
    }
}
#endif

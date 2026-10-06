using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Game.Shared.UI
{
    /// <summary>Regional Portuguese selection before Unity's standard system selector.</summary>
    [Serializable]
    public sealed class PortugueseLocaleSelector : IStartupLocaleSelector
    {
        public Locale GetStartupLocale(ILocalesProvider availableLocales)
        {
            string code = CultureInfo.CurrentUICulture.Name;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var javaLocale = new AndroidJavaClass("java.util.Locale"))
            using (var deviceLocale = javaLocale.CallStatic<AndroidJavaObject>("getDefault"))
                code = deviceLocale.Call<string>("toLanguageTag");
#elif UNITY_IOS && !UNITY_EDITOR
            code = getPreferredLanguage();
#endif
            string mapped = MapPortuguese(code);
            if (mapped == null && Application.systemLanguage == SystemLanguage.Portuguese)
                mapped = "pt-PT";
            return mapped == null ? null : availableLocales.GetLocale(mapped);
        }

        public static string MapPortuguese(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            string[] parts = code.Replace('_', '-').Split('-');
            if (!string.Equals(parts[0], "pt", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (string part in parts)
                if (string.Equals(part, "BR", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
            return "pt-PT";
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string getPreferredLanguage();
#endif
    }
}

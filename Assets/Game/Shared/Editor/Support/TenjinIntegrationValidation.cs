using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Save;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Shared.Editor.Support
{
    public static class TenjinIntegrationValidation
    {
        [MenuItem("Game/Analytics/Validate Tenjin Integration (Offline)")]
        public static void RunFromMenu() => Debug.Log(Run());

        // No gameplay, native SDK, network, SaveManager or PlayerPrefs calls.
        public static string Run()
        {
            int passed = 0;
            Action<bool, string> check = (ok, name) => { if (!ok) throw new Exception(name); passed++; };
            var old = JsonUtility.FromJson<StoreIapAnalyticsTransactionSaveData>(
                "{\"transactionId\":\"historical\",\"productId\":\"test\",\"confirmed\":true,\"levelIapPurchaseAccepted\":true}");
            check(!old.tenjinPurchasePending && !old.tenjinPurchaseDispatchReserved, "Old records must not enroll");
            var data = GameSaveDataFactory.CreateDefault();
            data.store.iapAnalyticsTransactions.Add(old);
            data.store.iapAnalyticsTransactions.Add(new StoreIapAnalyticsTransactionSaveData
            {
                transactionId = "new", productId = "test", confirmed = true,
                tenjinPurchasePending = true, tenjinReceipt = "synthetic", tenjinSignature = "synthetic",
                tenjinQuantity = 2, currency = "USD", localizedPriceValue = 1.99
            });
            GameSaveDataNormalizer.Normalize(data);
            check(data.store.iapAnalyticsTransactions.Count == 2, "Non-gameplay purchase rows survive normalization");
            var copy = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));
            check(copy.store.iapAnalyticsTransactions[1].tenjinPurchasePending, "Pending dispatch survives serialization");
            check(copy.store.iapAnalyticsTransactions[1].tenjinReceipt == "synthetic", "Pending receipt survives serialization");
            check(copy.store.iapAnalyticsTransactions[1].tenjinQuantity == 2, "Quantity survives serialization");
            check(!copy.store.iapAnalyticsTransactions[0].tenjinPurchasePending, "Historical receipt is not replayed");
            foreach (string key in new[] { null, "", "   " })
            {
                bool rejected = false;
                try { TenjinBuildValidation.ValidateReleaseKey(key); } catch (BuildFailedException) { rejected = true; }
                check(rejected, "Blank release key must be rejected");
            }
            TenjinBuildValidation.ValidateReleaseKey("synthetic-not-a-real-key");
            passed++;
            var config = AssetDatabase.LoadAssetAtPath<AnalyticsConfig>("Assets/Game/Data/Configs/Analytics.asset");
            check(config != null, "Analytics config exists");
            if (Game.Shared.Config.FeatureConfig.ExternalServicesEnabled)
            {
                check(config.EnableTenjin && config.EnableFirebaseAnalytics, "Both providers configured");
            }
            else
            {
                check(!config.CollectionEnabled && !config.EnableTenjin && !config.EnableFirebaseAnalytics,
                    "Showcase collection and providers disabled");
                check(string.IsNullOrEmpty(config.AndroidTenjinSdkKey) && string.IsNullOrEmpty(config.IosTenjinSdkKey),
                    "Showcase Tenjin credentials empty");
            }
            int owners = 0;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var owner in root.GetComponentsInChildren<AnalyticsBootstrap>(true))
            {
                owners++;
                var serialized = new SerializedObject(owner);
                check(serialized.FindProperty("config").objectReferenceValue == config, "Existing config wired");
                check(serialized.FindProperty("consentOwner").objectReferenceValue != null, "Consent owner wired");
            }
            check(owners == 1, "One analytics owner in Boot");
            passed += ValidateDiagnostics();
            return "Tenjin: " + passed + " offline checks passed.";
        }

        // Synthetic Edit Mode diagnostics only; no Tenjin/native calls or saved state.
        private static int ValidateDiagnostics()
        {
            int passed = 0;
            Action<bool, string> check = (ok, name) => { if (!ok) throw new Exception(name); passed++; };
            var config = ScriptableObject.CreateInstance<AnalyticsConfig>();
            var previousHandler = Debug.unityLogger.logHandler;
            bool previousEnabled = Debug.unityLogger.logEnabled;
            var previousFilter = Debug.unityLogger.filterLogType;
            var previousCulture = CultureInfo.CurrentCulture;
            var capture = new CapturingLogHandler();
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                Debug.unityLogger.logHandler = capture;
                Debug.unityLogger.logEnabled = true;
                Debug.unityLogger.filterLogType = LogType.Log;
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                check(!config.EnableTenjinDebugLogs, "Tenjin diagnostics defaults off");
                var serialized = new SerializedObject(config);
                serialized.FindProperty("enableDebugLogs").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var type = typeof(AnalyticsConfig).Assembly.GetType("Game.Shared.Analytics.Providers.TenjinAnalyticsProvider", true);
                var provider = Activator.CreateInstance(type, methods, null, new object[] { config }, CultureInfo.InvariantCulture);
                type.GetMethod("Initialize", methods).Invoke(provider, null);
                check(capture.Messages.Count == 0, "Debug false emits no provider diagnostics");
                serialized.FindProperty("enableTenjinDebugLogs").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                type.GetMethod("Initialize", methods).Invoke(provider, null);
                check(capture.Contains("[Tenjin] Provider ready"), "Dedicated flag works independently of general analytics debug");
                var custom = type.GetMethod("TrackCustom", methods);
                custom.Invoke(provider, new object[] { "level_achieved", 19 });
                check(capture.Contains("Event requested: level_achieved | value=19"), "Displayed integer level logged");
                check(capture.Contains("Queued until Connect:"), "Queued is distinguished from issued");
                check(!capture.Contains("SDK call issued:"), "No false native/server acknowledgement before Connect");
                custom.Invoke(provider, new object[] { "tutorial_complete", null });
                check(!capture.Contains("tutorial_complete") &&
                    (int)type.GetProperty("PendingEventCount").GetValue(provider) == 1,
                    "Tutorial attribution is disabled and never queued");
                int queued = (int)type.GetProperty("PendingEventCount").GetValue(provider);
                custom.Invoke(provider, new object[] { "level_achieved", 0 });
                custom.Invoke(provider, new object[] { "unsupported", null });
                check((int)type.GetProperty("PendingEventCount").GetValue(provider) == queued, "Invalid custom events rejected");
                type.GetMethod("TrackAdRevenue", methods).Invoke(provider, new object[]
                    { "synthetic-private-json", "Rewarded", "USD", 0.000123d, "synthetic-unit", "synthetic-placement", "synthetic-network" });
                check(capture.Contains("Format=Rewarded | Currency=USD | Revenue=0.000123"), "Micros summary invariant under Turkish culture");
                check(!capture.Contains("synthetic-private-json"), "Raw ILRD payload not logged");
                type.GetMethod("ReportError", methods).Invoke(provider, new object[] { "synthetic-stage", new Exception("synthetic-secret") });
                check(capture.Contains("SDK call failed | synthetic-stage: Exception"), "Local failure is actionable");
                check(!capture.Contains("synthetic-secret"), "Exception message cannot leak secrets");
                capture.Messages.Clear();
                serialized.FindProperty("enableTenjinDebugLogs").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                custom.Invoke(provider, new object[] { "level_achieved", 20 });
                check(capture.Messages.Count == 0, "Disabling debug silences further diagnostics");
                check((int)type.GetProperty("PendingEventCount").GetValue(provider) == queued + 2, "Debug false does not disable tracking/queue");

                var scopeType = type.GetNestedType("SdkPayloadLogScope", BindingFlags.NonPublic);
                try
                {
                    using ((IDisposable)Activator.CreateInstance(scopeType, true))
                    {
                        Debug.Log("Android Transaction synthetic-receipt synthetic-signature");
                        Debug.Log("iOS Transaction with receipt synthetic-receipt");
                        Debug.Log("Got admob ILRD data synthetic-private-json");
                        Debug.Log("Unrelated log retained");
                        Debug.LogWarning("Unrelated warning retained");
                        Debug.LogError("Unrelated error retained");
                        throw new InvalidOperationException("synthetic scope unwind");
                    }
                }
                catch (InvalidOperationException) { }
                check(!capture.Contains("synthetic-receipt") && !capture.Contains("synthetic-signature"), "Vendor receipt/signature logs suppressed");
                check(!capture.Contains("synthetic-private-json"), "Vendor raw ILRD log suppressed");
                check(capture.Contains("Unrelated log retained") && capture.Contains("Unrelated warning retained") && capture.Contains("Unrelated error retained"), "Other Unity logs unchanged");
                check(ReferenceEquals(Debug.unityLogger.logHandler, capture), "Logger restored even after exceptions");
            }
            finally
            {
                Debug.unityLogger.logHandler = previousHandler;
                Debug.unityLogger.logEnabled = previousEnabled;
                Debug.unityLogger.filterLogType = previousFilter;
                CultureInfo.CurrentCulture = previousCulture;
                UnityEngine.Object.DestroyImmediate(config);
            }
            return passed;
        }

        private sealed class CapturingLogHandler : ILogHandler
        {
            internal readonly List<string> Messages = new List<string>();
            internal bool Contains(string value) => Messages.Exists(message => message.Contains(value));
            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args) =>
                Messages.Add(string.Format(CultureInfo.InvariantCulture, format, args));
            public void LogException(Exception exception, UnityEngine.Object context) => Messages.Add(exception.GetType().Name);
        }
    }
}

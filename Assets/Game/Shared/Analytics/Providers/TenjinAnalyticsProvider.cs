using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Shared.Analytics.Core;
using Game.Shared.Analytics.Debugging;
using Game.Shared.Save;
using UnityEngine;

namespace Game.Shared.Analytics.Providers
{
    internal sealed class TenjinAnalyticsProvider : IAnalyticsProvider, IAnalyticsProviderDebugInfo
    {
        private static TenjinAnalyticsProvider instance;
        private readonly AnalyticsConfig config;
        private readonly Queue<Action> pending = new Queue<Action>();
        private BaseTenjin sdk;
        private bool consentResolved;
        private bool consentAllowed;
        private bool connected;
        private int lastConnectFrame = -1;
        private bool Diagnostics => config.EnableTenjinDebugLogs;
        private TenjinAnalyticsProvider(AnalyticsConfig config) { this.config = config; IsCollectionEnabled = config.CollectionEnabled; }
        internal static TenjinAnalyticsProvider GetOrCreate(AnalyticsConfig config) => instance ?? (instance = new TenjinAnalyticsProvider(config));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { instance = null; }
        public bool IsReady => connected && consentAllowed && IsCollectionEnabled;
        public bool IsCollectionEnabled { get; private set; }
        internal bool IsConsentDenied => consentResolved && !consentAllowed && sdk != null;
        public string ProviderDisplayName => "Tenjin";
        public AnalyticsProviderDebugState DebugState => LastError != null ? AnalyticsProviderDebugState.Error :
            IsReady ? AnalyticsProviderDebugState.Ready : consentResolved && !consentAllowed ? AnalyticsProviderDebugState.Disabled : AnalyticsProviderDebugState.Initializing;
        public int PendingEventCount => pending.Count;
        public string LastError { get; private set; }
        public void Initialize() => Log("Provider ready; waiting for existing UMP/ATT lifecycle.");

        internal void ApplyConsent(bool resolved, bool foreground = false)
        {
            consentResolved = resolved;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            try
            {
                bool previouslyReady = IsReady;
                if (!resolved || !IsCollectionEnabled)
                {
                    consentAllowed = false;
                    sdk?.SetCacheEventSetting(false);
                    sdk?.OptOut();
                    if (resolved) pending.Clear();
                    Log(!resolved ? "Tracking deferred: consent unresolved." : "Tracking disabled: analytics collection disabled.");
                    return;
                }
                if (sdk == null)
                {
#if UNITY_ANDROID
                    string key = config.AndroidTenjinSdkKey;
                    Log("Android SDK key configured: " + (string.IsNullOrEmpty(key) ? "NO" : "YES"));
#else
                    string key = config.IosTenjinSdkKey;
                    Log("iOS SDK key configured: " + (string.IsNullOrEmpty(key) ? "NO" : "YES"));
#endif
                    if (string.IsNullOrEmpty(key))
                    {
                        LastError = "Tenjin SDK key is missing from Analytics config.";
                        Log("Initialization blocked: " + LastError);
                        return;
                    }
                    Log("Initializing SDK");
                    sdk = Tenjin.getInstance(key);
#if UNITY_ANDROID
                    sdk.SetAppStoreType(AppStoreType.googleplay);
#endif
                    Log("SDK initialized");
                    // 1.21.0 DebugLogs(): Android is a no-op; iOS has no public
                    // disable/redaction API. Keep uncontrolled native output off.
                    Log("Safe provider diagnostics active; native payload logging is not enabled. SDK call issued is NOT server acknowledgement.");
                }
                // UMP writes the IAB TCF purpose-1 preferences consumed by this native API.
                // DMA parameters are read automatically by Tenjin; do not override them.
                // Native Tenjin treats missing/malformed purpose strings as opt-in.
                // Never allow that fallback in a region where UMP says GDPR applies.
                if (GoogleMobileAds.Api.ApplicationPreferences.GetInt("IABTCF_gdprApplies") == 1)
                {
                    string purposes = GoogleMobileAds.Api.ApplicationPreferences.GetString("IABTCF_PurposeConsents");
                    if (string.IsNullOrEmpty(purposes) || purposes.Length > 12 ||
                        System.Text.RegularExpressions.Regex.IsMatch(purposes, "[^01]"))
                    {
                        consentAllowed = false;
                        sdk.SetCacheEventSetting(false);
                        sdk.OptOut();
                        pending.Clear();
                        Log("Tracking blocked: CMP purpose consent missing or malformed.");
                        return;
                    }
                }
                consentAllowed = sdk.OptInOutUsingCMP();
                // Reset the Android wrapper's previous explicit opt-out only after CMP grants access.
                if (consentAllowed) sdk.OptIn();
                sdk.SetCacheEventSetting(consentAllowed); // Explicit false also clears the persistent setting.
                Log("CMP tracking allowed: " + (consentAllowed ? "YES" : "NO") + " | SDK cache/retry=" + (consentAllowed ? "enabled" : "disabled"));
                if (!consentAllowed) { pending.Clear(); return; }
                if (!previouslyReady) Connect(foreground);
            }
            catch (Exception ex) { consentAllowed = false; ReportError("consent/init", ex); }
#endif
        }

        internal void Connect(bool foreground = false)
        {
            if (lastConnectFrame == UnityEngine.Time.frameCount) return;
            if (sdk == null || !consentAllowed || !IsCollectionEnabled)
            {
                Log("Connect deferred: SDK/consent/collection not ready.");
                return;
            }
            lastConnectFrame = UnityEngine.Time.frameCount;
            Log(foreground ? "App returned to foreground -> Connect requested" : "Connect requested");
            try
            {
                sdk.Connect();
                connected = true;
                LastError = null;
                Log("SDK call issued: Connect (not server acknowledgement)");
                while (pending.Count > 0 && IsReady) pending.Dequeue().Invoke();
            }
            catch (Exception ex) { ReportError("connect", ex); }
        }

        internal void TrackCustom(string name, int? value)
        {
            if (name != "level_achieved" || !value.HasValue || value.Value <= 0) return;
            string description = name + (value.HasValue ? " | value=" + value.Value.ToString(CultureInfo.InvariantCulture) : "");
            Log("Event requested: " + description);
            Dispatch(() =>
            {
                if (value.HasValue) sdk.SendEvent(name, value.Value.ToString(CultureInfo.InvariantCulture));
                else sdk.SendEvent(name);
                Log("SDK call issued: SendEvent | " + description + " (not server acknowledgement)");
            }, description);
        }
        internal void TrackAdRevenue(string json, string format, string currency, double revenue, string adUnit, string placement, string network)
        {
            string summary = "Format=" + format + " | Currency=" + currency + " | Revenue=" + revenue.ToString("0.######", CultureInfo.InvariantCulture) +
                " | AdUnit=" + adUnit + " | Placement=" + placement + " | Network=" + network;
            Log("Ad revenue requested | " + summary);
            Dispatch(() =>
            {
                using (new SdkPayloadLogScope()) sdk.AdMobImpressionFromJSON(json);
                Log("SDK call issued: AdMobImpressionFromJSON | " + summary + " (not server acknowledgement)");
            }, "AdMob ILRD | Format=" + format);
        }
        private void Dispatch(Action action, string description)
        {
            if (!IsCollectionEnabled || (consentResolved && !consentAllowed))
            {
                Log("Not issued: " + description + " | consent/collection does not permit tracking.");
                return;
            }
            if (!IsReady)
            {
                if (pending.Count < 128) { pending.Enqueue(action); Log("Queued until Connect: " + description); }
                else Log("Not issued: " + description + " | pre-connect queue full.");
                return;
            }
            try { action(); } catch (Exception ex) { ReportError("event", ex); }
        }

        internal void TrackPurchase(StoreIapAnalyticsTransactionSaveData entry)
        {
            if (!IsReady) { Log("Transaction not issued: SDK/consent/collection not ready."); return; }
            Log("Transaction requested | Product=" + entry.productId + " | Currency=" + entry.currency +
                " | Revenue=" + (entry.localizedPriceValue * entry.tenjinQuantity).ToString("G", CultureInfo.InvariantCulture) +
                " | UnitPrice=" + entry.localizedPriceValue.ToString("G", CultureInfo.InvariantCulture) +
                " | Quantity=" + entry.tenjinQuantity.ToString(CultureInfo.InvariantCulture));
            // Caller durably reserves this transaction BEFORE handoff; never queued twice locally.
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (new SdkPayloadLogScope())
                    sdk.Transaction(entry.productId, entry.currency, entry.tenjinQuantity, entry.localizedPriceValue,
                        null, entry.tenjinReceipt, entry.tenjinSignature);
#elif UNITY_IOS && !UNITY_EDITOR
                using (new SdkPayloadLogScope())
                    sdk.Transaction(entry.productId, entry.currency, entry.tenjinQuantity, entry.localizedPriceValue,
                        entry.transactionId, entry.tenjinReceipt, null);
#endif
                Log("SDK call issued: Transaction (not server acknowledgement; validation result is in Tenjin dashboard)");
            }
            catch (Exception ex) { ReportError("transaction", ex); }
        }

        public void SetCollectionEnabled(bool enabled) { IsCollectionEnabled = enabled; ApplyConsent(consentResolved); }
        public AnalyticsProviderTrackResult Track(AnalyticsEvent analyticsEvent, bool queueWhenProviderNotReady = true) =>
            AnalyticsProviderTrackResult.SkippedUnsupported("GA4 event; Tenjin has an independent explicit dispatch path.");
        public void SetDefaultLevelContext(int displayedLevelNumber, int internalLevelNumber, string source) { }
        public void ClearDefaultLevelContext(string source) { }
        public void ResetAnalyticsData(string source = "analytics_reset") { pending.Clear(); }
        private void ReportError(string stage, Exception ex)
        {
            LastError = stage + ": " + ex.GetType().Name; // Never print SDK keys or receipts.
            if (Diagnostics) Debug.LogWarning("[Tenjin] SDK call failed | " + LastError + " (local exception; not a server response)");
        }
        private void Log(string message) { if (Diagnostics) Debug.Log("[Tenjin] " + message); }

        // Tenjin 1.21.0 synchronously logs full receipts/signatures in Android
        // development builds and raw ILRD JSON even in release builds. Intercept
        // ONLY those known SDK messages during our call, using Unity's public
        // logging API. Never silence the global logger or modify Lunar/SDK code.
        private sealed class SdkPayloadLogScope : ILogHandler, IDisposable
        {
            private readonly ILogHandler previous = Debug.unityLogger.logHandler;
            public SdkPayloadLogScope() { Debug.unityLogger.logHandler = this; }
            public void Dispose() { if (ReferenceEquals(Debug.unityLogger.logHandler, this)) Debug.unityLogger.logHandler = previous; }
            public void LogException(Exception exception, UnityEngine.Object context) => previous.LogException(exception, context);
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                string message = format == "{0}" && args != null && args.Length == 1 ? args[0] as string : format;
                if (IsPayloadLog(message)) return;
                previous.LogFormat(logType, context, format, args);
            }
            private static bool IsPayloadLog(string message) => message != null &&
                (message.StartsWith("Android Transaction ", StringComparison.Ordinal) ||
                 message.StartsWith("iOS Transaction ", StringComparison.Ordinal) ||
                 message.StartsWith("Got admob ILRD data ", StringComparison.Ordinal));
        }
    }
}

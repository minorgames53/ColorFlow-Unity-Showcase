using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Integrations.Firebase.Core;
using Game.Shared.Analytics.Core;
using Game.Shared.Analytics.Debugging;
using UnityEngine;

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
using Firebase.Analytics;
#endif

namespace Game.Shared.Analytics.Providers
{
    public sealed class FirebaseAnalyticsProvider : IAnalyticsProvider, IAnalyticsProviderDebugInfo
    {
        private const int MaxPendingEventCount = 100;
        private const int MaxFirebaseParameterCount = 25;
        private const string FirebaseReservedPrefix = "firebase_";
        private const string GoogleReservedPrefix = "google_";
        private const string GaReservedPrefix = "ga_";

        private readonly Queue<AnalyticsEvent> pendingEvents = new Queue<AnalyticsEvent>(MaxPendingEventCount);
        private readonly HashSet<string> unsupportedPropertyWarnings = new HashSet<string>();
        private readonly HashSet<string> invalidParameterNameWarnings = new HashSet<string>();
        private readonly bool enableDebugLogs;

        private bool isInitializationStarted;
        private bool isUserIdSet;
        private bool missingSdkWarningLogged;
        private bool pendingQueueFullWarningLogged;
        private bool hasDefaultLevelContext;
        private bool pendingDefaultLevelContextChange = true;
        private int defaultDisplayedLevelNumber;
        private int defaultInternalLevelNumber;
        private string defaultLevelContextSource = "startup";
        private AnalyticsProviderDebugState debugState = AnalyticsProviderDebugState.Unknown;
        private string lastError;

        public FirebaseAnalyticsProvider(
            bool initialCollectionEnabled = true,
            bool enableDebugLogs = false)
        {
            IsCollectionEnabled = initialCollectionEnabled;
            this.enableDebugLogs = enableDebugLogs;
        }

        public bool IsReady { get; private set; }
        public bool IsCollectionEnabled { get; private set; }
        public string ProviderDisplayName => "Firebase Analytics";
        public AnalyticsProviderDebugState DebugState =>
            !IsCollectionEnabled ? AnalyticsProviderDebugState.Disabled : debugState;
        public int PendingEventCount => pendingEvents.Count;
        public string LastError => lastError;

        public void Initialize()
        {
            if (isInitializationStarted)
            {
                return;
            }

            isInitializationStarted = true;
            debugState = AnalyticsProviderDebugState.Initializing;
            lastError = null;

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
            FirebaseAppInitializer.EnsureInitialized(
                () =>
                {
                    IsReady = true;
                    debugState = AnalyticsProviderDebugState.Ready;
                    lastError = null;
                    ApplyPendingDefaultLevelContext();
                    SetUserIdIfAvailable();
                    FirebaseAnalytics.SetAnalyticsCollectionEnabled(IsCollectionEnabled);
                    FlushPendingEvents();
                },
                failureMessage =>
                {
                    IsReady = false;
                    debugState = AnalyticsProviderDebugState.Error;
                    lastError = failureMessage;
                    Debug.LogError($"FirebaseAnalyticsProvider failed to initialize Firebase: {failureMessage}");
                    LogDefaultLevelContextPending(
                        hasDefaultLevelContext ? "set" : "clear");
                });
#else
            LogMissingSdkWarningOnce();
            LogDefaultLevelContextPending(
                hasDefaultLevelContext ? "set" : "clear");
#endif
        }

        public AnalyticsProviderTrackResult Track(
            AnalyticsEvent analyticsEvent,
            bool queueWhenProviderNotReady = true)
        {
            if (analyticsEvent == null)
            {
                throw new ArgumentNullException(nameof(analyticsEvent));
            }

            if (!IsCollectionEnabled)
            {
                LogDebug(
                    $"[Analytics][Firebase][skipped-collection-disabled] event='{analyticsEvent.Name}'.");
                return AnalyticsProviderTrackResult.SkippedCollectionDisabled("Provider collection is disabled.");
            }

#if !USE_FIREBASE_ANALYTICS && !UNITY_IOS
            LogMissingSdkWarningOnce();
            LogDebug(
                $"[Analytics][Firebase][sdk-unavailable] event='{analyticsEvent.Name}', reason='USE_FIREBASE_ANALYTICS is not defined'.");
            return AnalyticsProviderTrackResult.SdkUnavailable("USE_FIREBASE_ANALYTICS is not defined.");
#else
            if (!IsReady)
            {
                if (debugState == AnalyticsProviderDebugState.Error)
                {
                    LogDebug(
                        $"[Analytics][Firebase][error] event='{analyticsEvent.Name}', providerInitializationFailed=true.");
                    return AnalyticsProviderTrackResult.Error(
                        string.IsNullOrWhiteSpace(lastError)
                            ? "Firebase provider initialization failed."
                            : lastError);
                }

                if (queueWhenProviderNotReady)
                {
                    EnqueuePendingEvent(analyticsEvent);
                    LogDebug(
                        $"[Analytics][Firebase][queued] event='{analyticsEvent.Name}', sourceParameterCount={analyticsEvent.Properties?.Count ?? 0}. Final payload will be logged when the SDK is called.");
                    return AnalyticsProviderTrackResult.Queued("Firebase is not ready. Event was queued.");
                }

                LogDebug(
                    $"[Analytics][Firebase][skipped-provider-not-ready] event='{analyticsEvent.Name}', queueRequested=false.");
                return AnalyticsProviderTrackResult.SkippedProviderNotReady(
                    "Firebase is not ready and provider queueing was disabled for this dispatch.");
            }

            return LogEventToFirebase(analyticsEvent);
#endif
        }

        public void SetCollectionEnabled(bool enabled)
        {
            IsCollectionEnabled = enabled;

            if (!enabled)
            {
                pendingEvents.Clear();
            }
            else if (IsReady && debugState == AnalyticsProviderDebugState.Disabled)
            {
                debugState = AnalyticsProviderDebugState.Ready;
            }

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
            if (IsReady)
            {
                FirebaseAnalytics.SetAnalyticsCollectionEnabled(enabled);
            }
#endif
        }

        public void SetDefaultLevelContext(
            int displayedLevelNumber,
            int internalLevelNumber,
            string source)
        {
            if (displayedLevelNumber <= 0 || internalLevelNumber <= 0)
            {
                ClearDefaultLevelContext(source);
                return;
            }

            hasDefaultLevelContext = true;
            defaultDisplayedLevelNumber = displayedLevelNumber;
            defaultInternalLevelNumber = internalLevelNumber;
            defaultLevelContextSource = NormalizeSource(source);
            pendingDefaultLevelContextChange = true;

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
            if (IsReady)
            {
                ApplyPendingDefaultLevelContext();
                return;
            }
#endif

            LogDefaultLevelContextPending("set");
        }

        public void ClearDefaultLevelContext(string source)
        {
            hasDefaultLevelContext = false;
            defaultDisplayedLevelNumber = 0;
            defaultInternalLevelNumber = 0;
            defaultLevelContextSource = NormalizeSource(source);
            pendingDefaultLevelContextChange = true;

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
            if (IsReady)
            {
                ApplyPendingDefaultLevelContext();
                return;
            }
#endif

            LogDefaultLevelContextPending("clear");
        }

        public void ResetAnalyticsData(string source = "analytics_reset")
        {
            pendingEvents.Clear();
            hasDefaultLevelContext = false;
            defaultDisplayedLevelNumber = 0;
            defaultInternalLevelNumber = 0;
            defaultLevelContextSource = NormalizeSource(source);
            pendingDefaultLevelContextChange = true;

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
            if (IsReady)
            {
                FirebaseAnalytics.ResetAnalyticsData();
                ApplyPendingDefaultLevelContext();
            }
#endif
        }

        private void EnqueuePendingEvent(AnalyticsEvent analyticsEvent)
        {
            if (pendingEvents.Count >= MaxPendingEventCount)
            {
                pendingEvents.Dequeue();
                LogPendingQueueFullWarningOnce();
            }

            pendingEvents.Enqueue(analyticsEvent);
            debugState = AnalyticsProviderDebugState.Queued;
            lastError = null;
        }

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
        private void FlushPendingEvents()
        {
            while (IsCollectionEnabled && pendingEvents.Count > 0)
            {
                LogEventToFirebase(pendingEvents.Dequeue());
            }

            if (!IsCollectionEnabled)
            {
                pendingEvents.Clear();
            }
        }

        private AnalyticsProviderTrackResult LogEventToFirebase(AnalyticsEvent analyticsEvent)
        {
            if (!IsValidFirebaseEventName(analyticsEvent.Name))
            {
                string detail = $"Invalid Firebase event name '{analyticsEvent.Name}'.";
                Debug.LogWarning($"FirebaseAnalyticsProvider skipped invalid event name '{analyticsEvent.Name}'.");
                LogDebug(
                    $"[Analytics][Firebase][skipped-invalid-event] event='{analyticsEvent.Name}'.");
                return AnalyticsProviderTrackResult.SkippedInvalid(detail);
            }

            try
            {
                FirebaseParameterBuildResult parameterResult = BuildFirebaseParameters(
                    analyticsEvent.Properties);
                FirebaseAnalytics.LogEvent(analyticsEvent.Name, parameterResult.Parameters);
                LogDebug(
                    $"[Analytics][Firebase][SDK-called] event='{analyticsEvent.Name}', explicitFinalParameters=[{string.Join(", ", parameterResult.DebugParameters)}], managedDefaultLevelContext={CreateDefaultLevelContextDebugDescription()}{parameterResult.CreateDebugSuffix()}. SDK-called does not mean uploaded or visible in GA4 yet.");
                debugState = AnalyticsProviderDebugState.ForwardedToSdk;
                lastError = null;
                return AnalyticsProviderTrackResult.Forwarded(parameterResult.CreateDetail());
            }
            catch (Exception exception)
            {
                debugState = AnalyticsProviderDebugState.Error;
                lastError = exception.Message;
                Debug.LogWarning($"FirebaseAnalyticsProvider failed to log event '{analyticsEvent.Name}': {exception}");
                LogDebug(
                    $"[Analytics][Firebase][error] event='{analyticsEvent.Name}', sdkCallFailed=true.");
                return AnalyticsProviderTrackResult.Error(exception.Message);
            }
        }

        private FirebaseParameterBuildResult BuildFirebaseParameters(
            IReadOnlyDictionary<string, object> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return new FirebaseParameterBuildResult(
                    Array.Empty<Parameter>(),
                    Array.Empty<string>(),
                    0,
                    0,
                    0);
            }

            List<Parameter> parameters = new List<Parameter>(MaxFirebaseParameterCount);
            List<string> debugParameters = new List<string>(MaxFirebaseParameterCount);
            int invalidNameCount = 0;
            int unsupportedValueCount = 0;
            int truncatedCount = 0;
            List<KeyValuePair<string, object>> orderedProperties =
                new List<KeyValuePair<string, object>>(properties);
            orderedProperties.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));

            foreach (KeyValuePair<string, object> property in orderedProperties)
            {
                if (!IsValidFirebaseParameterName(property.Key, out string invalidReason))
                {
                    invalidNameCount++;
                    LogInvalidParameterNameWarningOnce(property.Key, invalidReason);
                    continue;
                }

                if (TryCreateFirebaseParameter(
                        property.Key,
                        property.Value,
                        out Parameter parameter,
                        out string debugParameter))
                {
                    if (parameters.Count >= MaxFirebaseParameterCount)
                    {
                        truncatedCount++;
                        continue;
                    }

                    parameters.Add(parameter);
                    debugParameters.Add(debugParameter);
                }
                else
                {
                    unsupportedValueCount++;
                }
            }

            return new FirebaseParameterBuildResult(
                parameters.ToArray(),
                debugParameters.ToArray(),
                invalidNameCount,
                unsupportedValueCount,
                truncatedCount);
        }

        private bool TryCreateFirebaseParameter(
            string name,
            object value,
            out Parameter parameter,
            out string debugParameter)
        {
            parameter = null;
            debugParameter = null;

            if (string.IsNullOrWhiteSpace(name) || value == null)
            {
                return false;
            }

            if (IsLevelParameter(name)
                && value is int levelNumber)
            {
                string formattedLevel = FormatLevel(levelNumber);
                parameter = new Parameter(name, formattedLevel);
                debugParameter = FormatDebugParameter(name, formattedLevel, "string");
                return true;
            }

            switch (value)
            {
                case string stringValue:
                    parameter = new Parameter(name, stringValue);
                    debugParameter = FormatDebugParameter(name, stringValue, "string");
                    return true;
                case int intValue:
                    parameter = new Parameter(name, (long)intValue);
                    debugParameter = FormatDebugParameter(name, (long)intValue, "long");
                    return true;
                case long longValue:
                    parameter = new Parameter(name, longValue);
                    debugParameter = FormatDebugParameter(name, longValue, "long");
                    return true;
                case float floatValue:
                    parameter = new Parameter(name, (double)floatValue);
                    debugParameter = FormatDebugParameter(name, (double)floatValue, "double");
                    return true;
                case double doubleValue:
                    parameter = new Parameter(name, doubleValue);
                    debugParameter = FormatDebugParameter(name, doubleValue, "double");
                    return true;
                case bool boolValue:
                    parameter = new Parameter(name, boolValue ? 1L : 0L);
                    debugParameter = FormatDebugParameter(name, boolValue ? 1L : 0L, "long");
                    return true;
                default:
                    LogUnsupportedPropertyWarningOnce(name, value.GetType());
                    return false;
            }
        }

        private void SetUserIdIfAvailable()
        {
            if (isUserIdSet || string.IsNullOrEmpty(SystemInfo.deviceUniqueIdentifier))
            {
                return;
            }

            FirebaseAnalytics.SetUserId(SystemInfo.deviceUniqueIdentifier);
            isUserIdSet = true;
        }

        private void ApplyPendingDefaultLevelContext()
        {
            if (!IsReady || !pendingDefaultLevelContextChange)
            {
                return;
            }

            if (hasDefaultLevelContext)
            {
                string displayedLevel = FormatLevel(defaultDisplayedLevelNumber);
                string internalLevel = FormatLevel(defaultInternalLevelNumber);
                FirebaseAnalytics.SetDefaultEventParameters(
                    new Parameter(
                        AnalyticsParameterNames.LevelDisplayedNumber,
                        displayedLevel),
                    new Parameter(
                        AnalyticsParameterNames.LevelNumber,
                        internalLevel));
                LogDebug(
                    $"[Analytics][Firebase][default-context-SDK-applied] action='set', source='{defaultLevelContextSource}', {AnalyticsParameterNames.LevelDisplayedNumber}=\"{displayedLevel}\" (string), {AnalyticsParameterNames.LevelNumber}=\"{internalLevel}\" (string). This configures future SDK events; it does not confirm an automatic purchase event was logged.");
            }
            else
            {
                // Null values remove only the two defaults managed by this system.
                // Passing a null parameter array would clear unrelated defaults too.
                FirebaseAnalytics.SetDefaultEventParameters(
                    new Parameter(
                        AnalyticsParameterNames.LevelDisplayedNumber,
                        (string)null),
                    new Parameter(
                        AnalyticsParameterNames.LevelNumber,
                        (string)null));
                LogDebug(
                    $"[Analytics][Firebase][default-context-SDK-applied] action='clear', source='{defaultLevelContextSource}', managedParameters=['{AnalyticsParameterNames.LevelDisplayedNumber}', '{AnalyticsParameterNames.LevelNumber}']. This does not change collection or consent.");
            }

            pendingDefaultLevelContextChange = false;
        }

        private static bool IsLevelParameter(string parameterName)
        {
            return parameterName == AnalyticsParameterNames.LevelNumber
                || parameterName == AnalyticsParameterNames.LevelDisplayedNumber;
        }

        private static string FormatLevel(int levelNumber)
        {
            return $"level_{levelNumber:000}";
        }
#endif

        private void LogDefaultLevelContextPending(string action)
        {
            if (!enableDebugLogs)
            {
                return;
            }

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
            string reason = "provider-not-ready";
#else
            string reason = "sdk-unavailable";
#endif
            if (hasDefaultLevelContext)
            {
                Debug.Log(
                    $"[Analytics][Firebase][default-context-pending] action='{action}', source='{defaultLevelContextSource}', reason='{reason}', {AnalyticsParameterNames.LevelDisplayedNumber}=\"{FormatLevelForDebug(defaultDisplayedLevelNumber)}\" (string), {AnalyticsParameterNames.LevelNumber}=\"{FormatLevelForDebug(defaultInternalLevelNumber)}\" (string). Only the latest requested context will be applied.");
                return;
            }

            Debug.Log(
                $"[Analytics][Firebase][default-context-pending] action='{action}', source='{defaultLevelContextSource}', reason='{reason}', managedParameters=['{AnalyticsParameterNames.LevelDisplayedNumber}', '{AnalyticsParameterNames.LevelNumber}']. Only the latest clear/set request will be applied.");
        }

        private void LogDebug(string message)
        {
            if (enableDebugLogs)
            {
                Debug.Log(message);
            }
        }

        private static string FormatDebugParameter(
            string name,
            object value,
            string firebaseType)
        {
            if (IsSensitiveParameterName(name))
            {
                return $"{name}=<redacted> ({firebaseType})";
            }

            string formattedValue;
            if (value is string stringValue)
            {
                formattedValue = $"\"{EscapeDebugString(stringValue)}\"";
            }
            else if (value is double doubleValue)
            {
                formattedValue = doubleValue.ToString("R", CultureInfo.InvariantCulture);
            }
            else
            {
                formattedValue = Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return $"{name}={formattedValue} ({firebaseType})";
        }

        private static string EscapeDebugString(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\"", "\\\"");
        }

        private static bool IsSensitiveParameterName(string parameterName)
        {
            return !string.IsNullOrEmpty(parameterName) &&
                   (parameterName.IndexOf("receipt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    parameterName.IndexOf("purchase_token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    parameterName.IndexOf("purchaseToken", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FormatLevelForDebug(int levelNumber)
        {
            return $"level_{levelNumber:000}";
        }

        private string CreateDefaultLevelContextDebugDescription()
        {
            if (!hasDefaultLevelContext)
            {
                return "<cleared>";
            }

            return $"[{AnalyticsParameterNames.LevelDisplayedNumber}=\"{FormatLevelForDebug(defaultDisplayedLevelNumber)}\" (string), {AnalyticsParameterNames.LevelNumber}=\"{FormatLevelForDebug(defaultInternalLevelNumber)}\" (string)]";
        }

        private static string NormalizeSource(string source)
        {
            return string.IsNullOrWhiteSpace(source) ? "unspecified" : source.Trim();
        }

        private static bool IsValidFirebaseParameterName(string parameterName)
        {
            return IsValidFirebaseParameterName(parameterName, out _);
        }

        private static bool IsValidFirebaseParameterName(string parameterName, out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(parameterName))
            {
                reason = "Name is null or empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(parameterName))
            {
                reason = "Name is whitespace-only.";
                return false;
            }

            if (parameterName.Length > 40)
            {
                reason = "Name is longer than 40 characters.";
                return false;
            }

            if (!IsAsciiLetter(parameterName[0]))
            {
                reason = "Name must start with an ASCII letter.";
                return false;
            }

            if (parameterName.StartsWith(FirebaseReservedPrefix, StringComparison.OrdinalIgnoreCase)
                || parameterName.StartsWith(GoogleReservedPrefix, StringComparison.OrdinalIgnoreCase)
                || parameterName.StartsWith(GaReservedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Name uses a reserved prefix.";
                return false;
            }

            for (int i = 1; i < parameterName.Length; i++)
            {
                char character = parameterName[i];
                if (IsAsciiLetter(character) || IsAsciiDigit(character) || character == '_')
                {
                    continue;
                }

                reason = "Name contains unsupported characters.";
                return false;
            }

            return true;
        }

        private static bool IsValidFirebaseEventName(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)
                || eventName.Length > 40
                || eventName.StartsWith(FirebaseReservedPrefix, StringComparison.OrdinalIgnoreCase)
                || !IsAsciiLetter(eventName[0]))
            {
                return false;
            }

            for (int i = 0; i < eventName.Length; i++)
            {
                char character = eventName[i];

                if (IsAsciiLetter(character) || IsAsciiDigit(character) || character == '_')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsAsciiLetter(char character)
        {
            return character >= 'a' && character <= 'z'
                || character >= 'A' && character <= 'Z';
        }

        private static bool IsAsciiDigit(char character)
        {
            return character >= '0' && character <= '9';
        }

        private void LogMissingSdkWarningOnce()
        {
            if (missingSdkWarningLogged)
            {
                return;
            }

            missingSdkWarningLogged = true;
            debugState = AnalyticsProviderDebugState.Error;
            lastError = "USE_FIREBASE_ANALYTICS is not defined.";
            Debug.LogWarning("FirebaseAnalyticsProvider is disabled because USE_FIREBASE_ANALYTICS is not defined.");
        }

        private void LogPendingQueueFullWarningOnce()
        {
            if (pendingQueueFullWarningLogged)
            {
                return;
            }

            pendingQueueFullWarningLogged = true;
            Debug.LogWarning("FirebaseAnalyticsProvider pending queue is full. Oldest pending events will be dropped.");
        }

        private void LogUnsupportedPropertyWarningOnce(string propertyName, Type propertyType)
        {
            string warningKey = $"{propertyName}:{propertyType.FullName}";

            if (!unsupportedPropertyWarnings.Add(warningKey))
            {
                return;
            }

            Debug.LogWarning(
                $"FirebaseAnalyticsProvider skipped unsupported property '{propertyName}' of type '{propertyType.Name}'.");
        }

        private void LogInvalidParameterNameWarningOnce(string parameterName, string reason)
        {
            string displayName = parameterName ?? "<null>";
            string warningKey = $"{displayName}:{reason}";

            if (!invalidParameterNameWarnings.Add(warningKey))
            {
                return;
            }

            Debug.LogWarning(
                $"FirebaseAnalyticsProvider skipped invalid parameter name '{displayName}': {reason}");
        }

#if USE_FIREBASE_ANALYTICS || UNITY_IOS
        private sealed class FirebaseParameterBuildResult
        {
            public FirebaseParameterBuildResult(
                Parameter[] parameters,
                string[] debugParameters,
                int invalidNameCount,
                int unsupportedValueCount,
                int truncatedCount)
            {
                Parameters = parameters;
                DebugParameters = debugParameters;
                InvalidNameCount = invalidNameCount;
                UnsupportedValueCount = unsupportedValueCount;
                TruncatedCount = truncatedCount;
            }

            public Parameter[] Parameters { get; }
            public string[] DebugParameters { get; }
            public int InvalidNameCount { get; }
            public int UnsupportedValueCount { get; }
            public int TruncatedCount { get; }

            public string CreateDetail()
            {
                List<string> parts = null;
                AddPart(ref parts, InvalidNameCount, "invalid parameter", "invalid parameters", "skipped");
                AddPart(ref parts, UnsupportedValueCount, "unsupported value", "unsupported values", "skipped");
                AddPart(ref parts, TruncatedCount, "parameter", "parameters", "omitted because the 25 parameter limit was reached");

                if (parts == null || parts.Count == 0)
                {
                    return null;
                }

                return $"Forwarded to Firebase with {string.Join(" and ", parts)}.";
            }

            public string CreateDebugSuffix()
            {
                string detail = CreateDetail();
                return string.IsNullOrEmpty(detail)
                    ? string.Empty
                    : $", conversionDetail='{EscapeDebugString(detail)}'";
            }

            private static void AddPart(
                ref List<string> parts,
                int count,
                string singular,
                string plural,
                string suffix)
            {
                if (count <= 0)
                {
                    return;
                }

                parts = parts ?? new List<string>();
                parts.Add($"{count} {(count == 1 ? singular : plural)} {suffix}");
            }
        }
#endif
    }
}

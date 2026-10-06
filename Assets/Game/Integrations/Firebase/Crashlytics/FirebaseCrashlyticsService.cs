using System;
using System.Collections.Generic;
using Game.Integrations.Firebase.Core;
using Game.Shared.CrashReporting.Core;
using UnityEngine;

#if USE_FIREBASE_CRASHLYTICS
using FirebaseCrashlytics = Firebase.Crashlytics.Crashlytics;
#endif

namespace Game.Integrations.Firebase.Crashlytics
{
    public sealed class FirebaseCrashlyticsService : ICrashReportingService
    {
        private const int MaxPendingOperationCount = 100;

        private readonly Queue<Action> pendingOperations = new Queue<Action>(MaxPendingOperationCount);
        private readonly bool reportUncaughtExceptionsAsFatal;
        private readonly bool enableDebugLogs;

        private bool isInitializationStarted;
        private bool pendingQueueFullWarningLogged;
        private bool missingSdkWarningLogged;

        public FirebaseCrashlyticsService(
            bool initialCollectionEnabled,
            bool reportUncaughtExceptionsAsFatal,
            bool enableDebugLogs)
        {
            IsCollectionEnabled = initialCollectionEnabled;
            this.reportUncaughtExceptionsAsFatal = reportUncaughtExceptionsAsFatal;
            this.enableDebugLogs = enableDebugLogs;
        }

        public bool IsReady { get; private set; }
        public bool IsCollectionEnabled { get; private set; }

        public void Initialize()
        {
            if (isInitializationStarted)
            {
                return;
            }

            isInitializationStarted = true;

#if USE_FIREBASE_CRASHLYTICS
            FirebaseAppInitializer.EnsureInitialized(
                () =>
                {
                    ApplyConfiguration();
                    IsReady = true;
                    FlushPendingOperations();

                    if (enableDebugLogs)
                    {
                        Debug.Log("FirebaseCrashlyticsService initialized.");
                    }
                },
                failureMessage =>
                {
                    IsReady = false;
                    Debug.LogError($"FirebaseCrashlyticsService failed to initialize Firebase: {failureMessage}");
                });
#else
            LogMissingSdkWarningOnce();
#endif
        }

        public void SetCollectionEnabled(bool enabled)
        {
            IsCollectionEnabled = enabled;

            if (!enabled)
            {
                pendingOperations.Clear();
            }

#if USE_FIREBASE_CRASHLYTICS
            if (IsReady)
            {
                TryInvokeCrashlytics(
                    () => FirebaseCrashlytics.IsCrashlyticsCollectionEnabled = enabled,
                    "set Crashlytics collection state");
            }
#endif
        }

        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || !IsCollectionEnabled)
            {
                return;
            }

#if USE_FIREBASE_CRASHLYTICS
            if (IsReady)
            {
                TryInvokeCrashlytics(
                    () => FirebaseCrashlytics.Log(message),
                    "log Crashlytics message");
                return;
            }

            EnqueuePendingOperation(() => FirebaseCrashlytics.Log(message));
#endif
        }

        public void LogException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (!IsCollectionEnabled)
            {
                return;
            }

#if USE_FIREBASE_CRASHLYTICS
            if (IsReady)
            {
                TryInvokeCrashlytics(
                    () => FirebaseCrashlytics.LogException(exception),
                    "log Crashlytics exception");
                return;
            }

            EnqueuePendingOperation(() => FirebaseCrashlytics.LogException(exception));
#endif
        }

        public void SetCustomKey(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || !IsCollectionEnabled)
            {
                return;
            }

            string capturedKey = key;
            string capturedValue = value ?? string.Empty;

#if USE_FIREBASE_CRASHLYTICS
            if (IsReady)
            {
                TryInvokeCrashlytics(
                    () => FirebaseCrashlytics.SetCustomKey(capturedKey, capturedValue),
                    "set Crashlytics custom key");
                return;
            }

            EnqueuePendingOperation(() => FirebaseCrashlytics.SetCustomKey(capturedKey, capturedValue));
#endif
        }

        public void SetUserId(string userId)
        {
            if (!IsCollectionEnabled)
            {
                return;
            }

            string capturedUserId = userId ?? string.Empty;

#if USE_FIREBASE_CRASHLYTICS
            if (IsReady)
            {
                TryInvokeCrashlytics(
                    () => FirebaseCrashlytics.SetUserId(capturedUserId),
                    "set Crashlytics user id");
                return;
            }

            EnqueuePendingOperation(() => FirebaseCrashlytics.SetUserId(capturedUserId));
#endif
        }

        public void ClearUserId()
        {
            SetUserId(string.Empty);
        }

#if USE_FIREBASE_CRASHLYTICS
        private void ApplyConfiguration()
        {
            TryInvokeCrashlytics(
                () =>
                {
                    FirebaseCrashlytics.ReportUncaughtExceptionsAsFatal = reportUncaughtExceptionsAsFatal;
                    FirebaseCrashlytics.IsCrashlyticsCollectionEnabled = IsCollectionEnabled;
                },
                "apply Crashlytics configuration");
        }

        private void FlushPendingOperations()
        {
            while (IsCollectionEnabled && pendingOperations.Count > 0)
            {
                Action operation = pendingOperations.Dequeue();
                TryInvokeCrashlytics(operation, "flush pending Crashlytics operation");
            }

            if (!IsCollectionEnabled)
            {
                pendingOperations.Clear();
            }
        }

        private void EnqueuePendingOperation(Action operation)
        {
            if (!IsCollectionEnabled || operation == null)
            {
                return;
            }

            if (pendingOperations.Count >= MaxPendingOperationCount)
            {
                pendingOperations.Dequeue();
                LogPendingQueueFullWarningOnce();
            }

            pendingOperations.Enqueue(operation);
        }

        private static void TryInvokeCrashlytics(Action operation, string operationName)
        {
            try
            {
                operation.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"FirebaseCrashlyticsService failed to {operationName}: {exception}");
            }
        }
#endif

        private void LogPendingQueueFullWarningOnce()
        {
            if (pendingQueueFullWarningLogged)
            {
                return;
            }

            pendingQueueFullWarningLogged = true;
            Debug.LogWarning("FirebaseCrashlyticsService pending queue is full. Oldest pending operations will be dropped.");
        }

        private void LogMissingSdkWarningOnce()
        {
            if (missingSdkWarningLogged || !enableDebugLogs)
            {
                return;
            }

            missingSdkWarningLogged = true;
            Debug.LogWarning("FirebaseCrashlyticsService is disabled because USE_FIREBASE_CRASHLYTICS is not defined.");
        }
    }
}

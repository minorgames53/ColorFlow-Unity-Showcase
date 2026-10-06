using System;
using System.Collections.Generic;
using Game.Shared.Config;
using UnityEngine;

using Firebase;
using Firebase.Extensions;

namespace Game.Integrations.Firebase.Core
{
    public static class FirebaseAppInitializer
    {
        private enum InitializationState
        {
            NotStarted,
            Initializing,
            Ready,
            Failed
        }

        private static readonly List<Action> readyCallbacks = new List<Action>();
        private static readonly List<Action<string>> failureCallbacks = new List<Action<string>>();

        private static InitializationState state = InitializationState.NotStarted;
        private static string failureMessage;
        private static int initializationGeneration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            initializationGeneration++;
            state = InitializationState.NotStarted;
            failureMessage = null;
            readyCallbacks.Clear();
            failureCallbacks.Clear();
        }

        public static void EnsureInitialized(
            Action onReady,
            Action<string> onFailure = null)
        {
            if (!FeatureConfig.ExternalServicesEnabled)
            {
                InvokeFailureCallback(onFailure, "External services are disabled for the public showcase.");
                return;
            }

            if (state == InitializationState.Ready)
            {
                InvokeReadyCallback(onReady);
                return;
            }

            if (state == InitializationState.Failed)
            {
                InvokeFailureCallback(onFailure, failureMessage);
                return;
            }

            if (onReady != null)
            {
                readyCallbacks.Add(onReady);
            }

            if (onFailure != null)
            {
                failureCallbacks.Add(onFailure);
            }

            if (state == InitializationState.Initializing)
            {
                return;
            }

            state = InitializationState.Initializing;
            StartDependencyCheck();
        }

        private static void StartDependencyCheck()
        {
            int generation = initializationGeneration;
            FirebaseApp.CheckAndFixDependenciesAsync()
                .ContinueWithOnMainThread(task =>
                {
                    if (generation != initializationGeneration) return;
                    if (task.IsCanceled)
                    {
                        CompleteFailure("Firebase dependency check was canceled.");
                        return;
                    }

                    if (task.IsFaulted)
                    {
                        CompleteFailure($"Firebase dependency check failed: {task.Exception}");
                        return;
                    }

                    DependencyStatus dependencyStatus = task.Result;

                    if (dependencyStatus != DependencyStatus.Available)
                    {
                        CompleteFailure($"Firebase dependencies are not available: {dependencyStatus}");
                        return;
                    }

                    try
                    {
                        FirebaseApp defaultInstance = FirebaseApp.DefaultInstance;

                        if (defaultInstance == null)
                        {
                            CompleteFailure("FirebaseApp.DefaultInstance is null.");
                            return;
                        }
                    }
                    catch (Exception exception)
                    {
                        CompleteFailure($"FirebaseApp.DefaultInstance could not be accessed: {exception}");
                        return;
                    }

                    CompleteReady();
                });
        }

        private static void CompleteReady()
        {
            state = InitializationState.Ready;
            failureMessage = null;

            Action[] callbacks = readyCallbacks.ToArray();
            readyCallbacks.Clear();
            failureCallbacks.Clear();

            for (int i = 0; i < callbacks.Length; i++)
            {
                InvokeReadyCallback(callbacks[i]);
            }
        }

        private static void CompleteFailure(string message)
        {
            state = InitializationState.Failed;
            failureMessage = string.IsNullOrWhiteSpace(message)
                ? "Firebase initialization failed."
                : message;

            Action<string>[] callbacks = failureCallbacks.ToArray();
            readyCallbacks.Clear();
            failureCallbacks.Clear();

            for (int i = 0; i < callbacks.Length; i++)
            {
                InvokeFailureCallback(callbacks[i], failureMessage);
            }
        }

        private static void InvokeReadyCallback(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Firebase ready callback failed: {exception}");
            }
        }

        private static void InvokeFailureCallback(Action<string> callback, string message)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback.Invoke(message);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Firebase failure callback failed: {exception}");
            }
        }
    }
}

using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections;
using Google.Play.Common;
using Google.Play.Review;
#endif
using UnityEngine;

namespace Game.Integrations.RateUs
{
    public static class AppReviewRequest
    {
        public static bool TryStart()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return TryStartIosReview();
#else
            Debug.Log("App Review requires a supported platform flow and coroutine host.");
            return false;
#endif
        }

        public static bool TryStart(
            MonoBehaviour coroutineHost,
            Action<bool> onFinished)
        {
#if UNITY_IOS && !UNITY_EDITOR
            bool started = TryStartIosReview();
            if (started)
            {
                InvokeFinishedSafely(onFinished, true);
            }

            return started;
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (coroutineHost == null || !coroutineHost.isActiveAndEnabled)
            {
                return false;
            }

            try
            {
                coroutineHost.StartCoroutine(RequestAndroidReview(onFinished));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Google Play In-App Review could not start: {exception.Message}");
                return false;
            }
#else
            Debug.Log("App Review is skipped on the current platform.");
            return false;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        private static bool TryStartIosReview()
        {
            try
            {
                return UnityEngine.iOS.Device.RequestStoreReview();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"App Review request could not start: {exception.Message}");
                return false;
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IEnumerator RequestAndroidReview(Action<bool> onFinished)
        {
            ReviewManager reviewManager;
            PlayAsyncOperation<PlayReviewInfo, ReviewErrorCode> requestOperation;
            try
            {
                reviewManager = new ReviewManager();
                requestOperation = reviewManager.RequestReviewFlow();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Google Play In-App Review request failed to start: {exception.Message}");
                InvokeFinishedSafely(onFinished, false);
                yield break;
            }

            if (requestOperation == null)
            {
                Debug.LogWarning(
                    "Google Play In-App Review request returned no operation.");
                InvokeFinishedSafely(onFinished, false);
                yield break;
            }

            yield return requestOperation;
            if (!requestOperation.IsSuccessful)
            {
                Debug.LogWarning(
                    $"Google Play In-App Review request failed: {requestOperation.Error}");
                InvokeFinishedSafely(onFinished, false);
                yield break;
            }

            PlayReviewInfo reviewInfo;
            try
            {
                reviewInfo = requestOperation.GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Google Play In-App Review info was unavailable: {exception.Message}");
                InvokeFinishedSafely(onFinished, false);
                yield break;
            }

            PlayAsyncOperation<VoidResult, ReviewErrorCode> launchOperation;
            try
            {
                launchOperation = reviewManager.LaunchReviewFlow(reviewInfo);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Google Play In-App Review launch failed to start: {exception.Message}");
                InvokeFinishedSafely(onFinished, false);
                yield break;
            }

            if (launchOperation == null)
            {
                Debug.LogWarning(
                    "Google Play In-App Review launch returned no operation.");
                InvokeFinishedSafely(onFinished, false);
                yield break;
            }

            yield return launchOperation;
            if (!launchOperation.IsSuccessful)
            {
                Debug.LogWarning(
                    $"Google Play In-App Review launch failed: {launchOperation.Error}");
                InvokeFinishedSafely(onFinished, false);
                yield break;
            }

            InvokeFinishedSafely(onFinished, true);
        }
#endif

        private static void InvokeFinishedSafely(
            Action<bool> onFinished,
            bool succeeded)
        {
            if (onFinished == null)
            {
                return;
            }

            try
            {
                onFinished.Invoke(succeeded);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"App Review completion callback failed: {exception.Message}");
            }
        }
    }
}

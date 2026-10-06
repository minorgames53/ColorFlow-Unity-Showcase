using System;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using UnityEngine;

namespace Game.Shared.Ads.Core
{
    internal sealed class AdsLogger
    {
        private readonly bool enabled;

        public AdsLogger(bool enabled)
        {
            this.enabled = enabled;
        }

        public void Log(string message)
        {
            if (enabled)
            {
                Debug.Log($"[Ads] {message}");
            }
        }

        public void Log(AdPlacement placement, string message)
        {
            if (enabled)
            {
                Debug.Log($"[Ads][{placement}] {message}");
            }
        }

        public void Warning(string message)
        {
            if (enabled)
            {
                Debug.LogWarning($"[Ads] {message}");
            }
        }

        public void Warning(AdPlacement placement, string message)
        {
            if (enabled)
            {
                Debug.LogWarning($"[Ads][{placement}] {message}");
            }
        }

        public void Error(string message)
        {
            Debug.LogError($"[Ads] {message}");
        }

        public void LoadFailed(AdPlacement placement, LoadAdError error)
        {
            if (!enabled)
            {
                return;
            }

            string response = BuildResponseSummary(error?.GetResponseInfo());
            Debug.LogWarning(
                $"[Ads][{placement}] Load failed: {BuildErrorSummary(error)}{response}");
        }

        public void ShowFailed(AdPlacement placement, AdError error)
        {
            if (!enabled)
            {
                return;
            }

            Debug.LogWarning(
                $"[Ads][{placement}] Fullscreen show failed: {BuildErrorSummary(error)}");
        }

        public void Response(AdPlacement placement, ResponseInfo responseInfo)
        {
            if (!enabled)
            {
                return;
            }

            string summary = BuildResponseSummary(responseInfo);
            if (!string.IsNullOrEmpty(summary))
            {
                Debug.Log($"[Ads][{placement}] Loaded{summary}");
            }
            else
            {
                Debug.Log($"[Ads][{placement}] Loaded");
            }
        }

        private static string BuildErrorSummary(AdError error)
        {
            return error == null
                ? "unknown error"
                : $"domain={error.GetDomain()}, code={error.GetCode()}, message={error.GetMessage()}";
        }

        private static string BuildResponseSummary(ResponseInfo responseInfo)
        {
            if (responseInfo == null)
            {
                return string.Empty;
            }

            string adapter = responseInfo.GetMediationAdapterClassName();
            string responseId = responseInfo.GetResponseId();
            return $" (adapter={NullFallback(adapter)}, responseId={NullFallback(responseId)})";
        }

        private static string NullFallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "none" : value;
        }
    }

    internal static class AdsMainThread
    {
        public static void EnsureExecutorInitialized()
        {
            if (!MobileAdsEventExecutor.IsActive())
            {
                MobileAdsEventExecutor.Initialize();
            }
        }

        public static void Execute(Action action)
        {
            if (action == null)
            {
                return;
            }

            MobileAdsEventExecutor.ExecuteInUpdate(action);
        }
    }
}

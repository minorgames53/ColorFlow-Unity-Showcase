using System;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace Game.Shared.Support
{
    [DisallowMultipleComponent]
    public sealed class SupportMailService : MonoBehaviour
    {
        [SerializeField] private string supportEmail = "";
        [SerializeField] private string gameDisplayName = "Color Flow";

        public void OpenFeedbackMail()
        {
            if (!Game.Shared.Config.FeatureConfig.ExternalServicesEnabled)
            {
                Debug.Log("Support email is disabled in the public showcase.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(supportEmail))
            {
                Debug.LogError($"{nameof(SupportMailService)} cannot open support email because support email is empty.", this);
                return;
            }

            string unknownText = LocalizationSettings.StringDatabase.GetLocalizedString("General", "support.unknown");
            string version = string.IsNullOrWhiteSpace(Application.version)
                ? unknownText
                : Application.version;
            string displayName = string.IsNullOrWhiteSpace(gameDisplayName)
                ? "Color Flow"
                : gameDisplayName;

            string subject = string.Format(LocalizationSettings.StringDatabase.GetLocalizedString(
                "General", "support.feedback_subject"), displayName, version);
            string body = $"\r\n\r\nUID: {SystemInfo.deviceUniqueIdentifier}";
            string url = $"mailto:{supportEmail.Trim()}?subject={Uri.EscapeDataString(subject)}" +
                         $"&body={Uri.EscapeDataString(body)}";

            Debug.Log($"Opening support email: {subject}", this);
            Application.OpenURL(url);
        }
    }
}

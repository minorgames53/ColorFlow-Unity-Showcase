namespace Game.Shared.DeveloperTools
{
    public static class DeveloperPanelAvailability
    {
        public static bool IsAllowed =>
            UnityEngine.Application.isPlaying &&
            DeveloperPanelBootstrap.Instance != null &&
            DeveloperPanelBootstrap.Instance.IsUnlocked &&
            DeveloperPanelBootstrap.Instance.IsDeviceAuthorized;
    }
}

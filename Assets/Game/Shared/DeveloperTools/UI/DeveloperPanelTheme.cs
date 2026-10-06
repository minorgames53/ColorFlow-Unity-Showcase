using UnityEngine;

namespace Game.Shared.DeveloperTools.UI
{
    public sealed class DeveloperPanelTheme
    {
        public Color OverlayBackground { get; private set; }
        public Color PanelBackground { get; private set; }
        public Color CardBackground { get; private set; }
        public Color CardBackgroundSecondary { get; private set; }
        public Color Border { get; private set; }
        public Color TextPrimary { get; private set; }
        public Color TextSecondary { get; private set; }
        public Color Accent { get; private set; }
        public Color Success { get; private set; }
        public Color Warning { get; private set; }
        public Color Error { get; private set; }
        public Color Disabled { get; private set; }

        public float PanelPadding { get; private set; }
        public float SectionSpacing { get; private set; }
        public float CardSpacing { get; private set; }
        public float CornerRadius { get; private set; }
        public int CanvasSortingOrder { get; private set; }

        public static DeveloperPanelTheme CreateDefault()
        {
            return new DeveloperPanelTheme
            {
                OverlayBackground = new Color(0.01f, 0.015f, 0.025f, 0.72f),
                PanelBackground = new Color(0.035f, 0.055f, 0.085f, 0.98f),
                CardBackground = new Color(0.07f, 0.10f, 0.15f, 1f),
                CardBackgroundSecondary = new Color(0.095f, 0.13f, 0.19f, 1f),
                Border = new Color(0.20f, 0.29f, 0.38f, 1f),
                TextPrimary = new Color(0.94f, 0.97f, 1f, 1f),
                TextSecondary = new Color(0.62f, 0.70f, 0.78f, 1f),
                Accent = new Color(0.08f, 0.82f, 0.92f, 1f),
                Success = new Color(0.28f, 0.82f, 0.48f, 1f),
                Warning = new Color(0.98f, 0.74f, 0.25f, 1f),
                Error = new Color(0.95f, 0.30f, 0.32f, 1f),
                Disabled = new Color(0.32f, 0.38f, 0.45f, 1f),
                PanelPadding = 28f,
                SectionSpacing = 18f,
                CardSpacing = 12f,
                CornerRadius = 8f,
                CanvasSortingOrder = 32000
            };
        }
    }
}

using Game.Shared.DeveloperTools.UI;

namespace Game.Shared.DeveloperTools
{
    public sealed class DeveloperPanelContext
    {
        public DeveloperPanelContext(
            DeveloperPanelTheme theme,
            DeveloperPanelUIFactory uiFactory)
        {
            Theme = theme;
            UIFactory = uiFactory;
        }

        public DeveloperPanelTheme Theme { get; }
        public DeveloperPanelUIFactory UIFactory { get; }
    }
}

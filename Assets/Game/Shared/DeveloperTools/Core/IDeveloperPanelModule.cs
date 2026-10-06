using Game.Shared.DeveloperTools.UI;
using UnityEngine;

namespace Game.Shared.DeveloperTools
{
    public interface IDeveloperPanelModule
    {
        string Id { get; }
        string DisplayName { get; }

        void Build(Transform parent, DeveloperPanelContext context);
        void OnOpened();
        void OnClosed();
        void Refresh();
    }
}

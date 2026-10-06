using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.UI
{
    public sealed class DeveloperPanelUIReferences
    {
        public DeveloperPanelUIReferences(
            GameObject root,
            GameObject panel,
            GameObject floatingDebugButtonRoot,
            Transform moduleContainer,
            Transform tabContainer,
            Button floatingDebugButton,
            Button closeButton,
            TextMeshProUGUI sceneValueText,
            TextMeshProUGUI buildValueText)
        {
            Root = root;
            Panel = panel;
            FloatingDebugButtonRoot = floatingDebugButtonRoot;
            ModuleContainer = moduleContainer;
            TabContainer = tabContainer;
            FloatingDebugButton = floatingDebugButton;
            CloseButton = closeButton;
            SceneValueText = sceneValueText;
            BuildValueText = buildValueText;
        }

        public GameObject Root { get; }
        public GameObject Panel { get; }
        public GameObject FloatingDebugButtonRoot { get; }
        public Transform ModuleContainer { get; }
        public Transform TabContainer { get; }
        public Button FloatingDebugButton { get; }
        public Button CloseButton { get; }
        public TextMeshProUGUI SceneValueText { get; }
        public TextMeshProUGUI BuildValueText { get; }
    }
}

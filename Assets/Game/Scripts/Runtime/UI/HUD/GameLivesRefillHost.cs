using Game.Shared.Lives.UI;
using Gameplay.UI.Shop;
using UnityEngine;

namespace Gameplay.UI.HUD
{
    [DisallowMultipleComponent]
    public sealed class GameLivesRefillHost : MonoBehaviour
    {
        [SerializeField] private LivesRefillPanelController refillPanelController;
        [SerializeField] private GameShopPanelController gameShopPanelController;

        private void OnEnable()
        {
            if (refillPanelController != null)
            {
                refillPanelController.InsufficientGold -= HandleInsufficientGold;
                refillPanelController.InsufficientGold += HandleInsufficientGold;
            }
        }

        private void OnDisable()
        {
            if (refillPanelController != null)
            {
                refillPanelController.InsufficientGold -= HandleInsufficientGold;
            }
        }

        public bool Open()
        {
            if (refillPanelController == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameLivesRefillHost)} cannot open Life Panel because no {nameof(LivesRefillPanelController)} is assigned.",
                    this);
                return false;
            }

            return refillPanelController.Open();
        }

        public bool OpenOverCurrentPanel()
        {
            if (refillPanelController == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameLivesRefillHost)} cannot open Life Panel because no {nameof(LivesRefillPanelController)} is assigned.",
                    this);
                return false;
            }

            return refillPanelController.OpenOverCurrentPanel();
        }

        private void HandleInsufficientGold()
        {
            if (gameShopPanelController == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameLivesRefillHost)} cannot open Shop because no {nameof(GameShopPanelController)} is assigned.",
                    this);
                return;
            }

            if (!refillPanelController.Close(gameShopPanelController.OpenForInsufficientGold))
            {
                gameShopPanelController.OpenForInsufficientGold();
            }
        }
    }
}

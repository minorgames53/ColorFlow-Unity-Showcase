using Game.Shared.Lives.UI;
using UnityEngine;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuLivesRefillHost : MonoBehaviour
    {
        [SerializeField] private LivesRefillPanelController refillPanelController;
        [SerializeField] private MenuBottomNavigationController bottomNavigation;

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
                    $"{nameof(MenuLivesRefillHost)} cannot open Life Panel because no {nameof(LivesRefillPanelController)} is assigned.",
                    this);
                return false;
            }

            return refillPanelController.Open();
        }

        private void HandleInsufficientGold()
        {
            if (bottomNavigation == null)
            {
                Debug.LogWarning(
                    $"{nameof(MenuLivesRefillHost)} cannot select Shop because no {nameof(MenuBottomNavigationController)} is assigned.",
                    this);
                return;
            }

            if (!refillPanelController.Close(bottomNavigation.SelectShop))
            {
                bottomNavigation.SelectShop();
            }
        }
    }
}

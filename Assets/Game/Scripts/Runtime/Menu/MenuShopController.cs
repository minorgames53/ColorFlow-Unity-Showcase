using Game.Shared.Store.UI;
using UnityEngine;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuShopController : MonoBehaviour
    {
        [SerializeField] private ShopContentController shopContent;

        private void OnEnable()
        {
            shopContent?.RefreshAll();
        }
    }
}

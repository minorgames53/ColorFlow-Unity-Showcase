using System;
using Game.Shared.Save;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Game.Shared.Store
{
    internal static class TenjinPurchaseValidation
    {
        // Snapshot while PendingOrder still owns the consumable receipt. Revenue is
        // dispatched only after StoreManager receives PurchaseConfirmed/acknowledgement.
        internal static void Capture(StorePurchase purchase, SaveManager save)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (!(purchase.NativeOrder is PendingOrder order)) return;
            var items = order.CartOrdered?.Items();
            if (items == null || items.Count != 1 || items[0].Quantity <= 0) return;
            if (double.IsNaN(purchase.LocalizedPriceValue) || double.IsInfinity(purchase.LocalizedPriceValue) ||
                purchase.LocalizedPriceValue < 0 || string.IsNullOrWhiteSpace(purchase.Currency)) return;
            try
            {
                string receipt;
                string signature = null;
#if UNITY_ANDROID
                var wrapper = JObject.Parse(purchase.Receipt);
                if ((string)wrapper["Store"] != "GooglePlay") return;
                var payload = JObject.Parse((string)wrapper["Payload"]);
                receipt = (string)payload["json"];
                signature = (string)payload["signature"];
                if (string.IsNullOrWhiteSpace(signature)) return;
                // Play purchaseState 0 = purchased; deferred/unpaid orders are not revenue.
                var googlePurchase = JObject.Parse(receipt);
                if ((int?)googlePurchase["purchaseState"] != 0) return;
#else
                receipt = order.Info.Apple?.jwsRepresentation;
                if (string.IsNullOrEmpty(receipt)) receipt = order.Info.Apple?.AppReceipt;
#endif
                if (string.IsNullOrWhiteSpace(receipt)) return;
                save.CaptureTenjinPurchase(purchase.TransactionId, receipt, signature, items[0].Quantity);
                save.TrySave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Tenjin] IAP validation snapshot unavailable: " + ex.GetType().Name);
            }
#endif
        }
    }
}

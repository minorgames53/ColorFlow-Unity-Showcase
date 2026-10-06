using System;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Game.Shared.Analytics.Core
{
    internal static class AdRevenueAnalytics
    {
        internal static void Track(AdValue value, ResponseInfo response, string adUnitId, string format, string placement)
        {
            if (value == null || response == null || value.Value < 0 || string.IsNullOrWhiteSpace(value.CurrencyCode)) return;
            // Exact Tenjin 1.21.0 AdMob adapter schema. iOS expects units in this field,
            // whereas Android expects micros. Do not apply a second conversion.
            var payload = new AdMobRevenue
            {
                ad_unit_id = adUnitId,
                currency_code = value.CurrencyCode,
                response_id = response.GetResponseId(),
                precision_type = value.Precision.ToString(),
                mediation_adapter_class_name = response.GetMediationAdapterClassName(),
#if UNITY_IOS
                value_micros = value.Value / 1000000d
#else
                value_micros = value.Value
#endif
            };
            // Format/placement are diagnostic context. The official AdMob schema has
            // no such fields: retain its exact payload rather than inventing keys.
            AnalyticsBootstrap.Instance?.TrackAdRevenue(JsonUtility.ToJson(payload), format,
                value.CurrencyCode, value.Value / 1000000d, adUnitId, placement,
                payload.mediation_adapter_class_name);
        }

        [Serializable]
        private sealed class AdMobRevenue
        {
            public string ad_unit_id, currency_code, response_id, precision_type, mediation_adapter_class_name;
#if UNITY_IOS
            public double value_micros;
#else
            public long value_micros;
#endif
        }
    }
}

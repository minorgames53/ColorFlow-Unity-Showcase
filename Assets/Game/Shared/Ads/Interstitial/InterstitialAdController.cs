using System;
using Game.Shared.Ads.Core;
using UnityEngine;

namespace Game.Shared.Ads.Interstitial
{
    internal sealed class InterstitialAdController : IDisposable
    {
        private readonly InterstitialAdSlot slot;

        public InterstitialAdController(
            MonoBehaviour coroutineHost,
            AdsConfig config,
            AdsLogger logger,
            Action<AdPlacement, bool> availabilityChanged)
        {
            config.TryGetAdUnitId(AdPlacement.InterstitialGameToMenu, out string adUnitId);
            slot = new InterstitialAdSlot(
                coroutineHost,
                config,
                AdPlacement.InterstitialGameToMenu,
                adUnitId,
                logger,
                availabilityChanged);
        }

        public void Preload()
        {
            slot.Load();
        }

        public bool Load(AdPlacement placement)
        {
            return placement == AdPlacement.InterstitialGameToMenu && slot.Load();
        }

        public bool IsReady(AdPlacement placement)
        {
            return placement == AdPlacement.InterstitialGameToMenu && slot.IsReady;
        }

        public AdLoadState GetState(AdPlacement placement)
        {
            return placement == AdPlacement.InterstitialGameToMenu
                ? slot.State
                : AdLoadState.Idle;
        }

        public bool Show(AdPlacement placement, Action<AdShowResult> onFinished, Action onOpened = null)
        {
            return placement == AdPlacement.InterstitialGameToMenu && slot.Show(onFinished, onOpened);
        }

        public void CancelShow() => slot.CancelShow();

        public void Dispose()
        {
            slot.Dispose();
        }
    }
}

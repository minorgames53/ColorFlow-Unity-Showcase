using System;
using System.Collections.Generic;
using Game.Shared.Ads.Core;
using UnityEngine;

namespace Game.Shared.Ads.Rewarded
{
    internal sealed class RewardedAdController : IDisposable
    {
        private readonly Dictionary<AdPlacement, RewardedAdSlot> slots =
            new Dictionary<AdPlacement, RewardedAdSlot>();

        public RewardedAdController(
            MonoBehaviour coroutineHost,
            AdsConfig config,
            AdsLogger logger,
            Action<AdPlacement, bool> availabilityChanged)
        {
            CreateSlot(
                coroutineHost,
                config,
                logger,
                availabilityChanged,
                AdPlacement.RewardedLife);
            CreateSlot(
                coroutineHost,
                config,
                logger,
                availabilityChanged,
                AdPlacement.RewardedDoubleGold);
        }

        public void PreloadAll()
        {
            foreach (RewardedAdSlot slot in slots.Values)
            {
                slot.Load();
            }
        }

        public bool Load(AdPlacement placement)
        {
            return slots.TryGetValue(placement, out RewardedAdSlot slot) && slot.Load();
        }

        public bool IsReady(AdPlacement placement)
        {
            return slots.TryGetValue(placement, out RewardedAdSlot slot) && slot.IsReady;
        }

        public AdLoadState GetState(AdPlacement placement)
        {
            return slots.TryGetValue(placement, out RewardedAdSlot slot)
                ? slot.State
                : AdLoadState.Idle;
        }

        public bool Show(
            AdPlacement placement,
            Action onRewardEarned,
            Action<AdShowResult> onFinished)
        {
            return slots.TryGetValue(placement, out RewardedAdSlot slot) &&
                   slot.Show(onRewardEarned, onFinished);
        }

        public void Dispose()
        {
            foreach (RewardedAdSlot slot in slots.Values)
            {
                slot.Dispose();
            }

            slots.Clear();
        }

        private void CreateSlot(
            MonoBehaviour coroutineHost,
            AdsConfig config,
            AdsLogger logger,
            Action<AdPlacement, bool> availabilityChanged,
            AdPlacement placement)
        {
            config.TryGetAdUnitId(placement, out string adUnitId);
            slots.Add(
                placement,
                new RewardedAdSlot(
                    coroutineHost,
                    config,
                    placement,
                    adUnitId,
                    logger,
                    availabilityChanged));
        }
    }
}

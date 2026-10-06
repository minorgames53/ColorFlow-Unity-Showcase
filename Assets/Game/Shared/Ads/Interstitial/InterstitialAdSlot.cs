using System;
using System.Collections;
using Game.Shared.Ads.Core;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Game.Shared.Ads.Interstitial
{
    internal sealed class InterstitialAdSlot : IDisposable
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly AdsConfig config;
        private readonly AdPlacement placement;
        private readonly string adUnitId;
        private readonly AdsLogger logger;
        private readonly Action<AdPlacement, bool> availabilityChanged;

        private InterstitialAd ad;
        private DateTime loadedAtUtc;
        private Coroutine retryCoroutine;
        private InterstitialShowSession activeSession;
        private int retryCount;
        private int loadGeneration;
        private bool isDisposed;
        private bool paidEventTracked;
        private bool lastAvailability;

        private Action<AdValue> onPaid;
        private Action onImpression;
        private Action onClicked;
        private Action onOpened;
        private Action onClosed;
        private Action<AdError> onShowFailed;

        public InterstitialAdSlot(
            MonoBehaviour coroutineHost,
            AdsConfig config,
            AdPlacement placement,
            string adUnitId,
            AdsLogger logger,
            Action<AdPlacement, bool> availabilityChanged)
        {
            this.coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.placement = placement;
            this.adUnitId = adUnitId;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.availabilityChanged = availabilityChanged;
        }

        public AdLoadState State { get; private set; } = AdLoadState.Idle;

        public bool IsReady
        {
            get
            {
                if (isDisposed || State != AdLoadState.Ready)
                {
                    return false;
                }

                if (DateTime.UtcNow - loadedAtUtc <= config.MaxAdAge)
                {
                    if (ad != null && ad.CanShowAd())
                    {
                        return true;
                    }
                }

                logger.Warning(placement, "Cached ad is stale or no longer showable; reloading");
                DestroyCurrentAd();
                SetState(AdLoadState.Idle);
                Load();
                return false;
            }
        }

        public bool Load()
        {
            if (isDisposed)
            {
                return false;
            }

            if (State == AdLoadState.Loading || State == AdLoadState.Showing)
            {
                logger.Warning(placement, $"Load ignored while state is {State}");
                return false;
            }

            if (State == AdLoadState.Ready)
            {
                _ = IsReady;
                return false;
            }

            CancelRetry();

            if (string.IsNullOrWhiteSpace(adUnitId))
            {
                SetState(AdLoadState.Failed);
                logger.Warning(placement, "Ad unit ID is unavailable for the current platform/config");
                return false;
            }

            DestroyCurrentAd();
            SetState(AdLoadState.Loading);
            int generation = ++loadGeneration;
            logger.Log(placement, "Loading");

            try
            {
                InterstitialAd.Load(adUnitId, new AdRequest(), (loadedAd, error) =>
                {
                    AdsMainThread.Execute(() => HandleLoadCompleted(generation, loadedAd, error));
                });
            }
            catch (Exception exception)
            {
                HandleLoadException(generation, exception);
            }
            return true;
        }

        public bool Show(Action<AdShowResult> onFinished, Action onOpened = null)
        {
            if (!IsReady)
            {
                return false;
            }

            if (ad == null || !ad.CanShowAd())
            {
                DestroyCurrentAd();
                SetState(AdLoadState.Idle);
                Load();
                return false;
            }

            InterstitialShowSession session = new InterstitialShowSession(
                onFinished,
                onOpened,
                logger,
                placement);
            activeSession = session;
            SetState(AdLoadState.Showing);
            logger.Log(placement, "Showing");

            try
            {
                ad.Show();
            }
            catch (Exception exception)
            {
                logger.Warning(placement, $"Show threw: {exception.Message}");
                CompleteShowAfterFailure(session);
            }
            return true;
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            loadGeneration++;
            CancelRetry();
            DestroyCurrentAd();
            SetState(AdLoadState.Idle);
            activeSession?.Finish(AdShowResult.Failed);
            activeSession = null;
        }

        private void HandleLoadCompleted(int generation, InterstitialAd loadedAd, LoadAdError error)
        {
            if (isDisposed || generation != loadGeneration || State != AdLoadState.Loading)
            {
                loadedAd?.Destroy();
                return;
            }

            if (error != null || loadedAd == null)
            {
                loadedAd?.Destroy();
                logger.LoadFailed(placement, error);
                SetState(AdLoadState.Failed);
                ScheduleRetry();
                return;
            }

            ad = loadedAd;
            paidEventTracked = false;
            loadedAtUtc = DateTime.UtcNow;
            retryCount = 0;
            RegisterEvents(loadedAd);
            SetState(AdLoadState.Ready);
            logger.Response(placement, loadedAd.GetResponseInfo());
        }

        private void HandleLoadException(int generation, Exception exception)
        {
            if (isDisposed || generation != loadGeneration || State != AdLoadState.Loading)
            {
                return;
            }

            logger.Warning(placement, $"Load threw: {exception.Message}");
            SetState(AdLoadState.Failed);
            ScheduleRetry();
        }

        private void RegisterEvents(InterstitialAd source)
        {
            onPaid = value => AdsMainThread.Execute(() => HandlePaid(source, value));
            onImpression = () => AdsMainThread.Execute(() => HandleImpression(source));
            onClicked = () => AdsMainThread.Execute(() => HandleClicked(source));
            onOpened = () => AdsMainThread.Execute(() => HandleOpened(source));
            onClosed = () => AdsMainThread.Execute(() => HandleClosed(source));
            onShowFailed = error => AdsMainThread.Execute(() => HandleShowFailed(source, error));

            source.OnAdPaid += onPaid;
            source.OnAdImpressionRecorded += onImpression;
            source.OnAdClicked += onClicked;
            source.OnAdFullScreenContentOpened += onOpened;
            source.OnAdFullScreenContentClosed += onClosed;
            source.OnAdFullScreenContentFailed += onShowFailed;
        }

        private void UnregisterEvents(InterstitialAd source)
        {
            if (source == null)
            {
                return;
            }

            if (onPaid != null) source.OnAdPaid -= onPaid;
            if (onImpression != null) source.OnAdImpressionRecorded -= onImpression;
            if (onClicked != null) source.OnAdClicked -= onClicked;
            if (onOpened != null) source.OnAdFullScreenContentOpened -= onOpened;
            if (onClosed != null) source.OnAdFullScreenContentClosed -= onClosed;
            if (onShowFailed != null) source.OnAdFullScreenContentFailed -= onShowFailed;

            onPaid = null;
            onImpression = null;
            onClicked = null;
            onOpened = null;
            onClosed = null;
            onShowFailed = null;
        }

        private void HandlePaid(InterstitialAd source, AdValue value)
        {
            if (!IsCurrentAd(source)) return;
            logger.Log(placement, $"Paid: {value?.Value ?? 0} {value?.CurrencyCode ?? "unknown"}");
            if (paidEventTracked || value == null) return;
            paidEventTracked = true;
            Game.Shared.Analytics.Core.AdRevenueAnalytics.Track(value, source.GetResponseInfo(), adUnitId, "Interstitial", placement.ToString());
        }

        private void HandleImpression(InterstitialAd source)
        {
            if (IsCurrentAd(source)) logger.Log(placement, "Impression recorded");
        }

        private void HandleClicked(InterstitialAd source)
        {
            if (IsCurrentAd(source)) logger.Log(placement, "Clicked");
        }

        private void HandleOpened(InterstitialAd source)
        {
            if (!IsCurrentAd(source) || State != AdLoadState.Showing) return;
            logger.Log(placement, "Fullscreen content opened");
            activeSession?.Opened();
        }

        public void CancelShow()
        {
            CompleteShowAfterFailure(activeSession);
        }

        private void HandleClosed(InterstitialAd source)
        {
            if (!IsCurrentAd(source) || State != AdLoadState.Showing)
            {
                return;
            }

            InterstitialShowSession session = activeSession;
            activeSession = null;
            logger.Log(placement, "Closed");
            DestroyCurrentAd();
            SetState(AdLoadState.Idle);
            session?.Finish(AdShowResult.Closed);
            Load();
        }

        private void HandleShowFailed(InterstitialAd source, AdError error)
        {
            if (!IsCurrentAd(source) || State != AdLoadState.Showing)
            {
                return;
            }

            logger.ShowFailed(placement, error);
            CompleteShowAfterFailure(activeSession);
        }

        private void CompleteShowAfterFailure(InterstitialShowSession session)
        {
            if (session == null || !ReferenceEquals(session, activeSession))
            {
                return;
            }

            activeSession = null;
            DestroyCurrentAd();
            SetState(AdLoadState.Failed);
            session.Finish(AdShowResult.Failed);
            Load();
        }

        private bool IsCurrentAd(InterstitialAd source)
        {
            return !isDisposed && source != null && ReferenceEquals(source, ad);
        }

        private void DestroyCurrentAd()
        {
            InterstitialAd current = ad;
            ad = null;
            loadedAtUtc = default;

            if (current == null)
            {
                return;
            }

            UnregisterEvents(current);

            try
            {
                current.Destroy();
            }
            catch (Exception exception)
            {
                logger.Warning(placement, $"Destroy failed: {exception.Message}");
            }
        }

        private void ScheduleRetry()
        {
            if (isDisposed || retryCoroutine != null || string.IsNullOrWhiteSpace(adUnitId))
            {
                return;
            }

            retryCount++;
            float delay = config.GetRetryDelaySeconds(retryCount);
            logger.Log(placement, $"Retry {retryCount} scheduled in {delay:0.#} seconds");
            retryCoroutine = coroutineHost.StartCoroutine(RetryAfterDelay(delay));
        }

        private IEnumerator RetryAfterDelay(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            retryCoroutine = null;

            if (!isDisposed && State == AdLoadState.Failed)
            {
                Load();
            }
        }

        private void CancelRetry()
        {
            if (retryCoroutine == null)
            {
                return;
            }

            coroutineHost.StopCoroutine(retryCoroutine);
            retryCoroutine = null;
        }

        private bool SetState(AdLoadState nextState)
        {
            if (State == nextState)
            {
                return true;
            }

            if (!IsValidTransition(State, nextState))
            {
                logger.Warning(placement, $"Invalid state transition rejected: {State} -> {nextState}");
                return false;
            }

            State = nextState;
            bool isAvailable = State == AdLoadState.Ready;
            if (isAvailable != lastAvailability)
            {
                lastAvailability = isAvailable;
                availabilityChanged?.Invoke(placement, isAvailable);
            }

            return true;
        }

        private static bool IsValidTransition(AdLoadState current, AdLoadState next)
        {
            switch (current)
            {
                case AdLoadState.Idle:
                    return next == AdLoadState.Loading || next == AdLoadState.Failed;
                case AdLoadState.Loading:
                    return next == AdLoadState.Ready || next == AdLoadState.Failed || next == AdLoadState.Idle;
                case AdLoadState.Ready:
                    return next == AdLoadState.Showing || next == AdLoadState.Idle;
                case AdLoadState.Showing:
                    return next == AdLoadState.Idle || next == AdLoadState.Failed;
                case AdLoadState.Failed:
                    return next == AdLoadState.Loading || next == AdLoadState.Idle;
                default:
                    return false;
            }
        }


        private sealed class InterstitialShowSession
        {
            private readonly Action<AdShowResult> onFinished;
            private readonly Action onOpened;
            private readonly AdsLogger logger;
            private readonly AdPlacement placement;
            private bool finishInvoked;
            private bool openedInvoked;

            public InterstitialShowSession(
                Action<AdShowResult> onFinished,
                Action onOpened,
                AdsLogger logger,
                AdPlacement placement)
            {
                this.onFinished = onFinished;
                this.onOpened = onOpened;
                this.logger = logger;
                this.placement = placement;
            }

            public void Opened()
            {
                if (finishInvoked || openedInvoked) return;
                openedInvoked = true;
                try { onOpened?.Invoke(); }
                catch (Exception exception)
                {
                    logger.Warning(placement, $"Opened callback threw: {exception.Message}");
                }
            }

            public void Finish(AdShowResult result)
            {
                if (finishInvoked)
                {
                    return;
                }

                finishInvoked = true;
                try
                {
                    onFinished?.Invoke(result);
                }
                catch (Exception exception)
                {
                    logger.Warning(placement, $"Finished callback threw: {exception.Message}");
                }
            }
        }
    }
}

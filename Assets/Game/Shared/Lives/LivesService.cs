using System;
using Game.Shared.Save;

namespace Game.Shared.Lives
{
    public sealed class LivesService
    {
        private readonly SaveManager saveManager;
        private readonly LivesConfig config;
        private bool isInitialized;
        private bool lastHasInfiniteLives;

        public LivesService(SaveManager saveManager, LivesConfig config = null)
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            this.config = config;
        }

        public bool IsInitialized => isInitialized;
        public bool IsEnabled => config == null || config.LivesEnabled;

        public int CurrentLives
        {
            get
            {
                EnsureInitialized();
                RefreshIfRequired();
                return saveManager.Lives;
            }
        }

        public int MaxLives
        {
            get
            {
                EnsureInitialized();
                RefreshIfRequired();
                return saveManager.MaxLives;
            }
        }

        public bool IsFull => CurrentLives >= MaxLives;
        public bool HasLives => !IsEnabled || HasInfiniteLives || CurrentLives > 0;
        public bool HasInfiniteLives
        {
            get
            {
                EnsureInitialized();
                return saveManager.HasInfiniteLives;
            }
        }

        public DateTime? InfiniteLivesEndUtc
        {
            get
            {
                EnsureInitialized();
                return TryGetUtcDateTime(
                    saveManager.InfiniteLivesEndUtcUnixSeconds,
                    out DateTime endUtc)
                    ? endUtc
                    : null;
            }
        }

        public DateTime? NextRefillUtc
        {
            get
            {
                EnsureInitialized();
                RefreshIfRequired();

                return TryGetUtcDateTime(
                    saveManager.NextLifeRefillUtcUnixSeconds,
                    out DateTime nextRefillUtc)
                    ? nextRefillUtc
                    : null;
            }
        }

        public TimeSpan RemainingRefillTime
        {
            get
            {
                EnsureInitialized();

                long nowUnixSeconds = GetUtcNowUnixSeconds();
                RefreshIfRequired(nowUnixSeconds);

                long remainingSeconds =
                    saveManager.NextLifeRefillUtcUnixSeconds - nowUnixSeconds;
                return remainingSeconds > 0
                    ? TimeSpan.FromSeconds(remainingSeconds)
                    : TimeSpan.Zero;
            }
        }

        public event Action<int, int> LivesChanged;
        public event Action RefillStateChanged;

        public void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            if (!saveManager.IsInitialized)
            {
                saveManager.Initialize();
            }

            isInitialized = true;
            lastHasInfiniteLives = saveManager.HasInfiniteLives;
            saveManager.StoreStateChanged += HandleStoreStateChanged;
            RefreshAt(GetUtcNowUnixSeconds());
        }

        public bool TrySpendLife()
        {
            // Disabled gameplay must neither consume lives nor touch a saved refill deadline.
            if (!IsEnabled) return true;
            EnsureInitialized();

            if (saveManager.HasInfiniteLives)
            {
                return true;
            }

            long nowUnixSeconds = GetUtcNowUnixSeconds();
            RefreshAt(nowUnixSeconds);

            LivesState currentState = CaptureState();
            if (currentState.Current <= 0)
            {
                return false;
            }

            int nextLives = currentState.Current - 1;
            long nextRefillUtc = currentState.NextRefillUtc;
            if (nextLives < currentState.Max &&
                (nextRefillUtc <= 0 || !IsValidUnixTimestamp(nextRefillUtc)))
            {
                nextRefillUtc = nowUnixSeconds + RefillDurationSeconds;
            }

            ApplyStateAndSave(nextLives, nextRefillUtc);
            return true;
        }

        public void AddLives(int amount)
        {
            EnsureInitialized();
            if (amount <= 0)
            {
                return;
            }

            RefreshAt(GetUtcNowUnixSeconds());
            LivesState currentState = CaptureState();

            long addedLives = (long)currentState.Current + amount;
            int nextLives = (int)Math.Min(currentState.Max, addedLives);
            long nextRefillUtc = nextLives >= currentState.Max
                ? 0
                : currentState.NextRefillUtc;

            ApplyStateAndSave(nextLives, nextRefillUtc);
        }

        public void SetLives(int amount)
        {
            EnsureInitialized();

            long nowUnixSeconds = GetUtcNowUnixSeconds();
            RefreshAt(nowUnixSeconds);
            LivesState currentState = CaptureState();

            int nextLives = Math.Max(0, Math.Min(amount, currentState.Max));
            long nextRefillUtc = currentState.NextRefillUtc;
            if (nextLives >= currentState.Max)
            {
                nextRefillUtc = 0;
            }
            else if (nextRefillUtc <= 0 || !IsValidUnixTimestamp(nextRefillUtc))
            {
                nextRefillUtc = nowUnixSeconds + RefillDurationSeconds;
            }

            ApplyStateAndSave(nextLives, nextRefillUtc);
        }

        public void AddInfiniteLives(TimeSpan duration)
        {
            AddInfiniteLivesForStore(duration);
            saveManager.Save();
        }

        internal void AddInfiniteLivesForStore(TimeSpan duration)
        {
            EnsureInitialized();
            saveManager.AddInfiniteLives(duration);
        }

        public void Refresh()
        {
            EnsureInitialized();
            RefreshInfiniteLivesState();
            RefreshAt(GetUtcNowUnixSeconds());
        }

        public void Tick()
        {
            if (!isInitialized)
            {
                return;
            }

            RefreshInfiniteLivesState();
            RefreshIfRequired();
        }

        private void HandleStoreStateChanged()
        {
            RefreshInfiniteLivesState();
        }

        private void RefreshInfiniteLivesState()
        {
            bool hasInfiniteLives = saveManager.HasInfiniteLives;
            if (hasInfiniteLives == lastHasInfiniteLives)
            {
                return;
            }

            lastHasInfiniteLives = hasInfiniteLives;
            RefillStateChanged?.Invoke();
        }

        private void RefreshIfRequired()
        {
            RefreshIfRequired(GetUtcNowUnixSeconds());
        }

        private void RefreshIfRequired(long nowUnixSeconds)
        {
            if (!IsEnabled) return;
            LivesState state = CaptureState();
            bool isFull = state.Current >= state.Max;
            bool requiresRefresh =
                (config != null && state.Max != config.MaxLives) ||
                state.Max < 0 ||
                state.Current < 0 ||
                state.Current > state.Max ||
                (isFull && state.NextRefillUtc != 0) ||
                (!isFull &&
                 (state.NextRefillUtc <= 0 ||
                  !IsValidUnixTimestamp(state.NextRefillUtc) ||
                  nowUnixSeconds >= state.NextRefillUtc));

            if (requiresRefresh)
            {
                RefreshAt(nowUnixSeconds);
            }
        }

        private void RefreshAt(long nowUnixSeconds)
        {
            // Preserve the saved state while disabled. Re-enabling catches up offline refill
            // from the original deadline through the existing algorithm below.
            if (!IsEnabled) return;
            LivesState state = CaptureState();
            int normalizedMax = config != null ? config.MaxLives : Math.Max(0, state.Max);
            int normalizedCurrent = Math.Max(0, Math.Min(state.Current, normalizedMax));
            long normalizedNextRefillUtc = state.NextRefillUtc;

            if (normalizedCurrent >= normalizedMax)
            {
                normalizedNextRefillUtc = 0;
            }
            else if (normalizedNextRefillUtc <= 0 ||
                     !IsValidUnixTimestamp(normalizedNextRefillUtc))
            {
                normalizedNextRefillUtc = nowUnixSeconds + RefillDurationSeconds;
            }
            else if (nowUnixSeconds >= normalizedNextRefillUtc)
            {
                long elapsedSeconds = nowUnixSeconds - normalizedNextRefillUtc;
                long elapsedRefills = 1 + elapsedSeconds / RefillDurationSeconds;
                long missingLives = normalizedMax - normalizedCurrent;
                long appliedRefills = Math.Min(missingLives, elapsedRefills);

                normalizedCurrent += (int)appliedRefills;
                normalizedNextRefillUtc = normalizedCurrent >= normalizedMax
                    ? 0
                    : normalizedNextRefillUtc + elapsedRefills * RefillDurationSeconds;
            }

            ApplyStateAndSave(normalizedCurrent, normalizedNextRefillUtc, normalizedMax);
        }

        private bool ApplyStateAndSave(int current, long nextRefillUtc, int? maximum = null)
        {
            LivesState beforeState = CaptureState();
            int nextMax = maximum ?? Math.Max(0, beforeState.Max);
            if (beforeState.Current == current &&
                beforeState.NextRefillUtc == nextRefillUtc &&
                beforeState.Max == nextMax)
            {
                return false;
            }

            saveManager.SetLivesState(current, nextMax, nextRefillUtc);
            saveManager.Save();

            LivesState afterState = CaptureState();
            if (beforeState.Current != afterState.Current ||
                beforeState.Max != afterState.Max)
            {
                LivesChanged?.Invoke(afterState.Current, afterState.Max);
            }

            if (beforeState.NextRefillUtc != afterState.NextRefillUtc)
            {
                RefillStateChanged?.Invoke();
            }

            return true;
        }

        private LivesState CaptureState()
        {
            return new LivesState(
                saveManager.Lives,
                saveManager.MaxLives,
                saveManager.NextLifeRefillUtcUnixSeconds);
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                Initialize();
            }
        }

        private static long GetUtcNowUnixSeconds()
        {
            return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
        }

        private static bool IsValidUnixTimestamp(long unixSeconds)
        {
            return TryGetUtcDateTime(unixSeconds, out _);
        }

        private static bool TryGetUtcDateTime(long unixSeconds, out DateTime utcDateTime)
        {
            if (unixSeconds <= 0)
            {
                utcDateTime = default;
                return false;
            }

            try
            {
                utcDateTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                utcDateTime = default;
                return false;
            }
        }

        private long RefillDurationSeconds =>
            (long)(config != null ? config.RefillDuration : TimeSpan.FromMinutes(20)).TotalSeconds;

        private readonly struct LivesState
        {
            public LivesState(int current, int max, long nextRefillUtc)
            {
                Current = current;
                Max = max;
                NextRefillUtc = nextRefillUtc;
            }

            public int Current { get; }
            public int Max { get; }
            public long NextRefillUtc { get; }
        }
    }
}

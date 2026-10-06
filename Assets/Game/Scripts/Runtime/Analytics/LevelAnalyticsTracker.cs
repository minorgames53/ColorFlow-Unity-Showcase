using System;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gameplay.Analytics
{
    [DisallowMultipleComponent]
    public sealed class LevelAnalyticsTracker : MonoBehaviour
    {
        private const string SnapshotKey = "analytics_active_level_attempt";
        private const string LastDisplayedLevelKey = "analytics_last_displayed_level";
        private const string LastAttemptNumberKey = "analytics_last_attempt_number";

        [SerializeField] private MonoBehaviour progressProviderBehaviour;

        private ILevelProgressProvider progressProvider;
        private bool isAttemptActive;
        private bool isTerminalEventSent;
        private bool attributionWinSent;
        private bool suppressAnalytics;
        private int levelDisplayedNumber;
        private int levelNumber;
        private int attemptNumber;
        private double activeDurationSeconds;
        private double activeSegmentStartTime;

        public static LevelAnalyticsTracker Instance { get; private set; }

        public bool TryGetLevelContext(out int displayedLevelNumber, out int internalLevelNumber)
        {
            displayedLevelNumber = levelDisplayedNumber;
            internalLevelNumber = levelNumber;
            return !suppressAnalytics && displayedLevelNumber > 0 && internalLevelNumber > 0;
        }

        public static LevelAnalyticsTracker GetOrCreate(Component owner)
        {
            if (Instance != null)
            {
                return Instance;
            }

            LevelAnalyticsTracker existing = FindFirstObjectByType<LevelAnalyticsTracker>();
            if (existing != null)
            {
                return existing;
            }

            GameObject trackerObject = new GameObject(nameof(LevelAnalyticsTracker));
            if (owner != null)
            {
                Scene currentScene = owner.gameObject.scene;
                if (currentScene.IsValid())
                {
                    SceneManager.MoveGameObjectToScene(trackerObject, currentScene);
                }
            }

            return trackerObject.AddComponent<LevelAnalyticsTracker>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            CacheReferences();
            RecoverExitedAttemptIfNeeded();
        }

        private void OnDestroy()
        {
            SaveSnapshot();
            ClearDefaultLevelContext("gameplay_tracker_destroyed");
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                CloseActiveTimeSegment();
                SaveSnapshot();
                return;
            }

            ResumeActiveTimeSegment();
        }

        private void OnApplicationQuit()
        {
            CloseActiveTimeSegment();
            SaveSnapshot();
        }

        public void BeginLevel(int displayedLevelNumber, int internalLevelNumber, bool suppressAnalyticsEvents)
        {
            RecoverExitedAttemptIfNeeded();

            suppressAnalytics = suppressAnalyticsEvents;
            isAttemptActive = true;
            isTerminalEventSent = false;
            attributionWinSent = false;
            levelDisplayedNumber = Mathf.Max(1, displayedLevelNumber);
            levelNumber = Mathf.Max(1, internalLevelNumber);
            attemptNumber = suppressAnalytics
                ? 1
                : ResolveNextAttemptNumber(this.levelDisplayedNumber);
            activeDurationSeconds = 0d;
            activeSegmentStartTime = Now;

            if (suppressAnalytics)
            {
                ClearSnapshot();
                ClearDefaultLevelContext(
                    "gameplay_debug_attempt_suppressed",
                    true);
                return;
            }

            PublishDefaultLevelContext("gameplay_begin_level");
            PersistLastAttempt(this.levelDisplayedNumber, attemptNumber);
            SaveSnapshot();
            Track(AnalyticsEventFactory.CreateLevelStarted(
                this.levelDisplayedNumber,
                levelNumber,
                attemptNumber));
        }

        public void CompleteLevel()
        {
            // BeginWin is the guarded success source. A win after revive is valid even
            // when GA4 has already recorded this attempt's failure; leave GA4 unchanged.
            if (!suppressAnalytics && !attributionWinSent && levelDisplayedNumber > 0)
            {
                attributionWinSent = true;
                AnalyticsBootstrap.Instance?.TrackAttribution("level_achieved", levelDisplayedNumber);
            }
            if (!CanSendTerminalEvent())
            {
                return;
            }

            MarkTerminal();
            Track(AnalyticsEventFactory.CreateLevelCompleted(
                levelDisplayedNumber,
                levelNumber,
                attemptNumber,
                activeDurationSeconds));
            ClearSnapshot();
        }

        public void FailLevel()
        {
            if (!CanSendTerminalEvent())
            {
                return;
            }

            int progressPercent = GetProgressPercent();
            MarkTerminal();
            Track(AnalyticsEventFactory.CreateLevelFailed(
                levelDisplayedNumber,
                levelNumber,
                attemptNumber,
                activeDurationSeconds,
                progressPercent));
            ClearSnapshot();
        }

        public void SuppressCurrentAttemptForDebug()
        {
            suppressAnalytics = true;
            isAttemptActive = false;
            isTerminalEventSent = true;
            ClearSnapshot();
            ClearDefaultLevelContext(
                "gameplay_attempt_suppressed_for_debug",
                true);
        }

        public void UpdateProgressSnapshot()
        {
            SaveSnapshot();
        }

        private bool CanSendTerminalEvent()
        {
            return isAttemptActive && !isTerminalEventSent && !suppressAnalytics;
        }

        private void MarkTerminal()
        {
            CloseActiveTimeSegment();
            isTerminalEventSent = true;
            isAttemptActive = false;
        }

        private void CacheReferences()
        {
            progressProvider = progressProviderBehaviour as ILevelProgressProvider;
            if (progressProvider == null)
            {
                progressProvider = GetProgressProviderOnGameObject();
            }

            if (progressProvider == null)
            {
                progressProvider = FindFirstObjectByType<TargetLaneLevelProgressProvider>();
            }
        }

        private ILevelProgressProvider GetProgressProviderOnGameObject()
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ILevelProgressProvider provider)
                {
                    return provider;
                }
            }

            return null;
        }

        private int GetProgressPercent()
        {
            CacheReferences();
            return Mathf.Clamp(progressProvider != null ? progressProvider.GetProgressPercent() : 0, 0, 100);
        }

        private int ResolveNextAttemptNumber(int displayedLevel)
        {
            int previousDisplayed = PlayerPrefs.GetInt(LastDisplayedLevelKey, 0);
            int previousAttempt = PlayerPrefs.GetInt(LastAttemptNumberKey, 0);
            return previousDisplayed == displayedLevel ? Mathf.Max(1, previousAttempt + 1) : 1;
        }

        private static void PersistLastAttempt(int displayedLevel, int attempt)
        {
            PlayerPrefs.SetInt(LastDisplayedLevelKey, Mathf.Max(1, displayedLevel));
            PlayerPrefs.SetInt(LastAttemptNumberKey, Mathf.Max(1, attempt));
            PlayerPrefs.Save();
        }

        private void SaveSnapshot()
        {
            if (!isAttemptActive || isTerminalEventSent || suppressAnalytics)
            {
                return;
            }

            LevelAttemptSnapshot snapshot = new LevelAttemptSnapshot
            {
                isActive = true,
                levelDisplayedNumber = levelDisplayedNumber,
                levelNumber = levelNumber,
                attemptNumber = attemptNumber,
                activeDurationSeconds = GetCurrentDurationSeconds(),
                progressPercent = GetProgressPercent()
            };

            try
            {
                PlayerPrefs.SetString(SnapshotKey, JsonUtility.ToJson(snapshot));
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{nameof(LevelAnalyticsTracker)} could not save active attempt snapshot: {exception.Message}", this);
            }
        }

        private void ClearSnapshot()
        {
            if (PlayerPrefs.HasKey(SnapshotKey))
            {
                PlayerPrefs.DeleteKey(SnapshotKey);
                PlayerPrefs.Save();
            }
        }

        private void RecoverExitedAttemptIfNeeded()
        {
            if (!PlayerPrefs.HasKey(SnapshotKey))
            {
                return;
            }

            string json = PlayerPrefs.GetString(SnapshotKey, string.Empty);
            LevelAttemptSnapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<LevelAttemptSnapshot>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{nameof(LevelAnalyticsTracker)} discarded invalid active attempt snapshot: {exception.Message}", this);
                ClearSnapshot();
                return;
            }

            if (snapshot == null || !snapshot.isActive)
            {
                ClearSnapshot();
                return;
            }

            AnalyticsEvent exitEvent = AnalyticsEventFactory.CreateLevelExited(
                Mathf.Max(1, snapshot.levelDisplayedNumber),
                Mathf.Max(1, snapshot.levelNumber),
                Mathf.Max(1, snapshot.attemptNumber),
                snapshot.activeDurationSeconds,
                Mathf.Clamp(snapshot.progressPercent, 0, 100));

            if (!TryTrack(exitEvent))
            {
                return;
            }

            PersistLastAttempt(snapshot.levelDisplayedNumber, snapshot.attemptNumber);
            ClearSnapshot();
        }

        private void CloseActiveTimeSegment()
        {
            if (!isAttemptActive || isTerminalEventSent || activeSegmentStartTime <= 0d)
            {
                return;
            }

            activeDurationSeconds += Math.Max(0d, Now - activeSegmentStartTime);
            activeSegmentStartTime = 0d;
        }

        private double GetCurrentDurationSeconds()
        {
            if (!isAttemptActive || isTerminalEventSent || activeSegmentStartTime <= 0d)
            {
                return Math.Max(0d, activeDurationSeconds);
            }

            return Math.Max(0d, activeDurationSeconds + Math.Max(0d, Now - activeSegmentStartTime));
        }

        private void ResumeActiveTimeSegment()
        {
            if (isAttemptActive && !isTerminalEventSent && activeSegmentStartTime <= 0d)
            {
                activeSegmentStartTime = Now;
            }
        }

        private static void Track(AnalyticsEvent analyticsEvent)
        {
            TryTrack(analyticsEvent);
        }

        private static bool TryTrack(AnalyticsEvent analyticsEvent)
        {
            if (AnalyticsBootstrap.Instance == null || AnalyticsBootstrap.Instance.Service == null)
            {
                return false;
            }

            AnalyticsBootstrap.Instance.Track(analyticsEvent);
            return true;
        }

        private void PublishDefaultLevelContext(string source)
        {
            AnalyticsBootstrap.Instance?.SetDefaultLevelContext(
                levelDisplayedNumber,
                levelNumber,
                this,
                source);
        }

        private void ClearDefaultLevelContext(string source, bool force = false)
        {
            AnalyticsBootstrap.Instance?.ClearDefaultLevelContext(
                force ? null : this,
                source);
        }

        private static double Now => Time.realtimeSinceStartupAsDouble;

        [Serializable]
        private sealed class LevelAttemptSnapshot
        {
            public bool isActive;
            public int levelDisplayedNumber;
            public int levelNumber;
            public int attemptNumber;
            public double activeDurationSeconds;
            public int progressPercent;
        }
    }

    public interface ILevelProgressProvider
    {
        int GetProgressPercent();
    }
}

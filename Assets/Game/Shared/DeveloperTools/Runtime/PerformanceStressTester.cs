using UnityEngine;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
#endif

namespace Game.Shared.DeveloperTools.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Developer/Performance Stress Tester")]
    public sealed class PerformanceStressTester : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public enum FpsLimitPreset
        {
            [InspectorName("60 FPS")] Fps60 = 60,
            [InspectorName("30 FPS")] Fps30 = 30,
            [InspectorName("20 FPS")] Fps20 = 20,
            [InspectorName("15 FPS")] Fps15 = 15,
            [InspectorName("Custom FPS")] Custom = 0
        }

        [Header("FPS Limit")]
        [SerializeField] private FpsLimitPreset fpsLimit = FpsLimitPreset.Fps60;
        [SerializeField, Min(1)] private int customFps = 30;
        [SerializeField] private bool disableVSyncWhileActive = true;

        [Header("Frame Spike Simulation")]
        [SerializeField] private bool enableFrameSpikes;
        [SerializeField, Min(0.05f)] private float spikeInterval = 3f;
        [SerializeField, Min(0f)] private float spikeDurationMilliseconds = 150f;
        [SerializeField] private bool randomizeSpikeInterval;
        [SerializeField, Min(0f)] private float spikeIntervalRandomRange = 0.25f;

        private readonly Stopwatch spikeStopwatch = new Stopwatch();
        private System.Random intervalRandom;
        private int previousTargetFrameRate;
        private int previousVSyncCount;
        private double nextSpikeTime;
        private bool settingsApplied;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CaptureAndApplySettings();
            ScheduleNextSpike();
        }

        private void Update()
        {
            if (!settingsApplied || !enableFrameSpikes || spikeDurationMilliseconds <= 0f ||
                UnityEngine.Time.realtimeSinceStartupAsDouble < nextSpikeTime)
            {
                return;
            }

            SimulateFrameSpike();
            ScheduleNextSpike();
        }

        private void OnDisable()
        {
            RestoreSettings();
        }

        private void OnDestroy()
        {
            RestoreSettings();
        }

        private void OnValidate()
        {
            customFps = Mathf.Max(1, customFps);
            spikeInterval = Mathf.Max(0.05f, spikeInterval);
            spikeDurationMilliseconds = Mathf.Max(0f, spikeDurationMilliseconds);
            spikeIntervalRandomRange = Mathf.Max(0f, spikeIntervalRandomRange);

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            ApplySettings();
            ScheduleNextSpike();
        }

        private void CaptureAndApplySettings()
        {
            if (!settingsApplied)
            {
                previousTargetFrameRate = Application.targetFrameRate;
                previousVSyncCount = QualitySettings.vSyncCount;
                settingsApplied = true;
            }

            ApplySettings();
        }

        private void ApplySettings()
        {
            if (!settingsApplied)
            {
                return;
            }

            QualitySettings.vSyncCount = disableVSyncWhileActive ? 0 : previousVSyncCount;
            Application.targetFrameRate = ResolveTargetFrameRate();
        }

        private int ResolveTargetFrameRate()
        {
            return fpsLimit == FpsLimitPreset.Custom
                ? Mathf.Max(1, customFps)
                : Mathf.Max(1, (int)fpsLimit);
        }

        private void RestoreSettings()
        {
            if (!settingsApplied)
            {
                return;
            }

            Application.targetFrameRate = previousTargetFrameRate;
            QualitySettings.vSyncCount = previousVSyncCount;
            settingsApplied = false;
            nextSpikeTime = 0d;
            spikeStopwatch.Reset();
        }

        private void ScheduleNextSpike()
        {
            if (!Application.isPlaying || !settingsApplied || !enableFrameSpikes)
            {
                nextSpikeTime = 0d;
                return;
            }

            double interval = spikeInterval;
            if (randomizeSpikeInterval && spikeIntervalRandomRange > 0f)
            {
                intervalRandom ??= new System.Random(Environment.TickCount);
                double randomOffset = (intervalRandom.NextDouble() * 2d - 1d) * spikeIntervalRandomRange;
                interval = Math.Max(0.05d, interval + randomOffset);
            }

            nextSpikeTime = UnityEngine.Time.realtimeSinceStartupAsDouble + interval;
        }

        private void SimulateFrameSpike()
        {
            double targetDurationSeconds = spikeDurationMilliseconds / 1000d;
            spikeStopwatch.Restart();
            while (spikeStopwatch.Elapsed.TotalSeconds < targetDurationSeconds)
            {
                Thread.SpinWait(64);
            }

            spikeStopwatch.Stop();
        }
#else
        private void Awake()
        {
            enabled = false;
        }
#endif
    }
}

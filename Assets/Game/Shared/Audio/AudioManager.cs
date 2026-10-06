using System.Collections.Generic;
using UnityEngine;

namespace Game.Shared.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        #region Singleton

        public static AudioManager Instance { get; private set; }

        #endregion

        #region Inspector

        [SerializeField] private AudioSource sfxSource = null;
        [SerializeField] private AudioSource musicSource = null;
        [SerializeField] private AudioCueCatalog cueCatalog = null;
        [SerializeField, Min(1)] private int sfxPoolSize = 8;

        #endregion

        #region State

        private readonly Dictionary<AudioKey, AudioCue> cueLookup = new Dictionary<AudioKey, AudioCue>();
        private readonly Dictionary<AudioKey, float> lastPlayTimes = new Dictionary<AudioKey, float>();
        private readonly List<AudioSource> sfxPool = new List<AudioSource>();
        private bool isSoundEnabled = true;
        private int nextSfxPoolIndex;

        public bool IsSoundEnabled => isSoundEnabled;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildCueLookup();
            BuildSfxPool();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region Public API

        public bool PlaySfx(AudioKey key)
        {
            return PlaySfx(key, 1f);
        }

        public bool PlaySfx(AudioKey key, float pitch)
        {
            if (!isSoundEnabled)
            {
                return false;
            }

            if (sfxSource == null)
            {
                Debug.LogWarning($"{nameof(AudioManager)} cannot play SFX because {nameof(sfxSource)} is not assigned.", this);
                return false;
            }

            if (!TryGetPlayableCue(key, out AudioCue cue) || !CanPlayByMinimumInterval(cue))
            {
                return false;
            }

            AudioSource source = GetNextSfxSource();
            source.clip = cue.Clip;
            source.volume = cue.Volume;
            source.pitch = Mathf.Max(0.01f, pitch);
            source.Play();
            lastPlayTimes[key] = UnityEngine.Time.unscaledTime;
            return true;
        }

        public bool PlayMusic(AudioKey key)
        {
            if (!isSoundEnabled)
            {
                return false;
            }

            if (musicSource == null)
            {
                Debug.LogWarning($"{nameof(AudioManager)} cannot play music because {nameof(musicSource)} is not assigned.", this);
                return false;
            }

            if (!TryGetPlayableCue(key, out AudioCue cue))
            {
                return false;
            }

            if (musicSource.isPlaying && musicSource.clip == cue.Clip)
            {
                return true;
            }

            musicSource.clip = cue.Clip;
            musicSource.loop = true;
            musicSource.Play();
            return true;
        }

        public void StopMusic()
        {
            if (musicSource == null)
            {
                return;
            }

            musicSource.Stop();
            musicSource.clip = null;
        }

        public void StopAllSfx()
        {
            if (sfxSource != null)
            {
                StopSfxSource(sfxSource);
            }

            for (int i = 0; i < sfxPool.Count; i++)
            {
                AudioSource source = sfxPool[i];

                if (source != null && source != sfxSource)
                {
                    StopSfxSource(source);
                }
            }
        }

        // Settings UI can call this, then persist the same value through SaveManager.
        public void SetSoundEnabled(bool enabled)
        {
            if (!enabled)
            {
                StopAllSfx();
            }

            if (isSoundEnabled == enabled)
            {
                if (!enabled)
                {
                    StopMusic();
                }

                return;
            }

            isSoundEnabled = enabled;

            if (!isSoundEnabled)
            {
                StopMusic();
            }
        }

        #endregion

        #region Lookup

        private void BuildCueLookup()
        {
            cueLookup.Clear();
            lastPlayTimes.Clear();

            if (cueCatalog == null || cueCatalog.Cues == null)
            {
                return;
            }

            foreach (AudioCue cue in cueCatalog.Cues)
            {
                if (cue == null || cue.Key == AudioKey.None)
                {
                    continue;
                }

                if (cueLookup.ContainsKey(cue.Key))
                {
                    Debug.LogWarning($"Duplicate audio cue key '{cue.Key}' found on {nameof(AudioManager)}. Keeping the first cue.", this);
                    continue;
                }

                cueLookup.Add(cue.Key, cue);
            }
        }

        private void BuildSfxPool()
        {
            sfxPool.Clear();
            nextSfxPoolIndex = 0;

            if (sfxSource == null)
            {
                return;
            }

            sfxPool.Add(sfxSource);

            for (int i = 1; i < Mathf.Max(1, sfxPoolSize); i++)
            {
                AudioSource pooledSource = gameObject.AddComponent<AudioSource>();
                CopySfxSourceSettings(sfxSource, pooledSource);
                sfxPool.Add(pooledSource);
            }
        }

        private bool TryGetPlayableCue(AudioKey key, out AudioCue cue)
        {
            cue = null;

            if (key == AudioKey.None)
            {
                return false;
            }

            if (!cueLookup.TryGetValue(key, out cue))
            {
                return false;
            }

            return cue != null && cue.Clip != null;
        }

        #endregion

        #region Helpers

        private bool CanPlayByMinimumInterval(AudioCue cue)
        {
            if (cue.MinimumInterval <= 0f)
            {
                return true;
            }

            if (!lastPlayTimes.TryGetValue(cue.Key, out float lastPlayTime))
            {
                return true;
            }

            return UnityEngine.Time.unscaledTime - lastPlayTime >= cue.MinimumInterval;
        }

        private AudioSource GetNextSfxSource()
        {
            if (sfxPool.Count == 0)
            {
                BuildSfxPool();
            }

            if (sfxPool.Count == 0)
            {
                return sfxSource;
            }

            AudioSource source = sfxPool[nextSfxPoolIndex];
            nextSfxPoolIndex = (nextSfxPoolIndex + 1) % sfxPool.Count;
            return source;
        }

        private static void StopSfxSource(AudioSource source)
        {
            source.Stop();
            source.clip = null;
        }

        private static void CopySfxSourceSettings(AudioSource source, AudioSource target)
        {
            target.outputAudioMixerGroup = source.outputAudioMixerGroup;
            target.playOnAwake = false;
            target.loop = false;
            target.priority = source.priority;
            target.panStereo = source.panStereo;
            target.spatialBlend = source.spatialBlend;
            target.reverbZoneMix = source.reverbZoneMix;
            target.dopplerLevel = source.dopplerLevel;
            target.rolloffMode = source.rolloffMode;
            target.minDistance = source.minDistance;
            target.maxDistance = source.maxDistance;
            target.bypassEffects = source.bypassEffects;
            target.bypassListenerEffects = source.bypassListenerEffects;
            target.bypassReverbZones = source.bypassReverbZones;
        }

        #endregion
    }
}

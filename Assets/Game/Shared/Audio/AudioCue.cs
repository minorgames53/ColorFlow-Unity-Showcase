using System;
using UnityEngine;

namespace Game.Shared.Audio
{
    [Serializable]
    public sealed class AudioCue
    {
        [SerializeField] private AudioKey key = AudioKey.None;
        [SerializeField] private AudioClip clip = null;

        [SerializeField, Range(0f, 1f)]
        private float volume = 1f;

        [SerializeField, Min(0f)]
        private float minimumInterval = 0f;

        public AudioKey Key => key;
        public AudioClip Clip => clip;
        public float Volume => volume;
        public float MinimumInterval => minimumInterval;
    }
}

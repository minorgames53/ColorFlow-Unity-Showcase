using System.Collections.Generic;
using UnityEngine;

namespace Game.Shared.Audio
{
    [CreateAssetMenu(
        fileName = "AudioCueCatalog",
        menuName = "Game/Shared/Audio Cue Catalog")]
    public sealed class AudioCueCatalog : ScriptableObject
    {
        [SerializeField] private List<AudioCue> cues = null;

        public IReadOnlyList<AudioCue> Cues => cues;
    }
}

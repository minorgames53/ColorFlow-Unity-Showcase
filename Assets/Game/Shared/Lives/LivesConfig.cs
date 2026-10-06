using System;
using UnityEngine;

namespace Game.Shared.Lives
{
    [CreateAssetMenu(fileName = "LivesConfig", menuName = "Game/Shared/Lives Config")]
    public sealed class LivesConfig : ScriptableObject
    {
        [Header("Lives Config")]
        [SerializeField] private bool livesEnabled = true;
        [SerializeField, Min(1)] private int maxLives = 5;
        [SerializeField, Min(1), Tooltip("Minutes required to regenerate one life.")]
        private int refillDurationMinutes = 20;

        public bool LivesEnabled => livesEnabled;
        public int MaxLives => Math.Max(1, maxLives);
        public TimeSpan RefillDuration => TimeSpan.FromMinutes(Math.Max(1, refillDurationMinutes));

        private void OnValidate()
        {
            maxLives = Math.Max(1, maxLives);
            refillDurationMinutes = Math.Max(1, refillDurationMinutes);
        }
    }
}

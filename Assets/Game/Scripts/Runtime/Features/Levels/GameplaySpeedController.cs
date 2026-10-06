using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Levels
{
    public interface IGameplaySpeedTarget
    {
        void SetGameplaySpeedMultiplier(float multiplier);
    }

    [DisallowMultipleComponent]
    public sealed class GameplaySpeedController : MonoBehaviour
    {
        [Header("Speed")]
        [SerializeField, Min(0.01f)] private float initialMultiplier = 1f;

        [Header("Targets")]
        [SerializeField] private bool autoFindTargets = true;
        [SerializeField] private MonoBehaviour[] explicitTargets;

        private readonly List<IGameplaySpeedTarget> cachedTargets = new List<IGameplaySpeedTarget>();
        private float currentMultiplier = 1f;

        public float CurrentMultiplier => currentMultiplier;
        public event Action<float> SpeedMultiplierChanged;

        private void Awake()
        {
            currentMultiplier = Mathf.Max(0.01f, initialMultiplier);
            RefreshTargets();
            ApplyMultiplierToTargets(currentMultiplier);
        }

        private void OnValidate()
        {
            initialMultiplier = Mathf.Max(0.01f, initialMultiplier);
        }

        public void SetMultiplier(float multiplier)
        {
            multiplier = Mathf.Max(0.01f, multiplier);
            if (Mathf.Approximately(currentMultiplier, multiplier))
            {
                return;
            }

            currentMultiplier = multiplier;
            RefreshTargets();
            ApplyMultiplierToTargets(currentMultiplier);
            SpeedMultiplierChanged?.Invoke(currentMultiplier);
        }

        public void ResetMultiplier()
        {
            SetMultiplier(initialMultiplier);
        }

        public void RefreshTargets()
        {
            cachedTargets.Clear();

            if (explicitTargets != null)
            {
                for (int i = 0; i < explicitTargets.Length; i++)
                {
                    AddTarget(explicitTargets[i]);
                }
            }

            if (!autoFindTargets)
            {
                return;
            }

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                AddTarget(behaviours[i]);
            }
        }

        private void AddTarget(MonoBehaviour behaviour)
        {
            if (behaviour == null || behaviour == this)
            {
                return;
            }

            IGameplaySpeedTarget target = behaviour as IGameplaySpeedTarget;
            if (target == null)
            {
                return;
            }

            if (!cachedTargets.Contains(target))
            {
                cachedTargets.Add(target);
            }
        }

        private void ApplyMultiplierToTargets(float multiplier)
        {
            for (int i = cachedTargets.Count - 1; i >= 0; i--)
            {
                IGameplaySpeedTarget target = cachedTargets[i];
                if (target == null)
                {
                    cachedTargets.RemoveAt(i);
                    continue;
                }

                target.SetGameplaySpeedMultiplier(multiplier);
            }
        }
    }
}

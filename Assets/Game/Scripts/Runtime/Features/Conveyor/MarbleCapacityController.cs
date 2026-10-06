using System;
using UnityEngine;

namespace Gameplay.Conveyor
{
    public sealed class MarbleCapacityController : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maximumFieldMarbles = 45;

        private int activeFieldMarbleCount;
        private int reservedMarbleCount;

        public event Action CountsChanged;

        public int MaximumFieldMarbles => maximumFieldMarbles;
        public int ActiveFieldMarbleCount => activeFieldMarbleCount;
        public int ReservedMarbleCount => reservedMarbleCount;
        public int TotalReservedAndActiveCount => activeFieldMarbleCount + reservedMarbleCount;

        private void OnValidate()
        {
            maximumFieldMarbles = Mathf.Max(1, maximumFieldMarbles);
            activeFieldMarbleCount = Mathf.Clamp(activeFieldMarbleCount, 0, maximumFieldMarbles);
            reservedMarbleCount = Mathf.Clamp(reservedMarbleCount, 0, maximumFieldMarbles - activeFieldMarbleCount);
        }

        public bool CanReserve(int count)
        {
            return count > 0 && activeFieldMarbleCount + reservedMarbleCount + count <= maximumFieldMarbles;
        }

        public bool TryReserve(int count)
        {
            if (!CanReserve(count))
            {
                return false;
            }

            reservedMarbleCount += count;
            NotifyCountsChanged();
            return true;
        }

        public void CommitReservation(int count)
        {
            if (count <= 0)
            {
                return;
            }

            int committedCount = Mathf.Min(count, reservedMarbleCount);
            reservedMarbleCount -= committedCount;
            activeFieldMarbleCount = Mathf.Min(maximumFieldMarbles, activeFieldMarbleCount + committedCount);
            NotifyCountsChanged();
        }

        public void CancelReservation(int count)
        {
            if (count <= 0)
            {
                return;
            }

            reservedMarbleCount = Mathf.Max(0, reservedMarbleCount - count);
            NotifyCountsChanged();
        }

        public void NotifyMarbleConsumed(int count = 1)
        {
            if (count <= 0)
            {
                return;
            }

            activeFieldMarbleCount = Mathf.Max(0, activeFieldMarbleCount - count);
            NotifyCountsChanged();
        }

        public void ResetCounts()
        {
            activeFieldMarbleCount = 0;
            reservedMarbleCount = 0;
            NotifyCountsChanged();
        }

        private void NotifyCountsChanged()
        {
            CountsChanged?.Invoke();
        }
    }
}

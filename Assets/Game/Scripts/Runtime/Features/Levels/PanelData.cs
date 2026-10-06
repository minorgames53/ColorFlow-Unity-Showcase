using System;
using UnityEngine;

namespace Gameplay.Levels
{
    [Serializable]
    public sealed class PanelData
    {
        [SerializeField] private int row;
        [SerializeField] private int col;
        [SerializeField, Min(1)] private int number = 1;

        public int Row => row;
        public int Col => col;
        public int Number => number;
    }
}

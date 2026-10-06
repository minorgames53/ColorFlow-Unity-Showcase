using System;
using UnityEngine;

namespace Gameplay.Levels
{
    [Serializable]
    public sealed class MultiplierGateData
    {
        [SerializeField] private int row;
        [SerializeField] private int col;
        [SerializeField] private int mult = 2;

        public int Row => row;
        public int Col => col;
        public int Multiplier => mult;
    }
}

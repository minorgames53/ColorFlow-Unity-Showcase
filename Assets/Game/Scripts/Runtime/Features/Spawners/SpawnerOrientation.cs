using System;
using UnityEngine;

namespace Gameplay.Spawners
{
    public enum SpawnerOrientation
    {
        Horizontal = 0,
        Vertical = 1
    }

    [Serializable]
    public sealed class SpawnerOrientationProfile
    {
        [SerializeField] private Vector3 maskLocalPosition;
        [SerializeField] private Vector3 maskLocalEulerAngles;
        [SerializeField] private Vector3 countLocalPosition;
        [SerializeField] private Vector3 sourceBoxHiddenLocalPosition;
        [SerializeField] private Vector3 sourceBoxVisibleLocalPosition;
        [SerializeField] private Vector3 sourceBoxScale = Vector3.one;

        public Vector3 MaskLocalPosition => maskLocalPosition;
        public Vector3 MaskLocalEulerAngles => maskLocalEulerAngles;
        public Vector3 CountLocalPosition => countLocalPosition;
        public Vector3 SourceBoxHiddenLocalPosition => sourceBoxHiddenLocalPosition;
        public Vector3 SourceBoxVisibleLocalPosition => sourceBoxVisibleLocalPosition;
        public Vector3 SourceBoxScale => sourceBoxScale;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Shared.DeveloperTools
{
    [CreateAssetMenu(fileName = "test_devices", menuName = "Game/Shared/Test Devices")]
    public sealed class TestDevicesConfig : ScriptableObject
    {
        [Serializable]
        private sealed class TestDevice
        {
            [SerializeField] private string deviceName;
            [SerializeField] private string uid;

            public string Uid => uid;
        }

        [SerializeField] private List<TestDevice> devices = new List<TestDevice>();

        public bool Contains(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid) || devices == null)
                return false;

            string candidate = uid.Trim();
            if (string.Equals(candidate, SystemInfo.unsupportedIdentifier, StringComparison.Ordinal))
                return false;

            foreach (TestDevice device in devices)
            {
                if (device != null && !string.IsNullOrWhiteSpace(device.Uid) &&
                    string.Equals(device.Uid.Trim(), candidate, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}

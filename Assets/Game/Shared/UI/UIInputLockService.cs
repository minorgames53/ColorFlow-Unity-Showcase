using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.UI;

namespace Game.Shared.UI
{
    [DisallowMultipleComponent]
    public sealed class UIInputLockService : MonoBehaviour
    {
        [SerializeField] private InputSystemUIInputModule inputModule;

        private int lockCount;
        private readonly HashSet<object> modalInputOwners = new HashSet<object>();

        public int LockCount => lockCount;
        public bool IsLocked => lockCount > 0;

        private void OnEnable()
        {
            ApplyInputState();
        }

        private void OnDisable()
        {
            lockCount = 0;
            modalInputOwners.Clear();
            if (inputModule != null)
            {
                inputModule.enabled = true;
            }
        }

        public void LockUIInput()
        {
            if (lockCount < int.MaxValue)
            {
                lockCount++;
            }

            ApplyInputState();
        }

        public void UnlockUIInput()
        {
            if (lockCount > 0)
            {
                lockCount--;
            }

            ApplyInputState();
        }

        private void ApplyInputState()
        {
            if (inputModule != null)
            {
                inputModule.enabled = lockCount == 0 || modalInputOwners.Count > 0;
            }
        }

        // A blocking modal still needs its button. Retain all existing lock counts;
        // the owning PanelManager blocks background UI while this scope is held.
        public void SetModalInputAllowed(object owner, bool allowed)
        {
            if (owner == null) return;
            if (allowed) modalInputOwners.Add(owner);
            else modalInputOwners.Remove(owner);
            ApplyInputState();
        }
    }
}

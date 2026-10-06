using System;
using System.Collections;
using UnityEngine;

namespace Game.Shared.Navigation
{
    public sealed class SceneReadyNotifier : MonoBehaviour
    {
        [SerializeField] private SceneId sceneId = SceneId.Menu;
        [SerializeField] private bool notifyOnStart = true;
        [SerializeField] private bool waitOneFrame = true;

        public event Action Ready;
        public bool IsReady { get; private set; }

        private IEnumerator Start()
        {
            if (!notifyOnStart)
            {
                yield break;
            }

            if (waitOneFrame)
            {
                yield return null;
            }

            NotifyReady();
        }

        public void NotifyReady()
        {
            IsReady = true;
            SceneLoader.Instance?.NotifySceneReady(sceneId);
            Ready?.Invoke();
        }
    }
}

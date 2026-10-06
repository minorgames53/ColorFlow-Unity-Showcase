using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Gameplay.BoardFeatures;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.KeyLocks
{
    public sealed class KeyLockFeatureController
    {
        private enum KeyState
        {
            OnSource,
            FlyingToWaiting,
            Waiting,
            Reserved,
            FlyingToTarget,
            Consumed
        }

        private sealed class RuntimeKey
        {
            public SourceBox Source;
            public KeyView View;
            public KeyState State;
            public Tween Tween;
        }

        private sealed class RuntimeLock
        {
            public TargetBox Target;
            public LockView View;
            public bool Reserved;
            public bool Resolved;
        }

        private readonly Dictionary<SourceBox, RuntimeKey> keysBySource = new Dictionary<SourceBox, RuntimeKey>();
        private readonly Dictionary<TargetBox, RuntimeLock> locksByTarget = new Dictionary<TargetBox, RuntimeLock>();
        private readonly List<RuntimeKey> waitingKeys = new List<RuntimeKey>();
        private readonly HashSet<int> claimedKeyCells = new HashSet<int>();

        private LevelDefinition level;
        private BoardFeatureCatalog catalog;
        private Transform waitingArea;
        private TargetLaneController laneController;
        private Object logContext;
        private int lifecycleVersion;

        public static bool ValidateConfiguration(
            LevelDefinition levelDefinition,
            BoardFeatureCatalog featureCatalog,
            Transform lockWaitingArea,
            Object context)
        {
            if (!ContainsKeyOrLockData(levelDefinition))
            {
                return true;
            }

            bool isValid = true;
            if (lockWaitingArea == null)
            {
                Debug.LogError($"Key/Locked TargetBox feature requires a LockWaitingArea Transform.", context);
                isValid = false;
            }

            if (featureCatalog == null)
            {
                Debug.LogError($"Key/Locked TargetBox feature requires a BoardFeatureCatalog.", context);
                return false;
            }

            if (featureCatalog.KeyPrefab == null || !featureCatalog.KeyPrefab.HasRequiredReferences)
            {
                Debug.LogError($"BoardFeatureCatalog '{featureCatalog.name}' is missing a valid Key prefab/View reference.", context);
                isValid = false;
            }

            if (featureCatalog.LockPrefab == null || !featureCatalog.LockPrefab.HasRequiredReferences)
            {
                Debug.LogError($"BoardFeatureCatalog '{featureCatalog.name}' is missing a valid Lock prefab/View reference.", context);
                isValid = false;
            }

            return isValid;
        }

        public void Prepare(
            LevelDefinition levelDefinition,
            BoardFeatureCatalog featureCatalog,
            Transform lockWaitingArea,
            TargetLaneController targetLaneController,
            Object context)
        {
            Clear();
            level = levelDefinition;
            catalog = featureCatalog;
            waitingArea = lockWaitingArea;
            laneController = targetLaneController;
            logContext = context;
        }

        public void RegisterSourceBox(SourceBox sourceBox)
        {
            if (sourceBox == null || level == null || catalog?.KeyPrefab == null ||
                sourceBox.CellIndex < 0 || sourceBox.CellIndex >= level.Cells.Count ||
                level.Cells[sourceBox.CellIndex]?.HasKey != true || claimedKeyCells.Contains(sourceBox.CellIndex) ||
                keysBySource.ContainsKey(sourceBox))
            {
                return;
            }

            Transform keyParent = sourceBox.PresentationRoot != null ? sourceBox.PresentationRoot : sourceBox.transform;
            KeyView view = Object.Instantiate(catalog.KeyPrefab, keyParent);
            view.name = $"Key_{sourceBox.CellIndex}";
            view.transform.localPosition = Vector3.zero;
            view.transform.localRotation = Quaternion.identity;
            view.Initialize();

            RuntimeKey key = new RuntimeKey
            {
                Source = sourceBox,
                View = view,
                State = KeyState.OnSource
            };
            keysBySource.Add(sourceBox, key);
            sourceBox.ReleaseStarted -= HandleKeySourceBoxReleaseStarted;
            sourceBox.ReleaseStarted += HandleKeySourceBoxReleaseStarted;
        }

        public void RegisterTargetBox(TargetBox targetBox, int laneIndex, bool isFront)
        {
            if (targetBox == null || !targetBox.IsRuntimeLocked || catalog?.LockPrefab == null ||
                locksByTarget.ContainsKey(targetBox))
            {
                return;
            }

            LockView view = Object.Instantiate(catalog.LockPrefab, targetBox.transform);
            view.name = $"Lock_Lane{laneIndex}";
            view.transform.localPosition = Vector3.zero;
            view.transform.localRotation = Quaternion.identity;
            view.Initialize();

            locksByTarget.Add(targetBox, new RuntimeLock
            {
                Target = targetBox,
                View = view
            });

            if (isFront)
            {
                TryUnlockWaitingTargets();
            }
        }

        public void NotifyTargetReachedFront(TargetBox targetBox, int laneIndex)
        {
            if (targetBox != null && locksByTarget.ContainsKey(targetBox))
            {
                TryUnlockWaitingTargets();
            }
        }

        public void Clear()
        {
            lifecycleVersion++;

            foreach (KeyValuePair<SourceBox, RuntimeKey> pair in keysBySource)
            {
                if (pair.Key != null)
                {
                    pair.Key.ReleaseStarted -= HandleKeySourceBoxReleaseStarted;
                }

                pair.Value?.Tween?.Kill(false);
                DestroyRuntimeObject(pair.Value?.View != null ? pair.Value.View.gameObject : null);
            }

            foreach (RuntimeLock runtimeLock in locksByTarget.Values)
            {
                runtimeLock?.View?.KillTween();
                DestroyRuntimeObject(runtimeLock?.View != null ? runtimeLock.View.gameObject : null);
            }

            keysBySource.Clear();
            locksByTarget.Clear();
            waitingKeys.Clear();
            claimedKeyCells.Clear();
            level = null;
            catalog = null;
            waitingArea = null;
            laneController = null;
            logContext = null;
        }

        private void HandleKeySourceBoxReleaseStarted(SourceBox sourceBox)
        {
            if (sourceBox == null || !keysBySource.TryGetValue(sourceBox, out RuntimeKey key))
            {
                return;
            }

            BeginCollectKey(key);
        }

        private void BeginCollectKey(RuntimeKey key)
        {
            if (key == null || key.State != KeyState.OnSource || key.View == null || waitingArea == null)
            {
                return;
            }

            key.State = KeyState.FlyingToWaiting;
            claimedKeyCells.Add(key.Source.CellIndex);
            key.Source.ReleaseStarted -= HandleKeySourceBoxReleaseStarted;
            AudioManager.Instance?.PlaySfx(AudioKey.KeyCollect);

            if (TryReserveFirstWaitingTarget(key, out RuntimeLock directTargetLock))
            {
                key.View.transform.SetParent(waitingArea, true);
                FlyKeyToTarget(key, directTargetLock);
                return;
            }

            key.View.transform.SetParent(waitingArea, true);

            Vector2 randomOffset = Random.insideUnitCircle * key.View.WaitingRadius;
            Vector3 targetPosition = waitingArea.position + new Vector3(randomOffset.x, randomOffset.y, 0f);
            int expectedLifecycleVersion = lifecycleVersion;
            key.Tween = key.View.transform
                .DOMove(targetPosition, key.View.SourceToWaitingDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(key.View.gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    key.Tween = null;
                    if (expectedLifecycleVersion != lifecycleVersion || key.View == null || key.State != KeyState.FlyingToWaiting)
                    {
                        return;
                    }

                    key.State = KeyState.Waiting;
                    waitingKeys.Add(key);
                    TryUnlockWaitingTargets();
                });
        }

        private void TryUnlockWaitingTargets()
        {
            if (waitingKeys.Count == 0 || laneController == null)
            {
                return;
            }

            while (waitingKeys.Count > 0)
            {
                RuntimeKey key = TakeFirstWaitingKey();
                if (key == null)
                {
                    return;
                }

                if (!TryReserveFirstWaitingTarget(key, out RuntimeLock runtimeLock))
                {
                    key.State = KeyState.Waiting;
                    waitingKeys.Insert(0, key);
                    return;
                }

                FlyKeyToTarget(key, runtimeLock);
            }
        }

        private bool TryReserveFirstWaitingTarget(RuntimeKey key, out RuntimeLock runtimeLock)
        {
            runtimeLock = null;
            if (key == null || key.View == null || laneController == null)
            {
                return false;
            }

            for (int laneIndex = 0; laneIndex < LevelDefinition.TargetBoxLaneCount; laneIndex++)
            {
                if (!laneController.TryGetCurrentTarget(laneIndex, out TargetBox target) ||
                    target == null || !target.IsRuntimeLocked ||
                    !locksByTarget.TryGetValue(target, out RuntimeLock candidate) ||
                    candidate.Reserved || candidate.Resolved)
                {
                    continue;
                }

                candidate.Reserved = true;
                key.State = KeyState.Reserved;
                runtimeLock = candidate;
                return true;
            }

            return false;
        }

        private RuntimeKey TakeFirstWaitingKey()
        {
            for (int i = 0; i < waitingKeys.Count; i++)
            {
                RuntimeKey key = waitingKeys[i];
                if (key == null || key.State != KeyState.Waiting || key.View == null)
                {
                    waitingKeys.RemoveAt(i--);
                    continue;
                }

                waitingKeys.RemoveAt(i);
                return key;
            }

            return null;
        }

        private void FlyKeyToTarget(RuntimeKey key, RuntimeLock runtimeLock)
        {
            if (key?.View == null || runtimeLock?.Target == null)
            {
                return;
            }

            key.State = KeyState.FlyingToTarget;
            int expectedLifecycleVersion = lifecycleVersion;
            key.Tween = key.View.PlayTargetApproach(
                runtimeLock.Target.transform.position,
                () =>
                {
                    key.Tween = null;
                    if (expectedLifecycleVersion != lifecycleVersion || key.View == null || runtimeLock.Target == null ||
                        runtimeLock.Resolved || key.State != KeyState.FlyingToTarget)
                    {
                        return;
                    }

                    runtimeLock.View.PlayUnlockImpact(key.View, () =>
                        CompleteUnlock(key, runtimeLock, expectedLifecycleVersion));
                });
        }

        private void CompleteUnlock(RuntimeKey key, RuntimeLock runtimeLock, int expectedLifecycleVersion)
        {
            if (expectedLifecycleVersion != lifecycleVersion || key?.View == null || runtimeLock?.Target == null || runtimeLock.Resolved)
            {
                return;
            }

            runtimeLock.Resolved = true;
            runtimeLock.Reserved = false;
            key.State = KeyState.Consumed;
            DestroyRuntimeObject(runtimeLock.View != null ? runtimeLock.View.gameObject : null);
            runtimeLock.View = null;
            DestroyRuntimeObject(key.View.gameObject);
            key.View = null;

            if (runtimeLock.Target.UnlockRuntimeLock())
            {
                AudioManager.Instance?.PlaySfx(AudioKey.KeyUnlock);
            }
            else
            {
                Debug.LogWarning($"Locked TargetBox '{runtimeLock.Target.name}' was already unlocked before its reserved Key arrived.", logContext);
            }

            TryUnlockWaitingTargets();
        }

        private static bool ContainsKeyOrLockData(LevelDefinition levelDefinition)
        {
            if (levelDefinition == null)
            {
                return false;
            }

            IReadOnlyList<LevelCellData> cells = levelDefinition.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i]?.HasKey == true)
                {
                    return true;
                }
            }

            IReadOnlyList<TargetBoxLaneData> lanes = levelDefinition.TargetBoxLanes;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                IReadOnlyList<TargetBoxData> boxes = lanes[laneIndex]?.Boxes;
                if (boxes == null)
                {
                    continue;
                }

                for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                {
                    if (boxes[boxIndex]?.IsLocked == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void DestroyRuntimeObject(GameObject runtimeObject)
        {
            if (runtimeObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(runtimeObject);
            }
            else
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }
    }
}

using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay.TargetBoxes
{
    [DisallowMultipleComponent]
    public sealed class TargetBoxFillAnimationView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform targetBoxTransform;
        [SerializeField] private Transform passiveViewRoot;
        [SerializeField] private GameObject spawnPrefab;

        [Header("Target Position")]
        [SerializeField] private Vector3 targetStartPosition;
        [SerializeField] private Vector3 targetEndPosition = new Vector3(0f, 0.25f, 0f);

        [Header("Target Scale")]
        [SerializeField] private Vector3 targetStartScale = Vector3.one;
        [SerializeField] private Vector3 targetEndScale = Vector3.zero;

        [Header("Passive Scale")]
        [SerializeField] private Vector3 passiveStartScale = Vector3.zero;
        [SerializeField] private Vector3 passiveEndScale = Vector3.one;

        [Header("Move")]
        [SerializeField, Min(0f)] private float moveDuration = 0.2f;
        [SerializeField] private Ease moveEase = Ease.OutQuad;

        [Header("Transition")]
        [SerializeField, Min(0f)] private float transitionDelay = 0.15f;
        [SerializeField, Min(0f)] private float targetScaleDuration = 0.4f;
        [SerializeField] private Ease targetScaleEase = Ease.InBack;
        [SerializeField, Min(0f)] private float passiveScaleDuration = 0.3f;
        [SerializeField] private Ease passiveScaleEase = Ease.OutBack;

        [Header("Spawn")]
        [SerializeField, Min(0f)] private float prefabSpawnDelay = 0.5f;
        [SerializeField, Min(0f)] private float prefabLifetime = 1f;

        private Sequence activeSequence;
        private GameObject spawnedInstance;
        private bool isPlaying;

        private void Reset()
        {
            targetBoxTransform = transform;
            passiveViewRoot = transform.Find("PassiveViewRoot");
            targetStartPosition = transform.localPosition;
            targetEndPosition = targetStartPosition + Vector3.up * 0.25f;
            targetStartScale = transform.localScale;
            targetEndScale = Vector3.zero;
            passiveStartScale = Vector3.zero;
            passiveEndScale = Vector3.one;
        }

        private void OnDisable()
        {
            KillActiveSequence();
        }

        private void OnValidate()
        {
            moveDuration = Mathf.Max(0f, moveDuration);
            transitionDelay = Mathf.Max(0f, transitionDelay);
            targetScaleDuration = Mathf.Max(0f, targetScaleDuration);
            passiveScaleDuration = Mathf.Max(0f, passiveScaleDuration);
            prefabSpawnDelay = Mathf.Max(0f, prefabSpawnDelay);
            prefabLifetime = Mathf.Max(0f, prefabLifetime);
        }

        public bool Play(Action onCompleted = null)
        {
            if (isPlaying)
            {
                return false;
            }

            if (targetBoxTransform == null)
            {
                Debug.LogWarning($"{nameof(TargetBoxFillAnimationView)} on '{name}' cannot play because Target Box Transform is missing.", this);
                onCompleted?.Invoke();
                return false;
            }

            var sortingGroup = gameObject.AddComponent<SortingGroup>();
            sortingGroup.sortingOrder = 4;
            
            isPlaying = true;
            DestroySpawnedPrefab();
            Vector3 runtimeStartPosition = targetBoxTransform.localPosition + targetStartPosition;
            Vector3 runtimeEndPosition = runtimeStartPosition + (targetEndPosition - targetStartPosition);
            // Keep the completed box at the same local depth throughout the fill animation.
            runtimeStartPosition.z = -25f;
            runtimeEndPosition.z = -25f;
            ApplyStartState(runtimeStartPosition);

            activeSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            activeSequence.Join(targetBoxTransform
                .DOLocalMove(runtimeEndPosition, moveDuration)
                .SetEase(moveEase));

            if (transitionDelay > 0f)
            {
                activeSequence.AppendInterval(transitionDelay);
            }

            activeSequence.Join(targetBoxTransform
                .DOScale(targetEndScale, targetScaleDuration)
                .SetEase(targetScaleEase));

            if (passiveViewRoot != null)
            {
                passiveViewRoot.gameObject.SetActive(true);
                passiveViewRoot.transform.GetChild(0).GetComponent<SpriteRenderer>().sortingOrder = 3;
                activeSequence.Join(passiveViewRoot
                    .DOScale(passiveEndScale, passiveScaleDuration)
                    .SetEase(passiveScaleEase));
            }

            if (prefabSpawnDelay > 0f)
            {
                activeSequence.AppendInterval(prefabSpawnDelay);
            }

            activeSequence.AppendCallback(SpawnPrefab);
            activeSequence.OnComplete(() =>
            {
                activeSequence = null;
                isPlaying = false;
                onCompleted?.Invoke();
            });

            return true;
        }

        public void ResetImmediate()
        {
            KillActiveSequence();
            isPlaying = false;
            DestroySpawnedPrefab();
            ApplyStartState();
        }

        private void KillActiveSequence()
        {
            activeSequence?.Kill(false);
            activeSequence = null;
            isPlaying = false;
        }

        private void ApplyStartState()
        {
            ApplyStartState(targetStartPosition);
        }

        private void ApplyStartState(Vector3 startPosition)
        {
            if (targetBoxTransform != null)
            {
                targetBoxTransform.DOKill(false);
                targetBoxTransform.localPosition = startPosition;
                targetBoxTransform.localScale = targetStartScale;
            }

            if (passiveViewRoot != null)
            {
                passiveViewRoot.DOKill(false);
                passiveViewRoot.localScale = passiveStartScale;
            }
        }

        private void SpawnPrefab()
        {
            if (targetBoxTransform == null)
            {
                return;
            }

            if (spawnPrefab == null)
            {
                Debug.LogWarning($"{nameof(TargetBoxFillAnimationView)} on '{name}' cannot spawn completion prefab because Spawn Prefab is missing.", this);
                return;
            }

            spawnedInstance = Instantiate(
                spawnPrefab,
                targetBoxTransform.position,
                targetBoxTransform.rotation);

            if (prefabLifetime <= 0f)
            {
                DestroySpawnedPrefab();
                return;
            }

            Destroy(spawnedInstance, prefabLifetime);
        }

        private void DestroySpawnedPrefab()
        {
            if (spawnedInstance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(spawnedInstance);
            }
            else
            {
                DestroyImmediate(spawnedInstance);
            }

            spawnedInstance = null;
        }
    }
}

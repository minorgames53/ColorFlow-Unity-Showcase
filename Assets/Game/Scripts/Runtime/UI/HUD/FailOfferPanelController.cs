using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Gameplay.UI.HUD
{
    [DisallowMultipleComponent]
    public sealed class FailOfferPanelController : MonoBehaviour
    {
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private RawImage videoImage;
        [SerializeField] private CanvasGroup panelCanvasGroup;

        // UIPanel deactivates its hierarchy on close. Keep the decoder outside that
        // hierarchy so its preparation and RenderTexture survive panel closes.
        private VideoPlayer runtimePlayer;
        private Coroutine revealVideoRoutine;
        private bool preparingFirstFrame;
        private bool firstFrameReady;
        private int firstFrameReadyAt;
        private bool playWhenPrepared;
        private bool suppressPlaybackOnEnable;
        private bool sourceWasEnabled;

        private void Awake()
        {
            ConfigureVideoPlayer();
            SetVideoImageVisible(false);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || suppressPlaybackOnEnable)
            {
                return;
            }

            PlayPreparedPreview();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            StopPreviewPlayback();
        }

        private void OnDestroy()
        {
            StopRevealVideoRoutine();
            if (runtimePlayer != null)
            {
                runtimePlayer.prepareCompleted -= HandlePrepareCompleted;
                runtimePlayer.frameReady -= HandleFirstFrameReady;
                runtimePlayer.errorReceived -= HandleVideoError;
                runtimePlayer.Stop();
                Destroy(runtimePlayer.gameObject);
            }

            if (videoPlayer != null && sourceWasEnabled)
            {
                videoPlayer.enabled = true;
            }
        }

        private void OnValidate()
        {
            ConfigureVideoPlayer();
        }

        public void PreloadPreview()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!EnsureRuntimePlayer())
            {
                return;
            }

            // Repeated preload requests must not interrupt an already open preview.
            if (playWhenPrepared)
            {
                return;
            }

            ActivateHiddenPreloadHost();
            SetVideoImageVisible(false);
            EnsureFirstFrame();
        }

        public void PlayPreparedPreview()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!isActiveAndEnabled || !EnsureRuntimePlayer())
            {
                return;
            }

            // Both OnEnable and FailRecoveryController can request playback.
            if (playWhenPrepared && runtimePlayer.isPlaying && !preparingFirstFrame)
            {
                return;
            }

            playWhenPrepared = true;
            if (HasFirstFrame())
            {
                RevealPreparedVideo();
                return;
            }

            SetVideoImageVisible(false);
            EnsureFirstFrame();
        }

        public void StopPreviewPlayback()
        {
            playWhenPrepared = false;
            StopRevealVideoRoutine();
            SetVideoImageVisible(false);
            if (runtimePlayer == null)
            {
                return;
            }

            // Warm-up may finish while the panel is inactive. Its event handlers
            // never reveal the UI unless a new playback request is active.
            if (!preparingFirstFrame)
            {
                if (runtimePlayer.isPrepared)
                {
                    runtimePlayer.Pause();
                }

                EnsureFirstFrame();
            }
        }

        private void EnsureFirstFrame()
        {
            if (preparingFirstFrame || HasFirstFrame())
            {
                return;
            }

            firstFrameReady = false;
            preparingFirstFrame = true;
            if (runtimePlayer.isPrepared)
            {
                DecodeFirstFrame();
            }
            else
            {
                runtimePlayer.Prepare();
            }
        }

        private void HandlePrepareCompleted(VideoPlayer source)
        {
            if (source == runtimePlayer && preparingFirstFrame)
            {
                DecodeFirstFrame();
            }
        }

        private void DecodeFirstFrame()
        {
            runtimePlayer.Pause();
            runtimePlayer.sendFrameReadyEvents = true;
            runtimePlayer.frame = 0;
            runtimePlayer.Play();
        }

        private void HandleFirstFrameReady(VideoPlayer source, long frameIndex)
        {
            if (source != runtimePlayer || !preparingFirstFrame || frameIndex != 0)
            {
                return;
            }

            source.Pause();
            source.sendFrameReadyEvents = false;
            preparingFirstFrame = false;
            firstFrameReady = true;
            firstFrameReadyAt = Time.frameCount;

            if (playWhenPrepared && isActiveAndEnabled)
            {
                RevealPreparedVideo();
            }
        }

        private bool HasFirstFrame()
        {
            return firstFrameReady && runtimePlayer != null && runtimePlayer.isPrepared &&
                   runtimePlayer.targetTexture != null && runtimePlayer.targetTexture.IsCreated();
        }

        private void RevealPreparedVideo()
        {
            if (revealVideoRoutine != null)
            {
                return;
            }

            // A frame warmed on an earlier update is already in the target texture.
            // Do not seek again here: that would reintroduce the opening delay.
            if (HasFirstFrame() && Time.frameCount > firstFrameReadyAt)
            {
                BeginPlayback();
                return;
            }

            revealVideoRoutine = StartCoroutine(RevealVideoAfterFrameRender());
        }

        private IEnumerator RevealVideoAfterFrameRender()
        {
            // frameReady runs before the frame is drawn into the RenderTexture.
            yield return new WaitForEndOfFrame();
            revealVideoRoutine = null;
            if (playWhenPrepared && isActiveAndEnabled && HasFirstFrame())
            {
                BeginPlayback();
            }
        }

        private void BeginPlayback()
        {
            firstFrameReady = false;
            SetVideoImageVisible(true);
            runtimePlayer.Play();
        }

        private void HandleVideoError(VideoPlayer source, string message)
        {
            if (source != runtimePlayer)
            {
                return;
            }

            playWhenPrepared = false;
            preparingFirstFrame = false;
            firstFrameReady = false;
            StopRevealVideoRoutine();
            SetVideoImageVisible(false);
            source.sendFrameReadyEvents = false;
            // A failed decoder must be reset before a subsequent request retries it.
            source.Stop();
            Debug.LogWarning($"{nameof(FailOfferPanelController)} on '{name}' preview failed: {message}", this);
        }

        private void StopRevealVideoRoutine()
        {
            if (revealVideoRoutine != null)
            {
                StopCoroutine(revealVideoRoutine);
                revealVideoRoutine = null;
            }
        }

        private bool EnsureRuntimePlayer()
        {
            if (runtimePlayer != null)
            {
                return true;
            }

            if (videoPlayer == null || videoImage == null || videoPlayer.targetTexture == null)
            {
                Debug.LogWarning(
                    $"{nameof(FailOfferPanelController)} on '{name}' requires a VideoPlayer, RawImage and target RenderTexture.",
                    this);
                return false;
            }

            ConfigureVideoPlayer();
            sourceWasEnabled = videoPlayer.enabled;
            videoPlayer.enabled = false;

            var host = new GameObject($"{name} Preview Playback");
            host.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            SceneManager.MoveGameObjectToScene(host, gameObject.scene);
            runtimePlayer = host.AddComponent<VideoPlayer>();
            runtimePlayer.playOnAwake = false;
            runtimePlayer.source = videoPlayer.source;
            if (videoPlayer.source == VideoSource.VideoClip)
            {
                runtimePlayer.clip = videoPlayer.clip;
            }
            else
            {
                runtimePlayer.url = videoPlayer.url;
            }

            runtimePlayer.renderMode = VideoRenderMode.RenderTexture;
            runtimePlayer.targetTexture = videoPlayer.targetTexture;
            runtimePlayer.aspectRatio = videoPlayer.aspectRatio;
            runtimePlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            runtimePlayer.playbackSpeed = videoPlayer.playbackSpeed;
            runtimePlayer.isLooping = true;
            runtimePlayer.waitForFirstFrame = true;
            runtimePlayer.skipOnDrop = false;
            // This is a silent offer preview, including during hidden warm-up.
            runtimePlayer.audioOutputMode = VideoAudioOutputMode.None;
            runtimePlayer.prepareCompleted += HandlePrepareCompleted;
            runtimePlayer.frameReady += HandleFirstFrameReady;
            runtimePlayer.errorReceived += HandleVideoError;
            videoImage.texture = runtimePlayer.targetTexture;
            return true;
        }

        private void ConfigureVideoPlayer()
        {
            if (videoPlayer == null)
            {
                return;
            }

            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = true;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = false;
        }

        private void ActivateHiddenPreloadHost()
        {
            if (gameObject.activeInHierarchy)
            {
                return;
            }

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            suppressPlaybackOnEnable = true;
            gameObject.SetActive(true);
            suppressPlaybackOnEnable = false;
            SetVideoImageVisible(false);
        }

        private void SetVideoImageVisible(bool isVisible)
        {
            if (videoImage != null)
            {
                videoImage.enabled = isVisible;
            }
        }
    }
}

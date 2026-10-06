using Facebook.Unity;
using Game.Shared.Config;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public sealed class MetaSdkBootstrap : MonoBehaviour
{
    private static MetaSdkBootstrap instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (!FeatureConfig.ExternalServicesEnabled)
        {
            return;
        }

        Debug.Log(
            $"[Meta SDK] Awake called. " +
            $"Platform: {Application.platform}, " +
            $"Initialized: {FB.IsInitialized}"
        );

        if (FB.IsInitialized)
        {
            CompleteInitialization();
            return;
        }

        Debug.Log("[Meta SDK] Calling FB.Init.");

        FB.Init(
            onInitComplete: OnInitialized,
            onHideUnity: OnHideUnity
        );
    }

    private void OnInitialized()
    {
        if (!FeatureConfig.ExternalServicesEnabled)
        {
            return;
        }

        Debug.Log(
            $"[Meta SDK] Init callback received. " +
            $"Initialized: {FB.IsInitialized}"
        );

        if (!FB.IsInitialized)
        {
            Debug.LogError("[Meta SDK] Initialization failed.");
            return;
        }

        CompleteInitialization();
    }

    private static void CompleteInitialization()
    {
        if (!FeatureConfig.ExternalServicesEnabled)
        {
            return;
        }

        FB.ActivateApp();
        Debug.Log("[Meta SDK] ActivateApp called.");
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (FeatureConfig.ExternalServicesEnabled && !isPaused && FB.IsInitialized)
        {
            FB.ActivateApp();
            Debug.Log("[Meta SDK] ActivateApp called after resume.");
        }
    }

    private static void OnHideUnity(bool isGameShown)
    {
        Debug.Log($"[Meta SDK] OnHideUnity: {isGameShown}");
    }
}

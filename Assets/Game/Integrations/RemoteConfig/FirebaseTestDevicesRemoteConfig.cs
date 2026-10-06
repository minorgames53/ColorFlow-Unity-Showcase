using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.RemoteConfig;
using Game.Integrations.Firebase.Core;

namespace Game.Integrations.RemoteConfig
{
    // One request owned by DeveloperPanelBootstrap. No timers, scene listeners or realtime listener.
    internal static class FirebaseTestDevicesRemoteConfig
    {
        internal const string ParameterKey = "test_devices";

        internal sealed class Result
        {
            internal string Json;
            internal DateTime FetchedAtUtc;
            internal string Failure;
            internal bool Succeeded => Failure == null;
        }

        internal static async Task<Result> FetchAtBootAsync(CancellationToken cancellation)
        {
            try
            {
                var initialized = new TaskCompletionSource<bool>();
                FirebaseAppInitializer.EnsureInitialized(
                    () => initialized.TrySetResult(true),
                    _ => initialized.TrySetResult(false));
                using (cancellation.Register(() => initialized.TrySetCanceled()))
                {
                    if (!await initialized.Task)
                        return new Result { Failure = "Firebase initialization failed." };
                }

                cancellation.ThrowIfCancellationRequested();
                FirebaseRemoteConfig remote = FirebaseRemoteConfig.DefaultInstance;
                await remote.SetDefaultsAsync(new Dictionary<string, object>
                {
                    { ParameterKey, "{\"devices\":[]}" }
                });
                cancellation.ThrowIfCancellationRequested();

                // A restart should attempt a fresh fetch, not reuse the SDK's default 12-hour cache.
                // This is called once per Boot; SDK server throttling still applies.
                await remote.FetchAsync(TimeSpan.Zero);
                cancellation.ThrowIfCancellationRequested();
                if (remote.Info.LastFetchStatus != LastFetchStatus.Success)
                    return new Result { Failure = "Remote Config fetch failed or was throttled." };

                DateTime fetchedAtUtc = remote.Info.FetchTime.ToUniversalTime();
                // false means values were already activated, not that the fetch failed.
                await remote.ActivateAsync();
                cancellation.ThrowIfCancellationRequested();
                return new Result
                {
                    Json = remote.GetValue(ParameterKey).StringValue,
                    FetchedAtUtc = fetchedAtUtc
                };
            }
            catch (OperationCanceledException)
            {
                return new Result { Failure = "Boot request canceled or timed out." };
            }
            catch (Exception)
            {
                // Do not log parameter contents, device identifiers or cache data.
                return new Result { Failure = "Remote Config request could not complete." };
            }
        }
    }
}

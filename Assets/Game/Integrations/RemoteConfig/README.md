# Remote test devices

The public showcase sets `FeatureConfig.ExternalServicesEnabled` to `false`.
Boot skips the request and cached authorization entirely, leaves the device UID
empty, and denies developer access. The integration behavior below applies only
after external services and native configuration have been explicitly restored.

`DeveloperPanelBootstrap` owns access and starts one Firebase Remote Config request
from Boot per application/Play session. Settings, scene changes and foreground
callbacks never fetch. A restart attempts a new fetch (`TimeSpan.Zero`); Firebase
server throttling can still prevent an update. No realtime listener is registered.

The Boot-started request is asynchronous and can finish after the initial scene
transition; it does not hold up gameplay. Access stays locked until it completes
or the 15-second deadline selects a fallback. Late results after timeout or session
shutdown cannot change application permission or refresh the application cache.

## Firebase Console setup (manual)

In the existing Firebase project's **Remote Config**, create a JSON parameter named
`test_devices`. Use an empty default first:

```json
{"devices":[]}
```

To register a phone:

1. Obtain its `UID:` from Settings or the support email.
2. In Unity select **Game > Developer Tools > Test Device UID Converter**.
3. Paste the UID (with or without `UID:`), enter a non-sensitive device label,
   and copy the generated device entry.
4. Add that entry to the parameter's `devices` array and publish the change.
5. Fully close and reopen the game through Boot. When loading has completed,
   open Settings and click UID three times.

Example using SHA-256 of the synthetic UID `abc` (not a real test device):

```json
{
  "devices": [
    {
      "deviceName": "Synthetic example only",
      "uidSha256": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    }
  ]
}
```

The approved privacy recommendation is implemented as `uidSha256`, not plaintext
`uid`. Raw UID fields, missing/wrong-shaped arrays, invalid hashes, duplicate JSON
properties and oversized documents are rejected. Empty arrays are valid and revoke
all access on the next successful Boot fetch. Hashes are case-insensitive; the UID
itself is trimmed but its letter case is preserved before hashing. Duplicate hashes
are deduplicated. Maximum: 512 devices / 128 KiB JSON.

## Offline behavior and session ownership

- Only a successfully fetched, parsed snapshot is written to the dedicated
  `ColorFlow.DeveloperTools.test_devices.v1` PlayerPrefs key. No game save changes.
- The SDK's actual fetch timestamp is retained. Reading cache does not renew it.
- A fetch failure/timeout or malformed value uses the last valid snapshot only if
  it is less than 24 hours old. No valid cache means developer access is denied.
- Missing published parameter uses the empty in-app default (denies access).
- While Boot fetch is pending, cached permission is not exposed: a new response
  might revoke it. Clicks during this period do not count; click three times again
  once the request has finished.
- The local `test_devices.asset` and its class are preserved but are no longer read
  for permission. They are not a fallback and are never merged with remote data.
- Session unlock is never persisted. Scene changes preserve the current session;
  app restart/Play re-entry resets it, including disabled domain reload.
- Expiry closes an open developer panel and hides its floating button. Foreground
  handling only checks local expiry; it does not perform a network request.
- Removing a device remotely is not immediate revocation: the device must fetch
  at its next Boot, or its cached authorization must expire.

Remote Config values and local cache are inspectable by clients. Hashes reduce
exposure of raw IDs; they are not authentication or tamper protection. Do not put
secrets, real employee names, or sensitive operations behind this client-only gate.

## SDK / wiring

- Firebase Remote Config Unity SDK **13.11.0**, matching the existing Firebase SDK.
- Uses the existing `FirebaseAppInitializer`; Firebase initialization is no longer
  conditional on Analytics/Crashlytics collection defines.
- Existing Boot `DeveloperPanelBootstrap` and Settings UID trigger are reused.
  No new scene object or Inspector reference is required.
- Android dependencies are resolved by EDM4U. iOS native dependencies are exported
  through `RemoteConfigDependencies.xml`; verify the build on macOS/Xcode.
- Native macOS Remote Config bundle has an explicit `.gitignore` exception so it
  accompanies the integration when the project is moved to a Mac.

## Verification

**Game > Developer Tools > Validate Remote Test Devices (Offline)** runs the pure
data checks without networking, Play Mode, PlayerPrefs writes or scene mutations.

Device tests still required (start from Boot; use a disposable game save):

1. Valid listed UID unlocks only on the third click; unlisted UID never unlocks.
2. Settings close/reopen resets click count. Menu/Game transitions and foreground
   changes create no further requests and retain the unlocked session.
3. Add/remove a remote entry: current session does not refresh; restarting fetches
   the published configuration. Check SDK throttling/failure messages separately.
4. Offline launch with fresh cache works; no cache/expired cache fails closed.
5. Malformed remote data preserves fresh cache; a valid empty list revokes it.
6. No old request result grants access after timeout, scene teardown, or Play reset.
7. App/Play restart locks access, also with domain and scene reload disabled.
8. Android and iOS non-development builds: support email UID matches the converter
   input, developer Ads/Save actions remain guarded, normal gameplay is unaffected.

Reference: https://firebase.google.com/docs/remote-config/unity/get-started

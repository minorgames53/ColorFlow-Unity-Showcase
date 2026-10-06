# Lunar console access

In the public showcase, external services and remote developer authorization are
disabled by `FeatureConfig.ExternalServicesEnabled=false`. Lunar stays disabled;
previously cached device authorization does not bypass this guard. The integration
details below describe the opt-in service configuration.

The already imported **Lunar Mobile Console FREE 1.9.0** in `Assets/LunarConsole`
is reused. No package download, upgrade or vendor-source modification is needed.

## Single permission owner

`DeveloperPanelBootstrap.IsDeviceAuthorized` is the only test-device decision.
Lunar does not require the developer panel's separate three-click session unlock.

The existing bootstrap reads the existing support UID from
`SystemInfo.deviceUniqueIdentifier`, then `TestDeviceAccessSnapshot.HashUid` hashes
the trimmed, case-preserved UID with SHA-256. The existing `test_devices` Remote
Config JSON contains `devices[].uidSha256`. Lunar reads neither identifiers nor
the list; it only consumes `IsDeviceAuthorized`.

`FirebaseTestDevicesRemoteConfig.FetchAtBootAsync` uses the shared
`FirebaseAppInitializer`, sets an empty default, fetches with `TimeSpan.Zero` and
activates once per Boot. The existing 15-second deadline, parsing rules and cache
policy are unchanged. Permission is unavailable while resolution is pending.
Failure/timeout may authorize only through the existing validated cache, younger
than 24 hours. Missing/expired/invalid cache, unsupported UID and unlisted devices
remain denied. An error alone never grants access.

There is no new Remote Config key, whitelist, device lookup, PlayerPrefs flag,
network request, refresh loop or polling. The same permission already protects
developer Ads/Save/level testing and analytics debug tools.

## Wiring and lifecycle

- `Boot.unity / DeveloperPanel` keeps its existing `DeveloperPanelBootstrap`.
- A **disabled** `LunarConsole` component was added to that same persistent root.
  `DeveloperPanelBootstrap.lunarConsole` references it directly.
- Settings were copied from the imported `LunarConsole.prefab`: SwipeDown gesture,
  capacity 4096, trim 512, exception warnings enabled, log overlay disabled.
- No additional root, prefab instance or runtime object lookup is used.
- Boot resolution applies permission immediately after `accessReady = true`.
  Existing cache expiry/revocation and session reset apply denial immediately.
  Owner disable tears down Lunar; re-enable reapplies the existing permission.
- Repeated decisions do not toggle an already matching component. Scene changes
  preserve the existing owner. Foreground processing checks existing local expiry;
  it does not fetch again. There is no new same-session remote refresh mechanism.
- Editor always leaves Lunar disabled and uses Unity's Console. Development builds
  have no authorization bypass; release builds use the same test-device decision.

**FREE API distinction:** `SetConsoleEnabled(bool)` exists but its implementation
is compiled only for FULL. This integration uses `MonoBehaviour.enabled` instead.
The shipped `OnEnable` creates the native platform and subscribes to
`Application.logMessageReceivedThreaded`; `OnDisable` destroys the platform,
removes its UI/gesture and unsubscribes the log callback. An inactive native
platform cannot be opened even by `LunarConsole.Show()`.

Lunar's `Awake` can register its lightweight singleton while its component is
disabled, but no native console or log collection starts until authorization.
`LunarConsole.isConsoleEnabled` in this package checks singleton existence, not
permission; do not use it as an access check.

The scene build validator rejects an enabled/unowned Lunar component, a missing
reference or a console on another root. Do not install the package's enabled
sample prefab into other scenes. Keep the imported native SDK enabled for builds:
the runtime component, not the package compile switch, controls access.

## Logs

Once authorized, Lunar receives the standard Unity log/warning/error stream
without integration-specific filters. Logs emitted before authorization are not
buffered or replayed. Native-only SDK logcat/Xcode output is visible only if the
SDK also forwards it into Unity's log pipeline. Lunar does not enable other SDKs'
debug logging. Firebase, AdMob and ATT/UMP behavior is unchanged.

## Validation and device acceptance

Performed without Play Mode or live save changes:

- Existing test-device parser/cache validation: 38 offline checks passed.
- Lunar FREE source compiled for Android and iOS without `UNITY_EDITOR`.
- Bootstrap integration compiled under Android and iOS conditions.
- Boot/Menu/Game scenes inspected for duplicate Lunar components and missing scripts.
- Disabled Inspector wiring and the build-time denial guard checked.
- Unity Console checked for compile errors.

Device checks still required:

1. Listed device: after Boot resolution, open the console using SwipeDown and verify
   Unity log/warning/error output. No Settings UID unlock should be required.
2. Unlisted device and pending resolution: gesture/API cannot open the console.
3. Offline with no/expired cache: denied. Offline with a fresh authorized cache:
   follows the existing cached permission, then closes when it expires.
4. Repeat scene transitions/foreground changes: no duplicate console or log subscriptions.
5. Remove the device remotely and restart: successful fetch revokes access.
6. Verify Android and iOS release builds on devices. Native builds were not run here.

Changed: `DeveloperPanelBootstrap.cs`, `Boot.unity`.
Added: `LunarConsoleBuildValidation.cs`, this README and Unity-generated metadata.
Unity's scene save also dropped the obsolete serialized `testDevices` field, which
was already absent from the bootstrap class; the old local asset remains untouched
and is still not used for authorization.
The existing, imported `Assets/LunarConsole/` folder must be included in version
control with its metadata. No manual asset import or Inspector assignment remains.

Asset: https://assetstore.unity.com/packages/tools/gui/lunar-mobile-console-free-82881

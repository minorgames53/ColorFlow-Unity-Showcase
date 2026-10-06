# Color Flow — Tenjin

The public showcase sets `FeatureConfig.ExternalServicesEnabled=false`, disables
Tenjin in `Analytics.asset`, and leaves both SDK keys empty. No Tenjin instance,
connect, attribution, or revenue dispatch is started. The existing iOS plist hook
also disables native collection/Meta initialization for showcase exports. The
integration behavior below applies only after an explicit private-service setup.

## Installation and ownership

- Official `com.tenjin.sdk`, UPM release tag **1.21.0**, commit `8c55e92f5cfc2ee59fc6ad2afc19fed200940fdc`.
- Release verified through GitHub Releases API on 2026-09-15. Package name is `com.tenjin.sdk` (the LLM guide's sample name differs).
- `Boot / AnalyticsBootstrap`: existing `Analytics.asset` reference plus serialized `consentOwner` referencing the existing `AdsService`.
- No custom manager/root, no runtime Find/Resources loading. The SDK itself creates its own hidden persistent callback object through `Tenjin.getInstance`.
- Firebase provider and AnalyticsService dispatch/GA4 retry behavior unchanged. Attribution dispatch is separate so Tenjin failures cannot replay GA4 purchases.
- Previous attribution SDKs were already removed. Lunar and Remote Config authorization are unchanged; Tenjin is NOT test-device gated.

## Privacy and sessions

The existing AdsService UMP completion/privacy-options notification drives Tenjin. No new ATT or UMP request is made. Unknown consent fails closed. UMP's IAB TCF preferences are compatible with native `OptInOutUsingCMP` (purpose 1); missing/malformed purposes in GDPR regions are rejected before the native SDK's permissive fallback. Tenjin reads Google DMA consent itself.

`Connect` runs after consent initialization and on a real `OnApplicationPause(true -> false)` transition. Initial false callbacks and repeated same-frame callbacks do not duplicate Connect. Ad-disabling in AdsConfig prevents the existing UMP lifecycle; consequently Tenjin also stays closed until that consent owner resolves. No-Ads entitlements do not disable UMP.

SDK event/IAP caching is explicitly enabled only while collection/CMP permits it and disabled explicitly on revocation, because Tenjin persists the cache setting. SDK cache/network delivery is not a server acknowledgement. Pre-connect custom events use a bounded in-memory queue (128); no custom install/session events.

## Events

| Telemetry | Source | Tenjin API |
|---|---|---|
| Install/open/session | UMP completion; background -> foreground | Connect |
| level_achieved | LevelResultFlowController.BeginWin -> LevelAnalyticsTracker.CompleteLevel | SendEvent(name, displayedLevel.ToString(InvariantCulture)) |
| Purchase | StoreManager confirmed transaction ledger | Transaction |
| Rewarded/interstitial revenue | Existing instance's OnAdPaid -> HandlePaid | AdMobImpressionFromJSON |

Tutorial attribution is disabled: no `tutorial_complete` is sent or queued by Tenjin. Tutorial gameplay and existing SaveManager completion/unlock state are preserved. A revived win is sent once without changing GA4's terminal-attempt semantics. No per-level event names.

## Purchase validation and deduplication

Unity IAP 5 consumable receipts disappear after confirmation. `TenjinPurchaseValidation` snapshots the PendingOrder into the existing IAP analytics ledger BEFORE confirmation. Android parses the unified GooglePlay Payload into original purchase JSON + signature, checks purchaseState=0, and retains cart quantity. iOS uses Apple.jwsRepresentation then AppReceipt fallback. Prices are numeric doubles, not locale-formatted strings.

Only confirmed rows dispatch: failed/cancelled/deferred and merely pending orders never produce revenue. Existing confirmed/historical rows default to not enrolled; restores do not enroll. Purchases outside a level are supported without adding GA4 level events. There are no subscriptions in the current catalog.

The existing save ledger now carries `tenjinPurchasePending` and `tenjinPurchaseDispatchReserved`. Reservation is saved **before** calling Transaction, then receipt/signature are cleared. This deliberately prioritizes at-most-once SDK handoff: a process crash between durable reservation and native handoff may lose that event, rather than duplicate revenue. There is no cross-process atomic transaction with a native SDK; native cache/retry handles events after successful handoff. Do not treat reserved as validated/received by Tenjin.

## AdMob

Tenjin 1.21.0's dedicated helpers have static per-format subscription flags that skip replacement ad instances. We therefore use its official manual API with the exact adapter JSON schema, through the project's existing removable callback and one paid-event guard per loaded full-screen ad. No parallel helper subscription, no banner, no custom revenue event.

`value_micros` is a long in Android micros; Tenjin's iOS adapter expects currency units in that field, so iOS divides by 1,000,000 exactly once. The response ID, ad unit, precision, currency and mediation adapter are supplied as in the official adapter.

## Native/build configuration

- Android: SDK Maven 1.23.0, Ads Identifier 18.1.0, AppSet 16.1.0; explicit Google Play Install Referrer 2.2 through existing EDM4U; INTERNET/ACCESS_NETWORK_STATE/AD_ID permissions.
- The Android build postprocessor appends the official Tenjin R8 keep rules once to Unity's already-consumed generated `proguard-unity.txt`, without changing PlayerSettings or adding a duplicate native library.
- EDM4U Force Resolve performed. Gradle 8.13 dependency-only resolution succeeded with Billing 9.0.0, GMA Unity 11.3.0/native 25.4.0, Unity IAP 5.4.2 and Firebase Unity 13.11.0 preserved. No version force/downgrade. SDK auto-detects GMA and adds its standard `tenjin_admob_enabled` define; this does not subscribe our ads.
- iOS: this release bundles an XCFramework ZIP and official Xcode embedding/linking postprocessor, not a Tenjin pod. Framework contains PrivacyInfo.xcprivacy; no second CocoaPods copy is added. Existing Firebase/AdMob pods remain unchanged. A small pre/postprocessor preserves the project's ATT usage description because upstream overwrites it with sample text. Existing SKAdNetworkItems are not modified.
- `TenjinBuildValidation` rejects a missing consent reference and enabled Tenjin with blank platform key in release builds.
- No custom SKAN schema/conversion-value code was invented; dashboard mapping/ownership is still required.

## Validation

### Safe Unity / Lunar diagnostics

`Assets/Game/Data/Configs/Analytics.asset > Tenjin Attribution > Enable Tenjin Debug Logs`
defaults to **false**. Enable it for device diagnostics. It works in release builds,
independently of `Enable Debug Logs` and `Development Build`, and never authorizes
tracking or access to Lunar. No additional scene object/reference is required.

Search Lunar for `[Tenjin]`. The provider logs key **presence only**, initialization,
consent gating, startup/resume Connect, custom event name/integer value, purchase
product/currency/unit price/total revenue/quantity, and ad format/currency/revenue/
ad unit/placement/mediation adapter. Prices use invariant culture. Raw SDK keys,
receipts, signatures, device IDs and exception messages are never included.

`requested` means the integration received an operation; `Queued until Connect`
means it has not reached the SDK; `SDK call issued` means the public SDK method
returned, **NOT server acknowledgement**. Local exceptions report only stage and
exception type. This Unity SDK exposes attribution/deeplink callbacks, but no
per-Connect/event/transaction/ILRD network success/failure callback. Server receipt
and validation must be checked in Tenjin's Live Event tool/dashboard.

SDK 1.21.0 `DebugLogs()` is unimplemented on Android. iOS exposes only an enable
call, with no public disable/redaction option. We do not enable uncontrolled native
logging. Additionally, upstream `Transaction` prints receipt/signature in Development
Builds and `AdMobImpressionFromJSON` prints raw JSON. A scoped Unity `ILogHandler`
intercepts only those known synchronous vendor messages around these two calls,
restoring the previous handler in `Dispose` even after exceptions. Other Unity
log/warning/error messages pass through. Neither the package nor Lunar is patched.

Ad format/placement are **diagnostic context only**: the official AdMob payload
schema has no such fields. The actual Tenjin request retains its official ad unit,
mediation adapter, currency, value, response ID and precision fields. No invented
JSON keys or second custom-impression request is added.

Lunar only subscribes after existing test-device authorization. Earlier initialization
logs may therefore be absent from Lunar (there is no log replay). Once Lunar is
available, background/foreground the app to see a fresh Connect and complete a
level or show a paid ad to see subsequent calls. Do not delay Tenjin for Lunar.

Offline validation now includes 18 diagnostic checks (32 total): flag default and
independence, false preserving the event queue, integer event values, Turkish-locale
revenue formatting, queued/issued distinction, payload/exception redaction, unrelated
logs passing through and scoped logger restoration. These use synthetic in-memory
data, not Play Mode or the native SDK.

- Unity Editor C# Console: zero errors. Offline validation: `Game > Analytics > Validate Tenjin Integration (Offline)` (Boot open), 32 checks. Existing Remote Test Devices: 38 checks.
- Android player-define C# compilation passed. iOS-define C# compilation passed using the Editor's iOS-capable managed core reference; this is not an Xcode/native-link check.
- Windows cannot verify the final iOS archive/signing. No APK/AAB, archive or Play Mode/gameplay test was run.
- After credentials: device checks should cover cold launch/resume; UMP/ATT accept/deny; tutorial completion producing no Tenjin event; level/revive win; confirmed consumable and duplicate/restore callbacks; successive rewarded/interstitial loads; and offline -> online delivery. Check actual receipt/events in Tenjin, not only Unity handoff logs.

## USER MUST ENTER

1. `Assets/Game/Data/Configs/Analytics.asset` Inspector > **Tenjin Attribution > Android Tenjin Sdk Key**: Android app's SDK key from Tenjin CONFIGURE > Apps > Android app. **Ios Tenjin Sdk Key**: corresponding iOS app key. Leave both keys out of source-control commits where possible. No secret is included here.
2. Tenjin **CONFIGURE > Apps > Android app > Public Key**: Google Play's Base64 RSA license/public key (Play Console > Color Flow > Monetization setup / Licensing). This belongs in Tenjin dashboard, NOT Unity.
3. Tenjin **CONFIGURE > Apps > app > App Store Commission > Edit**: actual applicable 0%, 15% or 30% and effective dates. The correct rate depends on the app's store agreement; the integration does not assume one.
4. iOS app **CONFIGURE > Apps > app > SKAdNetwork > SKAdNetwork Configuration**: verify/claim app and supply the business-approved conversion schema CSV. Agree one schema owner with the TikTok setup; do not configure competing mappings.
5. Later TikTok connection (dashboard only): Tenjin campaign **Tracking > App Integration Key** goes into TikTok Events Manager's **Connect to a mobile measurement partner > Tenjin** tracking setting. The generated **TikTok App ID** goes into Tenjin **CONFIGURE > Apps > app > Callbacks > TikTok**. Enable required Install/Purchase/Ad Revenue/custom-event callbacks there. These are NOT Unity SDK keys.

No iOS subscription shared secret is required by the current non-subscription catalog. No Tenjin/TikTok dashboard changes have been made.

## Official sources

- https://github.com/tenjin/tenjin-unity-sdk/releases/tag/1.21.0
- https://github.com/tenjin/tenjin-unity-sdk/blob/1.21.0/README.md
- https://github.com/tenjin/tenjin-unity-sdk/blob/1.21.0/Adapters/TenjinAdMobIntegration.cs
- https://raw.githubusercontent.com/tenjin/sdk-llm-guides/main/guides/unity/llm-guide.md
- https://tenjin.com/docs/upload-conversion-value-schema/
- https://tenjin.com/tr/docs/tiktok-for-business-setup/

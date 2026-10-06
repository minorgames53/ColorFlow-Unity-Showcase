# Color Flow

A Unity color-matching puzzle game built around marble transport, board constraints, and limited conveyor capacity.

## Project Overview

Color Flow is a mobile-oriented Unity project presented as a Unity Developer portfolio. Its purpose is to demonstrate gameplay programming, Unity architecture, mobile game development, and code quality through the gameplay implementation, reusable game services, content authoring tools, and platform integrations.

The project uses **Unity 6000.3.16f1**, C#, and the Universal Render Pipeline. Its three main scenes are `Boot`, `Game`, and `Menu`.

The showcase configuration disables project-managed advertising, purchases, and Firebase, Meta, and Tenjin startup by default. Integration code remains available for inspection. A fresh public checkout is not immediately runnable: some required commercial dependencies are intentionally excluded and must be obtained separately under an appropriate license. Additional asset provenance and license checks remain open, as detailed below.

## Gameplay

Players tap accessible source boxes to release colored marbles onto a conveyor. Marbles travel toward matching target boxes, and filling a target advances its lane. Players must choose releases carefully because available field capacity is limited.

The level system combines several mechanics:

- Connected boxes, keys and locks, crates, panels, directional boxes, gift boxes, spawners, and multiplier gates.
- Hand, Shuffle, and UFO boosters with their own targeting, execution, and completion flows.
- Tutorial steps for the first level and booster unlocks.
- Win rewards, retry, and a cleanup recovery flow that can resume the current attempt.
- Saved progression, lives, currency, and booster inventory.

Levels are authored as ScriptableObjects. A catalog defines their order and which levels participate in deterministic content loops after the initial sequence.

## Technical Highlights

- **Explicit marble ownership:** source release, conveyor entry, target reservation, and arrival use separate lifecycle steps. Capacity is reserved before a release begins, and target completion depends on actual arrival.
- **Session and recovery states:** level flow distinguishes playing, paused, recovering, won, failed, and exiting. Recovery tokens and operation versions guard asynchronous callbacks across retries and scene changes.
- **Data-driven content:** level definitions, feature catalogs, color catalogs, and configuration assets supply gameplay and presentation data. Editor tools import and validate level JSON.
- **Persistent game services:** shared save, lives, audio, haptics, navigation, advertising, and store systems provide common entry points for gameplay and UI.
- **Purchase lifecycle handling:** store code includes pending fulfillment, transaction deduplication, persistent rewards, and confirmation handling. These paths require separate sandbox validation on the target platform.
- **Localization:** Unity Localization and Addressables supply string tables for 11 locales, with Portuguese locale selection and Arabic text presentation support.
- **Development tools:** level management, save inspection, a marble ownership tracker, and developer panels support inspection of gameplay and service state.

## Architecture

The project groups gameplay by feature and keeps shared services and platform adapters in separate top-level areas within `Assets/Game`.

| Area | Responsibility |
| --- | --- |
| `Scripts/Runtime/Features` | Board, source boxes, conveyor, targets, boosters, and level lifecycle |
| `Scripts/Runtime/UI`, `Menu`, `Tutorial` | Game-specific presentation, navigation controls, and guided interactions |
| `Scripts/Runtime/Analytics` | Level attempt context and gameplay progress tracking |
| `Shared` | Reusable services, common UI, persistence, and development utilities |
| `Integrations` | Platform and SDK adapters, build hooks, and integration notes |
| `Scripts/Editor` and feature-specific `Editor` folders | Content authoring, diagnostics, and build support |

`LevelSessionController` owns the active session. `LevelBuildController` assembles its board and lanes, while `SourceBoxBoardController`, `ConveyorController`, and `TargetLaneController` own their respective gameplay stages. `LevelResultFlowController` coordinates outcomes and recovery with the existing service APIs.

Components communicate through serialized references, C# events, and shared service entry points. UI presentation consumes these systems; reward persistence and progression are owned by the corresponding gameplay and service code.

## Project Structure

This is a condensed view of the current project layout:

```text
Assets/
├── Game/
│   ├── Art/
│   ├── Audio/
│   ├── Data/
│   │   ├── Configs/
│   │   └── Levels/
│   ├── Docs/
│   ├── Font/
│   ├── Integrations/
│   ├── Localization/
│   ├── Materials/
│   ├── Prefabs/
│   ├── Scenes/                 # Boot, Game, Menu
│   ├── Scripts/
│   │   ├── Editor/
│   │   └── Runtime/
│   │       ├── Analytics/
│   │       ├── Developer/
│   │       ├── Features/
│   │       ├── Menu/
│   │       ├── Tutorial/
│   │       └── UI/
│   ├── Shaders/
│   └── Shared/
├── AddressableAssetsData/
├── Plugins/
└── ...                        # SDK, rendering, and third-party asset folders
Packages/
ProjectSettings/
```

Third-party packages retain their existing integration locations. The directory structure is a navigation aid; it does not imply separate assembly boundaries.

## Performance / Optimization

The implementation includes a reusable `AudioSource` pool for sound effects, cached audio cue lookups, and per-cue playback intervals. Conveyor warning calculations run when conveyor contents change. Target warning calculations reuse temporary collections, and gameplay owners explicitly clean up their tweens and subscriptions during clear, disable, and scene exit.

These are implementation choices, not benchmark results. This repository does not claim a measured frame rate, memory budget, or verified device performance. Device profiling should cover dense boards, simultaneous transfers, booster animations, recovery, and repeated scene transitions.

## Third-Party Dependencies

Package versions and resolution information are recorded in [manifest.json](Packages/manifest.json) and [packages-lock.json](Packages/packages-lock.json). Major dependencies include:

| Dependency | Use |
| --- | --- |
| Unity Universal RP, uGUI, TextMesh Pro | Rendering and UI |
| Unity Localization and Addressables | Localized content and asset loading |
| DOTween | Gameplay and UI animation |
| Odin Inspector | Inspector and authoring support |
| Unity IAP | Store purchase integration |
| Google Mobile Ads and mediation packages | Advertising and consent integration |
| Firebase | Analytics, crash reporting, and remote configuration |
| Tenjin | Attribution and revenue integration |
| Meta/Facebook SDK | App activation integration |
| Google Play Review | In-app review requests |
| Lunar Console and MOST Haptic Feedback | Device diagnostics and haptics |
| UIEffect and ParticleEffectForUGUI | UI effects |
| Epic Toon FX and bundled fonts | Third-party visual and typography assets |

The local development setup also includes tools such as Hot Reload and Unity MCP. Their presence is separate from the game's runtime features.

Commercial third-party packages and visual assets used by the original project are intentionally excluded from the public repository due to licensing restrictions.

The exclusions are defined in `.gitignore` for the fresh public repository. These files remain installed in the original local workspace; they have not been physically removed. The package manifest and lock file remain unchanged. Each listed asset or folder is excluded together with its paired Unity `.meta` file.

| Dependency | Excluded locations | Effect on a fresh checkout | Licensed setup |
| --- | --- | --- | --- |
| Hot Reload 1.13.22 | `Packages/com.singularitygroup.hotreload/` | Optional Editor tool; no direct gameplay-code dependency was found. Its embedded-package entry remains in the lock file although the folder is excluded and the manifest has no explicit declaration. Fresh package resolution still needs validation. | Obtain a licensed copy from Singularity Group or its Unity Asset Store listing if desired, and follow the vendor's installation instructions. It is not required for gameplay. |
| Odin Inspector | `Assets/Plugins/Sirenix/` | Compilation fails until Odin is restored: six project scripts use attributes such as `Button` and `OnValueChanged`. No Odin serialization dependency was found. | Obtain a licensed copy from Sirenix or the Unity Asset Store and import the required package. |
| DOTween Pro | `Assets/Plugins/Demigiant/DOTweenPro/`, `DOTweenPro Examples/`, `readme_DOTweenPro.txt`, and the bundled `DemiLib/` under the same Demigiant directory | No exclusively Pro API or serialized Pro component dependency was found. Compilation without these files has not been verified. | If needed, obtain a licensed copy from Demigiant's Unity Asset Store listing and follow its setup instructions. Do not replace or remove the included base DOTween as a substitute. |
| Epic Toon FX 1.81 | `Assets/Epic Toon FX/` | Seven project assets reference its materials and mesh; a public checkout therefore has missing visual references until those dependencies are restored. | Obtain a licensed copy from the publisher's Unity Asset Store listing. Import the compatible assets with their original Unity metadata so the expected GUID references resolve. |

Base DOTween and `Assets/Resources/DOTweenSettings.asset`, including its metadata, remain included. The excluded commercial packages are not restored automatically by these instructions or by the public repository.

The following content remains included while owner clearance is pending: `MOST_HapticFeedback` from MOST IN ONE, which is a compile-time dependency; the provenance of `Assets/Game/Art`, `Assets/Game/Audio`, and `Assets/Game/Shared/CreativeAds`; possible Epic Toon FX-derived content under the game's own FX assets; the missing bundled license notice for Nunito fonts; and redistribution notices for the included SDKs. These items have not been approved for redistribution by this preparation step. MOST IN ONE should be obtained from its publisher's licensed distribution if a separate installation is required.

Third-party engines, SDKs, plugins, fonts, and art remain subject to their respective licenses. The exclusions above resolve the distribution choice for those four commercial packages; they do not establish clearance for all remaining repository content.

## How to Run

A fresh public checkout is **not a turnkey project**. The instructions below require the licensed dependencies described above; the checkout has not been validated with those packages absent.

1. Install **Unity 6000.3.16f1** through Unity Hub and open the project root.
2. Acquire and import Odin Inspector under an appropriate license to resolve the known compilation dependency. Restore Epic Toon FX with compatible original metadata to resolve the missing material and mesh references. Follow the vendors' installation instructions; these packages are not automatically downloaded by this project.
3. Allow the declared packages and assets to resolve and import. Check the remaining Hot Reload lock entry during a fresh import; Hot Reload is optional. Compile and inspect the project before deciding whether a licensed DOTween Pro installation is needed, since the configuration without Pro has not been tested.
4. Confirm that compilation succeeds and required prefab references are resolved, then open `Assets/Game/Scenes/Boot.unity` and enter Play Mode. Boot initializes the shared services and chooses the initial scene from saved progression; a new save starts in gameplay, while later progression can open the menu.
5. Click or tap accessible source boxes to release marbles. Progress through levels to encounter additional mechanics and booster tutorials.

Use the showcase configuration for review. `FeatureConfig.ExternalServicesEnabled` is a source-controlled guard set to `false`. The existing initialization paths skip Firebase, Tenjin, Meta, Google Mobile Ads consent/initialization, and store connections. Local saves, lives, currency, boosters, and level progression remain available. Purchases and ad-funded actions are unavailable; no simulated purchase or ad rewards are granted. Remote developer access stays denied, including previously cached authorization, and the Settings UID is empty.

The serialized analytics/crash collection switches and ads switch are also off; `useTestAds` is on, and AdMob IDs are Google's sample IDs. Firebase configuration files contain nonfunctional `demo-color-flow-showcase` placeholders, not credentials for a hosted demo backend. Meta/Tenjin credentials, signing identifiers, cloud account binding, and the support email have been cleared. Shop/remove-ads/privacy-service/rate-us feature switches are off.

Unity's own Analytics, diagnostics, purchasing, and Ads startup settings are disabled as well. The Tenjin offline validator understands the showcase configuration; its scene-wiring checks still require the Boot scene.

Android manifest rules disable native collection and remove automatic Firebase/Meta/AdMob initialization providers. The existing iOS plist postprocessor applies equivalent collection defaults after vendor processing. These native safeguards must be reviewed together with the managed guard before opting into any live service; changing one boolean is not a complete integration setup. Do not add private signing material or production credentials to the repository.

The showcase uses `Showcase` as its company name, `com.example.colorflow.showcase` as its application identifier, and `https://example.com/privacy` as a placeholder privacy URL. These are demonstration values, not a production identity or a live privacy policy. The generated Android dependency-resolver cache, `ProjectSettings/AndroidResolverDependencies.xml` and its paired metadata, is excluded from version control.

`Assets/GeneratedLocalRepo/` and its metadata are also excluded as generated Maven output. The Firebase source artifacts under `Assets/Firebase/m2repository/` remain included; allow External Dependency Manager to resolve them before validating an Android build. No local generated files were deleted.

Local game saves are stored under `Application.persistentDataPath`. Company or application identifier changes can select a separate save location; previous saves have not been deleted or migrated. Existing saved progress at the selected location can affect the initial scene and tutorial state. The project includes a Save Debugger for inspecting local state.

Editor import, compilation, Console review, and a fresh-checkout gameplay walkthrough remain required release checks; this README does not certify that those checks have passed.

For mobile validation, inspect the merged Android manifest / exported iOS plist and verify startup, resume, and scene transitions on a device with network monitoring. Editor tooling and package resolution are separate from the game's disabled service initialization. The previously excluded Firebase App/Analytics macOS bundles also require a documented installation or distribution decision before claiming a fresh macOS checkout is runnable.

## Documentation

The detailed engineering notes are currently written in Turkish:

- [Architecture](Assets/Game/Docs/ARCHITECTURE.md): responsibility boundaries, shared services, configuration, UI, and animation ownership.
- [Game Flow](Assets/Game/Docs/GAME_FLOW.md): startup, progression, session states, rewards, recovery, retry, and cleanup.
- [Analytics](Assets/Game/Docs/ANALYTICS.md): event ownership, level context, asynchronous operations, and provider boundaries.
- [Marble Tracker](Assets/Game/Docs/MARBLE_TRACKER.md): Editor diagnostics for marble identity, transfers, and ownership.
- [Localization Validation](Assets/Game/Docs/LOCALIZATION_VALIDATION.md): localization checks and their scope.

Integration-specific notes are available for [Tenjin](Assets/Game/Integrations/Tenjin/README.md), [Remote Config](Assets/Game/Integrations/RemoteConfig/README.md), and [Lunar Console](Assets/Game/Integrations/LunarConsole/README.md).

## My Contribution

My work presented in this portfolio covers the project-specific gameplay and supporting systems: board and conveyor interactions, marble transfer ownership, level progression and recovery, boosters, UI flows, persistence, content authoring tools, and the code connecting those systems to external SDKs.

The contribution scope is the game's implementation and integration work. Third-party engines, SDKs, plugins, fonts, and art assets are credited to their respective creators; they are not presented as original work.

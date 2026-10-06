# Editor-only marble tracking

Open **Tools → Color Flow → Marble Tracker** during Play Mode. No Inspector assignments, scene edits, prefab components, packages or ProjectSettings changes are required. New classes need a normal Unity script recompile; Hot Reload alone may not load them. If scripts/domain reload during an attempt, rebuild the level to obtain a complete history.

## Enable / disable

The window's **Enable Tracking** checkbox is available both in Edit Mode and Play Mode. It defaults to enabled and is saved per project path in `EditorPrefs`, not in gameplay saves or project assets. The existing Editor lifecycle bridge restores it without requiring the window to be open, including when Play Mode domain reload is disabled. The runtime tracker has no `UnityEditor` dependency.

Disabling immediately clears the current records/history and prevents registration, build inventory audits, container validation and automatic warnings. Re-enabling waits for the next level build/retry; it does not scan or reconstruct the current attempt. Session/scene resets preserve the preference. Existing Console entries are not deleted. All code remains Editor-only.

Toggle acceptance checks (not run in Play Mode): disable during an attempt and verify records clear; retry and win/lose while disabled must not produce tracker warnings; enable mid-attempt and verify no tracking until rebuild; restart the Editor and repeat with domain reload disabled to verify persistence. No Inspector assignments are required.

## Source of truth and hooks

The tracker observes existing gameplay state; it never spawns, moves, reparents, repairs or claims a marble. All added runtime code is inside `#if UNITY_EDITOR`, including the tracker types, storage, lifecycle callbacks, scans, string construction and logging. The window and play/scene lifecycle bridge are in `Scripts/Editor`.

| Existing owner | Observed commit / representation |
| --- | --- |
| `Marble` | Successful initialization registers the reference and sequential attempt ID; field release, enable, disable and destroy record lifecycle evidence. There is no marble pool in the inspected creation paths. Reinitializing the same live object gets a new ID and flags the previous incarnation. |
| `SourceBox` | Successful `marbles.Add` records origin/slot; committed normal release is transit until `Marble.Release`; Hand detachment is transit while target reveal is pending; `AddRecoveredMarble` commits recovery arrival. |
| `SourceBoxBoardController` | Initial conveyor creation and multiplier duplication record origins. Source/spawner/gift/sealed-source creation all converge on `SourceBox.Initialize`. Build audits compare unsealed source cells with actual created counts. |
| `ConveyorEntryZone` | Pending/deferred admission, registered entry transfer and successful detach. Failed slot commit returns through existing pending admission. |
| `ConveyorController` | Slot assignment commits Conveyor; removal commits transit; actual slot changes record updated slot/entry order. Motion records and occupied slots are cross-checked. Incoming/outgoing reservations are not separate owners. |
| `TargetLaneController` + Hand/UFO partials | Transfer dictionary/list registration records transit; shared `ConfirmTransferArrival` records TargetBox only after actual arrival. UFO reservation cancellation restores the previous UFO owner. |
| `TargetBox` | Slot children represent arrived marbles, excluding active transfer animations already parented to slots. Completion authorizes the existing inactive presentation; completed targets remain accounted. |
| `UfoBoosterController` | Successful capture records UFO; captured records with a pending target reservation are presentation/rollback aliases, not duplicate ownership. Conveyor restoration uses the central conveyor hook. |
| `FailRecoveryController` | `cleanupOwnedMarbles.Add` commits Recovery; recovered SourceBox acceptance uses the SourceBox hook. Cleanup destruction before a level-clear boundary is suspicious. |
| `LevelBuildController` / `LevelSessionController` | Clear ends the old attempt before cleanup callbacks; build begins a fresh attempt and snapshots inventory. Session fallback build/exit is covered. Won, finalized Failed and Recovering run validation before state-change subscribers reset boosters/recovery. |

Shuffle reorders target objects and queues, without changing marble ownership. Validation reports current lane/depth alongside the target's original identity. The normal, connected, Hand and UFO completion paths retain completed target objects until level clear, so their slot contents remain verifiable while inactive.

## Records and inventory

Each runtime reference has an attempt-local `Marble #001` ID, Unity instance ID, displayed/content level context, attempt, current/initial color, immutable origin, current owner/detail, previous and last known location, state, validation snapshot and transition history. Every ownership history entry includes frame, scaled runtime time, realtime, from/to locations and owner descriptions, and reason. Enable/disable entries preserve the last ownership transition and transfer age.

Locations: `Unknown`, `SourceBox`, `DropZone`, `Conveyor`, `TargetBox`, `InTransit`, `Recovery`, `Ufo`, `Destroyed`, `Removed`.

Coordinates are row/column, 1-based in the UI. Conveyor slots and entry queue indices are 0-based. Object names/paths never determine ownership; object identity and existing logical coordinates do. Target detail includes initial lane/depth, color, slot and instance identity. Current lane/depth and queue indices are displayed in the latest observation after validation.

`Initial` counts runtime objects at build completion; `Later` counts subsequent creations. **Authored supply** reuses `LevelDefinitionValidator`'s source/spawner/gift/sealed-source/multiplier counting rules and adds `InitialConveyorMarbles`. Target requirement reuses the same validator's target data and `LevelDefinition.TargetBoxCapacity`. Startup audits additionally compare actual unsealed source creation, committed conveyor seeds and runtime target capacity. This distinguishes data imbalance/build failure from later disappearance without pretending deferred contents already have runtime IDs.

`Not yet registered` is authored supply minus registered runtime objects: it can mean deferred production, an untriggered gate, or a production failure. It is not automatically a missing runtime marble. `Accounted` counts registered records without current suspicion, including healthy transit and completed targets. `Missing` counts suspicious candidates, including ownership discrepancies; it does not assert that all candidates were physically destroyed. Requirement minus accounted is therefore not by itself proof of loss.

## Validation and limits

Full scans run only at build completion, **Validate Now**, and terminal/recovery checkpoints. Window repaint only reads records. Validation reads source contents with release-state semantics, entry pending/deferred/active transfer lists, conveyor motions and slots, target active/pending-Hand transfers and target slot contents (including inactive completed boxes), recovery owned marbles, and UFO captured ownership.

Normal SourceBox lists retain old references after release. These are excluded unless the unreleased marble still belongs to that source's release animation. The loose DropZone has no explicit gameplay collection: validation uses the existing `ReleasedMarbleContainer` reference and Marble flags, as recovery does. More specific entry/transfer collections take precedence. No spatial bounds or physics-based loss prediction is invented.

Flags: Unknown, stuck transit, tracker/owner mismatch, duplicate ownership, unexpected disable, unexpected removal/destruction, unregistered scene marbles and internal conveyor motion/slot discrepancy. Owner comparison uses object identity, not display labels. A brief interval with no container is allowed for transit; after the configurable default **12 scaled seconds** it is suspicious. Editor/timeScale pause and explicit UFO source/entry tween freezes do not age that timeout. Long legitimate target reservations can require a larger window timeout.

Collection results are snapshots. A later tracked ownership transition marks the prior comparison stale; the detail panel requests another validation. Current active/destroyed state and transit age are evaluated without scanning. Validation does not rewrite tracked location to make it agree with actual collections. **Copy report** copies inventory issues and all marble histories to the clipboard.

Automatic checks produce one combined warning for newly observed anomalies. Repeated identical per-marble statuses/inventory issues are suppressed within the attempt. Manual validation displays findings without Console spam. Retry resets records/IDs/history and increments the displayed level's session-local attempt count. Scene unload/change clears current records; a new Play session resets counters, including when domain reload is disabled. An ended attempt remains visible only until the next build/scene change.

## Validation handoff

Performed without entering Play Mode:

- Compile runtime assembly with Editor symbols, and compile the Editor assembly against it, using the installed Unity C# compiler and project references; outputs are isolated in a temporary directory.
- Compile runtime assembly without `UNITY_EDITOR` symbols.
- Parse the changed gameplay files with player preprocessor symbols and compare C# tokens against their pre-change versions; additions must disappear completely. Inspect emitted player DLL metadata for absence of tracker definitions/references.
- Review creation, normal/Hand/UFO transfer, rollback, completion, recovery, seed, multiplier, retry and clear call sites. Check whitespace and Unity Console through MCP. Final Unity import/recompile and Console confirmation remain with the developer.

Results (2026-09-18): all three isolated compilations passed; the player-token comparison passed for all 15 modified gameplay files, both new C# files reduced to no player code, and the player DLL had no tracker definitions/references. Compiler warnings were from existing Shop/HUD/Tenjin/PlayModeComponentSaver code. Unity was outside Play Mode and had pending external changes; its Console still contained the superseded intermediate conveyor compile errors after that source was fixed and independently recompiled successfully. No forced Editor refresh/compile, Console clear, scene save or Play Mode test was performed. Let Unity import/recompile the final files before the final Console check.

Developer acceptance scenarios (not run by this task):

1. Normal level: validate at startup, during source release, pending entry, entry animation, conveyor catch-up, partial target arrival and completed inactive targets. Expect no spurious duplicate/missing records.
2. Hand: validate while awaiting target reveal and during physics/bounce/flight; inspect a single continuous history through target arrival. Include connected-source selection and cancellation.
3. UFO: validate capture/chamber/reservation/fire/arrival, cancelled reservation, restore-to-conveyor, and movement freeze longer than the timeout. Source/entry freeze should not produce stuck-transit warnings.
4. Clean Up: validate recovery ownership, arrival in a recovered source, re-release and second recovery. Original ID/color/source origin must remain unchanged; no new attempt.
5. Shuffle: include partially filled targets; current lane/depth changes, marble identity and TargetBox owner remain stable.
6. Deferred/multiplier level: check startup authored inventory, later unique IDs for spawner/gift/sealed sources and gate duplicates, including parent origin on duplicates. Initial conveyor seeds must match authored slots/counts.
7. Retry/rebuild and scene exit: fresh IDs, incremented attempt, no old callbacks contaminating new history. Repeat with domain reload disabled.
8. Fault injection in a disposable developer session: remove an owner collection entry, duplicate ownership, stop a transfer, disable/destroy an unconsumed marble. Validate must reveal the anomaly and preserve the last ownership transition; repeat validation/checkpoints to confirm warning deduplication. The tracker must never repair gameplay.

## Affected files

New:

- `Assets/Game/Scripts/Runtime/Developer/MarbleDebugTracker.cs` (+ Unity-generated `.meta`)
- `Assets/Game/Scripts/Editor/MarbleTrackerWindow.cs` (+ Unity-generated `.meta`)
- `Assets/Game/Docs/MARBLE_TRACKER.md`

Editor-guarded hooks/read-only adapters:

- `Features/SourceBoxes/Marble.cs`, `SourceBox.cs`, `SourceBoxReleaseAnimator.cs`, `SourceBoxBoardController.cs`
- `Features/Conveyor/ConveyorController.cs`, `ConveyorEntryZone.cs`
- `Features/TargetBoxes/TargetBox.cs`, `TargetLaneController.cs`, `TargetLaneController.HandTransfer.cs`, `TargetLaneController.Ufo.cs`
- `Features/Boosters/UfoBoosterController.cs`
- `Features/Levels/FailRecoveryController.cs`, `LevelBuildController.cs`, `LevelSessionController.cs`, `LevelDefinitionValidator.cs`

The `Features/` paths above are relative to `Assets/Game/Scripts/Runtime/`. `Assets/Game/Docs/GAME_FLOW.md` links this document. Pre-existing unrelated working-tree changes are preserved.

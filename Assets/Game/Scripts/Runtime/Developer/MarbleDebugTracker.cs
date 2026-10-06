#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Gameplay.Boosters;
using Gameplay.Conveyor;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gameplay.MarbleDebug
{
    public enum MarbleDebugLocation { Unknown, SourceBox, DropZone, Conveyor, TargetBox, InTransit, Recovery, Ufo, Destroyed, Removed }

    public sealed class MarbleDebugTransition
    {
        public int Frame;
        public float Time, Realtime;
        public MarbleDebugLocation From, To;
        public string FromOwner, ToOwner, Reason;
        public override string ToString() => $"F{Frame} / {Time:F2}s (real {Realtime:F2}s)  {From} [{FromOwner}] -> {To} [{ToOwner}]  {Reason}";
    }

    public sealed class MarbleDebugRecord
    {
        public int Id, Level, Attempt, InstanceId;
        public Marble Marble;
        public MarbleColorId Color, InitialColor;
        public string Origin, OwnerDetail, LastKnownOwner, ValidationIssue = "", ObservedOwners = "";
        public Object Owner;
        public Object UfoOwner;
        public float LocationTime;
        public float TransitPausedDuration;
        public MarbleDebugTransition LastOwnershipTransition;
        public MarbleDebugLocation Location, PreviousLocation, LastKnownLocation;
        public bool CompletedTarget, ExpectedRemoval, Destroyed, WasInitial;
        public int Revision, ValidatedRevision = -1;
        public readonly List<MarbleDebugTransition> History = new List<MarbleDebugTransition>();
        public MarbleDebugTransition Last => LastOwnershipTransition;
        public string Label => $"Marble #{Id:000}";
        public bool Active => Marble != null && Marble.gameObject.activeInHierarchy;
        public string State => Destroyed || Marble == null ? "Destroyed" : Active ? "Active" : "Disabled";
    }

    // A scan result, never a gameplay ownership registry. Rebuilt only at explicit checkpoints.
    public sealed class MarbleDebugObservation
    {
        public Marble Marble;
        public MarbleDebugLocation Location;
        public Object Owner;
        public string Detail;
    }

    public static class MarbleDebugTracker
    {
        private sealed class ReferenceComparer : IEqualityComparer<Marble>
        {
            public bool Equals(Marble x, Marble y) => ReferenceEquals(x, y);
            public int GetHashCode(Marble obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static readonly Dictionary<Marble, MarbleDebugRecord> byMarble = new Dictionary<Marble, MarbleDebugRecord>(new ReferenceComparer());
        private static readonly List<MarbleDebugRecord> records = new List<MarbleDebugRecord>();
        private static readonly Dictionary<int, int> attempts = new Dictionary<int, int>();
        private static readonly HashSet<string> reported = new HashSet<string>();
        private static readonly Dictionary<Object, float> suspendedOwners = new Dictionary<Object, float>();
        private static readonly Dictionary<TargetBox, string> targetOrigins = new Dictionary<TargetBox, string>();
        private static readonly Dictionary<MarbleColorId, int> initial = new Dictionary<MarbleColorId, int>();
        private static readonly Dictionary<MarbleColorId, int> supply = new Dictionary<MarbleColorId, int>();
        private static readonly Dictionary<MarbleColorId, int> requirements = new Dictionary<MarbleColorId, int>();
        private static readonly Dictionary<MarbleColorId, int> seedRequirements = new Dictionary<MarbleColorId, int>();
        private static readonly List<string> inventoryIssues = new List<string>();
        private static int columns = 1;
        public static IReadOnlyList<MarbleDebugRecord> Records => records;
        public static IReadOnlyDictionary<MarbleColorId, int> Initial => initial;
        public static IReadOnlyDictionary<MarbleColorId, int> Supply => supply;
        public static IReadOnlyDictionary<MarbleColorId, int> Requirements => requirements;
        public static IReadOnlyList<string> InventoryIssues => inventoryIssues;
        public static int Level { get; private set; }
        public static int InternalLevel { get; private set; }
        public static int Attempt { get; private set; }
        public static int SceneHandle { get; private set; }
        // The Editor lifecycle bridge applies the saved preference before level builds.
        public static bool TrackingEnabled { get; private set; }
        public static bool IsActive { get; private set; }
        public static bool IsBuilding { get; private set; }
        public static int LastValidationFrame { get; private set; } = -1;
        public static string LastCheckpoint { get; private set; } = "Not validated";
        // Scaled time excludes Editor pause / timeScale suspension. This never affects gameplay.
        public static float TransitTimeout = 12f;

        public static void SetTrackingEnabled(bool enabled)
        {
            if (TrackingEnabled == enabled) return;
            TrackingEnabled = enabled;
            // Never resume a partial history. Enabling waits for the next BeginAttempt.
            Clear();
        }

        public static void ResetSession()
        {
            Clear();
            attempts.Clear();
        }

        public static void Clear()
        {
            byMarble.Clear(); records.Clear(); reported.Clear(); targetOrigins.Clear(); suspendedOwners.Clear();
            initial.Clear(); supply.Clear(); requirements.Clear(); seedRequirements.Clear(); inventoryIssues.Clear();
            IsActive = IsBuilding = false;
            Level = InternalLevel = Attempt = 0;
            SceneHandle = 0;
            LastValidationFrame = -1;
            LastCheckpoint = "Not validated";
            LastScanIssues = Array.Empty<string>();
        }

        public static void BeginAttempt(LevelDefinition definition, Component context)
        {
            if (!TrackingEnabled || !Application.isPlaying || definition == null) return;
            Clear();
            var session = Object.FindFirstObjectByType<LevelSessionController>();
            Level = session != null && session.DisplayedLevelNumber > 0 ? session.DisplayedLevelNumber : definition.LevelNumber;
            InternalLevel = definition.LevelNumber;
            attempts.TryGetValue(Level, out int previous);
            attempts[Level] = Attempt = previous + 1;
            columns = Math.Max(1, definition.ColumnCount);
            SceneHandle = context.gameObject.scene.handle;
            IsActive = IsBuilding = true;
            // Reuse the actual level validator's counting rules, including deferred contents and gates.
            LevelDefinitionValidator.GetDebugInventory(definition, supply, requirements, inventoryIssues);
            if (definition.InitialConveyorMarbles != null)
                foreach (var color in definition.InitialConveyorMarbles)
                {
                    seedRequirements.TryGetValue(color, out int count);
                    seedRequirements[color] = count + 1;
                }
        }

        public static void CompleteBuild()
        {
            if (!IsActive) return;
            IsBuilding = false;
            foreach (var record in records)
            {
                record.WasInitial = true;
                initial.TryGetValue(record.Color, out int count);
                initial[record.Color] = count + 1;
            }
            foreach (var board in Find<SourceBoxBoardController>()) board.DebugCollectInitialBuildIssues(inventoryIssues);
            foreach (var pair in seedRequirements)
            {
                int actual = records.Count(record => record.Color == pair.Key && record.Origin == "Initial conveyor seed" &&
                    record.Location == MarbleDebugLocation.Conveyor);
                if (actual != pair.Value)
                    inventoryIssues.Add($"BUILD SHORTFALL: {pair.Key} initial conveyor seeds expected {pair.Value}, committed {actual}.");
            }
            var targets = Find<TargetLaneController>().SelectMany(lanes => lanes.SpawnedTargetBoxes).Where(target => target != null).ToList();
            foreach (var pair in requirements)
            {
                int actual = targets.Where(target => target.ColorId == pair.Key)
                    .Sum(target => target.AvailableReservationCount + target.ReservedSlotCount);
                if (actual != pair.Value)
                    inventoryIssues.Add($"TARGET BUILD MISMATCH: {pair.Key} authored requirement {pair.Value}, runtime capacity {actual}.");
            }
            Validate("Build complete", false);
        }

        public static void EndAttempt(string reason)
        {
            if (!IsActive) return;
            foreach (var record in records)
            {
                record.ExpectedRemoval = true;
                Move(record, MarbleDebugLocation.Removed, null, "Level cleanup", reason);
            }
            IsActive = IsBuilding = false;
        }

        public static void Register(Marble marble)
        {
            if (!IsActive || marble == null) return;
            if (byMarble.TryGetValue(marble, out var previous))
            {
                Move(previous, MarbleDebugLocation.Removed, null, "Reinitialized instance", "Reinitialize / pool reuse before attempt ended");
                previous.Destroyed = true;
            }
            var record = new MarbleDebugRecord
            {
                Id = records.Count + 1, Marble = marble, InstanceId = marble.GetInstanceID(),
                Level = Level, Attempt = Attempt, Color = marble.ColorId, InitialColor = marble.ColorId,
                Location = MarbleDebugLocation.Unknown, OwnerDetail = "Uncommitted initialization"
            };
            byMarble[marble] = record;
            records.Add(record);
            Move(record, MarbleDebugLocation.Unknown, null, record.OwnerDetail, "Created / initialized");
        }

        public static string SourceDetail(SourceBox source, int slot)
        {
            if (!IsActive) return "";
            string cell = source.IsRecoveredSourceBox ? "Recovered SourceBox" :
                $"SourceBox R{source.CellIndex / columns + 1} C{source.CellIndex % columns + 1}";
            return $"{cell} (#{source.GetInstanceID()}) / Slot {slot + 1}";
        }

        public static void SourceAdded(Marble marble, SourceBox source, int slot)
        {
            if (!IsActive) return;
            SetOrigin(marble, SourceDetail(source, slot));
            SetLocation(marble, MarbleDebugLocation.SourceBox, source, SourceDetail(source, slot),
                source.IsRecoveredSourceBox ? "Recovery arrival committed" : "Source creation committed");
        }

        public static void SetOrigin(Marble marble, string origin)
        {
            if (TryGet(marble, out var record) && record.Origin == null) record.Origin = origin;
        }

        public static void MultiplierCreated(Marble marble, Marble parent, int gate)
        {
            if (!IsActive) return;
            string parentLabel = TryGet(parent, out var record) ? $"{record.Label}; origin {record.Origin}" : "unregistered parent";
            SetOrigin(marble, $"Multiplier Gate {gate} / parent {parentLabel}");
        }

        public static void SetLocation(Marble marble, MarbleDebugLocation location, Object owner, string detail, string reason)
        {
            if (!IsActive || !TryGet(marble, out var record)) return;
            Move(record, location, owner, detail, reason);
        }

        private static bool TryGet(Marble marble, out MarbleDebugRecord record)
        {
            record = null;
            return IsActive && !ReferenceEquals(marble, null) && byMarble.TryGetValue(marble, out record);
        }

        private static void Move(MarbleDebugRecord record, MarbleDebugLocation location, Object owner, string detail, string reason)
        {
            record.History.Add(new MarbleDebugTransition
            {
                Frame = Time.frameCount, Time = Time.time, Realtime = Time.realtimeSinceStartup,
                From = record.Location, To = location, FromOwner = record.OwnerDetail, ToOwner = detail, Reason = reason
            });
            record.PreviousLocation = record.Location;
            record.Location = location; record.Owner = owner; record.OwnerDetail = detail;
            record.LocationTime = Time.time;
            record.TransitPausedDuration = 0f;
            record.LastOwnershipTransition = record.History[record.History.Count - 1];
            if (location == MarbleDebugLocation.Ufo) record.UfoOwner = owner;
            record.Revision++;
            if (location != MarbleDebugLocation.Destroyed && location != MarbleDebugLocation.Removed)
            {
                record.LastKnownLocation = location; record.LastKnownOwner = detail;
            }
        }

        public static void ReleasedToField(Marble marble)
        {
            // Hand's release animation also calls Marble.Release, while the target owns the transfer.
            if (!TryGet(marble, out var record) || marble.IsTransferringToTarget || marble.IsOwnedByUfo) return;
            SetLocation(marble, MarbleDebugLocation.DropZone, marble.transform.parent, "Released field / DropZone", "Field physics enabled");
        }

        public static void RestoreUfo(Marble marble)
        {
            if (TryGet(marble, out var record))
                SetLocation(marble, MarbleDebugLocation.Ufo, record.UfoOwner,
                    "UFO chamber / restored", "Target reservation cancelled; UFO ownership restored");
        }

        public static void SuspendTransfers(Object owner, bool suspended)
        {
            if (!IsActive || owner == null) return;
            if (suspended)
            {
                if (!suspendedOwners.ContainsKey(owner)) suspendedOwners.Add(owner, Time.time);
            }
            else if (suspendedOwners.TryGetValue(owner, out float start))
            {
                foreach (var record in records)
                    if (ReferenceEquals(record.Owner, owner) && record.Location == MarbleDebugLocation.InTransit)
                        record.TransitPausedDuration += Time.time - Mathf.Max(start, record.LocationTime);
                suspendedOwners.Remove(owner);
            }
        }

        public static float TransitAge(MarbleDebugRecord record)
        {
            float pendingPause = record.Owner != null && suspendedOwners.TryGetValue(record.Owner, out float start)
                ? Time.time - Mathf.Max(start, record.LocationTime) : 0f;
            return Mathf.Max(0f, Time.time - record.LocationTime - record.TransitPausedDuration - pendingPause);
        }

        public static void Lifecycle(Marble marble, bool destroyed, string reason)
        {
            if (!IsActive || !TryGet(marble, out var record)) return;
            if (destroyed)
            {
                record.Destroyed = true;
                Move(record, MarbleDebugLocation.Destroyed, null, record.LastKnownOwner,
                    record.CompletedTarget ? "Completed target removed" : reason);
            }
            else
            {
                // Preserve the location and its age; enable/disable must not restart a stuck-transfer timer.
                record.History.Add(new MarbleDebugTransition
                {
                    Frame = Time.frameCount, Time = Time.time, Realtime = Time.realtimeSinceStartup,
                    From = record.Location, To = record.Location,
                    FromOwner = record.OwnerDetail, ToOwner = record.OwnerDetail, Reason = reason
                });
            }
        }

        public static void RegisterTarget(TargetBox target, int lane, int depth)
        {
            if (IsActive) targetOrigins[target] = $"Target Lane {lane + 1} / Initial depth {depth + 1} / {target.ColorId} (#{target.GetInstanceID()})";
        }

        public static string TargetDetail(TargetBox target, int slot)
        {
            if (!IsActive) return "";
            string label = target != null && targetOrigins.TryGetValue(target, out string origin) ? origin : "Unknown target";
            return $"{label} / Slot {slot + 1}";
        }

        public static void TargetCompleted(TargetBox target)
        {
            if (!IsActive || target == null || !target.IsComplete) return;
            foreach (var record in records)
                if (ReferenceEquals(record.Owner, target) && record.Location == MarbleDebugLocation.TargetBox)
                    record.CompletedTarget = true;
        }

        public static void Observe(List<MarbleDebugObservation> result, Marble marble, MarbleDebugLocation location, Object owner, string detail)
        {
            if (!IsActive || ReferenceEquals(marble, null)) return;
            result.Add(new MarbleDebugObservation { Marble = marble, Location = location, Owner = owner, Detail = detail });
        }

        private static T[] Find<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(value => value.gameObject.scene.handle == SceneHandle).ToArray();

        public static void Validate(string checkpoint, bool logWarnings)
        {
            if (!Application.isPlaying || !IsActive) return;
            var observed = new List<MarbleDebugObservation>();
            foreach (var source in Find<SourceBox>()) source.DebugCollectMarbles(observed);
            foreach (var entry in Find<ConveyorEntryZone>()) entry.DebugCollectMarbles(observed);
            foreach (var conveyor in Find<ConveyorController>()) conveyor.DebugCollectMarbles(observed);
            foreach (var targets in Find<TargetLaneController>()) targets.DebugCollectMarbles(observed);
            foreach (var recovery in Find<FailRecoveryController>()) recovery.DebugCollectMarbles(observed);
            foreach (var ufo in Find<UfoBoosterController>()) ufo.DebugCollectMarbles(observed);

            // The released field has no gameplay list. Its existing root + Marble flags are the
            // authoritative representation used by recovery too. Entry/transfer lists take precedence.
            foreach (var board in Find<SourceBoxBoardController>())
            {
                if (board.ReleasedMarbleContainer == null) continue;
                foreach (var marble in board.ReleasedMarbleContainer.GetComponentsInChildren<Marble>(true))
                {
                    if (marble.IsReleased && !marble.IsTransferringToTarget && !marble.IsOwnedByUfo &&
                        !observed.Any(item => ReferenceEquals(item.Marble, marble)))
                        Observe(observed, marble, MarbleDebugLocation.DropZone, board.ReleasedMarbleContainer, "Released field / DropZone");
                }
            }

            var scanIssues = new List<string>();
            foreach (var marble in Find<Marble>())
                if (!byMarble.ContainsKey(marble)) scanIssues.Add($"UNREGISTERED runtime marble {marble.ColorId} instance #{marble.GetInstanceID()}");
            foreach (var record in records)
            {
                var owners = observed.Where(item => ReferenceEquals(item.Marble, record.Marble)).ToList();
                record.ObservedOwners = string.Join(" | ", owners.Select(item => $"{item.Location}: {item.Detail}"));
                record.ValidationIssue = "";
                record.ValidatedRevision = record.Revision;
                if (record.ExpectedRemoval) continue;
                if (owners.Count > 1) record.ValidationIssue = "DUPLICATE OWNER";
                bool matches = owners.Any(item => item.Location == record.Location && ReferenceEquals(item.Owner, record.Owner));
                if (matches && record.Location == MarbleDebugLocation.Conveyor &&
                    !owners.Any(item => ReferenceEquals(item.Owner, record.Owner) && item.Detail == record.OwnerDetail))
                    record.ValidationIssue += " TRACKER / SLOT MISMATCH";
                if (!matches && !record.Destroyed && record.Location != MarbleDebugLocation.Removed)
                {
                    // Unowned transit is allowed briefly (e.g. Hand commit -> reveal registration).
                    if (owners.Count > 0 || record.Location != MarbleDebugLocation.InTransit)
                        record.ValidationIssue += " TRACKER / OWNER MISMATCH";
                }
            }
            LastValidationFrame = Time.frameCount;
            LastCheckpoint = checkpoint;
            LastScanIssues = scanIssues;
            if (logWarnings) LogNewAnomalies(checkpoint);
        }

        public static IReadOnlyList<string> LastScanIssues { get; private set; } = Array.Empty<string>();

        public static string Status(MarbleDebugRecord record)
        {
            if (!TrackingEnabled || record.ExpectedRemoval) return "";
            var issues = new List<string>();
            if (record.Location == MarbleDebugLocation.Unknown) issues.Add("UNKNOWN");
            if ((record.Destroyed || record.Marble == null || record.Location == MarbleDebugLocation.Removed) && !record.CompletedTarget)
                issues.Add("DESTROYED / REMOVED UNEXPECTEDLY");
            if (record.Marble != null && (!record.Active || !record.Marble.enabled) && !record.CompletedTarget)
                issues.Add("DISABLED UNEXPECTEDLY");
            if (record.Location == MarbleDebugLocation.InTransit)
            {
                if (TransitAge(record) > TransitTimeout) issues.Add("STUCK IN TRANSIT");
            }
            if (record.ValidatedRevision == record.Revision && !string.IsNullOrWhiteSpace(record.ValidationIssue))
                issues.Add(record.ValidationIssue.Trim());
            return string.Join("; ", issues);
        }

        public static bool IsAccounted(MarbleDebugRecord record) => !record.ExpectedRemoval && Status(record).Length == 0;

        private static void LogNewAnomalies(string checkpoint)
        {
            var message = new StringBuilder();
            int suspicious = 0;
            foreach (var record in records)
            {
                string status = Status(record);
                if (status.Length == 0 || !reported.Add($"{record.Id}:{status}")) continue;
                suspicious++;
                message.AppendLine($"{record.Label} / {record.Color}: {status}\nOrigin: {record.Origin}\nLast known: {record.LastKnownLocation} / {record.LastKnownOwner}\nLast transition: {record.Last}\nActual: {record.ObservedOwners}");
            }
            foreach (string issue in inventoryIssues.Concat(LastScanIssues))
                if (reported.Add(issue)) message.AppendLine(issue);
            if (message.Length > 0)
                Debug.LogWarning($"[MarbleTracker] Level {Level} (content {InternalLevel}) / Attempt {Attempt} / {checkpoint}\n{suspicious} new suspicious marble(s).\n{message}");
        }

        public static void SessionStateChanged(GameplaySessionState state)
        {
            if (!IsActive) return;
            if (state == GameplaySessionState.Won || state == GameplaySessionState.Failed || state == GameplaySessionState.Recovering)
                Validate(state == GameplaySessionState.Recovering ? "Logical fail / before recovery" : state.ToString(), true);
        }
    }
}
#endif

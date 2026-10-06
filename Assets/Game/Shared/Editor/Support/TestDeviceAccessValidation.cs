using System;
using Game.Shared.DeveloperTools;
using UnityEditor;

namespace Game.Shared.Editor.Support
{
    // Pure data tests: no network, PlayerPrefs writes, scene changes or Play Mode.
    public static class TestDeviceAccessValidation
    {
        [MenuItem("Game/Developer Tools/Validate Remote Test Devices (Offline)")]
        public static void RunFromMenu()
        {
            UnityEngine.Debug.Log(Run());
        }

        public static string Run()
        {
            int passed = 0;
            Action<bool, string> check = (success, name) =>
            {
                if (!success) throw new InvalidOperationException("Test devices validation failed: " + name);
                passed++;
            };
            DateTime now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
            const string hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            string json = "{\"devices\":[{\"deviceName\":\"Synthetic test\",\"uidSha256\":\"" + hash + "\"}]}";
            TestDeviceAccessSnapshot snapshot;
            check(TestDeviceAccessSnapshot.HashUid("abc") == hash, "known SHA-256");
            check(TestDeviceAccessSnapshot.HashUid(" abc ") == hash, "UID whitespace");
            check(TestDeviceAccessSnapshot.HashUid("ABC") != hash, "UID case preserved");
            check(TestDeviceAccessSnapshot.HashUid(null) == string.Empty, "null UID");
            check(TestDeviceAccessSnapshot.HashUid(" ") == string.Empty, "blank UID");
            check(TestDeviceAccessSnapshot.HashUid("n/a") == string.Empty, "unsupported UID");
            check(TestDeviceAccessSnapshot.TryParseRemote(json, now, now, out snapshot), "valid remote");
            check(snapshot.ContainsHash(hash, now), "authorized hash");
            check(snapshot.ContainsHash(hash.ToUpperInvariant(), now), "hash case insensitive");
            check(!snapshot.ContainsHash(TestDeviceAccessSnapshot.HashUid("other"), now), "other UID denied");
            check(!snapshot.ContainsHash(null, now), "missing hash denied");
            check(snapshot.IsFresh(now.AddHours(23)), "fresh cache");
            check(!snapshot.IsFresh(now.AddHours(24)), "24-hour boundary");
            check(!snapshot.IsFresh(now.AddSeconds(-1)), "clock rollback denied");
            string cache = snapshot.ToCacheJson();
            TestDeviceAccessSnapshot restored;
            check(TestDeviceAccessSnapshot.TryReadCache(cache, now.AddHours(12), out restored), "cache round trip");
            check(restored.VerifiedAtUtc == now && restored.ContainsHash(hash, now.AddHours(12)), "cache timestamp not extended");
            check(!TestDeviceAccessSnapshot.TryReadCache(cache, now.AddHours(24), out restored), "expired cache rejected");
            check(!TestDeviceAccessSnapshot.TryReadCache(cache, now.AddSeconds(-1), out restored), "future cache rejected");
            check(!TestDeviceAccessSnapshot.TryReadCache("{\"schemaVersion\":99}", now, out restored), "cache version");
            check(!TestDeviceAccessSnapshot.TryReadCache("broken", now, out restored), "malformed cache");
            check(!TestDeviceAccessSnapshot.TryParseRemote(json, now.AddHours(-24), now, out restored), "old remote fetch");
            check(!TestDeviceAccessSnapshot.TryParseRemote(json, now.AddSeconds(1), now, out restored), "future remote fetch");
            check(TestDeviceAccessSnapshot.TryParseRemote("{\"devices\":[]}", now, now, out restored) &&
                  !restored.ContainsHash(hash, now), "empty list revokes access");
            foreach (string invalid in new[]
            {
                "", "null", "[]", "{}", "{\"devices\":null}", "{\"devices\":{}}",
                "{\"devices\":[null]}", "{\"devices\":[{}]}", "{\"devices\":[{\"uid\":\"abc\"}]}",
                "{\"devices\":[{\"uidSha256\":\"short\"}]}",
                "{\"devices\":[{\"uidSha256\":\"" + new string('z', 64) + "\"}]}",
                "{\"devices\":[],\"devices\":[]}", json + " {}", "{\"devices\":[}"
            })
                check(!TestDeviceAccessSnapshot.TryParseRemote(invalid, now, now, out restored), "invalid remote schema");
            // An invalid candidate must not mutate the caller's previously validated snapshot.
            check(snapshot.ContainsHash(hash, now), "last valid snapshot preserved");
            return "Remote test devices: " + passed + " offline checks passed.";
        }
    }
}

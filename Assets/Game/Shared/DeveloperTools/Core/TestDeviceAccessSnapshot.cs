using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Shared.DeveloperTools
{
    // Validated remote data, not a second permission owner. Bootstrap still owns session access.
    public sealed class TestDeviceAccessSnapshot
    {
        public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);
        private const int MaximumJsonLength = 131072;
        private const int MaximumDevices = 512;
        private readonly HashSet<string> hashes;

        public DateTime VerifiedAtUtc { get; }

        private TestDeviceAccessSnapshot(HashSet<string> hashes, DateTime verifiedAtUtc)
        {
            this.hashes = hashes;
            VerifiedAtUtc = verifiedAtUtc;
        }

        public bool IsFresh(DateTime nowUtc)
        {
            return nowUtc >= VerifiedAtUtc && nowUtc - VerifiedAtUtc < MaximumAge;
        }

        public bool ContainsHash(string hash, DateTime nowUtc)
        {
            return IsFresh(nowUtc) && !string.IsNullOrEmpty(hash) && hashes.Contains(hash);
        }

        public static string HashUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid) ||
                string.Equals(uid.Trim(), "n/a", StringComparison.OrdinalIgnoreCase)) return string.Empty;

            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(uid.Trim()));
                var result = new StringBuilder(64);
                foreach (byte value in digest) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        public static bool TryParseRemote(string json, DateTime verifiedAtUtc,
            DateTime nowUtc, out TestDeviceAccessSnapshot snapshot)
        {
            snapshot = null;
            if (verifiedAtUtc.Kind != DateTimeKind.Utc || verifiedAtUtc > nowUtc ||
                nowUtc - verifiedAtUtc >= MaximumAge || string.IsNullOrWhiteSpace(json) ||
                json.Length > MaximumJsonLength) return false;

            try
            {
                JObject root;
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text) { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read()) return false;
                }

                var devices = root["devices"] as JArray;
                if (devices == null || devices.Count > MaximumDevices) return false;
                var parsedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JToken device in devices)
                {
                    var entry = device as JObject;
                    // Reject plaintext UID schema rather than silently accepting an unusable list.
                    if (entry == null || entry["uid"] != null || entry["uidSha256"]?.Type != JTokenType.String)
                        return false;
                    string hash = ((string)entry["uidSha256"]).Trim();
                    if (hash.Length != 64) return false;
                    foreach (char c in hash)
                        if (!Uri.IsHexDigit(c)) return false;
                    parsedHashes.Add(hash.ToLowerInvariant());
                }

                snapshot = new TestDeviceAccessSnapshot(parsedHashes, verifiedAtUtc);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public string ToCacheJson()
        {
            var devices = new JArray();
            foreach (string hash in hashes) devices.Add(new JObject { ["uidSha256"] = hash });
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["verifiedAtUtcTicks"] = VerifiedAtUtc.Ticks,
                ["payload"] = new JObject { ["devices"] = devices }
            }.ToString(Formatting.None);
        }

        public static bool TryReadCache(string json, DateTime nowUtc, out TestDeviceAccessSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonLength) return false;
            try
            {
                JObject cache = JObject.Parse(json);
                if (cache["schemaVersion"]?.Type != JTokenType.Integer || (int)cache["schemaVersion"] != 1 ||
                    cache["verifiedAtUtcTicks"]?.Type != JTokenType.Integer || !(cache["payload"] is JObject))
                    return false;
                var verifiedAt = new DateTime((long)cache["verifiedAtUtcTicks"], DateTimeKind.Utc);
                return TryParseRemote(cache["payload"].ToString(Formatting.None), verifiedAt, nowUtc, out snapshot);
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException ||
                                              exception is OverflowException)
            {
                return false;
            }
        }
    }
}

using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>
    /// What config_get answered (GDD 22.5, 20.15): flags and numbers resolved for this region and
    /// cohort, the minimum client version, and whether this client must update.
    /// </summary>
    public sealed class RemoteConfigSnapshot
    {
        public static readonly RemoteConfigSnapshot Defaults =
            new RemoteConfigSnapshot(0, "local", "0.0.0", false, new Dictionary<string, object>());

        public RemoteConfigSnapshot(long version, string region, string minClientVersion, bool updateRequired, IReadOnlyDictionary<string, object> flags)
        {
            Version = version;
            Region = region;
            MinClientVersion = minClientVersion;
            UpdateRequired = updateRequired;
            Flags = flags;
        }

        public long Version { get; }

        public string Region { get; }

        public string MinClientVersion { get; }

        public bool UpdateRequired { get; }

        public IReadOnlyDictionary<string, object> Flags { get; }

        public bool Bool(string name, bool fallback) =>
            Flags.TryGetValue(name, out var value) && value is bool b ? b : fallback;

        public double Number(string name, double fallback) =>
            Flags.TryGetValue(name, out var value) && value is double d ? d : fallback;

        public string Text(string name, string fallback) =>
            Flags.TryGetValue(name, out var value) && value is string s ? s : fallback;
    }

    public interface IRemoteConfigService
    {
        Awaitable<Result<RemoteConfigSnapshot>> FetchAsync(string clientVersion, string cohort, CancellationToken cancellation);
    }
}

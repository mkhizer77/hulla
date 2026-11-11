using System;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// Where the client connects. The local defaults match server/nakama/compose.yml. A development
    /// build can point at another server with <c>-hulla-server address</c> (and <c>-hulla-server-key</c>)
    /// or the server field in Settings, remembered in player prefs; a development build starts at the
    /// server it was built for (BuildCommands: HULLA_DEV_SERVER or HullaServer.local.json, else this Mac).
    /// A release build always uses the server it was built for and takes no overrides.
    /// An address is <c>host[:port]</c> for plain http on 7350, or <c>https://host[:port]</c> on 443,
    /// as for the phase 0 box (docs/runbooks/gcp-phase0.md).
    /// </summary>
    public sealed class AppConfig
    {
        public const string ServerPrefKey = "hulla.server";
        public const string ServerKeyPrefKey = "hulla.server.key";
        public const string LocalServerKey = "local-only-server-key";
        private const string DevServerResource = "hulla-dev-server";
        private const int DefaultPort = 7350;
        private const int HttpsPort = 443;

        public AppConfig(string scheme, string host, int port, string serverKey)
        {
            Scheme = scheme;
            Host = host;
            Port = port;
            ServerKey = serverKey;
        }

        public string Scheme { get; }

        public string Host { get; }

        public int Port { get; }

        public string ServerKey { get; }

        /// <summary>Always with the port, so logs show exactly where the client connects.</summary>
        public string HostAndPort => Host + ":" + Port;

        /// <summary>The address as the server field shows it and <see cref="WithServer"/> reads it back.</summary>
        public string Address => Scheme == "https"
            ? "https://" + Host + (Port == HttpsPort ? "" : ":" + Port)
            : HostAndPort;

        public static AppConfig Local() => new AppConfig("http", "127.0.0.1", DefaultPort, LocalServerKey);

        public static AppConfig FromEnvironment()
        {
            if (!Debug.isDebugBuild)
            {
                var release = Built();
                return release == null ? Local() : WithServer(release.Value.Address, release.Value.Key);
            }

            var args = Environment.GetCommandLineArgs();
            string? server = null;
            string? key = null;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-hulla-server")
                {
                    server = args[i + 1];
                }
                else if (args[i] == "-hulla-server-key")
                {
                    key = args[i + 1];
                }
            }

            if (server != null)
            {
                return WithServer(server, key);
            }

            var stored = PlayerPrefs.GetString(ServerPrefKey, "");
            if (stored.Length > 0)
            {
                var storedKey = PlayerPrefs.GetString(ServerKeyPrefKey, "");
                return WithServer(stored, storedKey.Length > 0 ? storedKey : null);
            }

            var built = Built();
            return built == null ? Local() : WithServer(built.Value.Address, built.Value.Key);
        }

        /// <summary>
        /// Reads an address. Without a key, the key the build carries for that server is used when the
        /// address matches it, else the local development key.
        /// </summary>
        public static AppConfig WithServer(string address, string? serverKey = null)
        {
            var text = address.Trim();
            var scheme = "http";
            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https";
                text = text.Substring("https://".Length);
            }
            else if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring("http://".Length);
            }

            text = text.TrimEnd('/');
            var host = text;
            var port = scheme == "https" ? HttpsPort : DefaultPort;
            var colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out var parsed))
            {
                host = text.Substring(0, colon);
                port = parsed;
            }

            if (host.Length == 0)
            {
                host = Local().Host;
            }

            var key = string.IsNullOrEmpty(serverKey) ? KnownKey(scheme, host, port) : serverKey!;
            return new AppConfig(scheme, host, port, key);
        }

        /// <summary>The build's own server key when the address is the build's server.</summary>
        private static string KnownKey(string scheme, string host, int port)
        {
            var built = Built();
            if (built?.Key == null)
            {
                return LocalServerKey;
            }

            var target = WithServer(built.Value.Address, built.Value.Key);
            return target.Scheme == scheme && target.Port == port && string.Equals(target.Host, host, StringComparison.OrdinalIgnoreCase)
                ? built.Value.Key
                : LocalServerKey;
        }

        /// <summary>
        /// The server the build was made for: the address on the first line of the resource and, for a
        /// deployed server, its key on the second. Written by BuildCommands for the build only.
        /// </summary>
        private static (string Address, string? Key)? Built()
        {
            var text = Resources.Load<TextAsset>(DevServerResource)?.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var lines = text!.Split('\n');
            var address = lines[0].Trim();
            var key = lines.Length > 1 ? lines[1].Trim() : "";
            return address.Length == 0 ? null : (address, key.Length > 0 ? key : null);
        }
    }
}

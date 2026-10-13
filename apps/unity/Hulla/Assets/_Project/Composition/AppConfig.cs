using System;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// Where the client connects. The local defaults match server/nakama/compose.yml. A development
    /// build can point at another machine with <c>-hulla-server host[:port]</c> or the server field on
    /// the home screen, remembered in player prefs; a development APK starts at the machine that built it.
    /// </summary>
    public sealed class AppConfig
    {
        public const string ServerPrefKey = "hulla.server";
        private const string DevServerResource = "hulla-dev-server";
        private const int DefaultPort = 7350;

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

        /// <summary>Always with the port, so the server field shows exactly where the client connects.</summary>
        public string HostAndPort => Host + ":" + Port;

        public static AppConfig Local() => new AppConfig("http", "127.0.0.1", DefaultPort, "local-only-server-key");

        public static AppConfig FromEnvironment()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-hulla-server")
                {
                    return WithServer(args[i + 1]);
                }
            }

            var stored = PlayerPrefs.GetString(ServerPrefKey, "");
            if (stored.Length > 0)
            {
                return WithServer(stored);
            }

            // A development APK carries the address of the machine that built it.
            var built = Debug.isDebugBuild ? Resources.Load<TextAsset>(DevServerResource)?.text.Trim() : null;
            return string.IsNullOrEmpty(built) ? Local() : WithServer(built!);
        }

        public static AppConfig WithServer(string hostAndPort)
        {
            var local = Local();
            var text = hostAndPort.Trim();
            var colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out var port))
            {
                return new AppConfig(local.Scheme, text.Substring(0, colon), port, local.ServerKey);
            }

            return new AppConfig(local.Scheme, text.Length > 0 ? text : local.Host, DefaultPort, local.ServerKey);
        }
    }
}

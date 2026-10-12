namespace Hulla.Composition
{
    /// <summary>Where the client connects; the local defaults match server/nakama/compose.yml.</summary>
    public sealed class AppConfig
    {
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

        public static AppConfig Local() => new AppConfig("http", "127.0.0.1", 7350, "local-only-server-key");
    }
}

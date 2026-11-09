using System;

namespace Hulla.Integration.Tests
{
    /// <summary>
    /// The server the integration tests talk to. HULLA_NAKAMA_HOST (and HULLA_NAKAMA_PORT) turn them on;
    /// the local compose server needs nothing else. A deployed box also needs HULLA_NAKAMA_SCHEME=https,
    /// HULLA_NAKAMA_SERVER_KEY and HULLA_NAKAMA_HTTP_KEY (docs/runbooks/gcp-phase0.md).
    /// </summary>
    internal static class IntegrationServer
    {
        public static string Scheme => Env("HULLA_NAKAMA_SCHEME", "http");

        public static string ServerKey => Env("HULLA_NAKAMA_SERVER_KEY", "local-only-server-key");

        public static string HttpKey => Env("HULLA_NAKAMA_HTTP_KEY", "local-only-http-key");

        private static string Env(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value!;
        }
    }
}

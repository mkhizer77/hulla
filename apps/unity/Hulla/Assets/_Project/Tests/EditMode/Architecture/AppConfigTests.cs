using Hulla.Composition;
using NUnit.Framework;

namespace Hulla.Architecture.Tests
{
    /// <summary>Server addresses for development builds: the local compose server and the phase 0 box.</summary>
    public sealed class AppConfigTests
    {
        [Test]
        public void A_bare_address_is_plain_http_on_the_nakama_port()
        {
            var config = AppConfig.WithServer("192.168.0.129");

            Assert.AreEqual("http", config.Scheme);
            Assert.AreEqual("192.168.0.129", config.Host);
            Assert.AreEqual(7350, config.Port);
            Assert.AreEqual(AppConfig.LocalServerKey, config.ServerKey);
            Assert.AreEqual("192.168.0.129:7350", config.Address);
        }

        [Test]
        public void An_https_address_uses_443_and_keeps_its_scheme()
        {
            var config = AppConfig.WithServer(" https://api.hulla.example.test/ ", "box-key");

            Assert.AreEqual("https", config.Scheme);
            Assert.AreEqual("api.hulla.example.test", config.Host);
            Assert.AreEqual(443, config.Port);
            Assert.AreEqual("box-key", config.ServerKey);
            Assert.AreEqual("https://api.hulla.example.test", config.Address);
        }

        [Test]
        public void An_explicit_port_survives_a_round_trip_through_the_address()
        {
            var https = AppConfig.WithServer("https://box.example.test:8443", "k");
            var http = AppConfig.WithServer("http://127.0.0.1:7351");

            Assert.AreEqual(8443, https.Port);
            Assert.AreEqual("https://box.example.test:8443", https.Address);
            Assert.AreEqual(https.Address, AppConfig.WithServer(https.Address, "k").Address);
            Assert.AreEqual("http", http.Scheme);
            Assert.AreEqual(7351, http.Port);
            Assert.AreEqual("127.0.0.1:7351", http.Address);
        }

        [Test]
        public void An_empty_address_falls_back_to_this_machine()
        {
            Assert.AreEqual(AppConfig.Local().Address, AppConfig.WithServer("  ").Address);
        }
    }
}

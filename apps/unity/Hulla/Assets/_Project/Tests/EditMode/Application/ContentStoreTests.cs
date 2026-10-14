using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Hulla.Infrastructure.Content;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    /// <summary>Signed content (ADR-0011, GDD 18.9): signatures, hashes, pinning per launch and the offline fallback.</summary>
    public sealed class ContentStoreTests
    {
        // Made with Node's crypto, as `pnpm content:sign` signs: proves the key formats and the padding match.
        private const string NodeSpki = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA56/LlAo/ZBn4rkujr2UH
D6Me2HwRtTzer8Qq3Fp+KkYClGApF7mOZTFa9qK0A52luRqtK2PZNae2Sjj+1J3d
YRKP8ZBnHjj4SSGK5XqUCA+ypR/8HAiVBEpNxKBBhJUzGP00mZ6sqGMve1cZsLOH
lQBIO2lFRftmKIP4raeUKg/kh50VyA0o2ODrIyeEr563qDcNBT3xmXpv/VIufXft
IA2TPek/nuYoIce+faI8bX+i7e3pcN5FNBPpjs6vrfE1Dc6dePuHTq6iPovsAQuf
UYFj7oEH8bO1t7DGrbOl2AjLVxsP8Dj0bUHFPsrtUHtghaQcB0+BNl3Osa4dLBzQ
HQIDAQAB
-----END PUBLIC KEY-----";

        private const string NodePkcs1 = @"-----BEGIN RSA PUBLIC KEY-----
MIIBCgKCAQEA56/LlAo/ZBn4rkujr2UHD6Me2HwRtTzer8Qq3Fp+KkYClGApF7mO
ZTFa9qK0A52luRqtK2PZNae2Sjj+1J3dYRKP8ZBnHjj4SSGK5XqUCA+ypR/8HAiV
BEpNxKBBhJUzGP00mZ6sqGMve1cZsLOHlQBIO2lFRftmKIP4raeUKg/kh50VyA0o
2ODrIyeEr563qDcNBT3xmXpv/VIufXftIA2TPek/nuYoIce+faI8bX+i7e3pcN5F
NBPpjs6vrfE1Dc6dePuHTq6iPovsAQufUYFj7oEH8bO1t7DGrbOl2AjLVxsP8Dj0
bUHFPsrtUHtghaQcB0+BNl3Osa4dLBzQHQIDAQAB
-----END RSA PUBLIC KEY-----";

        private const string NodeBytes = @"{""id"":""content-manifest"",""version"":7,""digest"":""abababababababababababababababababababababababababababababababab"",""files"":[{""path"":""emotes/v1.json"",""type"":""emotes"",""id"":""v1"",""version"":1,""sha256"":""cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd"",""bytes"":12}]}";

        private const string NodeSignature = "O8tdVUFJ1REEj73klrdr4Ff4crT2E/LYBVaM4f+e2e5FyuE7ya9j5MrtyqhHiDQclg+mIHb2I4wkp+51D1a1R6T13TW27av8UGJ9I1cT+RrRd97MXHZbmQEO8ALWzJ6FMI7Iz8HCexliJkVPaGXc8SZyIT7J1MC7JnOHmpI92uhQVhU+rK4TOdGGnubIDzsvMMw6NzhwDjLSWOCfq3aS+UprmJMjwH25TEV5PDctEFXApLAR41oXuV4j/KvRbtpq9SUC95F0MIGbnNPbrekt9kv14xyvHY380Kq8o0+Ms6qAcnkutzlOfSvKZNf2pL+s8ldu/eqiQ0QlJPjv4C+VBA==";

        private const string Emotes = "{\"id\":\"v1\",\"version\":1,\"emotes\":[]}";
        private const string Presets = "{\"id\":\"standard\",\"version\":1}";

        private string _cache = "";
        private RSA _key = null!;

        [SetUp]
        public void SetUp()
        {
            _cache = Path.Combine(Path.GetTempPath(), "hulla-content-" + System.Guid.NewGuid().ToString("N"));
            _key = RSA.Create();
            _key.KeySize = 2048;
        }

        [TearDown]
        public void TearDown()
        {
            _key.Dispose();
            if (Directory.Exists(_cache))
            {
                Directory.Delete(_cache, true);
            }
        }

        [Test]
        public void ASignatureFromTheSigningToolVerifiesWithEitherKeyFormat()
        {
            foreach (var pem in new[] { NodeSpki, NodePkcs1 })
            {
                var key = ManifestSignature.ParsePublicKey(pem);
                Assert.IsTrue(key.HasValue, pem.Substring(0, 30));
                Assert.IsTrue(ManifestSignature.Verify(NodeBytes, NodeSignature, key!.Value));
                Assert.IsFalse(ManifestSignature.Verify(NodeBytes.Replace("\"version\":7", "\"version\":8"), NodeSignature, key.Value), "changed bytes");
                Assert.IsFalse(ManifestSignature.Verify(NodeBytes, null, key.Value), "no signature");
                Assert.IsFalse(ManifestSignature.Verify(NodeBytes, "bm90IGEgc2lnbmF0dXJl", key.Value), "another signature");
            }

            Assert.IsFalse(ManifestSignature.ParsePublicKey("").HasValue);
            Assert.IsFalse(ManifestSignature.ParsePublicKey("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----").HasValue);
        }

        [Test]
        public void ALaunchReadsTheBundledContentUntilNewerContentIsSynced()
        {
            var store = Store(new FakeApi());
            Run(store.OpenAsync(CancellationToken.None));
            Assert.AreEqual(1, store.Version);
            Assert.AreEqual(Emotes, Run(store.ReadTextAsync("emotes/v1.json", CancellationToken.None)));
            Assert.IsNull(Run(store.ReadTextAsync("missing/v1.json", CancellationToken.None)), "only listed files are read");
        }

        [Test]
        public void SignedNewerContentDownloadsOnlyWhatChangedAndIsPinnedFromTheNextLaunch()
        {
            var newer = Emotes.Replace("[]", "[{\"id\":\"wave\"}]");
            var api = new FakeApi();
            api.Serve(2, ("emotes/v1.json", newer), ("presets/thulla/standard.json", Presets));
            api.Sign(_key);
            var store = Store(api);
            Run(store.OpenAsync(CancellationToken.None));
            Assert.AreEqual(ContentSyncOutcome.Downloaded, Run(store.SyncAsync(CancellationToken.None)));
            CollectionAssert.AreEqual(new[] { "emotes/v1.json" }, api.Fetched, "an unchanged file stays bundled");
            Assert.AreEqual(1, store.Version, "this launch keeps the version it opened with");
            Assert.AreEqual(Emotes, Run(store.ReadTextAsync("emotes/v1.json", CancellationToken.None)));
            Assert.AreEqual(ContentSyncOutcome.UpToDate, Run(store.SyncAsync(CancellationToken.None)));

            var next = Store(new FakeApi());
            Run(next.OpenAsync(CancellationToken.None));
            Assert.AreEqual(2, next.Version);
            Assert.AreEqual(newer, Run(next.ReadTextAsync("emotes/v1.json", CancellationToken.None)));
            Assert.AreEqual(Presets, Run(next.ReadTextAsync("presets/thulla/standard.json", CancellationToken.None)));
        }

        [Test]
        public void ATamperedManifestIsRejectedAndNothingIsCached()
        {
            var api = new FakeApi();
            api.Serve(2, ("emotes/v1.json", Emotes.Replace("[]", "[{\"id\":\"wave\"}]")), ("presets/thulla/standard.json", Presets));
            api.Sign(_key);
            api.Tamper();
            var store = Store(api);
            Run(store.OpenAsync(CancellationToken.None));
            Assert.AreEqual(ContentSyncOutcome.Rejected, Run(store.SyncAsync(CancellationToken.None)));
            Assert.IsEmpty(api.Fetched);

            var next = Store(new FakeApi());
            Run(next.OpenAsync(CancellationToken.None));
            Assert.AreEqual(1, next.Version);
        }

        [Test]
        public void AFileThatDoesNotMatchItsHashIsRejected()
        {
            var api = new FakeApi();
            api.Serve(2, ("emotes/v1.json", Emotes.Replace("[]", "[{\"id\":\"wave\"}]")), ("presets/thulla/standard.json", Presets));
            api.Sign(_key);
            api.Files["emotes/v1.json"] = Emotes.Replace("[]", "[{\"id\":\"rude\"}]");
            var store = Store(api);
            Run(store.OpenAsync(CancellationToken.None));
            Assert.AreEqual(ContentSyncOutcome.Rejected, Run(store.SyncAsync(CancellationToken.None)));

            var next = Store(new FakeApi());
            Run(next.OpenAsync(CancellationToken.None));
            Assert.AreEqual(1, next.Version, "half a set is never pinned");
            Assert.AreEqual(Emotes, Run(next.ReadTextAsync("emotes/v1.json", CancellationToken.None)));
        }

        [Test]
        public void AnUnsignedManifestPassesOnlyInADevelopmentBuild()
        {
            var api = new FakeApi();
            api.Serve(2, ("emotes/v1.json", Emotes.Replace("[]", "[{\"id\":\"wave\"}]")), ("presets/thulla/standard.json", Presets));
            var release = Store(api, acceptUnsigned: false);
            Run(release.OpenAsync(CancellationToken.None));
            Assert.AreEqual(ContentSyncOutcome.Rejected, Run(release.SyncAsync(CancellationToken.None)));

            var development = Store(api, acceptUnsigned: true);
            Run(development.OpenAsync(CancellationToken.None));
            Assert.AreEqual(ContentSyncOutcome.Downloaded, Run(development.SyncAsync(CancellationToken.None)));

            var nextRelease = Store(new FakeApi(), acceptUnsigned: false);
            Run(nextRelease.OpenAsync(CancellationToken.None));
            Assert.AreEqual(1, nextRelease.Version, "a release build ignores unsigned content a development build cached");
        }

        [Test]
        public void OfflineALaunchKeepsTheCacheAndADamagedCacheFallsBackToTheBundle()
        {
            var api = new FakeApi();
            api.Serve(2, ("emotes/v1.json", Emotes.Replace("[]", "[{\"id\":\"wave\"}]")), ("presets/thulla/standard.json", Presets));
            api.Sign(_key);
            var store = Store(api);
            Run(store.OpenAsync(CancellationToken.None));
            Run(store.SyncAsync(CancellationToken.None));

            var offline = Store(new FakeApi { Down = true });
            Run(offline.OpenAsync(CancellationToken.None));
            Assert.AreEqual(ContentSyncOutcome.Unavailable, Run(offline.SyncAsync(CancellationToken.None)));
            Assert.AreEqual(2, offline.Version);

            foreach (var file in Directory.GetFiles(Path.Combine(_cache, "files")))
            {
                File.WriteAllText(file, "{}");
            }

            var damaged = Store(new FakeApi());
            Run(damaged.OpenAsync(CancellationToken.None));
            Assert.AreEqual(1, damaged.Version);
            Assert.AreEqual(Emotes, Run(damaged.ReadTextAsync("emotes/v1.json", CancellationToken.None)));
        }

        private ContentStore Store(FakeApi api, bool acceptUnsigned = false)
        {
            var bundled = new FakeBundle();
            bundled.Files["emotes/v1.json"] = Emotes;
            bundled.Files["presets/thulla/standard.json"] = Presets;
            bundled.Files["manifest.json"] = ManifestJson(1, bundled.Files);
            return new ContentStore(bundled, api, _cache, _key.ExportParameters(false), acceptUnsigned);
        }

        private static string ManifestJson(long version, IDictionary<string, string> files)
        {
            var list = new List<string>();
            foreach (var pair in files)
            {
                if (pair.Key != "manifest.json")
                {
                    list.Add($"{{\"path\":\"{pair.Key}\",\"sha256\":\"{ContentStore.Sha256Hex(pair.Value)}\",\"bytes\":{Encoding.UTF8.GetByteCount(pair.Value)}}}");
                }
            }

            return $"{{\"id\":\"content-manifest\",\"version\":{version},\"digest\":\"d\",\"files\":[{string.Join(",", list)}]}}";
        }

        private static T Run<T>(Awaitable<T> awaitable)
        {
            var awaiter = awaitable.GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, "the fakes answer at once");
            return awaiter.GetResult();
        }

        private static void Run(Awaitable awaitable)
        {
            var awaiter = awaitable.GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, "the fakes answer at once");
            awaiter.GetResult();
        }

        private static Awaitable<T> Done<T>(T value)
        {
            var done = new AwaitableCompletionSource<T>();
            done.SetResult(value);
            return done.Awaitable;
        }

        private sealed class FakeBundle : IBundledContent
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();

            public Awaitable<string?> ReadAsync(string path, CancellationToken cancellation) =>
                Done<string?>(Files.TryGetValue(path, out var text) ? text : null);
        }

        private sealed class FakeApi : IContentApi
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public readonly List<string> Fetched = new List<string>();
            public bool Down;
            private string _bytes = "";
            private string? _signature;

            public void Serve(long version, params (string Path, string Text)[] files)
            {
                foreach (var (path, text) in files)
                {
                    Files[path] = text;
                }

                _bytes = ManifestJson(version, Files);
            }

            public void Sign(RSA key) =>
                _signature = System.Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(_bytes), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            public void Tamper() => _bytes = _bytes.Replace("\"version\":2", "\"version\":3");

            public Awaitable<Result<SignedManifest>> ManifestAsync(CancellationToken cancellation) =>
                Done(Down || _bytes == "" ? Result<SignedManifest>.Fail("content.unavailable") : Result<SignedManifest>.Ok(new SignedManifest(_bytes, _signature, "k1")));

            public Awaitable<Result<string>> FileAsync(string path, CancellationToken cancellation)
            {
                Fetched.Add(path);
                return Done(Files.TryGetValue(path, out var text) ? Result<string>.Ok(text) : Result<string>.Fail("content.unavailable"));
            }
        }
    }
}

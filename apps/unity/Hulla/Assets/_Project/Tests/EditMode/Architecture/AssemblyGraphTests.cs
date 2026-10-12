using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Architecture.Tests
{
    /// <summary>The asmdef graph matches ARCHITECTURE 9.1; adding a forbidden reference fails here.</summary>
    public sealed class AssemblyGraphTests
    {
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            ["Hulla.Domain"] = new string[0],
            ["Hulla.Application"] = new[] { "Hulla.Domain", "Hulla.Protocol" },
            ["Hulla.Infrastructure"] = new[] { "Hulla.Application", "Hulla.Domain", "Hulla.Protocol" },
            ["Hulla.Presentation"] = new[] { "Hulla.Application", "Hulla.Domain" },
            ["Hulla.Games.Thulla"] = new[] { "Hulla.Application", "Hulla.Domain", "Hulla.Presentation" },
            ["Hulla.Composition"] = new[]
            {
                "Hulla.Application", "Hulla.Domain", "Hulla.Protocol", "Hulla.Infrastructure",
                "Hulla.Presentation", "Hulla.Games.Thulla",
            },
        };

        /// <summary>Only the adapter and view layers may reference SDKs; Domain and Application stay plain.</summary>
        private static readonly HashSet<string> ThirdPartyAllowed = new HashSet<string>
        {
            "Hulla.Infrastructure", "Hulla.Presentation", "Hulla.Games.Thulla", "Hulla.Composition",
        };

        private static Dictionary<string, AsmDef> LoadProjectAssemblies()
        {
            var root = Path.Combine(Application.dataPath, "_Project");
            return Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Tests/"))
                .Select(path => JsonUtility.FromJson<AsmDef>(File.ReadAllText(path)))
                .ToDictionary(a => a.name, a => a);
        }

        [Test]
        public void EveryLayerAssemblyExistsAndNoOtherOne()
        {
            CollectionAssert.AreEquivalent(Allowed.Keys, LoadProjectAssemblies().Keys);
        }

        [Test]
        public void EachAssemblyReferencesOnlyWhatSection91Allows()
        {
            foreach (var asm in LoadProjectAssemblies().Values)
            {
                var forbidden = asm.references
                    .Where(r => r.StartsWith("Hulla.") || !ThirdPartyAllowed.Contains(asm.name))
                    .Except(Allowed[asm.name])
                    .ToArray();
                Assert.IsEmpty(forbidden, $"{asm.name} references {string.Join(", ", forbidden)}");
            }
        }

        [Test]
        public void DomainHasNoEngineReferences()
        {
            Assert.IsTrue(LoadProjectAssemblies()["Hulla.Domain"].noEngineReferences);
        }

        [Test]
        public void ApplicationMayNotReferenceAnSdk()
        {
            Assert.IsFalse(ThirdPartyAllowed.Contains("Hulla.Application"));
            Assert.IsFalse(ThirdPartyAllowed.Contains("Hulla.Domain"));
        }

        [Test]
        public void ForbiddenReferenceIsDetected()
        {
            var presentation = new AsmDef { name = "Hulla.Presentation", references = new[] { "Hulla.Infrastructure" } };
            Assert.IsNotEmpty(presentation.references.Except(Allowed[presentation.name]));
        }

        [System.Serializable]
        private sealed class AsmDef
        {
            public string name = "";
            public string[] references = new string[0];
            public bool noEngineReferences;
        }
    }
}

using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Architecture.Tests
{
    /// <summary>
    /// IL2CPP strips what it cannot see used, and Newtonsoft reaches the protocol types only through
    /// reflection, so Assets/link.xml must keep that assembly whole or messages lose their fields on
    /// devices while the editor and the Mac build work.
    /// </summary>
    public sealed class StrippingTests
    {
        [Test]
        public void LinkXmlKeepsTheProtocolTypes()
        {
            var path = Path.Combine(UnityEngine.Application.dataPath, "link.xml");
            Assert.IsTrue(File.Exists(path), "Assets/link.xml exists");
            var kept = XDocument.Load(path).Descendants("assembly")
                .Any(a => (string?)a.Attribute("fullname") == "Hulla.Protocol" && (string?)a.Attribute("preserve") == "all");
            Assert.IsTrue(kept, "link.xml preserves Hulla.Protocol in full");
        }
    }
}

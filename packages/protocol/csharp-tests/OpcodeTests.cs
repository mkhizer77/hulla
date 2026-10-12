using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hulla.Protocol.Tests;

/// <summary>The generated C# opcode registry agrees with opcodes.json in both directions.</summary>
public sealed class OpcodeTests
{
    private static JArray LoadRegistry()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "packages", "protocol", "schemas", "opcodes.json");
            if (File.Exists(path))
            {
                return (JArray)JObject.Parse(File.ReadAllText(path))["messages"]!;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("packages/protocol/schemas/opcodes.json not found");
    }

    [Test]
    public void EveryRegisteredTypeMapsToItsOpcodeAndBack()
    {
        foreach (var entry in LoadRegistry())
        {
            var type = (string)entry["type"]!;
            var opcode = (long)entry["opcode"]!;
            Assert.That(MessageTypes.OpcodeFor(type), Is.EqualTo(opcode), type);
            Assert.That(MessageTypes.ForOpcode(opcode), Is.EqualTo(type), type);
            Assert.That(MessageTypes.IsPublic(type), Is.EqualTo((string)entry["visibility"]! == "public"), type);
        }
    }

    [Test]
    public void TheSharedSettingsReadWireEnums()
    {
        var state = Newtonsoft.Json.JsonConvert.DeserializeObject<Seat>("{\"seat\":1,\"playerId\":null,\"displayName\":null,\"ready\":false,\"connected\":true,\"autoPlay\":false}", ProtocolJson.Settings);
        Assert.That(state!.SeatSeat, Is.EqualTo(1));
        var closed = Newtonsoft.Json.JsonConvert.DeserializeObject<TableClosed>("{\"reason\":\"host_closed\"}", ProtocolJson.Settings);
        Assert.That(closed!.Reason, Is.EqualTo(TableClosedPayloadReason.HostClosed));
    }

    [Test]
    public void UnknownValuesAreRejected()
    {
        Assert.That(MessageTypes.ForOpcode(9999), Is.Null);
        Assert.That(MessageTypes.OpcodeFor("no_such_type"), Is.EqualTo(-1));
    }
}

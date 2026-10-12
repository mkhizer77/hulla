using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hulla.Protocol.Tests;

/// <summary>Every example message deserialises into its generated DTO and serialises back to the same canonical JSON.</summary>
public sealed class RoundTripTests
{
    private static readonly string ExamplesDir = FindExamplesDir();

    /// <summary>Walks up from the test binary until the repository root, wherever the build output lives.</summary>
    private static string FindExamplesDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "packages", "protocol", "examples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("packages/protocol/examples not found above " + TestContext.CurrentContext.TestDirectory);
    }

    private static readonly IReadOnlyDictionary<string, Type> PayloadTypes = new Dictionary<string, Type>
    {
        ["play_card"] = typeof(PlayCard),
        ["move_applied"] = typeof(MoveApplied),
        ["trick_resolved"] = typeof(TrickResolved),
        ["private_draw"] = typeof(PrivateDraw),
        ["turn_start"] = typeof(TurnStart),
        ["round_end"] = typeof(RoundEnd),
        ["round_start"] = typeof(RoundStart),
        ["join_table"] = typeof(JoinTable),
        ["table_state"] = typeof(TableState),
        ["move_rejected"] = typeof(MoveRejected),
        ["quick_chat"] = typeof(QuickChat),
        ["confirmed"] = typeof(Confirmed),
        ["presence"] = typeof(Presence),
    };

    public static IEnumerable<string> ExampleFiles() => Directory.GetFiles(ExamplesDir, "*.json").Order();

    [TestCaseSource(nameof(ExampleFiles))]
    public void Example_round_trips_through_its_DTO(string file)
    {
        var message = JObject.Parse(File.ReadAllText(file));
        var type = message["type"]!.Value<string>()!;
        Assert.That(PayloadTypes.ContainsKey(type), $"no DTO mapped for {type}");

        var payloadJson = message["payload"]!.ToString(Formatting.None);
        var dto = JsonConvert.DeserializeObject(payloadJson, PayloadTypes[type], Converter.Settings);
        Assert.That(dto, Is.Not.Null);
        var back = JsonConvert.SerializeObject(dto, Converter.Settings);

        Assert.That(Canonical(JToken.Parse(back)), Is.EqualTo(Canonical(message["payload"]!)), Path.GetFileName(file));

        var envelope = JsonConvert.DeserializeObject<MessageEnvelope>(message.ToString(Formatting.None), Converter.Settings);
        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Type, Is.EqualTo(type));
        Assert.That(envelope.V, Is.EqualTo(1));
    }

    [Test]
    public void Every_example_type_has_a_mapped_DTO_and_the_folder_is_not_empty()
    {
        var files = ExampleFiles().ToList();
        Assert.That(files, Has.Count.GreaterThanOrEqualTo(10));
    }

    /// <summary>Sorted keys, no whitespace: the same canonical form the engines hash.</summary>
    private static string Canonical(JToken token)
    {
        return Sort(token).ToString(Formatting.None);
    }

    private static JToken Sort(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                {
                    var sorted = new JObject();
                    foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        sorted.Add(property.Name, Sort(property.Value));
                    }
                    return sorted;
                }
            case JArray array:
                return new JArray(array.Select(Sort));
            default:
                return token;
        }
    }
}

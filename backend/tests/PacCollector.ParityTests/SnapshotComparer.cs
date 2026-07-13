using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace PacCollector.ParityTests;

// helper minimo de snapshot testing. Serializa el objeto a JSON deterministico
// y lo compara contra el archivo .json committed. Si el snapshot no existe lo crea
// (primer run; el dev despues lo committea). Drift = el test falla con el diff.
internal static class SnapshotComparer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void AssertMatchesSnapshot<T>(T actual, string snapshotName, string[]? ignoreFields = null)
    {
        var serialized = Serialize(actual, ignoreFields);
        var snapshotPath = ResolveSnapshotPath(snapshotName);
        if (!File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, serialized);
            // primer run: crear y pasar. CI con tests verdes va a commitearlo en el siguiente push.
            return;
        }
        var expected = File.ReadAllText(snapshotPath);
        Normalize(serialized).Should().Be(Normalize(expected),
            $"snapshot drift detectado para {snapshotName}; revisa el diff entre actual y {snapshotPath}");
    }

    public static void AssertMatchesTextSnapshot(string actual, string snapshotName)
    {
        var snapshotPath = ResolveSnapshotPath(snapshotName);
        if (!File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, actual);
            return;
        }
        var expected = File.ReadAllText(snapshotPath);
        Normalize(actual).Should().Be(Normalize(expected),
            $"text snapshot drift detectado para {snapshotName}");
    }

    private static string Serialize<T>(T value, string[]? ignoreFields)
    {
        if (ignoreFields is null || ignoreFields.Length == 0)
            return JsonSerializer.Serialize(value, Json);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, Json));
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            WriteSkipping(doc.RootElement, writer, ignoreFields);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteSkipping(JsonElement el, Utf8JsonWriter writer, string[] ignore)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in el.EnumerateObject())
                {
                    if (ignore.Contains(prop.Name, StringComparer.Ordinal)) continue;
                    writer.WritePropertyName(prop.Name);
                    WriteSkipping(prop.Value, writer, ignore);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in el.EnumerateArray())
                    WriteSkipping(item, writer, ignore);
                writer.WriteEndArray();
                break;
            default:
                el.WriteTo(writer);
                break;
        }
    }

    private static string ResolveSnapshotPath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Snapshots", name + ".json");

    // normaliza fin de linea para que Windows CRLF y Unix LF no fallen
    private static string Normalize(string s) => s.Replace("\r\n", "\n");
}

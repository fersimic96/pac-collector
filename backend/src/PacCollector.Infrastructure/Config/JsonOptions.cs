using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacCollector.Infrastructure.Config;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = Build();
    public static readonly JsonSerializerOptions Pretty = Build(writeIndented: true);

    private static JsonSerializerOptions Build(bool writeIndented = false)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = writeIndented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return o;
    }
}

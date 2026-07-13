using System.Text.Json.Serialization;

namespace PacCollector.Infrastructure.Config;

[JsonConverter(typeof(JsonStringEnumConverter<HotFolderFormat>))]
public enum HotFolderFormat
{
    LimsEthernet,
    CsvAll,
    Csv,
}

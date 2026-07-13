using PacCollector.Domain.Entities;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.ParityTests;

// gate de paridad para el print plugin: parsea las 3 fixtures binarias del equipo
// y compara contra el Sample serializado en disco. El Uuid se ignora (varia por run).
//
// Para regenerar: borrar Snapshots/*.json y re-correr (los tests crean el snapshot
// en el primer run y el dev lo committea). Cualquier cambio que altere la salida del
// parser hace fallar el snapshot y obliga a revisar el diff antes de mergear.
public class PrintPluginParityTests
{
    private static readonly DateTimeOffset FrozenTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Theory]
    [InlineData("optifzp-print-builtin", "optifzp_print_8076.bin")]
    [InlineData("opticpp-print-builtin", "opticpp_print_8035.bin")]
    [InlineData("optipmd-print-builtin", "optipmd_print_1216.bin")]
    [InlineData("optidist2-print-builtin", "optidist2_print_215003.bin")]
    public void ParseFixture_MatchesSnapshot(string specId, string fixtureName)
    {
        var spec = PrintPluginSpecLoader.LoadAll().Single(s => s.Id == specId);
        var plugin = new ConfigurablePrintPlugin(spec);
        var raw = LoadFixture(fixtureName);

        var sample = plugin.ParsePrintMessage(raw, sourceIp: "192.168.1.50", FrozenTime);

        SnapshotComparer.AssertMatchesSnapshot(
            ToSnapshot(sample),
            snapshotName: Path.GetFileNameWithoutExtension(fixtureName));
    }

    private static byte[] LoadFixture(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    // proyeccion estable del Sample, ignorando campos no deterministicos (uuid, rawJson)
    private static object ToSnapshot(Sample s) => new
    {
        analyzerType = s.AnalyzerType,
        serial = s.Serial.AsString,
        sampleIdentifier = s.SampleIdentifier,
        s.Operator,
        s.Program,
        s.StartAt,
        s.EndAt,
        s.Ibp,
        s.Fbp,
        s.Residue,
        s.Recovery,
        s.FbpVolume,
        s.EndOfTest,
        s.AlarmBitmask,
        curve = new
        {
            count = s.Curve.Count,
            points = s.Curve.Points.Select(p => new { p.PctRecovered, p.TemperatureC }).ToList(),
        },
        // hpgl_curve son bytes HP-GL crudos del payload (>50KB); lo excluimos del snapshot
        // porque la fixture binaria misma ya garantiza paridad de ese bloque.
        extra = s.Extra
            .Where(kv => kv.Key != "hpgl_curve")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value),
        s.SourceIp,
        receivedAtUnixSeconds = s.ReceivedAt.ToUnixTimeSeconds(),
    };
}

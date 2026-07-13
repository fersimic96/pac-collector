using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PacCollector.Api.Tests;

// fixture comun: levanta la app con un DataDir temporal y AutoStartServer=false
// asi los tests no abren sockets reales en :3000/:9980/:631
public sealed class ApiFixture : WebApplicationFactory<Program>, IDisposable
{
    public string DataDir { get; }

    public ApiFixture()
    {
        DataDir = Path.Combine(Path.GetTempPath(), "paccollector-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);

        // settings.json con AutoStartServer=false para que no levante listeners
        var settings = """
        {
          "version": 1,
          "general": { "autoStartServer": false, "printServerEnabled": false }
        }
        """;
        File.WriteAllText(Path.Combine(DataDir, "settings.json"), settings);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDir"] = DataDir,
            });
        });
        return base.CreateHost(builder);
    }

    public new void Dispose()
    {
        base.Dispose();
        try { Directory.Delete(DataDir, recursive: true); } catch { /* best-effort */ }
    }
}

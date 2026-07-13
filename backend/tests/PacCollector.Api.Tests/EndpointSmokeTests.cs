using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace PacCollector.Api.Tests;

// smoke tests del API: cada endpoint contesta 200/204 con el shape esperado.
// no validan logica de dominio profunda (eso lo cubren los tests de Application/Infrastructure).
public class EndpointSmokeTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;
    public EndpointSmokeTests(ApiFixture fixture) => _fixture = fixture;

    private HttpClient Client => _fixture.CreateClient();

    [Fact]
    public async Task Health_Returns200WithStatusOk()
    {
        var resp = await Client.GetAsync("/api/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task ListInstruments_ReturnsEmptyArrayInitially()
    {
        var resp = await Client.GetAsync("/api/instruments");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetSample_NotFoundReturnsNotFound()
    {
        var resp = await Client.GetAsync("/api/samples/nonexistent-uuid");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SearchSamples_EmptyFiltersReturnsPage()
    {
        var resp = await Client.PostAsJsonAsync("/api/samples/search", new { offset = 0, limit = 50 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("total").GetUInt64().Should().Be(0);
    }

    [Fact]
    public async Task ListPlugins_Returns7LimsPlugins()
    {
        // /api/plugins lista solo plugins LIMS (7 equipos PAC). Los print plugins
        // son detalle interno (se aplican via sniff de bytes en FindForPrint).
        var resp = await Client.GetAsync("/api/plugins");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetArrayLength().Should().Be(7);
    }

    [Fact]
    public async Task GetConfig_ReturnsAppConfig()
    {
        var resp = await Client.GetAsync("/api/config");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("version").GetUInt32().Should().Be(1);
        body.TryGetProperty("general", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ServerStatus_HasAllExpectedFields()
    {
        var resp = await Client.GetAsync("/api/server/status");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("running").GetBoolean().Should().BeFalse();
        body.GetProperty("printRunning").GetBoolean().Should().BeFalse();
        body.GetProperty("udpPort").GetUInt16().Should().Be(3000);
        body.GetProperty("tcpPort").GetUInt16().Should().Be(9980);
        body.GetProperty("printPort").GetUInt16().Should().Be(631);
        body.GetProperty("instrumentsCount").GetInt32().Should().Be(0);
        body.GetProperty("samplesToday").GetUInt64().Should().Be(0);
        body.TryGetProperty("serverIp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task NetworkLocalIps_Returns200WithArray()
    {
        var resp = await Client.GetAsync("/api/network/local-ips");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task NetworkInterfaces_Returns200WithArray()
    {
        var resp = await Client.GetAsync("/api/network/interfaces");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SystemPlatform_ReturnsOsString()
    {
        var resp = await Client.GetAsync("/api/system/platform");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var platform = body.GetProperty("platform").GetString();
        platform.Should().BeOneOf("windows", "macos", "linux", "unknown");
    }

    [Fact]
    public async Task PatchPluginEnabled_ReturnsNoContent()
    {
        var resp = await Client.PatchAsJsonAsync("/api/plugins/optipmd-builtin/enabled", new { enabled = false });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // verificar via /api/plugins
        var list = await Client.GetFromJsonAsync<JsonElement>("/api/plugins");
        foreach (var p in list.EnumerateArray())
        {
            if (p.GetProperty("id").GetString() == "optipmd-builtin")
                p.GetProperty("enabled").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public async Task ListenerStartStop_Idempotent()
    {
        (await Client.PostAsync("/api/listeners/stop", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.PostAsync("/api/listeners/stop", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

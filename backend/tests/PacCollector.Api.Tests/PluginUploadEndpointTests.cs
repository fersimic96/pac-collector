using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace PacCollector.Api.Tests;

// cubre los flujos del upload + delete + reload con fallback:
//   - upload OK con spec valido -> 201 + count +1
//   - JSON invalido -> 400 con error
//   - campos requeridos missing -> 400 con error
//   - delete de embedded -> 404 (no se puede borrar lo que no esta en override dir)
//   - delete de override -> 204 + count -1
//   - reload -> 200 + count actual
public class PluginUploadEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;
    public PluginUploadEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private HttpClient Client => _fixture.CreateClient();

    private static StringContent JsonBody(string raw) => new(raw, Encoding.UTF8, "application/json");

    [Fact]
    public async Task UploadLims_WithValidSpec_Returns201AndAppearsInList()
    {
        var spec = """
        {
          "pluginId": "optix-test-1",
          "displayName": "PAC OptiX Test",
          "analyzerType": "OptiX-Test",
          "vendor": "PAC Collector",
          "version": "0.1.0",
          "fieldSpecs": [
            { "key": "Result", "label": "Resultado", "unit": "°C", "group": "Resultado" }
          ]
        }
        """;
        var before = await Client.GetFromJsonAsync<JsonElement>("/api/plugins");
        var beforeCount = before.GetArrayLength();

        var resp = await Client.PostAsync("/api/plugins/lims", JsonBody(spec));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("activePluginsCount").GetInt32().Should().Be(beforeCount + 1);

        var after = await Client.GetFromJsonAsync<JsonElement>("/api/plugins");
        var ids = after.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        ids.Should().Contain("optix-test-1");
    }

    [Fact]
    public async Task UploadLims_WithInvalidJson_Returns400WithError()
    {
        var resp = await Client.PostAsync("/api/plugins/lims", JsonBody("{this is not json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
        body.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
        body.GetProperty("errors")[0].GetString().Should().Contain("JSON");
    }

    [Fact]
    public async Task UploadLims_MissingRequiredFields_Returns400WithFieldDetail()
    {
        var spec = """{"vendor": "x"}""";
        var resp = await Client.PostAsync("/api/plugins/lims", JsonBody(spec));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var errors = body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        errors.Should().Contain(e => e!.Contains("pluginId"));
        errors.Should().Contain(e => e!.Contains("analyzerType"));
        errors.Should().Contain(e => e!.Contains("displayName"));
    }

    [Fact]
    public async Task UploadLims_EmptyBody_Returns400()
    {
        var resp = await Client.PostAsync("/api/plugins/lims", JsonBody(""));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DeleteLims_OfEmbeddedPlugin_Returns404Because_NotOverridable()
    {
        var resp = await Client.DeleteAsync("/api/plugins/lims/optipmd-builtin");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
        body.GetProperty("errors")[0].GetString().Should().Contain("override-de-disco");
    }

    [Fact]
    public async Task UploadAndDeleteLims_RoundTripsCorrectly()
    {
        var spec = """
        {
          "pluginId": "optix-roundtrip",
          "displayName": "Roundtrip",
          "analyzerType": "OptiX-RT",
          "vendor": "PAC Collector",
          "version": "0.1.0",
          "fieldSpecs": []
        }
        """;
        var up = await Client.PostAsync("/api/plugins/lims", JsonBody(spec));
        up.StatusCode.Should().Be(HttpStatusCode.Created);

        var del = await Client.DeleteAsync("/api/plugins/lims/optix-roundtrip");
        del.StatusCode.Should().Be(HttpStatusCode.OK);
        var delBody = await del.Content.ReadFromJsonAsync<JsonElement>();
        delBody.GetProperty("ok").GetBoolean().Should().BeTrue();

        var after = await Client.GetFromJsonAsync<JsonElement>("/api/plugins");
        var ids = after.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        ids.Should().NotContain("optix-roundtrip");
    }

    [Fact]
    public async Task UploadPrint_WithValidSpec_Returns201()
    {
        var spec = """
        {
          "id": "optix-print-test",
          "displayName": "OptiX Print",
          "analyzerType": "OptiX-Print",
          "vendor": "PAC Collector",
          "version": "0.1.0",
          "kind": "labelValue",
          "headerMarker": "OptiX-Print",
          "headlineLabel": "Result",
          "extraFieldKeys": [],
          "fieldSpecs": []
        }
        """;
        var resp = await Client.PostAsync("/api/plugins/print", JsonBody(spec));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UploadPrint_MissingHeaderMarker_Returns400()
    {
        var spec = """
        {
          "id": "broken",
          "displayName": "Broken",
          "analyzerType": "OptiX-Broken",
          "kind": "labelValue"
        }
        """;
        var resp = await Client.PostAsync("/api/plugins/print", JsonBody(spec));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").EnumerateArray()
            .Any(e => e.GetString()!.Contains("headerMarker"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Reload_Returns200WithActiveCount()
    {
        var resp = await Client.PostAsync("/api/plugins/reload", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("activePluginsCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Upload_PreservesEnabledStateAcrossReload()
    {
        // disable un plugin existente
        await Client.PatchAsJsonAsync("/api/plugins/optipmd-builtin/enabled", new { enabled = false });

        // subir un spec (que dispara reload)
        var spec = """
        {
          "pluginId": "optix-preserve-enabled",
          "displayName": "Preserve",
          "analyzerType": "OptiX-PE",
          "vendor": "PAC Collector",
          "version": "0.1.0",
          "fieldSpecs": []
        }
        """;
        await Client.PostAsync("/api/plugins/lims", JsonBody(spec));

        var after = await Client.GetFromJsonAsync<JsonElement>("/api/plugins");
        var pmd = after.EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == "optipmd-builtin");
        pmd.GetProperty("enabled").GetBoolean().Should().BeFalse("enabled=false debe sobrevivir al reload");

        // cleanup
        await Client.PatchAsJsonAsync("/api/plugins/optipmd-builtin/enabled", new { enabled = true });
        await Client.DeleteAsync("/api/plugins/lims/optix-preserve-enabled");
    }
}

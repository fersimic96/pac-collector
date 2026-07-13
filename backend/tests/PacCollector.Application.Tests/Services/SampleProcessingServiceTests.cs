using System.Text;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PacCollector.Application.Services;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Ports;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Application.Tests.Services;

public class SampleProcessingServiceTests
{
    private readonly IPluginRegistry _plugins = Substitute.For<IPluginRegistry>();
    private readonly ISampleRepository _samples = Substitute.For<ISampleRepository>();
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly IFileWriter _files = Substitute.For<IFileWriter>();
    private readonly IEventBus _events = Substitute.For<IEventBus>();

    private SampleProcessingService NewSut() => new(_plugins, _samples, _instruments, _files, _events);

    private static byte[] Json(string analyzerType, string? extra = null)
    {
        var body = $"{{\"AnalyzerType\":\"{analyzerType}\"{(extra is null ? "" : "," + extra)}}}";
        return Encoding.UTF8.GetBytes(body);
    }

    [Fact]
    public async Task NoPluginForType_PublishesUnknownPayload_AndReturnsFalse()
    {
        _plugins.FindForType("OptiUnknown").Returns((IInstrumentPlugin?)null);
        _files.WriteUnknownPayloadAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new UnknownPayloadSaved("/tmp/unknown.bin"));

        var ok = await NewSut().ProcessRawMessageAsync(Json("OptiUnknown"), sourceIp: null);

        ok.Should().BeFalse();
        _events.Received(1).Publish(Arg.Is<DomainEvent.UnknownPayloadReceived>(
            e => e.AnalyzerType == "OptiUnknown" && e.SavedPath == "/tmp/unknown.bin"));
    }

    [Fact]
    public async Task InvalidJson_PublishesUnknownPayload_WithReason()
    {
        _files.WriteUnknownPayloadAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new UnknownPayloadSaved("/tmp/u.bin"));

        var ok = await NewSut().ProcessRawMessageAsync(Encoding.UTF8.GetBytes("not json"), null);

        ok.Should().BeFalse();
        _events.Received().Publish(Arg.Is<DomainEvent.UnknownPayloadReceived>(
            e => e.Reason.Contains("invalid JSON")));
    }

    [Fact]
    public async Task ValidSample_NewInstrument_RunsFullPipeline()
    {
        var plugin = Substitute.For<IInstrumentPlugin>();
        var sample = new Sample
        {
            Uuid = "u-1",
            Serial = AnalyzerSerial.Create("2125"),
            AnalyzerType = "OptiPMD",
            SampleIdentifier = "29",
            Ibp = 142.7,
            Fbp = 366.9,
            Curve = DistillationCurve.Empty(),
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        plugin.ParseMessage(Arg.Any<ReadOnlyMemory<byte>>(), null, Arg.Any<DateTimeOffset>())
            .Returns(sample);
        _plugins.FindForType("OptiPMD").Returns(plugin);
        _instruments.FindBySerialAsync("2125", Arg.Any<CancellationToken>()).Returns((Instrument?)null);
        _samples.ExistsForRunAsync("2125", "29", sample.StartAt, Arg.Any<CancellationToken>()).Returns(false);

        var ok = await NewSut().ProcessRawMessageAsync(Json("OptiPMD"), null);

        ok.Should().BeTrue();
        _events.Received().Publish(Arg.Any<DomainEvent.InstrumentDiscovered>());
        await _instruments.Received(1).UpsertOnContactAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>());
        await _samples.Received(1).SaveReceivedSampleAsync(sample, Arg.Any<CancellationToken>());
        await _instruments.Received(1).IncrementSampleCountAsync("2125", Arg.Any<CancellationToken>());
        await _files.Received(1).WriteSampleArtifactsAsync(sample, Arg.Any<CancellationToken>());
        _events.Received().Publish(Arg.Is<DomainEvent.SampleReceived>(e => e.Uuid == "u-1"));
    }

    [Fact]
    public async Task DuplicateSample_SkipsPersistence()
    {
        var plugin = Substitute.For<IInstrumentPlugin>();
        var sample = new Sample
        {
            Uuid = "u-1",
            Serial = AnalyzerSerial.Create("2125"),
            AnalyzerType = "OptiPMD",
            SampleIdentifier = "29",
            Curve = DistillationCurve.Empty(),
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        plugin.ParseMessage(Arg.Any<ReadOnlyMemory<byte>>(), null, Arg.Any<DateTimeOffset>()).Returns(sample);
        _plugins.FindForType("OptiPMD").Returns(plugin);
        _instruments.FindBySerialAsync("2125", Arg.Any<CancellationToken>()).Returns((Instrument?)null);
        _samples.ExistsForRunAsync("2125", "29", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(true);

        var ok = await NewSut().ProcessRawMessageAsync(Json("OptiPMD"), null);

        ok.Should().BeFalse();
        _events.Received().Publish(Arg.Any<DomainEvent.SampleDuplicateSkipped>());
        await _samples.DidNotReceive().SaveReceivedSampleAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>());
        await _files.DidNotReceive().WriteSampleArtifactsAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>());
    }

    // ── print path ──

    [Fact]
    public async Task Print_NoPluginMatches_PublishesUnknownPayload_AndReturnsFalse()
    {
        _plugins.FindForPrint(Arg.Any<ReadOnlyMemory<byte>>()).Returns((IInstrumentPlugin?)null);
        _files.WriteUnknownPayloadAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new UnknownPayloadSaved("/tmp/print-unknown.bin"));

        var raw = Encoding.UTF8.GetBytes("\x1B%-12345X@PJL\nrandom print data");
        var ok = await NewSut().ProcessPrintMessageAsync(raw, sourceIp: "10.0.0.5");

        ok.Should().BeFalse();
        _events.Received(1).Publish(Arg.Is<DomainEvent.UnknownPayloadReceived>(
            e => e.SavedPath == "/tmp/print-unknown.bin" && e.Reason.Contains("print")));
        await _samples.DidNotReceive().SaveReceivedSampleAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Print_PluginThrowsDomainException_PublishesPluginParseFailed_AndPropagates()
    {
        var plugin = Substitute.For<IInstrumentPlugin>();
        plugin.ParsePrintMessage(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>())
            .Throws(new PacCollector.Domain.Errors.MalformedMessageException("bad PCL"));
        _plugins.FindForPrint(Arg.Any<ReadOnlyMemory<byte>>()).Returns(plugin);

        var raw = new byte[] { 0x1B, 0x45, 0x20, 0x20 };
        var act = async () => await NewSut().ProcessPrintMessageAsync(raw, null);

        await act.Should().ThrowAsync<PacCollector.Domain.Errors.MalformedMessageException>();
        _events.Received(1).Publish(Arg.Is<DomainEvent.PluginParseFailed>(e => e.Reason.Contains("bad PCL")));
    }

    [Fact]
    public async Task Print_ValidSample_RunsFullPipeline_AndPublishesSampleReceived()
    {
        var plugin = Substitute.For<IInstrumentPlugin>();
        var sample = new Sample
        {
            Uuid = "print-uuid",
            Serial = AnalyzerSerial.Create("8076"),
            AnalyzerType = "OptiFZP",
            SampleIdentifier = "FZP-001",
            Curve = DistillationCurve.Empty(),
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        plugin.ParsePrintMessage(Arg.Any<ReadOnlyMemory<byte>>(), null, Arg.Any<DateTimeOffset>()).Returns(sample);
        _plugins.FindForPrint(Arg.Any<ReadOnlyMemory<byte>>()).Returns(plugin);
        _instruments.FindBySerialAsync("8076", Arg.Any<CancellationToken>()).Returns((Instrument?)null);
        _samples.ExistsForRunAsync("8076", "FZP-001", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(false);

        var ok = await NewSut().ProcessPrintMessageAsync(new byte[] { 0x1B, 0x45 }, null);

        ok.Should().BeTrue();
        _events.Received().Publish(Arg.Any<DomainEvent.InstrumentDiscovered>());
        await _samples.Received(1).SaveReceivedSampleAsync(sample, Arg.Any<CancellationToken>());
        await _instruments.Received(1).IncrementSampleCountAsync("8076", Arg.Any<CancellationToken>());
        await _files.Received(1).WriteSampleArtifactsAsync(sample, Arg.Any<CancellationToken>());
        _events.Received().Publish(Arg.Is<DomainEvent.SampleReceived>(e => e.Uuid == "print-uuid"));
    }

    [Fact]
    public async Task Print_DuplicateRun_SkipsPersistence()
    {
        var plugin = Substitute.For<IInstrumentPlugin>();
        var sample = new Sample
        {
            Uuid = "print-uuid",
            Serial = AnalyzerSerial.Create("8076"),
            AnalyzerType = "OptiFZP",
            SampleIdentifier = "FZP-001",
            Curve = DistillationCurve.Empty(),
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        plugin.ParsePrintMessage(Arg.Any<ReadOnlyMemory<byte>>(), null, Arg.Any<DateTimeOffset>()).Returns(sample);
        _plugins.FindForPrint(Arg.Any<ReadOnlyMemory<byte>>()).Returns(plugin);
        _instruments.FindBySerialAsync("8076", Arg.Any<CancellationToken>()).Returns((Instrument?)null);
        _samples.ExistsForRunAsync("8076", "FZP-001", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(true);

        var ok = await NewSut().ProcessPrintMessageAsync(new byte[] { 0x1B, 0x45 }, null);

        ok.Should().BeFalse();
        _events.Received().Publish(Arg.Any<DomainEvent.SampleDuplicateSkipped>());
        await _samples.DidNotReceive().SaveReceivedSampleAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>());
        await _files.DidNotReceive().WriteSampleArtifactsAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>());
    }
}

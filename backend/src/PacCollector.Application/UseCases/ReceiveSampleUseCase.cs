// ══════════════════════════════════════════════════════════════════════════════
// USE CASE ReceiveSampleUseCase — el punto de entrada de una MUESTRA LIMS.
// Lo llama el TcpServer cuando un equipo manda datos por TCP (:9980).
//
// Es intencionalmente MINIMALISTA: su trabajo es (1) calcular el checksum que el
// equipo espera de vuelta, y (2) delegar TODO el procesamiento pesado al
// SampleProcessingService. Si mañana hay que agregar logging/validación de IP,
// se agrega ACÁ sin ensuciar el pipeline.
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
using PacCollector.Application.Services;      // [NUESTRO] SampleProcessingService (el pipeline)
using PacCollector.Domain.ValueObjects;       // [NUESTRO] PacChecksum

namespace PacCollector.Application.UseCases;

public sealed class ReceiveSampleUseCase
{
    // dependencia: el pipeline completo. Se la inyecta el DI container.
    private readonly SampleProcessingService _processing;

    // constructor: recibe el pipeline y lo guarda.
    public ReceiveSampleUseCase(SampleProcessingService processing) => _processing = processing;

    // ─── EJECUTAR ─────────────────────────────────────────────────────────────
    // async [C#] = asíncrono.  Task<PacChecksum> [.NET] = "voy a devolver un PacChecksum, más tarde".
    public async Task<PacChecksum> ExecuteAsync(
        ReadOnlyMemory<byte> raw,          // ReadOnlyMemory<byte> [.NET] = los bytes crudos que mandó el equipo
        string? sourceIp,                  // desde qué IP llegó (puede ser null)
        CancellationToken ct = default)    // CancellationToken [.NET] = "señal para cancelar si hay que apagar"
    {
        var checksum = PacChecksum.FromBytes(raw.Span);
        // └─ PacChecksum.FromBytes [NUESTRO] = calcula el checksum del mensaje (el algoritmo del DLL).
        //    raw.Span [.NET] = la ventana de bytes. Esto es lo que el equipo espera en la respuesta.

        await _processing.ProcessRawMessageAsync(raw, sourceIp, ct);
        // └─ await [C#] = esperá que el pipeline termine (parsear, guardar, emitir evento).
        //    ProcessRawMessageAsync [NUESTRO] = el pipeline completo de 6 pasos (SampleProcessingService).

        return checksum;
        // └─ devuelve el checksum al TcpServer, que lo mete en la respuesta {"Error":"","SaveCheckSum":"XXXX"}
    }
}

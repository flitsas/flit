using System.Net;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// <c>POST /api/v1/consolidados/lotes/{loteId}/cancelacion</c> (HU #13385). El dueño (<see cref="UsuarioId"/>) sale del
/// token (<c>sub</c>); el rol, la IP y el navegador solo van a la auditoría.
/// </summary>
public sealed record CancelarLoteConsolidadosCommand(
    Guid LoteId,
    Guid UsuarioId,
    string RolCodigo,
    IPAddress? ClientIp = null,
    string? UserAgent = null);

/// <summary>Resultado de <see cref="CancelarLoteConsolidadosHandler.HandleAsync"/>; la API lo traduce a HTTP.</summary>
/// <param name="Estado">Qué pasó (202 / 404 / 409 / 503).</param>
/// <param name="Lote">El lote cancelado (o tal cual si ya lo estaba o ya había terminado).</param>
public sealed record CancelarLoteConsolidadosResultado(CancelarLoteEstado Estado, ConsolidadoExportBatch? Lote = null)
{
    /// <summary>202: lo canceló esta petición o ya estaba cancelado (idempotente).</summary>
    public bool Aceptado => Estado is CancelarLoteEstado.Cancelado or CancelarLoteEstado.YaCancelado;
}

/// <summary>
/// HU #13385 (Feature #13307, diseño 09 §2.4, CF-09/CF-10) — cancela el lote en curso de su dueño.
/// <list type="bullet">
///   <item>La transacción (lock del lote del <c>sub</c>, ítems vivos → <c>cancelado</c>, partes descartadas, DEK
///   destruida, <c>lote_cancelado</c>) es <see cref="IConsolidadoLoteRepository.CancelarAsync"/>.</item>
///   <item>Después, fuera de la transacción y best-effort, el borrado de los binarios de las partes purgadas
///   (<see cref="IConsolidadoLoteParteStorage.Delete"/>; hoy no-op, ilegibles sin la DEK). Un fallo ahí no cambia la
///   respuesta: la cancelación ya está confirmada.</item>
///   <item>El permiso (<c>consolidado-masivo.download</c>) lo exige la API antes de llegar aquí (403 sin cambios).</item>
/// </list>
/// Los consolidados que el lote ya generó quedan como oficiales en sus trámites (CF-09): este caso de uso no los toca.
/// Logs sin PII: solo ids y conteos.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await handler.HandleAsync(new CancelarLoteConsolidadosCommand(loteId, sub, "Radicador"), ct);
/// if (r.Aceptado) return Results.Accepted(null, LoteConsolidadosDto.Desde(r.Lote!));
/// </code>
/// </remarks>
public sealed partial class CancelarLoteConsolidadosHandler(
    IConsolidadoLoteRepository repositorio,
    IConsolidadoLoteParteStorage storage,
    ILogger<CancelarLoteConsolidadosHandler>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<CancelarLoteConsolidadosHandler>.Instance;

    public async Task<CancelarLoteConsolidadosResultado> HandleAsync(
        CancelarLoteConsolidadosCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.LoteId == Guid.Empty || command.UsuarioId == Guid.Empty)
            return new CancelarLoteConsolidadosResultado(CancelarLoteEstado.NoEncontrado);

        var r = await repositorio.CancelarAsync(
            new CancelacionLote(command.LoteId, command.UsuarioId, command.RolCodigo, command.ClientIp, command.UserAgent),
            ct).ConfigureAwait(false);

        switch (r.Estado)
        {
            case CancelarLoteEstado.Cancelado:
                LogCancelado(_logger, command.LoteId, r.ItemsCancelados, r.RutasPartesPurgadas?.Count ?? 0);
                BorrarBinarios(command.LoteId, r.RutasPartesPurgadas);
                break;
            case CancelarLoteEstado.NoRegistrado:
                LogNoRegistrado(_logger, command.LoteId);
                break;
        }

        return new CancelarLoteConsolidadosResultado(r.Estado, r.Lote);
    }

    private void BorrarBinarios(Guid loteId, IReadOnlyList<string>? rutas)
    {
        foreach (var ruta in rutas ?? [])
        {
            try
            {
                storage.Delete(ruta);
            }
#pragma warning disable CA1031 // Best-effort: la cancelación ya está confirmada y el binario es ilegible sin la DEK.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogBorradoFallido(_logger, loteId, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote de consolidados {LoteId} cancelado por su dueño: {Items} ítem(s) vivos cancelados, {Partes} parte(s) purgadas.")]
    private static partial void LogCancelado(ILogger logger, Guid loteId, int items, int partes);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote de consolidados {LoteId}: la cancelación no se registró (transacción revertida); el lote sigue activo.")]
    private static partial void LogNoRegistrado(ILogger logger, Guid loteId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote de consolidados {LoteId}: el borrado best-effort de una parte purgada falló ({Tipo}); queda ilegible sin la DEK.")]
    private static partial void LogBorradoFallido(ILogger logger, Guid loteId, string tipo);
}

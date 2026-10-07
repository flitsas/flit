using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Procesar UN ítem ya reclamado (<c>procesando</c>) de un lote de descarga masiva (HU #13375).
/// </summary>
/// <param name="Lote">Lote del ítem (origen, compañía congelada, organismo, solicitante y tipo de documento).</param>
/// <param name="Item">Ítem reclamado; <see cref="ConsolidadoExportBatchItem.Attempts"/> = fallos técnicos previos.</param>
/// <param name="MaxIntentos"><c>max_item_attempts</c> de <c>consolidado_export_settings</c>.</param>
/// <param name="EsperaReintentoSegundos"><c>retry_delay_seconds</c> de <c>consolidado_export_settings</c>.</param>
public sealed record ProcesarItemLoteCommand(
    ConsolidadoExportBatch Lote,
    ConsolidadoExportBatchItem Item,
    short MaxIntentos,
    int EsperaReintentoSegundos)
{
    /// <summary>Comando con los parámetros de reintento leídos de la fila de settings.</summary>
    public static ProcesarItemLoteCommand Con(
        ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item, ConsolidadoExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(lote, item, settings.MaxItemAttempts, settings.RetryDelaySeconds);
    }
}

/// <summary>Desenlace del procesamiento de un ítem.</summary>
public enum ProcesarItemLoteDesenlace
{
    /// <summary>Hay PDF: el ítem queda <c>incluido</c> con su snapshot.</summary>
    Incluido,

    /// <summary>El ítem queda <c>omitido</c> con código y texto legible.</summary>
    Omitido,

    /// <summary>Error técnico con intentos disponibles: el ítem vuelve a <c>pendiente</c> con <c>next_attempt_at</c>.</summary>
    Reprogramado,
}

/// <summary>Resultado de <see cref="ProcesarItemLoteHandler.HandleAsync"/>.</summary>
/// <param name="Desenlace">Qué se decidió.</param>
/// <param name="Aplicado">
/// <c>true</c> si la persistencia cerró el ítem; <c>false</c> si ya no estaba en <c>procesando</c> (lote cancelado
/// o cierre repetido) y no se escribió nada.
/// </param>
/// <param name="Intentos">Valor de <c>attempts</c> tras el procesamiento.</param>
/// <param name="Codigo">Código de omisión (si <see cref="ProcesarItemLoteDesenlace.Omitido"/>).</param>
/// <param name="Motivo">Texto legible de la omisión.</param>
/// <param name="DeliveryMode"><c>existente</c> | <c>generado</c> (si <see cref="ProcesarItemLoteDesenlace.Incluido"/>).</param>
/// <param name="SiguienteIntentoEn"><c>next_attempt_at</c> (si <see cref="ProcesarItemLoteDesenlace.Reprogramado"/>).</param>
public sealed record ProcesarItemLoteResultado(
    ProcesarItemLoteDesenlace Desenlace,
    bool Aplicado,
    short Intentos,
    string? Codigo = null,
    string? Motivo = null,
    string? DeliveryMode = null,
    DateTimeOffset? SiguienteIntentoEn = null);

/// <summary>
/// HU #13375 (Épica #13216, CF-09, CF-16, CF-20) — procesa un ítem del lote de forma independiente:
/// <list type="number">
///   <item>Revalida el acceso del solicitante con el procesador de su origen (<see cref="ILoteItemOrigen"/>). Sin
///   acceso ⇒ <c>omitido</c> «Acceso revocado» y el entregador NO se llama.</item>
///   <item>Entrega con el entregador del origen («existente o primera generación», #13371). Un reintento tras una
///   generación ya persistida toma ese consolidado como existente (<c>soloSiNoExiste</c>).</item>
///   <item>Incluido ⇒ snapshot + modo de entrega; omisión de negocio ⇒ texto de <see cref="ConsolidadoErrorTextos"/>;
///   error técnico ⇒ <c>pendiente</c> con <c>next_attempt_at = ahora + retry_delay_seconds</c> y, al agotar
///   <c>max_item_attempts</c>, <c>omitido</c> <c>error_tecnico</c>.</item>
/// </list>
/// <para>No lanza por un caso malo: el lote sigue con el siguiente ítem. Solo propaga la cancelación y la falta de
/// procesador para el origen (error de configuración). Los logs llevan ids de lote e ítem y códigos, nunca placa,
/// radicado ni documento.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo (carril de ítems, #13376, en un scope nuevo por ítem):
/// <code>
/// var r = await handler.HandleAsync(ProcesarItemLoteCommand.Con(lote, itemReclamado, settings), ct);
/// // r.Desenlace: Incluido | Omitido | Reprogramado · r.Aplicado = false si el ítem ya no estaba en "procesando"
/// </code>
/// </remarks>
public sealed partial class ProcesarItemLoteHandler(
    LoteItemOrigenPorOrigen origenes,
    IConsolidadoLoteItemProceso proceso,
    ILogger<ProcesarItemLoteHandler> logger,
    TimeProvider? timeProvider = null)
{
    /// <summary>Causa registrada cuando la revalidación del acceso lanzó una excepción.</summary>
    public const string CausaRevalidacion = "revalidacion_fallida";

    /// <summary>Causa registrada cuando el entregador devolvió un resultado incoherente.</summary>
    public const string CausaResultadoInvalido = "resultado_entrega_invalido";

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<ProcesarItemLoteResultado> HandleAsync(ProcesarItemLoteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var contexto = LoteItemContexto.Desde(command.Lote, command.Item);
        var origen = origenes.Para(command.Lote.Origin);

        bool tieneAcceso;
        try
        {
            tieneAcceso = await origen.TieneAccesoAsync(contexto, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogExcepcion(logger, contexto.BatchId, contexto.ItemId, CausaRevalidacion, ex.GetType().Name);
            return await FalloTecnicoAsync(command, CausaRevalidacion, ct).ConfigureAwait(false);
        }

        if (!tieneAcceso)
            return await OmitirAsync(command, ConsolidadoLoteOmisiones.AccesoRevocado, command.Item.Attempts, ct).ConfigureAwait(false);

        LoteItemEntregaResult entrega;
        try
        {
            entrega = await origen.EntregarAsync(contexto, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogExcepcion(logger, contexto.BatchId, contexto.ItemId, ConsolidadoLoteEntregador.CausaExcepcion, ex.GetType().Name);
            entrega = LoteItemEntregaResult.Fallo(ConsolidadoLoteEntregador.CausaExcepcion);
        }

        switch (entrega.Estado)
        {
            case LoteItemEntregaEstado.Incluido
                when entrega.Adjunto is { } adjunto
                    && entrega.DeliveryMode is { } modo
                    && ConsolidadoExportDeliveryMode.Todos.Contains(modo):
                return await IncluirAsync(command, adjunto, modo, ct).ConfigureAwait(false);

            case LoteItemEntregaEstado.Omitido when ConsolidadoLoteOmisiones.EsValido(entrega.Codigo):
                return await OmitirAsync(command, entrega.Codigo!, command.Item.Attempts, ct).ConfigureAwait(false);

            case LoteItemEntregaEstado.ErrorTecnico:
                return await FalloTecnicoAsync(command, entrega.Codigo ?? CausaResultadoInvalido, ct).ConfigureAwait(false);

            default:
                return await FalloTecnicoAsync(command, CausaResultadoInvalido, ct).ConfigureAwait(false);
        }
    }

    private async Task<ProcesarItemLoteResultado> IncluirAsync(
        ProcesarItemLoteCommand command, LoteItemAdjunto adjunto, string modo, CancellationToken ct)
    {
        var item = command.Item;
        var aplicado = await proceso
            .MarcarIncluidoAsync(new LoteItemIncluido(command.Lote.Id, item.Id, adjunto, modo, _clock.GetUtcNow()), ct)
            .ConfigureAwait(false);
        LogIncluido(logger, command.Lote.Id, item.Id, modo, aplicado);
        return new ProcesarItemLoteResultado(ProcesarItemLoteDesenlace.Incluido, aplicado, item.Attempts, DeliveryMode: modo);
    }

    private async Task<ProcesarItemLoteResultado> OmitirAsync(
        ProcesarItemLoteCommand command, string codigo, short intentos, CancellationToken ct)
    {
        var item = command.Item;
        var motivo = ConsolidadoErrorTextos.ParaLote(codigo);
        var aplicado = await proceso
            .MarcarOmitidoAsync(new LoteItemOmitido(command.Lote.Id, item.Id, codigo, motivo, intentos, _clock.GetUtcNow()), ct)
            .ConfigureAwait(false);
        LogOmitido(logger, command.Lote.Id, item.Id, codigo, aplicado);
        return new ProcesarItemLoteResultado(ProcesarItemLoteDesenlace.Omitido, aplicado, intentos, codigo, motivo);
    }

    private async Task<ProcesarItemLoteResultado> FalloTecnicoAsync(
        ProcesarItemLoteCommand command, string causa, CancellationToken ct)
    {
        var item = command.Item;
        var intentos = (short)(item.Attempts + 1);
        LogFalloTecnico(logger, command.Lote.Id, item.Id, causa, intentos, command.MaxIntentos);

        if (intentos >= command.MaxIntentos)
            return await OmitirAsync(command, ConsolidadoLoteOmisiones.ErrorTecnico, intentos, ct).ConfigureAwait(false);

        var siguiente = _clock.GetUtcNow().AddSeconds(command.EsperaReintentoSegundos);
        var aplicado = await proceso
            .ReprogramarAsync(new LoteItemReintento(command.Lote.Id, item.Id, intentos, siguiente), ct)
            .ConfigureAwait(false);
        return new ProcesarItemLoteResultado(
            ProcesarItemLoteDesenlace.Reprogramado, aplicado, intentos, SiguienteIntentoEn: siguiente);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: ítem {ItemId} incluido ({DeliveryMode}); aplicado={Aplicado}.")]
    private static partial void LogIncluido(ILogger logger, Guid batchId, Guid itemId, string deliveryMode, bool aplicado);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: ítem {ItemId} omitido ({OmissionCode}); aplicado={Aplicado}.")]
    private static partial void LogOmitido(ILogger logger, Guid batchId, Guid itemId, string omissionCode, bool aplicado);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote {BatchId}: ítem {ItemId} con fallo técnico ({Causa}), intento {Intento} de {MaxIntentos}.")]
    private static partial void LogFalloTecnico(
        ILogger logger, Guid batchId, Guid itemId, string causa, short intento, short maxIntentos);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote {BatchId}: ítem {ItemId} lanzó {ExceptionType} ({Causa}).")]
    // Solo el tipo: el mensaje de una excepción de E/S o de BD puede arrastrar datos del trámite.
    private static partial void LogExcepcion(ILogger logger, Guid batchId, Guid itemId, string causa, string exceptionType);
}

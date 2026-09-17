using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Origen del disparo automático de la firma de impronta manual (HU #12116).</summary>
public static class FirmaImprontaAutomaticaOrigen
{
    /// <summary>El trámite queda en <c>entregado</c> (radicación, incluida re-radicación por subsanación).</summary>
    public const string Radicacion = "radicacion";

    /// <summary>El OT asigna la placa (Flujo B) sobre un trámite ya entregado.</summary>
    public const string AsignacionPlaca = "asignacion_placa";

    /// <summary>El gestor envía al OT un trámite en <c>asignado</c> (ADR-0059): reintento por si la identidad quedó vigente tras la asignación.</summary>
    public const string EnvioOt = "envio_ot";

    /// <summary>Backfill administrativo de trámites que quedaron sin firmar (sin consolidado generado).</summary>
    public const string Backfill = "backfill";
}

/// <summary>Desenlace de <see cref="FirmarImprontaManualSiListaHandler"/>.</summary>
public enum FirmaImprontaAutomaticaEstado
{
    /// <summary>Se firmó ahora: adjunto reemplazado + fila de auditoría persistida.</summary>
    Firmada,

    /// <summary>El adjunto ya traía el sello (idempotencia por hash del PDF base).</summary>
    YaFirmada,

    /// <summary>No cumple los gates de momento (ver <see cref="Motivo"/>: constantes de <see cref="ImprontaManualStampReadiness"/>).</summary>
    NoLista,

    /// <summary>El trámite no tiene adjunto activo tipo "impronta" no-Kyverum.</summary>
    SinImprontaManual,

    /// <summary>Fallo inesperado (trámite no encontrado u otro error best-effort).</summary>
    Fallo,
}

/// <param name="Motivo">
/// Con <see cref="FirmaImprontaAutomaticaEstado.NoLista"/>: constante de <see cref="ImprontaManualStampReadiness"/>.
/// Con <see cref="FirmaImprontaAutomaticaEstado.Fallo"/>: código sintético del fallo (sin PII).
/// </param>
public sealed record FirmaImprontaAutomaticaResultado(
    FirmaImprontaAutomaticaEstado Estado,
    string? Motivo = null);

/// <summary>
/// HU #12116 — firma automáticamente la impronta manual del trámite en cuanto se cumplen sus gates
/// (matrícula con placa asignada + identidad de propietarios vigente), sin esperar a que alguien
/// genere el consolidado. Reutiliza la misma lógica de sellado que <c>ConsolidadoCommand</c> /
/// <c>ConsolidadoMaestroCommand</c> vía <see cref="ImprontaManualStampApplier.StampAndPersistAsync"/>:
/// no duplica readiness ni persistencia.
///
/// <para>Best-effort SIEMPRE: nunca lanza (salvo <see cref="OperationCanceledException"/>). Un fallo
/// se loguea a Error (sin PII) y deja un evento consultable
/// <c>impronta_firma_automatica_fallida</c> en la bitácora de la instancia, vía el mismo puerto que
/// ya usa la regeneración documental trazada (Bug #11613), con su propio tipo de evento.</para>
/// </summary>
public sealed class FirmarImprontaManualSiListaHandler(
    IProcedureInstanceRepository repo,
    IExpedienteConsolidadoMerger merger,
    IAttachmentStorage storage,
    IImprontaManualStamper? stamper = null,
    ISignatureVaultPolicy? vaultPolicy = null,
    IVehicleSignatureImprintRepository? auditRepo = null,
    IRegeneracionDocumentalTrazaWriter? trazaWriter = null,
    ILogger<FirmarImprontaManualSiListaHandler>? logger = null)
{
    /// <summary>Tipo del evento persistido en <c>tramites.procedure_instance_events</c> ante un fallo.</summary>
    public const string EventoFallo = "impronta_firma_automatica_fallida";

    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;
    private readonly IRegeneracionDocumentalTrazaWriter _traza =
        trazaWriter ?? NullRegeneracionDocumentalTrazaWriter.Instance;
    private readonly ILogger _logger =
        (ILogger?)logger ?? NullLogger<FirmarImprontaManualSiListaHandler>.Instance;

    public async Task<FirmaImprontaAutomaticaResultado> HandleAsync(
        Guid procedureInstanceId, Guid tenantId, string origen, CancellationToken ct = default)
    {
        try
        {
            var instance = await repo
                .GetByIdWithFurGraphAsync(procedureInstanceId, tenantId, ct)
                .ConfigureAwait(false);
            if (instance is null)
                return await FalloAsync(tenantId, procedureInstanceId, origen, "tramite_no_encontrado", null, ct)
                    .ConfigureAwait(false);

            var attachment = instance.Attachments.FirstOrDefault(a =>
                string.Equals(a.Tipo, "impronta", StringComparison.OrdinalIgnoreCase)
                && !AttachmentProviders.IsKyverum(a.Provider));
            if (attachment is null)
                return new FirmaImprontaAutomaticaResultado(FirmaImprontaAutomaticaEstado.SinImprontaManual);

            if (stamper is null)
                return new FirmaImprontaAutomaticaResultado(FirmaImprontaAutomaticaEstado.SinImprontaManual);

            byte[] bytes;
            await using (var stream = await storage.OpenReadAsync(attachment.StoragePath, ct).ConfigureAwait(false))
            {
                if (stream is null)
                    return await FalloAsync(
                            tenantId, procedureInstanceId, origen, "adjunto_no_disponible", null, ct)
                        .ConfigureAwait(false);
                bytes = await ReadAllBytesAsync(stream, ct).ConfigureAwait(false);
            }

            var pdf = merger.NormalizeToPdf(bytes, attachment.Mimetype);

            var outcome = await ImprontaManualStampApplier
                .StampAndPersistAsync(
                    pdf, attachment, instance, storage, stamper, ct,
                    _vaultPolicy, repo, auditRepo, _logger)
                .ConfigureAwait(false);

            switch (outcome.Outcome)
            {
                case ImprontaManualStampOutcome.Applied:
                    await repo.SaveChangesAsync(ct).ConfigureAwait(false);
                    return new FirmaImprontaAutomaticaResultado(FirmaImprontaAutomaticaEstado.Firmada);

                case ImprontaManualStampOutcome.AlreadyStamped:
                    return new FirmaImprontaAutomaticaResultado(FirmaImprontaAutomaticaEstado.YaFirmada);

                case ImprontaManualStampOutcome.NotReady:
                    FirmaImprontaAutomaticaLog.NoLista(
                        _logger, procedureInstanceId, origen, outcome.NotReadyReason ?? "desconocido");
                    return new FirmaImprontaAutomaticaResultado(
                        FirmaImprontaAutomaticaEstado.NoLista, outcome.NotReadyReason);

                default:
                    return new FirmaImprontaAutomaticaResultado(FirmaImprontaAutomaticaEstado.SinImprontaManual);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await FalloAsync(
                    tenantId, procedureInstanceId, origen, "excepcion", ex.GetType().Name, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task<FirmaImprontaAutomaticaResultado> FalloAsync(
        Guid tenantId, Guid procedureInstanceId, string origen, string codigo, string? detalle,
        CancellationToken ct)
    {
        FirmaImprontaAutomaticaLog.Fallo(_logger, procedureInstanceId, origen, codigo);
        try
        {
            await _traza
                .EscribirFalloAsync(
                    tenantId, procedureInstanceId, origen, codigo, detalle, EventoFallo, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FirmaImprontaAutomaticaLog.TrazaNoPersistida(_logger, ex, procedureInstanceId, origen);
        }

        return new FirmaImprontaAutomaticaResultado(FirmaImprontaAutomaticaEstado.Fallo, codigo);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }
}

/// <summary>Logging source-generado (CA1848) de la firma automática de impronta. NUNCA incluye PII.</summary>
internal static partial class FirmaImprontaAutomaticaLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Impronta manual del trámite {InstanceId} (origen {Origen}) aún no lista para firmar: {Motivo}.")]
    public static partial void NoLista(ILogger logger, Guid instanceId, string origen, string motivo);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "La firma automática de impronta del trámite {InstanceId} (origen {Origen}) falló con el código {CodigoError}.")]
    public static partial void Fallo(ILogger logger, Guid instanceId, string origen, string codigoError);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "No se pudo persistir la traza del fallo de firma automática de impronta del trámite {InstanceId} (origen {Origen}); el diagnóstico queda solo en los logs.")]
    public static partial void TrazaNoPersistida(ILogger logger, Exception ex, Guid instanceId, string origen);
}

using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Mandato resultante de la acción (HU #14547). <c>AttachmentId</c> null si no se pudo emitir.</summary>
public sealed record MandatoRegeneradoDto(Guid? AttachmentId, Guid? FirmanteAnteriorId, Guid? FirmanteNuevoId, bool SinFirmar);

/// <summary>Consolidado del wizard rearmado (HU #14547).</summary>
public sealed record ConsolidadoRegeneradoDto(Guid AttachmentId, DateTimeOffset GeneradoEn);

/// <summary>
/// Consolidado maestro del OT (HU #14547). <c>Estado</c>: <c>regenerado</c> | <c>radicado_conservado</c> | <c>fallo</c>.
/// </summary>
public sealed record MaestroRegeneradoDto(string Estado, Guid? AttachmentId, DateTimeOffset? GeneradoEn);

/// <summary>Aviso no bloqueante de la acción, en español y sin datos personales.</summary>
public sealed record AvisoRegeneracionDto(string Codigo, string Mensaje);

/// <summary>Respuesta de <c>POST /api/v1/admin/tramites/{id}/mandato/regenerar</c> (HU #14547).</summary>
public sealed record RegenerarMandatoYConsolidadoResult(
    MandatoRegeneradoDto Mandato,
    ConsolidadoRegeneradoDto Consolidado,
    MaestroRegeneradoDto Maestro,
    IReadOnlyList<AvisoRegeneracionDto> Avisos);

/// <summary>
/// HU #14547 — matriz de precedencia del OT para el consolidado maestro. Hoy la compone
/// <c>AdminOtEndpoints</c> con <c>IResolvedDocumentMatrixResolver</c> (Admin), que esta capa no referencia;
/// el adaptador vive en Infraestructura. Lista vacía = el maestro cae al orden por modalidad.
/// </summary>
public interface IConsolidadoMaestroPrecedenciaProvider
{
    Task<IReadOnlyList<string>> ResolverAsync(Guid procedureTypeId, Guid? transitOfficeId, CancellationToken ct = default);
}

/// <summary>
/// HU #14547 — «Regenerar mandato y consolidado» (solo SuperAdmin, la comprobación de rol la hace el endpoint).
/// <list type="number">
///   <item>Gate: no existe → <c>not_found</c>; migrado de V1 en estado final → <c>migrado_solo_lectura</c>;
///   aprobado/anulado/revocado → <see cref="EstadoNoPermite"/>; sin FUR → <c>fur_requerido</c>. Ninguno toca nada.</item>
///   <item>Mandato con el mandatario recalculado (R1, <see cref="IMandatoRegenerador"/>). El FUR y el resto del
///   expediente NO se regeneran (R2). El personalizado de la compañía se conserva.</item>
///   <item>Evento <see cref="EventoMandatoRegenerado"/> con los ids del firmante anterior y el nuevo.</item>
///   <item>Consolidado del wizard sin cascada del FUR y sin la protección de <c>Source='user'</c>.</item>
///   <item>Maestro respetando la radicación ante Quipux y la matriz del OT. Si falla, éxito parcial: el maestro
///   queda invalidado (la emisión del mandato bajó su marca) y se rearma en la próxima vista.</item>
/// </list>
/// </summary>
public sealed partial class RegenerarMandatoYConsolidadoHandler(
    IProcedureInstanceRepository repo,
    IMandatoRegenerador mandatoRegenerador,
    GenerarConsolidadoHandler consolidado,
    GenerarConsolidadoMaestroHandler maestro,
    IConsolidadoMaestroPrecedenciaProvider? precedencia = null,
    ILogger<RegenerarMandatoYConsolidadoHandler>? logger = null)
{
    private readonly ILogger _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public const string EstadoNoPermite = "estado_no_permite_regenerar_mandato";
    public const string EventoMandatoRegenerado = "mandato_regenerado_admin";

    public const string MaestroRegenerado = "regenerado";
    public const string MaestroRadicadoConservado = "radicado_conservado";
    public const string MaestroFallo = "fallo";

    public const string AvisoMaestroRadicadoConservado = "maestro_radicado_conservado";
    public const string AvisoMaestroFallo = "maestro_fallo";
    public const string AvisoMandatoSinFirmar = "mandato_sin_firmar";
    public const string AvisoMandatoPersonalizadoConservado = "mandato_personalizado_conservado";

    public async Task<(RegenerarMandatoYConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id, Guid tenantId, Guid? userId, CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");
        if (instance.IsMigrated && TramiteEstado.EsFinal(instance.Status))
            return (null, "migrado_solo_lectura");
        if (TramiteEstado.EsFinal(instance.Status))
            return (null, EstadoNoPermite);
        // Sin FUR el wizard no se puede armar; cortar aquí evita dejar el mandato cambiado y el consolidado sin rearmar.
        if (!GenerarConsolidadoHandler.TieneFur(instance))
            return (null, SubmitGate.FurRequerido);

        var procedureTypeId = instance.ProcedureTypeId;
        var transitOfficeId = instance.TransitOfficeId;

        var (mandato, errorMandato) = await mandatoRegenerador.RegenerarMandatoAsync(id, tenantId, ct).ConfigureAwait(false);
        if (errorMandato is not null || mandato is null)
            return (null, errorMandato ?? "mandato_no_disponible");

        var avisos = new List<AvisoRegeneracionDto>();
        if (mandato.PersonalizadoConservado)
            avisos.Add(new(AvisoMandatoPersonalizadoConservado,
                "La compañía tiene un mandato personalizado: se conservó y no se regeneró."));
        if (mandato.SinFirmar)
            avisos.Add(new(AvisoMandatoSinFirmar,
                "La configuración de Mandatos no resuelve hoy un mandatario para este trámite: el mandato salió «Sin firmar»."));

        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = EventoMandatoRegenerado,
            // Solo ids: el nombre y el documento del mandatario ya viven en el directorio (sin PII de más).
            Payload = JsonSerializer.Serialize(new
            {
                firmante_anterior_id = mandato.FirmanteAnteriorId,
                firmante_nuevo_id = mandato.FirmanteNuevoId,
                mandato_attachment_id = mandato.AttachmentId,
                sin_firmar = mandato.SinFirmar,
                personalizado_conservado = mandato.PersonalizadoConservado,
            }),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId,
        }, ct).ConfigureAwait(false);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        // row_version nuevo tras el guardado del mandato (mismo motivo que ReloadAsync de la cascada).
        repo.ResetTracking();
        var (wizard, errorWizard) = await consolidado.RegenerarSinCascadaAsync(id, tenantId, ct).ConfigureAwait(false);
        if (errorWizard is not null || wizard is null)
            return (null, errorWizard ?? "consolidado_no_generado");

        repo.ResetTracking();
        var (maestroDto, avisoMaestro) = await RearmarMaestroAsync(id, tenantId, procedureTypeId, transitOfficeId, ct)
            .ConfigureAwait(false);
        if (avisoMaestro is not null)
            avisos.Add(avisoMaestro);

        var final = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).ConfigureAwait(false);
        var generadoWizard = final?.Attachments.FirstOrDefault(a => a.Id == wizard.Document.AttachmentId)?.UploadedAt
            ?? final?.ConsolidadoWizardGeneradoEn
            ?? DateTimeOffset.UtcNow;
        if (maestroDto.AttachmentId is { } maestroId)
        {
            maestroDto = maestroDto with
            {
                GeneradoEn = final?.Attachments.FirstOrDefault(a => a.Id == maestroId)?.UploadedAt
                    ?? final?.ConsolidadoMaestroGeneradoEn,
            };
        }

        return (new RegenerarMandatoYConsolidadoResult(
            new MandatoRegeneradoDto(mandato.AttachmentId, mandato.FirmanteAnteriorId, mandato.FirmanteNuevoId, mandato.SinFirmar),
            new ConsolidadoRegeneradoDto(wizard.Document.AttachmentId, generadoWizard),
            maestroDto,
            avisos), null);
    }

    private async Task<(MaestroRegeneradoDto Maestro, AvisoRegeneracionDto? Aviso)> RearmarMaestroAsync(
        Guid id, Guid tenantId, Guid procedureTypeId, Guid? transitOfficeId, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<string> matriz = [];
            if (precedencia is not null)
            {
                matriz = await precedencia.ResolverAsync(procedureTypeId, transitOfficeId, ct).ConfigureAwait(false);
            }

            var (result, error) = await maestro
                .RegenerarSinCascadaRespetandoRadicacionAsync(id, tenantId, matriz, ct)
                .ConfigureAwait(false);
            if (error is null && result is not null)
            {
                var conservado = result.Modo is ConsolidadoEntregaModos.RadicadoFijo or ConsolidadoEntregaModos.SoloLectura;
                return conservado
                    ? (new MaestroRegeneradoDto(MaestroRadicadoConservado, result.Document.AttachmentId, null),
                        new AvisoRegeneracionDto(AvisoMaestroRadicadoConservado,
                            "El consolidado del organismo ya se radicó en Quipux: el OT conserva el expediente radicado."))
                    : (new MaestroRegeneradoDto(MaestroRegenerado, result.Document.AttachmentId, null), null);
            }

            LogMaestroFallo(_logger, id, error ?? "sin_resultado");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogMaestroExcepcion(_logger, ex, id);
        }

        return (new MaestroRegeneradoDto(MaestroFallo, null, null),
            new AvisoRegeneracionDto(AvisoMaestroFallo,
                "El mandato y el consolidado del gestor se regeneraron, pero el consolidado del organismo no: se rearmará al abrirlo."));
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Regenerar mandato (HU #14547): el consolidado maestro del trámite {InstanceId} no se rearmó ({Error}); queda invalidado.")]
    private static partial void LogMaestroFallo(ILogger logger, Guid instanceId, string error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Regenerar mandato (HU #14547): el consolidado maestro del trámite {InstanceId} falló; queda invalidado.")]
    private static partial void LogMaestroExcepcion(ILogger logger, Exception ex, Guid instanceId);
}

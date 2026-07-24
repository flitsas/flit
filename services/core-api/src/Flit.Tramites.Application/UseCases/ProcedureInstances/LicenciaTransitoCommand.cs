using System.Text.Json;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Adjunta la Licencia de Tránsito (LT) que el Organismo de Tránsito emite al decidir el
/// trámite. A diferencia de los adjuntos del operador (solo en <c>borrador</c>), la LT se
/// adjunta con el trámite <c>entregado</c> o <c>aprobado</c>. Idempotente: re-adjuntar
/// reemplaza la LT previa (el consolidado siempre toma la vigente). Registra el evento
/// <c>lt_adjuntada</c> en la bitácora.
/// </summary>
public sealed class AdjuntarLicenciaTransitoHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    /// <summary>Tipo documental de la Licencia de Tránsito en <c>procedure_instance_attachments</c>.</summary>
    public const string Tipo = "licencia_transito";

    public async Task<(AttachmentDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        UploadAttachmentInput input,
        Guid? uploadedBy = null,
        CancellationToken ct = default)
    {
        if (input.Content is null || input.SizeBytes <= 0)
            return (null, "missing_file");
        if (string.IsNullOrWhiteSpace(input.Mimetype) || !AttachmentRules.ValidMimetypes.Contains(input.Mimetype))
            return (null, "invalid_mime");
        if (input.SizeBytes > AttachmentRules.MaxSizeBytes)
            return (null, "file_too_large");

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // La LT la emite el OT sobre un trámite ya radicado: entregado (en decisión) o aprobado.
        if (instance.Status is not (TramiteEstado.Entregado or TramiteEstado.Aprobado))
            return (null, "estado_invalido");

        var now = DateTimeOffset.UtcNow;

        // Reemplazo de la LT previa (misma semántica que el consolidado re-generado).
        foreach (var prev in instance.Attachments
                     .Where(a => string.Equals(a.Tipo, Tipo, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            storage.Delete(prev.StoragePath);
            instance.Attachments.Remove(prev);
            repo.RemoveAttachment(prev);
        }

        var filename = string.IsNullOrWhiteSpace(input.Filename) ? "licencia_transito.pdf" : input.Filename.Trim();
        var stored = await storage.SaveAsync(id, Tipo, filename, input.Content, ct);

        // Guarda FK (HU #10431): si el sub del JWT no existe en identity.users, se registra null.
        var resolvedUploadedBy = uploadedBy is { } ub && ub != Guid.Empty
            && await repo.UserExistsAsync(ub, ct).ConfigureAwait(false)
            ? uploadedBy
            : null;

        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = Tipo,
            Filename = filename,
            Mimetype = input.Mimetype.Trim().ToLowerInvariant(),
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "ot",
            UploadedAt = now,
            UploadedBy = resolvedUploadedBy,
        };
        instance.Attachments.Add(attachment);
        repo.Add(attachment);

        // Feature #10701 / HU #10860 — adjuntar la LT cambia el contenido del expediente: invalida
        // los consolidados persistidos (maestro y wizard) para que la próxima generación la incluya.
        instance.InvalidarConsolidados();

        var evento = new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "lt_adjuntada",
            Payload = JsonSerializer.Serialize(new
            {
                filename,
                sha256 = stored.Sha256,
                adjuntada_at = now,
            }),
            CreatedAt = now,
            CreatedBy = resolvedUploadedBy,
        };
        instance.Events.Add(evento);
        repo.Add(evento);

        await repo.SaveChangesAsync(ct);

        return (UploadAttachmentHandler.ToDto(attachment), null);
    }
}

/// <summary>
/// Resuelve el PDF del expediente consolidado más reciente del trámite para descarga
/// (lo consume el perfil OT para VISUALIZAR el consolidado sin regenerarlo). Considera tanto
/// el consolidado del wizard (<c>consolidado</c>) como el consolidado maestro
/// (<c>consolidado_maestro</c>, Feature #10701) y devuelve el más reciente entre ambos: así
/// "Ver consolidado" muestra el maestro cuando es lo único que el OT generó.
/// Errores: <c>not_found</c> (instancia), <c>consolidado_no_generado</c>, <c>file_missing</c>.
/// </summary>
public sealed class DescargarConsolidadoHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    private static readonly HashSet<string> ConsolidadoTipos = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "consolidado_maestro",
    };

    public async Task<(AttachmentDownload? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var consolidado = instance.Attachments
            .Where(a => ConsolidadoTipos.Contains(a.Tipo))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault();
        if (consolidado is null)
            return (null, "consolidado_no_generado");

        var stream = await storage.OpenReadAsync(consolidado.StoragePath, ct);
        if (stream is null)
            return (null, "file_missing");

        var filename = string.IsNullOrWhiteSpace(consolidado.Filename) ? "consolidado.pdf" : consolidado.Filename;
        var mime = string.IsNullOrWhiteSpace(consolidado.Mimetype) ? "application/pdf" : consolidado.Mimetype;
        return (new AttachmentDownload(stream, mime, filename), null);
    }
}

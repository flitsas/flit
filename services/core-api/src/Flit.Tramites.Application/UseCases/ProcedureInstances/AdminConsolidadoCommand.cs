using System.Text.Json;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// "Limpiar consolidado" (HU #12158, AC1): fuerza SIEMPRE la regeneración del expediente consolidado
/// del wizard, sin importar si el vigente lo cargó el admin a mano (<c>Source="user"</c>). Es la ÚNICA
/// vía que puede descartar un consolidado <c>Source="user"</c> — la regeneración automática/del
/// gestor lo protege (ver <see cref="GenerarConsolidadoHandler"/>, parámetro
/// <c>bypassSourceUserProtection</c>, AC2). Reutiliza el mecanismo de <c>force</c> ya existente
/// (Feature #11066) en vez de duplicar la lógica de invalidación/fusión.
/// </summary>
public sealed class LimpiarConsolidadoHandler(
    IProcedureInstanceRepository repo,
    GenerarConsolidadoHandler generarConsolidado)
{
    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var (result, error) = await generarConsolidado
            .HandleAsync(id, tenantId, userId, force: true, bypassSourceUserProtection: true, ct)
            .ConfigureAwait(false);
        if (error is not null || result is null)
            return (null, error);

        // AC4 — trazabilidad de la acción explícita del admin. Se registra AQUÍ, además del evento
        // genérico "consolidado_generado" que ya persiste GenerarConsolidadoHandler, para que quede
        // claro en la bitácora que este reemplazo lo pidió el admin (y no una cascada automática).
        var now = DateTimeOffset.UtcNow;
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "consolidado_limpiado_admin",
            Payload = JsonSerializer.Serialize(new
            {
                attachmentId = result.Document.AttachmentId,
                filename = result.Document.Filename,
                sha256 = result.Document.Sha256,
            }),
            CreatedAt = now,
            CreatedBy = userId,
        }, ct).ConfigureAwait(false);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        return (result, null);
    }
}

/// <summary>Datos del archivo externo que el admin carga como consolidado (HU #12158, AC3).</summary>
public sealed record CargarConsolidadoExternoInput(
    string Filename,
    string Mimetype,
    long SizeBytes,
    Stream Content);

/// <summary>
/// "Cargar consolidado externo" (HU #12158, AC3): registra un PDF provisto por el admin como el
/// expediente consolidado del trámite, con <c>Source="user"</c>. A partir de ahí prevalece sobre
/// cualquier regeneración automática futura (protección de <see cref="GenerarConsolidadoHandler"/>,
/// AC2) hasta que alguien lo reemplace con "Limpiar consolidado" (AC1) u otra carga. Es una acción
/// explícita del admin: reemplaza CUALQUIER consolidado vigente sin importar su <c>Source</c> actual,
/// igual que "Limpiar".
/// </summary>
public sealed class CargarConsolidadoExternoHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    /// <summary>Paridad con <see cref="AttachmentRules.MaxSizeBytes"/> (20 MB).</summary>
    public const long MaxSizeBytes = 20L * 1024 * 1024;

    private const string Tipo = "consolidado";

    public async Task<(ConsolidadoDocumentDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CargarConsolidadoExternoInput input,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (input?.Content is null || input.SizeBytes <= 0)
            return (null, "missing_file");
        if (!string.Equals(input.Mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return (null, "invalid_mime");
        if (input.SizeBytes > MaxSizeBytes)
            return (null, "file_too_large");

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Mismo guard de solo-lectura que GenerarConsolidadoHandler (Bug #11612 / migración V1→V2):
        // un trámite migrado en estado final conserva el expediente con el que el organismo lo
        // aprobó/anuló — ni siquiera la carga manual del admin puede reemplazarlo.
        if (instance.IsMigrated && TramiteEstado.EsFinal(instance.Status))
            return (null, "migrado_solo_lectura");

        var filename = string.IsNullOrWhiteSpace(input.Filename) ? "consolidado.pdf" : input.Filename.Trim();
        var stored = await storage.SaveAsync(id, Tipo, filename, input.Content, ct).ConfigureAwait(false);

        // Acción explícita del admin (igual que "Limpiar", AC1): reemplaza CUALQUIER consolidado
        // vigente, sin mirar su Source actual.
        foreach (var prev in instance.Attachments
            .Where(a => string.Equals(a.Tipo, Tipo, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            storage.Delete(prev.StoragePath);
            instance.Attachments.Remove(prev);
            repo.RemoveAttachment(prev);
        }

        var now = DateTimeOffset.UtcNow;
        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = Tipo,
            Filename = filename,
            Mimetype = "application/pdf",
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "user",
            UploadedAt = now,
            UploadedBy = userId,
        };
        instance.Attachments.Add(attachment);
        // PK store-generated con Id ya seteado: Added explícito para forzar INSERT (mismo patrón que
        // UploadAttachmentHandler / GenerarConsolidadoHandler).
        repo.Add(attachment);

        // Botón único (Feature #10701, HU #10860): un consolidado cargado a mano SÍ refleja el
        // expediente vigente — evita que la próxima lectura sin `force` dispare una regeneración
        // encima de lo que el admin acaba de cargar (aunque la protección de Source="user" en
        // GenerarConsolidadoHandler ya lo cubriría igual).
        instance.ConsolidadoWizardVigente = true;

        // AC4 — trazabilidad.
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "consolidado_cargado_admin",
            Payload = JsonSerializer.Serialize(new
            {
                attachmentId = attachment.Id,
                filename = attachment.Filename,
                sha256 = attachment.Sha256,
            }),
            CreatedAt = now,
            CreatedBy = userId,
        }, ct).ConfigureAwait(false);

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        return (new ConsolidadoDocumentDto(attachment.Id, attachment.Tipo, attachment.Filename, attachment.Sha256), null);
    }
}

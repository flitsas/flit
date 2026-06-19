using System.Text.Json;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Contrato congelado consumido por el frontend (Slice 3 — adjuntos).
/// </summary>
public sealed record AttachmentDto(
    Guid Id,
    string Tipo,
    string Filename,
    string Mimetype,
    long SizeBytes,
    string Sha256,
    string Source,
    DateTimeOffset UploadedAt);

public sealed record AttachmentsResponse(IReadOnlyList<AttachmentDto> Attachments);

/// <summary>Metadatos del archivo subido (el endpoint los extrae del <c>IFormFile</c>).</summary>
public sealed record UploadAttachmentInput(
    string Tipo,
    string Filename,
    string Mimetype,
    long SizeBytes,
    Stream Content);

/// <summary>Reglas de validación de adjuntos (compartidas con el contrato del front).</summary>
public static class AttachmentRules
{
    public const long MaxSizeBytes = 20L * 1024 * 1024; // 20 MB

    public static readonly IReadOnlySet<string> ValidTipos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "factura", "aduana", "impronta", "soat", "certificado_ambiental", "compraventa",
        "acta_remate", "oficio_judicial", "declaracion_aduana", "comprobante_derechos",
        "acta_entrega", "otro",
    };

    public static readonly IReadOnlySet<string> ValidMimetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/png", "image/webp",
    };
}

/// <summary>
/// Sube un adjunto: valida tipo/mime/size, persiste el binario vía <see cref="IAttachmentStorage"/>,
/// crea la entidad y auto-marca el ítem de checklist cuyo <c>docTipo</c> coincide con el tipo subido.
/// Solo en estado <c>draft</c> (inmutabilidad, paridad actores/field-values).
/// </summary>
public sealed class UploadAttachmentHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    public async Task<(AttachmentDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        UploadAttachmentInput input,
        Guid? uploadedBy = null,
        CancellationToken ct = default)
    {
        if (input.Content is null || input.SizeBytes <= 0)
            return (null, "missing_file");
        if (string.IsNullOrWhiteSpace(input.Tipo) || !AttachmentRules.ValidTipos.Contains(input.Tipo))
            return (null, "invalid_tipo");
        if (string.IsNullOrWhiteSpace(input.Mimetype) || !AttachmentRules.ValidMimetypes.Contains(input.Mimetype))
            return (null, "invalid_mime");
        if (input.SizeBytes > AttachmentRules.MaxSizeBytes)
            return (null, "file_too_large");

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != ProcedureInstanceStatus.Draft)
            return (null, "not_draft");

        var tipo = input.Tipo.Trim().ToLowerInvariant();
        var stored = await storage.SaveAsync(id, tipo, input.Filename ?? "file", input.Content, ct);

        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = tipo,
            Filename = string.IsNullOrWhiteSpace(input.Filename) ? "file" : input.Filename.Trim(),
            Mimetype = input.Mimetype.Trim().ToLowerInvariant(),
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedBy = uploadedBy,
        };
        instance.Attachments.Add(attachment);
        // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
        // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
        repo.Add(attachment);

        // Auto-marca: si la tipología tiene un ítem cuyo docTipo == tipo subido, lo marca en checklist_estado.
        ChecklistEstadoJson.AutoMark(instance, tipo);

        // Instancia trackeada (GetByIdWithAttachmentsAsync sin AsNoTracking): el change tracker
        // detecta el attachment nuevo (INSERT) y el cambio de checklist_estado. NO se llama
        // Update(): marcaría el hijo nuevo como Modified → UPDATE de 0 filas en vez de INSERT.
        await repo.SaveChangesAsync(ct);

        return (ToDto(attachment), null);
    }

    internal static AttachmentDto ToDto(ProcedureInstanceAttachment a) =>
        new(a.Id, a.Tipo, a.Filename, a.Mimetype, a.SizeBytes, a.Sha256, a.Source, a.UploadedAt);
}

/// <summary>Lista los adjuntos de una instancia.</summary>
public sealed class ListAttachmentsHandler(IProcedureInstanceRepository repo)
{
    public async Task<(AttachmentsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var dtos = instance.Attachments
            .OrderBy(a => a.UploadedAt)
            .Select(UploadAttachmentHandler.ToDto)
            .ToList();
        return (new AttachmentsResponse(dtos), null);
    }
}

/// <summary>Borra un adjunto (FS + fila). Solo en <c>draft</c>.</summary>
public sealed class DeleteAttachmentHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    public async Task<string?> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid attachmentId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return "not_found";
        if (instance.Status != ProcedureInstanceStatus.Draft)
            return "not_draft";

        var attachment = instance.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null)
            return "attachment_not_found";

        storage.Delete(attachment.StoragePath);
        instance.Attachments.Remove(attachment);
        repo.RemoveAttachment(attachment);

        await repo.SaveChangesAsync(ct);
        return null;
    }
}

/// <summary>Helpers para leer/escribir el jsonb <c>checklist_estado</c> (Dictionary&lt;string,bool&gt;).</summary>
internal static class ChecklistEstadoJson
{
    public static Dictionary<string, bool> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, bool>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new Dictionary<string, bool>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, bool>();
        }
    }

    /// <summary>
    /// Marca en <c>checklist_estado</c> todos los ítems de la tipología cuyo <c>DocTipo</c>
    /// coincida con el tipo de documento recién subido. Resuelve la tipología por
    /// <c>tipologia_codigo</c> o, en su defecto, por <c>modalidad_entrada</c>.
    /// </summary>
    public static void AutoMark(ProcedureInstance instance, string docTipo)
    {
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var tip = TramiteTipologiaCatalog.Get(codigo);
        if (tip is null)
            return;

        var estado = Parse(instance.ChecklistEstado);
        var changed = false;
        foreach (var item in tip.Checklist)
        {
            if (string.Equals(item.DocTipo, docTipo, StringComparison.OrdinalIgnoreCase)
                && (!estado.TryGetValue(item.Id, out var v) || !v))
            {
                estado[item.Id] = true;
                changed = true;
            }
        }

        if (changed)
            instance.ChecklistEstado = JsonSerializer.Serialize(estado);
    }
}

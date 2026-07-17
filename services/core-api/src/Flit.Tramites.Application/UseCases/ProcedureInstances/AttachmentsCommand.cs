using System.Text.Json;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;

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
        // Documentos del checklist de traspaso (antes sin DocTipo → el front subía con el `key`,
        // que no estaba en este set → 400 "tipo inválido").
        "rtm", "paz_salvo", "cedulas", "cert_tradicion",
        // Prenda / gravamen (IT-3, Feature #10585): un DocTipo por decisión que requiere soporte.
        "prenda_solicitud", "prenda_registro", "prenda_levantamiento",
        // HU #10604 (R19) / #10697 — paz y salvo RNMC. RNMC ya NO bloquea el envío al OT (la medida
        // correctiva es informativa): este adjunto queda como OPCIONAL informativo, no como requisito.
        "paz_salvo_rnmc",
    };

    public static readonly IReadOnlySet<string> ValidMimetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/png", "image/webp",
    };

    /// <summary>
    /// Valida tipo/mime/size de un adjunto. Devuelve el código de error (compartido con el contrato
    /// del front) o <c>null</c> si es válido. Reutilizado por la subida multipart y el flujo presigned.
    /// </summary>
    public static string? Validate(string? tipo, string? mimetype, long sizeBytes)
    {
        if (sizeBytes <= 0)
            return "missing_file";
        if (string.IsNullOrWhiteSpace(tipo) || !ValidTipos.Contains(tipo))
            return "invalid_tipo";
        if (string.IsNullOrWhiteSpace(mimetype) || !ValidMimetypes.Contains(mimetype))
            return "invalid_mime";
        if (sizeBytes > MaxSizeBytes)
            return "file_too_large";
        return null;
    }
}

/// <summary>Datos para abrir una subida directa a S3 (presigned): metadata sin binario.</summary>
public sealed record PresignAttachmentInput(string Tipo, string Filename, string Mimetype, long SizeBytes);

/// <summary>Presigned POST policy + id de almacenamiento que el cliente usa para subir a S3.</summary>
public sealed record PresignAttachmentResponse(
    string StoragePath,
    string Url,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>Metadata de un adjunto ya subido directo a S3 (flujo presigned), sin binario.</summary>
public sealed record RegisterAttachmentInput(
    string Tipo,
    string Filename,
    string Mimetype,
    long SizeBytes,
    string Sha256,
    string StoragePath);

/// <summary>
/// Sube un adjunto: valida tipo/mime/size, persiste el binario vía <see cref="IAttachmentStorage"/>,
/// crea la entidad y auto-marca el ítem de checklist cuyo <c>docTipo</c> coincide con el tipo subido.
/// Solo en estado <c>draft</c> (inmutabilidad, paridad actores/field-values).
/// </summary>
public sealed class UploadAttachmentHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage,
    AttachmentValidator? validator = null)
{
    public async Task<(AttachmentDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        UploadAttachmentInput input,
        Guid? uploadedBy = null,
        CancellationToken ct = default)
    {
        if (input.Content is null)
            return (null, "missing_file");
        var validationError = validator is not null
            ? await validator.ValidateAsync(input.Tipo, input.Mimetype, input.SizeBytes, ct)
            : AttachmentRules.Validate(input.Tipo, input.Mimetype, input.SizeBytes);
        if (validationError is not null)
            return (null, validationError);

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != TramiteEstado.Borrador)
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

/// <summary>
/// Abre una subida directa a S3: valida tipo/mime/size + estado draft y devuelve una presigned POST
/// policy para que el cliente suba el binario SIN que pase por el request del API (PDFs grandes).
/// No crea la fila del adjunto: eso ocurre al registrar la metadata (<see cref="RegisterAttachmentHandler"/>)
/// una vez subido el binario.
/// </summary>
public sealed class PresignAttachmentHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage,
    AttachmentValidator? validator = null)
{
    public async Task<(PresignAttachmentResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        PresignAttachmentInput input,
        CancellationToken ct = default)
    {
        var validationError = validator is not null
            ? await validator.ValidateAsync(input.Tipo, input.Mimetype, input.SizeBytes, ct)
            : AttachmentRules.Validate(input.Tipo, input.Mimetype, input.SizeBytes);
        if (validationError is not null)
            return (null, validationError);

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != TramiteEstado.Borrador)
            return (null, "not_draft");

        var tipo = input.Tipo.Trim().ToLowerInvariant();
        var filename = string.IsNullOrWhiteSpace(input.Filename) ? "file" : input.Filename.Trim();
        var presigned = await storage.CreatePresignedUploadAsync(id, tipo, filename, ct);

        return (new PresignAttachmentResponse(presigned.StoragePath, presigned.Url, presigned.Fields), null);
    }
}

/// <summary>
/// Registra la metadata de un adjunto YA subido directo a S3 (flujo presigned). No recibe el binario:
/// confía en el <c>StoragePath</c> (id del file-manager) y en el <c>Sha256</c>/<c>SizeBytes</c> que el
/// cliente calculó. Aplica las mismas validaciones, estado draft y auto-marca de checklist que la
/// subida multipart.
/// </summary>
public sealed class RegisterAttachmentHandler(
    IProcedureInstanceRepository repo,
    AttachmentValidator? validator = null)
{
    public async Task<(AttachmentDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        RegisterAttachmentInput input,
        Guid? uploadedBy = null,
        CancellationToken ct = default)
    {
        var validationError = validator is not null
            ? await validator.ValidateAsync(input.Tipo, input.Mimetype, input.SizeBytes, ct)
            : AttachmentRules.Validate(input.Tipo, input.Mimetype, input.SizeBytes);
        if (validationError is not null)
            return (null, validationError);
        if (string.IsNullOrWhiteSpace(input.StoragePath))
            return (null, "missing_storage_path");
        if (string.IsNullOrWhiteSpace(input.Sha256))
            return (null, "missing_sha256");

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != TramiteEstado.Borrador)
            return (null, "not_draft");

        var tipo = input.Tipo.Trim().ToLowerInvariant();
        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = tipo,
            Filename = string.IsNullOrWhiteSpace(input.Filename) ? "file" : input.Filename.Trim(),
            Mimetype = input.Mimetype.Trim().ToLowerInvariant(),
            SizeBytes = input.SizeBytes,
            Sha256 = input.Sha256.Trim().ToLowerInvariant(),
            StoragePath = input.StoragePath.Trim(),
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedBy = uploadedBy,
        };
        instance.Attachments.Add(attachment);
        // PK store-generated con Id ya seteado: Added explícito para forzar INSERT (igual que la subida).
        repo.Add(attachment);

        ChecklistEstadoJson.AutoMark(instance, tipo);
        await repo.SaveChangesAsync(ct);

        return (UploadAttachmentHandler.ToDto(attachment), null);
    }
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

/// <summary>Contenido binario de un adjunto, listo para streamear en la respuesta.</summary>
public sealed record AttachmentDownload(Stream Content, string Mimetype, string Filename);

/// <summary>
/// Resuelve un adjunto de la instancia/tenant y abre su binario para descarga (DF-1). Sirve docs
/// subidos por el usuario y los generados por el sistema (FUR, certificado de identidad). No exige
/// <c>draft</c> (descargable en cualquier estado). Errores: instancia o adjunto inexistente → not_found;
/// binario perdido en disco → file_missing.
/// </summary>
public sealed class DownloadAttachmentHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    public async Task<(AttachmentDownload? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid attachmentId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var attachment = instance.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null)
            return (null, "not_found");

        var stream = await storage.OpenReadAsync(attachment.StoragePath, ct);
        if (stream is null)
            return (null, "file_missing");

        var filename = string.IsNullOrWhiteSpace(attachment.Filename) ? attachment.Tipo : attachment.Filename;
        var mime = string.IsNullOrWhiteSpace(attachment.Mimetype) ? "application/octet-stream" : attachment.Mimetype;
        return (new AttachmentDownload(stream, mime, filename), null);
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
        if (instance.Status != TramiteEstado.Borrador)
            return "not_draft";

        var attachment = instance.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null)
            return "attachment_not_found";

        var tipo = attachment.Tipo;
        storage.Delete(attachment.StoragePath);
        instance.Attachments.Remove(attachment);
        repo.RemoveAttachment(attachment);

        // Simétrico al AutoMark de la subida: si ya no queda ningún adjunto de ese tipo, se
        // des-marca el ítem de checklist que se había auto-marcado. Sin esto, borrar un documento
        // dejaba el ítem "satisfecho" y el gate seguía pasando sin el documento.
        ChecklistEstadoJson.AutoUnmark(instance, tipo);

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

    /// <summary>
    /// Inverso de <see cref="AutoMark"/>: al borrar un adjunto, des-marca en <c>checklist_estado</c>
    /// los ítems cuyo <c>DocTipo</c> coincida con el tipo borrado, SIEMPRE que no quede ningún otro
    /// adjunto de ese tipo. Debe llamarse DESPUÉS de quitar el adjunto de <c>instance.Attachments</c>.
    /// </summary>
    public static void AutoUnmark(ProcedureInstance instance, string docTipo)
    {
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var tip = TramiteTipologiaCatalog.Get(codigo);
        if (tip is null)
            return;

        // Si todavía queda otro adjunto del mismo tipo, el ítem sigue satisfecho por documento.
        if (instance.Attachments.Any(a => string.Equals(a.Tipo, docTipo, StringComparison.OrdinalIgnoreCase)))
            return;

        var estado = Parse(instance.ChecklistEstado);
        var changed = false;
        foreach (var item in tip.Checklist)
        {
            if (string.Equals(item.DocTipo, docTipo, StringComparison.OrdinalIgnoreCase)
                && estado.Remove(item.Id))
            {
                changed = true;
            }
        }

        if (changed)
            instance.ChecklistEstado = JsonSerializer.Serialize(estado);
    }
}

using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Genera el expediente consolidado maestro desde la tabla maestra de adjuntos de la instancia
/// (Feature #10701 / HU #10706). A diferencia de <see cref="GenerarConsolidadoHandler"/>:
/// <list type="bullet">
///   <item>Sin gate FUR obligatorio.</item>
///   <item>Sin gate de completitud documental.</item>
///   <item>Excluye consolidados previos (tipo <c>consolidado</c> y <c>consolidado_maestro</c>).</item>
///   <item>Orden por <see cref="ConsolidadoOrderingResolver"/> (modalidad) con fallback genérico.</item>
///   <item>Tipo del adjunto resultante: <c>consolidado_maestro</c>.</item>
/// </list>
/// Idempotente: re-generar reemplaza el adjunto previo de tipo <c>consolidado_maestro</c>.
/// </summary>
public sealed class GenerarConsolidadoMaestroHandler(
    IProcedureInstanceRepository repo,
    IExpedienteConsolidadoMerger merger,
    IAttachmentStorage storage)
{
    private static readonly HashSet<string> ConsolidadoTipos = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "consolidado_maestro",
    };

    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Excluir todos los consolidados previos del maestro y del wizard para no fusionarlos.
        var fuentes = instance.Attachments
            .Where(a => !ConsolidadoTipos.Contains(a.Tipo))
            .ToList();

        var ordered = ConsolidadoOrderingResolver.Select(fuentes, instance.ModalidadEntrada);
        if (ordered.Count == 0)
            return (null, "sin_adjuntos");

        var pdfParts = new List<byte[]>(ordered.Count);
        foreach (var attachment in ordered)
        {
            await using var stream = await storage.OpenReadAsync(attachment.StoragePath, ct);
            if (stream is null)
                return (null, "adjunto_no_disponible");

            var bytes = await ReadAllBytesAsync(stream, ct);
            try
            {
                pdfParts.Add(merger.NormalizeToPdf(bytes, attachment.Mimetype));
            }
            catch (NotSupportedException)
            {
                return (null, "mimetype_no_soportado");
            }
        }

        var merged = merger.Merge(pdfParts);
        var now = DateTimeOffset.UtcNow;
        var filename = $"consolidado_maestro_{SafeRef(instance.ReferenceNumber)}.pdf";
        const string tipoMaestro = "consolidado_maestro";
        var doc = new GeneratedDocument(tipoMaestro, filename, "application/pdf", merged);

        // Idempotencia: eliminar maestro previo antes de persistir el nuevo.
        foreach (var prev in instance.Attachments
            .Where(a => string.Equals(a.Tipo, tipoMaestro, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            storage.Delete(prev.StoragePath);
            instance.Attachments.Remove(prev);
            repo.RemoveAttachment(prev);
        }

        var stored = await storage.SaveAsync(id, doc.Tipo, doc.Filename, new MemoryStream(doc.Content), ct);
        var newAttachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = doc.Tipo,
            Filename = doc.Filename,
            Mimetype = doc.Mimetype,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.StoragePath,
            Source = "system",
            UploadedAt = now,
        };
        instance.Attachments.Add(newAttachment);
        repo.Add(newAttachment);

        var evento = new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "consolidado_maestro_generado",
            Payload = JsonSerializer.Serialize(new
            {
                filename = doc.Filename,
                sha256 = stored.Sha256,
                paginas_incluidas = ordered.Select(a => a.Tipo).ToList(),
                generado_at = now,
            }),
            CreatedAt = now,
        };
        instance.Events.Add(evento);
        repo.Add(evento);

        await repo.SaveChangesAsync(ct);

        var dto = new ConsolidadoDocumentDto(newAttachment.Id, doc.Tipo, doc.Filename, stored.Sha256);
        return (new GenerarConsolidadoResult(dto), null);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static string SafeRef(string reference) =>
        string.Concat(reference.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
}

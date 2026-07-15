using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record ConsolidadoDocumentDto(Guid AttachmentId, string Tipo, string Filename, string Sha256);

/// <param name="Regenerado">
/// <c>true</c> si el PDF se (re)construyó en esta petición; <c>false</c> si se reutilizó el
/// consolidado maestro vigente sin regenerarlo (Feature #10701, botón único). El consolidado del
/// wizard siempre regenera, así que su default es <c>true</c>.
/// </param>
public sealed record GenerarConsolidadoResult(ConsolidadoDocumentDto Document, bool Regenerado = true);

/// <summary>
/// Genera el expediente consolidado de matrícula inicial: fusiona el FUR, el certificado de
/// identidad y los demás adjuntos del trámite en un único PDF (tipo <c>consolidado</c>).
/// Idempotente: re-generar reemplaza el adjunto previo. Requiere FUR generado y documentos
/// obligatorios completos (misma regla que radicación).
/// </summary>
public sealed class GenerarConsolidadoHandler(
    IProcedureInstanceRepository repo,
    IExpedienteConsolidadoMerger merger,
    IAttachmentStorage storage,
    ChecklistMatrixCompleteness? matrixCompleteness = null)
{
    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // Grafo de checklist (incluye Attachments): permite que el gate "gestor manda" (matriz +
        // reglas condicionales) resuelva la completitud con el mismo contexto que radicación.
        var instance = await repo.GetByIdWithChecklistGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // HU #10522 (RF27/41) — el consolidado ya no rechaza otras modalidades: matrícula y traspaso
        // conservan su orden; cualquier otra modalidad usa el orden genérico (ver ConsolidadoOrderingResolver).

        if (!instance.Attachments.Any(a => string.Equals(a.Tipo, "fur", StringComparison.OrdinalIgnoreCase)))
            return (null, SubmitGate.FurRequerido);

        // HU #10522 (RF17/RF22) — el gestor manda la completitud documental si tiene matriz (flag ON).
        var docsCompletos = matrixCompleteness is null
            ? null
            : await matrixCompleteness.TryComputeCompletoAsync(instance, tenantId, ct);
        if (!(docsCompletos ?? DocumentosObligatoriosCompletos(instance)))
            return (null, SubmitGate.DocumentosIncompletos);

        var ordered = ConsolidadoOrderingResolver.Select(instance.Attachments, instance.ModalidadEntrada);
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
        var filename = $"consolidado_{SafeRef(instance.ReferenceNumber)}.pdf";
        var doc = new GeneratedDocument("consolidado", filename, "application/pdf", merged);

        foreach (var prev in instance.Attachments.Where(a =>
                     string.Equals(a.Tipo, doc.Tipo, StringComparison.OrdinalIgnoreCase)).ToList())
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
            Tipo = "consolidado_generado",
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

    private static bool DocumentosObligatoriosCompletos(ProcedureInstance instance)
    {
        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var computed = ChecklistEngine.Compute(codigo, manual, docTipos);
        return computed?.Completo ?? true;
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

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
    IAttachmentStorage storage,
    IOtConfiguredDocumentOrderProvider? otOrderProvider = null)
{
    private static readonly HashSet<string> ConsolidadoTipos = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "consolidado_maestro",
    };

    /// <param name="matrizPrecedencia">
    /// Orden resuelto de la matriz documental del tenant/OT (<c>ResolvedDocumentMatrixResolver</c> /
    /// <c>ot_document_precedence</c>, HU #10706 AC1). Lo resuelve y compone <c>AdminOtEndpoints</c>
    /// (la capa Application no referencia Admin). Si es null/vacío, se cae al orden por modalidad
    /// (<see cref="ConsolidadoOrderingResolver"/>) como respaldo.
    /// </param>
    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        IReadOnlyList<string>? matrizPrecedencia = null,
        CancellationToken ct = default)
    {
        // Graph con FieldValues (HU #10857): necesarios para los datos de la portada (placa, secretaría).
        var instance = await repo.GetByIdWithChecklistGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Botón único (Feature #10701): si la marca de la BD dice que el consolidado maestro está
        // vigente y el adjunto sigue existiendo, se REUTILIZA sin regenerar (el PDF ya refleja el
        // estado actual del expediente). La marca la baja a false cualquier cambio importante
        // (transición de estado o adjuntar la LT), forzando la regeneración en la próxima petición.
        var vigente = instance.Attachments
            .FirstOrDefault(a => string.Equals(a.Tipo, "consolidado_maestro", StringComparison.OrdinalIgnoreCase));
        if (instance.ConsolidadoMaestroVigente && vigente is not null)
        {
            var vigenteDto = new ConsolidadoDocumentDto(vigente.Id, vigente.Tipo, vigente.Filename, vigente.Sha256);
            return (new GenerarConsolidadoResult(vigenteDto, Regenerado: false), null);
        }

        // Fuente = tabla maestra de adjuntos de la instancia (AC2), excluyendo solo los consolidados
        // previos (wizard y maestro) para no fusionarlos consigo mismos. Los resolvers de orden
        // descartan además la evidencia biométrica (tipos "biometric_*", que no forma parte del
        // expediente legal) y los MIME no fusionables (solo pdf/jpeg/png/webp llegan al merge); los
        // adjuntos subidos ya están acotados a esos MIME por AttachmentRules.
        var fuentes = instance.Attachments
            .Where(a => !ConsolidadoTipos.Contains(a.Tipo))
            .ToList();

        // HU #11184 — si el OT configuró la prelación de este tipo de trámite, manda ella, y sin
        // cabecera fija: el FUR y los demás generados ocupan la posición que el organismo eligió.
        // Resolverla aquí dentro hace que el envío por el canal de radicación (Quipux) use el mismo
        // orden que la consola, sin tener que componerlo en cada llamador (AC5).
        var configurado = await ResolverOrdenOtAsync(instance, ct).ConfigureAwait(false);

        // AC1 (HU #10706): sin configuración del OT, orden por la matriz resuelta (documentos no
        // clasificados al final como "Anexos"); si tampoco hay matriz, respaldo por modalidad.
        var ordered = configurado.Count > 0
            ? GenericConsolidadoOrdering.SelectByPrecedence(
                fuentes, ConsolidadoDocumentCodeMap.ToPrecedence(configurado))
            : matrizPrecedencia is { Count: > 0 }
                ? GenericConsolidadoOrdering.SelectByResolvedMatrix(fuentes, matrizPrecedencia)
                : ConsolidadoOrderingResolver.Select(fuentes, instance.ModalidadEntrada);
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
                // Defensa en profundidad: el ordering ya filtró los MIME no fusionables, pero si un
                // documento generado trajera otro MIME, se responde 409 en vez de propagar un 500.
                return (null, "mimetype_no_soportado");
            }
        }

        // HU #10857 — expediente maestro con portada institucional (primera página).
        var mergeRequest = new MergeRequest(
            Parts: ordered.Zip(pdfParts, (a, pdf) => new MergePart(pdf, DocumentLabels.Display(a.Tipo))).ToList(),
            Cover: ExpedienteCoverInfoBuilder.FromInstance(instance),
            EstadoTramite: instance.Status);
        var merged = merger.Compose(mergeRequest);
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

        // Marca de vigencia (Feature #10701): el consolidado recién generado refleja el estado
        // actual del expediente. La bajarán a false las transiciones de estado y el adjuntar LT.
        instance.ConsolidadoMaestroVigente = true;

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

    /// <summary>
    /// Orden configurado por el OT para el tipo de trámite, o vacío si no configuró nada. Un fallo
    /// leyéndolo no puede dejar al organismo sin expediente: se cae al comportamiento anterior.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolverOrdenOtAsync(ProcedureInstance instance, CancellationToken ct)
    {
        if (otOrderProvider is null)
            return [];

        try
        {
            return await otOrderProvider
                .GetConfiguredOrderAsync(instance.ProcedureTypeId, instance.TransitOfficeId, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
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

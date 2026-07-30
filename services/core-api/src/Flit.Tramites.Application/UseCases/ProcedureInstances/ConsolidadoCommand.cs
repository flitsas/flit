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
/// <param name="AvisosCascada">
/// HU #11050 (AC3) — documentos de la cascada que NO se pudieron generar, con el motivo
/// (<c>"mandato: organismo_requerido"</c>). Antes el fallo de la cascada se descartaba en silencio: el
/// consolidado salía sin ese documento y el gestor no tenía forma de saber por qué. No bloquea: el
/// consolidado se entrega igual (misma decisión que la HU #11017 con los documentos obligatorios).
/// </param>
public sealed record GenerarConsolidadoResult(
    ConsolidadoDocumentDto Document,
    bool Regenerado = true,
    // HU #11017 - el consolidado ya NO se bloquea por documentos obligatorios faltantes: se genera y se
    // marca. `Incompleto` + `DocumentosFaltantes` permiten avisar al gestor de que falta, en vez de
    // negarle el documento sin explicacion.
    bool Incompleto = false,
    IReadOnlyList<string>? DocumentosFaltantes = null,
    IReadOnlyList<string>? AvisosCascada = null);

/// <summary>
/// Regenera los documentos "en caliente" del expediente del wizard (FUR + certificados generados)
/// para que salgan con fecha vigente antes de consolidar (HU #10860, cascada β de ADR-0032). Lo
/// implementa <c>GenerarFurHandler</c>; devuelve un código de error o null si la regeneración fue ok.
/// </summary>
public interface IExpedienteHotDocumentsRegenerator
{
    Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Genera la impronta del trámite cuando falta, para que el consolidado no obligue al gestor a
/// producirla antes (HU #11017). Lo implementa <c>GenerarImprontaAttachmentHandler</c>; devuelve el
/// código de error del proveedor o <c>null</c> si se generó. Best-effort: el consolidado sigue.
/// </summary>
public interface IImprontaAutoGenerator
{
    Task<string?> TryGenerateAsync(Guid id, Guid tenantId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Genera el expediente consolidado: fusiona el FUR, el certificado de identidad y los demás adjuntos
/// del trámite en un único PDF (tipo <c>consolidado</c>). Idempotente: re-generar reemplaza el previo.
/// <para>Feature #11066 — no regenera el paquete documental en caliente (certificados, mandato, etc.):
/// esos se generan al Preparar. Solo produce el FUR aquí si aún no existe. Los documentos obligatorios
/// faltantes no impiden el consolidado: se genera marcado como incompleto.</para>
/// </summary>
public sealed class GenerarConsolidadoHandler(
    IProcedureInstanceRepository repo,
    IExpedienteConsolidadoMerger merger,
    IAttachmentStorage storage,
    ChecklistMatrixCompleteness? matrixCompleteness = null,
    IExpedienteHotDocumentsRegenerator? hotDocsRegenerator = null,
    IImprontaAutoGenerator? improntaGenerator = null)
{
    public Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default) =>
        HandleAsync(id, tenantId, userId: null, force: false, ct);

    /// <param name="userId">
    /// Operador que pide el consolidado. Necesario solo para generar la impronta en cascada (el
    /// proveedor la exige); sin él, la impronta faltante simplemente no se autogenera.
    /// </param>
    public Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? userId,
        CancellationToken ct = default) =>
        HandleAsync(id, tenantId, userId, force: false, ct);

    /// <param name="force">
    /// Feature #11066 — cuando es <c>true</c>, invalida el consolidado vigente y lo regenera desde cero
    /// (FUR en cascada, adjuntos, fusión), en lugar de servir el PDF cacheado.
    /// </param>
    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? userId,
        bool force,
        CancellationToken ct = default)
    {
        // Grafo de checklist (incluye Attachments): permite que el gate "gestor manda" (matriz +
        // reglas condicionales) resuelva la completitud con el mismo contexto que radicación.
        var instance = await repo.GetByIdWithChecklistGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Migración V1→V2 — un trámite migrado es una FOTO de solo lectura: NO se regenera el
        // consolidado. V1 ya trae su propio expediente consolidado (tipo 'consolidado', source=migration);
        // regenerarlo aquí lo reemplazaría por uno nuevo del sistema, perdiendo el histórico.
        //
        // Va ANTES del caché y de la regeneración en cascada de HU #10860 a propósito: un trámite
        // migrado llega con ConsolidadoWizardVigente en false (el default de la columna), así que si
        // este guard fuera después, la cascada regeneraría el FUR y los documentos en caliente —
        // exactamente lo que el modo foto existe para impedir.
        if (instance.IsMigrated)
            return (null, "migrado_solo_lectura");

        // HU #10860 (ADR-0032) — caché explícita del expediente del wizard: si está vigente y el
        // consolidado persistido existe, se sirve sin regenerar (espejo del maestro, Feature #10701).
        // Feature #11066 — `force=true` invalida y salta el atajo de caché para reconstruir desde cero.
        var consolidadoVigente = instance.Attachments
            .FirstOrDefault(a => string.Equals(a.Tipo, "consolidado", StringComparison.OrdinalIgnoreCase));
        if (force && (instance.ConsolidadoWizardVigente || consolidadoVigente is not null))
        {
            instance.InvalidarConsolidados();
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            instance = await ReloadAsync(id, tenantId, instance, ct).ConfigureAwait(false);
            consolidadoVigente = instance.Attachments
                .FirstOrDefault(a => string.Equals(a.Tipo, "consolidado", StringComparison.OrdinalIgnoreCase));
        }

        if (!force && instance.ConsolidadoWizardVigente && consolidadoVigente is not null)
        {
            var vigenteDto = new ConsolidadoDocumentDto(
                consolidadoVigente.Id, consolidadoVigente.Tipo, consolidadoVigente.Filename, consolidadoVigente.Sha256);
            return (new GenerarConsolidadoResult(vigenteDto, Regenerado: false), null);
        }

        // Feature #11066 — el consolidado SOLO fusiona el expediente ya persistido.
        // NO regenera certificados / mandato / compraventa / etc.: esos se generan en Preparar
        // (generarFur). Solo si falta el FUR se produce aquí (sin eso no hay qué consolidar).

        // HU #10522 (RF27/41) — el consolidado ya no rechaza otras modalidades: matrícula y traspaso
        // conservan su orden; cualquier otra modalidad usa el orden genérico (ver ConsolidadoOrderingResolver).

        // HU #11017 — cascada: si falta el FUR se genera aquí, en vez de devolver `fur_requerido` y
        // dejar al gestor adivinando el orden correcto. Si aun así no aparece, se responde con el
        // motivo REAL del generador (p. ej. organismo_requerido) y no con "falta el FUR".
        if (!TieneFur(instance))
        {
            if (hotDocsRegenerator is null)
                return (null, SubmitGate.FurRequerido);

            string? errorFur;
            try
            {
                errorFur = await hotDocsRegenerator.RegenerateHotDocumentsAsync(id, tenantId, ct);
                instance = await ReloadAsync(id, tenantId, instance, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return (null, "storage_unavailable");
            }

            if (!TieneFur(instance))
                return (null, errorFur ?? SubmitGate.FurRequerido);
        }

        // HU #11017 — impronta en cascada. Best-effort y solo con usuario conocido: depende del RUNT
        // (Kyverum) y de que el trámite esté en borrador, así que su fallo NO impide el consolidado.
        if (improntaGenerator is not null && userId is { } operador && !TieneImpronta(instance))
        {
            await improntaGenerator.TryGenerateAsync(id, tenantId, operador, ct).ConfigureAwait(false);
            instance = await ReloadAsync(id, tenantId, instance, ct).ConfigureAwait(false);
        }

        // HU #11017 — los documentos obligatorios faltantes YA NO bloquean (antes: documentos_incompletos).
        // El consolidado se genera igual y se devuelve MARCADO, con la lista de lo que falta, para que el
        // gestor lo sepa antes de que el organismo se lo rechace.
        var checklist = matrixCompleteness is null
            ? null
            : await matrixCompleteness.TryComputeAsync(instance, tenantId, ct);
        var faltantes = checklist?.FaltanObligatorios ?? FaltantesObligatorios(instance);

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

        // HU #10857 — expediente con portada institucional (primera página) para todos los tipos.
        var mergeRequest = new MergeRequest(
            Parts: ordered.Zip(pdfParts, (a, pdf) => new MergePart(pdf, DocumentLabels.Display(a.Tipo))).ToList(),
            Cover: ExpedienteCoverInfoBuilder.FromInstance(instance),
            EstadoTramite: instance.Status);
        var merged = merger.Compose(mergeRequest);
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

        StoredFile stored;
        try
        {
            stored = await storage.SaveAsync(id, doc.Tipo, doc.Filename, new MemoryStream(doc.Content), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return (null, "storage_unavailable");
        }

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

        // HU #10860 — el consolidado recién generado refleja el expediente actual: marca vigente.
        instance.ConsolidadoWizardVigente = true;

        await repo.SaveChangesAsync(ct);

        var dto = new ConsolidadoDocumentDto(newAttachment.Id, doc.Tipo, doc.Filename, stored.Sha256);
        return (new GenerarConsolidadoResult(
            dto, Regenerado: true, Incompleto: faltantes.Count > 0, DocumentosFaltantes: faltantes), null);
    }

    /// <summary>
    /// Relee la instancia con el rastreo LIMPIO tras un paso de la cascada (HU #11029/#11034). Es
    /// obligatorio después de cualquier handler que guarde sobre la misma instancia: deja atrás los
    /// adjuntos borrados que siguen colgando de las colecciones ya cargadas y, sobre todo, recoge el
    /// <c>row_version</c> nuevo —token de concurrencia que un trigger sube en cada UPDATE—, sin el cual
    /// el guardado final afecta 0 filas y revienta con DbUpdateConcurrencyException.
    /// </summary>
    private async Task<ProcedureInstance> ReloadAsync(
        Guid id, Guid tenantId, ProcedureInstance fallback, CancellationToken ct)
    {
        repo.ResetTracking();
        return await repo.GetByIdWithChecklistGraphAsync(id, tenantId, ct).ConfigureAwait(false) ?? fallback;
    }

    private static bool TieneFur(ProcedureInstance instance) =>
        instance.Attachments.Any(a => string.Equals(a.Tipo, "fur", StringComparison.OrdinalIgnoreCase));

    private static bool TieneImpronta(ProcedureInstance instance) =>
        instance.Attachments.Any(a => a.Tipo.StartsWith("impronta", StringComparison.OrdinalIgnoreCase));

    /// <summary>Tipos obligatorios que faltan según el checklist del catálogo (HU #11017).</summary>
    private static IReadOnlyList<string> FaltantesObligatorios(ProcedureInstance instance)
    {
        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var computed = ChecklistEngine.Compute(codigo, manual, docTipos);
        return computed?.FaltanObligatorios ?? [];
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

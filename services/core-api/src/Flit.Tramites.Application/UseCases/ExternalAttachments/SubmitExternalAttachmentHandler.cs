using System.Security.Cryptography;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ExternalAttachments;

/// <summary>Archivo recibido del cliente externo (el endpoint ya lo leyó completo, hasta el tope del contrato).</summary>
public sealed record ExternalAttachmentFile(string FileName, string ContentType, byte[] Bytes);

/// <summary>Cuerpo de la respuesta 201/200 (contrato v3.2 §7).</summary>
public sealed record ExternalAttachmentReceipt(
    Guid AdjuntoId, string Tipo, string Sha256, Guid? ReemplazoDe, bool EnMatriz, bool PagadoMarcado);

public enum SubmitExternalAttachmentStatus
{
    /// <summary>Adjunto archivado (201).</summary>
    Created,

    /// <summary>Mismo contenido que el adjunto vigente de Flito: el adjunto no se toca (200); solo se pone la marca de pago si el estado marca y faltaba.</summary>
    Unchanged,

    /// <summary>Rechazado: ver <see cref="SubmitExternalAttachmentResult.Error"/>.</summary>
    Rejected,
}

/// <param name="Error">Código del contrato: <c>missing_file</c>, <c>invalid_tipo</c>, <c>invalid_mime</c>, <c>file_too_large</c>,
/// <c>procedure_not_found</c>, <c>not_allowed_in_state</c>, <c>attachment_exists</c>, <c>storage_unavailable</c>.</param>
/// <param name="Estado">Solo con <c>not_allowed_in_state</c>: estado actual del trámite.</param>
/// <param name="Terminal">Solo con <c>not_allowed_in_state</c>.</param>
/// <param name="TenantId">Compañía del trámite, si llegó a resolverse (para la bitácora de acceso).</param>
public sealed record SubmitExternalAttachmentResult(
    SubmitExternalAttachmentStatus Status,
    ExternalAttachmentReceipt? Receipt = null,
    string? Error = null,
    string? Estado = null,
    bool? Terminal = null,
    Guid? TenantId = null);

/// <summary>
/// HU #13263 (Feature #13261, Épica #12741) — <c>POST /api/v1/external/tramites/{id}/adjuntos</c>: el cliente externo
/// adjunta el comprobante de pago del impuesto departamental. Solo archiva: no corre OCR, no regenera el consolidado
/// y no toca el checklist (la casilla se satisface porque el checklist mira los adjuntos del trámite).
/// <para>Reglas: estado admitido; gana quien carga primero (un adjunto del gestor se conserva); el cliente externo
/// reemplaza el suyo anterior (<c>reemplazoDe</c>) y el mismo contenido es idempotente. Todo ocurre con la fila del
/// trámite bloqueada. El archivo se guarda en el almacenamiento ANTES de tocar la base y el binario viejo se retira
/// DESPUÉS de confirmar: si algo falla el cliente conserva lo que tenía.</para>
/// <para>HU #13264: en preasignación, asignado y rechazado con subsanación el 201 también deja
/// <c>impuesto_departamental_pagado = true</c> (<c>source = flito</c>) en la misma transacción; en entregado no.</para>
/// </summary>
public sealed class SubmitExternalAttachmentHandler(
    IExternalAttachmentRepository repository,
    IAttachmentStorage storage,
    IResolvedChecklistMatrixProvider matrixProvider)
{
    public async Task<SubmitExternalAttachmentResult> HandleAsync(
        Guid procedureId, string? tipo, ExternalAttachmentFile? file, CancellationToken cancellationToken = default)
    {
        var mime = ExternalAttachmentRules.NormalizeMimeType(file?.ContentType);
        var invalid = ExternalAttachmentRules.Validate(tipo, mime, file?.Bytes.LongLength ?? 0);
        if (invalid is not null)
        {
            return Rejected(invalid);
        }

        var bytes = file!.Bytes;
        var tipoCode = tipo!;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var result = await repository.RunLockedAsync(
            procedureId,
            tipoCode,
            (target, writer, ct) => DecideAsync(target, writer, tipoCode, file, mime, sha256, ct),
            cancellationToken).ConfigureAwait(false);

        // Ya confirmado: recién ahora se retira el binario del adjunto reemplazado.
        foreach (var path in result.RetiredPaths)
        {
            storage.Delete(path);
        }

        return result.Result;
    }

    private async Task<Decision> DecideAsync(
        ExternalAttachmentTarget? target,
        IExternalAttachmentWriter writer,
        string tipo,
        ExternalAttachmentFile file,
        string mime,
        string sha256,
        CancellationToken ct)
    {
        if (target is null)
        {
            return new(Rejected("procedure_not_found"), []);
        }

        var (allowed, terminal) = ExternalAttachmentRules.EvaluateState(target.Status, target.SubsanacionActiva);
        if (!allowed)
        {
            return new(new(SubmitExternalAttachmentStatus.Rejected, Error: "not_allowed_in_state",
                Estado: target.Status, Terminal: terminal, TenantId: target.TenantId), []);
        }

        // Gana quien carga primero: lo del gestor se conserva aunque el contenido sea idéntico.
        if (target.Vigentes.Any(a => !a.IsFromExternalClient))
        {
            return new(Rejected("attachment_exists", target.TenantId), []);
        }

        var enMatriz = (await matrixProvider.GetForAsync(target.ProcedureTypeId, target.TransitOfficeId, ct).ConfigureAwait(false))
            .Any(d => !d.EsGeneradoSistema && string.Equals(d.Codigo, tipo, StringComparison.OrdinalIgnoreCase));

        var propios = target.Vigentes;
        var igual = propios.FirstOrDefault(a => string.Equals(a.Sha256, sha256, StringComparison.OrdinalIgnoreCase));
        if (igual is not null)
        {
            // HU #13264 — el 200 no toca el adjunto, pero si el estado marca y la marca de flito falta (p. ej. el adjunto se
            // cargó en entregado y el trámite volvió a un estado editable) la pone, en la misma transacción bloqueada.
            var marcado = target.PagadoMarcado;
            if (!marcado && ExternalAttachmentRules.MarksPaid(target.Status, target.SubsanacionActiva))
            {
                await writer.MarkTaxPaidAsync(ct).ConfigureAwait(false);
                marcado = true;
            }

            return new(new(SubmitExternalAttachmentStatus.Unchanged,
                new ExternalAttachmentReceipt(igual.Id, tipo, sha256, null, enMatriz, marcado),
                TenantId: target.TenantId), []);
        }

        StoredFile stored;
        try
        {
            await using var content = new MemoryStream(file.Bytes, writable: false);
            stored = await storage.SaveAsync(target.ProcedureId, tipo, file.FileName, content, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // El almacenamiento no respondió: transitorio. No se escribió nada; el cliente conserva su adjunto.
            return new(Rejected("storage_unavailable", target.TenantId), []);
        }

        Guid adjuntoId;
        try
        {
            adjuntoId = await writer.ReplaceAsync(
                new NewExternalAttachment(tipo, file.FileName, mime, stored.SizeBytes, stored.Sha256, stored.StoragePath),
                [.. propios.Select(a => a.Id)],
                ct).ConfigureAwait(false);
        }
        catch (AttachmentFirstWinsConflictException)
        {
            // HU #13265 — el motor (DDL 131) vio un vigente del otro bando que esta lectura no veía: la transacción se
            // revierte entera (el adjunto propio anterior se conserva, con su binario) y el binario recién guardado,
            // que ninguna fila referencia, se retira.
            storage.Delete(stored.StoragePath);
            return new(Rejected("attachment_exists", target.TenantId), []);
        }

        // HU #13264 — en los estados editables el comprobante marca el impuesto como pagado (misma transacción que el
        // insert); en entregado se archiva sin tocar la marca y la respuesta refleja la que ya hubiera.
        var pagadoMarcado = target.PagadoMarcado;
        if (ExternalAttachmentRules.MarksPaid(target.Status, target.SubsanacionActiva))
        {
            await writer.MarkTaxPaidAsync(ct).ConfigureAwait(false);
            pagadoMarcado = true;
        }

        return new(new(SubmitExternalAttachmentStatus.Created,
                new ExternalAttachmentReceipt(adjuntoId, tipo, stored.Sha256, propios.Count > 0 ? propios[0].Id : null, enMatriz, pagadoMarcado),
                TenantId: target.TenantId),
            [.. propios.Select(a => a.StoragePath)]);
    }

    private static SubmitExternalAttachmentResult Rejected(string error, Guid? tenantId = null) =>
        new(SubmitExternalAttachmentStatus.Rejected, Error: error, TenantId: tenantId);

    private sealed record Decision(SubmitExternalAttachmentResult Result, IReadOnlyList<string> RetiredPaths);
}

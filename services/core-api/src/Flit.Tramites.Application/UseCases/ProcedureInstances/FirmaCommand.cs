using System.Text.Json;
using Flit.Tramites.Application.Signatures;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs (contrato Slice 7 — firma) ─────────────────────────────────────────

/// <summary>Vista (gestor autenticado) de una firma electrónica.</summary>
public sealed record SignatureDto(
    Guid Id,
    string Parte,
    string DocTipo,
    string Proveedor,
    string Estado,
    string? EnvelopeId,
    string? SignUrl,
    bool Firmada,
    DateTimeOffset SolicitadoAt,
    DateTimeOffset? FirmadoAt);

public sealed record SignaturesResponse(IReadOnlyList<SignatureDto> Signatures);

/// <summary>Entrada para solicitar la firma de una parte.</summary>
public sealed record SolicitarFirmaInput(string Parte, string? DocTipo);

public sealed record SimularFirmaResult(Guid Id, string Estado, string? PdfPath, string? Sha256);

// ── Handler: solicitar firma (autenticado) ───────────────────────────────────

/// <summary>
/// Crea (o reusa) la firma de una parte de la compraventa. Solo aplica a <c>traspaso_standard</c>
/// (matrícula no tiene compraventa) → <c>no_aplica</c> (409). Idempotente por (parte, doc_tipo):
/// si ya existe una firma activa (pendiente_envio | enviada | firmada) la devuelve sin duplicar.
/// Llama al proveedor MOCK para obtener envelopeId + signUrl y persiste la firma en estado
/// <c>enviada</c>.
/// </summary>
public sealed class SolicitarFirmaHandler(IProcedureInstanceRepository repo, ISignatureProvider provider)
{
    public async Task<(SignatureDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        SolicitarFirmaInput input,
        CancellationToken ct = default)
    {
        var parte = NormalizeParte(input.Parte);
        if (parte is null)
            return (null, "parte_invalida");

        var docTipo = string.IsNullOrWhiteSpace(input.DocTipo)
            ? SignatureDocTipos.Compraventa
            : input.DocTipo.Trim().ToLowerInvariant();

        var instance = await repo.GetByIdWithFurGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // La firma de la compraventa solo aplica a traspaso_standard.
        var codigo = instance.TypeCode;
        if (!string.Equals(codigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase))
            return (null, "no_aplica");

        // Idempotencia: una firma activa para (parte, doc) se reusa.
        var existing = instance.Signatures.FirstOrDefault(s =>
            string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.DocTipo, docTipo, StringComparison.OrdinalIgnoreCase)
            && s.Estado is SignatureEstados.PendienteEnvio or SignatureEstados.Enviada or SignatureEstados.Firmada);
        if (existing is not null)
            return (ToDto(existing), null);

        var now = DateTimeOffset.UtcNow;
        var nombre = instance.Actors
            .FirstOrDefault(a => string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase))?.FullName;

        var envelope = provider.CreateEnvelope(new SignatureRequest(id, parte, docTipo, nombre));

        var signature = new ProcedureInstanceSignature
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Parte = parte,
            DocTipo = docTipo,
            Proveedor = "mock",
            Estado = SignatureEstados.Enviada,
            EnvelopeId = envelope.EnvelopeId,
            Metadata = JsonSerializer.Serialize(new { signUrl = envelope.SignUrl }),
            SolicitadoAt = now,
            CreatedAt = now,
        };

        instance.Signatures.Add(signature);
        // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar INSERT.
        repo.Add(signature);
        await repo.SaveChangesAsync(ct);

        return (ToDto(signature), null);
    }

    private static string? NormalizeParte(string? parte)
    {
        if (string.IsNullOrWhiteSpace(parte))
            return null;
        var p = parte.Trim().ToLowerInvariant();
        return p is SignatureRules.ParteComprador or SignatureRules.ParteVendedor ? p : null;
    }

    internal static SignatureDto ToDto(ProcedureInstanceSignature s) =>
        new(s.Id, s.Parte, s.DocTipo, s.Proveedor, s.Estado, s.EnvelopeId, SignUrlFrom(s.Metadata),
            s.Estado == SignatureEstados.Firmada, s.SolicitadoAt, s.FirmadoAt);

    private static string? SignUrlFrom(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            return doc.RootElement.TryGetProperty("signUrl", out var v) ? v.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// ── Handler: listar firmas (autenticado) ──────────────────────────────────────

/// <summary>Lista las firmas electrónicas de una instancia (vista del gestor).</summary>
public sealed class ListFirmasHandler(IProcedureInstanceRepository repo)
{
    public async Task<(SignaturesResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithSignaturesAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var dtos = instance.Signatures
            .OrderBy(s => s.SolicitadoAt)
            .Select(SolicitarFirmaHandler.ToDto)
            .ToList();
        return (new SignaturesResponse(dtos), null);
    }
}

// ── Handler: simular firma (mock complete) ────────────────────────────────────

/// <summary>
/// Completa (firma) un sobre — acción MOCK que sustituye al callback del proveedor real. Solo desde
/// <c>enviada</c>; idempotente si ya está <c>firmada</c> (devuelve el estado actual). Marca
/// <c>firmada</c>, <c>firmado_at</c>, y registra un pdfPath + sha256 sintéticos. La lógica vive en
/// el handler para reusarse desde admin (Part A) y desde el portal público (Part B).
///
/// <para>HU #14175 — al transicionar a <c>firmada</c> registra UNA entrada en la bitácora de
/// cambios (<c>dato_modificado</c>) con documento, estado y firmante. La misma (documento, estado,
/// firmante) no genera una segunda fila (comparación Ordinal tras Trim).</para>
/// </summary>
public sealed class SimularFirmaHandler(IProcedureInstanceRepository repo)
{
    private static readonly JsonSerializerOptions s_jsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Overload de compatibilidad: no pasa actor (proveedor automático / webhook sin persona).
    /// <c>CreatedBy</c> queda en <c>null</c>.
    /// </summary>
    public Task<(SimularFirmaResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid signatureId,
        CancellationToken ct = default)
        => HandleAsync(id, tenantId, signatureId, null, ct);

    /// <summary>
    /// Overload principal. <paramref name="actorUserId"/> puede ser <c>null</c> cuando la firma la
    /// aplica el proveedor sin persona identificada (webhook, proceso batch).
    /// </summary>
    public async Task<(SimularFirmaResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid signatureId,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        // HU #14175: cargamos el grafo con actores para obtener el firmante en la bitácora.
        var instance = await repo.GetByIdWithFurGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var signature = instance.Signatures.FirstOrDefault(s => s.Id == signatureId);
        if (signature is null)
            return (null, "signature_not_found");

        if (signature.Estado == SignatureEstados.Firmada)
            return (new SimularFirmaResult(signature.Id, signature.Estado, signature.PdfPath, signature.Sha256), null);

        if (signature.Estado != SignatureEstados.Enviada)
            return (null, "estado_invalido");

        var now = DateTimeOffset.UtcNow;

        // ── HU #14175 — bitácora ─────────────────────────────────────────────────────────────────
        // Se captura el anterior ANTES de mutar la firma para reflejar el estado que tenía.
        var anteriorLabel = BuildFirmaLabel(signature.Estado, signature.DocTipo, instance, signature.Parte);
        var nuevoLabel    = BuildFirmaLabel(SignatureEstados.Firmada, signature.DocTipo, instance, signature.Parte);

        // MOCK: el proveedor real devolvería el PDF firmado + su hash en el callback.
        var pdfPath = $"signatures/{id:N}/{signature.Parte}_{signature.DocTipo}_signed.pdf";
        var sha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{signature.Id:N}:{now:O}")));

        signature.Estado    = SignatureEstados.Firmada;
        signature.PdfPath   = pdfPath;
        signature.Sha256    = sha256;
        signature.FirmadoAt = now;
        signature.UpdatedAt = now;

        // Comparación Ordinal tras Trim; iguales → no fila (AC2 — idempotencia de la bitácora).
        var ant = NormFirma(anteriorLabel);
        var nvo = NormFirma(nuevoLabel);
        if (!string.Equals(ant, nvo, StringComparison.Ordinal))
        {
            var payload = JsonSerializer.Serialize(
                new { etiqueta = "Firma", campo = "firma", anterior = ant, nuevo = nvo },
                s_jsonOpts);
            await repo.AddEventAsync(new ProcedureInstanceEvent
            {
                Id                  = Guid.NewGuid(),
                TenantId            = tenantId,
                ProcedureInstanceId = id,
                Tipo                = ProcedureFieldChangeRecorder.EventType,
                Payload             = payload,
                CreatedAt           = now,
                CreatedBy           = actorUserId,
            }, ct);
        }

        await repo.SaveChangesAsync(ct);

        return (new SimularFirmaResult(signature.Id, signature.Estado, pdfPath, sha256), null);
    }

    /// <summary>
    /// Construye la etiqueta del campo "firma" para la bitácora:
    /// <c>"{EstadoLabel} · {DocTipoLabel} · {Firmante}"</c> (sin firmante si el actor no está en el
    /// grafo cargado).
    /// </summary>
    /// <remarks>
    /// <c>internal</c> para que los tests de la HU #14175 puedan verificar el valor exacto de la
    /// etiqueta sin necesidad de deserializar el payload.
    /// </remarks>
    internal static string BuildFirmaLabel(
        string estado, string docTipo, ProcedureInstance instance, string parte)
    {
        var estadoLabel = string.Equals(estado, SignatureEstados.Firmada, StringComparison.OrdinalIgnoreCase)
            ? "Firmada"
            : "Pendiente";
        var docLabel = DocTipoLabel(docTipo);
        var firmante = instance.Actors
            .FirstOrDefault(a => string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase))
            ?.FullName ?? string.Empty;
        return string.IsNullOrWhiteSpace(firmante)
            ? $"{estadoLabel} · {docLabel}"
            : $"{estadoLabel} · {docLabel} · {firmante}";
    }

    // "compraventa" → "Compraventa"; "mandato" → "Mandato"; vacío → vacío.
    private static string DocTipoLabel(string? docTipo)
    {
        if (string.IsNullOrWhiteSpace(docTipo)) return string.Empty;
        var s = docTipo.Trim();
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    // null y blanco → cadena vacía; en otro caso, Trim().
    private static string NormFirma(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}

using System.Text.Json;
using Flit.Tramites.Application.Signatures;
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
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
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
/// </summary>
public sealed class SimularFirmaHandler(IProcedureInstanceRepository repo)
{
    public async Task<(SimularFirmaResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid signatureId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithSignaturesAsync(id, tenantId, ct);
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
        // MOCK: el proveedor real devolvería el PDF firmado + su hash en el callback.
        var pdfPath = $"signatures/{id:N}/{signature.Parte}_{signature.DocTipo}_signed.pdf";
        var sha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{signature.Id:N}:{now:O}")));

        signature.Estado = SignatureEstados.Firmada;
        signature.PdfPath = pdfPath;
        signature.Sha256 = sha256;
        signature.FirmadoAt = now;
        signature.UpdatedAt = now;

        await repo.SaveChangesAsync(ct);

        return (new SimularFirmaResult(signature.Id, signature.Estado, pdfPath, sha256), null);
    }
}

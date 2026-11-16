using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// HU #14301 — implementación de <see cref="IIdentityLetterService"/>. La carta pertenece a la VALIDACIÓN (no al trámite): sirve
/// igual con <c>ProcedureInstanceId</c> nulo (prevalidación) y deja serie, hash y ruta en columnas propias
/// (<c>letter_*</c>), sin tocar <c>certificate_hash</c> de Kyverum. Idempotente: con carta ya registrada no hace nada.
/// <para>El hash del código de integridad es el SHA-256 de una huella canónica de la evidencia (datos de la validación + hash de
/// cada imagen), NO del PDF —el PDF no puede contener su propio hash—; el SHA-256 del archivo va aparte en
/// <c>letter_pdf_sha256</c>. Como la huella no incluye la fecha de emisión, serie y hash son estables.</para>
/// </summary>
public sealed class IdentityLetterService(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage,
    IIdentityLetterPdfGenerator generator,
    ICompaniaRadicadoraDirectory? companies = null,
    TimeProvider? clock = null,
    ILogger<IdentityLetterService>? logger = null) : IIdentityLetterService
{
    /// <summary>Tipo de adjunto con el que se guarda el PDF en el storage.</summary>
    public const string TipoCarta = "carta_identidad";

    private const string HuellaVersion = "flit-identity-letter/v1";

    private readonly ILogger _logger = logger ?? NullLogger<IdentityLetterService>.Instance;

    public async Task<IdentityLetterOutcome> EnsureForValidationAsync(Guid validationId, CancellationToken ct = default)
    {
        var v = await repo.GetBiometricByIdAsync(validationId, ct).ConfigureAwait(false);
        if (v is null || !v.CorrespondeCartaIdentidad)
            return IdentityLetterOutcome.Skipped;
        if (v.TieneCartaIdentidad)
            return IdentityLetterOutcome.AlreadyPresent;

        string? savedPath = null;
        var registered = false;
        try
        {
            var selfie = await ReadAsync(v.FacePhotoPath, ct).ConfigureAwait(false);
            var front = await ReadAsync(v.IdFrontPhotoPath, ct).ConfigureAwait(false);
            var back = await ReadAsync(v.IdBackPhotoPath, ct).ConfigureAwait(false);
            var signature = await ReadAsync(v.SignatureImagePath, ct).ConfigureAwait(false);

            var now = (clock ?? TimeProvider.System).GetUtcNow();
            var evidenceSha = EvidenceSha256(v, selfie, front, back, signature);
            var company = companies is null
                ? null
                : await companies.GetRazonSocialAsync(v.TenantId, ct).ConfigureAwait(false);

            var data = new IdentityLetterData(
                Radicado: Radicado(v.Id),
                CompanyName: string.IsNullOrWhiteSpace(company) ? "-" : company.Trim(),
                Reference: (v.ProcedureInstanceId ?? v.Id).ToString("D"),
                IssuedAt: now,
                RoleLabel: RoleLabel(v.PartyRole),
                RegisteredName: v.Name,
                DocumentType: v.DocumentType,
                DocumentNumber: v.DocumentNumber,
                ValidatedAt: v.ValidatedAt ?? v.ReviewedAt ?? now,
                ValidUntil: v.ValidUntil,
                Serie: Serie(evidenceSha),
                EvidenceSha256: evidenceSha,
                SealedAt: now,
                ConsentIp: v.ConsentIp,
                ConsentAt: v.ConsentAt,
                ConsentTextVersion: v.ConsentTextVersion,
                Texts: ManualIdentityAuthorizationTexts.For(v.ConsentTextVersion),
                Selfie: selfie,
                IdFront: front,
                IdBack: back,
                Signature: signature);

            var pdf = generator.Generate(data);
            if (pdf.Length == 0)
                throw new IdentityLetterGenerationException("El generador devolvió un PDF vacío.");

            using var content = new MemoryStream(pdf, writable: false);
            var stored = await storage.SaveAsync(v.Id, TipoCarta, "carta-identidad.pdf", content, ct).ConfigureAwait(false);
            savedPath = stored.StoragePath;

            registered = v.RegistrarCartaIdentidad(data.Serie, evidenceSha, Hex(SHA256.HashData(pdf)), stored.StoragePath, now);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            IdentityLetterLog.Generated(_logger, v.Id);
            return IdentityLetterOutcome.Generated;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // La aprobación ya está confirmada: no se revierte. Se limpia el PDF huérfano y se deja que quien llama reintente.
            if (savedPath is not null)
            {
                try { storage.Delete(savedPath); }
                catch (Exception) { /* best-effort */ }
            }

            // Si el guardado de la fila falló, la entidad rastreada conserva los campos: se limpian para que un SaveChanges
            // posterior del mismo scope no deje una ruta a un archivo ya borrado.
            if (registered)
            {
                v.CartaSerie = v.CartaSha256 = v.CartaPdfSha256 = v.CartaPath = null;
                v.CartaGeneratedAt = null;
            }

            IdentityLetterLog.Failed(_logger, v.Id, ex.GetType().Name);
            if (ex is IdentityLetterGenerationException)
                throw;
            throw new IdentityLetterGenerationException("No se pudo generar la carta de identidad.", ex);
        }
    }

    /// <summary>Radicado propio estable: <c>CERT-</c> + 12 hex en mayúsculas derivados del id de la validación.</summary>
    public static string Radicado(Guid validationId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"flit-identity-letter-radicado:{validationId:D}"));
        return "CERT-" + Convert.ToHexString(hash.AsSpan(0, 6));
    }

    /// <summary>Serie del código de integridad, derivada del hash de la evidencia: <c>FL-XXXXXXXX-XXXXXX</c>.</summary>
    public static string Serie(string evidenceSha256) =>
        $"FL-{evidenceSha256[..8].ToUpperInvariant()}-{evidenceSha256.Substring(8, 6).ToUpperInvariant()}";

    /// <summary>Rol de la fila en mayúsculas (COMPRADOR, VENDEDOR, MANDATARIO...); sin rol (prevalidación) es TITULAR.</summary>
    public static string RoleLabel(string? partyRole) =>
        string.IsNullOrWhiteSpace(partyRole) ? "TITULAR" : partyRole.Trim().ToUpperInvariant();

    /// <summary>
    /// SHA-256 (hex minúsculas) de la huella canónica de la evidencia: identidad de la validación, aprobación, consentimiento y el
    /// hash de cada imagen. No incluye la fecha de emisión ni nada que cambie entre generaciones.
    /// </summary>
    public static string EvidenceSha256(
        ProcedureInstanceBiometricValidation v, byte[] selfie, byte[] front, byte[] back, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(v);
        var sb = new StringBuilder();
        void Line(string value) => sb.Append(value).Append('\n');
        Line(HuellaVersion);
        Line(v.Id.ToString("D"));
        Line(v.TenantId.ToString("D"));
        Line(v.DocumentType.Trim());
        Line(v.DocumentNumber.Trim());
        Line(v.Name.Trim());
        Line((v.ValidatedAt ?? v.ReviewedAt)?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        Line(v.ConsentAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        Line(v.ConsentTextVersion ?? string.Empty);
        Line(Hex(SHA256.HashData(selfie)));
        Line(Hex(SHA256.HashData(front)));
        Line(Hex(SHA256.HashData(back)));
        Line(Hex(SHA256.HashData(signature)));
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    private async Task<byte[]> ReadAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new IdentityLetterGenerationException("La validación no tiene todas las imágenes de la captura.");

        var stream = await storage.OpenReadAsync(path, ct).ConfigureAwait(false)
            ?? throw new IdentityLetterGenerationException("Una imagen de la captura no está disponible en el storage.");
        await using (stream.ConfigureAwait(false))
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            if (ms.Length == 0)
                throw new IdentityLetterGenerationException("Una imagen de la captura está vacía.");
            return ms.ToArray();
        }
    }
}

/// <summary>Logging source-generated (CA1848). Solo ids: sin PII, rutas ni tokens.</summary>
internal static partial class IdentityLetterLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Carta de identidad generada para la validación {ValidationId}.")]
    public static partial void Generated(ILogger logger, Guid validationId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo generar la carta de identidad de la validación {ValidationId} ({ErrorType}); se reintentará.")]
    public static partial void Failed(ILogger logger, Guid validationId, string errorType);
}

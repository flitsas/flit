using System.Security.Cryptography;
using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Arma el contexto de estampado de impronta manual: dueños actuales ordenados por <c>Ordinal</c>.
/// Persona jurídica (NIT) → rúbrica y sidecar del presentante (baúl o identidad), paridad FUR.
/// </summary>
public static class ImprontaManualStampContextBuilder
{
    public static async Task<ImprontaManualStampContext> BuildAsync(
        ProcedureInstance instance,
        ProcedureInstanceAttachment impronta,
        IAttachmentStorage storage,
        ISignatureVaultPolicy? vaultPolicy = null,
        IProcedureInstanceRepository? repo = null,
        CancellationToken ct = default)
    {
        vaultPolicy ??= NullSignatureVaultPolicy.Instance;

        var fv = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        string? Get(string key) => fv.TryGetValue(key, out var v) ? v : null;

        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        var rol = profile.RequiresSeller ? "vendedor" : "comprador";

        var actores = instance.Actors
            .Where(a => string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Ordinal)
            .ThenBy(a => a.CreatedAt)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var signers = new List<ImprontaManualSigner>(actores.Count);
        foreach (var actor in actores)
        {
            var subject = IdentitySubjectResolver.For(actor);
            var displayName = string.IsNullOrWhiteSpace(subject.Nombre)
                ? (string.IsNullOrWhiteSpace(actor.FullName) ? actor.DocumentNumber : actor.FullName.Trim())
                : subject.Nombre!.Trim();

            byte[]? imagen = null;
            string? sidecar = null;
            string? sealText = null;
            string? hashProp = null;

            // Precedencia FUR: baúl (si aplica) > imagen de identidad del presentante > sello texto.
            if (FirmaBaulCobertura.Aplica(actor)
                && !string.IsNullOrWhiteSpace(subject.TipoDocumento)
                && !string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            {
                var match = await vaultPolicy
                    .ResolveAsync(instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), ct)
                    .ConfigureAwait(false);
                if (match is not null && !string.IsNullOrWhiteSpace(match.StoragePath))
                {
                    imagen = await TryReadBytesAsync(storage, match.StoragePath, ct).ConfigureAwait(false);
                    if (imagen is { Length: > 0 })
                    {
                        if (!string.IsNullOrWhiteSpace(match.FullName))
                            displayName = match.FullName.Trim();
                        var baulMeta = new FirmaBaulMetadata(
                            match.DocumentNumber,
                            match.FullName,
                            match.VigenciaDesde,
                            match.VigenciaHasta,
                            match.SignatureVaultId,
                            match.CodigoHash);
                        sidecar = FirmaBaulSelloText.Build(baulMeta, incluirIdentificacion: true);
                        var huella = match.CodigoHash ?? match.SignatureHash;
                        hashProp = !string.IsNullOrWhiteSpace(huella)
                            ? Sha256Hex(Encoding.UTF8.GetBytes(huella!))
                            : Sha256Hex(Encoding.UTF8.GetBytes($"{subject.NumeroDocumento}|{displayName}"));
                    }
                }
            }

            var eligioBaul = FirmaBaulCobertura.EligioBaulExplicitamente(actor);
            if (imagen is null && !eligioBaul)
            {
                var bio = await ResolveBiometricAsync(instance, actor, subject, now, repo, ct)
                    .ConfigureAwait(false);
                if (bio is not null)
                {
                    sealText = IdentidadSelloText.Build(bio);
                    if (bio.SignatureImagePath is { Length: > 0 } path)
                    {
                        var bytes = await TryReadBytesAsync(storage, path, ct).ConfigureAwait(false);
                        // Preferir PNG/JPEG válidos; si el artefacto no pasa el sniff, igual se intenta
                        // pintar (PdfSharp/ImageSharp) — no dejar la zona vacía.
                        if (bytes is { Length: > 0 })
                        {
                            imagen = bytes;
                            sidecar = sealText;
                        }
                    }

                    hashProp ??= !string.IsNullOrWhiteSpace(bio.CertificateHash)
                        ? Sha256Hex(Encoding.UTF8.GetBytes(bio.CertificateHash!))
                        : Sha256Hex(Encoding.UTF8.GetBytes(
                            $"{subject.NumeroDocumento ?? actor.DocumentNumber}|{displayName}"));
                }
            }

            hashProp ??= Sha256Hex(Encoding.UTF8.GetBytes(
                $"{subject.NumeroDocumento ?? actor.DocumentNumber}|{displayName}"));

            signers.Add(new ImprontaManualSigner(
                FullName: displayName,
                HashPropietario: hashProp,
                SignatureImage: imagen,
                ImageSidecarText: sidecar,
                SealText: imagen is null ? sealText : null));
        }

        return new ImprontaManualStampContext(
            ReferenceNumber: instance.ReferenceNumber,
            Placa: Get("plate"),
            Vin: Get("vin"),
            NumMotor: Get("vehicle_engine_number") ?? Get("engine_number") ?? Get("num_motor"),
            NumChasis: Get("vehicle_chassis_number") ?? Get("chassis_number") ?? Get("num_chasis") ?? Get("vin"),
            FechaCargue: impronta.UploadedAt,
            Signers: signers);
    }

    /// <summary>
    /// Paridad FUR: fila local del trámite por documento del sujeto; si no, identidad vigente
    /// referenciada en el tenant (<see cref="IProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync"/>).
    /// </summary>
    private static async Task<ProcedureInstanceBiometricValidation?> ResolveBiometricAsync(
        ProcedureInstance instance,
        ProcedureInstanceActor actor,
        IdentitySubject subject,
        DateTimeOffset now,
        IProcedureInstanceRepository? repo,
        CancellationToken ct)
    {
        var validations = instance.BiometricValidations;
        if (validations is { Count: > 0 })
        {
            // 1) Rol + documento del sujeto (RL en NIT), preferir aprobada+vigente.
            var own = validations
                .Where(v =>
                    string.Equals(v.PartyRole, actor.ActorType, StringComparison.OrdinalIgnoreCase)
                    && BiometricRules.DocumentoCoincide(v, subject.TipoDocumento, subject.NumeroDocumento))
                .OrderByDescending(v => BiometricRules.EsAprobadaVigente(v, now))
                .ThenByDescending(v => !string.IsNullOrWhiteSpace(v.SignatureImagePath))
                .ThenByDescending(v => v.CreatedAt)
                .FirstOrDefault();
            if (own is not null)
                return own;

            // 2) Mismo documento sin filtro de rol (reuso / PartyRole null).
            var byDoc = validations
                .Where(v => BiometricRules.DocumentoCoincide(v, subject.TipoDocumento, subject.NumeroDocumento))
                .OrderByDescending(v => BiometricRules.EsAprobadaVigente(v, now))
                .ThenByDescending(v => !string.IsNullOrWhiteSpace(v.SignatureImagePath))
                .ThenByDescending(v => v.CreatedAt)
                .FirstOrDefault();
            if (byDoc is not null)
                return byDoc;
        }

        if (repo is null
            || string.IsNullOrWhiteSpace(subject.TipoDocumento)
            || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return null;

        return await repo
            .FindVigenteApprovedByDocumentAsync(
                instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), now, ct)
            .ConfigureAwait(false);
    }

    private static async Task<byte[]?> TryReadBytesAsync(
        IAttachmentStorage storage, string path, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(path, ct).ConfigureAwait(false);
            if (stream is null)
                return null;
            return await ReadAllAsync(stream, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }
}

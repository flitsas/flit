using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Aplica sellos FLIT a la impronta manual al componer el consolidado (ruta OT).
/// Si el stamp aplica: sobrescribe el adjunto en storage (nuevo path + fila) y
/// registra auditoría en <c>tramites.vehicle_signature_imprints</c> (paridad legacy).
/// </summary>
public static class ImprontaManualStampApplier
{
    public static async Task<byte[]> MaybeStampAsync(
        byte[] pdf,
        ProcedureInstanceAttachment attachment,
        ProcedureInstance instance,
        IAttachmentStorage storage,
        IImprontaManualStamper? stamper,
        CancellationToken ct,
        ISignatureVaultPolicy? vaultPolicy = null,
        IProcedureInstanceRepository? repo = null,
        IVehicleSignatureImprintRepository? auditRepo = null)
    {
        if (stamper is null)
            return pdf;
        if (!string.Equals(attachment.Tipo, "impronta", StringComparison.OrdinalIgnoreCase))
            return pdf;
        if (AttachmentProviders.IsKyverum(attachment.Provider))
            return pdf;
        if (stamper.AlreadyStamped(pdf))
            return pdf;

        var context = await ImprontaManualStampContextBuilder
            .BuildAsync(instance, attachment, storage, vaultPolicy, repo, ct)
            .ConfigureAwait(false);
        var result = stamper.Stamp(pdf, context);
        if (!result.Applied)
            return result.Pdf;

        await TryPersistSignedOriginalAsync(
                result, attachment, instance, storage, repo, auditRepo, ct)
            .ConfigureAwait(false);

        return result.Pdf;
    }

    private static async Task TryPersistSignedOriginalAsync(
        ImprontaManualStampResult stamp,
        ProcedureInstanceAttachment previous,
        ProcedureInstance instance,
        IAttachmentStorage storage,
        IProcedureInstanceRepository? repo,
        IVehicleSignatureImprintRepository? auditRepo,
        CancellationToken ct)
    {
        if (repo is null || auditRepo is null)
            return;

        // Idempotencia por hash único (paridad legacy uq sobre hash).
        var existing = await auditRepo
            .FindByDocumentHashAsync(stamp.DocumentHash, ct)
            .ConfigureAwait(false);
        if (existing is not null)
            return;

        StoredFile stored;
        try
        {
            stored = await storage
                .SaveAsync(
                    instance.Id,
                    previous.Tipo,
                    previous.Filename,
                    new MemoryStream(stamp.Pdf),
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Best-effort: el consolidado OT sigue con los bytes sellados en memoria.
            return;
        }

        storage.Delete(previous.StoragePath);

        var now = stamp.SignedAt ?? DateTimeOffset.UtcNow;
        // In-place: mismo attachment.id (ya existe en BD) para no violar
        // fk_vehicle_signature_imprints_attachment al Insert de la auditoría en el mismo SaveChanges.
        previous.SizeBytes = stored.SizeBytes;
        previous.Sha256 = stored.Sha256;
        previous.StoragePath = stored.StoragePath;
        previous.UploadedAt = now;

        auditRepo.Add(new VehicleSignatureImprint
        {
            Id = Guid.CreateVersion7(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            AttachmentId = previous.Id,
            ModuleCode = ResolveModuleCode(instance),
            PrivateKey = stamp.PrivateKeyPem,
            PublicKey = stamp.PublicKeyPem,
            DocumentHash = stamp.DocumentHash,
            Signature = stamp.SignatureBase64,
            SignedAt = now,
            WasSignedWithoutOwnerSignature = stamp.WasSignedWithoutOwnerSignature,
            CreatedAt = now,
        });
    }

    private static string ResolveModuleCode(ProcedureInstance instance)
    {
        var code = instance.ProcedureType?.Code;
        if (string.IsNullOrWhiteSpace(code))
            return "tramites";
        return code.Trim().Length <= 40 ? code.Trim() : code.Trim()[..40];
    }
}

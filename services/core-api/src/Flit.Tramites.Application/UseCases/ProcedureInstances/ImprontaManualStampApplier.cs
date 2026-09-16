using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Aplica sellos FLIT a la impronta manual al componer el consolidado (ruta OT).
/// Gates: matrícula con placa asignada; propietarios con identidad vigente.
/// Si el stamp aplica: sobrescribe el adjunto en storage y registra auditoría.
/// Idempotencia por <b>trámite + hash</b> (Bug #12594): el mismo PDF base puede firmarse en
/// trámites distintos sin considerarse duplicado; solo se deduplica dentro del mismo trámite.
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
        IVehicleSignatureImprintRepository? auditRepo = null,
        ILogger? logger = null)
    {
        if (stamper is null)
            return pdf;
        if (!string.Equals(attachment.Tipo, "impronta", StringComparison.OrdinalIgnoreCase))
            return pdf;
        if (AttachmentProviders.IsKyverum(attachment.Provider))
            return pdf;
        if (stamper.AlreadyStamped(pdf))
            return pdf;

        var (ready, _) = await ImprontaManualStampReadiness
            .EvaluateAsync(instance, repo, vaultPolicy, ct)
            .ConfigureAwait(false);
        if (!ready)
            return pdf;

        var context = await ImprontaManualStampContextBuilder
            .BuildAsync(instance, attachment, storage, vaultPolicy, repo, ct)
            .ConfigureAwait(false);
        var result = stamper.Stamp(pdf, context);
        if (!result.Applied)
            return result.Pdf;

        await TryPersistSignedOriginalAsync(
                result, attachment, instance, storage, repo, auditRepo, logger, ct)
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
        ILogger? logger,
        CancellationToken ct)
    {
        if (repo is null || auditRepo is null)
            return;

        // Idempotencia por trámite + hash (Bug #12594): solo filas activas del MISMO trámite
        // bloquean el re-registro (parcial uq (procedure_instance_id, document_hash) WHERE
        // deleted_at IS NULL). El mismo PDF base firmado en otro trámite SÍ debe persistir su
        // propio adjunto + auditoría.
        var existing = await auditRepo
            .FindActiveByInstanceAndHashAsync(instance.Id, stamp.DocumentHash, ct)
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
        catch (Exception ex)
        {
            // Best-effort: el consolidado OT sigue con los bytes sellados en memoria. Se registra
            // el fallo (sin PII: solo ids/tipo/nombre de excepción) porque antes se tragaba en
            // silencio y no había forma de diagnosticar por qué faltaba la auditoría (Bug #12594, H1).
            if (logger is not null)
            {
                ImprontaManualStampLog.StorageSaveFailed(
                    logger, instance.Id, previous.Id, previous.Tipo, ex.GetType().Name, ex);
            }
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
            SignedStoragePath = stored.StoragePath,
            SignedSha256 = stored.Sha256,
            SignedSizeBytes = stored.SizeBytes,
            SignedFilename = previous.Filename,
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

/// <summary>Logging source-generado (CA1848) de la impronta manual. NUNCA incluye PII.</summary>
internal static partial class ImprontaManualStampLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Impronta manual: fallo al persistir el original sellado (instanceId={InstanceId}, " +
            "attachmentId={AttachmentId}, tipo={Tipo}, exceptionType={ExceptionType})")]
    public static partial void StorageSaveFailed(
        ILogger logger, Guid instanceId, Guid attachmentId, string tipo, string exceptionType, Exception ex);
}

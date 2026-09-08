using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Aplica sellos FLIT a la impronta manual al componer el consolidado (ruta OT).
/// No muta el adjunto en storage; opera sobre los bytes que van al merge.
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
        IProcedureInstanceRepository? repo = null)
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
        return stamper.Stamp(pdf, context);
    }
}

using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

/// <summary>Lista improntas firmadas por placa para validación OT (HU #12148).</summary>
public sealed class ListImprintSignaturesByPlacaHandler
{
    private readonly IVehicleSignatureImprintRepository _imprintRepository;

    public ListImprintSignaturesByPlacaHandler(IVehicleSignatureImprintRepository imprintRepository)
    {
        _imprintRepository = imprintRepository ?? throw new ArgumentNullException(nameof(imprintRepository));
    }

    public async Task<ListImprintSignaturesByPlacaResult> HandleAsync(
        ListImprintSignaturesByPlacaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await _imprintRepository
            .ListByPlacaAsync(query.TenantId, query.Placa, cancellationToken)
            .ConfigureAwait(false);

        return new ListImprintSignaturesByPlacaResult
        {
            Data = rows.Select(Map).ToList(),
        };
    }

    internal static ImprintSignatureDto Map(Domain.Entities.VehicleSignatureImprintListRow row) =>
        new(
            row.Id,
            row.TenantId,
            row.ProcedureInstanceId,
            row.Placa,
            row.ModuleCode,
            row.AttachmentId,
            row.PublicKey,
            row.DocumentHash,
            row.Signature,
            row.SignedAt,
            row.WasSignedWithoutOwnerSignature,
            row.SignedStoragePath,
            row.SignedSha256,
            row.SignedSizeBytes,
            row.SignedFilename,
            row.DeletedAt);
}

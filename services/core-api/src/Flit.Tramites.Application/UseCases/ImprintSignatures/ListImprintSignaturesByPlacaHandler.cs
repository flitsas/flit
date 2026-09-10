using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

/// <summary>Lista improntas firmadas por placa para validación OT (HU #12148 / #12176).</summary>
public sealed class ListImprintSignaturesByPlacaHandler
{
    private readonly IVehicleSignatureImprintRepository _imprintRepository;
    private readonly IImprintSignatureValidationRepository _validationRepository;

    public ListImprintSignaturesByPlacaHandler(
        IVehicleSignatureImprintRepository imprintRepository,
        IImprintSignatureValidationRepository validationRepository)
    {
        _imprintRepository = imprintRepository ?? throw new ArgumentNullException(nameof(imprintRepository));
        _validationRepository = validationRepository ?? throw new ArgumentNullException(nameof(validationRepository));
    }

    public async Task<ListImprintSignaturesByPlacaResult> HandleAsync(
        ListImprintSignaturesByPlacaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await _imprintRepository
            .ListByPlacaAsync(query.Placa, cancellationToken)
            .ConfigureAwait(false);

        var latest = await _validationRepository
            .GetLatestByImprintIdsAsync(rows.Select(r => r.Id).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        return new ListImprintSignaturesByPlacaResult
        {
            Data = rows.Select(row =>
            {
                latest.TryGetValue(row.Id, out var last);
                return Map(row, last is null ? null : MapValidation(last));
            }).ToList(),
        };
    }

    internal static ImprintSignatureDto Map(
        Domain.Entities.VehicleSignatureImprintListRow row,
        ImprintSignatureValidationSummaryDto? lastValidation = null) =>
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
            row.DeletedAt,
            lastValidation);

    internal static ImprintSignatureValidationSummaryDto MapValidation(Domain.Entities.ImprintSignatureValidation row) =>
        new(row.Id, row.Result, row.FailureReason, row.ValidatedAt, row.ValidatedBy, row.Placa);
}

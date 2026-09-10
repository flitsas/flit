using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

public sealed class ListImprintSignatureValidationsQuery
{
    public required Guid VehicleSignatureImprintId { get; init; }
}

public sealed class ListImprintSignatureValidationsResult
{
    public bool Found { get; init; }
    public required IReadOnlyList<ImprintSignatureValidationSummaryDto> Data { get; init; }
}

/// <summary>Historial append-only de validaciones de una impronta (HU #12176).</summary>
public sealed class ListImprintSignatureValidationsHandler
{
    private readonly IVehicleSignatureImprintRepository _imprintRepository;
    private readonly IImprintSignatureValidationRepository _validationRepository;

    public ListImprintSignatureValidationsHandler(
        IVehicleSignatureImprintRepository imprintRepository,
        IImprintSignatureValidationRepository validationRepository)
    {
        _imprintRepository = imprintRepository ?? throw new ArgumentNullException(nameof(imprintRepository));
        _validationRepository = validationRepository ?? throw new ArgumentNullException(nameof(validationRepository));
    }

    public async Task<ListImprintSignatureValidationsResult> HandleAsync(
        ListImprintSignatureValidationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var imprint = await _imprintRepository
            .GetByIdAsync(query.VehicleSignatureImprintId, cancellationToken)
            .ConfigureAwait(false);

        if (imprint is null)
        {
            return new ListImprintSignatureValidationsResult
            {
                Found = false,
                Data = [],
            };
        }

        var rows = await _validationRepository
            .ListByImprintIdAsync(query.VehicleSignatureImprintId, cancellationToken)
            .ConfigureAwait(false);

        return new ListImprintSignatureValidationsResult
        {
            Found = true,
            Data = rows.Select(ListImprintSignaturesByPlacaHandler.MapValidation).ToList(),
        };
    }
}

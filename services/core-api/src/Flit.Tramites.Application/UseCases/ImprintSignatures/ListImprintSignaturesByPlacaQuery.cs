namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

public sealed class ListImprintSignaturesByPlacaQuery
{
    public required Guid TenantId { get; init; }

    public required string Placa { get; init; }
}

public sealed class ListImprintSignaturesByPlacaResult
{
    public required IReadOnlyList<ImprintSignatureDto> Data { get; init; }
}

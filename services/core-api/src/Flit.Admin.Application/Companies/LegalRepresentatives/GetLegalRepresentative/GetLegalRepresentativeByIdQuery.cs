namespace Flit.Admin.Application.Companies.LegalRepresentatives.GetLegalRepresentative;

/// <summary>Consulta de un representante legal por id dentro del tenant (HU #10901).</summary>
public sealed class GetLegalRepresentativeByIdQuery
{
    public required Guid TenantId { get; init; }

    public required Guid Id { get; init; }
}

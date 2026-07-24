namespace Flit.Admin.Application.Companies.LegalRepresentatives.UpdateLegalRepresentative;

/// <summary>
/// Comando de edición de un representante legal (HU #10901). <c>Id</c> es el representante a editar
/// dentro del tenant. <c>DocumentNumber</c> y <c>CompanyNit</c> son PII (Ley 1581): no loguear.
/// </summary>
public sealed class UpdateLegalRepresentativeCommand
{
    public required Guid TenantId { get; init; }

    public required Guid Id { get; init; }

    public string? CompanyNit { get; init; }

    public string? CompanyName { get; init; }

    public string? CompanyEmail { get; init; }

    public string? CompanyAddress { get; init; }

    public string? CompanyCity { get; init; }

    public string? CompanyPhone { get; init; }

    public string? DocumentType { get; init; }

    public string? DocumentNumber { get; init; }

    public string? FirstLastName { get; init; }

    public string? SecondLastName { get; init; }

    public string? Name { get; init; }

    public string? Email { get; init; }

    public string? Address { get; init; }

    public string? City { get; init; }

    public string? Phone { get; init; }

    public IReadOnlyList<Guid> ProcedureTypeIds { get; init; } = [];

    public Guid? ActorBy { get; init; }
}

namespace Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;

/// <summary>Consulta de mandatarios activos de un OT (RF27).</summary>
public sealed class ListMandateSignersQuery
{
    public required Guid TransitOfficeId { get; init; }
}

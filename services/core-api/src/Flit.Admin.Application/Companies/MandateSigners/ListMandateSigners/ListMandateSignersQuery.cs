using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;

/// <summary>Consulta de mandatarios activos de un OT (RF27).</summary>
public sealed class ListMandateSignersQuery
{
    public required Guid TransitOfficeId { get; init; }

    /// <summary>Bug #12912 (Habeas Data) — ver <see cref="OtCompanyVisibility"/>.</summary>
    public required OtCompanyVisibility Visibility { get; init; }
}

using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.SetCompanyStatus;

/// <summary>Desenlace del cambio de estado.</summary>
public enum SetCompanyStatusOutcome
{
    /// <summary>Estado aplicado (HTTP 200) — incluye la proyección actualizada.</summary>
    Updated,

    /// <summary>La compañía no existe (HTTP 404).</summary>
    NotFound,
}

/// <summary>Resultado de activar/desactivar una compañía.</summary>
public sealed class SetCompanyStatusResult
{
    private SetCompanyStatusResult(SetCompanyStatusOutcome outcome, CompanyListItem? company)
    {
        Outcome = outcome;
        Company = company;
    }

    public SetCompanyStatusOutcome Outcome { get; }

    public CompanyListItem? Company { get; }

    public static SetCompanyStatusResult Updated(CompanyListItem company) =>
        new(SetCompanyStatusOutcome.Updated, company);

    public static SetCompanyStatusResult NotFound() =>
        new(SetCompanyStatusOutcome.NotFound, null);
}

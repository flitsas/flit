using System.Text.Json.Serialization;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.ListCompanies;

/// <summary>
/// Resultado del listado paginado. Serializado como
/// <c>{ data, totalCount, page, pageSize }</c> (HU #10189, AC1).
/// </summary>
public sealed class ListCompaniesResult
{
    public ListCompaniesResult(
        IReadOnlyList<CompanyListItem> data,
        long totalCount,
        int page,
        int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>Validación fallida (HU #13998, tipo fuera de catálogo): sin consulta y sin datos.</summary>
    public static ListCompaniesResult Invalid(IReadOnlyList<CompanyValidationError> errors) =>
        new([], 0, 0, 0) { Errors = errors };

    /// <summary>Errores de validación; no se serializa en el 200.</summary>
    [JsonIgnore]
    public IReadOnlyList<CompanyValidationError> Errors { get; private init; } = [];

    [JsonIgnore]
    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<CompanyListItem> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}

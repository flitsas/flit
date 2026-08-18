using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Documents.Reports;
using Flit.Queries.Domain;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, segunda ola) — arma el Excel de un informe programado tipo "consulta":
/// resuelve la <c>SavedQuery</c> (por id, ignorando el usuario dueño — ver
/// <see cref="ICompanyQueryRepository.GetSavedByIdAsync"/>), re-ejecuta su definición (el filtro de
/// fechas es RELATIVO — "últimos 7 días" — así que cada envío periódico consulta el periodo actual,
/// no el que tenía cuando se guardó) y pagina hasta <see cref="RowCap"/> filas, igual que el tope
/// del export manual del navegador (<c>QueryConsole.tsx</c>).
/// </summary>
internal sealed class CompanyQueryReportDocumentBuilder(ICompanyQueryRepository repo)
{
    /// <summary>Mismo tope que el export manual del navegador (un correo automático no tiene quien
    /// le dé "descargar el resto" — se avisa en el cuerpo del correo en vez de repartir en varios
    /// adjuntos).</summary>
    public const int RowCap = 5_000;

    public sealed record Result(byte[] Bytes, string QueryName, int Total, bool Truncated);

    /// <summary>Null si la SavedQuery ya no existe (borrada después de programar el informe).</summary>
    public async Task<Result?> BuildAsync(Guid tenantId, Guid savedQueryId, CancellationToken ct)
    {
        var saved = await repo.GetSavedByIdAsync(tenantId, savedQueryId, ct);
        if (saved is null)
            return null;

        var rows = new List<CompanyQueryRowDto>();
        var total = 0;
        var page = 1;
        while (rows.Count < RowCap)
        {
            var request = QueryNormalizer.BuildRequest(
                CompanyQueryFieldCatalog.Instance, saved.Definition, page, QueryLimits.MaxPageSize);
            var result = await repo.ExecuteAsync(tenantId, request, ct);
            total = result.Total;
            rows.AddRange(result.Filas);

            if (result.Filas.Count == 0 || rows.Count >= total)
                break;
            page++;
        }

        var truncated = total > rows.Count;
        if (rows.Count > RowCap)
            rows = rows.Take(RowCap).ToList();

        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", saved.Definition.Columnas, rows);
        var bytes = TabularWorkbookWriter.Write([sheet]);
        return new Result(bytes, saved.Nombre, total, truncated);
    }
}

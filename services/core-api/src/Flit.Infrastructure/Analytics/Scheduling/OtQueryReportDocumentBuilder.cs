using Flit.Admin.Domain.OtQueries;
using Flit.Infrastructure.Documents.Reports;
using Flit.Queries.Domain;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, tercera ola) — arma el Excel de un informe programado tipo "consulta" con
/// alcance "ot" (§76 del DDL): re-ejecuta la SavedQuery del organismo (el filtro de fechas es
/// RELATIVO — "últimos 7 días" — así que cada envío periódico consulta el periodo actual) y pagina
/// hasta <see cref="RowCap"/> filas, mismo tope que <see cref="CompanyQueryReportDocumentBuilder"/>
/// y que el export manual del navegador (<c>OtQueriesTab.tsx</c>, vía <c>QueryConsole</c>).
/// </summary>
internal sealed class OtQueryReportDocumentBuilder(IOtQueryRepository repo)
{
    /// <summary>Mismo tope que el export manual del navegador.</summary>
    public const int RowCap = 5_000;

    public sealed record Result(byte[] Bytes, string QueryName, int Total, bool Truncated);

    /// <summary>SavedQuery del organismo dueño de <paramref name="otTenantId"/>. Null si ya no existe.</summary>
    public async Task<Result?> BuildAsync(Guid otTenantId, Guid savedQueryId, CancellationToken ct)
    {
        var saved = await repo.GetSavedByIdAsync(otTenantId, savedQueryId, cancellationToken: ct)
            .ConfigureAwait(false);
        if (saved is null)
            return null;

        var rows = new List<OtQueryRowDto>();
        var total = 0;
        var page = 1;
        while (rows.Count < RowCap)
        {
            var request = QueryNormalizer.BuildRequest(
                OtQueryFieldCatalog.Instance, saved.Definition, page, QueryLimits.MaxPageSize);
            var result = await repo.ExecuteAsync(otTenantId, request, cancellationToken: ct)
                .ConfigureAwait(false);
            if (result is null)
                return null; // Tenant sin organismo asociado.

            total = result.Total;
            rows.AddRange(result.Filas);

            if (result.Filas.Count == 0 || rows.Count >= total)
                break;
            page++;
        }

        var truncated = total > rows.Count;
        if (rows.Count > RowCap)
            rows = rows.Take(RowCap).ToList();

        var sheet = OtQueryReportColumns.BuildSheet("Consulta OT", saved.Definition.Columnas, rows);
        var bytes = TabularWorkbookWriter.Write([sheet]);
        return new Result(bytes, saved.Nombre, total, truncated);
    }
}

using Flit.Analytics.Application.IctQueries;
using Flit.Infrastructure.Documents.Reports;
using Flit.Queries.Domain;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, cuarta ola) — arma el Excel de un informe programado tipo "consulta" con
/// alcance "ict" (consultas propias de la empresa sobre sus pre-trámites de Integración con
/// Terceros): re-ejecuta la SavedQuery del tenant (el filtro de fechas es RELATIVO — "últimos 7
/// días" — así que cada envío periódico consulta el periodo actual) y pagina hasta
/// <see cref="RowCap"/> filas, mismo tope que <see cref="CompanyQueryReportDocumentBuilder"/> y
/// <see cref="OtQueryReportDocumentBuilder"/>, y que el export manual del navegador
/// (<c>IctQueriesTab.tsx</c>, vía <c>QueryConsole</c>).
/// </summary>
internal sealed class IctQueryReportDocumentBuilder(IIctQueryRepository repo)
{
    /// <summary>Mismo tope que el export manual del navegador.</summary>
    public const int RowCap = 5_000;

    public sealed record Result(byte[] Bytes, string QueryName, int Total, bool Truncated);

    /// <summary>SavedQuery del tenant. Null si ya no existe.</summary>
    public async Task<Result?> BuildAsync(Guid tenantId, Guid savedQueryId, CancellationToken ct)
    {
        var saved = await repo.GetSavedByIdAsync(tenantId, savedQueryId, ct).ConfigureAwait(false);
        if (saved is null)
            return null;

        var rows = new List<IctQueryRowDto>();
        var total = 0;
        var page = 1;
        while (rows.Count < RowCap)
        {
            var request = QueryNormalizer.BuildRequest(
                IctQueryFieldCatalog.Instance, saved.Definition, page, QueryLimits.MaxPageSize);
            var result = await repo.ExecuteAsync(tenantId, request, ct).ConfigureAwait(false);

            total = result.Total;
            rows.AddRange(result.Filas);

            if (result.Filas.Count == 0 || rows.Count >= total)
                break;
            page++;
        }

        var truncated = total > rows.Count;
        if (rows.Count > RowCap)
            rows = rows.Take(RowCap).ToList();

        var sheet = IctQueryReportColumns.BuildSheet("Consulta ICT", saved.Definition.Columnas, rows);
        var bytes = TabularWorkbookWriter.Write([sheet]);
        return new Result(bytes, saved.Nombre, total, truncated);
    }
}

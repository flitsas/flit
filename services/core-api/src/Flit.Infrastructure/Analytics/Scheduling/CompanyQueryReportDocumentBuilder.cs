using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Documents.Reports;
using Flit.Queries.Domain;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, segunda ola) — arma el Excel de un informe programado tipo "consulta", para
/// los dos alcances posibles (§75 del DDL): "empresa" (<see cref="BuildAsync"/>, tenant concreto) y
/// "superadmin" (<see cref="BuildForSuperAdminAsync"/>, todas las compañías a la vez — mismo motor
/// que <c>SuperAdminQueriesEndpoints</c>). Ambos re-ejecutan la definición guardada (el filtro de
/// fechas es RELATIVO — "últimos 7 días" — así que cada envío periódico consulta el periodo actual,
/// no el que tenía cuando se guardó) y paginan hasta <see cref="RowCap"/> filas, igual que el tope
/// del export manual del navegador (<c>QueryConsole.tsx</c>).
/// </summary>
internal sealed class CompanyQueryReportDocumentBuilder(
    ICompanyQueryRepository repo, ISuperAdminSavedQueryRepository superAdminSavedQueries)
{
    /// <summary>Mismo tope que el export manual del navegador (un correo automático no tiene quien
    /// le dé "descargar el resto" — se avisa en el cuerpo del correo en vez de repartir en varios
    /// adjuntos).</summary>
    public const int RowCap = 5_000;

    public sealed record Result(byte[] Bytes, string QueryName, int Total, bool Truncated);

    /// <summary>Alcance "empresa": SavedQuery del tenant. Null si ya no existe.</summary>
    public async Task<Result?> BuildAsync(Guid tenantId, Guid savedQueryId, CancellationToken ct)
    {
        var saved = await repo.GetSavedByIdAsync(tenantId, savedQueryId, ct);
        if (saved is null)
            return null;

        return await ExecuteAsync(
            saved, page => repo.ExecuteAsync(tenantId, page, ct));
    }

    /// <summary>Alcance "superadmin": SavedQuery de equipo, ejecutada sobre todas las compañías.
    /// Null si ya no existe.</summary>
    public async Task<Result?> BuildForSuperAdminAsync(Guid savedQueryId, CancellationToken ct)
    {
        var saved = await superAdminSavedQueries.GetByIdAsync(savedQueryId, ct);
        if (saved is null)
            return null;

        return await ExecuteAsync(
            saved, page => repo.ExecuteForSuperAdminAsync(page, ct));
    }

    private static async Task<Result> ExecuteAsync(
        SavedQueryDto saved, Func<QueryRequest, Task<CompanyQueryResultDto>> execute)
    {
        var rows = new List<CompanyQueryRowDto>();
        var total = 0;
        var page = 1;
        while (rows.Count < RowCap)
        {
            var request = QueryNormalizer.BuildRequest(
                CompanyQueryFieldCatalog.Instance, saved.Definition, page, QueryLimits.MaxPageSize);
            var result = await execute(request);
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

using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Abstractions;

/// <summary>Lectura del reporte detallado sobre <c>analytics.v_procedure_detail_report</c> (HU #10814–#10815).</summary>
public interface IDetailedReportReadRepository
{
    Task<DetailedProceduresPageDto> GetProceduresAsync(
        DetailedReportFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task ExportProceduresAsync(
        DetailedReportFilter filter,
        Func<DetailedProcedureRowDto, CancellationToken, Task> onRowAsync,
        CancellationToken ct = default);
}

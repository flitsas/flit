using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;

/// <summary>Lista trámites de clientes OT con grants vigentes (HU #10217 AC1/AC5).</summary>
public sealed class ListOtClientProceduresHandler
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IOtClientProcedureRepository _repository;

    public ListOtClientProceduresHandler(IOtClientProcedureRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListOtClientProceduresResult> HandleAsync(
        ListOtClientProceduresQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var result = await _repository.ListAsync(
            query.OtTenantId,
            new OtClientProcedureFilter
            {
                Status = query.Status,
                PlateFlowStatus = query.PlateFlowStatus,
                ProcedureTypeId = query.ProcedureTypeId,
                Vin = query.Vin,
                Placa = query.Placa,
                Vendedor = query.Vendedor,
                Comprador = query.Comprador,
                Gestor = query.Gestor,
                SortBy = query.SortBy,
                SortDir = query.SortDir,
                Page = page,
                PageSize = pageSize,
            },
            query.TransitOfficeId,
            cancellationToken).ConfigureAwait(false);

        return new ListOtClientProceduresResult
        {
            Data = result.Items.Select(OtClientProcedureMapper.ToResponse).ToList(),
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static int NormalizePage(int? page) =>
        page is null or < 1 ? DefaultPage : page.Value;

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null or < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }
}

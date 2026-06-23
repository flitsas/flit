using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;

/// <summary>Obtiene un trámite de cliente si el OT tiene grant vigente (HU #10217 AC4).</summary>
public sealed class GetOtClientProcedureHandler
{
    private readonly IOtClientProcedureRepository _repository;

    public GetOtClientProcedureHandler(IOtClientProcedureRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GetOtClientProcedureResult> HandleAsync(
        GetOtClientProcedureQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var procedure = await _repository
            .GetByIdAsync(query.OtTenantId, query.ProcedureInstanceId, cancellationToken)
            .ConfigureAwait(false);

        return procedure is null
            ? GetOtClientProcedureResult.NotFound()
            : GetOtClientProcedureResult.Found(OtClientProcedureMapper.ToResponse(procedure));
    }
}

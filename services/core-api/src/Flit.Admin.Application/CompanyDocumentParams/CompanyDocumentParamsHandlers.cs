using Flit.Admin.Domain.CompanyDocumentParams;

namespace Flit.Admin.Application.CompanyDocumentParams;

/// <summary>Respuesta de un parámetro documental por gestora (HU #10521, RF31).</summary>
public sealed record CompanyDocumentParamResponse(Guid Id, string DocumentTypeCode, string State);

/// <summary>Cuerpo del PUT: fija el estado de un tipo de documento para la gestora.</summary>
public sealed record UpsertCompanyDocumentParamRequest(string? DocumentTypeCode, string? State);

/// <summary>Lista los parámetros documentales de una compañía gestora.</summary>
public sealed class ListCompanyDocumentParamsHandler(ICompanyDocumentParamRepository repo)
{
    public async Task<IReadOnlyList<CompanyDocumentParamResponse>> HandleAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var items = await repo.ListByTenantAsync(tenantId, ct).ConfigureAwait(false);
        return items
            .Select(i => new CompanyDocumentParamResponse(i.Id, i.DocumentTypeCode, i.State))
            .ToList();
    }
}

/// <summary>Crea o actualiza el estado documental de un tipo para la gestora (upsert).</summary>
public sealed class UpsertCompanyDocumentParamHandler(ICompanyDocumentParamRepository repo)
{
    public async Task<(CompanyDocumentParamResponse? Result, string? Error)> HandleAsync(
        Guid tenantId,
        UpsertCompanyDocumentParamRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentTypeCode))
            return (null, "invalid_code");

        var state = request.State?.Trim().ToUpperInvariant();
        if (!CompanyDocumentParamStates.IsValid(state))
            return (null, "invalid_state");

        var code = request.DocumentTypeCode.Trim();
        var item = await repo.UpsertAsync(tenantId, code, state!, userId, ct).ConfigureAwait(false);
        return (new CompanyDocumentParamResponse(item.Id, item.DocumentTypeCode, item.State), null);
    }
}

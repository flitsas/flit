using Microsoft.AspNetCore.Http;
using Flit.Api.Endpoints;
namespace Flit.Api.Endpoints.Auditing;

/// <summary>
/// Envuelve un <see cref="AdminAuditFilter"/> para que solo audite cuando la petición es un COMMIT
/// (<c>dryRun=false</c>). Feature #14420 / ADR-0077: la invitación masiva deja UNA fila resumen por carga confirmada; una
/// previsualización (<c>dryRun</c>, el valor por defecto) no crea nada y no debe llenar la bitácora.
/// </summary>
internal sealed class CommitOnlyAuditFilter(AdminAuditFilter inner) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        return BulkInvitationEndpointSupport.IsCommit(context.HttpContext.Request)
            ? inner.InvokeAsync(context, next)
            : next(context);
    }
}

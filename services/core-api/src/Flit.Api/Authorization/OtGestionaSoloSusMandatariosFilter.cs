using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Api.Authorization;

/// <summary>
/// El OT gestiona SOLO sus mandatarios. Puede registrar, editar, dar de baja y reenviar la validación de los mandatarios
/// del organismo, pero no crea ni gestiona mandatarios de las compañías: esos son de cada compañía. De los suyos asigna
/// el mandatario general y, por compañía, el que firma sus trámites (nivel 1 de la prelación). El Super Admin no tiene
/// esta restricción.
/// </summary>
public sealed class OtGestionaSoloSusMandatariosFilter : IEndpointFilter
{
    public const string RouteKey = "mandateSignerId";

    public const string Code = "mandatario_de_compania";

    public const string Message =
        "Este mandatario es de una compañía y lo gestiona la compañía. Desde el organismo solo gestionas los mandatarios del organismo.";

    public const string AltaMessage =
        "Desde el organismo solo registras mandatarios del organismo. Los mandatarios de una compañía los registra la compañía.";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (CompanyTenantAccess.IsSuperAdmin(http.User))
        {
            return await next(context).ConfigureAwait(false);
        }

        // Alta: el cuerpo no puede traer compañías dueñas.
        if (context.Arguments.OfType<CreateMandateSignerRequest>().FirstOrDefault() is { } alta
            && alta.CompanyTenantIds is { Count: > 0 })
        {
            return Forbidden(AltaMessage);
        }

        if (http.Request.RouteValues.TryGetValue(RouteKey, out var raw)
            && Guid.TryParse(raw?.ToString(), out var signerId))
        {
            var reader = http.RequestServices.GetRequiredService<IMandateSignerReader>();
            var signer = await reader.GetByIdAsync(signerId, http.RequestAborted).ConfigureAwait(false);
            if (signer is { CompanyTenantIds.Count: > 0 })
            {
                return Forbidden(Message);
            }
        }

        return await next(context).ConfigureAwait(false);
    }

    private static IResult Forbidden(string message) =>
        Results.Json(new { code = Code, message }, statusCode: StatusCodes.Status403Forbidden);
}

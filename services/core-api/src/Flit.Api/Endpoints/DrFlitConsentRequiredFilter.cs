using System.Security.Claims;
using Flit.DrFlit.Application.Abstractions;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #12931 — el chat con IA y los casos de soporte envían datos a terceros (el LLM y Azure DevOps): sin
/// la aceptación de la versión vigente del tratamiento de datos responden 428 <c>consent_required</c> y
/// no se llama al LLM ni al sistema de soporte. El frontend pide la aceptación, pero la regla vive aquí:
/// un cliente que se salte la UI no la evade.
/// </summary>
internal sealed class DrFlitConsentRequiredFilter(
    IDrFlitConsentStore store,
    IDrFlitConsentSettings settings) : IEndpointFilter
{
    public const string ErrorCode = "consent_required";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var user = context.HttpContext.User;
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Sin usuario resuelto decide el endpoint (401): este filtro solo mira el consentimiento.
        if (!Guid.TryParse(raw, out var userId))
            return await next(context).ConfigureAwait(false);

        var version = settings.CurrentVersion;
        if (await store.HasAcceptedAsync(userId, version, context.HttpContext.RequestAborted).ConfigureAwait(false))
            return await next(context).ConfigureAwait(false);

        return Results.Problem(
            statusCode: StatusCodes.Status428PreconditionRequired,
            title: "Falta aceptar el tratamiento de datos",
            detail: "Para usar el asistente con IA o radicar un caso debes aceptar el tratamiento de datos de DR. FLIT.",
            extensions: new Dictionary<string, object?> { ["code"] = ErrorCode, ["version"] = version });
    }
}

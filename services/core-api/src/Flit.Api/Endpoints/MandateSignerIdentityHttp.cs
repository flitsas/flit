using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13246 (Feature #13245) — traducción HTTP de «Reenviar validación» del mandatario (rutas nuevas
/// <c>identity-validation/resend</c> de la compañía y del hub OT). Sin PII en las respuestas.
/// <list type="bullet">
///   <item>200 <c>{ identity: "sent"|"queued", validationId }</c> — <c>queued</c>: el proveedor falló de forma transitoria y la
///        validación quedó encolada para reintento.</item>
///   <item>404 — no existe, está eliminado o no es de ese organismo/compañía.</item>
///   <item>409 <c>{ code: "mandatario_no_requiere_validacion" }</c> — Persona jurídica, Formato en blanco o forma de firma baúl.</item>
///   <item>422 <c>{ errors: [{ field: "email", ... }] }</c> — la ficha no tiene correo (obligatorio con biometría).</item>
///   <item>502 <c>{ code: "proveedor_error" }</c> — el proveedor rechazó el envío de forma definitiva.</item>
/// </list>
/// </summary>
internal static class MandateSignerIdentityHttp
{
    public const string NoRequiereValidacionCode = "mandatario_no_requiere_validacion";

    public static IResult ToResult(ResendMandateSignerIdentityResult result, Guid mandateSignerId) =>
        result.Outcome switch
        {
            ResendMandateSignerIdentityOutcome.Sent =>
                Results.Ok(new { identity = "sent", validationId = result.ValidationId }),
            ResendMandateSignerIdentityOutcome.Queued =>
                Results.Ok(new { identity = "queued", validationId = result.ValidationId }),
            ResendMandateSignerIdentityOutcome.NotFound =>
                Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId}." }),
            ResendMandateSignerIdentityOutcome.NoRequiereValidacion =>
                Results.Json(
                    new
                    {
                        code = NoRequiereValidacionCode,
                        error = "Solo la Persona natural con forma de firma validación de identidad requiere validación.",
                    },
                    statusCode: StatusCodes.Status409Conflict),
            ResendMandateSignerIdentityOutcome.CorreoRequerido =>
                Results.Json(
                    new
                    {
                        errors = new[]
                        {
                            new
                            {
                                field = "email",
                                message = MandateSignerModelRules.CorreoRequeridoConBiometriaMessage,
                                value = (string?)null,
                            },
                        },
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Json(
                new { code = "proveedor_error", error = "El proveedor de identidad no pudo enviar la validación." },
                statusCode: StatusCodes.Status502BadGateway),
        };

    /// <summary>
    /// «Consultar estado»: 200 <c>{ status, updated }</c>; 404 sin ficha; 409 <c>mandatario_no_requiere_validacion</c> o
    /// <c>mandatario_sin_validacion</c> (aún no se envió); 503 <c>proveedor_no_disponible</c> (el worker sigue intentando).
    /// </summary>
    public static IResult ToResult(ReconcileMandateSignerIdentityResult result, Guid mandateSignerId) =>
        result.Outcome switch
        {
            ReconcileMandateSignerIdentityOutcome.Ok =>
                Results.Ok(new { status = result.Status, updated = result.Updated }),
            ReconcileMandateSignerIdentityOutcome.NotFound =>
                Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId}." }),
            ReconcileMandateSignerIdentityOutcome.NoRequiereValidacion =>
                Results.Json(
                    new
                    {
                        code = NoRequiereValidacionCode,
                        error = "Solo la Persona natural con forma de firma validación de identidad requiere validación.",
                    },
                    statusCode: StatusCodes.Status409Conflict),
            ReconcileMandateSignerIdentityOutcome.SinValidacion =>
                Results.Json(
                    new
                    {
                        code = "mandatario_sin_validacion",
                        error = "El mandatario aún no tiene validación de identidad. Envíala con «Reenviar validación».",
                    },
                    statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(
                new { code = "proveedor_no_disponible", error = "El proveedor de identidad no respondió. Intenta de nuevo en unos minutos." },
                statusCode: StatusCodes.Status503ServiceUnavailable),
        };
}

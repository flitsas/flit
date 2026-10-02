using System.Security.Claims;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Tramites.Domain.Documents;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13161 — helpers compartidos por los endpoints de mandatos (Plataforma, hub OT, mandatarios del OT y de la
/// compañía). Un cambio en el mapeo de errores o en la lectura del usuario se hace aquí, una sola vez; así no
/// reaparecen divergencias como la del <c>MapWrite</c> de Plataforma (HU #13126).
/// </summary>
internal static class MandateEndpointHelpers
{
    /// <summary>Id del usuario autenticado (<c>sub</c> o <c>nameidentifier</c>); nulo si no es un GUID.</summary>
    internal static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Resultado HTTP de una escritura de la configuración de mandato; igual en Plataforma y en el OT.</summary>
    internal static IResult MapWrite(MandateConfigWriteStatus status, MandateOtConfigView? view) =>
        status switch
        {
            MandateConfigWriteStatus.Ok => Results.Ok(view),
            MandateConfigWriteStatus.OfficeNotFound => Results.NotFound(),
            MandateConfigWriteStatus.CompanyNotFound => Results.NotFound(),
            MandateConfigWriteStatus.Conflict => Results.Conflict(new { error = "row_version_conflict" }),
            MandateConfigWriteStatus.InvalidTemplate => MandatoFormatResponses.InvalidTemplateCode(),
            MandateConfigWriteStatus.InvalidFamily => Results.BadRequest(new { error = "mandatary_family_invalida" }),
            MandateConfigWriteStatus.InvalidAssignmentMode =>
                Results.BadRequest(new { error = "assignment_mode_invalido" }),
            MandateConfigWriteStatus.InstitutionalRequired =>
                Results.BadRequest(new { error = "mandatario_institucional_requerido" }),
            MandateConfigWriteStatus.InvalidTemplateFile =>
                Results.BadRequest(new { error = "plantilla_pdf_invalida" }),
            MandateConfigWriteStatus.InvalidEditorBody =>
                Results.BadRequest(new { error = "editor_cuerpo_invalido" }),
            // HU #13126 — el default de mandatario no es válido.
            MandateConfigWriteStatus.InvalidDefaultSigner =>
                Results.BadRequest(new { error = "mandatario_default_invalido" }),
            _ => Results.BadRequest(),
        };
}

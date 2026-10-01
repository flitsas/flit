using Flit.Tramites.Domain.Documents;

namespace Flit.Api.Endpoints;

/// <summary>
/// Respuestas compartidas de los endpoints de mandatos que hablan del catálogo de formatos (HU #13168): el
/// código inválido y la lista de códigos válidos salen siempre de <see cref="MandatoFormatCatalog"/>, nunca de
/// una lista propia del endpoint.
/// </summary>
internal static class MandatoFormatResponses
{
    /// <summary>400 <c>template_code_invalido</c> con los códigos válidos para guardar la configuración (incluye auto).</summary>
    public static IResult InvalidTemplateCode() =>
        Results.Json(
            new { error = "template_code_invalido", allowed = MandatoFormatCatalog.Codes },
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>400 <c>template_code_invalido</c> con los códigos válidos para la vista previa por código.</summary>
    public static IResult InvalidPreviewCode() =>
        Results.Json(
            new { error = "template_code_invalido", allowed = MandatoFormatCatalog.RedactionCodes },
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Proyección del catálogo para el frontend (GET /mandatos/formatos).</summary>
    public static object Describe(MandatoFormatDefinition f) => new
    {
        code = f.Code,
        name = f.DefaultName,
        assignmentMode = f.DefaultAssignmentMode,
        baseRedaction = f.BaseRedaction,
        selectableAsRedaction = f.IsRedaction,
        delegatesToOfficeTemplate = !f.IsRedaction,
    };
}

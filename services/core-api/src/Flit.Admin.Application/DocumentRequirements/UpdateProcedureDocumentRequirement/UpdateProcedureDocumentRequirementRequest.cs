namespace Flit.Admin.Application.DocumentRequirements.UpdateProcedureDocumentRequirement;

/// <summary>
/// Payload de actualización de una asociación (HU #10195, AC3): solo se pueden cambiar
/// <c>{ ordenDefault, obligatorio }</c>. El par (trámite, documento) es inmutable.
/// </summary>
public sealed record UpdateProcedureDocumentRequirementRequest(
    int? OrdenDefault,
    bool? Obligatorio);

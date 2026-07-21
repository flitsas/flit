using System.Text.Json.Nodes;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

/// <summary>Regla de conformación (actor) con su perfil de validación (CFD-05).</summary>
public sealed record ConformationRuleProfileDto(string EntityCode, JsonNode ValidationProfile);

/// <summary>Fuente externa habilitada por el tipo (CFD-04). Poblada en HU-BE-03.</summary>
public sealed record ProcedureSourceDto(string SourceCode, int ExecutionOrder, JsonNode Config);

/// <summary>Documento requerido por el tipo (CFD-06). Poblado en HU-BE-04.</summary>
public sealed record ProcedureDocumentRequirementDto(
    string DocumentTypeCode, bool IsRequired, bool IsDummy, string? ConditionGroup);

/// <summary>
/// Perfil de conformación completo del tipo (§6.3 plan): <c>gateProfile</c> + <c>conformationRules</c>
/// + <c>sources</c> + <c>documentRequirements</c>. HU-BE-01 entrega la forma completa del contrato con
/// <c>gateProfile</c>/<c>conformationRules</c> materializados; <c>sources</c> y <c>documentRequirements</c>
/// llegan en HU-BE-03 / HU-BE-04 (aquí van como listas vacías, la clave siempre presente).
/// </summary>
public sealed record ProcedureConformationProfileDto(
    Guid ProcedureTypeId,
    string Code,
    string PublicationStatus,
    int Version,
    JsonNode GateProfile,
    IReadOnlyList<ConformationRuleProfileDto> ConformationRules,
    IReadOnlyList<ProcedureSourceDto> Sources,
    IReadOnlyList<ProcedureDocumentRequirementDto> DocumentRequirements);

public sealed class GetConformationProfileHandler(IProcedureTypeRepository repository)
{
    public async Task<(ProcedureConformationProfileDto? Result, string? Error)> HandleAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByIdWithDetailsAsync(id, ct);
        if (entity is null)
            return (null, "not_found");

        var rules = entity.ConformationRules
            .OrderBy(r => r.SortOrder)
            .Select(r => new ConformationRuleProfileDto(
                r.ProcedureEntity?.Code ?? string.Empty,
                ProfileJson.ParseOrEmpty(r.ValidationProfile)))
            .ToList();

        // HU-BE-01 (esqueleto): sources y documentRequirements se materializan en HU-BE-03 / HU-BE-04.
        // El contrato ya expone las 4 claves (AC BE-01-AC-05).
        var dto = new ProcedureConformationProfileDto(
            entity.Id,
            entity.Code,
            entity.PublicationStatus,
            entity.Version,
            ProfileJson.ParseOrEmpty(entity.GateProfile),
            rules,
            [],
            []);

        return (dto, null);
    }
}

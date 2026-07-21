using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

/// <summary>
/// Entrada del PUT de perfil de conformación (§6.3 plan). HU-BE-01 es el esqueleto base: solo persiste
/// <c>gateProfile</c>. Las HUs siguientes lo extienden — HU-BE-02 (entryMode + validaciones), HU-BE-03
/// (sources + conformationRules), HU-BE-04 (documentRequirements + flags identidad/firma), HU-BE-05
/// (requiresPlateRequest).
/// </summary>
public sealed record UpdateConformationProfileInput(JsonNode? GateProfile);

/// <summary>
/// Actualiza el perfil de conformación del tipo. Solo editable en estado <c>draft</c>: un tipo
/// <c>published</c> o <c>archived</c> devuelve <c>not_editable</c> (→ 422, AC BE-01-AC-06) para no
/// alterar tipos vivos ni los trámites en curso que dependen de ellos.
/// </summary>
public sealed class UpdateConformationProfileHandler(IProcedureTypeRepository repository)
{
    public async Task<(ProcedureConformationProfileDto? Result, string? Error)> HandleAsync(
        Guid id,
        UpdateConformationProfileInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entity = await repository.GetByIdWithDetailsAsync(id, ct);
        if (entity is null)
            return (null, "not_found");

        if (entity.PublicationStatus != PublicationStatus.Draft)
            return (null, "not_editable");

        // FEATURE-08 / HU-BE-02 (CFD-02): entryMode, si se envía, debe ser PLATE/VIN/BOTH.
        if (TryReadEntryMode(input.GateProfile, out var entryMode)
            && !ProcedureTypeGateProfile.IsValidEntryMode(entryMode))
        {
            return (null, "invalid_entry_mode");
        }

        if (input.GateProfile is not null)
            entity.GateProfile = input.GateProfile.ToJsonString();

        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        var rules = entity.ConformationRules
            .OrderBy(r => r.SortOrder)
            .Select(r => new ConformationRuleProfileDto(
                r.ProcedureEntity?.Code ?? string.Empty,
                ProfileJson.ParseOrEmpty(r.ValidationProfile)))
            .ToList();

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

    /// <summary>
    /// Lee <c>entryMode</c> del gate_profile de entrada. Devuelve <c>false</c> si está ausente o es
    /// JSON <c>null</c> (no hay nada que validar). Un valor no-string se devuelve como texto para que
    /// falle la validación de catálogo (no es PLATE/VIN/BOTH).
    /// </summary>
    private static bool TryReadEntryMode(JsonNode? gateProfile, out string? entryMode)
    {
        entryMode = null;
        var node = gateProfile?["entryMode"];
        if (node is null)
            return false;

        entryMode = node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToJsonString();
        return true;
    }
}

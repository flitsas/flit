using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

/// <summary>Fuente externa a habilitar por el tipo (CFD-04). <c>Config</c> lleva p. ej. <c>simitMode</c>.</summary>
public sealed record ConformationSourceInput(string SourceCode, int ExecutionOrder, JsonNode? Config);

/// <summary>Regla de conformación (actor) a persistir con su perfil de validación (CFD-05, incl. LESSEE).</summary>
public sealed record ConformationRuleUpsertInput(
    string EntityCode, JsonNode? ValidationProfile, bool IsActive = true, short SortOrder = 0);

/// <summary>
/// Entrada del PUT de perfil de conformación (§6.3 plan). HU-BE-01 persiste <c>gateProfile</c>;
/// HU-BE-02 valida <c>entryMode</c> + flags de validación; HU-BE-03 añade <c>sources</c> +
/// <c>conformationRules</c>. Campos opcionales: <c>null</c> = no tocar esa sección. HUs BE-04/05
/// amplían el <c>gateProfile</c> (documentos, comercial, identidad/firma, placa).
/// </summary>
public sealed record UpdateConformationProfileInput(
    JsonNode? GateProfile,
    IReadOnlyList<ConformationSourceInput>? Sources = null,
    IReadOnlyList<ConformationRuleUpsertInput>? ConformationRules = null);

/// <summary>
/// Actualiza el perfil de conformación del tipo. Solo editable en estado <c>draft</c>: un tipo
/// <c>published</c> o <c>archived</c> devuelve <c>not_editable</c> (→ 422, AC BE-01-AC-06) para no
/// alterar tipos vivos ni los trámites en curso que dependen de ellos. Las fuentes y las reglas de
/// conformación (HU-BE-03) se resuelven por código; código inexistente → <c>source_not_found</c> /
/// <c>entity_not_found</c>.
/// </summary>
public sealed class UpdateConformationProfileHandler(
    IProcedureTypeRepository repository,
    ICatalogRepository? catalogRepo = null,
    IProcedureTypeSourceRepository? sourceRepo = null)
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

        var now = DateTimeOffset.UtcNow;

        if (input.GateProfile is not null)
            entity.GateProfile = input.GateProfile.ToJsonString();
        entity.UpdatedAt = now;

        // FEATURE-08 / HU-BE-03 (CFD-05): reglas de conformación (actores, incl. LESSEE).
        List<ConformationRule>? persistedRules = null;
        if (input.ConformationRules is not null && catalogRepo is not null)
        {
            persistedRules = [];
            foreach (var r in input.ConformationRules)
            {
                var procEntity = await catalogRepo.GetProcedureEntityByCodeAsync(r.EntityCode, ct);
                if (procEntity is null)
                    return (null, $"entity_not_found:{r.EntityCode}");

                persistedRules.Add(new ConformationRule
                {
                    Id = Guid.NewGuid(),
                    ProcedureTypeId = entity.Id,
                    ProcedureEntityId = procEntity.Id,
                    ProcedureEntity = procEntity,
                    IsActive = r.IsActive,
                    SortOrder = r.SortOrder,
                    ValidationProfile = r.ValidationProfile?.ToJsonString() ?? "{}",
                    CreatedAt = now,
                });
            }

            await repository.ReplaceConformationRulesAsync(entity.Id, persistedRules, ct);
        }

        // FEATURE-08 / HU-BE-03 (CFD-04): fuentes externas por tipo (resueltas por código).
        if (input.Sources is not null && catalogRepo is not null && sourceRepo is not null)
        {
            var upserts = new List<ProcedureTypeSourceUpsert>(input.Sources.Count);
            foreach (var s in input.Sources)
            {
                var src = await catalogRepo.GetExternalDataSourceByCodeAsync(s.SourceCode, ct);
                if (src is null)
                    return (null, $"source_not_found:{s.SourceCode}");

                upserts.Add(new ProcedureTypeSourceUpsert(
                    src.Id, s.ExecutionOrder, s.Config?.ToJsonString() ?? "{}"));
            }

            await sourceRepo.ReplaceSourcesAsync(entity.Id, upserts, ct);
        }

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        if (input.Sources is not null && sourceRepo is not null)
            await sourceRepo.SaveChangesAsync(ct);

        var rules = (persistedRules ?? [.. entity.ConformationRules])
            .OrderBy(r => r.SortOrder)
            .Select(r => new ConformationRuleProfileDto(
                r.ProcedureEntity?.Code ?? string.Empty,
                ProfileJson.ParseOrEmpty(r.ValidationProfile)))
            .ToList();

        List<ProcedureSourceDto> sources = sourceRepo is null
            ? []
            : (await sourceRepo.ListByTypeAsync(entity.Id, ct))
                .Select(s => new ProcedureSourceDto(
                    s.SourceCode, s.ExecutionOrder, ProfileJson.ParseOrEmpty(s.Config)))
                .ToList();

        var dto = new ProcedureConformationProfileDto(
            entity.Id,
            entity.Code,
            entity.PublicationStatus,
            entity.Version,
            ProfileJson.ParseOrEmpty(entity.GateProfile),
            rules,
            sources,
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

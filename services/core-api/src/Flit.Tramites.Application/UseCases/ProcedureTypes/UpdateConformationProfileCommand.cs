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

/// <summary>Requisito documental a persistir por tipo (CFD-06). El documento se referencia por código.</summary>
public sealed record ConformationDocumentRequirementInput(
    string DocumentTypeCode, bool IsRequired = false, bool IsDummy = false,
    string? ConditionGroup = null, int SortOrder = 0);

/// <summary>
/// Entrada del PUT de perfil de conformación (§6.3 plan). HU-BE-01 persiste <c>gateProfile</c>;
/// HU-BE-02 valida <c>entryMode</c> + flags de validación; HU-BE-03 añade <c>sources</c> +
/// <c>conformationRules</c>; HU-BE-04 añade <c>documentRequirements</c> (y los flags comercial/
/// identidad/firma viajan dentro de <c>gateProfile</c>). Campos opcionales: <c>null</c> = no tocar.
/// </summary>
public sealed record UpdateConformationProfileInput(
    JsonNode? GateProfile,
    IReadOnlyList<ConformationSourceInput>? Sources = null,
    IReadOnlyList<ConformationRuleUpsertInput>? ConformationRules = null,
    IReadOnlyList<ConformationDocumentRequirementInput>? DocumentRequirements = null);

/// <summary>
/// Actualiza el perfil de conformación del tipo. Editable en <c>draft</c> y en <c>published</c>;
/// un tipo <c>archived</c> devuelve <c>not_editable</c> (→ 422).
/// <para>El AC BE-01-AC-06 original bloqueaba también los publicados «para no alterar los trámites
/// en curso». ADR-0050 quitó ese motivo: cada expediente congela su conformación en
/// <c>procedure_type_snapshots</c> al crearse, así que corregir el tipo no alcanza a ninguno vivo.
/// Editar un publicado sube <c>Version</c>.</para> Las fuentes y las reglas de
/// conformación (HU-BE-03) se resuelven por código; código inexistente → <c>source_not_found</c> /
/// <c>entity_not_found</c>.
/// </summary>
public sealed class UpdateConformationProfileHandler(
    IProcedureTypeRepository repository,
    ICatalogRepository? catalogRepo = null,
    IProcedureTypeSourceRepository? sourceRepo = null,
    IProcedureTypeDocumentRepository? docRepo = null)
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

        // ADR-0050 — un tipo PUBLICADO sí se puede corregir; un ARCHIVADO no.
        //
        // El candado original abarcaba ambos para no alterar los trámites en curso. Desde que cada
        // expediente congela su conformación en `procedure_type_snapshots` al crearse, editar el
        // tipo ya no puede afectar a ninguno vivo: siguen leyendo su snapshot. Sin este permiso no
        // había forma de corregir la parametrización del catálogo —los 21 tipos están publicados— y
        // «habilitar un trámite es configuración, no despliegue» se quedaba en la mitad.
        //
        // Editar un publicado SUBE la versión: los expedientes nuevos se conforman con la corregida
        // y queda registro de que el tipo cambió.
        if (entity.PublicationStatus == PublicationStatus.Archived)
            return (null, "not_editable");

        var eraPublicado = entity.PublicationStatus == PublicationStatus.Published;
        if (eraPublicado)
            entity.Version += 1;

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

        // FEATURE-08 / HU-BE-04 (CFD-06): requisitos documentales por tipo (resueltos por código).
        if (input.DocumentRequirements is not null && docRepo is not null)
        {
            var reqUpserts = new List<ProcedureDocumentRequirementUpsert>(input.DocumentRequirements.Count);
            foreach (var d in input.DocumentRequirements)
            {
                var docTypeId = await docRepo.ResolveDocumentTypeIdAsync(d.DocumentTypeCode, ct);
                if (docTypeId is null)
                    return (null, $"document_type_not_found:{d.DocumentTypeCode}");

                reqUpserts.Add(new ProcedureDocumentRequirementUpsert(
                    docTypeId.Value, d.IsRequired, d.IsDummy, d.ConditionGroup, d.SortOrder));
            }

            await docRepo.ReplaceRequirementsAsync(entity.Id, reqUpserts, ct);
        }

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        if (input.Sources is not null && sourceRepo is not null)
            await sourceRepo.SaveChangesAsync(ct);
        if (input.DocumentRequirements is not null && docRepo is not null)
            await docRepo.SaveChangesAsync(ct);

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

        List<ProcedureDocumentRequirementDto> documentRequirements = docRepo is null
            ? []
            : (await docRepo.ListByTypeAsync(entity.Id, ct))
                .Select(d => new ProcedureDocumentRequirementDto(
                    d.DocumentTypeCode, d.IsRequired, d.IsDummy, d.ConditionGroup))
                .ToList();

        var dto = new ProcedureConformationProfileDto(
            entity.Id,
            entity.Code,
            entity.PublicationStatus,
            entity.Version,
            ProfileJson.ParseOrEmpty(entity.GateProfile),
            rules,
            sources,
            documentRequirements);

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

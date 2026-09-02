using System.Text.Json.Nodes;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Captura el snapshot inmutable del tipo de trámite al crear una instancia (CFD-01 / AC#5).
/// El snapshot congela <c>gateProfile</c> + <c>conformationRules</c> + <c>stepSectionTypes</c> (NO los
/// <c>form_fields</c> completos, demasiado voluminosos) para que la instancia en curso vea siempre la
/// configuración con la que fue creada, aunque el tipo se re-publique con otra versión después.
/// </summary>
public sealed class CaptureTypeSnapshotHandler(
    IProcedureTypeRepository typeRepo,
    IProcedureTypeSnapshotRepository snapshotRepo)
{
    public async Task<(bool Captured, string? Error)> HandleAsync(
        Guid procedureInstanceId,
        Guid procedureTypeId,
        Guid tenantId,
        Guid? createdBy,
        CancellationToken ct = default)
    {
        var type = await typeRepo.GetByIdWithDetailsAsync(procedureTypeId, ct);
        if (type is null)
            return (false, "not_found");

        var snapshotJson = ProcedureTypeSnapshotBuilder.Build(type);
        var record = new ProcedureTypeSnapshotRecord(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            ProcedureInstanceId: procedureInstanceId,
            ProcedureTypeId: type.Id,
            TypeVersion: type.Version,
            Snapshot: snapshotJson,
            CreatedBy: createdBy);

        await snapshotRepo.AddAsync(record, ct);
        await snapshotRepo.SaveChangesAsync(ct);
        return (true, null);
    }
}

/// <summary>
/// Construye el JSON del snapshot liviano del tipo (CFD-01 / AC#5). Puro y determinista: la misma
/// entidad produce el mismo JSON, independientemente de cambios posteriores en el tipo live (el
/// snapshot es una copia por valor). Deliberadamente NO incluye <c>form_fields</c>.
/// </summary>
public static class ProcedureTypeSnapshotBuilder
{
    public static string Build(ProcedureType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var conformationRules = new JsonArray();
        foreach (var rule in type.ConformationRules.OrderBy(r => r.SortOrder))
        {
            conformationRules.Add(new JsonObject
            {
                ["entityCode"] = rule.ProcedureEntity?.Code ?? string.Empty,
                ["validationProfile"] = ProfileJson.ParseOrEmpty(rule.ValidationProfile),
            });
        }

        var stepSectionTypes = new JsonArray();
        foreach (var step in type.Steps.OrderBy(s => s.SortOrder))
        {
            var sectionTypes = new JsonArray();
            var sectionCodes = new JsonArray();
            foreach (var section in step.Sections.OrderBy(s => s.SortOrder))
            {
                sectionTypes.Add(section.SectionType);
                sectionCodes.Add(section.Code);
            }

            // `stepTitle` y `sectionCodes` los LEE `WizardStateQuery.FromSnapshot`, pero nadie los
            // escribía: el snapshot solo guardaba el código del paso y los tipos de sección. Dos
            // consecuencias, y la segunda es un gate:
            //
            //  · Sin `stepTitle`, todo paso de un expediente con snapshot caía al respaldo genérico
            //    (`SectionLabel`): «Actores» en vez de «Vendedor», «Comprador» o «Locatario». El
            //    nombre que el catálogo le da a la parte no llegaba a la pantalla.
            //  · Sin `sectionCodes`, `SectionCoversSeller(null)` devuelve TRUE por su primera
            //    condición, así que el paso de actores exigía la parte vendedora en un tipo cuyo
            //    recorrido no la tiene —el unilateral—: `RequiresSeller && true`.
            //
            // Los expedientes ya congelados no las traen y siguen cayendo al respaldo, que es el
            // comportamiento que tenían: el snapshot es una copia por valor y no se reescribe.
            stepSectionTypes.Add(new JsonObject
            {
                ["stepCode"] = step.Code,
                ["stepTitle"] = step.Title,
                ["sectionTypes"] = sectionTypes,
                ["sectionCodes"] = sectionCodes,
            });
        }

        var root = new JsonObject
        {
            ["code"] = type.Code,
            ["name"] = type.Name,
            ["family"] = type.Family,
            ["version"] = type.Version,
            ["gateProfile"] = ProfileJson.ParseOrEmpty(type.GateProfile),
            ["conformationRules"] = conformationRules,
            ["stepSectionTypes"] = stepSectionTypes,
        };

        return root.ToJsonString();
    }
}

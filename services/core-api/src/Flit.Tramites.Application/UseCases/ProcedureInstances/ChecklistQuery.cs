using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Ítem de checklist computado, en la forma congelada que consume el frontend.
/// </summary>
public sealed record ChecklistItemDto(
    string Key,
    string Label,
    bool Obligatorio,
    string? DocTipo,
    bool Satisfied);

public sealed record ChecklistResponse(
    IReadOnlyList<ChecklistItemDto> Items,
    IReadOnlyList<string> FaltanObligatorios,
    bool Completo);

/// <summary>
/// Computa el estado del checklist de una instancia: resuelve la tipología por
/// <c>tipologia_codigo</c> (o <c>modalidad_entrada</c>), combina el estado manual
/// (<c>checklist_estado</c>) con los tipos de documentos ya subidos (auto-marca) y
/// delega el cómputo en el dominio (<see cref="ChecklistEngine"/>).
/// </summary>
public sealed class GetChecklistHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ChecklistResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithActorsAndAttachmentsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();

        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);

        // HU #10542: para persona natural el documento de identidad se incorpora desde la
        // validación biométrica, así que el ítem de cédula no se ofrece como carga manual.
        var personTypeOverride = ChecklistPersonTypeRules.BuildOverride(
            instance.Actors.Select(a => a.PersonType).ToList());

        var computed = ChecklistEngine.ComputeWithOverride(codigo, manual, docTipos, personTypeOverride);
        if (computed is null)
            return (null, "tipologia_not_found");

        var items = computed.Items
            .Select(i => new ChecklistItemDto(
                i.Item.Id,
                i.Item.Label,
                i.Item.Obligatorio,
                i.Item.DocTipo,
                i.Satisfecho))
            .ToList();

        return (new ChecklistResponse(items, computed.FaltanObligatorios, computed.Completo), null);
    }
}

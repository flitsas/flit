using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record FieldValueInput(
    Guid? FormFieldId,
    string FieldKey,
    string? ValueText,
    string? ValueJson);

public sealed record PatchFieldValuesRequest(IReadOnlyList<FieldValueInput> Items);

public sealed class PatchFieldValuesHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        PatchFieldValuesRequest request,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != TramiteEstado.Borrador)
        {
            // Tras el envío solo se permiten claves de organismo de tránsito (generación
            // diferida del FUR). Cualquier otro campo sigue bloqueado en not_draft.
            var blocked = request.Items.Where(i =>
                !IsPostSubmitTransitOfficeKey(i.FieldKey)).ToList();
            if (blocked.Count > 0)
                return (null, "not_draft");
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var item in request.Items)
        {
            var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == item.FieldKey);
            if (existing is not null)
            {
                existing.ValueText = item.ValueText;
                existing.ValueJson = item.ValueJson;
                existing.Source = "user";
                existing.UpdatedAt = now;
            }
            else
            {
                // El front no conoce el form_field.id, así que al crear un value nuevo lo
                // resolvemos por field_key contra el grafo del procedure_type de la instancia.
                // Si el front sí mandó un Guid válido, se respeta tal cual.
                var formFieldId = item.FormFieldId;
                if (formFieldId is null || formFieldId == Guid.Empty)
                {
                    // Si no resuelve a un form_field (p.ej. claves de sistema/consulta como
                    // transit_office_*), se persiste como valor "loose" con FormFieldId = null
                    // en vez de rechazar con "unknown_field".
                    formFieldId = await repo.GetFormFieldIdByKeyAsync(instance.ProcedureTypeId, item.FieldKey, ct);
                }

                var fieldValue = new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcedureInstanceId = id,
                    FormFieldId = formFieldId,
                    FieldKey = item.FieldKey,
                    ValueText = item.ValueText,
                    ValueJson = item.ValueJson,
                    Source = "user",
                    CreatedAt = now
                };
                instance.FieldValues.Add(fieldValue);
                // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
                // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
                repo.Add(fieldValue);
            }
        }

        // La instancia se cargó trackeada (GetByIdWithDetailsAsync sin AsNoTracking), así que
        // el change tracker de EF detecta adds/updates sobre el grafo. NO se llama Update():
        // db.Update() sobre un grafo trackeado marcaría todo (incluidos los hijos NUEVOS con Id
        // ya seteado) como Modified → emitiría UPDATE de 0 filas en vez de INSERT.
        await repo.SaveChangesAsync(ct);

        return (GetProcedureInstanceHandler.ToDetail(instance), null);
    }

    private static bool IsPostSubmitTransitOfficeKey(string fieldKey) =>
        string.Equals(fieldKey, "transit_office_code", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKey, "transit_office_name", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKey, "transit_office_city", StringComparison.OrdinalIgnoreCase)
        // Feature #10587 — la compañía registra el estado del SOAT tras la asignación de placa
        // (la máquina de estados / el trigger de BD restringen a 'asignado').
        || string.Equals(fieldKey, "soat_estado", StringComparison.OrdinalIgnoreCase);
}

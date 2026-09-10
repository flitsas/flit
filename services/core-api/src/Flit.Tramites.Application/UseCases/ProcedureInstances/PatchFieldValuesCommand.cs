using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

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

        // B11 (HU #10659) — en TRASPASO el OT lo fija el RUNT (auto-bind en preflight) y NO es
        // editable por el usuario: cualquier PATCH de claves transit_office_* se rechaza. La excepción
        // post-submit (IsPostSubmitTransitOfficeKey) NO aplica en traspaso. Matrícula: sin cambios.
        var tipologia = instance.TypeCode;
        if (string.Equals(tipologia, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.Ordinal)
            && request.Items.Any(i => IsTransitOfficeKey(i.FieldKey)))
        {
            return (null, "ot_traspaso_no_modificable");
        }

        // ADR-0050 — sin trámites complementarios donde el tipo no los admite (familia OTROS). El
        // gate vive aquí porque este endpoint es la ÚNICA vía por la que el asistente declara una
        // transformación, y ocultar la tarjeta en pantalla no es una regla: un borrador reabierto,
        // un cliente viejo o un PATCH a mano volvían a colar un cambio de color por encima de un
        // blindaje. Lo que el tipo cambia por definición sí pasa — ahí el cambio ES el trámite.
        var complementoRechazado = PrimerComplementoNoAdmitido(instance, request.Items);
        if (complementoRechazado is not null)
            return (null, complementoRechazado);

        // Subsanación (flag sobre rechazado) o borrador: edición completa. Fuera de eso, tras el
        // envío solo se permiten claves de organismo de tránsito (generación diferida del FUR).
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
        {
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

    /// <summary>Error: el tipo no admite ese trámite por encima del suyo (familia OTROS).</summary>
    public const string ComplementoNoAdmitidoError = "complemento_no_admitido";

    /// <summary>Bandera declarativa de cada transformación → atributo que declara.</summary>
    private static readonly (string Key, TransformacionBase Cual)[] BanderasDeTransformacion =
    [
        (MandatoObjetoComposer.CambioColor, TransformacionBase.Color),
        (MandatoObjetoComposer.CambioCarroceria, TransformacionBase.Carroceria),
        (MandatoObjetoComposer.CambioCombustible, TransformacionBase.Combustible),
        (MandatoObjetoComposer.Blindaje, TransformacionBase.Blindaje),
    ];

    /// <summary>
    /// Valor EFECTIVO de cada atributo transformable y el snapshot RUNT contra el que se compara.
    /// La bandera no es la única vía: el FUR declara la transformación también por el diff
    /// RUNT ↔ efectivo (ver <c>FurCommand.Declarada</c>), así que bloquear solo la bandera dejaría
    /// abierta la puerta de atrás — basta escribir otro color para que el documento lo declare.
    /// </summary>
    private static readonly (string Key, string RuntKey, TransformacionBase Cual)[] ValoresTransformables =
    [
        ("vehicle_color", "vehicle_color_runt", TransformacionBase.Color),
        ("vehicle_body_type", "vehicle_body_type_runt", TransformacionBase.Carroceria),
        ("vehicle_fuel", "vehicle_fuel_runt", TransformacionBase.Combustible),
    ];

    /// <summary>
    /// Primer ítem del PATCH que declara una transformación que este tipo NO puede llevar encima, o
    /// <c>null</c> si todos son admisibles. Solo mira los tipos que no acumulan complementos: en
    /// matrícula y traspaso devuelve siempre <c>null</c> y el PATCH sigue como siempre.
    /// </summary>
    private static string? PrimerComplementoNoAdmitido(
        ProcedureInstance instance,
        IReadOnlyList<FieldValueInput> items)
    {
        var perfil = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        if (perfil.ComplementaryTransformationsAllowed(instance.ProcedureType?.Family))
            return null;

        var propia = ProcedureTypeLayers.TransformacionDelTipo(instance.ProcedureType?.Code);

        foreach (var item in items)
        {
            foreach (var (key, cual) in BanderasDeTransformacion)
            {
                if (!string.Equals(item.FieldKey, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                // Apagar una bandera siempre se permite: deshacer no es acumular, y es la única vía
                // para limpiar lo que un borrador anterior a esta regla dejó declarado.
                if (EsVerdadero(item.ValueText) && cual != propia)
                    return ComplementoNoAdmitidoError;
            }

            foreach (var (key, runtKey, cual) in ValoresTransformables)
            {
                if (!string.Equals(item.FieldKey, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (cual == propia)
                    continue;

                var runt = Persistido(instance, runtKey) ?? Enviado(items, runtKey);
                if (Difiere(runt, item.ValueText))
                    return ComplementoNoAdmitidoError;
            }
        }

        return null;
    }

    private static bool EsVerdadero(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static string? Persistido(ProcedureInstance instance, string fieldKey) =>
        instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

    private static string? Enviado(IReadOnlyList<FieldValueInput> items, string fieldKey) =>
        items.FirstOrDefault(i => string.Equals(i.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

    /// <summary>
    /// Mismo criterio que el FUR para «hubo transformación»: sin snapshot RUNT o sin valor efectivo no
    /// se declara nada, así que tampoco se rechaza nada (comparación trim + case-insensitive).
    /// </summary>
    private static bool Difiere(string? runt, string? efectivo) =>
        !string.IsNullOrWhiteSpace(runt)
        && !string.IsNullOrWhiteSpace(efectivo)
        && !string.Equals(runt.Trim(), efectivo.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsPostSubmitTransitOfficeKey(string fieldKey) =>
        string.Equals(fieldKey, "transit_office_code", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKey, "transit_office_name", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKey, "transit_office_city", StringComparison.OrdinalIgnoreCase)
        // Feature #10587 — la compañía registra el estado del SOAT tras la asignación de placa
        // (la máquina de estados / el trigger de BD restringen a 'asignado').
        || string.Equals(fieldKey, "soat_estado", StringComparison.OrdinalIgnoreCase);

    // B11 — toda clave del organismo de tránsito (incluye transit_office_id), para el bloqueo en traspaso.
    private static bool IsTransitOfficeKey(string fieldKey) =>
        fieldKey.StartsWith("transit_office_", StringComparison.OrdinalIgnoreCase);
}

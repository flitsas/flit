using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <param name="Source">
/// Origen del valor (<c>field_values.source</c>). <c>null</c> ⇒ <c>"user"</c>. Solo lo fijan llamadores de
/// SERVIDOR (p. ej. la orquestación ICT con <c>"ict"</c>); nunca se lee del JSON del PATCH HTTP
/// (<c>[JsonIgnore]</c>), para que un cliente no pueda hacerse pasar por otra fuente.
/// </param>
public sealed record FieldValueInput(
    Guid? FormFieldId,
    string FieldKey,
    string? ValueText,
    string? ValueJson,
    [property: System.Text.Json.Serialization.JsonIgnore] string? Source = null);

public sealed record PatchFieldValuesRequest(IReadOnlyList<FieldValueInput> Items);

/// <param name="validationPolicy">
/// Modos block/warn/off por ambiente. <c>null</c> ⇒ <see cref="TramiteValidationPolicy.BlockAll"/> (mismo
/// default que el resto de handlers). Aquí solo gobierna «cambio de carrocería sobre un vehículo sin carrocería».
/// </param>
public sealed class PatchFieldValuesHandler(
    IProcedureInstanceRepository repo,
    TramiteValidationPolicy? validationPolicy = null)
{
    private readonly TramiteValidationPolicy _validationPolicy = validationPolicy ?? TramiteValidationPolicy.BlockAll;

    public Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        PatchFieldValuesRequest request,
        CancellationToken ct = default) =>
        HandleCoreAsync(id, tenantId, request, semillaDeSistema: false, ct);

    /// <summary>
    /// Siembra de SISTEMA al materializar un borrador (Bug #13109): mismo escritor que el PATCH del
    /// gestor, salvo B11. B11 protege el OT del traspaso frente al USUARIO; aquí no hay usuario, sino el
    /// organismo que core-ict ya resolvió por el código de la transacción (activo + grant del tenant).
    /// Las demás reglas (complementos, edición solo en borrador) siguen aplicando. No lo expone ningún
    /// endpoint HTTP: solo lo invoca la orquestación gRPC de ICT.
    /// </summary>
    public Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleSystemSeedAsync(
        Guid id,
        Guid tenantId,
        PatchFieldValuesRequest request,
        CancellationToken ct = default) =>
        HandleCoreAsync(id, tenantId, request, semillaDeSistema: true, ct);

    private async Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleCoreAsync(
        Guid id,
        Guid tenantId,
        PatchFieldValuesRequest request,
        bool semillaDeSistema,
        CancellationToken ct)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // B11 (HU #10659) — en TRASPASO el OT lo fija el RUNT (auto-bind en preflight) y NO es
        // editable por el usuario: cualquier PATCH de claves transit_office_* se rechaza. La excepción
        // post-submit (IsPostSubmitTransitOfficeKey) NO aplica en traspaso. Matrícula: sin cambios.
        // La siembra de sistema (HandleSystemSeedAsync) no es una edición del usuario y no pasa por aquí.
        var tipologia = instance.TypeCode;
        if (!semillaDeSistema
            && string.Equals(tipologia, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.Ordinal)
            && request.Items.Any(i => IsTransitOfficeKey(i.FieldKey)))
        {
            return (null, "ot_traspaso_no_modificable");
        }

        // Matrícula ICT con placa de un rango del OT (plate_assignment_type=4): la placa está reservada en el
        // inventario de ESTE organismo, así que la secretaría queda FIJA. Este PATCH es el único camino por el
        // que el asistente cambia el organismo (la columna TransitOfficeId se promueve de estos field_values al
        // radicar). Re-enviar el mismo organismo no cambia nada y pasa.
        if (!semillaDeSistema)
        {
            var otFijado = OtFijadoPorPlacaDeRango(instance, request.Items);
            if (otFijado is not null)
                return (null, otFijado);
        }

        // Bug #13194 (review, SEC) — el estado y el vencimiento del SOAT son claves de SISTEMA: los
        // escriben la consulta al RUNT (ValidateSoatViaRuntHandler) y el OCR del PDF cargado
        // (PersistOcrFieldsHandler). Ningún flujo del front los manda por aquí; aceptarlos dejaba que un
        // gestor declarara «vigente» a mano y saltara el gate de «Enviar al OT».
        if (!semillaDeSistema && request.Items.Any(i => IsClaveSoatDeSistema(i.FieldKey)))
            return (null, ClaveDeSistemaError);

        // ADR-0050 — sin trámites complementarios donde el tipo no los admite (familia OTROS). El
        // gate vive aquí porque este endpoint es la ÚNICA vía por la que el asistente declara una
        // transformación, y ocultar la tarjeta en pantalla no es una regla: un borrador reabierto,
        // un cliente viejo o un PATCH a mano volvían a colar un cambio de color por encima de un
        // blindaje. Lo que el tipo cambia por definición sí pasa — ahí el cambio ES el trámite.
        var complementoRechazado = PrimerComplementoNoAdmitido(instance, request.Items);
        if (complementoRechazado is not null)
            return (null, complementoRechazado);

        // Cambio de carrocería SIMULTÁNEO sobre un vehículo que el RUNT reporta sin carrocería: no hay atributo
        // que sustituir. (Si el cambio de carrocería es el trámite principal lo cubren el preflight y el gate de
        // preparación.) Solo en modo block; en warn/off el preflight informa y no se detiene la edición.
        if (!semillaDeSistema && SinCarroceriaQueCambiar(instance, request.Items))
            return (null, VehicleBodyTypePolicy.ErrorCode);

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

        // Placa de rango ICT: el guard ya garantizó que lo que llega de plate/plate_assignment_type es el MISMO
        // valor; no se reescribe (evita guardar «  rng101 » sin normalizar y tocar la fila con origen «ict»).
        var placaFija = !semillaDeSistema && PlacaDeRangoIct(instance) is not null;

        foreach (var item in request.Items)
        {
            if (placaFija
                && !string.Equals(item.Source, "ict", StringComparison.Ordinal)
                && (string.Equals(item.FieldKey, "plate", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.FieldKey, "plate_assignment_type", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == item.FieldKey);
            if (existing is not null)
            {
                // HU #13264 — la marca de pago de FLITO no la sobrescribe la edición del gestor (subsanación).
                if (ExternalAttachmentRules.IsProtectedFlitoMark(existing.FieldKey, existing.Source))
                    continue;

                existing.ValueText = item.ValueText;
                existing.ValueJson = item.ValueJson;
                existing.Source = item.Source ?? "user";
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
                    Source = item.Source ?? "user",
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

    /// <summary>
    /// Código estable del error «el organismo está fijado por la placa de rango». El error viaja como
    /// <c>ot_fijado_por_placa_de_rango:{PLACA}</c>; ver <see cref="TryParseOtFijado"/>.
    /// </summary>
    public const string OtFijadoPorPlacaError = "ot_fijado_por_placa_de_rango";

    /// <summary>Extrae la placa de un error <see cref="OtFijadoPorPlacaError"/>; <c>false</c> si es otro error.</summary>
    public static bool TryParseOtFijado(string? error, out string placa)
    {
        placa = string.Empty;
        if (error is null || !error.StartsWith(OtFijadoPorPlacaError, StringComparison.Ordinal))
            return false;

        var separador = error.IndexOf(':', StringComparison.Ordinal);
        placa = separador >= 0 ? error[(separador + 1)..] : string.Empty;
        return true;
    }

    /// <summary>
    /// Código estable del error «la placa del rango está fijada». Viaja como <c>placa_fijada_por_rango:{PLACA}</c>;
    /// ver <see cref="TryParsePlacaFijada"/>.
    /// </summary>
    public const string PlacaFijadaPorRangoError = "placa_fijada_por_rango";

    /// <summary>Extrae la placa de un error <see cref="PlacaFijadaPorRangoError"/>; <c>false</c> si es otro error.</summary>
    public static bool TryParsePlacaFijada(string? error, out string placa)
    {
        placa = string.Empty;
        if (error is null || !error.StartsWith(PlacaFijadaPorRangoError, StringComparison.Ordinal))
            return false;

        var separador = error.IndexOf(':', StringComparison.Ordinal);
        placa = separador >= 0 ? error[(separador + 1)..] : string.Empty;
        return true;
    }

    private static readonly string[] ClavesCanonicasDeOrganismo =
    [
        TransitOfficeFieldKeys.Id,
        TransitOfficeFieldKeys.Code,
        TransitOfficeFieldKeys.Name,
        TransitOfficeFieldKeys.City,
        TransitOfficeFieldKeys.CityName,
    ];

    /// <summary>
    /// <c>ot_fijado_por_placa_de_rango:{PLACA}</c> si el trámite lleva <c>plate</c> con origen «ict» y
    /// <c>plate_assignment_type=4</c> y algún ítem cambia el organismo canónico; <c>null</c> en cualquier otro caso
    /// (incluido el re-envío del mismo organismo). Las claves <c>transit_office_actual_*</c>/<c>destino_*</c> son
    /// otros datos y no se tocan.
    /// </summary>
    /// <summary>La fila <c>plate</c> si es una placa de rango entregada por ICT (origen «ict» + tipo 4); si no, <c>null</c>.</summary>
    private static ProcedureInstanceFieldValue? PlacaDeRangoIct(ProcedureInstance instance)
    {
        var placa = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "plate", StringComparison.OrdinalIgnoreCase));
        return placa is not null
               && string.Equals(placa.Source, "ict", StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(placa.ValueText)
               && string.Equals(Persistido(instance, "plate_assignment_type")?.Trim(), "4", StringComparison.Ordinal)
            ? placa
            : null;
    }

    private static string? OtFijadoPorPlacaDeRango(ProcedureInstance instance, IReadOnlyList<FieldValueInput> items)
    {
        var placa = PlacaDeRangoIct(instance);
        if (placa is null)
            return null;

        foreach (var item in items)
        {
            // La placa y su tipo de asignación quedan fijos: con la placa reservada en el rango del OT, cambiarla
            // o el tipo soltaría el candado del organismo. Los ítems con origen «ict» los fija solo el servidor
            // (FieldValueInput.Source es [JsonIgnore]: nunca viene del PATCH HTTP), así que la orquestación ICT no
            // choca con este guard. Re-enviar el mismo valor (placa normalizada trim + mayúsculas; tipo «4») pasa.
            if (!string.Equals(item.Source, "ict", StringComparison.Ordinal))
            {
                var esPlaca = string.Equals(item.FieldKey, "plate", StringComparison.OrdinalIgnoreCase);
                var esTipo = string.Equals(item.FieldKey, "plate_assignment_type", StringComparison.OrdinalIgnoreCase);
                if (esPlaca
                    && !string.Equals(
                        placa.ValueText!.Trim().ToUpperInvariant(),
                        item.ValueText?.Trim().ToUpperInvariant(),
                        StringComparison.Ordinal))
                {
                    return $"{PlacaFijadaPorRangoError}:{placa.ValueText!.Trim()}";
                }

                if (esTipo && !string.Equals(item.ValueText?.Trim(), "4", StringComparison.Ordinal))
                    return $"{PlacaFijadaPorRangoError}:{placa.ValueText!.Trim()}";
            }

            var clave = ClavesCanonicasDeOrganismo.FirstOrDefault(c =>
                string.Equals(c, item.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (clave is null)
                continue;

            var actual = string.Equals(clave, TransitOfficeFieldKeys.Id, StringComparison.Ordinal)
                ? instance.TransitOfficeId?.ToString() ?? Persistido(instance, clave)
                : Persistido(instance, clave);
            // Sin valor vigente no hay nada que se esté cambiando (el Id es quien gobierna el OT).
            if (string.IsNullOrWhiteSpace(actual))
                continue;

            if (!MismoValorDeOrganismo(actual, item.ValueText))
                return $"{OtFijadoPorPlacaError}:{placa.ValueText!.Trim()}";
        }

        return null;
    }

    private static bool MismoValorDeOrganismo(string actual, string? nuevo)
    {
        if (string.IsNullOrWhiteSpace(nuevo))
            return false;
        if (Guid.TryParse(actual, out var a) && Guid.TryParse(nuevo, out var n))
            return a == n;
        return string.Equals(actual.Trim(), nuevo.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ¿El PATCH declara <c>cambio_carroceria=true</c> como trámite simultáneo sobre un vehículo que el RUNT
    /// reporta sin carrocería? Reutiliza <see cref="VehicleBodyTypePolicy.SinCarroceria"/> (no duplica la regla)
    /// y su semántica: sin consulta RUNT previa (ni clase ni marca hidratadas) no se sabe, así que no se bloquea.
    /// Solo en modo <see cref="TramiteValidationMode.Block"/>.
    /// </summary>
    private bool SinCarroceriaQueCambiar(ProcedureInstance instance, IReadOnlyList<FieldValueInput> items)
    {
        if (_validationPolicy.VehicleBodyTypeRequired != TramiteValidationMode.Block)
            return false;
        if (!items.Any(i => string.Equals(i.FieldKey, MandatoObjetoComposer.CambioCarroceria, StringComparison.OrdinalIgnoreCase)
                            && EsVerdadero(i.ValueText)))
            return false;
        // Como trámite principal ya lo cubren el preflight y el gate de preparación: no se toca ese comportamiento.
        if (VehicleBodyTypePolicy.ExigeCarroceriaPrevia(instance.TypeCode))
            return false;

        var consultaRespondio = !string.IsNullOrWhiteSpace(Persistido(instance, VehicleFieldKeys.Class))
                                || !string.IsNullOrWhiteSpace(Persistido(instance, VehicleFieldKeys.Brand));
        return consultaRespondio
               && VehicleBodyTypePolicy.SinCarroceria(Persistido(instance, VehicleBodyTypePolicy.BodyTypeRuntFieldKey));
    }

    /// <summary>Error: el PATCH intenta escribir una clave que solo escribe el sistema (Bug #13194).</summary>
    public const string ClaveDeSistemaError = "clave_de_sistema";

    /// <summary>Claves del SOAT que solo escribe el sistema (consulta RUNT u OCR del PDF), nunca el PATCH.</summary>
    private static readonly string[] ClavesSoatDeSistema = [SoatGate.FieldKey, "soat_vencimiento"];

    private static bool IsClaveSoatDeSistema(string fieldKey) =>
        ClavesSoatDeSistema.Contains(fieldKey, StringComparer.OrdinalIgnoreCase);

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
        || string.Equals(fieldKey, "transit_office_city", StringComparison.OrdinalIgnoreCase);
    // Feature #10587 — soat_estado ya no entra por aquí: es clave de sistema (Bug #13194). En 'asignado'
    // lo registran la validación ante el RUNT y el OCR del PDF del SOAT, no el PATCH del gestor.

    // B11 — toda clave del organismo de tránsito (incluye transit_office_id), para el bloqueo en traspaso.
    private static bool IsTransitOfficeKey(string fieldKey) =>
        fieldKey.StartsWith("transit_office_", StringComparison.OrdinalIgnoreCase);
}

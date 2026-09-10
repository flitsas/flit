using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// CF-02 (HU #10879, AC5) — creación del trámite al AVANZAR del paso 1 al paso 2, con el vehículo ya
/// consultado. <see cref="PreviewToken"/> apunta a la consulta que resolvió el paso 1
/// (<see cref="IPreflightPreviewStore"/>); si falta o expiró, el preflight vuelve a consultar al
/// proveedor (degradación, nunca error).
/// </summary>
public sealed record CreateFromConsultaRequest(
    Guid TenantId,
    Guid CreatedByUserId,
    /// <summary>
    /// Familia heredada (<c>matricula_inicial</c> / <c>traspaso</c>). Se mantiene para los clientes
    /// que aún no envían <see cref="ProcedureTypeCode"/>; ADR-0050 la sustituye por el código del
    /// tipo, que es lo que el gestor eligió del catálogo.
    /// </summary>
    string Modalidad,
    string? Vin,
    string? Plate,
    string? OwnerDocumentType,
    string? OwnerDocumentNumber,
    string? PreviewToken,
    /// <summary>
    /// HU #11199 — secretaría elegida en el primer paso. OBLIGATORIA en matrícula inicial; en traspaso
    /// llega nula porque el organismo lo impone el RUNT y lo fija el preflight (B11, HU #10659).
    /// </summary>
    Guid? TransitOfficeId = null,
    /// <summary>
    /// HU sin ADO 2026-08-11 — casilla 18 del FUR (tipo de servicio), elegido por el operador. Opcional
    /// y solo aplica en MATRÍCULA INICIAL (mismo criterio que <see cref="TransitOfficeId"/>: en
    /// traspaso se ignora sin rechazar la creación, porque el traspaso hidrata <c>vehicle_service</c>
    /// con texto libre del RUNT y este canal no le compete). Si viene, debe ser uno de los 6 códigos
    /// cerrados de <see cref="VehicleServiceTypeCode"/> — se valida estricto porque es entrada de un
    /// selector cerrado, no texto libre: un valor fuera del catálogo es un bug del caller, no un dato
    /// legítimo que debamos tolerar en silencio.
    /// </summary>
    string? TipoServicioCode = null,
    /// <summary>
    /// HU sin ADO 2026-08-11 — casilla 19 del FUR (empresa vinculadora). Solo tiene efecto cuando
    /// <see cref="TipoServicioCode"/> resuelve a <c>PUBLICO</c>: con cualquier otro tipo (o sin
    /// tipo) se IGNORAN sin rechazar la creación — mismo criterio de tolerancia que
    /// <see cref="TransitOfficeId"/> fuera de matrícula. Ambos opcionales: si faltan, la casilla 19
    /// queda en blanco (comportamiento por defecto, sin romper trámites existentes).
    /// </summary>
    string? EmpresaVinculadoraNit = null,
    string? EmpresaVinculadoraRazonSocial = null,
    /// <summary>
    /// ADR-0050 — <c>code</c> del tipo elegido en el catálogo. Cuando viene, MANDA sobre
    /// <see cref="Modalidad"/>: es el único dato con el que se puede crear un trámite de la familia
    /// OTROS, que por modalidad caía siempre en matrícula inicial.
    /// </summary>
    string? ProcedureTypeCode = null);

public sealed record CreateFromConsultaResult(
    ProcedureInstanceSummary Instance,
    PreflightSnapshotDto? Preflight);

/// <summary>
/// Crea el trámite en borrador CON los datos del vehículo ya validados y deja el preflight persistido,
/// en una sola operación. Es el reemplazo del "crear al entrar al wizard": antes de este punto no
/// existe registro alguno (AC3), de modo que abandonar el paso 1 no deja borradores vacíos.
///
/// <para>Orden deliberado: se re-verifica la duplicidad ANTES de persistir nada — entre la consulta del
/// paso 1 y el avance al paso 2 otro operador pudo abrir un trámite para el mismo VIN/placa. Solo si la
/// llave sigue libre se crea la instancia, se persisten los identificadores capturados y se corre el
/// preflight autoritativo reusando la consulta del paso 1.</para>
/// </summary>
public sealed class CreateProcedureInstanceFromConsultaHandler(
    IProcedureInstanceRepository repo,
    CreateProcedureInstanceHandler createHandler,
    PatchFieldValuesHandler patchHandler,
    RunPreflightHandler preflightHandler,
    IPreflightPreviewStore previewStore,
    ITransitOfficeResolver transitOfficeResolver,
    TramiteValidationPolicy? validationPolicy = null,
    IOtOperabilityGate? otOperability = null,
    IProcedureTypeRepository? typeRepo = null,
    // ADR-0051 Decisión 5 — best-effort, opcional: los tests que no lo inyectan simplemente no
    // sincronizan (comportamiento previo a esta pieza).
    SyncSellerActorFromConsultationsHandler? sellerSyncHandler = null)
{
    // HU #10970 — mismo modo por ambiente que el resto del flujo. Sin inyectar ⇒ bloqueo duro.
    private readonly TramiteValidationPolicy _validationPolicy =
        validationPolicy ?? TramiteValidationPolicy.BlockAll;

    // HU #11200 — misma pareja de comprobaciones que el paso 1 y que la radicación: grant vigente +
    // organismo operativo. Sin inyectar ⇒ permisivo (solo queda el grant).
    private readonly IOtOperabilityGate _otOperability = otOperability ?? NullOtOperabilityGate.Instance;

    public async Task<(CreateFromConsultaResult? Result, string? Error, Guid? ExistingProcedureInstanceId, VehicleStateBlock? VehicleState)> HandleAsync(
        CreateFromConsultaRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ADR-0050 — el tipo elegido manda. Sin él (clientes anteriores) se cae a la familia
        // heredada, que solo sabe distinguir matrícula de traspaso.
        ProcedureType? procedureType = null;
        if (typeRepo is not null && !string.IsNullOrWhiteSpace(request.ProcedureTypeCode))
        {
            procedureType = await typeRepo
                .GetByCodePublishedAsync(request.ProcedureTypeCode!.Trim(), ct)
                .ConfigureAwait(false);
            if (procedureType is null)
                return (null, "procedure_type_not_found", null, null);
        }

        var modalidad = procedureType is not null
            ? ProcedureFamilyCodes.FromCodeOrOtros(procedureType.Family)
            : ProcedureFamilyCodes.FromCodeOrLegacyModalidad(request.Modalidad);
        if (modalidad is null)
            return (null, "modalidad_not_available", null, null);

        // Qué identificador exige el trámite lo declara el tipo (`entryMode`), no la familia: es la
        // diferencia entre pedir el VIN de un vehículo sin placa y la placa de uno ya matriculado.
        // Un trámite de la familia OTROS entra por placa, y por familia habría entrado por VIN.
        var perfilTipo = ProcedureTypeGateProfile.FromJson(procedureType?.GateProfile);
        var entraPorVin = procedureType is not null
            ? string.Equals(perfilTipo.EntryMode, "VIN", StringComparison.OrdinalIgnoreCase)
            : modalidad == ProcedureFamily.Matriculas;

        // La casilla 18 sigue siendo cosa de los trámites que MATRICULAN el vehículo, que son
        // exactamente los que entran por VIN.
        var esMatricula = entraPorVin;

        // La SECRETARÍA, en cambio, ya no se deduce del identificador: un radicado de cuenta entra
        // por placa y aun así la elige el operador, porque el trámite consiste en llevar la cuenta a
        // otro organismo. Lo declara el tipo.
        //
        // Sin tipo resuelto se cae a `entraPorVin` —es decir, a la familia— y NO al perfil por
        // defecto: un perfil vacío diría «no la elige el operador» y una matrícula creada por
        // modalidad heredada se quedaría sin escribir su organismo.
        var exigeSecretaria = procedureType is not null
            ? perfilTipo.OperatorChoosesTransitOffice()
            : entraPorVin;
        var vin = Trim(request.Vin);
        var plate = Trim(request.Plate)?.ToUpperInvariant();

        if (entraPorVin ? vin is null : plate is null)
            return (null, "identificador_requerido", null, null);

        // HU #11199 (AC1/AC3) — la secretaría elegida en el paso 1 se re-confirma aquí, no se copia del
        // preview: entre la consulta y el avance al paso 2 pudieron revocar el grant o desactivar el
        // organismo, y este es el punto donde la elección se vuelve permanente.
        ResolvedTransitOffice? secretaria = null;
        if (exigeSecretaria)
        {
            if (request.TransitOfficeId is not { } elegido || elegido == Guid.Empty)
                return (null, TransitOfficeSelectionPolicy.RequiredErrorCode, null, null);

            secretaria = await transitOfficeResolver
                .ResolveEnabledByIdAsync(request.TenantId, elegido, ct)
                .ConfigureAwait(false);
            if (secretaria is null
                || !await _otOperability.IsOperableAsync(secretaria.Id, ct).ConfigureAwait(false))
                return (null, TransitOfficeSelectionPolicy.UnavailableErrorCode, null, null);
        }

        // HU sin ADO 2026-08-11 — tipoServicioCode (casilla 18) se valida ANTES de crear nada, igual
        // que la secretaría arriba: es entrada de un selector cerrado (VehicleServiceTypeCode), así
        // que un valor fuera del catálogo es un bug del caller y se rechaza en vez de crear el trámite
        // y dejar la casilla en un estado indefinido. Fuera de matrícula inicial NO se valida ni se
        // usa (ver más abajo): mismo criterio de tolerancia que TransitOfficeId en traspaso.
        var tipoServicioCode = Trim(request.TipoServicioCode)?.ToUpperInvariant();
        if (esMatricula && tipoServicioCode is not null
            && !VehicleServiceTypeCode.All.Any(c => string.Equals(c, tipoServicioCode, StringComparison.Ordinal)))
        {
            return (null, "invalid_tipo_servicio", null, null);
        }

        // CF-01 antes de persistir: si la llave se ocupó mientras el operador revisaba el paso 1, se
        // bloquea SIN dejar registro (el objetivo de CF-02 es justamente no crear trámites inservibles).
        // HU #10970 — solo en modo block. En warn/off el trámite SÍ se crea y es el preflight de abajo
        // (que aplica el mismo modo) el que deja el check amarillo o no deja nada; así el paso 1→2 no
        // diverge del semáforo que se persiste.
        if (_validationPolicy.DuplicateActiveProcedure == TramiteValidationMode.Block)
        {
            var duplicateId = await FindDuplicateAsync(modalidad.Value, request.TenantId, vin, plate, ct);
            if (duplicateId is not null)
                return (null, InitialProcedureValidationGate.DuplicateActiveProcedure, duplicateId, null);
        }

        var (summary, createError) = await createHandler.HandleAsync(
            new CreateProcedureInstanceRequest(
                request.TenantId,
                ProcedureTypeId: null,
                request.CreatedByUserId,
                request.TransitOfficeId,
                Modalidad: request.Modalidad,
                ProcedureTypeCode: procedureType?.Code),
            ct);

        if (createError is not null || summary is null)
            return (null, createError ?? "invalid_request", null, null);

        var items = new List<FieldValueInput>();
        // Casillas 18/19 del FUR: se escriben en un patch APARTE, después del preflight (ver abajo).
        var tipoServicioFieldValues = new List<FieldValueInput>();
        // El organismo elegido se escribe SIEMPRE que lo elija el operador, entre por VIN o por placa.
        // HU #11199 (AC4) — queda escrito con el trámite, así que el paso del FUR ya no tiene nada que
        // preguntar. `transit_office_origen` distingue estos trámites de los borradores anteriores al
        // cambio, que siguen eligiendo el organismo en el FUR (D8).
        if (exigeSecretaria)
        {
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.Id, secretaria!.Id.ToString(), null));
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.Code, secretaria.Code, null));
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.Name, secretaria.Name, null));
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.City, secretaria.CityCode, null));
            if (!string.IsNullOrWhiteSpace(secretaria.CityName))
                items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.CityName, secretaria.CityName, null));
            items.Add(new FieldValueInput(
                null,
                TransitOfficeSelectionPolicy.OrigenFieldKey,
                TransitOfficeSelectionPolicy.OrigenPasoUno,
                null));
        }

        if (esMatricula)
        {
            items.Add(new FieldValueInput(null, "vin", vin, null));

            // HU sin ADO 2026-08-11 — casilla 18 (tipo de servicio) y casilla 19 (empresa vinculadora)
            // del FUR. `vehicle_service` es el MISMO field_value que hidrata el RUNT en traspaso
            // (KyverumRuntVehicleResultMapper / IntempoVehicleResultMapper / VerifikResultMapper con
            // texto libre); aquí se persiste el CÓDIGO cerrado que el operador eligió en matrícula
            // inicial. FurFieldMapper.MarkServicio (vía VehicleServiceTypeCode.Resolve) normaliza
            // cualquiera de las dos formas a una sola casilla — ver ese normalizador para la
            // precedencia entre texto libre y código.
            if (tipoServicioCode is not null)
            {
                // Se acumulan aparte y se escriben DESPUÉS del preflight — ver el comentario largo en
                // el punto de escritura, más abajo: la hidratación del vehículo pisa `vehicle_service`.
                tipoServicioFieldValues.Add(new FieldValueInput(null, "vehicle_service", tipoServicioCode, null));

                // NIT/razón social de la empresa vinculadora SOLO tienen sentido con servicio PÚBLICO.
                // Con cualquier otro tipo se IGNORAN sin rechazar la creación (no un 400): mismo
                // criterio de tolerancia que ya usa este handler con TransitOfficeId fuera de
                // matrícula — un dato irrelevante para el contexto elegido se descarta en silencio, no
                // bloquea el trámite. Peor caso, si llegaran igual: casilla 19 queda en blanco, que es
                // lo correcto para un tipo no público.
                if (string.Equals(tipoServicioCode, VehicleServiceTypeCode.Publico, StringComparison.Ordinal))
                {
                    // Viajan con el tipo de servicio para que las tres llaves de la casilla 18/19 se
                    // escriban en el mismo punto y no se puedan desincronizar.
                    if (Trim(request.EmpresaVinculadoraNit) is { } evNit)
                        tipoServicioFieldValues.Add(new FieldValueInput(null, "empresa_vinculadora_nit", evNit, null));
                    if (Trim(request.EmpresaVinculadoraRazonSocial) is { } evRazonSocial)
                        tipoServicioFieldValues.Add(
                            new FieldValueInput(null, "empresa_vinculadora_razon_social", evRazonSocial, null));
                }
            }
        }
        else
        {
            items.Add(new FieldValueInput(null, "plate", plate, null));
            // owner_document_* viaja siempre (aunque la UI lo oculte con Kyverum): el fallback a
            // Verifik lo exige para consultar por placa. Misma convención que el paso 1 previo.
            items.Add(new FieldValueInput(null, "owner_document_type", Trim(request.OwnerDocumentType), null));
            items.Add(new FieldValueInput(null, "owner_document_number", Trim(request.OwnerDocumentNumber), null));
        }

        var (_, patchError) = await patchHandler.HandleAsync(
            summary.Id, request.TenantId, new PatchFieldValuesRequest(items), ct);
        if (patchError is not null)
            return (null, patchError, null, null);

        // ADR-0051 Decisión 5 — TRASPASO_UNILATERAL (y cualquier tipo futuro con la misma combinación
        // de capacidades) no captura al vendedor por formulario: sin esto, ningún camino crea el actor
        // "vendedor" y FinalizeDraftGate bloquearía el 100% de sus borradores con actores_incompletos.
        // Reusa el documento ya tecleado en el paso 1 (owner_document_type/number, recién persistidos
        // arriba) para un lookup best-effort — RUNT si es persona natural, RUES si es NIT — que nunca
        // bloquea la creación del trámite.
        if (perfilTipo.RequiresSeller && !perfilTipo.SellerCapturedViaForm && sellerSyncHandler is not null)
        {
            await sellerSyncHandler.SyncAsync(
                summary.Id, request.TenantId, request.OwnerDocumentType, request.OwnerDocumentNumber, ct);
        }

        // Preflight autoritativo sobre la instancia real, reusando la consulta del paso 1: hidrata los
        // atributos del vehículo, fija el OT en traspaso y persiste el snapshot, sin segunda llamada al
        // proveedor externo. Sin token (expirado / otra instancia del servicio) consulta de nuevo.
        var precomputed = previewStore.TryTake(request.TenantId, request.PreviewToken);
        var (preflight, preflightError, existingId, vehicleState) =
            await preflightHandler.HandleAsync(summary.Id, request.TenantId, precomputed, ct);

        // El tipo de servicio elegido por el operador se escribe DESPUÉS del preflight, a propósito.
        //
        // El preflight hidrata los atributos del vehículo desde el proveedor de consulta, y entre ellos
        // viene `vehicle_service`. En TRASPASO eso es correcto: el vehículo está matriculado y el RUNT
        // es la fuente de su tipo de servicio. En MATRÍCULA INICIAL no lo es —el vehículo aún no existe
        // en el RUNT— pero los proveedores devuelven el campo igual (hoy con "Particular" fijo en sus
        // datos de demo), así que si se escribiera antes, la hidratación pisaría la elección del
        // operador y la casilla 18 del FUR saldría marcada en "Particular" habiendo elegido "Público".
        //
        // Va antes del `return` por error del preflight a propósito: si el preflight falla el trámite
        // YA existe, y perder la elección del operador obligaría a rehacerla.
        if (esMatricula && tipoServicioFieldValues.Count > 0)
        {
            var (_, tipoServicioPatchError) = await patchHandler.HandleAsync(
                summary.Id, request.TenantId, new PatchFieldValuesRequest(tipoServicioFieldValues), ct);
            if (tipoServicioPatchError is not null && preflightError is null)
                return (null, tipoServicioPatchError, null, null);
        }

        if (preflightError is not null)
            return (null, preflightError, existingId, vehicleState);

        return (new CreateFromConsultaResult(summary, preflight), null, null, null);
    }

    private async Task<Guid?> FindDuplicateAsync(
        ProcedureFamily modalidad,
        Guid tenantId,
        string? vin,
        string? plate,
        CancellationToken ct)
    {
        if (modalidad == ProcedureFamily.Matriculas)
        {
            var vinNorm = VinNormalizer.Normalize(vin);
            if (vinNorm is null)
                return null;

            var existentes = await repo.FindTramitesByVinAsync(tenantId, vinNorm, Guid.Empty, ct);
            return DuplicateActiveProcedurePolicy.FindActiveDuplicate(
                existentes.Select(e => (e.Id, e.Estado, e.SubsanacionActiva)).ToList());
        }

        if (modalidad == ProcedureFamily.Traspaso && !string.IsNullOrEmpty(plate))
        {
            var existentes = await repo.FindTramitesByPlacaAsync(tenantId, plate, Guid.Empty, ct);
            return DuplicateActiveProcedurePolicy.FindActiveDuplicate(
                existentes.Select(e => (e.Id, e.Estado, e.SubsanacionActiva)).ToList());
        }

        return null;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

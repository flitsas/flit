using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

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
    string Modalidad,
    string? Vin,
    string? Plate,
    string? OwnerDocumentType,
    string? OwnerDocumentNumber,
    string? PreviewToken,
    Guid? TransitOfficeId = null);

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
    TramiteValidationPolicy? validationPolicy = null)
{
    // HU #10970 — mismo modo por ambiente que el resto del flujo. Sin inyectar ⇒ bloqueo duro.
    private readonly TramiteValidationPolicy _validationPolicy =
        validationPolicy ?? TramiteValidationPolicy.BlockAll;

    public async Task<(CreateFromConsultaResult? Result, string? Error, Guid? ExistingProcedureInstanceId, VehicleStateBlock? VehicleState)> HandleAsync(
        CreateFromConsultaRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modalidad = TramiteModalidadEntradaCodes.FromCode(request.Modalidad);
        if (modalidad is null)
            return (null, "modalidad_not_available", null, null);

        var esMatricula = modalidad == TramiteModalidadEntrada.MatriculaInicial;
        var vin = Trim(request.Vin);
        var plate = Trim(request.Plate)?.ToUpperInvariant();

        if (esMatricula ? vin is null : plate is null)
            return (null, "identificador_requerido", null, null);

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
                Modalidad: request.Modalidad),
            ct);

        if (createError is not null || summary is null)
            return (null, createError ?? "invalid_request", null, null);

        var items = new List<FieldValueInput>();
        if (esMatricula)
        {
            items.Add(new FieldValueInput(null, "vin", vin, null));
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

        // Preflight autoritativo sobre la instancia real, reusando la consulta del paso 1: hidrata los
        // atributos del vehículo, fija el OT en traspaso y persiste el snapshot, sin segunda llamada al
        // proveedor externo. Sin token (expirado / otra instancia del servicio) consulta de nuevo.
        var precomputed = previewStore.TryTake(request.TenantId, request.PreviewToken);
        var (preflight, preflightError, existingId, vehicleState) =
            await preflightHandler.HandleAsync(summary.Id, request.TenantId, precomputed, ct);

        if (preflightError is not null)
            return (null, preflightError, existingId, vehicleState);

        return (new CreateFromConsultaResult(summary, preflight), null, null, null);
    }

    private async Task<Guid?> FindDuplicateAsync(
        TramiteModalidadEntrada modalidad,
        Guid tenantId,
        string? vin,
        string? plate,
        CancellationToken ct)
    {
        if (modalidad == TramiteModalidadEntrada.MatriculaInicial)
        {
            var vinNorm = VinNormalizer.Normalize(vin);
            if (vinNorm is null)
                return null;

            var existentes = await repo.FindTramitesByVinAsync(tenantId, vinNorm, Guid.Empty, ct);
            return DuplicateActiveProcedurePolicy.FindActiveDuplicate(
                existentes.Select(e => (e.Id, e.Estado, e.SubsanacionActiva)).ToList());
        }

        if (modalidad == TramiteModalidadEntrada.Traspaso && !string.IsNullOrEmpty(plate))
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

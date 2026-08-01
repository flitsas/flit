using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gestor/radicador en sub-estado <c>asignado</c>: marca checks opcionales (SOAT / impuesto) y
/// avanza a <c>terminado</c> para que el OT pueda aprobar/rechazar.
/// </summary>
public sealed record CompletePlateFlowRequest(
    bool? SoatPagado = null,
    bool? ImpuestoDepartamentalPagado = null);

public sealed class CompletePlateFlowHandler(
    IProcedureInstanceRepository repo,
    ValidateSoatViaRuntHandler? soatValidator = null,
    ISoatRuntValidationPolicy? soatPolicy = null)
{
    /// <summary>
    /// El RUNT no reporta un SOAT vigente y la compañía tiene apagada la opción de continuar sin él.
    /// </summary>
    public const string SoatNoVigente = "soat_no_vigente";

    /// <summary>
    /// El trámite avanzó a Terminado SIN SOAT vigente porque la compañía tiene activa la opción de
    /// continuar. No es un error: el gestor tiene que enterarse de que lo envió al OT así.
    /// </summary>
    public const string SoatNoVigenteAdvertencia = "soat_no_vigente_advertencia";

    private readonly ISoatRuntValidationPolicy _soatPolicy =
        soatPolicy ?? NullSoatRuntValidationPolicy.Instance;

    public async Task<(ProcedureInstanceSummary? Result, string? Error, string? Warning)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        CompletePlateFlowRequest request,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found", null);

        if (instance.Status != TramiteEstado.Entregado)
            return (null, TramiteEstadoErrores.TransicionNoPermitida, null);

        if (instance.PlateFlowStatus != PlateFlowStatus.Asignado)
            return (null, "plate_flow_not_asignado", null);

        if (!PlateFlowStateMachine.IsValidTransition(PlateFlowStatus.Asignado, PlateFlowStatus.Terminado))
            return (null, TramiteEstadoErrores.TransicionNoPermitida, null);

        // Validación del SOAT contra el RUNT al procesar. La consulta se hace SIEMPRE que haya
        // validador (así el estado del SOAT queda registrado y el gestor lo ve). El bloqueo es el
        // default: con la opción APAGADA, un SOAT no vigente detiene el avance; con la opción
        // ACTIVA se deja continuar, pero devolviendo advertencia: enviar al OT un trámite sin SOAT
        // vigente no puede pasar en silencio.
        string? warning = null;
        if (soatValidator is not null)
        {
            var permiteContinuarSinSoatVigente =
                await _soatPolicy.IsEnabledAsync(tenantId, ct).ConfigureAwait(false);
            var (soat, soatError) = await soatValidator.HandleAsync(id, tenantId, ct).ConfigureAwait(false);

            // Un fallo de la consulta (proveedor caído, plantilla ausente) no puede convertirse en
            // un bloqueo silencioso del trámite: solo bloquea un SOAT que el RUNT dice que no está
            // vigente. Si no se pudo consultar, se sigue.
            if (DebeBloquearPorSoatNoVigente(
                    permiteContinuarSinSoatVigente,
                    soatError is null,
                    soat?.Vigente))
            {
                return (null, SoatNoVigente, null);
            }

            if (DebeAdvertirSoatNoVigente(
                    permiteContinuarSinSoatVigente,
                    soatError is null,
                    soat?.Vigente))
            {
                warning = SoatNoVigenteAdvertencia;
            }
        }

        var now = DateTimeOffset.UtcNow;

        // Fase 1 — escribir checks ESTANDO en asignado (el trigger de inmutabilidad solo lo
        // permite en ese sub-estado) y persistir antes de cambiar plate_flow_status.
        UpsertBoolField(instance, tenantId, id, PlateFlowCheckFields.SoatPagado, request.SoatPagado, now);
        UpsertBoolField(instance, tenantId, id, PlateFlowCheckFields.ImpuestoDepartamentalPagado, request.ImpuestoDepartamentalPagado, now);

        if (!await repo.SaveChangesWithConcurrencyGuardAsync(ct))
            return (null, TramiteEstadoErrores.ConflictoConcurrencia, null);

        // Fase 2 — avanzar a terminado (mismo patrón que AssignPlate: preasignado→asignado).
        instance.PlateFlowStatus = PlateFlowStatus.Terminado;
        instance.UpdatedAt = now;
        instance.UpdatedBy = changedBy;

        if (!await repo.SaveChangesWithConcurrencyGuardAsync(ct))
            return (null, TramiteEstadoErrores.ConflictoConcurrencia, null);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null, warning);
    }

    /// <summary>
    /// Gate del SOAT al procesar: la opción activa PERMITE continuar sin SOAT vigente; apagada
    /// (default) bloquea. Solo aplica cuando la consulta al RUNT respondió con un resultado.
    /// </summary>
    public static bool DebeBloquearPorSoatNoVigente(
        bool permiteContinuarSinSoatVigente,
        bool consultaRespondio,
        bool? soatVigente) =>
        !permiteContinuarSinSoatVigente
        && consultaRespondio
        && soatVigente == false;

    /// <summary>
    /// Contracara del gate: cuando la opción activa deja pasar un SOAT no vigente, el trámite avanza
    /// pero la UI tiene que decirlo. Nunca advierte si el RUNT no respondió.
    /// </summary>
    public static bool DebeAdvertirSoatNoVigente(
        bool permiteContinuarSinSoatVigente,
        bool consultaRespondio,
        bool? soatVigente) =>
        permiteContinuarSinSoatVigente
        && consultaRespondio
        && soatVigente == false;

    private void UpsertBoolField(
        ProcedureInstance instance,
        Guid tenantId,
        Guid instanceId,
        string key,
        bool? value,
        DateTimeOffset now)
    {
        if (value is null)
            return;

        var text = value.Value ? "true" : "false";
        var existing = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.ValueText = text;
            existing.Source = "user";
            existing.UpdatedAt = now;
            return;
        }

        var fieldValue = new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            FieldKey = key,
            ValueText = text,
            Source = "user",
            CreatedAt = now,
        };
        instance.FieldValues.Add(fieldValue);
        // PK store-generated (uuidv7) con Id ya seteado: Added explícito para forzar INSERT.
        // Sin esto EF infiere Modified → UPDATE de 0 filas → DbUpdateConcurrencyException.
        repo.Add(fieldValue);
    }
}

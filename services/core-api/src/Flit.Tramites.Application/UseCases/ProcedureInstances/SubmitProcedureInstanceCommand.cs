using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Radicar (N 03, ADR-0022): orquestador delgado sobre <see cref="ITramiteLifecycleService"/>.
/// Desde <c>borrador</c> encadena <c>borrador→preparado</c> (gate RF03: identidad + documentos)
/// y la radicación (gates OT: organismo habilitado + reglas); desde <c>preparado</c> solo la
/// radicación. Cada transición registra su fila de historial y su notificación. Si la preparación
/// pasa pero la radicación falla (p.ej. organismo_no_habilitado), el trámite queda en
/// <c>preparado</c>: corregida la causa, un nuevo submit solo reintenta la radicación.
/// <para>
/// ADR-0059 (HU #12597) — el destino de la radicación lo decide
/// <see cref="TramiteTransitionPolicy.DestinoDeRadicacion"/>: la Ruta Larga (tipo que pide placa y
/// no la tiene) entra por <c>preasignacion</c>; con placa (RUNT o digitada) o en tipos sin placa entra
/// por <c>entregado</c>. Los flags de preasignación de compañía/OT ya NO deciden la ruta.
/// </para>
/// <para>
/// Desde <c>rechazado</c> con subsanación activa (HU #10870) este MISMO handler re-radica directo
/// (sin encadenar preparado): <see cref="ITramiteLifecycleService"/> re-evalúa SOLO los gates de
/// negocio afectados por los campos corregidos desde el snapshot capturado al entrar a
/// subsanación (HU #10872, AC1) — más el gate final de entrega al OT, que siempre corre. La
/// identidad y las consultas externas aún vigentes NO se vuelven a solicitar (AC2).
/// </para>
/// </summary>
public sealed class SubmitProcedureInstanceHandler(
    ITramiteLifecycleService lifecycle,
    IProcedureInstanceRepository repo,
    ILogger<SubmitProcedureInstanceHandler> logger)
{
    private readonly ILogger<SubmitProcedureInstanceHandler> _logger = logger;

    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        // Con tipo y field_values: el destino de la radicación depende de si el tipo pide placa y de
        // si el trámite ya la tiene.
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // RF04 — estados finales (aprobado/anulado) son inmutables.
        if (TramiteEstado.EsFinal(instance.Status))
            return (null, TramiteEstadoErrores.EstadoFinal);

        // ICT (paridad v1 pauseDraftProcess / starts_procedure_in_paused): un trámite PAUSADO no avanza.
        // v1 bloqueaba con ForbiddenError salir de Borrador estando pausado; aquí se corta la radicación
        // (manual o auto-encadenada tras identidad) en el mismo punto. La anulación va por AbortDraft y NO
        // pasa por aquí, así que un trámite pausado sí se puede anular. is_paused default false ⇒ los
        // trámites de plataforma nunca entran en esta rama.
        if (instance.IsPaused)
            return (null, TramiteEstadoErrores.TramitePausado);

        // La resolución de identidad por persona (HU #10350, #87) y los gates OT viven en
        // TramiteLifecycleService — este orquestador solo encadena las transiciones.
        if (instance.Status == TramiteEstado.Borrador)
        {
            var preparado = await lifecycle.TransitionAsync(
                new TramiteTransitionCommand(
                    id, tenantId, TramiteEstado.Preparado,
                    "Radicación: gate de identidad y documentos superado.", changedBy),
                ct).ConfigureAwait(false);
            if (!preparado.Success)
                return (null, preparado.ErrorCode);
        }
        else if (!(instance.Status == TramiteEstado.Preparado
                  || TramiteEstado.EsReRadicacionSubsanacion(instance.Status, instance.SubsanacionActiva)))
        {
            return (null, TramiteEstadoErrores.TransicionNoPermitida);
        }

        var contexto = TransitionContext.ForInstance(instance, TramiteActor.Gestor);
        var destino = TramiteTransitionPolicy.DestinoDeRadicacion(contexto);
        SubmitLog.DestinoRadicacion(_logger, id, tenantId, destino, contexto.RequiresPlateRequest, contexto.HasPlate);

        var motivo = destino == TramiteEstado.Preasignacion
            ? "Radicación: sin placa; pendiente de asignación de placa por el organismo de tránsito."
            : "Radicación: trámite entregado al organismo de tránsito.";

        var final = await lifecycle.TransitionAsync(
            new TramiteTransitionCommand(id, tenantId, destino, motivo, changedBy, TramiteActor.Gestor),
            ct).ConfigureAwait(false);
        if (!final.Success)
            return (null, final.ErrorCode);

        return (CreateProcedureInstanceHandler.ToSummary(final.Instance!), null);
    }
}

/// <summary>Logging source-generated (CA1848) del destino de la radicación (ADR-0059).</summary>
internal static partial class SubmitLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Radicación del trámite {InstanceId} (tenant {TenantId}) → '{Destino}' (pide placa: {PidePlaca}, tiene placa: {TienePlaca}).")]
    public static partial void DestinoRadicacion(
        ILogger logger, Guid instanceId, Guid tenantId, string destino, bool pidePlaca, bool tienePlaca);
}

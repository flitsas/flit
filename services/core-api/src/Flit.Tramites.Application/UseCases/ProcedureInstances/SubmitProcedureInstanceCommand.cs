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
    ILogger<SubmitProcedureInstanceHandler> logger,
    FirmarImprontaManualSiListaHandler? firmaImpronta = null)
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

        // HU #12116 — firma automática de la impronta manual apenas el trámite queda entregado (cubre
        // también la re-radicación por subsanación). Best-effort: un fallo NO cambia la respuesta del
        // submit; el fallo ya queda logueado/trazado dentro del handler. En la Ruta Larga (ADR-0059)
        // el trámite entra en preasignación sin placa: aquí el gate responde «placa pendiente» y la
        // firma la dispara el OT al asignar la placa (AdminPlateRangesEndpoints).
        if (firmaImpronta is not null)
        {
            try
            {
                await firmaImpronta
                    .HandleAsync(id, tenantId, FirmaImprontaAutomaticaOrigen.Radicacion, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SubmitLog.FirmaImprontaOmitida(_logger, ex, id, tenantId);
            }
        }

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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "La firma automática de impronta del trámite {InstanceId} (tenant {TenantId}) se omitió por una excepción no controlada.")]
    public static partial void FirmaImprontaOmitida(ILogger logger, Exception ex, Guid instanceId, Guid tenantId);
}

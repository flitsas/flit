using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.RevocationRequests;

/// <summary>
/// HU #12571 (Feature #12565) — sub-máquina de estados y gates de la solicitud de revocatoria: valida
/// origen, ventana y unicidad ANTES de aceptar una solicitud (AC1-AC5). PURO (sin IO): recibe todo lo
/// que necesita ya resuelto por el llamador (HU #12572, el endpoint) y decide.
///
/// <para>
/// NO valida el <c>Status</c> del trámite más allá de exigir que sea <see cref="TramiteEstado.Aprobado"/>
/// (precondición de todos los AC de esta HU; lo garantiza el endpoint antes de invocar el gate). NO toca
/// <c>TramiteEstado</c>/<c>TramiteStateMachine</c> (ADR-0022): solo LEE el estado, nunca lo cambia.
/// </para>
///
/// <para>
/// AC4 (unicidad) también lo aplica el índice único parcial
/// <c>uq_procedure_revocation_requests_active_per_instance</c> en BD (HU #12570) — la comprobación aquí
/// (<paramref name="hasActiveRequest"/>) es la lectura optimista de UI/negocio; la carrera concurrente la
/// cierra <see cref="ActiveRevocationRequestExistsException"/> al persistir (ver
/// <c>ProcedureRevocationRequestRepository.SaveChangesAsync</c>), no una segunda unicidad en memoria.
/// </para>
/// </summary>
public static class RevocationRequestGate
{
    /// <summary>AC2 — 422: la ventana de revocatoria configurada para el OT ya venció.</summary>
    public const string VentanaVencida = "ventana_vencida";

    /// <summary>AC3 — 422: el trámite no fue creado en FLIT (integración ICT o migrado de V1).</summary>
    public const string OrigenNoSoportado = "origen_no_soportado";

    /// <summary>AC4 — 409: ya hay una solicitud <c>solicitada</c>/<c>en_revision</c> para el trámite.</summary>
    public const string SolicitudActivaExistente = "solicitud_activa_existente";

    /// <summary>
    /// Evalúa si se puede crear una nueva solicitud de revocatoria (AC1-AC5).
    /// </summary>
    /// <param name="tramiteStatus">
    /// Estado actual del trámite (<c>procedure_instances.status</c>). Precondición del gate: debe ser
    /// <see cref="TramiteEstado.Aprobado"/> — ninguno de los AC de esta HU define un código de error para
    /// otro estado; validar que el trámite esté Aprobado es responsabilidad del endpoint (HU #12572) antes
    /// de invocar este gate (p.ej. 404/409 genérico si no lo está). Aquí solo se defiende con una excepción
    /// de programación, no con un código de negocio.
    /// </param>
    /// <param name="tramiteOrigin">
    /// <c>procedure_instances.origin</c> crudo (null = plataforma, <c>"ict"</c> = integración). Se deriva
    /// junto con <paramref name="tramiteIsMigrated"/> vía <see cref="TramiteFuente.Desde"/> — misma fuente
    /// de verdad que ya usa el listado de trámites (HU #11056) para no inventar un criterio nuevo.
    /// </param>
    /// <param name="tramiteIsMigrated"><c>procedure_instances.is_migrated</c> (foto importada de V1).</param>
    /// <param name="approvedAt">
    /// Fecha/hora en que el trámite llegó a <see cref="TramiteEstado.Aprobado"/> por primera vez (la
    /// aprobación ORIGINAL, no la fecha de un reintento). Es la base fija desde la que corre la ventana
    /// en AC1 y AC5: un reintento tras un rechazo NO reinicia el conteo.
    /// </param>
    /// <param name="revocationWindowBusinessDays">
    /// <c>admin.transit_office_profiles.revocation_window_business_days</c> del OT del trámite.
    /// <c>null</c> = sin configurar = sin límite (AC1/AC5 la dan siempre por vigente).
    /// </param>
    /// <param name="hasActiveRequest">
    /// <c>true</c> si ya existe una fila <c>solicitada</c>/<c>en_revision</c> para este trámite
    /// (<see cref="ProcedureRevocationRequestStatus.EsActivo"/>). Una fila <c>rechazada</c> o
    /// <c>aprobada</c> NO cuenta (AC5: un rechazo previo no bloquea el reintento).
    /// </param>
    /// <param name="now">Momento de la solicitud (inyectado para que el gate sea determinístico en tests).</param>
    /// <param name="businessDayCalculator">Calculador de días hábiles (ver <see cref="IBusinessDayCalculator"/>).</param>
    public static GateResult Evaluate(
        string tramiteStatus,
        string? tramiteOrigin,
        bool tramiteIsMigrated,
        DateTimeOffset approvedAt,
        int? revocationWindowBusinessDays,
        bool hasActiveRequest,
        DateTimeOffset now,
        IBusinessDayCalculator businessDayCalculator)
    {
        ArgumentNullException.ThrowIfNull(businessDayCalculator);

        if (!string.Equals(tramiteStatus, TramiteEstado.Aprobado, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"RevocationRequestGate solo aplica a trámites en estado '{TramiteEstado.Aprobado}'; "
                + $"llegó '{tramiteStatus}'. El endpoint (HU #12572) debe validar el estado del trámite "
                + "antes de invocar este gate.");
        }

        // AC3 — el trámite debe haber sido creado en FLIT (fuente "dashboard"): ni integración ICT
        // (origin='ict') ni foto migrada de V1 (is_migrated). Misma derivación que TramiteFuente (HU #11056).
        var fuente = TramiteFuente.Desde(tramiteOrigin, tramiteIsMigrated);
        if (!string.Equals(fuente, TramiteFuente.Dashboard, StringComparison.Ordinal))
        {
            return GateResult.Block(
                OrigenNoSoportado,
                "El trámite no fue creado en FLIT; no admite solicitud de revocatoria.");
        }

        // AC4 — a lo sumo una solicitud activa por trámite.
        if (hasActiveRequest)
        {
            return GateResult.Block(
                SolicitudActivaExistente,
                "Ya existe una solicitud de revocatoria activa para este trámite.");
        }

        // AC2/AC5 — ventana vigente desde la aprobación ORIGINAL. NULL = sin límite.
        if (revocationWindowBusinessDays is { } dias)
        {
            var vence = businessDayCalculator.AddBusinessDays(approvedAt, dias);
            if (now > vence)
            {
                return GateResult.Block(
                    VentanaVencida,
                    "La ventana de revocatoria configurada para el organismo de tránsito ya venció.");
            }
        }

        // AC1/AC5 — todos los gates pasaron: puede crearse la fila (nuevo attempt_number, sin tocar
        // el status del trámite ni subsanacion_activa/SttWorkflow).
        return GateResult.Allowed;
    }
}

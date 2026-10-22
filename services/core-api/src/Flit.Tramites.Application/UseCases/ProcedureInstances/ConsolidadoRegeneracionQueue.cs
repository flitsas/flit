using System.Diagnostics.CodeAnalysis;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12795 (Épica #12760, D2) — cuál de los dos PDF consolidados se pide regenerar por anticipado.
/// Los valores son contrato estable: los consumen los hitos (HU #12796) y los demás carriles de la épica.
/// </summary>
public enum TipoConsolidado
{
    /// <summary>Consolidado del wizard (adjunto <c>consolidado</c>, bandera <c>consolidado_wizard_vigente</c>).</summary>
    Wizard = 0,

    /// <summary>Consolidado maestro del OT (adjunto <c>consolidado_maestro</c>, bandera <c>consolidado_maestro_vigente</c>).</summary>
    Maestro = 1,
}

/// <summary>
/// HU #12795 — cola de regeneración ANTICIPADA de consolidados (Épica #12760, decisión D1: perezoso
/// garantizado + anticipado en hitos, con debounce).
///
/// <para><b>Contrato.</b> <see cref="Encolar"/> no bloquea ni espera al PDF (AC1): deja la solicitud en
/// una cola en memoria y vuelve. Un worker la ejecuta tras una ventana de debounce; varias solicitudes
/// de la misma clave (tenant, trámite, documento) dentro de la ventana se funden en UNA regeneración
/// (AC2). El worker respeta la bandera de vigencia, los estados finales, el consolidado cargado a mano
/// (<c>Source="user"</c>) y los migrados V1 (AC3/AC4), y ejecuta cada trabajo en un scope de DI propio
/// con el tenant del trámite (AC5).</para>
///
/// <para><b>Best-effort.</b> La regeneración anticipada es una optimización sobre el camino perezoso: si
/// la cola está llena, el proceso se reinicia o el trabajo falla, el consolidado se reconstruye igual la
/// próxima vez que alguien lo pida con la bandera abajo. Por eso el llamador NO debe tratar un
/// <c>false</c> como error de su propia operación.</para>
///
/// <para><b>Tenant.</b> <paramref name="tenantId"/> es SIEMPRE el tenant dueño del trámite (el cliente),
/// no el del OT que dispara el hito.</para>
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Es literalmente una cola en memoria (mismo criterio que IUsageEventQueue); el nombre es contrato de la épica #12760.")]
public interface IConsolidadoRegeneracionQueue
{
    /// <summary>Solicita la regeneración anticipada. No espera a la generación.</summary>
    /// <param name="tenantId">Tenant dueño del trámite (cliente).</param>
    /// <param name="procedureInstanceId">Trámite.</param>
    /// <param name="documento">Consolidado a regenerar.</param>
    /// <returns>
    /// <c>true</c> si la solicitud quedó en cola; <c>false</c> si se descartó (ids vacíos, documento
    /// desconocido o cola llena). Nunca lanza por esas causas.
    /// </returns>
    bool Encolar(Guid tenantId, Guid procedureInstanceId, TipoConsolidado documento);
}

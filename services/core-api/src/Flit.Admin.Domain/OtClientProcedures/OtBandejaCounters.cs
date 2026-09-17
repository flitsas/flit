namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>
/// Contadores de la cabecera de la bandeja del OT: cuánto trabajo hay de cada clase en el organismo,
/// sobre TODO lo que la bandeja puede mostrar (grant vigente incluido) y no sobre la página cargada.
///
/// <para>
/// Se calculan aparte del listado, y no derivándolos de las filas traídas, porque el listado viene
/// paginado: contar la página respondería "cuántos de estos 20", que no es la pregunta. Y se
/// calculan sobre el conjunto SIN filtros de búsqueda: las tarjetas son el punto de entrada al
/// trabajo del organismo —se pulsan para filtrar—, así que tienen que seguir diciendo a dónde se
/// puede ir aunque ya se haya acotado la vista.
/// </para>
///
/// <para>
/// ADR-0059 — desde que la ruta de placa vive en <c>status</c>, cada clase es UN estado del ciclo de
/// vida: las tarjetas son excluyentes entre sí y pulsar una equivale a filtrar por ese estado.
/// </para>
/// </summary>
/// <param name="Preasignacion">Radicados sin placa: la cola concreta de "asignar placa" del organismo.</param>
/// <param name="Asignados">Con placa puesta por el organismo; la pelota está en el gestor (SOAT, impuestos, enviar al OT).</param>
/// <param name="PorDecidir">Entregados a la espera de la decisión del organismo (aprobar / rechazar).</param>
/// <param name="Aprobados">Trámites que el organismo aprobó.</param>
/// <param name="Rechazados">Trámites que el organismo rechazó (desde entregado o desde preasignación).</param>
/// <param name="Revocados">HU #12166 (Feature #12156) — Aprobados que el organismo revocó.</param>
/// <param name="SolicitudesRevocatoria">
/// Feature #12565 — Aprobados con una solicitud de revocatoria ACTIVA
/// (<c>solicitada</c>/<c>en_revision</c>): necesitan una decisión del organismo, a diferencia de
/// <see cref="Revocados"/> (ya decidido, estado final) o una solicitud <c>rechazada</c> (ya
/// decidida, el turno es del gestor).
///
/// <para>
/// ÚNICA EXCEPCIÓN a la regla de arriba: no es un estado del ciclo de vida ni es excluyente con
/// las demás — todo trámite con solicitud activa YA cuenta en <see cref="Aprobados"/>, porque el
/// sub-flujo de revocatoria es ORTOGONAL al estado (ADR-0022: el trámite sigue <c>aprobado</c>
/// mientras se decide). Existe aparte para que el organismo no tenga que abrir "Aprobados" fila
/// por fila para notar cuál tiene una revocatoria esperando, y al pulsarla filtra por el
/// sub-flujo, no por <c>status</c>.
/// </para>
/// </param>
public sealed record OtBandejaCounters(
    int Preasignacion,
    int Asignados,
    int PorDecidir,
    int Aprobados,
    int Rechazados,
    int Revocados,
    int SolicitudesRevocatoria);

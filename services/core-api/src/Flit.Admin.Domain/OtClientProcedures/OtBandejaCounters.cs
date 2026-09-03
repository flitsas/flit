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
/// Las clases NO son excluyentes entre sí ni suman el total: <see cref="SinAsignarPlaca"/> y
/// <see cref="ConPlacaAsignada"/> miran el sub-estado de placa, mientras que
/// <see cref="Aprobados"/>, <see cref="Rechazados"/> y <see cref="SinGestion"/> miran el estado del
/// ciclo de vida. Un trámite entregado con placa asignada cuenta en dos.
/// </para>
/// </summary>
/// <param name="SinAsignarPlaca">
/// Entregados en ruta de placa que todavía no la tienen (sub-estado <c>preasignado</c>): es la cola
/// concreta de "asignar placa" del organismo.
/// </param>
/// <param name="ConPlacaAsignada">
/// Entregados con la placa ya puesta (sub-estado <c>asignado</c> o <c>terminado</c>).
/// </param>
/// <param name="Aprobados">Trámites que el organismo aprobó.</param>
/// <param name="Rechazados">Trámites que el organismo rechazó.</param>
/// <param name="SinGestion">
/// Entregados que el organismo no ha tocado: sin decisión y sin haber entrado en la ruta de placa.
/// Es el trabajo que nadie ha empezado, y por eso la tarjeta que más urge mirar.
/// </param>
public sealed record OtBandejaCounters(
    int SinAsignarPlaca,
    int ConPlacaAsignada,
    int Aprobados,
    int Rechazados,
    int SinGestion);

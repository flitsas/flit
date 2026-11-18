namespace Flit.Diagnostico.Domain.Saneamiento;

/// <summary>Acción pedida sobre un saneamiento (plan §3.2).</summary>
public enum AccionSaneamiento
{
    Solicitar,
    Desmarcar,
    Tomar,
    CerrarSaneado,
    CerrarNoProcede,
    Reabrir,
    AnularFlit,
}

/// <summary>Quién actúa: la compañía cliente (permiso <c>saneamiento.solicitar</c>) o la operación FLIT (<c>saneamiento.gestionar</c>).</summary>
public enum ActorSaneamiento
{
    Compania,
    Flit,
}

/// <summary>Una fila de la tabla de §3.2: desde qué estado, con qué acción y actor, a qué estado, con qué evento y si exige motivo.</summary>
public sealed record TransicionSaneamiento(
    EstadoSaneamiento? Desde, AccionSaneamiento Accion, ActorSaneamiento Actor, EstadoSaneamiento Hasta, TipoEventoSaneamiento Evento, bool ExigeMotivo);

/// <summary>
/// Máquina de estados del saneamiento (plan §3.2, S7, S10). Pura. La compañía marca y desmarca (solo en SOLICITADO, sin motivo);
/// FLIT toma, cierra, reabre y anula (anular, cerrar como NO_PROCEDE y reabrir exigen motivo). Toda combinación (estado, acción,
/// actor) fuera de <see cref="Tabla"/> es inválida: el llamador responde <c>409 TRANSICION_INVALIDA</c>. ANULADO es final.
/// </summary>
/// <example>
/// <code>
/// TransicionesSaneamiento.Resolver(EstadoSaneamiento.Solicitado, AccionSaneamiento.Tomar, ActorSaneamiento.Flit)!.Hasta; // EnGestion
/// TransicionesSaneamiento.Resolver(EstadoSaneamiento.EnGestion, AccionSaneamiento.Desmarcar, ActorSaneamiento.Compania);  // null
/// </code>
/// </example>
public static class TransicionesSaneamiento
{
    /// <summary>La tabla de §3.2, fila por fila.</summary>
    public static IReadOnlyList<TransicionSaneamiento> Tabla { get; } =
    [
        new(null, AccionSaneamiento.Solicitar, ActorSaneamiento.Compania, EstadoSaneamiento.Solicitado, TipoEventoSaneamiento.Solicitado, false),
        new(EstadoSaneamiento.Solicitado, AccionSaneamiento.Desmarcar, ActorSaneamiento.Compania, EstadoSaneamiento.Anulado, TipoEventoSaneamiento.Desmarcado, false),
        new(EstadoSaneamiento.Solicitado, AccionSaneamiento.Tomar, ActorSaneamiento.Flit, EstadoSaneamiento.EnGestion, TipoEventoSaneamiento.Tomado, false),
        new(EstadoSaneamiento.Solicitado, AccionSaneamiento.AnularFlit, ActorSaneamiento.Flit, EstadoSaneamiento.Anulado, TipoEventoSaneamiento.AnuladoFlit, true),
        new(EstadoSaneamiento.EnGestion, AccionSaneamiento.AnularFlit, ActorSaneamiento.Flit, EstadoSaneamiento.Anulado, TipoEventoSaneamiento.AnuladoFlit, true),
        new(EstadoSaneamiento.EnGestion, AccionSaneamiento.CerrarSaneado, ActorSaneamiento.Flit, EstadoSaneamiento.Saneado, TipoEventoSaneamiento.CerradoSaneado, false),
        new(EstadoSaneamiento.EnGestion, AccionSaneamiento.CerrarNoProcede, ActorSaneamiento.Flit, EstadoSaneamiento.NoProcede, TipoEventoSaneamiento.CerradoNoProcede, true),
        new(EstadoSaneamiento.Saneado, AccionSaneamiento.Reabrir, ActorSaneamiento.Flit, EstadoSaneamiento.EnGestion, TipoEventoSaneamiento.Reabierto, true),
        new(EstadoSaneamiento.NoProcede, AccionSaneamiento.Reabrir, ActorSaneamiento.Flit, EstadoSaneamiento.EnGestion, TipoEventoSaneamiento.Reabierto, true),
    ];

    /// <summary>La fila de la tabla para (estado, acción, actor); null si la transición no existe. <paramref name="desde"/> null: sin saneamiento.</summary>
    public static TransicionSaneamiento? Resolver(EstadoSaneamiento? desde, AccionSaneamiento accion, ActorSaneamiento actor) =>
        Tabla.FirstOrDefault(t => t.Desde == desde && t.Accion == accion && t.Actor == actor);

    /// <summary>ANULADO es el único estado final: para volver a sanear nace otro saneamiento.</summary>
    public static bool EsFinal(EstadoSaneamiento estado) => estado == EstadoSaneamiento.Anulado;
}

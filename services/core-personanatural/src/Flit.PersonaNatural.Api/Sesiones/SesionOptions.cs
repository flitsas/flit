namespace Flit.PersonaNatural.Api.Sesiones;

/// <summary>
/// Reglas de la sesión efímera por trámite (HU #14035, ADR-0075). El portal no tiene cuentas: quien inicia o reanuda un
/// trámite recibe un token corto que solo abre ese trámite. Todo es configurable por ambiente (sección <c>Sesion</c>).
/// </summary>
public sealed class SesionOptions
{
    public const string SectionName = "Sesion";

    /// <summary>Vigencia de la sesión desde su último uso (renovación deslizante).</summary>
    public int VigenciaMinutos { get; init; } = 60;

    /// <summary>Tope absoluto desde que se emitió el token, aunque se siga usando.</summary>
    public int TopeAbsolutoHoras { get; init; } = 24;

    /// <summary>Intentos fallidos de reanudar sobre un mismo radicado antes de bloquearlo.</summary>
    /// <summary>Intentos fallidos de reanudar sobre un mismo radicado y desde una misma IP antes de bloquear a esa IP sobre ese radicado.</summary>
    public int MaxIntentosFallidos { get; init; } = 5;

    /// <summary>Intentos fallidos sobre un radicado, desde cualquier IP, antes de bloquearlo para todos (patrón de ataque distribuido).</summary>
    public int MaxIntentosPorRadicado { get; init; } = 20;

    /// <summary>Ventana, en minutos, en la que se cuentan esos intentos y dura el bloqueo.</summary>
    public int VentanaIntentosMinutos { get; init; } = 15;
}

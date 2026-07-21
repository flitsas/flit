namespace Flit.Modules.Quipux.Domain.Envios;

/// <summary>
/// Estados del ciclo de radicación de un trámite en Quipux. Es un ciclo propio de la integración,
/// distinto del estado de negocio del trámite (<c>Flit.Tramites.Domain.Tramites.Estados.TramiteEstado</c>):
/// una submission avanza por su cuenta y, en los hitos, empuja la transición del trámite.
/// </summary>
public static class QuipuxSubmissionEstado
{
    /// <summary>Encolada; aún no radicada en Quipux. Entrada de <c>QuipuxRegisterProcessor</c>.</summary>
    public const string Pendiente = "pendiente";

    /// <summary>Quipux respondió <c>codigo = 81</c>. Entrada de <c>QuipuxStatusPollProcessor</c>.</summary>
    public const string Registrado = "registrado";

    /// <summary>Quipux resolvió el trámite como aprobado (<c>estadoTramite.codigo = 2</c>).</summary>
    public const string Aprobado = "aprobado";

    /// <summary>Quipux resolvió el trámite como rechazado (<c>estadoTramite.codigo = 3</c>).</summary>
    public const string Rechazado = "rechazado";

    /// <summary>Agotó <c>max_attempts</c> sin radicar. Dead-letter observable; no se vuelve a reclamar.</summary>
    public const string Fallido = "fallido";

    public static readonly IReadOnlyList<string> Todos =
        [Pendiente, Registrado, Aprobado, Rechazado, Fallido];

    /// <summary>
    /// Estados en los que la submission sigue "viva". El índice único parcial
    /// <c>uq_quipux_submissions_activa</c> se define sobre exactamente estos dos valores para
    /// garantizar en BD que un trámite no tenga dos radicaciones en curso.
    /// </summary>
    public static readonly IReadOnlyList<string> Activos = [Pendiente, Registrado];

    /// <summary>Sin transiciones posteriores.</summary>
    public static bool EsFinal(string? estado) =>
        estado is Aprobado or Rechazado or Fallido;

    public static bool EsValido(string? estado) =>
        estado is not null && Todos.Contains(estado, StringComparer.Ordinal);
}

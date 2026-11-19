namespace Flit.Admin.Domain.Plataforma.PersonaNatural;

/// <summary>
/// Ventana de anulación del cobro en caja por organismo (Feature #14453, ADR-0082, revisión 2026-11-17). Rango 0..<see cref="MaxMinutos"/>:
/// 0 = el trámite se radica al cobrar y el cobro no se puede anular. El tope sale de <c>PortalCaja:VentanaAnulacionMaxMinutos</c> (60) y el
/// valor sin configurar de <c>PortalCaja:VentanaAnulacionPorDefectoMinutos</c> (15, el que tenía el portal antes de que fuera configurable).
/// </summary>
public sealed record CajaAnulacionOpciones(int PorDefectoMinutos = CajaAnulacionOpciones.PorDefecto, int MaxMinutos = CajaAnulacionOpciones.Maximo)
{
    public const int PorDefecto = 15;
    public const int Maximo = 60;

    /// <summary>Tope duro de la base (CHECK de la columna): la configuración nunca puede superarlo.</summary>
    public const int TopeDeLaBase = 1440;

    public bool Valida(int? minutos) => minutos is >= 0 && minutos <= MaxMinutos;
}

/// <summary>Lo que el administrador ve y core-api manda a confirmar: los minutos efectivos, si es el valor por defecto y el tope vigente.</summary>
public sealed record VentanaAnulacionCajaView(int Minutos, bool PorDefecto, int MaxMinutos);

/// <summary>Un cambio de la ventana: quién, el valor anterior (efectivo, o el default si no había) y el nuevo, y cuándo.</summary>
public sealed record CambioVentanaAnulacionView(int MinutosAntes, int MinutosDespues, string? ActorNombre, DateTimeOffset CambiadoEn);

/// <summary>
/// Persistencia de la ventana por organismo (<c>admin.persona_natural_caja_settings</c>) y de su historial
/// (<c>admin.persona_natural_caja_window_history</c>, DDL 200). Tablas globales sin tenant_id, como las de cobro.
/// </summary>
public interface IPersonaNaturalCajaAnulacionRepository
{
    /// <summary>Minutos guardados para el organismo o <c>null</c> si nunca los configuró.</summary>
    Task<int?> LeerAsync(Guid organismoId, CancellationToken ct = default);

    /// <summary>
    /// Guarda la ventana y, en la misma transacción, agrega al historial el cambio si el valor efectivo cambió. Devuelve el valor efectivo
    /// anterior (el guardado o <paramref name="minutosPorDefecto"/> si no había).
    /// </summary>
    Task<int> GuardarAsync(Guid organismoId, int minutos, int minutosPorDefecto, Guid actor, CancellationToken ct = default);

    /// <summary>Cambios del organismo, el más reciente primero, hasta <paramref name="max"/>.</summary>
    Task<IReadOnlyList<CambioVentanaAnulacionView>> HistorialAsync(Guid organismoId, int max, CancellationToken ct = default);
}

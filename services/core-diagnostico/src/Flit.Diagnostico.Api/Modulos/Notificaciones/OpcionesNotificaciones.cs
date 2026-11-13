namespace Flit.Diagnostico.Api.Modulos.Notificaciones;

/// <summary>
/// Retención de la campana (HU #14563 B8, ADR-0086 D-3). Opcionales con valores por defecto: 90 días y una purga diaria.
/// Variables: <c>Diagnostico__Notificaciones__RetencionDias</c>, <c>Diagnostico__Notificaciones__IntervaloPurga</c>.
/// </summary>
public sealed class OpcionesNotificaciones
{
    public const string Seccion = "Diagnostico:Notificaciones";

    public int RetencionDias { get; set; } = 90;

    public TimeSpan IntervaloPurga { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Filas por bloque de borrado.</summary>
    public int TamanoLote { get; set; } = 500;
}

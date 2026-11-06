namespace Flit.Tramites.Domain.Tramites.PersonaNatural;

/// <summary>
/// HU #14397 (Feature #14396, Épica #14297) — motivo con el que se anula el borrador de una solicitud del portal que FLIT 2
/// rechazó al crearla. Lo escribe la radicación del portal y lo leen el estado de vuelta y el reporte del portal: un trámite
/// anulado así nunca cayó a FLIT 2.0, así que el reporte no lo cuenta.
/// </summary>
public static class MotivosAnulacionPortal
{
    /// <summary>Prefijo del motivo; el código del rechazo va después, entre corchetes.</summary>
    public const string RechazoEnCreacion = "Persona Natural: solicitud rechazada en la creación";
}

namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// Variante de cuerpo del correo <c>tramites.asignacion-placa</c> (HU #11486, ADR-0046).
/// En productivo prevalece sobre el canal del tenant.
/// </summary>
public enum PlateAssignmentEmailBrand
{
    Flit = 0,
    Renting = 1,
}

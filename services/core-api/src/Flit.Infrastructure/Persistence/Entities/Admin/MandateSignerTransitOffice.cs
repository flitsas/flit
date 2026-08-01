namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Organismo de tránsito donde aplica un mandatario (HU #11201, Feature #11190).
///
/// <para>Antes el organismo vivía en el propio mandatario, así que la misma persona firmando en tres
/// organismos eran tres registros distintos —con tres firmas del baúl y tres validaciones de identidad
/// que renovar por separado—. Con este puente la persona existe una sola vez y sus organismos son
/// filas.</para>
///
/// <para><c>IsActive</c> sigue el mismo criterio que <see cref="MandateSignerCompany"/>: retirar un
/// organismo es baja lógica (se conserva el histórico y se libera la unicidad), y al inactivar el
/// mandatario sus organismos quedan inactivos con él.</para>
/// </summary>
public sealed class MandateSignerTransitOffice
{
    public Guid Id { get; set; }
    public Guid MandateSignerId { get; set; }
    public Guid TransitOfficeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

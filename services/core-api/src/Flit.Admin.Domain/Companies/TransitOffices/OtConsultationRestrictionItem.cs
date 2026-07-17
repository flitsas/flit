namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Fila de restricción de consulta de un tenant para un Organismo de Tránsito puntual
/// (HU #10759, AC1/AC5). La tabla es dispersa: solo existen filas para los pares que el
/// admin tocó explícitamente; ausencia de fila para un <see cref="ConsultationKind"/> dado
/// equivale a <see cref="Enabled"/> = <c>true</c> (permisivo).
/// </summary>
public sealed record OtConsultationRestrictionItem(
    Guid TransitOfficeId,
    string ConsultationKind,
    bool Enabled);

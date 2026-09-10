namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// Proyección del trámite para la plantilla <c>tramites.asignacion-placa</c> (HU #11486).
/// Espejo de campos del composer de infraestructura, sin acoplar Application a HTML.
/// </summary>
public sealed record PlateAssignmentEmailModelData(
    string ClienteNombre,
    string Placa,
    string EstadoActual,
    string Ciudad = "",
    string SecretariaTransito = "");

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarLog;

/// <summary>
/// Consulta del LOG QX (HU #10793). Los ejes de búsqueda son opcionales y llegan como texto libre; el
/// handler los normaliza (trim, page/pageSize) y parsea (<see cref="InstanceId"/> a Guid) antes de
/// delegar en el repositorio de lectura. Sin ningún eje, se listan todas las radicaciones paginadas.
/// </summary>
/// <remarks>
/// <see cref="InstanceId"/> es <c>string</c>, no <c>Guid?</c>, a propósito: el borde HTTP no debe
/// reventar con 400 cuando el usuario teclea un valor no-UUID (p. ej. un número con el eje "Trámite"
/// seleccionado). Un id malformado se resuelve como página vacía en el handler, mismo criterio que un
/// <see cref="Radicado"/> no numérico.
/// </remarks>
public sealed record ConsultarLogQuipuxQuery(
    string? Placa,
    string? InstanceId,
    string? Radicado,
    int? Page,
    int? PageSize);

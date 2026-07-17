namespace Flit.Modules.Quipux.Application.UseCases.ConsultarLog;

/// <summary>
/// Consulta del LOG QX (HU #10793). Los ejes de búsqueda son opcionales; el handler los normaliza
/// (trim, page/pageSize) antes de delegar en el repositorio de lectura. Sin ningún eje, se listan
/// todas las radicaciones paginadas.
/// </summary>
public sealed record ConsultarLogQuipuxQuery(
    string? Placa,
    Guid? ProcedureInstanceId,
    string? Radicado,
    int? Page,
    int? PageSize);

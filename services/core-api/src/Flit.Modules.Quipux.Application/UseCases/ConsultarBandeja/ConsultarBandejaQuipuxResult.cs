namespace Flit.Modules.Quipux.Application.UseCases.ConsultarBandeja;

/// <summary>
/// Página de la bandeja del LOG QX con eco de paginación y contadores (contrato
/// <c>LogQxBandejaPage</c> en OpenAPI).
/// </summary>
public sealed record ConsultarBandejaQuipuxResult(
    IReadOnlyList<QuipuxBandejaEntryView> Data,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<QuipuxBandejaContadorView> Contadores);

/// <summary>
/// Una fila de la bandeja: UN TRÁMITE (ADR-0051, D1). Los campos de radicación corresponden a la más
/// reciente y <c>Intentos</c> dice cuántas hubo.
/// </summary>
/// <param name="DocumentoQx">
/// Nombre del documento en Quipux — la llave de correlación con la secretaría (ADR-0051, D4). Null
/// mientras el trámite no tenga radicación. Quipux no emite radicado alguno.
/// </param>
/// <param name="EsperandoDesde">Desde cuándo espera; null en los estados terminales.</param>
/// <param name="HorasEsperando">
/// Horas de espera acumuladas, ya calculadas en servidor para que la interfaz no dependa del reloj
/// del navegador ni de su zona horaria. Null en los estados terminales.
/// </param>
public sealed record QuipuxBandejaEntryView(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Plate,
    string ProcedureTypeName,
    string Estado,
    Guid ClientTenantId,
    string ClientTenantName,
    string TransitOfficeName,
    string? DivipoCode,
    string? DocumentoQx,
    Guid? SubmissionId,
    int Intentos,
    int Attempts,
    int PollCount,
    int? QxRegisterCode,
    int? QxProcedureCode,
    string? RejectionReason,
    DateTimeOffset? UltimaActividad,
    DateTimeOffset? EsperandoDesde,
    double? HorasEsperando,
    DateTimeOffset? SubmissionCreatedAt);

/// <summary>Contador de un estado sobre el conjunto filtrado completo, no sobre la página.</summary>
public sealed record QuipuxBandejaContadorView(string Estado, int Total);

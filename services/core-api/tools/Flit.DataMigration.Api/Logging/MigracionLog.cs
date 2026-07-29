namespace Flit.DataMigration.Api.Logging;

/// <summary>
/// Log del host de migración, con delegados generados por <c>[LoggerMessage]</c> (CA1848, que este
/// repositorio trata como error).
/// <para>
/// Sin PII: se registran el id de V1, el lote y el resultado. Nunca cédulas, nombres ni tokens.
/// </para>
/// </summary>
internal static partial class MigracionLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Migración del trámite {V1Id} terminada (lote {BatchId}); con problemas: {ConProblemas}")]
    internal static partial void Terminado(ILogger logger, long v1Id, string batchId, bool conProblemas);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Migración del trámite {V1Id} abortada por configuración: {Code} — {Detalle}")]
    internal static partial void ConfiguracionInvalida(ILogger logger, long v1Id, string code, string detalle);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Trámite {V1Id}: la data plana quedó en {Estado} ({Motivo}); no se corren adjuntos ni documentos")]
    internal static partial void DetenidoTrasDatos(ILogger logger, long v1Id, string estado, string? motivo);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Migration:CreateTenantIfMissing venía en true y se fuerza a false: crear tenants por HTTP no está permitido")]
    internal static partial void CreacionDeTenantsDesactivada(ILogger logger);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "API de migración ACTIVA en este ambiente. Apágala al terminar la ola (MigracionApi:Enabled)")]
    internal static partial void ApiActiva(ILogger logger);
}

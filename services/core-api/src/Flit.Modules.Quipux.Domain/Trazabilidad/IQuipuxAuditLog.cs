namespace Flit.Modules.Quipux.Domain.Trazabilidad;

/// <summary>
/// Bitácora de radicación con scope propio: escribe en su propia unidad de trabajo y hace commit
/// inmediato, de modo que el evento queda registrado aunque el flujo llamador haga rollback o
/// falle. Nunca rompe el negocio — traga sus excepciones y las degrada a un log Warning. Es el
/// patrón de <c>IdentityValidationAuditLog</c>.
/// </summary>
/// <remarks>
/// Úsalo para el rastro que debe sobrevivir al fallo. Cuando el evento deba ser atómico con el
/// cambio de estado, pásalo a <c>IQuipuxSubmissionRepository.UpdateAsync</c>.
/// </remarks>
public interface IQuipuxAuditLog
{
    Task WriteAsync(
        Guid tenantId,
        Guid submissionId,
        string stage,
        string outcome,
        object? detail = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Bitácora de ejecuciones del worker. También con scope propio.</summary>
public interface IQuipuxJobRunLog
{
    /// <summary>Abre una ejecución y devuelve su id.</summary>
    Task<Guid> StartAsync(string jobName, CancellationToken cancellationToken = default);

    /// <summary>Cierra la ejecución con sus contadores. <paramref name="errorMessage"/> solo para el fallo que aborta el ciclo entero.</summary>
    Task FinishAsync(
        Guid runId,
        int claimedCount,
        int okCount,
        int errorCount,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);
}

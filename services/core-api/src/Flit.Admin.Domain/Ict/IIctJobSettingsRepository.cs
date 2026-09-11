namespace Flit.Admin.Domain.Ict;

/// <summary>
/// Persistencia de <c>ict.job_settings</c>. Tabla de core-ict (DDL embebido), sin RLS:
/// el aislamiento es SuperAdmin en el endpoint, no tenant.
/// </summary>
public interface IIctJobSettingsRepository
{
    Task<IctJobSettings> GetAsync(CancellationToken cancellationToken = default);

    Task<IctJobSettings> SaveAsync(IctJobSettings settings, CancellationToken cancellationToken = default);
}

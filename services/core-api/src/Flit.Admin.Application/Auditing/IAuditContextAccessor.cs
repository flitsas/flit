namespace Flit.Admin.Application.Auditing;

/// <summary>
/// Expone lo transversal de la petición HTTP que la auditoría de configuración necesita
/// (RNF01, ADR-0024), sin acoplar Application/Domain a <c>HttpContext</c>. La implementación
/// real vive en Infrastructure sobre <c>IHttpContextAccessor</c>; los tests usan
/// <see cref="NullAuditContextAccessor"/>.
/// </summary>
public interface IAuditContextAccessor
{
    /// <summary>IP de origen ya normalizada (respeta <c>X-Forwarded-For</c>), o <c>null</c>.</summary>
    string? ClientIp { get; }

    /// <summary>Usuario autenticado, si está disponible; el <c>ChangedBy</c> hoy llega por parámetro.</summary>
    Guid? UserId { get; }
}

/// <summary>
/// Implementación inerte para procesos sin HTTP y tests: no aporta IP ni usuario.
/// </summary>
public sealed class NullAuditContextAccessor : IAuditContextAccessor
{
    public static readonly NullAuditContextAccessor Instance = new();

    public string? ClientIp => null;

    public Guid? UserId => null;
}

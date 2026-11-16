namespace Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;

/// <summary>
/// Dominio propio del portal Persona Natural de un organismo (HU #14381, <c>admin.transit_office_domains</c>). Un organismo tiene
/// varios (hasta <c>Domains:PortalMaxPorOrganismo</c>), cada uno con su estado, su verificación y su certificado. Mismo ciclo que el
/// dominio de marca blanca (<c>pending → verified → active</c>, cualquiera → <c>failed</c>), pero sin red, sin login y sin activación
/// manual: con el TXT correcto y el certificado emitido pasa a <c>active</c>. <see cref="IsPrimary"/> marca el que usan el enlace, el
/// QR y los correos.
/// </summary>
public sealed record DominioDePortal(
    Guid Id,
    Guid TransitOfficeId,
    string Host,
    string Status,
    string? StatusReason,
    string VerificationToken,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CertificateIssuedAt,
    DateTimeOffset? CertificateExpiresAt,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? NextCheckAt,
    int CheckAttempts,
    DateTimeOffset? GraceUntil,
    DateTimeOffset StatusChangedAt,
    long RowVersion,
    bool IsPrimary);

/// <summary>Por qué no se pudo registrar un dominio. Lo decide la base de datos (fuente de verdad ante concurrencia).</summary>
public enum PortalDominioRegistroFallo
{
    Ninguno,

    /// <summary>El host ya está vigente en otro organismo o en una red de marca blanca.</summary>
    HostEnUso,

    /// <summary>El organismo ya tiene el máximo de dominios vigentes.</summary>
    LimiteAlcanzado,
}

public sealed record PortalDominioRegistro(DominioDePortal? Dominio, PortalDominioRegistroFallo Fallo)
{
    public static PortalDominioRegistro Ok(DominioDePortal dominio) => new(dominio, PortalDominioRegistroFallo.Ninguno);

    public static PortalDominioRegistro Falla(PortalDominioRegistroFallo fallo) => new(null, fallo);
}

/// <summary>Organismo que abre un dominio activo del portal: su id y el código con el que se arma <c>/o/{codigo}</c>.</summary>
public sealed record PortalDominioOrganismo(Guid TransitOfficeId, string Codigo);

/// <summary>Dominio vencido para comprobar, reclamado por el trabajo programado.</summary>
public sealed record PortalDominioReclamo(Guid Id, Guid TransitOfficeId, string Host);

/// <summary>Dominio del portal que el borde debe publicar (Ingress y certificado).</summary>
public sealed record PortalDominioAprovisionamiento(
    string Host, string Status, DateTimeOffset? CertificateIssuedAt, DateTimeOffset? CertificateExpiresAt);

public interface IPortalDominioRepository
{
    /// <summary>Dominios vigentes del organismo (principal primero, luego por antigüedad). Vacía si no tiene.</summary>
    Task<IReadOnlyList<DominioDePortal>> ListarAsync(Guid transitOfficeId, CancellationToken ct = default);

    /// <summary>Dominio vigente por id, de cualquier organismo (el llamador valida el alcance), o <c>null</c>.</summary>
    Task<DominioDePortal?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Dominio vigente con ese host (ya normalizado), o <c>null</c>.</summary>
    Task<DominioDePortal?> ObtenerPorHostAsync(string host, CancellationToken ct = default);

    /// <summary>
    /// Registra el dominio en <c>pending</c> con su token. Si el organismo ya tiene ESE mismo host, lo devuelve sin reiniciar el
    /// ciclo. Un host en uso o un organismo que ya tiene <paramref name="maximo"/> dominios fallan sin escribir.
    /// </summary>
    Task<PortalDominioRegistro> RegistrarAsync(Guid transitOfficeId, string host, string verificationToken, Guid? actor, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Retira el dominio (retiro lógico). Si era el principal, pasa a serlo el activo más antiguo del organismo. <c>null</c> si no existe.
    /// </summary>
    Task<DominioDePortal?> RetirarAsync(Guid id, Guid? actor, CancellationToken ct = default);

    /// <summary>Marca el dominio (que debe estar <c>active</c>) como principal del organismo; deja de serlo el anterior. <c>null</c> si no está activo.</summary>
    Task<DominioDePortal?> EstablecerPrincipalAsync(Guid id, Guid? actor, CancellationToken ct = default);

    /// <summary>Organismo (id y código) dueño del host solo si el dominio está <c>active</c> y el organismo activo. Sin caché.</summary>
    Task<PortalDominioOrganismo?> ObtenerOrganismoActivoPorHostAsync(string host, CancellationToken ct = default);

    /// <summary>Host <c>active</c> principal del organismo con ese código (sin distinguir mayúsculas), o <c>null</c>. Sin caché.</summary>
    Task<string?> ObtenerHostActivoPorCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary><c>verified</c> sin certificado (primera emisión) o <c>active</c> con certificado por vencer antes de <paramref name="renovarAntes"/>.</summary>
    Task<IReadOnlyList<string>> ListarHostsPendientesDeCertificadoAsync(DateTimeOffset renovarAntes, CancellationToken ct = default);

    Task<IReadOnlyList<PortalDominioAprovisionamiento>> ListarAprovisionamientoAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PortalDominioReclamo>> ReclamarVencidosAsync(int lote, TimeSpan arriendo, DateTimeOffset ahora, CancellationToken ct = default);

    /// <summary>Aplica el resultado de una comprobación DNS al dominio <paramref name="id"/> (solo a ese). Si deja de estar activo pierde el principal.</summary>
    Task<DominioDePortal?> AplicarComprobacionAsync(
        Guid id,
        string nuevoEstado,
        string? motivo,
        DateTimeOffset? verificadoEn,
        DateTimeOffset? graciaHasta,
        int intentos,
        DateTimeOffset? proximaComprobacion,
        DateTimeOffset ahora,
        Guid? actor,
        string? trabajo,
        CancellationToken ct = default);

    /// <summary>
    /// Registra la emisión del certificado del host y, si <paramref name="nuevoEstado"/> cambia, el estado (con <paramref name="activadoEn"/>).
    /// El primer dominio que pasa a activo queda como principal si el organismo no tiene otro.
    /// </summary>
    Task<DominioDePortal?> AplicarCertificadoAsync(
        string host,
        string nuevoEstado,
        DateTimeOffset? activadoEn,
        DateTimeOffset emitidoEn,
        DateTimeOffset? venceEn,
        string trabajo,
        CancellationToken ct = default);

    /// <summary>Un dominio <c>verified</c> cuyo certificado no se pudo emitir pasa a <c>failed</c> con el motivo. No toca a los demás.</summary>
    Task<DominioDePortal?> MarcarCertificadoFallidoAsync(
        string host, string motivo, DateTimeOffset ahora, DateTimeOffset proximaComprobacion, string trabajo, CancellationToken ct = default);
}

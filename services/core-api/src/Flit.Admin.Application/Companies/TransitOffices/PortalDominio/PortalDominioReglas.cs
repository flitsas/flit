using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalDominio;

/// <summary>Error de negocio del dominio del portal: estado HTTP, código estable y motivo exacto en español.</summary>
public sealed record PortalDominioError(int Status, string Code, string Message, int? EsperarSegundos = null);

/// <summary>Resultado de un caso de uso: el valor o el error.</summary>
public sealed record PortalDominioResultado<T>(T? Valor, PortalDominioError? Error)
{
    public bool Ok => Error is null;

    public static PortalDominioResultado<T> De(T valor) => new(valor, null);

    public static PortalDominioResultado<T> Falla(PortalDominioError error) => new(default, error);
}

/// <summary>Instrucción de DNS que el organismo debe crear.</summary>
public sealed record PortalDominioDnsDto(string Tipo, string Nombre, string Valor);

/// <summary>Forma de <c>DominioPortal</c> del contrato de la Épica #14297.</summary>
public sealed record PortalDominioDto(
    Guid Id, string Host, bool Principal, string Estado, string? Motivo, IReadOnlyList<PortalDominioDnsDto> Dns, long RowVersion);

public sealed record RegistrarPortalDominioRequest(string? Host);

/// <summary>Códigos estables y motivos exactos del dominio del portal (formato <c>{ code, message }</c>).</summary>
public static class PortalDominioErrores
{
    public const string Invalido = "PORTAL_DOMINIO_INVALIDO";
    public const string Reservado = "PORTAL_DOMINIO_RESERVADO";
    public const string EnUso = "PORTAL_DOMINIO_EN_USO";
    public const string Limite = "PORTAL_DOMINIO_LIMITE";
    public const string PrincipalNoActivo = "PORTAL_DOMINIO_PRINCIPAL_NO_ACTIVO";
    public const string Prohibido = "PORTAL_MARCA_PROHIBIDO";
    public const string NoRegistrado = "PORTAL_DOMINIO_NO_REGISTRADO";
    public const string Esperar = "PORTAL_DOMINIO_VERIFICACION_ESPERAR";

    /// <summary>Motivo estable guardado cuando el certificado de un dominio verificado no se pudo emitir.</summary>
    public const string CertificadoNoEmitido = "CERT_NOT_ISSUED";

    public static PortalDominioError DominioInvalido() => new(400, Invalido,
        "El dominio no es válido. Escriba solo el nombre del dominio, por ejemplo tramites.miorganismo.gov.co, sin https://, puerto ni rutas.");

    public static PortalDominioError DominioReservado(string host) => new(409, Reservado,
        $"El dominio {host} está reservado para FLIT y no se puede registrar. Use un dominio propio del organismo.");

    public static PortalDominioError DominioEnUso(string host) => new(409, EnUso,
        $"El dominio {host} ya está registrado por otro organismo o por una red de marca blanca. Use un dominio diferente.");

    public static PortalDominioError LimiteAlcanzado(int maximo) => new(409, Limite,
        $"El organismo ya tiene {maximo} dominios, el máximo permitido. Retire uno antes de registrar otro.");

    public static PortalDominioError NoActivoParaPrincipal(string host, string estado) => new(409, PrincipalNoActivo,
        $"Solo un dominio activo puede ser el principal. El dominio {host} está {estado}.");

    public static PortalDominioError DominioAjeno() => new(403, Prohibido, "Solo puede ver y cambiar los dominios del portal de su propio organismo.");

    public static PortalDominioError SinDominio() => new(404, NoRegistrado, "El dominio indicado no existe o ya fue retirado.");

    public static PortalDominioError VerificacionReciente(int segundos) => new(429, Esperar,
        $"La verificación se acaba de ejecutar. Espere {segundos} segundos antes de volver a intentarlo.", segundos);

    /// <summary>Motivo en español de un fallo guardado (<see cref="DomainStatusReasons"/> o <see cref="CertificadoNoEmitido"/>).</summary>
    public static string Motivo(string? codigo, string host) => codigo switch
    {
        null or "" => string.Empty,
        DomainStatusReasons.TxtNotFound => $"No encontramos el registro TXT _flit-verify.{host} en el DNS. Créelo con el valor indicado y vuelva a verificar.",
        DomainStatusReasons.TxtMismatch => $"El registro TXT _flit-verify.{host} existe, pero su valor no coincide con el indicado. Corríjalo y vuelva a verificar.",
        DomainStatusReasons.DnsError => "No pudimos consultar el DNS del dominio en este momento. Vuelva a verificar en unos minutos.",
        var c when c.StartsWith(CertificadoNoEmitido, StringComparison.Ordinal) =>
            "No se pudo emitir el certificado de seguridad del dominio. Revise que el dominio apunte al borde de FLIT; se volverá a intentar solo.",
        var otro => otro,
    };
}

/// <summary>
/// Máquina de estados del dominio del portal. La comprobación de TXT es la de marca blanca (<see cref="DomainStateMachine"/>); lo
/// distinto es el certificado: aquí el dominio <c>verified</c> pasa a <c>active</c> al llegar la señal, sin activación manual de
/// red (un portal público no tiene red ni login que activar). PURA: sin IO y sin reloj propio.
/// </summary>
public static class PortalDominioEstados
{
    public static DomainCheckDecision DecidirComprobacion(DomainCheckContext contexto, DomainVerificationOptions opciones) =>
        DomainStateMachine.DecideCheck(contexto, opciones);

    /// <summary>
    /// Señal de certificado emitido. <c>verified</c> → <c>active</c> (con su fecha de activación); <c>active</c> es una
    /// renovación y solo actualiza las fechas del certificado. Cualquier otro estado no admite certificado (<c>null</c>).
    /// </summary>
    public static DomainCertificateDecision? DecidirCertificado(string estadoActual, DateTimeOffset ahora) => estadoActual switch
    {
        TenantDomainStatus.Verified => new DomainCertificateDecision(TenantDomainStatus.Active, ahora),
        TenantDomainStatus.Active => new DomainCertificateDecision(TenantDomainStatus.Active, null),
        _ => null,
    };

    /// <summary>Estado del contrato (pendiente, verificado, activo, fallido).</summary>
    public static string Contrato(string estado) => estado switch
    {
        TenantDomainStatus.Pending => "pendiente",
        TenantDomainStatus.Verified => "verificado",
        TenantDomainStatus.Active => "activo",
        TenantDomainStatus.Failed => "fallido",
        _ => estado,
    };

    public static PortalDominioDto ADto(DominioDePortal dominio, string ipBorde)
    {
        ArgumentNullException.ThrowIfNull(dominio);
        var dns = new List<PortalDominioDnsDto>
        {
            new("TXT", $"_flit-verify.{dominio.Host}", dominio.VerificationToken),
        };
        if (!string.IsNullOrWhiteSpace(ipBorde))
            dns.Add(new PortalDominioDnsDto("A", dominio.Host, ipBorde));

        var motivo = dominio.Status == TenantDomainStatus.Failed ? PortalDominioErrores.Motivo(dominio.StatusReason, dominio.Host) : null;
        return new PortalDominioDto(dominio.Id, dominio.Host, dominio.IsPrimary, Contrato(dominio.Status), motivo, dns, dominio.RowVersion);
    }
}

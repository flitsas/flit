namespace Flit.Admin.Application.Identity;

/// <summary>
/// Identidad biométrica APROBADA y VIGENTE de una persona, resuelta desde el flujo de trámites
/// (HU #11028). Trae lo necesario para ESPEJARLA como validación administrativa sin falsear su
/// vigencia: se conservan la fecha de aprobación, el vencimiento original y el certificado emitido.
/// </summary>
/// <param name="Id">Validación de origen, para dejar constancia de dónde salió.</param>
/// <param name="TenantId">Compañía en cuyo trámite se validó (con grant vigente en el organismo).</param>
public sealed record PersonIdentitySnapshot(
    Guid Id,
    Guid TenantId,
    string Provider,
    string? CertificateHash,
    DateTimeOffset ValidatedAt,
    DateTimeOffset? ValidUntil);

/// <summary>
/// Puerto para encontrar la identidad que una PERSONA ya validó dentro del perímetro de un organismo
/// de tránsito (HU #11028), sin que el módulo Admin dependa de Trámites.
/// <para><b>Perímetro, y por qué:</b> un mandatario suele haber validado su identidad como parte de un
/// trámite de alguna de las compañías que operan con ese organismo, no "en el organismo". La búsqueda
/// se acota a los tenants con grant HABILITADO en el organismo: fuera de ese perímetro no se mira, para
/// no convertir el botón de vincular en una fuga de datos entre clientes que no se conocen.</para>
/// </summary>
public interface IPersonIdentityLookup
{
    Task<PersonIdentitySnapshot?> FindVigenteInTransitOfficeAsync(
        Guid transitOfficeId,
        string documentType,
        string documentNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

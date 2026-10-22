using System.Security.Cryptography;
using System.Text;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;

namespace Flit.Comparendos.Api.Modulos.Portal;

/// <summary>Un enlace recién emitido: la URL con el token (solo viaja en el correo) y hasta cuándo sirve.</summary>
public sealed record EnlacePortal(Guid Id, string Url, DateOnly Vence);

/// <summary>
/// Enlaces únicos al portal del conductor (§6.13, S-58): 32 bytes aleatorios en base64url; en la base solo su SHA-256.
/// Vigencia: hasta el fin del descuento del 25% más 30 días, con un mínimo de 30 días desde la emisión.
/// </summary>
public sealed class ServicioPortal(ComparendosDb db, ServicioCorreo correo, Reloj reloj)
{
    public EnlacePortal Emitir(Comparendo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var hoy = reloj.Hoy;
        var base_ = c.FechaFinDescuento25 is { } fin && fin > hoy ? fin : hoy;
        var vence = base_.AddDays(30);
        var enlace = new PortalEnlace
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            ContraventorDocumento = c.Contraventor?.NumeroDocumento ?? string.Empty,
            TokenHash = Hash(token),
            ExpiraEn = new DateTimeOffset(vence.ToDateTime(new TimeOnly(23, 59)), Domain.Tiempo.ColombiaTime.Offset).ToUniversalTime(),
            CreadoEn = reloj.Ahora,
        };
        db.PortalEnlaces.Add(enlace);
        return new EnlacePortal(enlace.Id, $"{correo.AppUrl}/portal/{token}", vence);
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

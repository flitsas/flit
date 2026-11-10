using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Deja en <c>cashier_event</c> lo que hace la caja: sin datos personales, con la IP solo como huella HMAC. Nunca registra el
/// token, el correo ni el código completo en los logs.
/// </summary>
public sealed class CajaAuditoria(PersonaNaturalDb db, PiiHasher hasher, TimeProvider reloj)
{
    public async Task RegistrarAsync(Cajero cajero, string accion, string? codigo, string resultado, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cajero);
        // Una excepción anterior pudo dejar entidades a medias en el contexto: el evento se guarda solo.
        db.ChangeTracker.Clear();
        db.Add(Nuevo(cajero.OrganismoId, cajero.UsuarioId, accion, codigo, resultado, ip, reloj.GetUtcNow(), hasher));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    internal static EventoCaja Nuevo(Guid organismoId, Guid usuarioId, string accion, string? codigo, string resultado, string? ip, DateTimeOffset ahora, PiiHasher hasher) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganismoId = organismoId,
        UsuarioId = usuarioId,
        Accion = accion,
        Codigo = codigo is { Length: > 20 } ? codigo[..20] : codigo,
        Resultado = resultado.Length > 30 ? resultado[..30] : resultado,
        IpHuella = string.IsNullOrEmpty(ip) ? null : Convert.ToHexStringLower(hasher.HashIp(ip)),
        CreadoEn = ahora,
    };
}

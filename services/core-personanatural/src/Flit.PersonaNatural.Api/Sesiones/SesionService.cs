using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Sesiones;

/// <summary>Resultado de validar un token de sesión.</summary>
public enum EstadoSesion
{
    Valida,
    Vencida,
    Invalida,
}

public sealed record SesionValidada(EstadoSesion Estado, Guid SolicitudId = default);

public sealed record SesionEmitida(string Token, DateTimeOffset ExpiraEn);

/// <summary>Lo que ve el ciudadano al reanudar. Sin datos personales: solo el estado del borrador.</summary>
public sealed record SolicitudReanudada(Guid SolicitudId, string CodigoPublico, string Estado, string TipoTramiteCodigo);

public abstract record ResultadoReanudar
{
    public sealed record Exito(SesionEmitida Sesion, SolicitudReanudada Solicitud) : ResultadoReanudar;

    /// <summary>No existe el radicado o la cédula no coincide: nunca se distingue cuál de las dos.</summary>
    public sealed record NoEncontrada : ResultadoReanudar;

    public sealed record Bloqueado(TimeSpan EsperarHasta) : ResultadoReanudar;
}

/// <summary>
/// Sesión efímera por trámite (HU #14035, ADR-0075). El token es opaco, de 256 bits, y solo se guarda su SHA-256: quien
/// lea la base no puede usar ninguna sesión. Abre únicamente SU solicitud, vive <see cref="SesionOptions.VigenciaMinutos"/>
/// desde el último uso y nunca más de <see cref="SesionOptions.TopeAbsolutoHoras"/>.
/// </summary>
public sealed class SesionService(PersonaNaturalDb db, PiiHasher hasher, TimeProvider reloj, IOptions<SesionOptions> options)
{
    private readonly SesionOptions _opciones = options.Value;

    /// <summary>Abre una sesión para una solicitud (la llama quien crea el borrador o quien lo reanuda).</summary>
    public async Task<SesionEmitida> IniciarAsync(Guid solicitudId, byte[]? ipHash, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var token = PiiHasher.NuevoToken();
        var expira = ahora.AddMinutes(_opciones.VigenciaMinutos);
        db.Add(new SesionTramite
        {
            Id = Guid.CreateVersion7(),
            SolicitudId = solicitudId,
            TokenHash = PiiHasher.HashToken(token),
            IpHash = ipHash,
            ExpiraEn = expira,
            CreatedAt = ahora,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SesionEmitida(token, expira);
    }

    /// <summary>
    /// Valida el token y, si sirve, renueva su vigencia sin pasar del tope absoluto. Un token inexistente o revocado es
    /// <see cref="EstadoSesion.Invalida"/>; uno vencido, <see cref="EstadoSesion.Vencida"/> (el front puede ofrecer reanudar).
    /// </summary>
    public async Task<SesionValidada> ValidarAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new SesionValidada(EstadoSesion.Invalida);

        var hash = PiiHasher.HashToken(token.Trim());
        var sesion = await db.Set<SesionTramite>().FirstOrDefaultAsync(s => s.TokenHash == hash, ct).ConfigureAwait(false);
        if (sesion is null || sesion.Revocada)
            return new SesionValidada(EstadoSesion.Invalida);

        var ahora = reloj.GetUtcNow();
        var tope = sesion.CreatedAt.AddHours(_opciones.TopeAbsolutoHoras);
        if (ahora >= sesion.ExpiraEn || ahora >= tope)
            return new SesionValidada(EstadoSesion.Vencida, sesion.SolicitudId);

        var renovada = ahora.AddMinutes(_opciones.VigenciaMinutos);
        sesion.ExpiraEn = renovada < tope ? renovada : tope;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SesionValidada(EstadoSesion.Valida, sesion.SolicitudId);
    }

    /// <summary>Revoca todas las sesiones de una solicitud (al cancelarla o radicarla).</summary>
    public Task<int> RevocarDeSolicitudAsync(Guid solicitudId, CancellationToken ct) =>
        db.Set<SesionTramite>().Where(s => s.SolicitudId == solicitudId && !s.Revocada)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Revocada, true), ct);

    /// <summary>
    /// Reanuda con radicado y cédula. La respuesta es la misma, y de tiempo parecido, si el radicado no existe o la cédula
    /// no coincide; cinco fallos seguidos sobre un radicado (exista o no) lo bloquean durante la ventana, incluso para quien
    /// ya traiga los datos correctos.
    /// </summary>
    public async Task<ResultadoReanudar> ReanudarAsync(string radicado, string cedula, byte[]? ipHash, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var clave = PiiHasher.ClaveRadicado(radicado);
        var ventana = TimeSpan.FromMinutes(_opciones.VentanaIntentosMinutos);
        var desde = ahora - ventana;

        var intentos = await db.Set<IntentoReanudar>().Where(i => i.Clave == clave && i.OcurrioEn > desde)
            .OrderBy(i => i.OcurrioEn).Select(i => i.OcurrioEn).ToListAsync(ct).ConfigureAwait(false);
        if (intentos.Count >= _opciones.MaxIntentosFallidos)
        {
            var libera = intentos[intentos.Count - _opciones.MaxIntentosFallidos] + ventana;
            return new ResultadoReanudar.Bloqueado(libera > ahora ? libera - ahora : TimeSpan.FromSeconds(1));
        }

        var codigo = PiiHasher.NormalizarRadicado(radicado);
        var solicitud = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.CodigoPublico == codigo && s.Estado != EstadoSolicitud.Cancelada)
            .Select(s => new { s.Id, s.CodigoPublico, s.Estado, s.TipoTramiteCodigo, s.CedulaSolicitanteHash })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // Siempre se calcula la huella y se compara, con un valor de relleno si no hay solicitud: el tiempo no delata nada.
        var huella = hasher.HashCedula(cedula);
        var esperada = solicitud?.CedulaSolicitanteHash is { Length: 32 } h ? h : new byte[32];
        var coincide = PiiHasher.Iguales(huella, esperada) & solicitud is not null;

        if (!coincide)
        {
            db.Add(new IntentoReanudar { Clave = clave, OcurrioEn = ahora });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new ResultadoReanudar.NoEncontrada();
        }

        await db.Set<IntentoReanudar>().Where(i => i.Clave == clave).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var sesion = await IniciarAsync(solicitud!.Id, ipHash, ct).ConfigureAwait(false);
        return new ResultadoReanudar.Exito(sesion, new SolicitudReanudada(solicitud.Id, solicitud.CodigoPublico, solicitud.Estado, solicitud.TipoTramiteCodigo));
    }
}

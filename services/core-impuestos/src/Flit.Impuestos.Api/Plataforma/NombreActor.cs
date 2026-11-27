using Flit.Identidad.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Nombre de quien actúa, para <c>creada_por_nombre</c> y <c>cerrada_por_nombre</c> (1.2.0). El token de la plataforma no trae el
/// claim <c>name</c>, así que el nombre se le pide a Identidad; si no se puede, queda el correo. Nunca lanza por Identidad: una
/// creación o un cierre no se caen porque falte un nombre.
/// </summary>
/// <example>
/// <code>
/// var nombre = await nombres.NombreAsync(usuario, ct); // "Ana Pérez" o, si Identidad no responde, el correo
/// </code>
/// </example>
public interface INombreActor
{
    /// <summary>
    /// <paramref name="nombreDelToken"/> si viene; si no, el nombre que Identidad tiene para <paramref name="usuario"/> en
    /// <paramref name="empresa"/> (la compañía del usuario); si tampoco, <paramref name="correo"/>.
    /// </summary>
    Task<string> ResolverAsync(Guid? empresa, Guid usuario, string? nombreDelToken, string correo, CancellationToken ct);
}

public static class NombreActorExtensions
{
    /// <summary>El nombre de <paramref name="usuario"/> en su propia compañía (la del token; para el Super Admin, FLIT).</summary>
    public static Task<string> NombreAsync(this INombreActor nombres, UsuarioActual usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(nombres);
        ArgumentNullException.ThrowIfNull(usuario);
        return nombres.ResolverAsync(usuario.EmpresaId, usuario.Id, usuario.NombreDelToken, usuario.Email, ct);
    }
}

/// <summary>
/// Por gRPC a Identidad (<c>ObtenerUsuario</c>, scope <c>platform.identidad.read</c> del cliente que registra
/// <see cref="GuardSuscripcionExtensions.AddSuscripcionImpuestos"/>, metadata <c>x-flit-tenant-id</c> = la compañía del usuario). Cada
/// nombre resuelto queda <see cref="Vigencia"/> en caché por (compañía, usuario). Sin Identidad conectada, NOT_FOUND, plazo vencido,
/// nombre vacío o cualquier otro fallo: el correo, sin guardarlo en caché, y un log Debug con ids y el motivo (nunca el correo).
/// </summary>
internal sealed partial class ResolvedorNombreActor(IdentidadService.IdentidadServiceClient? cliente, IMemoryCache cache, ILogger<ResolvedorNombreActor> logger)
    : INombreActor
{
    /// <summary>Cuánto vale un nombre resuelto antes de volver a preguntar.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

    public async Task<string> ResolverAsync(Guid? empresa, Guid usuario, string? nombreDelToken, string correo, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(nombreDelToken))
            return nombreDelToken.Trim();
        if (cliente is null)
            return Respaldo(empresa, usuario, correo, "NO_CONECTADO");
        if (empresa is not { } compania || compania == Guid.Empty || usuario == Guid.Empty)
            return Respaldo(empresa, usuario, correo, "SIN_EMPRESA_O_USUARIO");

        var clave = (nameof(ResolvedorNombreActor), compania, usuario);
        if (cache.TryGetValue(clave, out string? guardado) && guardado is not null)
            return guardado;
        try
        {
            var r = await cliente.ObtenerUsuarioAsync(new ObtenerUsuarioRequest { UsuarioId = usuario.ToString() },
                new Metadata { { "x-flit-tenant-id", compania.ToString() } }, cancellationToken: ct).ConfigureAwait(false);
            var nombre = r.Usuario?.Nombre?.Trim();
            if (string.IsNullOrEmpty(nombre))
                return Respaldo(empresa, usuario, correo, "NOMBRE_VACIO");
            cache.Set(clave, nombre, Vigencia);
            return nombre;
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            return Respaldo(empresa, usuario, correo, ex.StatusCode.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Token de servicio, red o cualquier otro fallo al preguntar: el correo.
            return Respaldo(empresa, usuario, correo, ex.GetType().Name);
        }
    }

    private string Respaldo(Guid? empresa, Guid usuario, string correo, string motivo)
    {
        LogRespaldo(logger, usuario, empresa, motivo);
        return correo;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Identidad no dio el nombre del usuario {Usuario} de {Empresa} ({Motivo}): se guarda el respaldo.")]
    private static partial void LogRespaldo(ILogger logger, Guid usuario, Guid? empresa, string motivo);
}

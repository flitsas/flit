using Flit.Identidad.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Nombre visible de los usuarios de una compañía (HU #13845, F08-C1/C6, decisión Q3): Diagnóstico no guarda copia de los
/// usuarios (plan §5), así que el nombre del creador de un lote se le pregunta a Identidad cada vez que se muestra. Devuelve
/// un nombre por cada usuario pedido; el que no se pudo resolver sale <see cref="ResolutorNombresUsuario.NoDisponible"/>.
/// Nunca lanza por Identidad: un listado no se cae porque falte un nombre. Lo reutiliza F07-H2 (encabezado del PDF).
/// </summary>
public interface IResolutorNombresUsuario
{
    Task<IReadOnlyDictionary<Guid, string>> ResolverAsync(Guid empresa, IEnumerable<Guid> usuarios, CancellationToken ct);
}

/// <summary>
/// Por gRPC a Identidad (<c>ObtenerUsuario</c>, scope <c>platform.identidad.read</c>, metadata <c>x-flit-tenant-id</c> = la
/// compañía del lote): solo los usuarios distintos que no estén en la caché, en paralelo, y cada nombre resuelto queda
/// <see cref="Vigencia"/> en caché por (compañía, usuario). NOT_FOUND, un nombre vacío o cualquier fallo al preguntar dan
/// <see cref="NoDisponible"/> y no se guardan: la siguiente petición vuelve a preguntar. El log lleva solo ids y el código gRPC.
/// </summary>
internal sealed partial class ResolutorNombresUsuario(IdentidadService.IdentidadServiceClient cliente, IMemoryCache cache, ILogger<ResolutorNombresUsuario> logger)
    : IResolutorNombresUsuario
{
    /// <summary>Lo que se muestra cuando Identidad no da el nombre (decisión Q3).</summary>
    public const string NoDisponible = "No disponible";

    /// <summary>Cuánto vale un nombre resuelto antes de volver a preguntar.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyDictionary<Guid, string>> ResolverAsync(Guid empresa, IEnumerable<Guid> usuarios, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuarios);
        var distintos = usuarios.Distinct().ToList();
        var nombres = new Dictionary<Guid, string>(distintos.Count);
        var faltantes = new List<Guid>();
        foreach (var usuario in distintos)
        {
            if (cache.TryGetValue(Clave(empresa, usuario), out string? nombre) && nombre is not null)
                nombres[usuario] = nombre;
            else
                faltantes.Add(usuario);
        }

        var resueltos = await Task.WhenAll(faltantes.Select(u => PreguntarAsync(empresa, u, ct))).ConfigureAwait(false);
        foreach (var (usuario, nombre) in resueltos)
        {
            if (nombre is not null)
                cache.Set(Clave(empresa, usuario), nombre, Vigencia);
            nombres[usuario] = nombre ?? NoDisponible;
        }

        return nombres;
    }

    private async Task<(Guid Usuario, string? Nombre)> PreguntarAsync(Guid empresa, Guid usuario, CancellationToken ct)
    {
        try
        {
            var r = await cliente.ObtenerUsuarioAsync(new ObtenerUsuarioRequest { UsuarioId = usuario.ToString() },
                new Metadata { { "x-flit-tenant-id", empresa.ToString() } }, cancellationToken: ct).ConfigureAwait(false);
            var nombre = r.Usuario?.Nombre?.Trim();
            return (usuario, string.IsNullOrEmpty(nombre) ? null : nombre);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return (usuario, null);
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            LogNoSePudo(logger, empresa, usuario, ex.StatusCode.ToString());
            return (usuario, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Token de servicio, red o cualquier otro fallo al preguntar: el listado sigue con «No disponible».
            LogNoSePudo(logger, empresa, usuario, ex.GetType().Name);
            return (usuario, null);
        }
    }

    private static (string, Guid, Guid) Clave(Guid empresa, Guid usuario) => (nameof(ResolutorNombresUsuario), empresa, usuario);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo preguntar a Identidad por el usuario {Usuario} de {Empresa} ({Motivo}): se muestra «No disponible».")]
    private static partial void LogNoSePudo(ILogger logger, Guid empresa, Guid usuario, string motivo);
}

/// <summary>
/// Sin Identidad configurada (<c>Diagnostico:Identidad:Address</c> vacío, entorno LOCAL): respaldo fijo, todo usuario sale
/// <see cref="ResolutorNombresUsuario.NoDisponible"/> sin llamar a nada.
/// </summary>
internal sealed class ResolutorNombresSinIdentidad : IResolutorNombresUsuario
{
    public Task<IReadOnlyDictionary<Guid, string>> ResolverAsync(Guid empresa, IEnumerable<Guid> usuarios, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuarios);
        IReadOnlyDictionary<Guid, string> nombres = usuarios.Distinct().ToDictionary(u => u, _ => ResolutorNombresUsuario.NoDisponible);
        return Task.FromResult(nombres);
    }
}

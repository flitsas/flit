using Flit.Identidad.Grpc.V1;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Sdk.Grpc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api.Modulos.Backoffice;

/// <summary>Una compañía tal como la tiene la plataforma: razón social y NIT (solo el número; null si no lo informa).</summary>
public sealed record EmpresaDirectorio(Guid Id, string Nombre, string? Nit);

/// <summary><c>EmpresaIdentificada</c> (impuestos.v1.yaml 1.1.0): la compañía elegida por el Super Admin con su NIT.</summary>
public sealed record EmpresaIdentificadaDto(Guid Id, string Nombre, string? Nit);

/// <summary>
/// Nombre y NIT de las compañías con Impuestos, para el encabezado y la lista del Super Admin. Impuestos no guarda datos de la
/// compañía: los toma de la plataforma.
/// </summary>
public interface IDirectorioEmpresas
{
    Task<IReadOnlyDictionary<Guid, EmpresaDirectorio>> ListarAsync(CancellationToken ct);
}

/// <summary>
/// Quién es una compañía para el Super Admin (delta 7 de impuestos.v1.yaml 1.1.0): su <c>EmpresaIdentificada</c> en la página de
/// usuarios y en el detalle del aprovisionamiento, y el nombre y NIT de cada fila del backoffice. Lee el
/// <see cref="IDirectorioEmpresas"/> y, si la plataforma no responde, no falla la petición: la compañía sale con su id, nombre
/// vacío y NIT null (la pantalla sigue sirviendo y el error queda en el log).
/// </summary>
/// <example>
/// <code>
/// var empresa = await ambito.IdentificarAsync(tenantId, ct); // { Id, Nombre, Nit }
/// </code>
/// </example>
public sealed partial class AmbitoDeLaEmpresa(IDirectorioEmpresas directorio, ILogger<AmbitoDeLaEmpresa> logger)
{
    public async Task<IReadOnlyDictionary<Guid, EmpresaDirectorio>> DirectorioAsync(CancellationToken ct)
    {
        try
        {
            return await directorio.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogSinDirectorio(logger, ex.GetType().Name);
            return new Dictionary<Guid, EmpresaDirectorio>();
        }
    }

    public async Task<EmpresaIdentificadaDto> IdentificarAsync(Guid empresa, CancellationToken ct) =>
        Identificar(await DirectorioAsync(ct).ConfigureAwait(false), empresa);

    public static EmpresaIdentificadaDto Identificar(IReadOnlyDictionary<Guid, EmpresaDirectorio> directorio, Guid empresa)
    {
        ArgumentNullException.ThrowIfNull(directorio);
        return directorio.TryGetValue(empresa, out var e) ? new EmpresaIdentificadaDto(e.Id, e.Nombre, e.Nit) : new EmpresaIdentificadaDto(empresa, string.Empty, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer el directorio de compañías de la plataforma ({Motivo}): se muestran sin nombre ni NIT.")]
    private static partial void LogSinDirectorio(ILogger logger, string motivo);
}

/// <summary>
/// Por el gRPC global de empresas de Identidad (<c>flit.identidad.v1.EmpresasService.ListarEmpresasConProducto</c>, scope
/// <c>platform.identidad.empresas</c>, sin <c>x-flit-tenant-id</c>): las compañías activas con Impuestos encendido. Descarta ids que
/// no son uuid y repite una compañía una sola vez.
/// </summary>
internal sealed class DirectorioEmpresasIdentidad(EmpresasService.EmpresasServiceClient cliente) : IDirectorioEmpresas
{
    public async Task<IReadOnlyDictionary<Guid, EmpresaDirectorio>> ListarAsync(CancellationToken ct)
    {
        var r = await cliente.ListarEmpresasConProductoAsync(new ListarEmpresasConProductoRequest { Producto = UsuariosPlataforma.Producto }, cancellationToken: ct)
            .ConfigureAwait(false);
        var empresas = new Dictionary<Guid, EmpresaDirectorio>();
        foreach (var e in r.Empresas)
        {
            if (Guid.TryParse(e.Id, out var id) && id != Guid.Empty)
                empresas.TryAdd(id, new EmpresaDirectorio(id, e.Nombre.Trim(), string.IsNullOrWhiteSpace(e.Nit) ? null : e.Nit.Trim()));
        }

        return empresas;
    }
}

/// <summary>Sin Identidad configurada: no hay directorio (todas las compañías salen sin nombre ni NIT).</summary>
internal sealed class DirectorioEmpresasNoConectado : IDirectorioEmpresas
{
    public Task<IReadOnlyDictionary<Guid, EmpresaDirectorio>> ListarAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, EmpresaDirectorio>>(new Dictionary<Guid, EmpresaDirectorio>());
}

/// <summary>Registro del backoffice de compañías (M13).</summary>
public static class BackofficeImpuestosExtensions
{
    /// <summary>Scope del gRPC global de empresas de Identidad.</summary>
    public const string ScopeEmpresas = "platform.identidad.empresas";

    /// <summary>
    /// Con <see cref="GuardSuscripcionExtensions.IdentidadAddressKey"/>, el directorio por Identidad (plazo 5 s); sin ella, el «no
    /// conectado». TryAdd: las pruebas registran su doble antes.
    /// </summary>
    public static IServiceCollection AddBackofficeImpuestos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration[GuardSuscripcionExtensions.IdentidadAddressKey] is { Length: > 0 } identidad)
        {
            services.AddFlitGrpcClient<EmpresasService.EmpresasServiceClient>(configuration, new Uri(identidad), ScopeEmpresas, o => o.Deadline = TimeSpan.FromSeconds(5));
            services.TryAddScoped<IDirectorioEmpresas, DirectorioEmpresasIdentidad>();
        }
        else
        {
            services.TryAddSingleton<IDirectorioEmpresas, DirectorioEmpresasNoConectado>();
        }

        services.AddScoped<AmbitoDeLaEmpresa>();
        services.AddScoped<LecturaEntreEmpresas>();
        return services;
    }
}

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>
/// Consulta de vehículo del portal (HU #14039): llama a core-consultas, proyecta a dato mínimo y devuelve solo eso. La
/// medición del consumo la deja core-consultas (por empresa y producto) en cada llamada que llega al proveedor, así que el
/// portal no la duplica aquí.
/// </summary>
public sealed class ConsultaVehiculoService(IConsultasRunt runt)
{
    /// <summary>
    /// <paramref name="placa"/> y <paramref name="propietario"/> o <paramref name="vin"/>, ya validados y normalizados por
    /// <see cref="EntradaConsulta"/>. Con VIN no hace falta el propietario.
    /// </summary>
    /// <exception cref="ProveedorNoDisponibleException">core-consultas no respondió o el proveedor del RUNT estaba caído.</exception>
    public async Task<VehiculoMinimo> ConsultarAsync(string? placa, string? vin, DocumentoConsulta? propietario, string? referencia, CancellationToken ct)
    {
        var resultado = await runt.ConsultarVehiculoAsync(placa, vin, propietario, referencia, ct).ConfigureAwait(false);
        if (ProyeccionMinima.ProveedorCaido(resultado))
            throw new ProveedorNoDisponibleException("El proveedor del RUNT no respondió.");

        return ProyeccionMinima.Vehiculo(resultado, placa);
    }
}

/// <summary>
/// Consulta de conductor del portal (HU #14040): mismo patrón que el vehículo. core-consultas resuelve la caché por TTL
/// (que solo reutiliza una persona con consentimiento, y el adaptador lo envía) y mide el consumo a nombre de la empresa.
/// </summary>
public sealed class ConsultaConductorService(IConsultasRunt runt)
{
    /// <exception cref="ProveedorNoDisponibleException">core-consultas no respondió o el proveedor del RUNT estaba caído.</exception>
    public async Task<ConductorMinimo> ConsultarAsync(DocumentoConsulta documento, string? referencia, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var resultado = await runt.ConsultarConductorAsync(documento, referencia, ct).ConfigureAwait(false);
        if (ProyeccionMinima.ProveedorCaido(resultado))
            throw new ProveedorNoDisponibleException("El proveedor del RUNT no respondió.");

        return ProyeccionMinima.Conductor(resultado);
    }
}

/// <summary>Registro de las consultas del portal (una extensión por HU para no pisar <c>Program.cs</c>).</summary>
public static class ConsultasPortalExtensions
{
    /// <summary>HU #14039 y #14040: cliente de core-consultas y consultas de vehículo y de conductor.</summary>
    public static IServiceCollection AddConsultasPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddConsultasRunt(configuration);
        services.AddScoped<ConsultaVehiculoService>();
        services.AddScoped<ConsultaConductorService>();
        return services;
    }
}

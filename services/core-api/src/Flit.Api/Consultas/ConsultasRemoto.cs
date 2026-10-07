using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.Consultas;

/// <summary>
/// core-api frente a core-consultas (Epic #13316; ADR-0065). Todo detrás de <c>Consultas:Remoto:Habilitado</c>
/// (apagada por defecto): sin ella core-api sigue consultando en proceso como siempre y no se registra nada aquí.
/// </summary>
internal static class ConsultasRemoto
{
    public const string FlagKey = "Consultas:Remoto:Habilitado";
    public const string AddressKey = "Consultas:Remoto:Address";

    public static IServiceCollection AddConsultasRemoto(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue(FlagKey, false))
            return services;

        var address = configuration[AddressKey];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{FlagKey} está encendida pero {AddressKey} no es una URL (p. ej. http://core-consultas:8084).");

        // HU #13344: la configuración por empresa que guarda el SuperAdmin también queda en Consultas.
        services.AddFlitGrpcClient<ConsultasAdminService.ConsultasAdminServiceClient>(configuration, uri, "platform.consultas.admin");
        services.AddScoped<IConsultasConfigSync, GrpcConsultasConfigSync>();
        // HU #13345: consumo por empresa para el SuperAdmin.
        services.AddScoped<IConsultasConsumo, GrpcConsultasConsumo>();
        return services;
    }
}

/// <summary>Envía la configuración de consultas de la empresa a <c>ConsultasAdminService</c> (HU #13344).</summary>
internal sealed class GrpcConsultasConfigSync(ConsultasAdminService.ConsultasAdminServiceClient client) : IConsultasConfigSync
{
    public async Task SyncAsync(Guid tenantId, TenantSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            // La empresa EDITADA, no la del SuperAdmin que edita: va explícita en la metadata.
            await client.GuardarConfiguracionEmpresaAsync(
                new GuardarConfiguracionEmpresaRequest { Configuracion = ToProto(settings) },
                new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, tenantId.ToString() } },
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            throw new ConsultasConfigSyncException($"Consultas respondió {ex.StatusCode}: {ex.Status.Detail}", ex);
        }
        catch (InvalidOperationException ex)
        {
            // El token de servicio no se pudo obtener (Identidad caída o cliente mal configurado).
            throw new ConsultasConfigSyncException(ex.Message, ex);
        }
    }

    internal static ConfiguracionEmpresa ToProto(TenantSettings settings)
    {
        var config = new ConfiguracionEmpresa
        {
            FailoverTimeoutMs = settings.RuntFailoverTimeoutMs,
            FuenteMultas = settings.FinesQuerySource ?? string.Empty,
            AvaluoPrincipal = settings.AvaluoProviderConfig.Primary,
        };
        config.AvaluosHabilitados.AddRange(settings.AvaluoProviderConfig.Enabled);
        foreach (var (tipo, seleccion) in settings.ConsultationProviderConfig.ByKind)
        {
            var cadena = new CadenaProveedores { Principal = seleccion.Primary };
            cadena.Respaldo.AddRange(seleccion.Fallback ?? []);
            config.Cadenas[tipo] = cadena;
        }

        return config;
    }
}

/// <summary>Consumo agregado de una empresa en Consultas (HU #13345).</summary>
internal interface IConsultasConsumo
{
    /// <exception cref="ConsultasNoDisponibleException">Consultas no respondió.</exception>
    Task<IReadOnlyList<ConsumoConsultasDto>> ObtenerAsync(Guid tenantId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct);
}

internal sealed record ConsumoConsultasDto(string Producto, string Fuente, long Total, long DesdeCache, long Errores, long LatenciaPromedioMs);

internal sealed class ConsultasNoDisponibleException(string message, Exception inner) : Exception(message, inner);

internal sealed class GrpcConsultasConsumo(ConsultasAdminService.ConsultasAdminServiceClient client) : IConsultasConsumo
{
    public async Task<IReadOnlyList<ConsumoConsultasDto>> ObtenerAsync(Guid tenantId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        try
        {
            var respuesta = await client.ObtenerConsumoAsync(
                new ObtenerConsumoRequest
                {
                    Desde = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(desde),
                    Hasta = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(hasta),
                },
                new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, tenantId.ToString() } },
                cancellationToken: ct).ConfigureAwait(false);
            return [.. respuesta.Consumos.Select(c => new ConsumoConsultasDto(c.Producto, c.Fuente, c.Total, c.DesdeCache, c.Errores, c.LatenciaPromedioMs))];
        }
        catch (RpcException ex)
        {
            throw new ConsultasNoDisponibleException($"Consultas respondió {ex.StatusCode}: {ex.Status.Detail}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConsultasNoDisponibleException(ex.Message, ex);
        }
    }
}


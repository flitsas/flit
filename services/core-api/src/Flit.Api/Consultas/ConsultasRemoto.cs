using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Consultas.Grpc.V1;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Improntas.Domain;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Api.Consultas;

/// <summary>
/// core-api frente a core-consultas (Epic #13316; ADR-0065). Desde el corte (HU #13348) core-api no tiene proveedores
/// ni sus secretos: toda consulta, avalúo, documento de proveedor y validación de identidad va a core-consultas, sin
/// respaldo en proceso. <see cref="AddressKey"/> es obligatoria.
/// </summary>
internal static class ConsultasRemoto
{
    public const string AddressKey = "Consultas:Remoto:Address";

    public static IServiceCollection AddConsultasRemoto(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration[AddressKey];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{AddressKey} no es una URL (p. ej. http://core-consultas:8084): core-api consulta por core-consultas.");

        // HU #13344: la configuración por empresa que guarda el SuperAdmin también queda en Consultas.
        services.AddFlitGrpcClient<ConsultasAdminService.ConsultasAdminServiceClient>(configuration, uri, "platform.consultas.admin");
        services.AddScoped<IConsultasConfigSync, GrpcConsultasConfigSync>();
        // HU #13345: consumo por empresa para el SuperAdmin.
        services.AddScoped<IConsultasConsumo, GrpcConsultasConsumo>();

        // HU #13346/#13348: las consultas de Trámites van a core-consultas. Deadline amplio: una consulta al RUNT con su
        // respaldo puede tardar decenas de segundos. Las cadenas globales (Consultations:DefaultChains) solo sirven
        // para mostrar la configuración: la cadena la ejecuta Consultas.
        var deadline = TimeSpan.FromSeconds(configuration.GetValue("Consultas:Remoto:DeadlineSegundos", 90));
        services.AddFlitGrpcClient<ConsultasService.ConsultasServiceClient>(configuration, uri, "platform.consultas", o => o.Deadline = deadline);
        services.Configure<ConsultationChainOptions>(o => configuration.GetSection(ConsultationChainOptions.SectionName).Bind(o));
        services.AddScoped<ConsultasRemotasCliente>();
        services.AddScoped<IConsultationProviderRegistry, ConsultasRemotasRegistry>();
        services.AddScoped<IConsultationProviderChainResolver, ConsultasRemotasChainResolver>();

        // HU #13348: avalúos, Confirmación RUNT, impronta y certificado RUES.
        services.AddScoped<IAvaluoSugeridor, AvaluoSugeridorRemoto>();
        services.AddScoped<IRuntVehicleRawClient, RuntCrudoPorConsultas>();
        services.AddScoped<IImprontaExternalClient, ImprontaPorConsultas>();
        services.AddScoped<IRuesExternalClient, RuesPorConsultas>();

        AddValidacionIdentidadRemota(services, configuration, uri);
        return services;
    }

    /// <summary>
    /// HU #13351: los clientes de Kyverum Verify están en Consultas (que guarda el secreto del aviso) y el aviso vuelve
    /// por el bus. Exige el bus de Trámites: sin él el resultado no llegaría.
    /// </summary>
    private static void AddValidacionIdentidadRemota(IServiceCollection services, IConfiguration configuration, Uri uri)
    {
        if (!configuration.GetValue("Tramites:Bus:Habilitado", false))
            throw new InvalidOperationException("core-api exige Tramites:Bus:Habilitado=true: el aviso de Kyverum Verify vuelve de Consultas por el bus.");

        services.AddFlitGrpcClient<ValidacionIdentidadService.ValidacionIdentidadServiceClient>(configuration, uri, "platform.consultas");
        services.RemoveAll<IKyverumVerifyClient>();
        services.RemoveAll<IKyverumCertificateClient>();
        services.AddScoped<IKyverumVerifyClient, KyverumVerifyPorConsultas>();
        services.AddScoped<IKyverumCertificateClient, KyverumCertificadoPorConsultas>();
        services.AddFlitConsumer<FlitDbContext, AvisoKyverumConsumer, AvisoKyverumVerify>(
            configuration, AvisoKyverumConsumer.Cola, producer: "consultas", [AvisoKyverumConsumer.Tipo]);
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


using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Guard de suscripción por petición (plan §5.1, F6-CF2): toda petición bajo el prefijo de negocio pregunta si la compañía del
/// token tiene Impuestos activo y, si no, si no hay compañía o si no se puede saber, responde 403
/// <c>{ code: PRODUCTO_NO_HABILITADO }</c>. Va después de la autenticación y la autorización: sin token sigue siendo 401. Las rutas
/// de salud quedan fuera del prefijo. El Super Admin autenticado pasa siempre (M13: opera sobre otras compañías con empresa
/// explícita y ve las suspendidas). Solo niega: no borra ni desvincula datos de la compañía.
/// </summary>
internal sealed partial class GuardSuscripcion(RequestDelegate siguiente, string prefijo, ILogger<GuardSuscripcion> logger)
{
    public const string CodigoNoHabilitado = "PRODUCTO_NO_HABILITADO";

    public async Task InvokeAsync(HttpContext context, IProductosHabilitados productos, IPlatformTenantAccessor tenants)
    {
        if (!context.Request.Path.StartsWithSegments(prefijo, StringComparison.OrdinalIgnoreCase))
        {
            await siguiente(context).ConfigureAwait(false);
            return;
        }

        // El Super Admin pasa aunque su propia compañía no tenga Impuestos: opera sobre otras con empresa explícita y ve las
        // suspendidas en solo lectura (impuestos.v1.yaml, F4-CF10). Un token de servicio nunca es Super Admin aquí.
        if (PlatformPrincipal.IsSuperAdmin(context.User) && !PlatformPrincipal.IsService(context.User))
        {
            await siguiente(context).ConfigureAwait(false);
            return;
        }

        var empresa = tenants.Current.TenantId;
        if (empresa is { } id && await productos.ImpuestosHabilitadoAsync(id, context.RequestAborted).ConfigureAwait(false))
        {
            await siguiente(context).ConfigureAwait(false);
            return;
        }

        LogNegado(logger, empresa);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new { code = CodigoNoHabilitado, error = "Impuestos no está habilitado para tu empresa." }, context.RequestAborted).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Petición a Impuestos negada: la empresa {Empresa} no tiene el producto activo o no se pudo verificar.")]
    private static partial void LogNegado(ILogger logger, Guid? empresa);
}

public static class GuardSuscripcionExtensions
{
    public const string IdentidadAddressKey = "Impuestos:Identidad:Address";
    public const string SimularSuscripcionKey = "Impuestos:Identidad:SimularSuscripcionActiva";

    /// <summary>
    /// Con <see cref="IdentidadAddressKey"/>: Identidad por gRPC (scope <c>platform.identidad.read</c>, plazo 5 s). Sin ella: ninguna
    /// compañía tiene Impuestos, salvo <see cref="SimularSuscripcionKey"/>=true explícito. Un registro previo de
    /// <see cref="IProductosHabilitados"/> (pruebas) se respeta.
    /// </summary>
    public static IServiceCollection AddSuscripcionImpuestos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddMemoryCache();
        if (configuration[IdentidadAddressKey] is { Length: > 0 } identidad)
        {
            services.AddFlitGrpcClient<Flit.Identidad.Grpc.V1.IdentidadService.IdentidadServiceClient>(
                configuration, new Uri(identidad), "platform.identidad.read", o => o.Deadline = TimeSpan.FromSeconds(5));
            services.TryAddSingleton<IProductosHabilitados, ProductosHabilitadosIdentidad>();
        }
        else
        {
            services.TryAddSingleton<IProductosHabilitados>(new ProductosSinIdentidad(configuration.GetValue(SimularSuscripcionKey, false)));
        }

        return services;
    }

    /// <summary>Aplica el guard a todo lo que cuelga de <paramref name="prefijo"/>. Va después de <c>UseAuthorization</c>.</summary>
    public static WebApplication UseGuardSuscripcion(this WebApplication app, string prefijo)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<GuardSuscripcion>(prefijo);
        return app;
    }
}

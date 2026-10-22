using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Guard de suscripción por petición (HU #13726, F00.1-H4; hasta que exista el guard del SDK, P2). Toda petición bajo el
/// prefijo de negocio pregunta si la compañía del token tiene Diagnóstico activo y, si no, si no hay compañía o si no se
/// puede saber, responde 403 <c>{ code: PRODUCTO_NO_HABILITADO }</c>. Va después de la autenticación y la autorización:
/// sin token sigue siendo 401. Las rutas de salud quedan fuera del prefijo y no pasan por aquí. Solo niega: no borra ni
/// desvincula datos de la compañía (suspender y reactivar los conserva).
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

        var empresa = tenants.Current.TenantId;
        if (empresa is { } id && await productos.DiagnosticoHabilitadoAsync(id, context.RequestAborted).ConfigureAwait(false))
        {
            await siguiente(context).ConfigureAwait(false);
            return;
        }

        LogNegado(logger, empresa);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new { code = CodigoNoHabilitado, error = "Diagnóstico no está habilitado para tu empresa." }, context.RequestAborted).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Petición a Diagnóstico negada: la empresa {Empresa} no tiene el producto activo o no se pudo verificar.")]
    private static partial void LogNegado(ILogger logger, Guid? empresa);
}

public static class GuardSuscripcionExtensions
{
    public const string IdentidadAddressKey = "Diagnostico:Identidad:Address";
    public const string SimularSuscripcionKey = "Diagnostico:Identidad:SimularSuscripcionActiva";

    /// <summary>
    /// Con <see cref="IdentidadAddressKey"/>: Identidad por gRPC (scope <c>platform.identidad.read</c>, plazo 5 s). Sin
    /// ella: ninguna compañía tiene Diagnóstico, salvo <see cref="SimularSuscripcionKey"/>=true explícito. Un registro
    /// previo de <see cref="IProductosHabilitados"/> (pruebas) se respeta. El mismo cliente resuelve el nombre de los
    /// usuarios (<see cref="IResolutorNombresUsuario"/>, HU #13845); sin Identidad, respaldo fijo «No disponible».
    /// </summary>
    public static IServiceCollection AddSuscripcionDiagnostico(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddMemoryCache();
        if (configuration[IdentidadAddressKey] is { Length: > 0 } identidad)
        {
            services.AddFlitGrpcClient<Flit.Identidad.Grpc.V1.IdentidadService.IdentidadServiceClient>(
                configuration, new Uri(identidad), "platform.identidad.read", o => o.Deadline = TimeSpan.FromSeconds(5));
            services.TryAddSingleton<IProductosHabilitados, ProductosHabilitadosIdentidad>();
            services.TryAddSingleton<IResolutorNombresUsuario, ResolutorNombresUsuario>();
        }
        else
        {
            services.TryAddSingleton<IProductosHabilitados>(new ProductosSinIdentidad(configuration.GetValue(SimularSuscripcionKey, false)));
            services.TryAddSingleton<IResolutorNombresUsuario, ResolutorNombresSinIdentidad>();
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

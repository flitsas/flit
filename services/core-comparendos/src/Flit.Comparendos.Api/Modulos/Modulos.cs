using Flit.Platform.Sdk.Grpc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Comparendos.Api.Modulos;

/// <summary>Registro y mapeo de los módulos del producto. Cada módulo agrega aquí sus servicios y sus endpoints.</summary>
public static class Modulos
{
    public static IServiceCollection AddModulosComparendos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddScoped<Comun.Auditoria>();
        services.AddScoped<Comun.RegistroArchivos>();
        services.AddScoped<Catalogos.MapeadorOt>();
        services.AddScoped<Comparendos.Timeline>();
        services.AddScoped<Comparendos.AccesoComparendo>();
        services.AddScoped<Bandeja.ConsultaBandeja>();
        services.AddScoped<Bandeja.ExportacionBandeja>();

        // Ingesta (HU #13460, #13461). Las fuentes reales se conectan al final (Feature #13464); mientras tanto, simulada.
        services.AddSingleton<Ingesta.Fuentes.IFuenteComparendos, Ingesta.Fuentes.FuenteSimulada>();
        services.AddScoped<Ingesta.Reconciliador>();
        services.AddScoped<Ingesta.CicloDeIngesta>();
        services.AddScoped<Trabajos.IEjecutorTrabajo>(sp => sp.GetRequiredService<Ingesta.CicloDeIngesta>());
        services.AddScoped<Ingesta.IAlertasIntegracion, Ingesta.AlertasIntegracion>();

        // Correo (HU #13484): por Notificaciones si hay dirección; si no, al log (local).
        services.AddScoped<Correo.ServicioCorreo>();
        services.AddSingleton<Correo.DespachadorCorreos>();
        if (configuration["Comparendos:Notificaciones:Address"] is { Length: > 0 } notificaciones)
        {
            services.AddFlitGrpcClient<Flit.Notificaciones.Grpc.V1.NotificacionesService.NotificacionesServiceClient>(
                configuration, new Uri(notificaciones), "platform.notificaciones.send", o => o.Deadline = TimeSpan.FromSeconds(60));
            services.AddScoped<Correo.IEnvioCorreo, Correo.EnvioPorNotificaciones>();
        }
        else
        {
            services.AddScoped<Correo.IEnvioCorreo, Correo.EnvioConsola>();
        }

        services.AddSingleton<Trabajos.ColaTrabajos>();
        if (configuration.GetValue("Comparendos:Trabajos:Habilitados", true))
        {
            services.AddHostedService<Trabajos.ProgramadorTrabajos>();
            services.AddHostedService<Trabajos.ProcesadorTrabajos>();
            services.AddHostedService(sp => sp.GetRequiredService<Correo.DespachadorCorreos>());
            services.AddHostedService<Bandeja.ExportadorEnSegundoPlano>();
        }
        return services;
    }

    public static WebApplication MapModulosComparendos(this WebApplication app, string prefijo)
    {
        var api = app.MapGroup(prefijo);
        api.MapGet("/sesion", Sesion.Obtener).RequireAuthorization(Plataforma.AutorizacionExtensions.PoliticaUsuario);
        Comun.ArchivosEndpoints.Map(api);
        Monitoreo.MonitoreoEndpoints.Map(api);
        Catalogos.CatalogosEndpoints.Map(api);
        Ingesta.IngestaEndpoints.Map(api);
        Correo.CorreoEndpoints.Map(api);
        Ingesta.AlertasYLogEndpoints.Map(api);
        Bandeja.BandejaEndpoints.Map(api);
        Comparendos.DetalleEndpoints.Map(api);
        Configuracion.EmpresasEndpoints.Map(api);
        return app;
    }
}

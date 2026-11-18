using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Radicacion;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Servicios de la caja del organismo (Feature #14453, ADR-0082). Desde que la caja es un módulo de FLIT 2.0 el BFF ya no expone rutas
/// HTTP de caja ni acepta tokens de usuario de caja: core-api (<c>/api/v1/ot/caja</c>) resuelve el organismo y el usuario desde su JWT y
/// llama a <see cref="CajaAdminGrpcService"/> con el token de servicio de <c>svc-tramites</c>. Apagado por defecto (<c>Caja:Habilitado</c>);
/// la ventana de anulación, la radicación diferida y la retención de prefacturas dependen de estas opciones.
/// </summary>
public static class CajaServicios
{
    public static IServiceCollection AddCajaPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var opciones = CajaOptions.Leer(configuration);
        services.TryAddSingleton(Options.Create(opciones));
        services.TryAddScoped<CajaAuditoria>();
        services.TryAddScoped<CajaTopeBusquedas>();
        services.TryAddScoped<CajaBusquedaService>();
        services.TryAddScoped<CajaConfirmacionService>();
        services.TryAddScoped<CajaListaService>();
        services.TryAddScoped<CajaAnulacionService>();
        services.TryAddScoped<RadicacionDePagadasService>();
        services.AddHostedService<RadicacionDePagadasJob>();
        return services;
    }
}

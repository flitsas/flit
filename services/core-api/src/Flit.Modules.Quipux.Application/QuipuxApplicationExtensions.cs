using Flit.Modules.Quipux.Application.UseCases.Cola;
using Flit.Modules.Quipux.Application.UseCases.Configuracion;
using Flit.Modules.Quipux.Application.UseCases.ConsultarEstado;
using Flit.Modules.Quipux.Application.UseCases.ConsultarLog;
using Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;
using Flit.Modules.Quipux.Application.UseCases.RegistrarDocumento;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Modules.Quipux.Application;

/// <summary>
/// Registro de los casos de uso de la integración Quipux. Los handlers se inyectan por TIPO
/// CONCRETO (no hay MediatR en el repo) y con vida <c>Scoped</c>: los workers abren un scope propio
/// por fila procesada, así que un fallo aislado no arrastra al resto del ciclo.
/// </summary>
public static class QuipuxApplicationExtensions
{
    /// <summary>
    /// Registra los tres casos de uso de Quipux.
    /// </summary>
    /// <remarks>
    /// Los PUERTOS que estos handlers consumen NO se registran aquí — sus implementaciones viven en
    /// <c>Flit.Infrastructure</c> y las cablea <c>InfrastructureExtensions</c>:
    /// <list type="bullet">
    ///   <item>Del dominio Quipux: <c>IQuipuxClient</c>, <c>IQuipuxDocumentUploader</c>,
    ///   <c>IQuipuxSettingsRepository</c>, <c>IQuipuxSubmissionRepository</c>, <c>IQuipuxAuditLog</c>.</item>
    ///   <item>Definidos por este módulo: <see cref="IQuipuxConsolidadoMaestroPort"/> (adapta
    ///   <c>GenerarConsolidadoMaestroHandler</c>), <see cref="IQuipuxOrganismoPort"/>
    ///   (<c>transit_offices.external_refs</c>) y <see cref="IQuipuxTenantPort"/>
    ///   (<c>identity.tenants.legal_name</c>).</item>
    ///   <item>De Trámites: <c>IProcedureInstanceRepository</c>, <c>IProcedureTypeRepository</c> y
    ///   <c>ITramiteLifecycleService</c> (ya registrados por el módulo de trámites).</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddQuipuxApplication(this IServiceCollection services)
    {
        // Encola la radicación de un trámite elegible (equivalente de listQuipux + preparación de 1.0).
        services.AddScoped<EncolarEnvioQuipuxHandler>();

        // Radica en Quipux y transiciona preparado → entregado. Absorbe el job
        // validateStatusDocumentForTimeOut de 1.0 vía la consulta previa al reintento.
        services.AddScoped<RegistrarDocumentoQuipuxHandler>();

        // Sondea el estado y reconcilia entregado → aprobado / rechazado.
        services.AddScoped<ConsultarEstadoQuipuxHandler>();

        // Configuración (admin.quipux_settings): lectura redactada y alta/actualización que cifra los
        // secretos vía el repositorio. Los usa la consola SuperAdmin (AdminQuipuxEndpoints).
        services.AddScoped<ObtenerQuipuxSettingsHandler>();
        services.AddScoped<GuardarQuipuxSettingsHandler>();

        // Consola de cola QX (HU #10774): listado por secretaría destino y acciones manuales
        // (re-encolar un fallido, cancelar un pendiente). Los consume AdminTransitOfficesEndpoints.
        services.AddScoped<ListarColaQuipuxHandler>();
        services.AddScoped<ReintentarSubmissionHandler>();
        services.AddScoped<CancelarSubmissionHandler>();

        // LOG QX (HU #10793): consulta de trazabilidad (búsqueda + timeline) para soporte/admin.
        // Solo lectura; lo consume AdminLogQxEndpoints.
        services.AddScoped<ConsultarLogQuipuxHandler>();

        return services;
    }
}

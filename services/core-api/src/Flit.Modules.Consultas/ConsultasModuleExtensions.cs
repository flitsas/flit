using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Consultations.Avaluos;
using Flit.Infrastructure.Improntas;
using Flit.Infrastructure.KyverumRunt;
using Flit.Infrastructure.Rues;
using Flit.Infrastructure.RuntConfirmation;
using Flit.Modules.Improntas.Domain;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flit.Modules.Consultas;

/// <summary>
/// Registro de los proveedores de consultas y avalúos (HU #13342; se movió tal cual de
/// <c>Flit.Infrastructure.InfrastructureExtensions</c>, en el mismo orden). Quien lo aloja registra además los puentes
/// que leen su base: <see cref="IConsultationTenantOverrideProvider"/>, <see cref="IAvaluoProviderPolicy"/> e
/// <see cref="IAvaluoMockValueSource"/>.
/// </summary>
public static class ConsultasModuleExtensions
{
    public static void AddConsultationProviders(this IServiceCollection services, IConfiguration configuration)
    {
        // Convención del repo: config primero (appsettings.json en local; claves
        // `Verifik__`/`Consultations__` en el .env de docker vía IConfiguration), con
        // fallback a las env vars crudas VERIFIK_*/INTEMPO_* (compat con .env.verifik).
        string? Cfg(string key, string env) =>
            configuration[key] ?? Environment.GetEnvironmentVariable(env);

        // Modos real|mock por proveedor.
        services.Configure<ConsultationProviderModeOptions>(o =>
        {
            o.VerifikVehicleMode = Cfg("Consultations:VerifikVehicleMode", "VERIFIK_VEHICLE_MODE") ?? "real";
            o.VerifikSimitMode = Cfg("Consultations:VerifikSimitMode", "VERIFIK_SIMIT_MODE") ?? "mock";
            o.VerifikRnmcMode = Cfg("Consultations:VerifikRnmcMode", "VERIFIK_RNMC_MODE") ?? "mock";
            o.VerifikConductorMode = Cfg("Consultations:VerifikConductorMode", "VERIFIK_CONDUCTOR_MODE") ?? "mock";
            o.VerifikRuesMode = Cfg("Consultations:VerifikRuesMode", "VERIFIK_RUES_MODE") ?? "mock";
            o.IntempoMode = Cfg("Consultations:IntempoMode", "INTEMPO_MODE") ?? "mock";
            o.FasecoldaMode = Cfg("Consultations:FasecoldaMode", "FASECOLDA_MODE") ?? "mock";
            // FEATURE 05 — comparendos. Ambos en mock por defecto: ver ConsultationProviderModeOptions.
            o.FlitFinesMode = Cfg("Consultations:FlitFinesMode", "FLIT_FINES_MODE") ?? "mock";
            o.KyverumFinesMode = Cfg("Consultations:KyverumFinesMode", "KYVERUM_FINES_MODE") ?? "mock";
        });

        // Config Verifik. Clave de config `Verifik:BearerToken` (alineada con el
        // docker-compose), fallback a la env cruda VERIFIK_API_TOKEN.
        services.Configure<VerifikOptions>(o =>
        {
            o.BaseUrl = Cfg("Verifik:BaseUrl", "VERIFIK_BASE_URL") ?? "https://api.verifik.co";
            o.ApiToken = Cfg("Verifik:BearerToken", "VERIFIK_API_TOKEN") ?? "";
            o.AuthScheme = Cfg("Verifik:AuthScheme", "VERIFIK_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("Verifik:TimeoutSeconds", "VERIFIK_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        // Config INTEMPO.
        services.Configure<IntempoOptions>(o =>
        {
            o.BaseUrl = Cfg("Intempo:BaseUrl", "INTEMPO_BASE_URL") ?? "https://www.moviliza.com.co";
            o.TimeoutSeconds = int.TryParse(Cfg("Intempo:TimeoutSeconds", "INTEMPO_TIMEOUT_SECONDS"), out var t) ? t : 15;
        });

        // FEATURE 05 — API de registro de FLIT (fuente interna de comparendos). Sin credenciales.
        services.Configure<FlitRegistrationApiOptions>(o =>
        {
            o.BaseUrl = Cfg("RegistrationApi:BaseUrl", "REGISTRATION_API_BASE_URL")
                        ?? "https://knli4dcix0.execute-api.us-east-1.amazonaws.com/pdn";
            o.InfractionPath = Cfg("RegistrationApi:InfractionPath", "REGISTRATION_API_INFRACTION_PATH")
                        ?? "api/v1/registration/simit";
            o.TimeoutSeconds = int.TryParse(Cfg("RegistrationApi:TimeoutSeconds", "REGISTRATION_API_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        // FEATURE 05 — KYVERUM comparendos (persona jurídica). URL/ruta provisionales; en mock
        // hasta que el proveedor entregue especificación y credenciales.
        services.Configure<KyverumFinesOptions>(o =>
        {
            o.BaseUrl = Cfg("KyverumFines:BaseUrl", "KYVERUM_FINES_BASE_URL") ?? "https://runt.kyverum.com";
            o.InfractionPath = Cfg("KyverumFines:InfractionPath", "KYVERUM_FINES_INFRACTION_PATH") ?? "/v1/comparendos:consultar";
            o.ApiKey = Cfg("KyverumFines:ApiKey", "KYVERUM_FINES_API_KEY") ?? "";
            o.AuthScheme = Cfg("KyverumFines:AuthScheme", "KYVERUM_FINES_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("KyverumFines:TimeoutSeconds", "KYVERUM_FINES_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        // Typed HttpClients (compatibles con PublishAot).
        services.AddHttpClient<VerifikConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikSimitConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikRnmcConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikConductorConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikRuesConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<IntempoConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<IntempoOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // FEATURE 05 — fuente interna de comparendos. NormalizedBaseUrl conserva la barra final:
        // el BaseUrl trae el stage del API Gateway (/pdn) y sin ella la ruta relativa lo descarta.
        services.AddHttpClient<FlitFinesConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<FlitRegistrationApiOptions>>().Value;
            c.BaseAddress = new Uri(o.NormalizedBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // FEATURE 05 — KYVERUM comparendos (persona jurídica). Config propia, no la del RUNT.
        services.AddHttpClient<KyverumFinesConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<KyverumFinesOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Kyverum RUNT (HU #10478): cliente de consultas compartido, mismo config que improntas
        // (ImprontaRuntOptions / KYVERUM_RUNT_*, configurado en AddImprontas). Los providers
        // kyverum_runt / kyverum_runt_conductor lo consumen; convergen al mismo ConsultationResult
        // que Verifik para ser intercambiables en la cadena de proveedores (Fase 3).
        services.AddHttpClient<KyverumRuntApiClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<ImprontaRuntOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Proveedores expuestos como IConsultationProvider para el registry.
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikSimitConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikRnmcConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConductorConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikRuesConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<IntempoConsultationProvider>());
        services.AddTransient<IConsultationProvider, KyverumRuntVehicleConsultationProvider>();
        services.AddTransient<IConsultationProvider, KyverumRuntConductorConsultationProvider>();
        // FEATURE 05 — comparendos por fuente. Quedan registrados pero SIN TRÁFICO hasta HU10758,
        // que es la que cablea fines_query_source al preflight y empieza a resolverlos.
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<FlitFinesConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<KyverumFinesConsultationProvider>());
        services.AddSingleton<IConsultationProvider, FlitIntegrationsGatewayProvider>();
        services.AddScoped<IConsultationProviderRegistry, ConsultationProviderRegistry>();

        // Cadena de proveedores Kyverum-first con fallback a Verifik (HU #10478, Fase 3). Defaults en
        // appsettings (sección Consultations:DefaultChains / FailoverTimeoutMs); si faltan, el propio
        // ConsultationChainOptions embebe el orden del plan. Aún no lo consumen los handlers (Fase 5).
        services.Configure<ConsultationChainOptions>(o =>
            configuration.GetSection(ConsultationChainOptions.SectionName).Bind(o));
        services.AddScoped<IConsultationProviderChainResolver>(sp =>
            new ConsultationProviderChainResolver(
                sp.GetRequiredService<IConsultationProviderRegistry>(),
                sp.GetRequiredService<IOptions<ConsultationChainOptions>>().Value));

        // Avalúo comercial multi-proveedor (Feature #10707, ADR-0029): capa aparte de la de
        // consultas (verificación) — agrega VALOR de varias fuentes en paralelo.
        AddAvaluoProviders(services, configuration);
    }

    /// <summary>
    /// HU #13348 (Epic #13316): los clientes de los documentos que generan los proveedores (impronta de Kyverum RUNT,
    /// certificado RUES) y el RUNT crudo de la Confirmación RUNT. Se movieron tal cual de
    /// <c>Flit.Infrastructure.InfrastructureExtensions</c>; los registra core-consultas después de
    /// <see cref="AddConsultationProviders"/> y <see cref="ConfigureKyverumRunt"/> (usan sus opciones).
    /// </summary>
    public static void AddClientesDeDocumentos(this IServiceCollection services, IConfiguration configuration)
    {
        // HU #12309 — consumidor propio del RUNT según providerKey (sin la cadena de proveedores del wizard).
        services.AddHttpClient<VerifikRuntRawHttpClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.AddScoped<IRuntVehicleRawClient, RuntVehicleRawClient>();

        // HU #10465 — Kyverum RUNT (improntas:generar), con las opciones de ConfigureKyverumRunt.
        services.AddHttpClient<IImprontaExternalClient, ImprontaRuntClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<ImprontaRuntOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        AddRues(services, configuration);
    }

    private static void AddRues(IServiceCollection services, IConfiguration configuration)
    {
        // RF36 — autogeneración del Certificado RUES. Opt-in: solo se registra el cliente HTTP cuando
        // Rues:Enabled=true y hay BaseUrl; sin él, Consultas responde «no habilitado» y Trámites cae al
        // respaldo de carga manual. Env var CRUDA primero (override 12-factor), fallback a configuration.
        // La API key NUNCA se loguea.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        var enabled = string.Equals(Cfg("Rues:Enabled", "RUES_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
        var baseUrl = Cfg("Rues:BaseUrl", "RUES_BASE_URL");
        if (!enabled || string.IsNullOrWhiteSpace(baseUrl))
            return;

        services.Configure<RuesOptions>(o =>
        {
            o.Enabled = true;
            o.BaseUrl = baseUrl;
            o.ApiKey = Cfg("Rues:ApiKey", "RUES_API_KEY") ?? "";
            o.AuthScheme = Cfg("Rues:AuthScheme", "RUES_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("Rues:TimeoutSeconds", "RUES_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        services.AddHttpClient<IRuesExternalClient, RuesApiClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<RuesOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
    }

    private static void AddAvaluoProviders(IServiceCollection services, IConfiguration configuration)
    {
        string? Cfg(string key, string env) =>
            configuration[key] ?? Environment.GetEnvironmentVariable(env);

        services.Configure<FasecoldaOptions>(o =>
        {
            o.ByVinBaseUrl = Cfg("Fasecolda:ByVinBaseUrl", "FASECOLDA_BY_VIN_API_BASE_URL") ?? o.ByVinBaseUrl;
            o.ByVinPath = Cfg("Fasecolda:ByVinPath", "FASECOLDA_BY_VIN_API_PATH") ?? o.ByVinPath;
            o.ApiBaseUrl = Cfg("Fasecolda:ApiBaseUrl", "FASECOLDA_API_BASE_URL") ?? o.ApiBaseUrl;
            o.AuthPath = Cfg("Fasecolda:AuthPath", "FASECOLDA_AUTH_API_PATH") ?? o.AuthPath;
            o.ListCodePath = Cfg("Fasecolda:ListCodePath", "FASECOLDA_LIST_CODE_API_PATH") ?? o.ListCodePath;
            o.GrantType = Cfg("Fasecolda:GrantType", "FASECOLDA_API_GRANT_TYPE") ?? o.GrantType;
            o.Username = Cfg("Fasecolda:Username", "FASECOLDA_API_USERNAME") ?? "";
            o.Password = Cfg("Fasecolda:Password", "FASECOLDA_API_PASSWORD") ?? "";
            o.TimeoutSeconds = int.TryParse(Cfg("Fasecolda:TimeoutSeconds", "FASECOLDA_API_SECONDS_TIMEOUT"), out var t) ? t : o.TimeoutSeconds;
        });

        // Dos hosts (búsqueda por VIN sin auth; guía de valores con token). Clientes con nombre.
        services.AddHttpClient("fasecolda-vin", (sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<FasecoldaOptions>>().Value;
            c.BaseAddress = new Uri(o.ByVinBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.AddHttpClient("fasecolda-api", (sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<FasecoldaOptions>>().Value;
            c.BaseAddress = new Uri(o.ApiBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddSingleton<FasecoldaTokenCache>();
        services.AddScoped<IAvaluoProvider, FasecoldaAvaluoProvider>();
        // Fase 1: mock, activables por configuración a real sin tocar el handler (ADR-0029).
        services.AddScoped<IAvaluoProvider, BaseGravableAvaluoProvider>();
        services.AddScoped<IAvaluoProvider, MercadoLibreAvaluoProvider>();
        services.AddScoped<IAvaluoProviderRegistry, AvaluoProviderRegistry>();
    }

    /// <summary>
    /// Opciones de Kyverum RUNT (<c>ImprontaRunt:*</c> / <c>KYVERUM_RUNT_*</c>), compartidas por las consultas RUNT y las
    /// improntas de core-api. Se movió tal cual de <c>AddImprontas</c> (HU #13342).
    /// </summary>
    public static void ConfigureKyverumRunt(IServiceCollection services, IConfiguration configuration)
    {
        // HU #10465 — Kyverum RUNT (improntas:generar). Mismo orden de precedencia que Kyverum Verify
        // (AddIdentityValidation): env var CRUDA primero (override de deploy 12-factor), fallback a
        // configuration (appsettings/user-secrets/`ImprontaRunt__*`). runt.kyverum.com es un dominio
        // DISTINTO de verify.kyverum.com (mismo proveedor, otro producto/scope). La API key NUNCA se
        // loguea.
        string? Cfg(string key, string env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : configuration[key];
        }

        services.Configure<ImprontaRuntOptions>(o =>
        {
            o.BaseUrl = Cfg("ImprontaRunt:BaseUrl", "KYVERUM_RUNT_BASE_URL") ?? "https://runt.kyverum.com";
            o.ApiKey = Cfg("ImprontaRunt:ApiKey", "KYVERUM_RUNT_API_KEY") ?? "";
            o.AuthScheme = Cfg("ImprontaRunt:AuthScheme", "KYVERUM_RUNT_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Cfg("ImprontaRunt:TimeoutSeconds", "KYVERUM_RUNT_TIMEOUT_SECONDS"), out var t)
                ? t : 30;
        });
    }
}

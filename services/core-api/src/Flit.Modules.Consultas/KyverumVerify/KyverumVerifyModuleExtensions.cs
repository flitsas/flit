using Flit.Infrastructure.Kyverum;
using Flit.Tramites.Application.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flit.Modules.Consultas.KyverumVerify;

/// <summary>
/// HU #13351 (Epic #13316, ADR-0065 §6-7) — clientes de Kyverum Verify (crear validación, estado y certificado). Los
/// registra core-api mientras atiende Kyverum en proceso y core-consultas cuando lo atiende por Trámites.
/// </summary>
public static class KyverumVerifyModuleExtensions
{
    public static IServiceCollection AddKyverumVerifyClients(this IServiceCollection services, Action<KyverumOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddHttpClient<IKyverumVerifyClient, KyverumVerifyClient>(ConfigureHttp);
        // Descarga del certificado de la validación (PDF) desde la API pública de Kyverum
        // (GET /v1/validations/{id}/certificado). Reusa el MISMO Bearer API key que el create — sin cookie
        // ni login admin (el panel /admin/api exige MFA y no aplica para integración server-to-server).
        services.AddHttpClient<IKyverumCertificateClient, KyverumCertificateClient>(ConfigureHttp);
        return services;
    }

    private static void ConfigureHttp(IServiceProvider sp, HttpClient c)
    {
        var o = sp.GetRequiredService<IOptions<KyverumOptions>>().Value;
        c.BaseAddress = new Uri(o.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
    }
}

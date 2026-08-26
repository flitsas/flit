using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// HU #11630 — contrato HTTP de <c>/api/v1/analytics/ict-reports/*</c>: autenticación,
/// autorización (jobs = SuperAdmin-only, aislamiento por tenant) y binding de la paginación.
///
/// <para><b>Qué NO cubre y por qué.</b> El host de pruebas se levanta SIN PostgreSQL accesible
/// (<see cref="TestEnvironment"/> desactiva la migración automática), igual que el resto de
/// <c>*AuthorizationTests</c> de este ensamblado. Todo lo que se verifica aquí ocurre ANTES de
/// tocar la base: el middleware de autenticación, el chequeo de rol, la resolución de tenant y el
/// binder de <c>page</c>/<c>pageSize</c>. Lo que dependa del RESULTADO de la consulta —que
/// <c>page=0</c> se normalice a 1, que <c>pageSize=99999</c> se acote a 200, que <c>total</c> sea
/// el universo y no el largo de la página— no es observable sin datos reales; queda en el smoke
/// manual documentado en la HU. Por eso las aserciones de paginación de abajo son de la forma
/// "NO es 400": comprueban que la petición pasa el borde y llega al handler, no el valor final.</para>
/// </summary>
public sealed class IctReportsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
    private static readonly Guid OtherTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string Rango = "from=2026-08-01&to=2026-08-19";

    private const string NovedadesUrl = $"/api/v1/analytics/ict-reports/novedades?{Rango}";
    private const string AtascadosUrl = "/api/v1/analytics/ict-reports/atascados";
    private const string JobsUrl = $"/api/v1/analytics/ict-reports/jobs?{Rango}";
    private const string WebhooksUrl = $"/api/v1/analytics/ict-reports/webhooks?{Rango}";

    private const string NovedadesExportUrl = $"/api/v1/analytics/ict-reports/novedades/export?{Rango}";
    private const string AtascadosExportUrl = "/api/v1/analytics/ict-reports/atascados/export";
    private const string JobsExportUrl = $"/api/v1/analytics/ict-reports/jobs/export?{Rango}";
    private const string WebhooksExportUrl = $"/api/v1/analytics/ict-reports/webhooks/export?{Rango}";

    private readonly WebApplicationFactory<Program> _factory;

    public IctReportsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Autenticación ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(NovedadesUrl)]
    [InlineData(AtascadosUrl)]
    [InlineData(JobsUrl)]
    [InlineData(WebhooksUrl)]
    [InlineData(NovedadesExportUrl)]
    [InlineData(AtascadosExportUrl)]
    [InlineData(JobsExportUrl)]
    [InlineData(WebhooksExportUrl)]
    public async Task SinToken_LosOchoEndpointsDevuelven401(string url)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Caso 16 de la spec: /jobs y /jobs/export son SuperAdmin-only ──────────────────────────

    [Theory]
    [InlineData(JobsUrl, "AdminCompany")]
    [InlineData(JobsUrl, "ot_admin")]
    [InlineData(JobsUrl, "OperadorCustom")]
    [InlineData(JobsExportUrl, "AdminCompany")]
    [InlineData(JobsExportUrl, "ot_admin")]
    [InlineData(JobsExportUrl, "OperadorCustom")]
    public async Task Jobs_ConUsuarioNoSuperAdmin_Devuelve403(string url, string rol)
    {
        // ict.job_runs es una tabla GLOBAL de plataforma, sin tenant_id: exponerla a un
        // administrador de compañía filtraría el rendimiento del pipeline de TODOS los tenants.
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, rol));

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(JobsUrl)]
    [InlineData(JobsExportUrl)]
    public async Task Jobs_ConSuperAdmin_PasaElBordeDeAutorizacion(string url)
    {
        var client = ClientWith(TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        // Sin base no puede completar; lo que se fija aquí es que el rol NO lo rechaza.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ── Caso 17 (mitad de autorización): un tenant no puede pedir los datos de otro ───────────

    [Theory]
    [InlineData("/api/v1/analytics/ict-reports/novedades")]
    [InlineData("/api/v1/analytics/ict-reports/atascados")]
    [InlineData("/api/v1/analytics/ict-reports/webhooks")]
    [InlineData("/api/v1/analytics/ict-reports/novedades/export")]
    [InlineData("/api/v1/analytics/ict-reports/atascados/export")]
    [InlineData("/api/v1/analytics/ict-reports/webhooks/export")]
    public async Task ConTenantIdDeOtraCompania_Devuelve403(string ruta)
    {
        // El aislamiento a nivel de DATOS (RLS + filtro tenant_id en el SQL) necesita Postgres real:
        // aquí solo se fija que el borde rechace pedir explícitamente otro tenant.
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            $"{ruta}?{Rango}&tenantId={OtherTenantId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/v1/analytics/ict-reports/novedades")]
    [InlineData("/api/v1/analytics/ict-reports/atascados")]
    [InlineData("/api/v1/analytics/ict-reports/webhooks")]
    public async Task ConTenantIdPropio_PasaElBordeDeAutorizacion(string ruta)
    {
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            $"{ruta}?{Rango}&tenantId={TenantId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    // ── Caso 15 de la spec: page/pageSize no numéricos los rechaza el binder ──────────────────

    [Theory]
    [InlineData("page=abc")]
    [InlineData("pageSize=abc")]
    [InlineData("page=1.5")]
    [InlineData("pageSize=50,100")]
    [InlineData("page=99999999999999999999")]
    public async Task Novedades_ConPaginacionNoNumerica_Devuelve400(string query)
    {
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            $"/api/v1/analytics/ict-reports/novedades?{Rango}&{query}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Novedades_SinRango_Devuelve400()
    {
        // 'from'/'to' son obligatorios (parámetros de ruta no anulables): sin ellos el binder corta.
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            "/api/v1/analytics/ict-reports/novedades", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Casos 12 y 13 (mitad de borde): valores absurdos se normalizan, NO se rechazan ────────

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-5")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("pageSize=99999")]
    [InlineData("page=0&pageSize=0")]
    public async Task Novedades_ConPaginacionFueraDeRango_NoDevuelve400(string query)
    {
        // El endpoint NORMALIZA (Math.Max/Math.Clamp) en vez de devolver 400: pedir la página 0 o un
        // tamaño de 99999 es un error del cliente que la API absorbe. Que el valor efectivo sea
        // page=1 / pageSize=200 se ve en la respuesta, que aquí no se puede leer (sin base).
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            $"/api/v1/analytics/ict-reports/novedades?{Rango}&{query}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    // ── Rango invertido: los 6 handlers con rango devuelven 400 (hallazgo H3 de la 1.ª ronda) ──

    /// <summary>
    /// <c>ict-reports</c> era el único grupo de <c>/api/v1/analytics/*</c> que no validaba
    /// <c>from &gt; to</c>: devolvía 200 con <c>total: 0</c> y calculaba un <c>PreviousRange</c> de
    /// longitud negativa. Ahora corta con el mismo 400 que el resto de analítica. Los dos endpoints
    /// de atascados no aparecen aquí porque no reciben rango (son "ahora mismo").
    /// </summary>
    [Theory]
    [InlineData("/api/v1/analytics/ict-reports/novedades")]
    [InlineData("/api/v1/analytics/ict-reports/webhooks")]
    [InlineData("/api/v1/analytics/ict-reports/novedades/export")]
    [InlineData("/api/v1/analytics/ict-reports/webhooks/export")]
    public async Task ConRangoInvertido_Devuelve400(string ruta)
    {
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            $"{ruta}?from=2026-08-19&to=2026-08-01", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/analytics/ict-reports/jobs")]
    [InlineData("/api/v1/analytics/ict-reports/jobs/export")]
    public async Task Jobs_ConRangoInvertidoYSuperAdmin_Devuelve400(string ruta)
    {
        // En /jobs el rango se valida DESPUÉS del rol: sin SuperAdmin sigue ganando el 403.
        var client = ClientWith(TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(
            $"{ruta}?from=2026-08-19&to=2026-08-01", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/analytics/ict-reports/jobs")]
    [InlineData("/api/v1/analytics/ict-reports/jobs/export")]
    public async Task Jobs_ConRangoInvertidoYSinSuperAdmin_ElRolGanaYDevuelve403(string ruta)
    {
        // Precedencia deliberada: un rango inválido NO debe convertirse en un oráculo que le
        // confirme a un no-autorizado que el endpoint existe y qué valida.
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            $"{ruta}?from=2026-08-19&to=2026-08-01", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConRangoDeUnSoloDia_NoEsRangoInvalido()
    {
        // from == to es un rango legítimo (un día), no invertido: el corte es estrictamente '>'.
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            "/api/v1/analytics/ict-reports/novedades?from=2026-08-19&to=2026-08-19",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    // ── Caso 4 (mitad de contrato): los /export no exponen paginación ─────────────────────────

    [Fact]
    public async Task Export_ConPageYPageSize_LosIgnoraSinFallar()
    {
        // Los cuatro /export entregan el documento completo hasta MaxRows: no declaran page/pageSize
        // y un cliente que los mande igual no debe romper la descarga (query sobrante = ignorado).
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(
            $"{NovedadesExportUrl}&page=2&pageSize=25", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}

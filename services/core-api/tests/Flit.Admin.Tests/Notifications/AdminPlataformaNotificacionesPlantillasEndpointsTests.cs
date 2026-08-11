using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Tests.Companies;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Notifications;

/// <summary>
/// HU #11356 (Feature #11347, última HU) — grupo de rutas SuperAdmin de catálogo + render de
/// muestra por id. Cubre los 6 AC contra <c>WebApplicationFactory&lt;Program&gt;</c>, mismo
/// patrón que <see cref="Flit.Admin.Tests.Companies.Whitelist.TransferStartEndpointTests"/>
/// (dobles inyectados vía <c>ConfigureTestServices</c>).
/// </summary>
/// <remarks>
/// AC5/AC6 no se prueban confiando en que "hoy no se llama a nada": se sustituyen
/// <see cref="IEmailSender"/>, <see cref="ITemporaryPasswordGenerator"/>,
/// <see cref="IPasswordHasher"/> e <see cref="IAdminAuditWriter"/> por dobles de NSubstitute que
/// SIGUEN cableados en el contenedor de DI de la app (los usa <c>AdminResetPasswordHandler</c> y
/// otros handlers reales) y se afirma <c>DidNotReceiveWithAnyArgs()</c> tras invocar el render de
/// muestra. Si alguien en el futuro cablea por error el endpoint de muestra para llamar a
/// cualquiera de estos puertos, la prueba falla.
/// </remarks>
public sealed class AdminPlataformaNotificacionesPlantillasEndpointsTests
    : IClassFixture<AdminPlataformaNotificacionesPlantillasEndpointsTests.SideEffectFreeFactory>
{
    private const string GroupUrl = "/api/v1/admin/plataforma/notificaciones/plantillas";

    private readonly SideEffectFreeFactory _factory;

    public AdminPlataformaNotificacionesPlantillasEndpointsTests(SideEffectFreeFactory factory)
    {
        _factory = factory;
        _factory.EmailSender.ClearReceivedCalls();
        _factory.PasswordGenerator.ClearReceivedCalls();
        _factory.PasswordHasher.ClearReceivedCalls();
        _factory.AuditWriter.ClearReceivedCalls();
    }

    // ── Autorización — mismo patrón que el resto de Plataforma ─────────────

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient()
            .GetAsync(GroupUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithNonSuperAdminRole_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(GroupUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── AC1 — listado del catálogo ──────────────────────────────────────────

    [Fact]
    public async Task AC1_List_Returns200With8TemplatesIdModuleAndTriggers()
    {
        var client = SuperAdminClient();

        var response = await client.GetAsync(GroupUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(8);
        body.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        body.Items.Should().Contain(i =>
            i.Id == "security.invitation"
            && i.Module == "Security"
            && i.Triggers.Contains("CreateInvitation")
            && i.Triggers.Contains("ResendInvitation"));
        body.Items.Should().Contain(i =>
            i.Id == "security.welcome-registration"
            && i.Module == "Security"
            && i.Triggers.Contains("WelcomeRegistration"));
        body.Items.Should().Contain(i =>
            i.Id == "analytics.alert" && i.Module == "Analytics" && i.Triggers.Contains("Alert"));
        body.Items.Should().Contain(i =>
            i.Id == "tramites.aprobado"
            && i.Module == "Tramites"
            && i.Triggers.Contains("ProcedureStatusChanged"));
        body.Items.Should().Contain(i =>
            i.Id == "tramites.rechazado"
            && i.Module == "Tramites"
            && i.Triggers.Contains("ProcedureStatusChanged"));
    }

    [Theory]
    [InlineData("tramites.aprobado", "FLIT_SMTP", "APROBADO")]
    [InlineData("tramites.aprobado", "TENANT_API", "¡Buenas Noticias!")]
    [InlineData("tramites.rechazado", "FLIT_SMTP", "RECHAZADO")]
    [InlineData("tramites.rechazado", "TENANT_API", "¡Es un gusto saludarte!")]
    public async Task GetSample_TramitesAprobadoYRechazado_RespectsChannelVariant(
        string templateId, string channel, string expectedMarker)
    {
        var client = SuperAdminClient();

        var response = await client.GetAsync(
            $"{GroupUrl}/{templateId}/muestra?channel={channel}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SampleDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        System.Net.WebUtility.HtmlDecode(body!.Html).Should().Contain(expectedMarker);
        body.Html.Should().Contain("<img");
        if (channel == "FLIT_SMTP")
            body.Html.Should().Contain("tramite-cambio-estado-header.png");
        else
            body.Html.Should().Contain("tramite-cambio-estado-renting-header.png");
    }

    // ── AC2 — render de muestra por id ──────────────────────────────────────

    [Theory]
    [InlineData("security.invitation", "[ACÁ VA EL NOMBRE DEL DESTINATARIO]")]
    [InlineData("security.forgot-password", "[ACÁ VA EL NOMBRE DEL DESTINATARIO]")]
    [InlineData("security.admin-reset-password", "[ACÁ VA LA CONTRASEÑA TEMPORAL]")]
    [InlineData("analytics.scheduled-report", "[ACÁ VA EL NOMBRE DEL INFORME PROGRAMADO]")]
    [InlineData("analytics.alert", "[ACÁ VA EL NOMBRE DE LA REGLA DE ALERTA]")]
    public async Task AC2_GetSample_WithValidId_Returns200WithSubjectAndVisibleMarker(
        string templateId, string expectedMarker)
    {
        var client = SuperAdminClient();

        var response = await client.GetAsync($"{GroupUrl}/{templateId}/muestra", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SampleDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.TemplateId.Should().Be(templateId);
        body.Subject.Should().NotBeNullOrWhiteSpace();
        // El composer HTML-encodea tildes/eñes (p. ej. "Á" → "&#193;"); se decodifica antes de
        // buscar el marcador visible, igual que lo vería un humano renderizando el correo.
        System.Net.WebUtility.HtmlDecode(body.Html).Should().Contain(expectedMarker);
    }

    // ── AC3 — la ruta rechaza datos reales, sin revelar existencia ─────────

    [Theory]
    [InlineData("tramiteId")]
    [InlineData("usuarioId")]
    public async Task AC3_GetSample_WithRealIdentifier_Returns400_SameBodyRegardlessOfExistence(string queryParam)
    {
        var client = SuperAdminClient();

        var withExistingLookingId = await client.GetAsync(
            $"{GroupUrl}/security.invitation/muestra?{queryParam}=11111111-1111-1111-1111-111111111111",
            TestContext.Current.CancellationToken);
        var withNonExistingLookingId = await client.GetAsync(
            $"{GroupUrl}/security.invitation/muestra?{queryParam}=99999999-9999-9999-9999-999999999999",
            TestContext.Current.CancellationToken);

        withExistingLookingId.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withNonExistingLookingId.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bodyExisting = await withExistingLookingId.Content
            .ReadAsStringAsync(TestContext.Current.CancellationToken);
        var bodyNonExisting = await withNonExistingLookingId.Content
            .ReadAsStringAsync(TestContext.Current.CancellationToken);

        bodyExisting.Should().Be(bodyNonExisting,
            "el 400 no debe variar según si el identificador colado existiría o no (AC3)");

        _factory.PasswordGenerator.DidNotReceiveWithAnyArgs().Generate();
        _ = _factory.EmailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC3_GetSample_WithRealIdentifier_Returns400_SameBodyRegardlessOfTemplateIdValidity()
    {
        var client = SuperAdminClient();

        // Mismo parámetro colado (tramiteId) con un templateId VÁLIDO y otro INVENTADO: si la
        // validación del id real ocurriera después de resolver el catálogo, estas dos respuestas
        // divergirían (400 vs 404) y eso ya sería un canal lateral. Deben ser IDÉNTICAS.
        var withValidTemplate = await client.GetAsync(
            $"{GroupUrl}/security.invitation/muestra?tramiteId=abc", TestContext.Current.CancellationToken);
        var withInvalidTemplate = await client.GetAsync(
            $"{GroupUrl}/plantilla-que-no-existe/muestra?tramiteId=abc", TestContext.Current.CancellationToken);

        withValidTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withInvalidTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bodyValid = await withValidTemplate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var bodyInvalid = await withInvalidTemplate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        bodyValid.Should().Be(bodyInvalid);
    }

    // ── AC4 — id inexistente ─────────────────────────────────────────────────

    [Fact]
    public async Task AC4_GetSample_WithUnknownId_Returns404()
    {
        var client = SuperAdminClient();

        var response = await client.GetAsync(
            $"{GroupUrl}/plantilla-que-no-existe/muestra", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── AC5 — reset administrativo no deja rastro ───────────────────────────

    [Fact]
    public async Task AC5_GetSample_AdminResetPassword_Returns200_NeverGeneratesPersistsOrLogsPassword()
    {
        var client = SuperAdminClient();

        var response = await client.GetAsync(
            $"{GroupUrl}/security.admin-reset-password/muestra", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Ni se generó, ni se hasheó, ni se escribió auditoría ni se envió correo.
        _factory.PasswordGenerator.DidNotReceiveWithAnyArgs().Generate();
        _factory.PasswordHasher.DidNotReceiveWithAnyArgs().Hash(default!);
        _ = _factory.AuditWriter.DidNotReceiveWithAnyArgs().WriteAsync(default!, TestContext.Current.CancellationToken);
        _ = _factory.EmailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, TestContext.Current.CancellationToken);
    }

    // ── AC6 — sin efectos observables en ninguna plantilla ──────────────────

    [Theory]
    [InlineData("security.invitation")]
    [InlineData("security.forgot-password")]
    [InlineData("security.admin-reset-password")]
    [InlineData("analytics.scheduled-report")]
    [InlineData("analytics.alert")]
    public async Task AC6_GetSample_AnyTemplate_Returns200_NeverSendsEmailNorWritesAudit(string templateId)
    {
        var client = SuperAdminClient();

        var response = await client.GetAsync($"{GroupUrl}/{templateId}/muestra", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _ = _factory.EmailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, TestContext.Current.CancellationToken);
        _ = _factory.AuditWriter.DidNotReceiveWithAnyArgs().WriteAsync(default!, TestContext.Current.CancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
    }

    private sealed record ListItemDto(string Id, string Name, string Module, List<string> Triggers);

    private sealed record ListDto(List<ListItemDto> Items);

    private sealed record SampleDto(string TemplateId, string Subject, string Html);

    /// <summary>
    /// Factory que sustituye los cuatro puertos con efectos observables (envío de correo,
    /// generación/hash de contraseña, auditoría) por dobles de NSubstitute, sin tocar
    /// PostgreSQL — esta HU no toca <c>Persistence/</c>.
    /// </summary>
    public sealed class SideEffectFreeFactory : WebApplicationFactory<Program>
    {
        public IEmailSender EmailSender { get; } = Substitute.For<IEmailSender>();
        public ITemporaryPasswordGenerator PasswordGenerator { get; } = Substitute.For<ITemporaryPasswordGenerator>();
        public IPasswordHasher PasswordHasher { get; } = Substitute.For<IPasswordHasher>();
        public IAdminAuditWriter AuditWriter { get; } = Substitute.For<IAdminAuditWriter>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => EmailSender);
                services.AddScoped(_ => PasswordGenerator);
                services.AddScoped(_ => PasswordHasher);
                services.AddScoped(_ => AuditWriter);
            });
        }
    }
}

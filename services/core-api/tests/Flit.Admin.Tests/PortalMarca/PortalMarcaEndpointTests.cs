using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using NSubstitute.ClearExtensions;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.OtClientProcedures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// Feature #14374 (Épica #14297) — contrato HTTP de <c>/api/v1/admin/ot/portal-marca</c>: rol <c>ot_admin</c> o SuperAdmin, el
/// <c>ot_admin</c> solo con su organismo (otro = 403 y nada cambia), SuperAdmin con <c>?transitOfficeId=</c>, errores
/// <c>{ code, message }</c> con el motivo exacto. Host real sin PostgreSQL: repositorio, almacenamiento y auditoría en memoria.
/// </summary>
public sealed partial class PortalMarcaEndpointTests : IClassFixture<PortalMarcaEndpointTests.MarcaFactory>
{
    private const string Ruta = "/api/v1/admin/ot/portal-marca";
    private static readonly Guid TenantA = Guid.Parse("0e000000-0000-4000-8000-00000001437a");
    private static readonly Guid TenantB = Guid.Parse("0e000000-0000-4000-8000-00000001437b");
    private static readonly Guid TenantSuper = Guid.Parse("5a000000-0000-4000-8000-000000014374");
    private static readonly Guid OrganismoA = Guid.Parse("07000000-0000-4000-8000-00000001437a");
    private static readonly Guid OrganismoB = Guid.Parse("07000000-0000-4000-8000-00000001437b");
    private static readonly Guid Usuario = Guid.Parse("14374000-0000-4000-8000-000000000001");

    private readonly MarcaFactory _factory;

    public PortalMarcaEndpointTests(MarcaFactory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC4 — alcance ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_OtAdminDelOrganismoAEntraSinParametroYVeSuPropiaMarca()
    {
        var r = await Cliente(OtAdmin(TenantA)).GetAsync(Ruta, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Raiz(r)).GetProperty("transitOfficeId").GetGuid().Should().Be(OrganismoA);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("POST", "/publish")]
    [InlineData("POST", "/retire")]
    public Task AC4_OtAdminDelOrganismoAQueNombraAlOrganismoB_403YNoCambiaNada(string metodo, string sub) => Verificar403Async(metodo, sub);

    private async Task Verificar403Async(string metodo, string sub)
    {
        var antes = await _factory.Repo.ObtenerAsync(OrganismoB, Ct);
        var cliente = Cliente(OtAdmin(TenantA));
        using var peticion = new HttpRequestMessage(new HttpMethod(metodo), $"{Ruta}{sub}?transitOfficeId={OrganismoB}");
        if (metodo == "PUT")
            peticion.Content = Json(new { platformName = "Intruso", colors = new { primary = "#0B3D91", secondary = "#0F5132", onPrimary = "#FFFFFF" }, rowVersion = 0 });
        else if (sub is "/publish" or "/retire")
            peticion.Content = Json(new { rowVersion = 0 });
        else if (sub is "/logo" or "/banner")
            peticion.Content = Multipart("logo.png", ImagenesDePrueba.Png());

        var r = await cliente.SendAsync(peticion, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Prohibido);
        (await _factory.Repo.ObtenerAsync(OrganismoB, Ct)).Should().Be(antes, "la marca del otro organismo no cambió");
        _factory.Auditoria.Entradas.Should().BeEmpty();
        _factory.Almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_UnOtAdminSinOrganismoEnSuPerfilRecibe403ConElMotivo()
    {
        var r = await Cliente(OtAdmin(Guid.NewGuid())).GetAsync(Ruta, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Raiz(r)).GetProperty("message").GetString().Should().Contain("organismo");
    }

    [Theory]
    [InlineData("ot_operador")]
    [InlineData("AdminCompany")]
    [InlineData("Radicador")]
    public async Task AC4_QuienNoEsSuperAdminNiOtAdminRecibe403(string rol)
    {
        var r = await Cliente(Token(TenantA, rol)).GetAsync(Ruta, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC4_SinSesionResponde401()
    {
        (await _factory.CreateClient().GetAsync(Ruta, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── AC5 — SuperAdmin ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_SuperAdminSinTransitOfficeIdRecibe400ConElMotivo()
    {
        var r = await Cliente(SuperAdmin()).GetAsync(Ruta, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.OrganismoRequerido);
    }

    [Fact]
    public async Task AC5_SuperAdminConUnOrganismoInexistenteRecibe404()
    {
        var r = await Cliente(SuperAdmin()).GetAsync($"{Ruta}?transitOfficeId={Guid.NewGuid()}", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.OrganismoNoExiste);
    }

    [Fact]
    public async Task AC5_SuperAdminGuardaYPublicaLaMarcaDeUnOrganismoYLaAuditoriaRegistraQuien()
    {
        var cliente = Cliente(SuperAdmin());
        var g = await cliente.PutAsync($"{Ruta}?transitOfficeId={OrganismoB}", Json(ColoresValidos(0)), Ct);
        g.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = (await Raiz(g)).GetProperty("rowVersion").GetInt64();

        var p = await cliente.PostAsync($"{Ruta}/publish?transitOfficeId={OrganismoB}", Json(new { rowVersion = version }), Ct);

        p.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await Raiz(p);
        cuerpo.GetProperty("publishedVersion").GetInt32().Should().Be(1);
        cuerpo.GetProperty("published").GetProperty("platformName").GetString().Should().Be("Portal de prueba");
        cuerpo.GetProperty("publishedBy").GetProperty("userId").GetGuid().Should().Be(Usuario);
        cuerpo.GetProperty("hasUnpublishedChanges").GetBoolean().Should().BeFalse();
        cuerpo.GetProperty("completeness").GetProperty("isComplete").GetBoolean().Should().BeTrue();
        var publicacion = _factory.Auditoria.Entradas.Should().ContainSingle(e => e.EntityName == "portal_marca_publicada").Subject;
        publicacion.ActorUserId.Should().Be(Usuario);
        publicacion.TargetEntityId.Should().Be(OrganismoB);
        (await _factory.Repo.ObtenerAsync(OrganismoA, Ct)).PublishedVersion.Should().Be(0, "el otro organismo no cambió");
    }

    // ── AC1/AC2 — contrato de publicar y errores ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ElOtAdminGuardaPublicaYRetira_ConLaFormaDelContrato()
    {
        var cliente = Cliente(OtAdmin(TenantA));
        var inicial = await Raiz(await cliente.GetAsync(Ruta, Ct));
        inicial.GetProperty("published").ValueKind.Should().Be(JsonValueKind.Null);
        inicial.GetProperty("publishedAt").ValueKind.Should().Be(JsonValueKind.Null);
        inicial.GetProperty("logoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        inicial.GetProperty("completeness").GetProperty("missing").GetArrayLength().Should().Be(4);

        var g = await Raiz(await cliente.PutAsync(Ruta, Json(ColoresValidos(0)), Ct));
        var p = await Raiz(await cliente.PostAsync($"{Ruta}/publish", Json(new { rowVersion = g.GetProperty("rowVersion").GetInt64() }), Ct));
        p.GetProperty("draft").GetProperty("colors").GetProperty("onPrimary").GetString().Should().Be("#FFFFFF");
        p.GetProperty("publishedVersion").GetInt32().Should().Be(1);

        var r = await cliente.PostAsync($"{Ruta}/retire", Json(new { rowVersion = p.GetProperty("rowVersion").GetInt64() }), Ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var retirada = await Raiz(r);
        retirada.GetProperty("published").ValueKind.Should().Be(JsonValueKind.Null);
        retirada.GetProperty("draft").GetProperty("platformName").GetString().Should().Be("Portal de prueba");
    }

    [Fact]
    public async Task AC2_UnContrasteInsuficienteResponde422ConCodigoYMotivoExacto()
    {
        var cliente = Cliente(OtAdmin(TenantA));
        var g = await Raiz(await cliente.PutAsync(Ruta, Json(new
        {
            platformName = "Portal de prueba",
            colors = new { primary = "#FFEB3B", secondary = "#0F5132", onPrimary = "#FFFFFF" },
            rowVersion = 0,
        }), Ct));

        var r = await cliente.PostAsync($"{Ruta}/publish", Json(new { rowVersion = g.GetProperty("rowVersion").GetInt64() }), Ct);

        r.StatusCode.Should().Be((HttpStatusCode)422);
        var cuerpo = await Raiz(r);
        cuerpo.GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Contraste);
        cuerpo.GetProperty("message").GetString().Should().Contain("4,5:1");
        (await _factory.Repo.ObtenerAsync(OrganismoA, Ct)).Published.Should().BeNull();
    }

    [Fact]
    public async Task PublicarUnBorradorIncompletoResponde422Incompleta()
    {
        var r = await Cliente(OtAdmin(TenantA)).PostAsync($"{Ruta}/publish", Json(new { rowVersion = 0 }), Ct);

        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Incompleta);
    }

    [Fact]
    public async Task GuardarConUnaVersionVencidaResponde409Conflicto()
    {
        var cliente = Cliente(OtAdmin(TenantA));
        await cliente.PutAsync(Ruta, Json(ColoresValidos(0)), Ct);

        var r = await cliente.PutAsync(Ruta, Json(ColoresValidos(0)), Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Conflicto);
    }

    [Fact]
    public async Task GuardarSinCuerpoOConDatosInvalidosResponde400()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var mal = await cliente.PutAsync(Ruta, Json(new { platformName = "<script>", rowVersion = 0 }), Ct);

        mal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Raiz(mal)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Datos);
    }

    // ── HU #14376 — imágenes por HTTP ────────────────────────────────────────────────────────────────────────────────

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> SembrarImagenAsync(Guid organismo)
    {
        var guardada = await _factory.Almacen.GuardarAsync(organismo, "image/png", new MemoryStream(ImagenesDePrueba.Png()), Ct);
        var (imagen, _) = await _factory.Repo.AgregarImagenAsync(organismo, PortalMarcaImagen.KindLogo, guardada, Usuario, Ct);
        return imagen.Id;
    }

    private static object ColoresValidos(long rowVersion) => new
    {
        platformName = "Portal de prueba",
        colors = new { primary = "#0B3D91", secondary = "#0F5132", onPrimary = "#FFFFFF" },
        rowVersion,
    };

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("flit-tests/14374");
        return client;
    }

    private static MultipartFormDataContent Multipart(string nombre, byte[] contenido)
    {
        var form = new MultipartFormDataContent();
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(parte, "file", nombre);
        return form;
    }

    private static StringContent Json(object cuerpo) => new(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> Raiz(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string OtAdmin(Guid tenant) => Token(tenant, "ot_admin", "TRANSIT_OFFICE");

    private static string SuperAdmin() => Token(TenantSuper, "SuperAdmin");

    private static string Token(Guid tenantId, string role, string? entityType = null)
    {
        var claims = new List<Claim>
        {
            new("sub", Usuario.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        if (entityType is not null)
            claims.Add(new Claim("entity_type", entityType));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host con el repositorio de marca, el almacenamiento, la auditoría y la bandeja OT (organismo del tenant) en memoria.</summary>
    public sealed class MarcaFactory : WebApplicationFactory<Program>
    {

        public RepositorioMarcaEnMemoria Repo { get; private set; } = new();
        public AlmacenImagenEnMemoria Almacen { get; private set; } = new();
        public AuditoriaEnMemoria Auditoria { get; private set; } = new();
        public IOtClientProcedureRepository Bandeja { get; } = Substitute.For<IOtClientProcedureRepository>();
        public IPortalDominioRepository Dominios { get; } = Substitute.For<IPortalDominioRepository>();

        public MarcaFactory() => Reiniciar();

        public void Reiniciar()
        {
            // Mismos objetos para el host: se vacían en lugar de reemplazarse.
            Repo = Copiar(Repo, new RepositorioMarcaEnMemoria());
            Almacen.Archivos.Clear();
            Auditoria.Entradas.Clear();
            Repo.Organismos.Add(OrganismoA);
            Repo.Organismos.Add(OrganismoB);
            Bandeja.ClearSubstitute();
            Dominios.ClearSubstitute();
            Bandeja.ResolveTransitOfficeIdAsync(TenantA, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(OrganismoA);
            Bandeja.ResolveTransitOfficeIdAsync(TenantB, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(OrganismoB);
        }

        private static RepositorioMarcaEnMemoria Copiar(RepositorioMarcaEnMemoria _, RepositorioMarcaEnMemoria nuevo) => nuevo;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPortalMarcaRepository>();
                services.AddScoped<IPortalMarcaRepository>(_ => new RepositorioProxy(() => Repo));
                services.RemoveAll<IPortalMarcaImagenStorage>();
                services.AddScoped<IPortalMarcaImagenStorage>(_ => Almacen);
                services.RemoveAll<IAdminAuditWriter>();
                services.AddScoped<IAdminAuditWriter>(_ => Auditoria);
                services.RemoveAll<IOtClientProcedureRepository>();
                services.AddScoped(_ => Bandeja);
                services.RemoveAll<IPortalDominioRepository>();
                services.AddScoped(_ => Dominios);

            });
        }
    }

    /// <summary>Delega siempre en el repositorio vigente de la fábrica (que se reinicia entre pruebas).</summary>
    private sealed class RepositorioProxy(Func<RepositorioMarcaEnMemoria> actual) : IPortalMarcaRepository
    {
        private RepositorioMarcaEnMemoria R => actual();

        public Task<bool> ExisteOrganismoAsync(Guid id, CancellationToken ct = default) => R.ExisteOrganismoAsync(id, ct);
        public Task<PortalMarcaRegistro> ObtenerAsync(Guid id, CancellationToken ct = default) => R.ObtenerAsync(id, ct);
        public Task<PortalMarcaRegistro?> GuardarBorradorAsync(Guid id, string? n, Flit.Admin.Domain.Companies.Branding.BrandColors? c, long v, Guid a, CancellationToken ct = default) =>
            R.GuardarBorradorAsync(id, n, c, v, a, ct);
        public Task<PortalMarcaRegistro?> PublicarAsync(Guid id, long v, Guid a, CancellationToken ct = default) => R.PublicarAsync(id, v, a, ct);
        public Task<PortalMarcaRegistro?> RetirarAsync(Guid id, long v, Guid a, CancellationToken ct = default) => R.RetirarAsync(id, v, a, ct);
        public Task<(PortalMarcaImagen Imagen, PortalMarcaRegistro Marca)> AgregarImagenAsync(Guid id, string k, NuevaPortalMarcaImagen i, Guid a, CancellationToken ct = default) =>
            R.AgregarImagenAsync(id, k, i, a, ct);
        public Task<PortalMarcaRegistro> QuitarImagenAsync(Guid id, string k, Guid a, CancellationToken ct = default) => R.QuitarImagenAsync(id, k, a, ct);
        public Task<(PortalMarcaImagen? Imagen, PortalMarcaRegistro? Marca)> AgregarCarruselAsync(Guid id, NuevaPortalMarcaImagen i, string alt, string? link, int max, Guid a, CancellationToken ct = default) =>
            R.AgregarCarruselAsync(id, i, alt, link, max, a, ct);
        public Task<PortalMarcaRegistro?> ActualizarCarruselAsync(Guid id, Guid img, string alt, string? link, Guid a, CancellationToken ct = default) =>
            R.ActualizarCarruselAsync(id, img, alt, link, a, ct);
        public Task<PortalMarcaRegistro?> OrdenarCarruselAsync(Guid id, IReadOnlyList<Guid> ids, Guid a, CancellationToken ct = default) =>
            R.OrdenarCarruselAsync(id, ids, a, ct);
        public Task<PortalMarcaRegistro?> QuitarCarruselAsync(Guid id, Guid img, Guid a, CancellationToken ct = default) => R.QuitarCarruselAsync(id, img, a, ct);
        public Task<(PortalMarcaImagen? Imagen, PortalMarcaRegistro? Marca)> AgregarBannerAsync(Guid id, NuevaPortalMarcaImagen i, string alt, string? link, int max, Guid a, CancellationToken ct = default) =>
            R.AgregarBannerAsync(id, i, alt, link, max, a, ct);
        public Task<PortalMarcaRegistro?> ActualizarBannerAsync(Guid id, Guid img, string alt, string? link, Guid a, CancellationToken ct = default) =>
            R.ActualizarBannerAsync(id, img, alt, link, a, ct);
        public Task<PortalMarcaRegistro?> OrdenarBannersAsync(Guid id, IReadOnlyList<Guid> ids, Guid a, CancellationToken ct = default) =>
            R.OrdenarBannersAsync(id, ids, a, ct);
        public Task<PortalMarcaRegistro?> QuitarBannerAsync(Guid id, Guid img, Guid a, CancellationToken ct = default) => R.QuitarBannerAsync(id, img, a, ct);
        public Task<PortalMarcaImagen?> ObtenerImagenAsync(Guid id, CancellationToken ct = default) => R.ObtenerImagenAsync(id, ct);
        public Task<bool> ImagenEstaPublicadaAsync(Guid id, CancellationToken ct = default) => R.ImagenEstaPublicadaAsync(id, ct);
        public Task<Guid?> ResolverOrganismoPorCodigoAsync(string codigo, CancellationToken ct = default) => R.ResolverOrganismoPorCodigoAsync(codigo, ct);
    }
}

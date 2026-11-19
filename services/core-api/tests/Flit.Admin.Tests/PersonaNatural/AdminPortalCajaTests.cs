using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.PersonaNatural;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Security.Application.Products;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14454, #14455 y #14461 (Feature #14453, Épica #14210, ADR-0082) contra la base real: «Usuarios de caja» (rol fijo
/// <c>ot_caja</c>, alcance por organismo), el guardián de asignación genérica, la resolución del usuario de caja que usa el portal y
/// «Cobros de caja» (fachada hacia el portal, con el cliente gRPC reemplazado por un doble). Requiere la base con el DDL 199.
/// </summary>
public sealed class AdminPortalCajaTests : IClassFixture<AdminPortalCajaTests.Anfitrion>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly WebApplicationFactory<Program> _off;
    private readonly WebApplicationFactory<Program> _on;
    private readonly FakeCajaAdminClient _portal;

    private readonly Guid _officeA = Guid.NewGuid();
    private readonly Guid _officeB = Guid.NewGuid();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _adminA = Guid.NewGuid();
    private readonly Guid _adminB = Guid.NewGuid();
    private readonly Guid _cajaA = Guid.NewGuid();
    private readonly Guid _cajaB = Guid.NewGuid();
    private readonly Guid _superAdmin = Guid.NewGuid();
    private Guid _roleCaja;
    private Guid _roleAdmin;

    public AdminPortalCajaTests(Anfitrion anfitrion)
    {
        _off = anfitrion.Base;
        _on = anfitrion.Factory;
        // El doble del portal vive en el host compartido: cada prueba lo recibe limpio (las de una clase corren en serie).
        _portal = anfitrion.Portal;
        _portal.Reiniciar();
        SeedAsync().GetAwaiter().GetResult();
    }

    /// <summary>Un host para toda la clase (<see cref="HostPorClase"/>): antes cada prueba arrancaba el suyo con
    /// <c>WithWebHostBuilder</c> (26 pruebas, ~7 min en CI, la clase más lenta de Flit.Admin.Tests). Cada prueba siembra sus
    /// organismos, empresas y usuarios con identificadores propios y los borra en <see cref="Dispose"/>, así que comparten host
    /// sin cruzarse. <see cref="HostPorClase.Base"/> es el host con la función apagada.</summary>
    public sealed class Anfitrion : HostPorClase
    {
        internal FakeCajaAdminClient Portal { get; } = new();

        protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> factory) =>
            factory.WithWebHostBuilder(b =>
            {
                b.UseSetting("PortalCaja:Habilitado", "true");
                b.UseSetting("PersonaNatural:Portal:Address", "http://localhost:9");
                b.ConfigureTestServices(s =>
                {
                    s.RemoveAll<ICajaAdminClient>();
                    s.AddSingleton<ICajaAdminClient>(Portal);
                });
            });
    }

    // ── Función apagada ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConLaFuncionApagada_LasRutasDeCajaResponden404()
    {
        using var client = ClienteDe(_off, "ot_admin", _tenantA, _adminA);

        (await client.GetAsync("/api/v1/admin/ot/portal-caja/users", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/admin/ot/portal-caja/cobros", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── #14455 backend: usuarios de caja ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OtAdmin_ListaSoloLosUsuariosDeCajaDeSuOrganismo()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        var response = await client.GetAsync("/api/v1/admin/ot/portal-caja/users", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var filas = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.EnumerateArray().ToList();
        filas.Select(f => f.GetProperty("userId").GetString()).Should().Equal(_cajaA.ToString());
        filas[0].GetProperty("status").GetString().Should().Be("active");
    }

    [Fact]
    public async Task OtAdmin_ConElOrganismoDeOtro_403SinEjecutarNada()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        var response = await client.GetAsync($"/api/v1/admin/ot/portal-caja/users?transitOfficeId={_officeB}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("OTRO_ORGANISMO");
    }

    [Fact]
    public async Task SuperAdmin_ExigeElOrganismo_YConEstePuedeVerCualquiera()
    {
        using var client = ClienteDe(_on, "SuperAdmin", Guid.NewGuid(), _superAdmin);

        (await client.GetAsync("/api/v1/admin/ot/portal-caja/users", Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var a = await client.GetAsync($"/api/v1/admin/ot/portal-caja/users?transitOfficeId={_officeA}", Ct);
        var b = await client.GetAsync($"/api/v1/admin/ot/portal-caja/users?transitOfficeId={_officeB}", Ct);
        a.StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.Content.ReadAsStringAsync(Ct)).Should().Contain(_cajaA.ToString()).And.NotContain(_cajaB.ToString());
        (await b.Content.ReadAsStringAsync(Ct)).Should().Contain(_cajaB.ToString()).And.NotContain(_cajaA.ToString());
    }

    [Fact]
    public async Task Invitar_CreaLaInvitacionConElRolDeCaja_EnciendeElProducto_YElCorreoRepetidoDa409()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        var correo = $"caja-nueva-{Guid.NewGuid():N}@ot.test";

        var creada = await client.PostAsJsonAsync("/api/v1/admin/ot/portal-caja/users", new { email = correo, fullName = "Cajera Nueva" }, Ct);

        creada.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var db = NewDb(_on);
        var invitacion = await db.UserInvitations.AsNoTracking().SingleAsync(i => i.Email == correo, Ct);
        invitacion.TenantId.Should().Be(_tenantA);
        invitacion.RoleId.Should().Be(_roleCaja);
        (await db.Set<Flit.Infrastructure.Persistence.Entities.Platform.TenantProductEntity>().AsNoTracking()
            .AnyAsync(p => p.TenantId == _tenantA && p.ProductCode == "caja" && p.Enabled, Ct)).Should().BeTrue();

        var repetida = await client.PostAsJsonAsync("/api/v1/admin/ot/portal-caja/users", new { email = correo, fullName = "Otra vez" }, Ct);
        repetida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await repetida.Content.ReadAsStringAsync(Ct)).Should().Contain("EMAIL_ALREADY_IN_USE");

        var lista = await client.GetStringAsync("/api/v1/admin/ot/portal-caja/users", Ct);
        lista.Should().Contain(correo).And.Contain("invited");
    }

    [Fact]
    public async Task Invitar_SinCorreoOSinNombre_400()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        (await client.PostAsJsonAsync("/api/v1/admin/ot/portal-caja/users", new { email = "", fullName = "X" }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/v1/admin/ot/portal-caja/users", new { email = "a@b.co", fullName = " " }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Desactivar_QuitaElAccesoAlSiguienteRequest_YReactivarLoDevuelve()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        await EncenderProductoAsync(_tenantA);
        (await Resolver(_tenantA, _cajaA)).Activo.Should().BeTrue();

        (await client.PostAsync($"/api/v1/admin/ot/portal-caja/users/{_cajaA}/suspend", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Resolver(_tenantA, _cajaA)).Activo.Should().BeFalse("la caja consulta el estado en cada petición, sin caché");
        (await client.GetStringAsync("/api/v1/admin/ot/portal-caja/users", Ct)).Should().Contain("suspended");

        (await client.DeleteAsync($"/api/v1/admin/ot/portal-caja/users/{_cajaA}/suspend", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Resolver(_tenantA, _cajaA)).Activo.Should().BeTrue();
    }

    [Fact]
    public async Task Desactivar_AUnUsuarioQueNoEsDeCajaODeOtroOrganismo_404()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        (await client.PostAsync($"/api/v1/admin/ot/portal-caja/users/{_adminA}/suspend", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync($"/api/v1/admin/ot/portal-caja/users/{_cajaB}/suspend", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reenviar_UnaInvitacionAjenaOQueNoEsDeCaja_404()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        (await client.PostAsync($"/api/v1/admin/ot/portal-caja/invitations/{Guid.NewGuid()}/resend", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ElUsuarioDeCaja_NoUsaLasRutasDeAdministracion()
    {
        using var client = ClienteDe(_on, "ot_caja", _tenantA, _cajaA);

        (await client.GetAsync("/api/v1/admin/ot/portal-caja/users", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/v1/admin/ot/portal-caja/cobros/{Guid.NewGuid()}/anular", new { reason = "Motivo suficiente de anulación", rowVersion = 3 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/admin/ot/users", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LaInvitacionGenericaDeUsuariosOt_NoAsignaElRolDeCaja()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite", new { email = $"cualquiera-{Guid.NewGuid():N}@ot.test", fullName = "X", roleIds = new[] { _roleCaja } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("CAJA_ROLE_EXCLUSIVE");
    }

    [Fact]
    public async Task UnOtAdmin_SigueInvitandoConSusRolesDeSiempre()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite", new { email = $"colab-{Guid.NewGuid():N}@ot.test", fullName = "Colaborador", roleIds = new[] { _roleAdmin } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── #14454: ResolverUsuarioCaja ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolver_DevuelveElOrganismoDelUsuario_SoloSiTodoEstaVigente()
    {
        await EncenderProductoAsync(_tenantA);

        var r = await Resolver(_tenantA, _cajaA);

        r.Activo.Should().BeTrue();
        r.OrganismoId.Should().Be(_officeA);
        r.UsuarioNombre.Should().Be("Caja A");
        r.OrganismoCodigo.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Resolver_SinElProductoCajaEncendido_NoEstaActivo()
    {
        await ApagarProductoAsync(_tenantA);

        (await Resolver(_tenantA, _cajaA)).Activo.Should().BeFalse();
    }

    [Fact]
    public async Task Resolver_ConOtraEmpresaOUnUsuarioSinElRol_NoEstaActivo()
    {
        await EncenderProductoAsync(_tenantA);
        await EncenderProductoAsync(_tenantB);

        (await Resolver(_tenantB, _cajaA)).Activo.Should().BeFalse("el usuario no es de esa empresa");
        (await Resolver(_tenantA, _adminA)).Activo.Should().BeFalse("ot_admin no es usuario de caja");
        (await Resolver(_tenantA, Guid.NewGuid())).Activo.Should().BeFalse();
        (await Resolver(Guid.Empty, _cajaA)).Activo.Should().BeFalse();
    }

    [Fact]
    public async Task Resolver_UnUsuarioInactivo_NoEstaActivo()
    {
        await EncenderProductoAsync(_tenantA);
        await using (var db = NewDb(_on))
        {
            await db.Users.Where(u => u.Id == _cajaA).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "inactive"), Ct);
        }

        (await Resolver(_tenantA, _cajaA)).Activo.Should().BeFalse();
    }

    // ── #14461 hub: cobros ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarCobros_UsaElOrganismoDelTokenDelAdministrador()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        _portal.Lista = new CajaCobrosLista([Cobro(Guid.NewGuid())], 1, 85000);

        var response = await client.GetAsync("/api/v1/admin/ot/portal-caja/cobros?date=2026-11-10&page=2&pageSize=30", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _portal.UltimoOrganismo.Should().Be(_officeA);
        _portal.UltimaFecha.Should().Be("2026-11-10");
        _portal.UltimaPagina.Should().Be((2, 30));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        body.GetProperty("totalDiaCop").GetInt64().Should().Be(85000);
        body.GetProperty("items")[0].GetProperty("anulable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ListarCobros_OtroOrganismo403_FechaMala400_SuperAdminSinOrganismo400()
    {
        using var admin = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        using var superAdmin = ClienteDe(_on, "SuperAdmin", Guid.NewGuid(), _superAdmin);

        (await admin.GetAsync($"/api/v1/admin/ot/portal-caja/cobros?transitOfficeId={_officeB}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/v1/admin/ot/portal-caja/cobros?date=10-11-2026", Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await superAdmin.GetAsync("/api/v1/admin/ot/portal-caja/cobros", Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await superAdmin.GetAsync($"/api/v1/admin/ot/portal-caja/cobros?transitOfficeId={_officeB}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        _portal.UltimoOrganismo.Should().Be(_officeB);
    }

    [Fact]
    public async Task ListarCobros_ConElPortalCaido_503()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        _portal.Caido = true;

        var response = await client.GetAsync("/api/v1/admin/ot/portal-caja/cobros", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("PORTAL_NO_DISPONIBLE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Anular_SinMotivo_400_YNoLlamaAlPortal(string? motivo)
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        var response = await client.PostAsJsonAsync($"/api/v1/admin/ot/portal-caja/cobros/{Guid.NewGuid()}/anular", new { reason = motivo, rowVersion = 1 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("REASON_REQUIRED");
        _portal.Anulaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Anular_ConUnMotivoMuyCortoOMuyLargo_400()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        var id = Guid.NewGuid();

        (await client.PostAsJsonAsync($"/api/v1/admin/ot/portal-caja/cobros/{id}/anular", new { reason = "corto", rowVersion = 3 }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/v1/admin/ot/portal-caja/cobros/{id}/anular", new { reason = new string('x', 301), rowVersion = 3 }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _portal.Anulaciones.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Anular_SinLaVersionDelCobro_400RowVersionRequired_SinLlamarAlPortal(long? rowVersion)
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/ot/portal-caja/cobros/{Guid.NewGuid()}/anular", new { reason = "Cobrado por error en la caja", rowVersion }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("ROW_VERSION_REQUIRED");
        _portal.Anulaciones.Should().BeEmpty("sin versión no hay control de concurrencia");
    }

    [Fact]
    public async Task ListaGenericaDeRolesAsignables_NoOfreceElRolDeCaja()
    {
        using var client = ClienteDe(_on, "AdminCompany", _tenantA, _adminA);

        var response = await client.GetAsync("/api/v1/security/roles", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var codigos = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.EnumerateArray()
            .Select(r => r.GetProperty("code").GetString()).ToList();
        codigos.Should().NotContain("ot_caja", "el rol de caja solo se reparte desde «Usuarios de caja»");
        codigos.Should().Contain("ot_admin", "la lista sigue ofreciendo los demás roles del organismo");
    }

    [Fact]
    public async Task Anular_ConMotivo_LlamaAlPortalConElOrganismoYElActorDelToken()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        var cobro = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/ot/portal-caja/cobros/{cobro}/anular", new { reason = "Cobrado por error en la caja", rowVersion = 7 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var llamada = _portal.Anulaciones.Should().ContainSingle().Subject;
        llamada.Organismo.Should().Be(_officeA);
        llamada.Cobro.Should().Be(cobro);
        llamada.Motivo.Should().Be("Cobrado por error en la caja");
        llamada.Actor.Should().Be(_adminA);
        llamada.ActorNombre.Should().Be("Admin A");
        llamada.RowVersion.Should().Be(7);
    }

    [Fact]
    public async Task Anular_UnCobroQueYaLlegoAFlit2_409_YUnoAjeno404()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);
        _portal.Resultado = new CajaAnulacionResultado(CajaAnulacionResultado.NoAnulable, string.Empty);
        var noAnulable = await client.PostAsJsonAsync($"/api/v1/admin/ot/portal-caja/cobros/{Guid.NewGuid()}/anular", new { reason = "Motivo suficiente de anulación", rowVersion = 3 }, Ct);
        noAnulable.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await noAnulable.Content.ReadAsStringAsync(Ct)).Should().Contain("COBRO_NO_ANULABLE");

        _portal.Resultado = new CajaAnulacionResultado(CajaAnulacionResultado.NoEncontrado, string.Empty);
        (await client.PostAsJsonAsync($"/api/v1/admin/ot/portal-caja/cobros/{Guid.NewGuid()}/anular", new { reason = "Motivo suficiente de anulación", rowVersion = 3 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anular_OtroOrganismo403_YPortalCaido503()
    {
        using var client = ClienteDe(_on, "ot_admin", _tenantA, _adminA);

        var ajeno = await client.PostAsJsonAsync(
            $"/api/v1/admin/ot/portal-caja/cobros/{Guid.NewGuid()}/anular?transitOfficeId={_officeB}", new { reason = "Motivo suficiente de anulación", rowVersion = 3 }, Ct);
        ajeno.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _portal.Anulaciones.Should().BeEmpty();

        _portal.Caido = true;
        (await client.PostAsJsonAsync($"/api/v1/admin/ot/portal-caja/cobros/{Guid.NewGuid()}/anular", new { reason = "Motivo suficiente de anulación", rowVersion = 3 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ── Ayudas ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static CajaCobroResumen Cobro(Guid id) =>
        new(id.ToString(), "PN-2026-000123", 85000, "efectivo", string.Empty, "2026-11-10T14:00:00Z", "Caja A", "pagado", true, string.Empty, string.Empty, string.Empty, string.Empty, 3);

    private async Task<UsuarioCajaResuelto> Resolver(Guid tenantId, Guid userId)
    {
        using var scope = _on.Services.CreateScope();
        return await CajaUsuarios.ResolverAsync(
            scope.ServiceProvider.GetRequiredService<FlitDbContext>(), scope.ServiceProvider.GetRequiredService<IProductAccessResolver>(),
            tenantId, userId, DateTimeOffset.UtcNow, Ct);
    }

    private async Task EncenderProductoAsync(Guid tenantId)
    {
        using var scope = _on.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SetTenantProductEnabledHandler>()
            .HandleAsync(new SetTenantProductEnabledCommand(tenantId, "caja", true, null), null, Ct);
    }

    private async Task ApagarProductoAsync(Guid tenantId)
    {
        using var scope = _on.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SetTenantProductEnabledHandler>()
            .HandleAsync(new SetTenantProductEnabledCommand(tenantId, "caja", false, null), null, Ct);
    }

    private static FlitDbContext NewDb(WebApplicationFactory<Program> factory) => factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static HttpClient ClienteDe(WebApplicationFactory<Program> factory, string role, Guid tenantId, Guid userId)
    {
        var client = factory.CreateClient();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("entity_type", "TRANSIT_OFFICE"),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedAsync()
    {
        await using var db = NewDb(_on);
        foreach (var (office, tenant) in new[] { (_officeA, _tenantA), (_officeB, _tenantB) })
        {
            db.TransitOffices.Add(new TransitOffice
            {
                Id = office, Code = $"C{Guid.NewGuid():N}"[..10], Name = "Organismo de prueba de caja", DepartmentCode = "99", CityCode = "99999", IsActive = true,
            });
            db.Tenants.Add(new Tenant
            {
                Id = tenant, Code = $"OT-CJ-{Guid.NewGuid():N}"[..20], LegalName = "OT caja", TaxId = TestNit.Unique(), TenantType = "RENTING", IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        foreach (var (id, name, tenant) in new[] { (_adminA, "Admin A", _tenantA), (_adminB, "Admin B", _tenantB), (_cajaA, "Caja A", _tenantA), (_cajaB, "Caja B", _tenantB) })
        {
            db.Users.Add(new User
            {
                Id = id, Email = $"{id:N}@ot.test", DisplayName = name, Status = "active", HomeTenantId = tenant, CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(Ct);

        _roleCaja = await db.Roles.AsNoTracking().Where(r => r.Code == "ot_caja" && r.TargetEntityType == "TRANSIT_OFFICE" && r.DeletedAt == null).Select(r => r.Id).SingleAsync(Ct);
        // "ot_admin" es del catalogo global; en una base limpia (CI) no lo siembra nadie antes de la prueba.
        _roleAdmin = await db.Roles.AsNoTracking().Where(r => r.Code == "ot_admin" && r.TargetEntityType == "TRANSIT_OFFICE" && r.DeletedAt == null)
            .Select(r => r.Id).SingleOrDefaultAsync(Ct);
        if (_roleAdmin == Guid.Empty)
        {
            _roleAdmin = Guid.NewGuid();
            db.Roles.Add(new Role { Id = _roleAdmin, Code = "ot_admin", Name = "Administrador OT", TargetEntityType = "TRANSIT_OFFICE", IsSystem = true, IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }


        db.TransitOfficeProfiles.Add(new TransitOfficeProfile { Id = Guid.NewGuid(), TenantId = _tenantA, TransitOfficeId = _officeA, OperationMode = "dashboard", CreatedAt = DateTimeOffset.UtcNow });
        db.TransitOfficeProfiles.Add(new TransitOfficeProfile { Id = Guid.NewGuid(), TenantId = _tenantB, TransitOfficeId = _officeB, OperationMode = "dashboard", CreatedAt = DateTimeOffset.UtcNow });
        foreach (var (user, tenant, role) in new[] { (_adminA, _tenantA, _roleAdmin), (_adminB, _tenantB, _roleAdmin), (_cajaA, _tenantA, _roleCaja), (_cajaB, _tenantB, _roleCaja) })
        {
            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.NewGuid(), TenantId = tenant, UserId = user, RoleId = role, AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(Ct);
    }

    public void Dispose()
    {
        using var db = NewDb(_on);
        var tenants = new[] { _tenantA, _tenantB };
        db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => tenants.Contains(a.TenantId)));
        db.UserTempSuspensions.RemoveRange(db.UserTempSuspensions.Where(s => tenants.Contains(s.TenantId)));
        db.UserInvitations.RemoveRange(db.UserInvitations.Where(i => tenants.Contains(i.TenantId)));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => tenants.Contains(p.TenantId)));
        db.Set<Flit.Infrastructure.Persistence.Entities.Platform.TenantProductEntity>().RemoveRange(
            db.Set<Flit.Infrastructure.Persistence.Entities.Platform.TenantProductEntity>().Where(p => tenants.Contains(p.TenantId)));
        db.SaveChanges();
        db.Users.RemoveRange(db.Users.Where(u => u.HomeTenantId != null && tenants.Contains(u.HomeTenantId.Value)));
        db.Tenants.RemoveRange(db.Tenants.Where(t => tenants.Contains(t.Id)));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _officeA || o.Id == _officeB));
        db.SaveChanges();
    }

    internal sealed record AnulacionLlamada(Guid Organismo, Guid Cobro, string Motivo, Guid Actor, string ActorNombre, long RowVersion);

    internal sealed class FakeCajaAdminClient : ICajaAdminClient
    {
        /// <summary>Vuelve al estado inicial: el doble se comparte entre las pruebas de la clase.</summary>
        public void Reiniciar()
        {
            Lista = new([], 0, 0);
            Resultado = new(null, "anulado");
            Caido = false;
            UltimoOrganismo = null;
            UltimaFecha = null;
            UltimaPagina = default;
            Anulaciones.Clear();
        }

        public CajaCobrosLista Lista { get; set; } = new([], 0, 0);

        public CajaAnulacionResultado Resultado { get; set; } = new(null, "anulado");

        public bool Caido { get; set; }

        public Guid? UltimoOrganismo { get; private set; }

        public string? UltimaFecha { get; private set; }

        public (int, int) UltimaPagina { get; private set; }

        public List<AnulacionLlamada> Anulaciones { get; } = [];

        public Task<CajaCobrosLista> ListarAsync(Guid organismoId, string? fecha, int pagina, int tamano, CancellationToken ct)
        {
            if (Caido)
                throw new CajaPortalNoDisponibleException(new InvalidOperationException("caído"));
            UltimoOrganismo = organismoId;
            UltimaFecha = fecha;
            UltimaPagina = (pagina, tamano);
            return Task.FromResult(Lista);
        }

        public Task<CajaAnulacionResultado> AnularAsync(Guid organismoId, Guid cobroId, string motivo, Guid actorUserId, string actorNombre, long rowVersion, CancellationToken ct)
        {
            if (Caido)
                throw new CajaPortalNoDisponibleException(new InvalidOperationException("caído"));
            Anulaciones.Add(new AnulacionLlamada(organismoId, cobroId, motivo, actorUserId, actorNombre, rowVersion));
            return Task.FromResult(Resultado);
        }

        public Task<CajaTramite> BuscarAsync(Guid organismoId, string codigo, Guid usuarioId, string ipHash, CancellationToken ct) =>
            throw new NotSupportedException("Lo cubre OtCajaTests.");

        public Task<CajaCobroConfirmado> ConfirmarAsync(CajaConfirmacion pedido, CancellationToken ct) =>
            throw new NotSupportedException("Lo cubre OtCajaTests.");
    }
}

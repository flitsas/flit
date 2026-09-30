using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13178 (Feature #13119 F7, Épica #13090) — directorio de compañías asociables según el perfil y validación de
/// ids candidatos (reutilizada por HU #13179). Reglas en el servicio de aplicación, sin base de datos.
/// <para>Uso de ejemplo: <c>ListForOtAsync("900.123", 1, 20)</c> devuelve las compañías activas cuyo NIT contiene
/// 900123 o cuyo nombre contiene el texto, sin duplicados por NIT.</para>
/// </summary>
public sealed class MandatarioAssociableCompaniesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client;

    public MandatarioAssociableCompaniesTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ManagingCompanyRow Row(string name, string nit, bool active = true, int day = 0, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, nit, T0.AddDays(day), active);

    private static MandatarioAssociableCompanies Sut(
        IEnumerable<ManagingCompanyRow> all, IEnumerable<CompanyChildListItem>? children = null)
    {
        var dir = Substitute.For<IManagingCompanyDirectory>();
        dir.ListAsync(Arg.Any<CancellationToken>()).Returns([.. all]);
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        hierarchy.ListChildrenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([.. children ?? []]);
        return new MandatarioAssociableCompanies(dir, hierarchy);
    }

    private static CompanyChildListItem Child(string name, string nit, bool active = true, Guid? id = null) =>
        new() { Id = id ?? Guid.NewGuid(), RazonSocial = name, Nit = nit, EstadoActivo = active };

    // ── AC1: OT y Super Admin ven todas las compañías activas ──────────────────────────────────────

    [Fact]
    public async Task AC1_Ot_VeTodasLasActivas_SinDuplicadosPorNit_YSoloIdNombreYNit()
    {
        var a = Row("Alfa SAS", "900111111-1", day: 1);
        var aDup = Row("Alfa SAS (copia)", "900.111.111", day: 5); // mismo NIT sin dígito de verificación
        var b = Row("Beta SAS", "900222222-2", day: 2);
        var inactiva = Row("Gamma SAS", "900333333-3", active: false);
        var sut = Sut([b, aDup, a, inactiva]);

        var result = await sut.ListForOtAsync(null, 1, 20, Ct);

        result.IsValid.Should().BeTrue();
        result.Page!.Items.Select(i => i.Id).Should().Equal(a.Id, b.Id);
        result.Page.Total.Should().Be(2);
        result.Page.AplicaSoloASuCompania.Should().BeFalse();
        typeof(AssociableCompany).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("Id", "Name", "Nit");
    }

    [Fact]
    public async Task AC1_Ot_PaginaLaLista()
    {
        var sut = Sut(Enumerable.Range(1, 5).Select(i => Row($"Compania {i:00}", $"90000000{i}")));

        var p2 = (await sut.ListForOtAsync(null, 2, 2, Ct)).Page!;

        p2.Items.Select(i => i.Name).Should().Equal("Compania 03", "Compania 04");
        p2.Total.Should().Be(5);
        p2.PageSize.Should().Be(2);
    }

    // ── AC2: el OT ve compañías que nunca le han radicado ──────────────────────────────────────────

    [Fact]
    public async Task AC2_Ot_ListaUnaCompaniaQueNuncaLeRadico_PorqueLaFuenteNoDependeDeTramites()
    {
        // La fuente del OT es el directorio de compañías gestoras, no OtVisibleCompanies ni los trámites recibidos.
        var nueva = Row("Compañía nueva SAS", "901999999-0");
        var sut = Sut([nueva]);

        var result = await sut.ListForOtAsync("nueva", 1, 20, Ct);

        result.Page!.Items.Should().ContainSingle(i => i.Id == nueva.Id);
    }

    // ── AC3 / AC4: el Admin de Compañía ve solo sus hijas ─────────────────────────────────────────

    [Fact]
    public async Task AC3_AdminCompania_VeSoloSusHijasActivas_NuncaLasAjenas()
    {
        var h1 = Child("Hija Uno", "800000001-1");
        var h2 = Child("Hija Dos", "800000002-2");
        var inactiva = Child("Hija Inactiva", "800000003-3", active: false);
        var ajena = Row("Ajena SAS", "700000001-1");
        var sut = Sut([ajena, Row("Hija Uno", "800000001-1", id: h1.Id)], [h1, h2, inactiva]);

        var result = await sut.ListForCompanyAsync(Guid.NewGuid(), null, 1, 20, Ct);

        result.Page!.Items.Select(i => i.Id).Should().BeEquivalentTo([h1.Id, h2.Id]);
        result.Page.Items.Should().NotContain(i => i.Id == ajena.Id);
        result.Page.AplicaSoloASuCompania.Should().BeFalse();
    }

    [Fact]
    public async Task AC4_CompaniaSinRed_ListaVaciaYAplicaSoloASuCompania()
    {
        var sut = Sut([Row("Ajena SAS", "700000001-1")], []);

        var page = (await sut.ListForCompanyAsync(Guid.NewGuid(), null, 1, 20, Ct)).Page!;

        page.Items.Should().BeEmpty();
        page.AplicaSoloASuCompania.Should().BeTrue();
    }

    [Fact]
    public async Task AC4_SinRed_SeDecidePorLasHijasNoPorLaBusqueda()
    {
        var sut = Sut([], [Child("Hija Uno", "800000001-1")]);

        var page = (await sut.ListForCompanyAsync(Guid.NewGuid(), "zzz", 1, 20, Ct)).Page!;

        page.Items.Should().BeEmpty();
        page.AplicaSoloASuCompania.Should().BeFalse("tiene hijas: solo que la búsqueda no encontró ninguna");
    }

    // ── AC6: búsqueda ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a")]
    [InlineData(" % ")]
    [InlineData("_")]
    public async Task AC6_BusquedaDeMenosDeDosCaracteres_SeRechaza(string term)
    {
        var sut = Sut([Row("Alfa SAS", "900111111-1")]);

        (await sut.ListForOtAsync(term, 1, 20, Ct)).IsValid.Should().BeFalse();
        (await sut.ListForCompanyAsync(Guid.NewGuid(), term, 1, 20, Ct)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("900.111.111-1")]
    [InlineData("900111111")]
    [InlineData("9001111111")]
    [InlineData("900 111")]
    public async Task AC6_BusquedaPorNit_IgnoraPuntosGuionYEspacios(string term)
    {
        var sut = Sut([Row("Alfa SAS", "900111111-1"), Row("Beta SAS", "900222222-2")]);

        var result = await sut.ListForOtAsync(term, 1, 20, Ct);

        result.Page!.Items.Select(i => i.Name).Should().Equal("Alfa SAS");
    }

    [Theory]
    [InlineData("a%")]
    [InlineData("%a")]
    [InlineData("a_")]
    [InlineData("'; DROP TABLE tenants;--")]
    public async Task AC6_CaracteresEspeciales_SeTratanComoTextoLiteral(string term)
    {
        var sut = Sut([Row("Alfa SAS", "900111111-1"), Row("a%b Ltda", "900222222-2")]);

        var result = await sut.ListForOtAsync(term, 1, 20, Ct);

        result.IsValid.Should().BeTrue();
        // Un comodín no compara contra todo: "a%" solo encuentra nombres que contengan literalmente "a%".
        result.Page!.Items.Select(i => i.Name).Should().BeSubsetOf(["a%b Ltda"]);
    }

    [Fact]
    public async Task AC6_BusquedaPorNombre_NoDistingueMayusculas()
    {
        var sut = Sut([Row("Alfa SAS", "900111111-1"), Row("Beta SAS", "900222222-2")]);

        (await sut.ListForOtAsync("ALFA", 1, 20, Ct)).Page!.Items.Should().ContainSingle(i => i.Name == "Alfa SAS");
    }

    // ── AC5: rechazos (insumo de HU #13179) ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_Rechazos_ClasificaPropiaInactivaInexistente_ParaElOt()
    {
        var owner = Row("Dueña SAS", "900000000-0");
        var ok = Row("Ok SAS", "900111111-1");
        var inactiva = Row("Inactiva SAS", "900222222-2", active: false);
        var ghost = Guid.NewGuid();
        var sut = Sut([owner, ok, inactiva]);

        var r = await sut.RejectionsAsync(null, [owner.Id], [owner.Id, ok.Id, inactiva.Id, ghost], Ct);

        r.Should().HaveCount(3);
        r[owner.Id].Should().Be(AssociableCompanyRejections.CompaniaPropia);
        r[inactiva.Id].Should().Be(AssociableCompanyRejections.CompaniaInactiva);
        r[ghost].Should().Be(AssociableCompanyRejections.CompaniaInexistente);
        r.Should().NotContainKey(ok.Id);
    }

    [Fact]
    public async Task AC5_Rechazos_AdminCompania_NoHijaEsFueraDeAlcance_HijaInactivaEsInactiva()
    {
        var hija = Child("Hija", "800000001-1");
        var hijaInactiva = Child("Hija inactiva", "800000002-2", active: false);
        var ajena = Row("Ajena SAS", "700000001-1");
        var scope = Guid.NewGuid();
        var sut = Sut(
            [
                ajena, Row("Dueña", "600000001-1", id: scope), Row("Hija", "800000001-1", id: hija.Id),
                Row("Hija inactiva", "800000002-2", active: false, id: hijaInactiva.Id),
            ],
            [hija, hijaInactiva]);

        var r = await sut.RejectionsAsync(scope, [scope], [hija.Id, hijaInactiva.Id, ajena.Id, scope], Ct);

        r.Should().NotContainKey(hija.Id);
        r[ajena.Id].Should().Be(AssociableCompanyRejections.FueraDeAlcance);
        r[hijaInactiva.Id].Should().Be(AssociableCompanyRejections.CompaniaInactiva,
            "es su hija pero está inactiva: 422 por inactiva, no 403");
        r[scope].Should().Be(AssociableCompanyRejections.CompaniaPropia);
    }

    // ── Directorio de infraestructura ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Directorio_ExcluyeOrganismosYPlataforma_ConProyeccionMinima()
    {
        await using var ctx = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-13178-{Guid.NewGuid()}").Options);
        var now = DateTimeOffset.UtcNow;
        var gestora = Guid.NewGuid();
        var inactiva = Guid.NewGuid();
        var ot = Guid.NewGuid();
        var plataforma = Guid.NewGuid();
        ctx.Tenants.AddRange(
            NewTenant(gestora, "CONCESIONARIO", true, now), NewTenant(inactiva, "RENTING", false, now),
            NewTenant(ot, "CONCESIONARIO", true, now), NewTenant(plataforma, "FLIT", true, now));
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile { TenantId = ot, TransitOfficeId = Guid.NewGuid() });
        await ctx.SaveChangesAsync(Ct);

        var rows = await new ManagingCompanyDirectory(ctx).ListAsync(Ct);

        rows.Select(r => r.Id).Should().BeEquivalentTo([gestora, inactiva]);
        rows.Single(r => r.Id == inactiva).IsActive.Should().BeFalse();
        typeof(ManagingCompanyRow).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            "Id", "Name", "Nit", "CreatedAt", "IsActive");
    }

    // ── AC5: perfiles en la API ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("gestor_tramites_ot", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("AdminCompany", null)]
    public async Task AC5_RutaDelOt_OtrosPerfiles_Reciben403(string role, string? entityType)
    {
        Authenticate(role, entityType, Guid.NewGuid());

        var response = await _client.GetAsync(OtUrl(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC5_RutaDeLaCompania_UnTenantAjeno_Recibe403()
    {
        Authenticate("AdminCompany", null, Guid.NewGuid());

        (await _client.GetAsync(CompanyUrl(Guid.NewGuid()), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC5_SinSesion_Recibe401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        (await _client.GetAsync(OtUrl(Guid.NewGuid()), Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC6_Api_BusquedaCorta_Responde422_EnLasDosRutas()
    {
        Authenticate(AdminAuthorization.SuperAdminRole, null, Guid.NewGuid());
        (await _client.GetAsync(OtUrl(Guid.NewGuid()) + "?search=a", Ct)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);

        var tenant = Guid.NewGuid();
        Authenticate("AdminCompany", null, tenant);
        (await _client.GetAsync(CompanyUrl(tenant) + "?search=a", Ct)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static string OtUrl(Guid office) => $"/api/v1/admin/transit-offices/{office}/mandate-signers/associable-companies";

    private static string CompanyUrl(Guid tenant) => $"/api/v1/admin/companies/{tenant}/mandate-signers/associable-companies";

    private static Tenant NewTenant(Guid id, string type, bool active, DateTimeOffset now) => new()
    {
        Id = id, Code = "T-" + id.ToString("N")[..8], LegalName = "Tenant " + id.ToString("N")[..4],
        TaxId = "9" + id.ToString("N")[..8], TenantType = type, IsActive = active, CreatedAt = now,
    };

    private void Authenticate(string role, string? entityType, Guid tenantId) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, entityType, tenantId));

    private static string MintToken(string role, string? entityType, Guid tenantId)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("role", role),
            new("tenant_id", tenantId.ToString()),
        };
        if (entityType is not null)
        {
            claims.Add(new Claim(AdminAuthorization.EntityTypeClaimType, entityType));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }
}

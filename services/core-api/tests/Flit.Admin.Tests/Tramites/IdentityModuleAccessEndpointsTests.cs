using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12711 — la API de Validación de Identidad exige el permiso del módulo y rechaza al perfil de
/// organismo de tránsito (<see cref="IdentityModuleAccessFilter"/>), con el pipeline HTTP real:
/// <list type="bullet">
///   <item>AC1 — Administrador OT y Operador OT: 403 en todas las rutas del módulo, sin consultar.</item>
///   <item>AC2 — usuario de compañía sin <c>validaciones.read</c>: 403 en las lecturas del módulo; sin
///   <c>validaciones.manage</c>: 403 en crear, editar, reenviar y reencolar.</item>
///   <item>AC3 — AdminCompany con el permiso y SuperAdmin: pasan.</item>
///   <item>AC4 — Radicador con solo <c>dashboard.read</c>: el listado plano del Dashboard sigue abierto.</item>
///   <item>AC5 — rutas por trámite y bitácora que usa el asistente: siguen abiertas al Radicador.</item>
/// </list>
/// Los roles se describen por sus permisos reales en RBAC (base de desarrollo, 2026-09-18):
/// AdminCompany = validaciones.read + validaciones.manage + dashboard.read; Radicador = dashboard.read +
/// tramites.read + tramites.create; Operador OT (gestor_tramites_ot) = dashboard.read + tramites.read;
/// Administrador OT (ot_admin) = ninguno de estos.
/// </summary>
public sealed class IdentityModuleAccessEndpointsTests
    : IClassFixture<IdentityModuleAccessEndpointsTests.AccessFactory>
{
    private static readonly Guid Compania = Guid.Parse("d0000000-0000-4000-8000-00000000000c");
    private static readonly Guid Organismo = Guid.Parse("d0000000-0000-4000-8000-0000000000a7");
    private static readonly Guid Registro = Guid.Parse("d0000000-0000-4000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly string[] AdminCompany = ["validaciones.read", "validaciones.manage", "dashboard.read"];
    private static readonly string[] Radicador = ["dashboard.read", "tramites.read", "tramites.create"];
    private static readonly string[] OperadorOt = ["dashboard.read", "tramites.read"];
    private static readonly string[] AdminOt = [];
    private static readonly string[] SoloLectura = ["validaciones.read"];

    /// <summary>Todas las rutas propias del módulo (AC1).</summary>
    public static TheoryData<string, string> RutasDelModulo => new()
    {
        { "GET", "/api/v1/tramites/biometric-validations" },
        { "GET", "/api/v1/tramites/biometric-validations/by-person" },
        { "GET", "/api/v1/tramites/biometric-validations/by-person/detail?documentType=CC&documentNumber=1" },
        { "GET", $"/api/v1/tramites/biometric-validations/{Registro}" },
        { "GET", $"/api/v1/tramites/biometric-validations/{Registro}/audit" },
        { "GET", "/api/v1/tramites/identity-validation/stuck" },
        { "GET", "/api/v1/tramites/identity-validation/alerts" },
        { "POST", "/api/v1/tramites/biometric-validations" },
        { "PATCH", $"/api/v1/tramites/biometric-validations/{Registro}" },
        { "POST", $"/api/v1/tramites/biometric-validations/{Registro}/resend" },
        { "POST", $"/api/v1/tramites/identity-validation/stuck/{Registro}/requeue" },
        { "POST", "/api/v1/tramites/identity-validation/stuck/requeue-all" },
    };

    /// <summary>Lecturas que abren SOLO con el permiso del módulo (AC2).</summary>
    public static TheoryData<string> LecturasDelModulo => new()
    {
        "/api/v1/tramites/biometric-validations/by-person",
        "/api/v1/tramites/biometric-validations/by-person/detail?documentType=CC&documentNumber=1",
        $"/api/v1/tramites/biometric-validations/{Registro}",
        "/api/v1/tramites/identity-validation/stuck",
    };

    /// <summary>Escrituras que exigen <c>validaciones.manage</c> (AC2).</summary>
    public static TheoryData<string, string> Escrituras => new()
    {
        { "POST", "/api/v1/tramites/biometric-validations" },
        { "PATCH", $"/api/v1/tramites/biometric-validations/{Registro}" },
        { "POST", $"/api/v1/tramites/biometric-validations/{Registro}/resend" },
        { "POST", $"/api/v1/tramites/identity-validation/stuck/{Registro}/requeue" },
        { "POST", "/api/v1/tramites/identity-validation/stuck/requeue-all" },
    };

    private readonly AccessFactory _factory;

    public IdentityModuleAccessEndpointsTests(AccessFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Outbox.ClearReceivedCalls();
    }

    // ── AC1 — roles de organismo ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RutasDelModulo))]
    public async Task Operador_OT_recibe_403_en_todas_las_rutas_del_modulo_sin_consultar(string method, string url)
    {
        var response = await SendAsync(Organismo, "gestor_tramites_ot", OperadorOt, method, url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Repo.ReceivedCalls().Should().BeEmpty("el rechazo ocurre antes de cualquier consulta");
        _factory.Outbox.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RutasDelModulo))]
    public async Task Administrador_OT_recibe_403_en_todas_las_rutas_del_modulo(string method, string url)
    {
        var response = await SendAsync(Organismo, "ot_admin", AdminOt, method, url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Un_organismo_con_el_permiso_del_modulo_igual_recibe_403()
    {
        // Si alguien le diera validaciones.read a un rol de organismo desde RBAC, el perfil manda.
        var response = await SendAsync(Organismo, "rol_ot_renombrado", AdminCompany, "GET",
            "/api/v1/tramites/biometric-validations/by-person");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain(IdentityModuleAccessFilter.TransitOfficeDeniedCode);
    }

    // ── AC2 — usuario de compañía sin el permiso ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LecturasDelModulo))]
    public async Task Radicador_sin_validaciones_read_recibe_403_en_las_lecturas_del_modulo(string url)
    {
        var response = await SendAsync(Compania, "Radicador", Radicador, "GET", url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain(IdentityModuleAccessFilter.PermissionRequiredCode);
    }

    [Theory]
    [MemberData(nameof(Escrituras))]
    public async Task Sin_validaciones_manage_las_escrituras_responden_403(string method, string url)
    {
        var response = await SendAsync(Compania, "rol_solo_lectura", SoloLectura, method, url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── AC3 — quien tiene el permiso sigue igual ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LecturasDelModulo))]
    public async Task AdminCompany_con_el_permiso_pasa_el_filtro(string url)
    {
        var response = await SendAsync(Compania, "AdminCompany", AdminCompany, "GET", url);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(RutasDelModulo))]
    public async Task SuperAdmin_pasa_el_filtro_sin_permisos_en_el_token(string method, string url)
    {
        var response = await SendAsync(Compania, "SuperAdmin", [], method, url);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── AC4 — Dashboard del gestor ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Radicador_con_dashboard_read_sigue_leyendo_el_listado_plano_del_Dashboard()
    {
        var response = await SendAsync(Compania, "Radicador", Radicador, "GET",
            "/api/v1/tramites/biometric-validations?vigenciaEstado=por_vencer&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── AC5 — asistente de trámites ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/tramites/instances/d0000000-0000-4000-8000-000000000009/biometric")]
    [InlineData("GET", "/api/v1/tramites/instances/d0000000-0000-4000-8000-000000000009/identity-validation/alerts")]
    [InlineData("GET", "/api/v1/tramites/biometric-validations/d0000000-0000-4000-8000-000000000001/audit")]
    public async Task Radicador_sigue_usando_las_rutas_de_identidad_del_asistente(string method, string url)
    {
        // La bitácora por id la consumen el asistente y el detalle del trámite (seguimiento de la
        // identidad de una parte): abre con tramites.read, no solo con el permiso del módulo.
        var response = await SendAsync(Compania, "Radicador", Radicador, method, url);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(
        Guid tenantId, string role, IReadOnlyList<string> permissions, string method, string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(tenantId, role, permissions));
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(Guid tenantId, string role, IReadOnlyList<string> permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));

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

    public sealed class AccessFactory : WebApplicationFactory<Program>
    {
        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();

        public IIdentityValidationOutboxRepository Outbox { get; } = Substitute.For<IIdentityValidationOutboxRepository>();

        public AccessFactory()
        {
            IReadOnlyList<BiometricPersonGroupProjection> none = [];
            Repo.ListBiometricValidationsGroupedByPersonAsync(
                    Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricPersonGroupFilter?>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns((none, 0));
            Repo.CountBiometricPersonsByEstadoAsync(
                    Arg.Any<TenantScope>(), Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, int>());
            Repo.ListBiometricValidationsByTenantAsync(
                    Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
            Repo.CountBiometricValidationsByEstadoAsync(
                    Arg.Any<TenantScope>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, int>());
            Repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<Guid, string>());
            Repo.ListBiometricValidationsByPersonAsync(
                    Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns((Array.Empty<ProcedureInstanceBiometricValidation>(), 0, false));
            Outbox.ListStuckAsync(Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<StuckIdentityValidationRow>());
            Outbox.ListStuckAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<StuckIdentityValidationRow>());
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped(_ => Outbox);
                services.AddScoped<ITenantScopeResolver>(_ => new SingleScopeResolver());
                services.AddScoped<ITransitOfficeTenantProbe>(_ => new OnlyOrganismo());
            });
        }
    }

    private sealed class SingleScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }

    /// <summary>Solo <see cref="Organismo"/> tiene perfil de organismo de tránsito.</summary>
    private sealed class OnlyOrganismo : ITransitOfficeTenantProbe
    {
        public Task<bool> IsTransitOfficeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == Organismo);
    }
}

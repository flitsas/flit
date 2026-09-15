using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Queries.Domain.Tenancy;
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
/// Bug #12554 — <c>/api/v1/admin/tramites/**</c> confiaba en el <c>X-Tenant-Id</c> crudo del cliente:
/// un usuario NO-SuperAdmin del tenant A, con permiso de gestión avanzada pero SIN jerarquía (alcance
/// <see cref="TenantScope.Single"/>, así que <see cref="Flit.Api.Authorization.TenantWriteGuard"/> no
/// actúa — ver AC4 de <c>NetworkWriteRejectionTests</c>, que solo cubre cabezas de grupo), podía mandar
/// <c>X-Tenant-Id: B</c> (tenant ajeno) y el endpoint operaba sobre el recurso de B, porque los 5
/// endpoints leían <c>[FromHeader(Name = "X-Tenant-Id")]</c> directamente en vez de tomar el tenant del
/// JWT (mismo patrón que <c>TenantEnforcementMiddleware</c> ya impone bajo <c>/api/v1/tramites</c>).
/// <para>
/// Prueba NEGATIVA por ruta (6 rutas: las 5 del enunciado del bug + <c>reasignar-gestor</c>): el único
/// repositorio del dominio (<see cref="IProcedureInstanceRepository"/>) se sustituye SIN configurar
/// (NSubstitute devuelve <c>null</c>/valores por defecto para cualquier llamada no configurada), así
/// que el resultado NUNCA es 200 en esta prueba — lo que se verifica con precisión quirúrgica es CON
/// QUÉ <c>tenantId</c> se invoca el repositorio: antes del fix, con el header crudo (B, el tenant
/// ajeno); después del fix, con el tenant del JWT (A) — nunca con B. Esa es la ARQUITECTURA del bug,
/// no un efecto colateral: cualquier instancia que el repositorio SÍ tuviera para B habría sido servida
/// al atacante antes del fix, y queda fuera de su alcance después.
/// </para>
/// Sin PostgreSQL (mismo criterio que <c>NetworkWriteRejectionTests</c>): el repo sustituido intercepta
/// antes de tocar cualquier motor de persistencia real.
/// </summary>
public sealed class AdminTramitesTenantScopeTests : IClassFixture<AdminTramitesTenantScopeTests.RepoSubstituteFactory>
{
    /// <summary>Tenant del atacante — dueño real del JWT con el que se autentica.</summary>
    private static readonly Guid TenantA = Guid.Parse("b0000000-0000-4000-8000-0000000000a1");

    /// <summary>Tenant ajeno — el que el atacante manda crudo en <c>X-Tenant-Id</c>.</summary>
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-4000-8000-0000000000b2");

    private static readonly Guid ProcedureId = Guid.Parse("b0000000-0001-4000-8000-000000000001");
    private static readonly Guid ValidationId = Guid.Parse("b0000000-0002-4000-8000-000000000002");
    private static readonly Guid NewAssignedToUserId = Guid.Parse("b0000000-0003-4000-8000-000000000003");

    private readonly RepoSubstituteFactory _factory;

    public AdminTramitesTenantScopeTests(RepoSubstituteFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
    }

    /// <summary>
    /// Las 6 rutas de gestión avanzada: método, ruta con <c>{id}</c>/<c>{validationId}</c> resueltos y
    /// el método del repositorio que cada una consulta ANTES de tocar cualquier otra dependencia (ver
    /// XML doc de cada handler — <c>AdminCambiarEstadoHandler</c>, <c>AdminAnularHandler</c>,
    /// <c>AdminReasignarGestorHandler</c>, <c>AdminReenviarValidacionIdentidadHandler</c>,
    /// <c>GenerarConsolidadoHandler</c> vía <c>LimpiarConsolidadoHandler</c>,
    /// <c>CargarConsolidadoExternoHandler</c>).
    /// </summary>
    public static TheoryData<string, string, RepoMethod> Routes() => new()
    {
        { "POST", $"/api/v1/admin/tramites/{ProcedureId}/estado", RepoMethod.GetByIdAsync },
        { "POST", $"/api/v1/admin/tramites/{ProcedureId}/anular", RepoMethod.GetByIdAsync },
        { "POST", $"/api/v1/admin/tramites/{ProcedureId}/reasignar-gestor", RepoMethod.GetByIdAsync },
        { "POST", $"/api/v1/admin/tramites/{ProcedureId}/validaciones-identidad/{ValidationId}/reenviar", RepoMethod.GetByIdAsync },
        { "POST", $"/api/v1/admin/tramites/{ProcedureId}/consolidado/limpiar", RepoMethod.GetByIdWithChecklistGraphAsync },
        { "POST", $"/api/v1/admin/tramites/{ProcedureId}/consolidado/cargar", RepoMethod.GetByIdWithAttachmentsAsync },
    };

    public enum RepoMethod
    {
        GetByIdAsync,
        GetByIdWithChecklistGraphAsync,
        GetByIdWithAttachmentsAsync,
    }

    // ── Prueba negativa por ruta ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task NoSuperAdmin_ConHeaderDeOtroTenant_NuncaOperaSobreElTenantDelHeader(
        string method, string route, RepoMethod repoMethod)
    {
        var client = ClientFor(TenantA);

        var response = await client.SendAsync(Request(method, route), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            $"{method} {route}: un recurso que el repositorio no reconoce para el tenant del JWT nunca puede resolver 200 ({body})");

        await AssertRepoCalledOnlyWithJwtTenantAsync(repoMethod);
    }

    /// <summary>
    /// Núcleo de la prueba (AC del Bug #12554): el repositorio se invoca con el tenant del JWT (A),
    /// JAMÁS con el <c>X-Tenant-Id</c> crudo que mandó el cliente (B). Antes del fix, el middleware no
    /// tocaba el header en <c>/api/v1/admin/tramites</c> y el repositorio se invocaba con B —esta
    /// aserción falla (rojo)—; después del fix, el middleware sobrescribe el header con el tenant del
    /// token para cualquier caller NO-SuperAdmin y el repositorio solo ve A.
    /// </summary>
    private async Task AssertRepoCalledOnlyWithJwtTenantAsync(RepoMethod repoMethod)
    {
        var repo = _factory.Repo;

        switch (repoMethod)
        {
            case RepoMethod.GetByIdAsync:
                await repo.Received(1).GetByIdAsync(ProcedureId, TenantA, Arg.Any<CancellationToken>());
                await repo.DidNotReceive().GetByIdAsync(ProcedureId, TenantB, Arg.Any<CancellationToken>());
                break;
            case RepoMethod.GetByIdWithChecklistGraphAsync:
                await repo.Received(1).GetByIdWithChecklistGraphAsync(ProcedureId, TenantA, Arg.Any<CancellationToken>());
                await repo.DidNotReceive().GetByIdWithChecklistGraphAsync(ProcedureId, TenantB, Arg.Any<CancellationToken>());
                break;
            case RepoMethod.GetByIdWithAttachmentsAsync:
                await repo.Received(1).GetByIdWithAttachmentsAsync(ProcedureId, TenantA, Arg.Any<CancellationToken>());
                await repo.DidNotReceive().GetByIdWithAttachmentsAsync(ProcedureId, TenantB, Arg.Any<CancellationToken>());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(repoMethod));
        }
    }

    // ── Inventario — toda ruta de gestión avanzada tiene su prueba negativa ────────────────────

    [Fact]
    public void ElInventarioDeRutasCubreTodaLaGestionAvanzadaDeTramites()
    {
        var registered = _factory.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw
                && raw.StartsWith("/api/v1/admin/tramites/{id:guid}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE")
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        registered.Should().NotBeEmpty("deben existir rutas de escritura bajo /api/v1/admin/tramites/{id:guid}");
        registered.Should().HaveCount(6,
            "el inventario de esta prueba (Routes()) debe tener una entrada por cada ruta registrada; "
            + "si esto cambia, agrega/retira la ruta en Routes() (Bug #12554)");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private HttpClient ClientFor(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, "AdminCompany"));
        // El atacante manda SIEMPRE el header del tenant ajeno (B), sin importar el JWT.
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString());
        return client;
    }

    private static HttpRequestMessage Request(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);

        if (route.EndsWith("/consolidado/cargar", StringComparison.Ordinal))
        {
            var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]); // %PDF
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "file", "consolidado.pdf");
            request.Content = form;
            return request;
        }

        object body = route switch
        {
            var r when r.EndsWith("/estado", StringComparison.Ordinal) => new { toStatus = "entregado" },
            var r when r.EndsWith("/reasignar-gestor", StringComparison.Ordinal) => new { newAssignedToUserId = NewAssignedToUserId },
            _ => new { },
        };
        request.Content = JsonContent.Create(body);
        return request;
    }

    /// <summary>JWT del tenant A con TODOS los permisos de gestión avanzada (el 403/404 debe venir del
    /// guard de tenant, no de <c>RequirePermission</c>).</summary>
    private static string Token(Guid tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", "22222222-2222-2222-2222-222222222222"),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(AdminTramiteAuthorization.AllSlugs.Select(slug => new Claim("permissions", slug)));

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

    /// <summary>Host con <see cref="IProcedureInstanceRepository"/> sustituido (NSubstitute, sin
    /// configurar: cualquier llamada no configurada devuelve <c>null</c>/valor por defecto) y
    /// <see cref="ITenantScopeResolver"/> fijo a <see cref="TenantScope.Single"/> (cliente SIN
    /// jerarquía — el caso que <c>TenantWriteGuard</c> no cubre).</summary>
    public sealed class RepoSubstituteFactory : WebApplicationFactory<Program>
    {
        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped<ITenantScopeResolver>(_ => new FixedSingleScopeResolver());
            });
        }
    }

    private sealed class FixedSingleScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }
}

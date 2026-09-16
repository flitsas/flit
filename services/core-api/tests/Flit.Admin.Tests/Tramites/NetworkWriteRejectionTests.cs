using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12358 (Feature #12257) — AC4: toda escritura de una cabeza de grupo sobre un trámite de un
/// hijo es rechazada con 403 por la comprobación de ESCRITURA (<see cref="TenantScope.CanWrite"/>),
/// aunque manipule el identificador; una prueba negativa por cada ruta de escritura del módulo de
/// trámites (runtime + gestión avanzada) y la paridad de un cliente sin jerarquía.
/// <para>
/// Host real (<see cref="WebApplicationFactory{TEntryPoint}"/>) con dos dobles inyectados vía
/// <c>ConfigureTestServices</c>: <see cref="ITenantScopeResolver"/> (P ⇒ <c>Group(P,[C1])</c>;
/// cualquier otro ⇒ <c>Single</c>) e <see cref="IProcedureInstanceOwnerLookup"/> (el trámite
/// <see cref="ChildProcedure"/> es de C1; <see cref="OwnProcedure"/> es de P). Sin PostgreSQL: el
/// guard responde ANTES del handler, así que ninguna prueba negativa necesita la base.
/// </para>
/// Uso de ejemplo: <c>PATCH /api/v1/tramites/instances/{idDeC1}/priority</c> con token de P ⇒
/// <c>403 { "error": "network_write_forbidden" }</c>.
/// </summary>
public sealed class NetworkWriteRejectionTests : IClassFixture<NetworkWriteRejectionTests.HeadOfGroupFactory>
{
    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");

    /// <summary>Trámite de C1 (hijo): la cabeza puede LEERLO por la red, nunca escribirlo.</summary>
    private static readonly Guid ChildProcedure = Guid.Parse("a0000000-0003-4000-8000-000000001101");

    /// <summary>Trámite de la propia cabeza P: escribir sobre él sigue permitido.</summary>
    private static readonly Guid OwnProcedure = Guid.Parse("a0000000-0003-4000-8000-000000000101");

    private static readonly Guid AnyId = Guid.Parse("a0000000-0004-4000-8000-000000000001");

    private readonly HeadOfGroupFactory _factory;

    public NetworkWriteRejectionTests(HeadOfGroupFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Inventario de rutas de escritura sobre un trámite identificado en la ruta (brief F13/F14/F15):
    /// 39 runtime bajo <c>/api/v1/tramites/instances/{id}</c> + 6 de gestión avanzada bajo
    /// <c>/api/v1/admin/tramites/{id}</c>. <c>{id}</c> se sustituye por el trámite del hijo.
    /// </summary>
    private static readonly (string Method, string Route)[] Routes =
    [
        // ProcedureInstanceEndpoints (F13)
        ("PUT", "/api/v1/tramites/instances/{id}/mandate-signer"),
        ("PATCH", "/api/v1/tramites/instances/{id}/field-values"),
        ("POST", "/api/v1/tramites/instances/{id}/ocr-fields"),
        ("PUT", "/api/v1/tramites/instances/{id}/prenda"),
        ("POST", "/api/v1/tramites/instances/{id}/finalize-draft"),
        ("PATCH", "/api/v1/tramites/instances/{id}/priority"),
        ("PATCH", "/api/v1/tramites/instances/{id}/current-step"),
        ("POST", "/api/v1/tramites/instances/{id}/submit"),
        ("PUT", "/api/v1/tramites/instances/{id}/pause"),
        ("POST", "/api/v1/tramites/instances/{id}/enviar-al-ot"),
        ("POST", "/api/v1/tramites/instances/{id}/plate-flow/complete"), // alias legado de enviar-al-ot (ADR-0059)
        ("POST", "/api/v1/tramites/instances/{id}/subsanar"),
        ("POST", "/api/v1/tramites/instances/{id}/cancelar-subsanacion"),
        ("POST", "/api/v1/tramites/instances/{id}/transition"),
        // Otros archivos (F14)
        ("PUT", "/api/v1/tramites/instances/{id}/actors"),
        ("POST", "/api/v1/tramites/instances/{id}/attachments"),
        ("POST", "/api/v1/tramites/instances/{id}/attachments/presign"),
        ("POST", "/api/v1/tramites/instances/{id}/attachments/register"),
        ("DELETE", "/api/v1/tramites/instances/{id}/attachments/{any}"),
        ("POST", "/api/v1/tramites/instances/{id}/attachments/generate-impronta"),
        ("POST", "/api/v1/tramites/instances/{id}/attachments/generate-rues"),
        ("PATCH", "/api/v1/tramites/instances/{id}/checklist/impronta-diferida"),
        ("POST", "/api/v1/tramites/instances/{id}/signatures"),
        ("POST", "/api/v1/tramites/instances/{id}/signatures/{any}/simulate"),
        ("POST", "/api/v1/tramites/instances/{id}/deferred-signature"),
        ("POST", "/api/v1/tramites/instances/{id}/fur"),
        ("POST", "/api/v1/tramites/instances/{id}/consolidado"),
        ("PUT", "/api/v1/tramites/instances/{id}/commercial"),
        ("POST", "/api/v1/tramites/instances/{id}/biometric"),
        ("POST", "/api/v1/tramites/instances/{id}/biometric/simulate"),
        ("POST", "/api/v1/tramites/instances/{id}/biometric/{any}/reconcile"),
        ("POST", "/api/v1/tramites/instances/{id}/identity/ensure"),
        ("POST", "/api/v1/tramites/instances/{id}/consultations/RUNT"),
        ("POST", "/api/v1/tramites/instances/{id}/runt-person"),
        ("POST", "/api/v1/tramites/instances/{id}/soat/validate-runt"),
        ("POST", "/api/v1/tramites/instances/{id}/rues-lookup"),
        ("POST", "/api/v1/tramites/instances/{id}/participants"),
        ("POST", "/api/v1/tramites/instances/{id}/participants/{any}/reinvite"),
        ("POST", "/api/v1/tramites/instances/{id}/preflight"),
        ("POST", "/api/v1/tramites/instances/{id}/rnmc"),
        // Gestión avanzada (F15) — fuera del TenantEnforcementMiddleware; el guard resuelve el alcance por BD.
        ("POST", "/api/v1/admin/tramites/{id}/anular"),
        ("POST", "/api/v1/admin/tramites/{id}/estado"),
        ("POST", "/api/v1/admin/tramites/{id}/reasignar-gestor"),
        ("POST", "/api/v1/admin/tramites/{id}/validaciones-identidad/{any}/reenviar"),
        ("POST", "/api/v1/admin/tramites/{id}/consolidado/limpiar"),
        ("POST", "/api/v1/admin/tramites/{id}/consolidado/cargar"),
    ];

    public static TheoryData<string, string> WriteRoutes()
    {
        var data = new TheoryData<string, string>();
        foreach (var (method, route) in Routes)
            data.Add(method, route);
        return data;
    }

    // ── AC4 — una prueba negativa por ruta ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task AC4_Cabeza_de_grupo_escribiendo_sobre_un_tramite_del_hijo_recibe_403_por_CanWrite(string method, string route)
    {
        var client = ClientFor(P);

        var response = await client.SendAsync(Request(method, route, ChildProcedure), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {route}: {body}");
        body.Should().Contain(TenantWriteGuard.ErrorCode, $"{method} {route} debe rechazarse por CanWrite, no por otra policy");
    }

    [Fact]
    public async Task AC4_Pausa_masiva_con_un_tramite_del_hijo_en_el_body_recibe_403()
    {
        var client = ClientFor(P);

        var response = await client.PostAsJsonAsync(
            "/api/v1/tramites/instances/pause-massive",
            new { ids = new[] { OwnProcedure, ChildProcedure }, paused = true },
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        body.Should().Contain(TenantWriteGuard.ErrorCode);
    }

    [Fact]
    public void AC4_El_inventario_de_rutas_negativas_cubre_toda_escritura_registrada_sobre_un_tramite()
    {
        // Si alguien añade una ruta de escritura con {id} bajo los prefijos vigilados y no la lista arriba,
        // esta prueba la nombra: AC4 exige UNA prueba negativa por ruta de escritura.
        var registered = _factory.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw
                && (raw.StartsWith("/api/v1/tramites/instances/{id:guid}", StringComparison.OrdinalIgnoreCase)
                    || raw.StartsWith("/api/v1/admin/tramites/{id:guid}", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE")
                .Select(m => $"{m} {Normalize(e.RoutePattern.RawText!)}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var covered = Routes
            .Select(r => $"{r.Method} {Normalize(r.Route)}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = registered.Where(r => !covered.Contains(r)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        missing.Should().BeEmpty("cada ruta de escritura sobre un trámite necesita su prueba negativa (AC4). Faltan: {0}", string.Join(", ", missing));

        static string Normalize(string raw) =>
            System.Text.RegularExpressions.Regex.Replace(raw, @"\{[^}]+\}", "{}")
                .Replace("/RUNT", "/{}", StringComparison.Ordinal);
    }

    // ── Paridad: Single y la propia cabeza no cambian ─────────────────────────────────────────

    [Fact]
    public async Task Paridad_Cliente_sin_jerarquia_no_pasa_por_el_guard()
    {
        var client = ClientFor(S);

        var response = await client.SendAsync(
            Request("PATCH", "/api/v1/tramites/instances/{id}/priority", ChildProcedure), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain(TenantWriteGuard.ErrorCode, "para un cliente Single el comportamiento es idéntico a hoy");
    }

    [Fact]
    public async Task Paridad_La_cabeza_puede_escribir_sobre_su_propio_tramite()
    {
        var client = ClientFor(P);

        var response = await client.SendAsync(
            Request("PATCH", "/api/v1/tramites/instances/{id}/priority", OwnProcedure), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain(TenantWriteGuard.ErrorCode, "CanWrite(P) es verdadero para la propia cabeza");
    }

    // ── Policy de cabeza en las rutas consolidadas ────────────────────────────────────────────

    [Fact]
    public async Task Red_Cliente_sin_jerarquia_recibe_403_network_scope_required()
    {
        var response = await ClientFor(S).GetAsync("/api/v1/tramites/network/instances", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
    }

    [Fact]
    public async Task Red_SuperAdmin_recibe_403_network_scope_required()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(P, "SuperAdmin"));

        var response = await client.GetAsync("/api/v1/tramites/network/instances", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
    }

    [Fact]
    public async Task Red_Sin_token_recibe_401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/tramites/network/instances", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Red_childTenantId_fuera_del_alcance_recibe_403_sin_consultar()
    {
        var client = ClientFor(P);

        var get = await client.GetAsync($"/api/v1/tramites/network/instances?childTenantId={X}", TestContext.Current.CancellationToken);
        var search = await client.PostAsJsonAsync("/api/v1/tramites/network/instances/search", new { childTenantId = X }, TestContext.Current.CancellationToken);
        var counts = await client.PostAsJsonAsync("/api/v1/tramites/network/instances/estado-counts", new { childTenantId = X }, TestContext.Current.CancellationToken);

        foreach (var response in new[] { get, search, counts })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ChildOutOfScope);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private HttpClient ClientFor(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, "AdminCompany"));
        return client;
    }

    private static HttpRequestMessage Request(string method, string route, Guid procedureId)
    {
        var url = route.Replace("{id}", procedureId.ToString(), StringComparison.Ordinal)
            .Replace("{any}", AnyId.ToString(), StringComparison.Ordinal);
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "DELETE")
            return request;

        // Las rutas de carga de archivo declaran multipart/form-data: con otro Content-Type el
        // enrutamiento (AcceptsMatcherPolicy) responde 415 antes de elegir endpoint, así que el guard
        // se ejercita con el tipo correcto (y un archivo vacío que jamás llega al handler).
        if (route.EndsWith("/attachments", StringComparison.Ordinal) || route.EndsWith("/consolidado/cargar", StringComparison.Ordinal))
        {
            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent([1, 2, 3]), "file", "vacio.pdf");
            request.Content = form;
            return request;
        }

        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>JWT con tenant, rol y TODOS los permisos de gestión avanzada (para que el 403 sea del guard, no de RequirePermission).</summary>
    private static string Token(Guid tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", "11111111-1111-1111-1111-111111111111"),
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

    /// <summary>Host con alcance de grupo para P y dueños fijos de los dos trámites de prueba.</summary>
    public sealed class HeadOfGroupFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ITenantScopeResolver>(_ => new FakeScopeResolver());
                services.AddScoped<IProcedureInstanceOwnerLookup>(_ => new FakeOwnerLookup());
            });
        }
    }

    private sealed class FakeScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == P
                ? TenantScope.Group(P, [C1], GroupKind.Concesion)
                : TenantScope.Single(tenantId));
    }

    private sealed class FakeOwnerLookup : IProcedureInstanceOwnerLookup
    {
        private static readonly Dictionary<Guid, Guid> Owners = new()
        {
            [ChildProcedure] = C1,
            [OwnProcedure] = P,
        };

        public Task<Guid?> GetOwnerTenantIdAsync(Guid procedureInstanceId, CancellationToken ct = default) =>
            Task.FromResult(Owners.TryGetValue(procedureInstanceId, out var owner) ? owner : (Guid?)null);

        public Task<IReadOnlyDictionary<Guid, Guid>> GetOwnerTenantIdsAsync(IReadOnlyCollection<Guid> procedureInstanceIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
                procedureInstanceIds.Where(Owners.ContainsKey).Distinct().ToDictionary(id => id, id => Owners[id]));
    }
}

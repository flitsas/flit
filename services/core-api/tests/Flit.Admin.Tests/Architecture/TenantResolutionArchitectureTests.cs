using System.Reflection;
using System.Text.RegularExpressions;
using Flit.Api.Authorization;
using Flit.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #12320 (Feature #12254) — pruebas de arquitectura del punto único de resolución del tenant.
/// Uso de ejemplo: no se invoca; corre en CI y falla nombrando el tipo, archivo o ruta infractor.
/// <list type="bullet">
///   <item><b>AC2 (reflexión)</b>: ningún tipo del assembly <c>Flit.Api</c> — incluidos tipos anidados y
///   las clases generadas para lambdas/funciones locales — declara un método cuyo nombre contenga
///   <c>TryResolveTenantId</c>, <c>ResolveTenantId</c> o <c>TryResolveNonEmptyTenantId</c> fuera de
///   <see cref="RequestTenantResolver"/>.</item>
///   <item><b>AC2 (fuente)</b>: la reflexión no ve el cuerpo de los métodos, así que se complementa con
///   un barrido de los <c>.cs</c> de <c>src/Flit.Api</c>: ningún archivo salvo el componente (y la lista
///   explícita <see cref="RawClaimReadAllowlist"/>) lee el claim <c>tenant_id</c> directamente
///   (<c>FindFirstValue</c>/<c>FindFirst</c>/<c>HasClaim</c> con <c>AdminAuthorization.TenantIdClaimType</c>
///   o el literal <c>"tenant_id"</c>) ni declara una firma <c>TryResolveTenantId(</c>.</item>
///   <item><b>AC3</b>: toda ruta registrada bajo <c>/api/v1/tramites</c> debe estar cubierta por
///   <see cref="TenantEnforcementMiddleware.RuntimeScopedRoutes"/>, declarada como NO tenant-scoped por
///   diseño en <see cref="NotTenantScopedRoutes"/> (prefijo) o congelada como deuda en
///   <see cref="LegacyUncoveredRoutes"/> (patrón exacto); una ruta nueva sin declarar falla nombrándola.</item>
/// </list>
/// </summary>
public sealed class TenantResolutionArchitectureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantResolutionArchitectureTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static readonly Assembly ApiAssembly = typeof(RequestTenantResolver).Assembly;

    private static readonly string[] ForbiddenMethodNameFragments =
    [
        "TryResolveTenantId",
        "ResolveTenantId",
        "TryResolveNonEmptyTenantId",
    ];

    /// <summary>
    /// Archivos que leen el claim crudo por una razón documentada (no resuelven un tenant para
    /// autorizar). Cualquier archivo nuevo que lea el claim directamente falla el test.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RawClaimReadAllowlist =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // GET /auth/me devuelve el claim como STRING en el payload y hace Guid.Parse (lanza si está
            // mal formado): semántica distinta a "resolver o no"; se conserva tal cual (AC1: mismo resultado).
            ["Endpoints/AuthEndpoints.cs"] = "payload de /auth/me expone el claim crudo",
        };

    /// <summary>
    /// Rutas bajo <c>/api/v1/tramites</c> que por diseño NO son runtime tenant-scoped: parametrización y
    /// catálogos globales (protegidos por policy de rol — HU #10508 — o públicos por ser estáticos), sin
    /// dato de ninguna compañía. Cada entrada nueva aquí es una decisión explícita.
    /// </summary>
    private static readonly string[] NotTenantScopedRoutes =
    [
        "/api/v1/tramites/procedure-types",
        "/api/v1/tramites/vehicle-bodyworks",
        "/api/v1/tramites/vehicle-colors",
        "/api/v1/tramites/vehicle-service-types",
        // HU #12034 — lista estática de tipos documentales con OCR; no depende del inquilino.
        "/api/v1/tramites/ocr/tipos",
        // CF-02 (HU #10883) — esqueleto del wizard para el paso 1 sin trámite: catálogo global.
        "/api/v1/tramites/wizard-preview",
        // Guía informativa de documentos del paso 1 sin instancia: catálogo global (+ overrides OT por id).
        "/api/v1/tramites/document-requirements/preview",
    ];

    /// <summary>
    /// Baseline congelada (HU #12320) de rutas que HOY quedan fuera de
    /// <see cref="TenantEnforcementMiddleware.RuntimeScopedRoutes"/> y toman el tenant del
    /// <c>X-Tenant-Id</c> crudo del cliente o del body. AC3/AC4 de esta HU exigen matching idéntico al
    /// histórico, así que NO se incorporan al middleware aquí: quedan declaradas como deuda para que
    /// (a) una ruta NUEVA sin declarar falle nombrándola y (b) al cubrirlas en una HU posterior haya que
    /// retirarlas de esta lista (el test <c>AC3_LasRutasDeclaradasNoEstanTambienEnLaListaDelMiddleware</c>
    /// lo obliga).
    /// </summary>
    private static readonly string[] LegacyUncoveredRoutes =
    [
        // HU #10197 — create legacy con snapshot documental (tenant del body) y lectura del snapshot.
        "/api/v1/tramites/",
        "/api/v1/tramites/{tramiteId:guid}/document-requirements",
        // OCR del paso 1 sin instancia (tenant del header crudo).
        "/api/v1/tramites/ocr/{tipo}",
        "/api/v1/tramites/ocr/lote",
        // HU #10478 — proveedor de consulta del tenant (tenant del header crudo).
        "/api/v1/tramites/consultation-config",
    ];

    // ── AC2 — reflexión sobre el assembly Flit.Api ─────────────────────────────────

    [Fact]
    public void AC2_NingunTipoFueraDelComponenteDeclaraUnResolverDeTenant()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var offenders = ApiAssembly.GetTypes()
            .Where(t => t != typeof(RequestTenantResolver))
            .SelectMany(t => t.GetMethods(all).Select(m => (Type: t, Method: m)))
            .Where(x => ForbiddenMethodNameFragments.Any(f =>
                x.Method.Name.Contains(f, StringComparison.Ordinal)))
            .Select(x => $"{x.Type.FullName}.{x.Method.Name}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "la resolución del tenant vive únicamente en RequestTenantResolver (HU #12320); "
            + "los métodos locales (incl. funciones locales/lambdas, cuyo nombre generado conserva el original) están prohibidos");
    }

    // ── AC2 — barrido de fuente (complementa la reflexión) ─────────────────────────

    private static readonly Regex RawClaimReadPattern = new(
        @"\b(FindFirstValue|FindFirst|HasClaim|FindAll)\s*\(\s*(AdminAuthorization\.TenantIdClaimType|""tenant_id"")",
        RegexOptions.Compiled);

    private static readonly Regex LocalResolverSignaturePattern = new(
        @"\b(bool|Guid\??)\s+(TryResolveTenantId|ResolveTenantId|TryResolveNonEmptyTenantId)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void AC2_NingunArchivoDeFlitApiLeeElClaimTenantIdFueraDelComponente()
    {
        var apiDir = LocateFlitApiSourceDirectory();
        var files = Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        files.Should().NotBeEmpty($"debe existir el código fuente de Flit.Api en {apiDir}");

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(apiDir, file).Replace('\\', '/');
            if (rel.Equals("Authorization/RequestTenantResolver.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (LocalResolverSignaturePattern.IsMatch(line))
                    offenders.Add($"{rel}:{i + 1} declara un resolver local: {line.Trim()}");

                if (RawClaimReadPattern.IsMatch(line) && !RawClaimReadAllowlist.ContainsKey(rel))
                    offenders.Add($"{rel}:{i + 1} lee el claim tenant_id directamente: {line.Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            "toda lectura del claim tenant_id pasa por RequestTenantResolver (HU #12320); "
            + "si un archivo necesita el claim crudo, documenta la razón en RawClaimReadAllowlist");
    }

    [Fact]
    public void AC2_LaAllowlistDeLecturaCrudaSigueVigente()
    {
        // Si el archivo allowlisted deja de leer el claim crudo, la entrada debe retirarse (no acumular deuda).
        var apiDir = LocateFlitApiSourceDirectory();
        foreach (var (rel, reason) in RawClaimReadAllowlist)
        {
            var path = Path.Combine(apiDir, rel.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(path).Should().BeTrue($"la allowlist referencia {rel} ({reason})");
            File.ReadLines(path).Any(l => RawClaimReadPattern.IsMatch(l)).Should().BeTrue(
                $"{rel} ya no lee el claim crudo: retira la entrada de RawClaimReadAllowlist ({reason})");
        }
    }

    // ── AC3 — toda ruta bajo /api/v1/tramites está declarada ────────────────────────

    [Fact]
    public void AC3_TodaRutaRegistradaBajoTramitesEstaCubiertaPorLaDeclaracionDelMiddleware()
    {
        var routes = RegisteredTramitesRoutes();

        routes.Should().NotBeEmpty($"deben existir endpoints registrados bajo {TenantEnforcementMiddleware.RuntimeRoutePrefix}");

        var uncovered = routes
            .Where(route => !IsCoveredByMiddleware(route) && !IsDeclaredNotTenantScoped(route) && !IsLegacyUncovered(route))
            .ToList();

        uncovered.Should().BeEmpty(
            "cada ruta bajo /api/v1/tramites debe estar en TenantEnforcementMiddleware.RuntimeScopedRoutes "
            + "o declararse explícitamente (NotTenantScopedRoutes / LegacyUncoveredRoutes) en este test (HU #12320 AC3). "
            + "Rutas sin declarar: {0}", string.Join(", ", uncovered));
    }

    [Fact]
    public void AC3_LasRutasDeclaradasNoEstanTambienEnLaListaDelMiddleware()
    {
        // Coherencia de la declaración: una ruta no puede ser tenant-scoped y no-scoped/legacy a la vez.
        // Cuando una HU posterior cubra una ruta legacy en el middleware, este test obliga a retirarla de aquí.
        var conflicts = NotTenantScopedRoutes.Concat(LegacyUncoveredRoutes).Where(IsCoveredByMiddleware).ToList();
        conflicts.Should().BeEmpty("NotTenantScopedRoutes/LegacyUncoveredRoutes y RuntimeScopedRoutes deben ser disjuntas");
    }

    [Fact]
    public void AC3_LasRutasLegacyDeclaradasSiguenRegistradas()
    {
        // Sin deuda fantasma: si una ruta legacy desaparece o se cubre, la entrada debe retirarse.
        var registered = RegisteredTramitesRoutes();
        var stale = LegacyUncoveredRoutes
            .Where(legacy => !registered.Contains(legacy, StringComparer.OrdinalIgnoreCase))
            .ToList();
        stale.Should().BeEmpty("cada entrada de LegacyUncoveredRoutes debe corresponder a una ruta registrada (exacta)");
    }

    private List<string> RegisteredTramitesRoutes()
    {
        var prefix = TenantEnforcementMiddleware.RuntimeRoutePrefix;
        return _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(raw => raw is not null && ("/" + raw.TrimStart('/')).StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            .Select(raw => "/" + raw!.TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsLegacyUncovered(string routePattern) =>
        LegacyUncoveredRoutes.Contains(routePattern, StringComparer.OrdinalIgnoreCase);

    private static bool IsCoveredByMiddleware(string routePattern) =>
        TenantEnforcementMiddleware.IsRuntimeScoped(new Microsoft.AspNetCore.Http.PathString(routePattern));

    private static bool IsDeclaredNotTenantScoped(string routePattern) =>
        NotTenantScopedRoutes.Any(declared =>
            new Microsoft.AspNetCore.Http.PathString(routePattern)
                .StartsWithSegments(declared, StringComparison.OrdinalIgnoreCase));

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static string LocateFlitApiSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Api");
            if (File.Exists(Path.Combine(candidate, "Flit.Api.csproj")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontró src/Flit.Api subiendo desde " + AppContext.BaseDirectory);
    }
}

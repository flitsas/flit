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
        // /api/v1/tramites/consultation-config (HU #10478) salió de esta lista el 2026-09-15: ya está en
        // RuntimeScopedRoutes (bug de registro diferido, mismo patrón que Bug #12558).
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

    // ── HU #12358 AC7 — toda ruta de red está declarada en el middleware ────────────────

    /// <summary>Prefijo único de la vista consolidada de la red (Feature #12257, decisión 1 del plan).</summary>
    private const string NetworkRoutePrefix = "/api/v1/tramites/network";

    [Fact]
    public void AC7_TodaRutaDeRedEstaCubiertaPorRuntimeScopedRoutes()
    {
        // Más estricto que AC3: una ruta de red NO puede declararse «no scopeada» ni «legacy». Si alguien
        // registra /network/algo fuera de la lista del middleware, ScopeFromItems sería null y la policy
        // de cabeza respondería 403 siempre; esta prueba lo nombra antes.
        var network = RegisteredTramitesRoutes()
            .Where(r => r.StartsWith(NetworkRoutePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        network.Should().NotBeEmpty($"deben existir rutas registradas bajo {NetworkRoutePrefix} (HU #12358)");
        network.Should().Contain(
        [
            NetworkRoutePrefix + "/instances",
            NetworkRoutePrefix + "/instances/search",
            NetworkRoutePrefix + "/instances/estado-counts",
            NetworkRoutePrefix + "/instances/{id:guid}",
        ]);

        var uncovered = network.Where(route => !IsCoveredByMiddleware(route)).ToList();
        uncovered.Should().BeEmpty(
            "toda ruta bajo /api/v1/tramites/network debe estar en TenantEnforcementMiddleware.RuntimeScopedRoutes (HU #12358 AC7). "
            + "Rutas sin declarar: {0}", string.Join(", ", uncovered));

        TenantEnforcementMiddleware.RuntimeScopedRoutes.Should().Contain(
            r => r.Path.Equals(NetworkRoutePrefix, StringComparison.OrdinalIgnoreCase) && r.Match == TenantEnforcementMiddleware.RouteMatch.Prefix,
            "la cobertura de la red es UNA entrada Prefix, no una por ruta");
    }

    // ── Bug #12554 — toda ruta de gestión avanzada (/api/v1/admin/tramites) está declarada ──────

    /// <summary>Prefijo de gestión avanzada del admin sobre trámites (Feature #12155), FUERA de
    /// <see cref="TenantEnforcementMiddleware.RuntimeRoutePrefix"/> — por eso <see
    /// cref="AC3_TodaRutaRegistradaBajoTramitesEstaCubiertaPorLaDeclaracionDelMiddleware"/> nunca lo
    /// vio: <see cref="RegisteredTramitesRoutes"/> solo barre lo que empieza por
    /// <c>/api/v1/tramites</c>. Este test barre TODOS los endpoints registrados (sin ese filtro) y
    /// exige que los de <c>/api/v1/admin/tramites</c> también estén en
    /// <see cref="TenantEnforcementMiddleware.RuntimeScopedRoutes"/>.</summary>
    private const string AdminTramitesRoutePrefix = "/api/v1/admin/tramites";

    [Fact]
    public void AC8_TodaRutaAdminTramitesEstaCubiertaPorRuntimeScopedRoutes()
    {
        var adminRoutes = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(raw => raw is not null && ("/" + raw.TrimStart('/')).StartsWith(AdminTramitesRoutePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(raw => "/" + raw!.TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        adminRoutes.Should().NotBeEmpty($"deben existir endpoints registrados bajo {AdminTramitesRoutePrefix} (Bug #12554)");

        var uncovered = adminRoutes.Where(route => !IsCoveredByMiddleware(route)).ToList();
        uncovered.Should().BeEmpty(
            "toda ruta bajo /api/v1/admin/tramites debe estar en TenantEnforcementMiddleware.RuntimeScopedRoutes (Bug #12554): "
            + "sin esto, el endpoint confía en el X-Tenant-Id crudo del cliente en vez del tenant del JWT. Rutas sin declarar: {0}",
            string.Join(", ", uncovered));

        TenantEnforcementMiddleware.RuntimeScopedRoutes.Should().Contain(
            r => r.Path.Equals(AdminTramitesRoutePrefix, StringComparison.OrdinalIgnoreCase) && r.Match == TenantEnforcementMiddleware.RouteMatch.Prefix,
            "la cobertura de gestión avanzada es UNA entrada Prefix, no una por ruta (Bug #12554)");
    }

    // ── Bug #12564 — toda entrada de RuntimeScopedRoutes tolera la barra final ────────────────

    /// <summary>
    /// El routing de ASP.NET Core sirve <c>/ruta/</c> igual que <c>/ruta</c>, pero
    /// <c>PathString.Equals</c> (comparación histórica de <see cref="TenantEnforcementMiddleware.RouteMatch.Exact"/>)
    /// no las iguala: <c>GET /api/v1/tramites/consultation-config/</c> saltaba el middleware y el
    /// handler leía el <c>X-Tenant-Id</c> crudo (revisión de seguridad del PR #377). Mismo defecto en
    /// <c>transit-offices</c>, <c>preflight-preview</c> y <c>rues-preview</c>. Exact tolera SOLO la
    /// barra final: ni un sufijo pegado (<c>…configX</c>) ni un segmento hijo (<c>…config/otra</c>).
    /// </summary>
    [Fact]
    public void AC10_TodaRutaRuntimeScopedSigueCubiertaConBarraFinal()
    {
        var routes = TenantEnforcementMiddleware.RuntimeScopedRoutes;
        routes.Should().NotBeEmpty();

        var sinBarraFinal = routes
            .Where(r => !TenantEnforcementMiddleware.IsRuntimeScoped(new Microsoft.AspNetCore.Http.PathString(r.Path + "/")))
            .Select(r => r.Path)
            .ToList();
        sinBarraFinal.Should().BeEmpty(
            "toda entrada de RuntimeScopedRoutes debe interceptar también la petición con barra final (Bug #12564): "
            + "el routing la sirve igual y sin middleware el handler lee X-Tenant-Id crudo. Rutas que la dejan pasar: {0}",
            string.Join(", ", sinBarraFinal));

        var exact = routes.Where(r => r.Match == TenantEnforcementMiddleware.RouteMatch.Exact).ToList();
        exact.Should().NotBeEmpty();
        foreach (var route in exact)
        {
            TenantEnforcementMiddleware.IsRuntimeScoped(new Microsoft.AspNetCore.Http.PathString(route.Path + "x"))
                .Should().BeFalse($"Exact no debe matchear un sufijo pegado ({route.Path}x)");
            TenantEnforcementMiddleware.IsRuntimeScoped(new Microsoft.AspNetCore.Http.PathString(route.Path + "/otra"))
                .Should().BeFalse($"Exact no debe matchear un segmento hijo ({route.Path}/otra)");
        }
    }

    // ── Bug #12558 — toda ruta que lea [FromHeader(Name = "X-Tenant-Id")] está declarada ───────

    /// <summary>
    /// Rutas que leen el header <c>X-Tenant-Id</c> vía <c>[FromHeader]</c> pero NO están cubiertas por
    /// <see cref="TenantEnforcementMiddleware.RuntimeScopedRoutes"/> por una razón documentada (no un
    /// olvido). Cada entrada nueva aquí es una decisión explícita — igual que <see cref="NotTenantScopedRoutes"/>.
    /// </summary>
    private static readonly string[] HeaderReadingRoutesExcludedFromCoverage =
    [
        // Endpoints/Tramites/PublicProcedureTypeEndpoints.cs:28-40 — GET /api/v1/procedure-types exige
        // X-Tenant-Id PRESENTE (400 si falta) pero es un catálogo GLOBAL (comentario "AC-04" del propio
        // endpoint): el valor del tenant NUNCA se usa para filtrar resultados, solo se valida que venga.
        // No hay dato de ninguna compañía que fugar; agregarlo al middleware no cambiaría el
        // comportamiento del endpoint. Por diseño — no es el bug de esta HU.
        "/api/v1/procedure-types",
        // HALLAZGO (Bug #12558, fuera de alcance) — Endpoints/Tramites/OcrEndpoints.cs: estas 2 rutas
        // YA estaban congeladas como deuda en LegacyUncoveredRoutes (AC3, HU #12034: OCR del paso 1).
        // Verificado 2026-09-15: el [FromHeader] es parámetro MUERTO (los handlers no reciben el tenant),
        // así que no hay dato de ninguna compañía que fugar — queda como limpieza de código, no de
        // seguridad. // TODO Bug pendiente: cubrir con RuntimeScopedRoutes o retirar el parámetro
        // cuando se aborde HU #12034.
        // /api/v1/tramites/consultation-config (HU #10478) salió de aquí el 2026-09-15 al cubrirse en
        // RuntimeScopedRoutes (bug de registro diferido; prueba negativa en ConsultationConfigTenantScopeTests).
        "/api/v1/tramites/ocr/{tipo}",
        "/api/v1/tramites/ocr/lote",
    ];

    private static readonly Regex FromHeaderTenantIdPattern = new(
        @"\[FromHeader\(Name\s*=\s*""X-Tenant-Id""\)\]",
        RegexOptions.Compiled);

    private static readonly Regex MapGroupLiteralPattern = new(
        @"\bMapGroup\(\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex MapVerbLiteralPattern = new(
        @"\b(?:app|group)\.Map(?:Get|Post|Put|Patch|Delete)\(\s*""([^""]*)""",
        RegexOptions.Compiled);

    /// <summary>Referencia a un handler nombrado: <c>group.MapGet("/{scope}", GetAsync)</c> — el
    /// segundo argumento es un identificador simple, no una lambda inline.</summary>
    private static readonly Regex NamedHandlerCalleePattern = new(
        @",\s*([A-Za-z_]\w*)\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void AC9_TodaRutaQueLeeXTenantIdEstaCubiertaPorRuntimeScopedRoutes()
    {
        var apiDir = LocateFlitApiSourceDirectory();
        var files = Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var candidateRoutes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            if (!FromHeaderTenantIdPattern.IsMatch(content))
                continue;

            // A lo sumo un MapGroup relevante por archivo de endpoints (patrón del repo: `var group =
            // app.MapGroup("/prefijo")...` una sola vez, antes de cualquier Map* que lo use).
            var groupMatch = MapGroupLiteralPattern.Match(content);
            var groupPrefix = groupMatch.Success && groupMatch.Groups[1].Value.StartsWith('/')
                ? groupMatch.Groups[1].Value.TrimEnd('/')
                : string.Empty;

            var verbMatches = MapVerbLiteralPattern.Matches(content).Cast<Match>().ToList();
            for (var i = 0; i < verbMatches.Count; i++)
            {
                var suffix = verbMatches[i].Groups[1].Value;
                var combinedRoute = groupPrefix.Length > 0 && !suffix.StartsWith("/api", StringComparison.Ordinal)
                    ? groupPrefix + suffix
                    : suffix;

                var start = verbMatches[i].Index;
                var end = i + 1 < verbMatches.Count ? verbMatches[i + 1].Index : content.Length;
                var segment = content[start..end];

                if (FromHeaderTenantIdPattern.IsMatch(segment))
                {
                    // Lambda inline: la lectura del header está en el propio bloque de esta ruta.
                    candidateRoutes.Add(combinedRoute);
                    continue;
                }

                // Handler nombrado (p.ej. group.MapGet("/{scope}", GetAsync)): la lectura puede vivir en
                // un método aparte del archivo — se localiza su firma y se revisa AHÍ.
                var callee = NamedHandlerCalleePattern.Match(segment);
                if (!callee.Success)
                    continue;

                var methodName = callee.Groups[1].Value;
                var declPattern = new Regex(
                    $@"static\s+async\s+Task<IResult>\s+{Regex.Escape(methodName)}\s*\(([\s\S]*?)\)\s*\r?\n\s*\{{",
                    RegexOptions.Compiled);
                var decl = declPattern.Match(content);
                if (decl.Success && FromHeaderTenantIdPattern.IsMatch(decl.Value))
                    candidateRoutes.Add(combinedRoute);
            }
        }

        candidateRoutes.Should().NotBeEmpty(
            "debe existir al menos un endpoint que lea [FromHeader(Name = \"X-Tenant-Id\")] en Flit.Api");

        var uncovered = candidateRoutes
            .Where(route => !IsCoveredByMiddleware(route)
                && !HeaderReadingRoutesExcludedFromCoverage.Contains(route, StringComparer.OrdinalIgnoreCase))
            .ToList();

        uncovered.Should().BeEmpty(
            "toda ruta que lea [FromHeader(Name = \"X-Tenant-Id\")] debe estar en "
            + "TenantEnforcementMiddleware.RuntimeScopedRoutes o declararse explícitamente en "
            + "HeaderReadingRoutesExcludedFromCoverage (Bug #12558). Rutas sin declarar: {0}",
            string.Join(", ", uncovered));
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

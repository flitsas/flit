using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Middleware;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// Épica #13216 (HU #13374, ADR-0070) — contrato HTTP de <c>POST /api/v1/tramites/consolidados/lotes</c>: permiso
/// <c>consolidado-masivo.download</c>, códigos estables (409/422/503 con <c>error</c> en la raíz del ProblemDetails),
/// tenant del JWT impuesto por <see cref="TenantEnforcementMiddleware"/> (AC5) y origen decidido por el servidor (AC6).
/// Host real sin PostgreSQL: repositorios y cifrador sustituidos (mismo patrón que <see cref="AdminTramitesTenantScopeTests"/>).
/// <para>Uso de ejemplo: <c>POST /api/v1/tramites/consolidados/lotes { tipoDocumento: "consolidado", confirmaEfectos: true,
/// seleccion: { modo: "ids", ids: [..], excluidos: [], filtro: null } }</c> ⇒ <c>202 LoteConsolidados</c> en <c>en_cola</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteEndpointTests : IClassFixture<ConsolidadoLoteEndpointTests.LoteFactory>
{
    private const string Ruta = "/api/v1/tramites/consolidados/lotes";
    private const string Permiso = "consolidado-masivo.download";

    private static readonly Guid TenantC = Guid.Parse("c0000000-0000-4000-8000-0000000013c3");
    private static readonly Guid TenantB = Guid.Parse("c0000000-0000-4000-8000-0000000013b2");
    private static readonly Guid UsuarioId = Guid.Parse("13374000-0000-4000-8000-000000000001");

    private readonly LoteFactory _factory;

    public ConsolidadoLoteEndpointTests(LoteFactory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    // ── AC1 — creación por API ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ConPermiso_ModoIdsConLasCuatroClaves_202_EnCola_TotalCongelado()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _factory.Instancias.ListIdsFilteredAsync(TenantC, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(a, TenantC, "TRM-1", "ABC123"), new ProcedureInstanceRef(b, TenantC, "TRM-2", null)]);

        var response = await Cliente(Gestor()).PostAsync(Ruta, Json(new
        {
            tipoDocumento = "consolidado",
            confirmaEfectos = true,
            seleccion = new { modo = "ids", ids = new[] { a, b }, excluidos = Array.Empty<Guid>(), filtro = (object?)null },
        }), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, body);
        response.Headers.Location!.ToString().Should().StartWith("/api/v1/consolidados/lotes/");
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("estado").GetString().Should().Be("en_cola");
        root.GetProperty("total").GetInt32().Should().Be(2);
        root.GetProperty("tipoDocumento").GetString().Should().Be("consolidado");
        root.GetProperty("procesados").GetInt32().Should().Be(0);
        root.GetProperty("partes").GetArrayLength().Should().Be(0);
        root.GetProperty("id").GetGuid().Should().NotBeEmpty();

        var nuevo = _factory.UltimoNuevo!;
        nuevo.Items.Select(i => i.Id).Should().Equal(a, b);
        nuevo.ModoSeleccion.Should().Be(ConsolidadoExportSelectionMode.Ids);
        nuevo.UsuarioId.Should().Be(UsuarioId);
        nuevo.RolCodigo.Should().Be("Radicador");
        nuevo.UserAgent.Should().Be("flit-tests/13374");
    }

    [Fact]
    public async Task AC1_ModoFiltroConIdsVaciosYAlcanceRed_202_ResuelveConElFiltroDelListado()
    {
        var ct = TestContext.Current.CancellationToken;
        var x = Guid.NewGuid();
        var excluido = Guid.NewGuid();
        _factory.Instancias.ListIdsFilteredAsync(TenantC, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(x, TenantC, "TRM-3", "XYZ987"), new ProcedureInstanceRef(excluido, TenantC, "TRM-4", null)]);

        var response = await Cliente(Gestor()).PostAsync(Ruta, Json(new
        {
            tipoDocumento = "consolidado",
            confirmaEfectos = true,
            seleccion = new
            {
                modo = "filtro",
                ids = Array.Empty<Guid>(),
                excluidos = new[] { excluido },
                filtro = new { placa = "XYZ", estado = "entregado", skip = 0, take = 50, alcanceRed = "red" },
            },
        }), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        _factory.UltimoNuevo!.Items.Select(i => i.Id).Should().Equal(x);
        _factory.UltimoNuevo.ModoSeleccion.Should().Be(ConsolidadoExportSelectionMode.Filtro);
        await _factory.Instancias.Received(1).ListIdsFilteredAsync(TenantC,
            Arg.Is<ProcedureInstanceListFilter>(f => f.Placa == "XYZ"),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — sin permiso ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_SinPermiso_403_YNoSeCreaLote()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Cliente(Gestor(permisos: [])).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().ObtenerSettingsAsync(default);
    }

    [Fact]
    public async Task AC2_SinToken_401()
    {
        var response = await _factory.CreateClient().PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC3 — códigos estables ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_LoteActivo_409_ConErrorYLoteActivoIdEnLaRaiz()
    {
        var ct = TestContext.Current.CancellationToken;
        var activo = Guid.NewGuid();
        _factory.Lotes.ObtenerLoteActivoIdAsync(UsuarioId, Arg.Any<CancellationToken>()).Returns(activo);

        var response = await Cliente(Gestor()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var root = await Raiz(response, ct);
        root.GetProperty("error").GetString().Should().Be("lote_activo");
        root.GetProperty("loteActivoId").GetGuid().Should().Be(activo);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task AC3_MasDe10000Ids_422_SeleccionExcedeTope()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = Enumerable.Range(0, LoteSeleccionTopes.MaxIds + 1).Select(_ => Guid.NewGuid()).ToArray();

        var response = await Cliente(Gestor()).PostAsync(Ruta, CuerpoIds(ids), ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Raiz(response, ct)).GetProperty("error").GetString().Should().Be("seleccion_excede_tope");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task AC3_MasDe10000Excluidos_422_SeleccionExcedeTope()
    {
        var ct = TestContext.Current.CancellationToken;
        var excluidos = Enumerable.Range(0, LoteSeleccionTopes.MaxExcluidos + 1).Select(_ => Guid.NewGuid()).ToArray();

        var response = await Cliente(Gestor()).PostAsync(Ruta, Json(new
        {
            tipoDocumento = "consolidado",
            confirmaEfectos = true,
            seleccion = new { modo = "filtro", ids = Array.Empty<Guid>(), excluidos, filtro = new { } },
        }), ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Raiz(response, ct)).GetProperty("error").GetString().Should().Be("seleccion_excede_tope");
    }

    [Fact]
    public async Task AC3_AuditoriaNoRegistrada_503_LoteNoCreado()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.ResultadoCrear = new CrearLoteResultado(CrearLoteEstado.NoCreado);

        var response = await Cliente(Gestor()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var root = await Raiz(response, ct);
        root.GetProperty("error").GetString().Should().Be("lote_no_creado");
        root.GetProperty("detail").GetString().Should().Be(CrearLoteConsolidadosHandler.MensajeNoDisponible);
    }

    [Fact]
    public async Task AC3_MotorInactivo_503_MotorInactivo_SinCrear()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Lotes.ObtenerSettingsAsync(Arg.Any<CancellationToken>()).Returns(new ConsolidadoExportSettings { IsActive = false });

        var response = await Cliente(Gestor()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Raiz(response, ct)).GetProperty("error").GetString().Should().Be("motor_inactivo");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Theory]
    [InlineData("{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":false,\"seleccion\":{\"modo\":\"ids\",\"ids\":[]}}", "confirmacion_requerida")]
    [InlineData("{\"tipoDocumento\":\"consolidado_maestro\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"ids\",\"ids\":[]}}", "tipo_no_permitido")]
    [InlineData("{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true}", "seleccion_requerida")]
    [InlineData("{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"todos\",\"ids\":[]}}", "seleccion_invalida")]
    [InlineData("{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"ids\":[],\"excluidos\":[],\"filtro\":null}}", "seleccion_invalida")]
    [InlineData("{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"filtro\":{\"condiciones\":[{\"fieldId\":\"no_existe\",\"operator\":\"eq\",\"values\":[\"x\"]}]}}}", "filtro_invalido")]
    [InlineData("{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"filtro\":{\"busquedaRapida\":\"no_existe\"}}}", "filtro_invalido")]
    public async Task AC3_CuerpoInvalido_400_ConCodigoEstable(string cuerpo, string codigo)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Cliente(Gestor()).PostAsync(Ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(ct));
        (await Raiz(response, ct)).GetProperty("error").GetString().Should().Be(codigo);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task AC3_BusquedaRapidaDemasiadoAmplia_422_ConCodigoDistintoDelTope()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Instancias.ListWithSummaryGraphFilteredAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<ProcedureInstance>(), 5_000));

        var response = await Cliente(Gestor()).PostAsync(Ruta, new StringContent(
            "{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"ids\":[],\"excluidos\":[],\"filtro\":{\"busquedaRapida\":\"sin_firmas\"}}}",
            Encoding.UTF8, "application/json"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Raiz(response, ct)).GetProperty("error").GetString().Should().Be("busqueda_demasiado_amplia");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC5 — la ruta está en el middleware de tenant (bloqueante de seguridad) ────────────

    [Fact]
    public void AC5_LaRutaDelLoteEstaEnRuntimeScopedRoutesComoPrefijo()
    {
        TenantEnforcementMiddleware.RuntimeScopedRoutes.Should().ContainEquivalentOf(
            new TenantEnforcementMiddleware.RuntimeScopedRoute("/api/v1/tramites/consolidados", TenantEnforcementMiddleware.RouteMatch.Prefix));
        TenantEnforcementMiddleware.IsRuntimeScoped(new PathString(Ruta)).Should().BeTrue();
        TenantEnforcementMiddleware.IsRuntimeScoped(new PathString(Ruta + "/")).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC5_GestorDeC_ConXTenantIdPropioOAjeno_ElLoteSoloResuelveTramitesDeC(bool cabeceraPropia)
    {
        var ct = TestContext.Current.CancellationToken;
        var deC = Guid.NewGuid();
        var deB = Guid.NewGuid();
        _factory.Instancias.ListIdsFilteredAsync(TenantC, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(deC, TenantC, "TRM-C", null)]);
        _factory.Instancias.ListIdsFilteredAsync(TenantB, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(deB, TenantB, "TRM-B", null)]);

        var client = Cliente(Gestor());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", (cabeceraPropia ? TenantC : TenantB).ToString());
        var response = await client.PostAsync(Ruta, CuerpoIds(deC, deB), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        _factory.UltimoNuevo!.TenantId.Should().Be(TenantC);
        _factory.UltimoNuevo.Origen.Should().Be(ConsolidadoExportOrigin.Tramites);
        _factory.UltimoNuevo.Items.Should().OnlyContain(i => i.TenantId == TenantC);
        await _factory.Instancias.DidNotReceive().ListIdsFilteredAsync(TenantB, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>());
    }

    // ── AC6 — el origen lo decide el servidor ─────────────────────────────────────────────

    [Theory]
    [InlineData("origen")]
    [InlineData("origin")]
    public async Task AC6_GestorQueMandaOrigenSuperadminEnElCuerpo_ElLoteSeCreaConOrigenTramites(string campo)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _factory.Instancias.ListIdsFilteredAsync(TenantC, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(id, TenantC, "TRM-6", null)]);

        var cuerpo = $"{{\"{campo}\":\"superadmin\",\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true," +
                     $"\"seleccion\":{{\"modo\":\"ids\",\"ids\":[\"{id}\"],\"excluidos\":[],\"filtro\":null}}}}";
        var response = await Cliente(Gestor()).PostAsync(Ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        _factory.UltimoNuevo!.Origen.Should().Be(ConsolidadoExportOrigin.Tramites);
        _factory.UltimoNuevo.TenantId.Should().Be(TenantC);
        _factory.UltimoNuevo.ScopeTenantId.Should().BeNull();
    }

    [Fact]
    public async Task AC6_SuperAdminSinPermisoExplicito_ConXTenantId_OrigenSuperadmin_SinCompania_ScopeDeLaCabecera()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = Cliente(SuperAdmin());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString());

        var response = await client.PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        var nuevo = _factory.UltimoNuevo!;
        nuevo.Origen.Should().Be(ConsolidadoExportOrigin.Superadmin);
        nuevo.TenantId.Should().BeNull();
        nuevo.ScopeTenantId.Should().Be(TenantB);
        nuevo.RolCodigo.Should().Be("SuperAdmin");
        _factory.ResolverSuperAdmin.UltimoContexto!.TenantId.Should().Be(TenantB);
    }

    [Fact]
    public async Task AC6_SuperAdminSinResolverDelOrigen_503_MotorInactivo_SinCrear()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sinResolver = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILoteSeleccionResolver>();
            services.AddScoped<ILoteSeleccionResolver, TramitesSeleccionResolver>();
        }));
        var client = sinResolver.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SuperAdmin());

        var response = await client.PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Raiz(response, ct)).GetProperty("error").GetString().Should().Be("motor_inactivo");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("flit-tests/13374");
        return client;
    }

    private static StringContent CuerpoIds(params Guid[] ids) => Json(new
    {
        tipoDocumento = "consolidado",
        confirmaEfectos = true,
        seleccion = new { modo = "ids", ids, excluidos = Array.Empty<Guid>(), filtro = (object?)null },
    });

    private static StringContent Json(object cuerpo) =>
        new(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> Raiz(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Gestor(string[]? permisos = null) =>
        Token(TenantC, "Radicador", permisos ?? [Permiso]);

    /// <summary>Super Admin SIN el claim del permiso: pasa por el bypass de <c>RequirePermission</c>.</summary>
    private static string SuperAdmin() => Token(TenantC, "SuperAdmin", []);

    private static string Token(Guid tenantId, string role, string[] permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", UsuarioId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));

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

    /// <summary>
    /// Host con <see cref="IConsolidadoLoteRepository"/>, <see cref="IProcedureInstanceRepository"/> e
    /// <see cref="IConsolidadoLoteCipher"/> sustituidos. Los resolvers de selección se fijan a
    /// <see cref="TramitesSeleccionResolver"/> real + un resolver falso del origen <c>superadmin</c> (lo aporta
    /// #13383; aquí solo se comprueba que el servidor lo elige).
    /// </summary>
    public sealed class LoteFactory : WebApplicationFactory<Program>
    {
        public IConsolidadoLoteRepository Lotes { get; private set; } = Substitute.For<IConsolidadoLoteRepository>();
        public IProcedureInstanceRepository Instancias { get; private set; } = Substitute.For<IProcedureInstanceRepository>();
        public IConsolidadoLoteCipher Cipher { get; } = Substitute.For<IConsolidadoLoteCipher>();
        public ResolverSuperAdminFalso ResolverSuperAdmin { get; } = new();
        public NuevoLoteConsolidados? UltimoNuevo { get; private set; }
        public CrearLoteResultado? ResultadoCrear { get; set; }

        public LoteFactory() => Reiniciar();

        public void Reiniciar()
        {
            Lotes.ClearReceivedCalls();
            Instancias.ClearReceivedCalls();
            Lotes.ClearSubstitute();
            Instancias.ClearSubstitute();
            UltimoNuevo = null;
            ResultadoCrear = null;
            ResolverSuperAdmin.UltimoContexto = null;

            Cipher.GenerarDekEnvuelta().Returns([1, 2, 3]);
            Lotes.ObtenerSettingsAsync(Arg.Any<CancellationToken>()).Returns(new ConsolidadoExportSettings { IsActive = true });
            Lotes.ObtenerLoteActivoIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
            Lotes.CrearAsync(Arg.Any<NuevoLoteConsolidados>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                var nuevo = ci.Arg<NuevoLoteConsolidados>();
                UltimoNuevo = nuevo;
                return ResultadoCrear ?? new CrearLoteResultado(CrearLoteEstado.Creado, new ConsolidadoExportBatch
                {
                    Id = Guid.NewGuid(),
                    TenantId = nuevo.TenantId,
                    RequestedByUserId = nuevo.UsuarioId,
                    RequestedRoleCode = nuevo.RolCodigo,
                    ScopeTenantId = nuevo.ScopeTenantId,
                    Origin = nuevo.Origen,
                    DocumentType = nuevo.TipoDocumento,
                    SelectionMode = nuevo.ModoSeleccion,
                    Status = ConsolidadoExportStatus.EnCola,
                    TotalItems = nuevo.Items.Count,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            });
            Instancias.ListIdsFilteredAsync(Arg.Any<Guid?>(), Arg.Any<ProcedureInstanceListFilter>(),
                    Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<ProcedureInstanceRef>());
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Lotes);
                services.AddScoped(_ => Instancias);
                services.AddSingleton(Cipher);
                services.AddScoped<ITenantScopeResolver>(_ => new SingleScope());
                services.RemoveAll<ILoteSeleccionResolver>();
                services.AddScoped<ILoteSeleccionResolver, TramitesSeleccionResolver>();
                services.AddScoped<ILoteSeleccionResolver>(_ => ResolverSuperAdmin);
            });
        }
    }

    /// <summary>Resolver del origen <c>superadmin</c> de prueba: registra el contexto y devuelve un trámite del scope.</summary>
    public sealed class ResolverSuperAdminFalso : ILoteSeleccionResolver
    {
        public LoteSeleccionContexto? UltimoContexto { get; set; }

        public string Origen => ConsolidadoExportOrigin.Superadmin;

        public Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
            LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default)
        {
            UltimoContexto = contexto;
            var tenant = contexto.TenantId ?? Guid.NewGuid();
            return Task.FromResult<IReadOnlyList<ProcedureInstanceRef>>([new ProcedureInstanceRef(Guid.NewGuid(), tenant, "TRM-SA", null)]);
        }
    }

    private sealed class SingleScope : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }
}

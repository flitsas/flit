using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
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
/// HU #13417 (épica #13216, ADR-0070 adenda v7) — <c>POST /api/v1/tramites/consolidados/lotes</c> con
/// <c>alcanceRed</c> en la raíz del cuerpo: el alcance sale del <see cref="TenantScope"/> que resuelve el middleware
/// desde la BD (aquí, un <see cref="ITenantScopeResolver"/> de prueba), las puertas de red responden 403 con su código
/// estable y sin crear nada, y <c>LoteConsolidados.alcanceRed</c> se devuelve de solo lectura. Host real sin PostgreSQL.
/// <para>Uso de ejemplo: <c>POST … { tipoDocumento: "consolidado", confirmaEfectos: true, alcanceRed: "red",
/// seleccion: { modo: "filtro", ids: [], excluidos: [], filtro: { … } } }</c> ⇒ <c>202 { alcanceRed: "red" }</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteRedEndpointTests : IClassFixture<ConsolidadoLoteRedEndpointTests.RedFactory>
{
    private const string Ruta = "/api/v1/tramites/consolidados/lotes";
    private const string Permiso = "consolidado-masivo.download";

    internal static readonly Guid P = Guid.Parse("c0000000-0000-4000-8000-0000000134a1");
    internal static readonly Guid C1 = Guid.Parse("c0000000-0000-4000-8000-0000000134c1");
    internal static readonly Guid C2 = Guid.Parse("c0000000-0000-4000-8000-0000000134c2");
    internal static readonly Guid Sola = Guid.Parse("c0000000-0000-4000-8000-0000000134f5");
    private static readonly Guid X = Guid.Parse("c0000000-0000-4000-8000-0000000134f9");
    private static readonly Guid UsuarioId = Guid.Parse("13417000-0000-4000-8000-000000000001");

    private readonly RedFactory _factory;

    public ConsolidadoLoteRedEndpointTests(RedFactory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 / AC2 / AC3 — lote de red ─────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_TodaLaRedEnModoFiltro_202_ConElAlcanceDeGrupo_YAlcanceRedRed()
    {
        var refs = new[] { Ref(P), Ref(C1), Ref(C2) };
        _factory.EnRedDevuelve(refs);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo("red", Filtro()), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("alcanceRed").GetString().Should().Be("red");
        json.RootElement.GetProperty("total").GetInt32().Should().Be(3);

        var nuevo = _factory.UltimoNuevo!;
        nuevo.TenantId.Should().Be(P);
        nuevo.Origen.Should().Be(ConsolidadoExportOrigin.Tramites);
        nuevo.NetworkScope.Should().BeTrue();
        nuevo.ScopeTenantId.Should().BeNull();
        nuevo.Items.Select(i => i.TenantId).Should().Equal(P, C1, C2);
        nuevo.ResumenFiltroJson.Should().Contain("\"alcanceRed\":\"red\"");
        await _factory.Instancias.Received(1).ListIdsFilteredInScopeAsync(
            Arg.Is<TenantScope>(s => s.IsGroup && s.ReadTenantIds.SetEquals(new[] { P, C1, C2 })),
            Arg.Is<ProcedureInstanceListFilter>(f => f.Placa == "XYZ"),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _factory.Instancias.DidNotReceiveWithAnyArgs().ListIdsFilteredAsync(default, default!, default, default, default, default);
    }

    [Fact]
    public async Task AC2_UnaHija_202_ScopeEsLaHija_YAlcanceRedHija()
    {
        _factory.EnRedDevuelve([Ref(C1), Ref(C1)]);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo(C1.ToString(), Filtro()), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, body);
        JsonDocument.Parse(body).RootElement.GetProperty("alcanceRed").GetString().Should().Be("hija");
        body.Should().NotContain(C1.ToString(), "la respuesta no expone la hija");
        _factory.UltimoNuevo!.ScopeTenantId.Should().Be(C1);
        _factory.UltimoNuevo.ResumenFiltroJson.Should().Contain("\"alcanceRed\":\"hija\"").And.NotContain(C1.ToString());
        await _factory.Instancias.Received(1).ListIdsFilteredInScopeAsync(
            Arg.Is<TenantScope>(s => !s.IsGroup && s.ReadTenantIds.SetEquals(new[] { C1 })), Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_CasillasAManoConAlcanceEnLaRaiz_202_EntranCabezaEHija()
    {
        var a = Ref(P);
        var b = Ref(C1);
        _factory.EnRedDevuelve([a, b]);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo("red", null, a.Id, b.Id), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        _factory.UltimoNuevo!.Items.Should().Equal(a, b);
        _factory.UltimoNuevo.ModoSeleccion.Should().Be(ConsolidadoExportSelectionMode.Ids);
    }

    [Fact]
    public async Task AC5_IdInyectadoDeCompaniaAjena_NoEntraAunqueLaConsultaLoDevolviera()
    {
        var propio = Ref(P);
        var ajeno = Ref(X);
        _factory.EnRedDevuelve([propio, ajeno]);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo("red", null, propio.Id, ajeno.Id), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        _factory.UltimoNuevo!.Items.Should().Equal(propio);
    }

    [Fact]
    public async Task AC9_Transicion_ElAlcanceSoloEnElFiltroSeAcepta()
    {
        _factory.EnRedDevuelve([Ref(C2)]);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo(null, Filtro(alcanceRed: "red")), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        _factory.UltimoNuevo!.NetworkScope.Should().BeTrue();
    }

    // ── AC4 / AC6 / AC7 / AC8 — puertas de red (403, sin lote) ─────────────────────────────

    [Fact]
    public async Task AC4_HijaFueraDeLaRed_403_NetworkChildOutOfScope()
    {
        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo(X.ToString(), Filtro()), Ct);

        await EsperaProblema(response, HttpStatusCode.Forbidden, "network_child_out_of_scope");
        await NoSeCreoNada();
    }

    [Fact]
    public async Task AC6_RadicadorDeLaCabeza_403_NetworkRoleRequired()
    {
        var response = await Cliente(Token(P, "Radicador", [Permiso])).PostAsync(Ruta, Cuerpo("red", Filtro()), Ct);

        await EsperaProblema(response, HttpStatusCode.Forbidden, "network_role_required");
        await NoSeCreoNada();
    }

    [Fact]
    public async Task AC7_ClienteSinRed_403_NetworkScopeRequired()
    {
        var response = await Cliente(Token(Sola, "AdminCompany", [Permiso])).PostAsync(Ruta, Cuerpo("red", Filtro()), Ct);

        await EsperaProblema(response, HttpStatusCode.Forbidden, "network_scope_required");
        await NoSeCreoNada();
    }

    [Fact]
    public async Task AC7_SuperAdminConAlcanceRed_403_NetworkScopeRequired()
    {
        var response = await Cliente(Token(P, "SuperAdmin", [])).PostAsync(Ruta, Cuerpo(C1.ToString(), Filtro()), Ct);

        await EsperaProblema(response, HttpStatusCode.Forbidden, "network_scope_required");
        await NoSeCreoNada();
        _factory.ResolverSuperAdmin.UltimoContexto.Should().BeNull();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("hija")]
    public async Task AC8_CabezaConcesionConDocumentosDeRedApagados_403_NetworkDocumentsDisabled(string alcance)
    {
        _factory.Clase = GroupKind.Concesion;
        _factory.DocumentosConcesion = false;

        var response = await Cliente(Admin()).PostAsync(
            Ruta, Cuerpo(alcance == "red" ? "red" : C1.ToString(), Filtro()), Ct);

        await EsperaProblema(response, HttpStatusCode.Forbidden, "network_documents_disabled");
        await NoSeCreoNada();
    }

    [Fact]
    public async Task AC8_CabezaConcesionConDocumentosDeRedEncendidos_202()
    {
        _factory.Clase = GroupKind.Concesion;
        _factory.DocumentosConcesion = true;
        _factory.EnRedDevuelve([Ref(C1)]);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo("red", Filtro()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
    }

    // ── AC9 — alcance inválido o contradictorio (400) ─────────────────────────────────────

    [Theory]
    [InlineData("todos", null)]
    [InlineData("00000000-0000-0000-0000-000000000000", null)]
    [InlineData("red", "c0000000-0000-4000-8000-0000000134c1")]
    public async Task AC9_AlcanceInvalidoOContradictorio_400_SeleccionInvalida(string raiz, string? filtro)
    {
        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo(raiz, Filtro(alcanceRed: filtro)), Ct);

        await EsperaProblema(response, HttpStatusCode.BadRequest, "seleccion_invalida");
        await NoSeCreoNada();
    }

    // ── AC10 — sin alcance de red no cambia nada ──────────────────────────────────────────

    [Fact]
    public async Task AC10_SinAlcance_ElLoteEsElPropio_YLaRespuestaNoTraeAlcanceRed()
    {
        _factory.Instancias.ListIdsFilteredAsync(P, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([Ref(P)]);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo(null, Filtro()), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, body);
        JsonDocument.Parse(body).RootElement.TryGetProperty("alcanceRed", out _).Should().BeFalse();
        _factory.UltimoNuevo!.NetworkScope.Should().BeFalse();
        await _factory.Instancias.DidNotReceiveWithAnyArgs().ListIdsFilteredInScopeAsync(default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task UuidDeLaPropiaCabeza_SeNormalizaAPropio()
    {
        _factory.Instancias.ListIdsFilteredAsync(P, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([Ref(P)]);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo(P.ToString(), Filtro()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        _factory.UltimoNuevo!.NetworkScope.Should().BeFalse();
        _factory.UltimoNuevo.ScopeTenantId.Should().BeNull();
    }

    // ── AC11 — el tope total aplica a la red ──────────────────────────────────────────────

    [Fact]
    public async Task AC11_LaSeleccionDeRedSuperaElTope_422_ConTotalYTope()
    {
        _factory.Tope = 2;
        _factory.EnRedDevuelve([Ref(P), Ref(C1), Ref(C2), Ref(C2)]);
        _factory.Instancias.CountIdsFilteredInScopeAsync(Arg.Any<TenantScope>(), Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>())
            .Returns(7);

        var response = await Cliente(Admin()).PostAsync(Ruta, Cuerpo("red", Filtro()), Ct);

        var raiz = await EsperaProblema(response, HttpStatusCode.UnprocessableEntity, "seleccion_excede_tope");
        raiz.GetProperty("total").GetInt32().Should().Be(7);
        raiz.GetProperty("tope").GetInt32().Should().Be(2);
        await NoSeCreoNada();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static ProcedureInstanceRef Ref(Guid tenant) => new(Guid.NewGuid(), tenant, "TRM", null);

    private static object Filtro(string? alcanceRed = null) =>
        new { placa = "XYZ", estado = "entregado", skip = 0, take = 50, alcanceRed };

    private static StringContent Cuerpo(string? alcanceRed, object? filtro, params Guid[] ids) =>
        new(JsonSerializer.Serialize(new
        {
            tipoDocumento = "consolidado",
            confirmaEfectos = true,
            alcanceRed,
            seleccion = new
            {
                modo = ids.Length > 0 ? "ids" : "filtro",
                ids,
                excluidos = Array.Empty<Guid>(),
                filtro,
            },
        }), Encoding.UTF8, "application/json");

    private async Task NoSeCreoNada() =>
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);

    private static async Task<JsonElement> EsperaProblema(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(status, body);
        var raiz = JsonDocument.Parse(body).RootElement.Clone();
        raiz.GetProperty("error").GetString().Should().Be(error);
        raiz.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace().And.NotContain(error);
        return raiz;
    }

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Admin() => Token(P, "AdminCompany", [Permiso]);

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
    /// Host con repositorios, cifrador e interruptores sustituidos y un resolver de alcance de prueba: la cabeza
    /// <see cref="P"/> es grupo {P, C1, C2} de clase <see cref="Clase"/>; cualquier otro tenant es <c>Single</c>.
    /// </summary>
    public sealed class RedFactory : WebApplicationFactory<Program>
    {
        public IConsolidadoLoteRepository Lotes { get; } = Substitute.For<IConsolidadoLoteRepository>();
        public IProcedureInstanceRepository Instancias { get; } = Substitute.For<IProcedureInstanceRepository>();
        public IConsolidadoLoteCipher Cipher { get; } = Substitute.For<IConsolidadoLoteCipher>();
        public IHierarchySwitches Switches { get; } = Substitute.For<IHierarchySwitches>();
        public ConsolidadoLoteEndpointTests.ResolverSuperAdminFalso ResolverSuperAdmin { get; } = new();
        public NuevoLoteConsolidados? UltimoNuevo { get; private set; }
        public GroupKind Clase { get; set; } = GroupKind.MarcaBlanca;
        public bool DocumentosConcesion { get; set; }
        public int Tope { get; set; } = 10_000;

        public RedFactory() => Reiniciar();

        public void Reiniciar()
        {
            Lotes.ClearSubstitute();
            Instancias.ClearSubstitute();
            Switches.ClearSubstitute();
            UltimoNuevo = null;
            Clase = GroupKind.MarcaBlanca;
            DocumentosConcesion = false;
            Tope = 10_000;
            ResolverSuperAdmin.UltimoContexto = null;

            Cipher.GenerarDekEnvuelta().Returns([1, 2, 3]);
            Switches.IsNetworkDocumentsConcesionEnabledAsync(Arg.Any<CancellationToken>()).Returns(_ => DocumentosConcesion);
            Lotes.ObtenerSettingsAsync(Arg.Any<CancellationToken>())
                .Returns(_ => new ConsolidadoExportSettings { IsActive = true, MaxItemsPerBatch = Tope });
            Lotes.ObtenerLoteActivoIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
            Lotes.CrearAsync(Arg.Any<NuevoLoteConsolidados>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                var nuevo = ci.Arg<NuevoLoteConsolidados>();
                UltimoNuevo = nuevo;
                return new CrearLoteResultado(CrearLoteEstado.Creado, new ConsolidadoExportBatch
                {
                    Id = Guid.NewGuid(),
                    TenantId = nuevo.TenantId,
                    RequestedByUserId = nuevo.UsuarioId,
                    RequestedRoleCode = nuevo.RolCodigo,
                    ScopeTenantId = nuevo.ScopeTenantId,
                    NetworkScope = nuevo.NetworkScope,
                    Origin = nuevo.Origen,
                    DocumentType = nuevo.TipoDocumento,
                    SelectionMode = nuevo.ModoSeleccion,
                    Status = ConsolidadoExportStatus.EnCola,
                    TotalItems = nuevo.Items.Count,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            });
            Instancias.ListIdsFilteredAsync(Arg.Any<Guid?>(), Arg.Any<ProcedureInstanceListFilter>(),
                    Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<ProcedureInstanceRef>());
        }

        /// <summary>La consulta de red devuelve <paramref name="refs"/> (prefijo hasta el límite, como el LIMIT).</summary>
        public void EnRedDevuelve(IReadOnlyList<ProcedureInstanceRef> refs) =>
            Instancias.ListIdsFilteredInScopeAsync(Arg.Any<TenantScope>(), Arg.Any<ProcedureInstanceListFilter>(),
                    Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.ArgAt<int?>(4) is { } max && refs.Count > max ? refs.Take(max).ToList() : refs);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Lotes);
                services.AddScoped(_ => Instancias);
                services.AddSingleton(Cipher);
                services.RemoveAll<IHierarchySwitches>();
                services.AddSingleton(Switches);
                services.AddScoped<ITenantScopeResolver>(_ => new ResolverDeRed(this));
                services.RemoveAll<ILoteSeleccionResolver>();
                services.AddScoped<ILoteSeleccionResolver, TramitesSeleccionResolver>();
                services.AddScoped<ILoteSeleccionResolver>(_ => ResolverSuperAdmin);
            });
        }
    }

    private sealed class ResolverDeRed(RedFactory factory) : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == P ? TenantScope.Group(P, [C1, C2], factory.Clase) : TenantScope.Single(tenantId));
    }
}

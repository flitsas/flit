using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Endpoints.Tramites;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13383 (Feature #13307, épica #13216, ADR-0070 A4.1/A4.6) — <c>POST /api/v1/tramites/consolidados/lotes</c> del
/// Super Admin con los resolvers de selección REALES del contenedor (incluido el del origen <c>superadmin</c>), y los
/// negativos de origen, permiso y compañía. Repositorios y cifrador sustituidos (la persistencia real la cubre
/// <c>CrearLoteSuperAdminTests</c> en <c>Flit.Integration.Tests</c>).
/// <para>Uso de ejemplo: un token con rol <c>SuperAdmin</c> y sin el claim <c>consolidado-masivo.download</c> ⇒ <c>202</c>,
/// lote <c>origin = superadmin</c>, <c>tenant_id = null</c>.</para>
/// </summary>
public sealed class CrearLoteSuperAdminEndpointTests : IClassFixture<CrearLoteSuperAdminEndpointTests.Factory>
{
    private const string Ruta = "/api/v1/tramites/consolidados/lotes";
    private const string Permiso = "consolidado-masivo.download";

    private static readonly Guid TenantA = Guid.Parse("c0000000-0000-4000-8000-0000000133a1");
    private static readonly Guid TenantB = Guid.Parse("c0000000-0000-4000-8000-0000000133b2");
    private static readonly Guid TenantC = Guid.Parse("c0000000-0000-4000-8000-0000000133c3");
    private static readonly Guid UsuarioId = Guid.Parse("13383000-0000-4000-8000-000000000001");

    private readonly Factory _factory;

    public CrearLoteSuperAdminEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 / AC7 — resolver real del origen superadmin + bypass del permiso ──────────────

    [Fact]
    public async Task AC7_SuperAdminSinElClaim_202_ConElResolverRealDelOrigen_SinCompania_ItemsDeAyB()
    {
        var deA = Guid.NewGuid();
        var deB = Guid.NewGuid();
        _factory.Instancias.ListIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(deA, TenantA, "R-A", null), new ProcedureInstanceRef(deB, TenantB, "R-B", null)]);

        var response = await Cliente(SuperAdmin()).PostAsync(Ruta, CuerpoIds(deA, deB), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var nuevo = _factory.UltimoNuevo!;
        nuevo.Origen.Should().Be(ConsolidadoExportOrigin.Superadmin);
        nuevo.TenantId.Should().BeNull();
        nuevo.ScopeTenantId.Should().BeNull();
        nuevo.RolCodigo.Should().Be("SuperAdmin");
        nuevo.Items.Select(i => (i.Id, i.TenantId)).Should().Equal((deA, TenantA), (deB, TenantB));
        await _factory.Instancias.Received(1).ListIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC7_NoSuperAdminSinElPermiso_403_SinCrear()
    {
        var response = await Cliente(Gestor(permisos: [])).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC3 — scope por cabecera ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_SuperAdminConXTenantIdB_ElResolverRealConsultaSoloB_YElDeANoEntra()
    {
        var deA = Guid.NewGuid();
        var deB = Guid.NewGuid();
        // Si el repositorio (mal) devolviera uno de A, la defensa del handler lo descarta igual.
        _factory.Instancias.ListIdsFilteredAsync(TenantB, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(deA, TenantA, "R-A", null), new ProcedureInstanceRef(deB, TenantB, "R-B", null)]);
        var client = Cliente(SuperAdmin());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString());

        var response = await client.PostAsync(Ruta, CuerpoIds(deA, deB), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var nuevo = _factory.UltimoNuevo!;
        nuevo.ScopeTenantId.Should().Be(TenantB);
        nuevo.TenantId.Should().BeNull();
        nuevo.Items.Select(i => i.Id).Should().Equal(deB);
        (await Raiz(response)).GetProperty("total").GetInt32().Should().Be(1);
        await _factory.Instancias.DidNotReceive().ListIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 — tipo según el origen ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_SuperAdminConConsolidadoMaestro_202_ConEseTipo()
    {
        _factory.Instancias.ListIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(Guid.NewGuid(), TenantA, "R-A", null)]);

        var response = await Cliente(SuperAdmin()).PostAsync(Ruta, CuerpoIds("consolidado_maestro", Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        _factory.UltimoNuevo!.TipoDocumento.Should().Be(ConsolidadoExportDocumentType.ConsolidadoMaestro);
        (await Raiz(response)).GetProperty("tipoDocumento").GetString().Should().Be("consolidado_maestro");
    }

    [Fact]
    public async Task AC4_GestorConConsolidadoMaestro_400_TipoNoPermitido_SinCrear()
    {
        var response = await Cliente(Gestor()).PostAsync(Ruta, CuerpoIds("consolidado_maestro", Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("tipo_no_permitido");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC5 — el cuerpo no decide el origen ───────────────────────────────────────────────

    [Fact]
    public async Task AC5_GestorDeCConCamposDeOrigenYCompaniaEnElCuerpo_OrigenTramites_TenantC()
    {
        var id = Guid.NewGuid();
        _factory.Instancias.ListIdsFilteredAsync(TenantC, Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(id, TenantC, "R-C", null)]);
        var cuerpo =
            $"{{\"origen\":\"superadmin\",\"origin\":\"superadmin\",\"isSuperAdmin\":true,\"tenantId\":null," +
            $"\"scopeTenantId\":\"{TenantB}\",\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true," +
            $"\"seleccion\":{{\"modo\":\"ids\",\"ids\":[\"{id}\"],\"excluidos\":[],\"filtro\":null}}}}";

        var response = await Cliente(Gestor()).PostAsync(Ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var nuevo = _factory.UltimoNuevo!;
        nuevo.Origen.Should().Be(ConsolidadoExportOrigin.Tramites);
        nuevo.TenantId.Should().Be(TenantC);
        nuevo.ScopeTenantId.Should().BeNull();
        await _factory.Instancias.DidNotReceive().ListIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    // ── AC6 — usuario sin compañía que no es Super Admin ──────────────────────────────────

    [Fact]
    public async Task AC6_NoSuperAdminSinCompaniaEnElToken_403_SinLoteNiAuditoria()
    {
        var response = await Cliente(Token(null, "Radicador", [Permiso])).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().ObtenerLoteActivoIdAsync(default, default);
    }

    [Fact]
    public void AC6_ElCodigoSinCompaniaDelCasoDeUso_SeTraduceA403ConErrorEnLaRaiz()
    {
        var r = new CrearLoteConsolidadosResultado(null, CrearLoteConsolidadosErrores.SinCompania, "El usuario no tiene una compañía activa.");

        var problema = ConsolidadoLoteEndpoints.ProblemaDe(r).Should().BeOfType<ProblemHttpResult>().Subject;

        problema.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problema.ProblemDetails.Extensions["error"].Should().Be("sin_compania");
    }

    // ── AC8 — paridad 409 / 503 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AC8_SuperAdminConLoteActivo_409_ConSuId()
    {
        var activo = Guid.NewGuid();
        _factory.Lotes.ObtenerLoteActivoIdAsync(UsuarioId, Arg.Any<CancellationToken>()).Returns(activo);

        var response = await Cliente(SuperAdmin()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var root = await Raiz(response);
        root.GetProperty("error").GetString().Should().Be("lote_activo");
        root.GetProperty("loteActivoId").GetGuid().Should().Be(activo);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task AC8_SuperAdminConAuditoriaFallida_503_LoteNoCreado()
    {
        _factory.ResultadoCrear = new CrearLoteResultado(CrearLoteEstado.NoCreado);

        var response = await Cliente(SuperAdmin()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("lote_no_creado");
    }

    // ── Contrato OpenAPI (diseño 09 §4) ───────────────────────────────────────────────────

    [Fact]
    public void Contrato_ElPostDocumentaElOrigenSuperadmin_ElScope_LasCompaniasAlcanzadas_Y400y403()
    {
        var bloque = BloquePost();

        bloque.Should().Contain("superadmin").And.Contain("scope_tenant_id").And.Contain("reached_tenant_ids");
        bloque.Should().Contain("compania", "el Super Admin también acota con la condición compania del filtro");
        bloque.Should().NotContain("mientras llega #13383", "el resolver del origen superadmin ya existe");
        bloque.Should().Contain("Solo SuperAdmin", "la cabecera X-Tenant-Id solo acota al Super Admin");
        bloque.Should().Contain("consolidado_maestro fuera de origen superadmin");
        bloque.Should().Contain("usuario sin compañía que no es SuperAdmin");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static string BloquePost()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "core-api.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("el contrato OpenAPI debe encontrarse desde el directorio de tests");
        var yaml = File.ReadAllText(Path.Combine(dir!.FullName, "contracts", "openapi", "core-api.v1.yaml")).Replace("\r\n", "\n");
        var inicio = yaml.IndexOf($"  {Ruta}:\n", StringComparison.Ordinal);
        inicio.Should().BeGreaterThan(0);
        var fin = yaml.IndexOf("\n  /api/", inicio + 4, StringComparison.Ordinal);
        return yaml[inicio..(fin < 0 ? yaml.Length : fin)];
    }

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent CuerpoIds(params Guid[] ids) => CuerpoIds("consolidado", ids);

    private static StringContent CuerpoIds(string tipo, params Guid[] ids) => new(JsonSerializer.Serialize(new
    {
        tipoDocumento = tipo,
        confirmaEfectos = true,
        seleccion = new { modo = "ids", ids, excluidos = Array.Empty<Guid>(), filtro = (object?)null },
    }), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> Raiz(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Gestor(string[]? permisos = null) => Token(TenantC, "Radicador", permisos ?? [Permiso]);

    /// <summary>Super Admin SIN el claim del permiso: pasa por el bypass de <c>RequirePermission</c> (AC7).</summary>
    private static string SuperAdmin() => Token(TenantC, "SuperAdmin", []);

    private static string Token(Guid? tenantId, string role, string[] permisos)
    {
        var claims = new List<Claim> { new("sub", UsuarioId.ToString()), new("role", role), new("role_code", role) };
        if (tenantId is { } t)
            claims.Add(new Claim("tenant_id", t.ToString()));
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
    /// Host con repositorios y cifrador sustituidos y los resolvers de selección TAL CUAL los registra la composición
    /// de producción (a diferencia de <c>ConsolidadoLoteEndpointTests</c>, que los reemplaza).
    /// </summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public IConsolidadoLoteRepository Lotes { get; } = Substitute.For<IConsolidadoLoteRepository>();
        public IProcedureInstanceRepository Instancias { get; } = Substitute.For<IProcedureInstanceRepository>();
        public IConsolidadoLoteCipher Cipher { get; } = Substitute.For<IConsolidadoLoteCipher>();
        public NuevoLoteConsolidados? UltimoNuevo { get; private set; }
        public CrearLoteResultado? ResultadoCrear { get; set; }

        public Factory() => Reiniciar();

        public void Reiniciar()
        {
            Lotes.ClearSubstitute();
            Instancias.ClearSubstitute();
            UltimoNuevo = null;
            ResultadoCrear = null;

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
                    Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
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
            });
        }
    }

    private sealed class SingleScope : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }
}

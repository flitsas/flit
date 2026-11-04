using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14071 (Feature J, Épica #14021) — administración de tarifas del portal. La parte de SQL (versionado, solapes,
/// inmutabilidad, vigente, auditoría) corre contra la base con las migraciones aplicadas (DDL 153 y 163); cada prueba arma
/// sus propios organismos y tipos con códigos únicos y los retira al terminar. La autorización no toca la base.
/// </summary>
public sealed class TarifasPnTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private const string Base = Flit.Api.Endpoints.AdminPlataformaPersonaNaturalEndpoints.BasePath;

    // ── Concepto del dominio de Admin = concepto del cotizador ────────────────────────────────────────────────────────

    [Fact]
    public void LosConceptosDeAdminCoincidenConLosDelCotizador()
    {
        var delCotizador = new[]
        {
            ConceptosTarifa.DerechosTransito, ConceptosTarifa.ServicioFlit, ConceptosTarifa.CambioColor,
            ConceptosTarifa.ConversionCombustible, ConceptosTarifa.CambioCarroceria, ConceptosTarifa.EnvioDocumento,
        };

        ConceptosTarifaPn.Todos.Should().BeEquivalentTo(delCotizador);
    }

    // ── AC6 — permiso: la autorización corta antes de la base ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("Gerente")]
    public async Task AC6_UnRolQueNoEsSuperAdmin_AunConElPermisoEnElToken_RecibeForbidden(string rol)
    {
        var client = ClientWith(Token(rol, conPermiso: true));

        (await client.GetAsync($"{Base}/tarifas", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var post = await client.PostAsync($"{Base}/tarifas", new StringContent("{}", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC6_SinSesion_RecibeUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync($"{Base}/tarifas", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void AC6_TodasLasRutasExigenElRolSuperAdmin()
    {
        var rutas = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(Base, StringComparison.Ordinal)).ToList();

        rutas.Should().NotBeEmpty();
        foreach (var ruta in rutas)
            ruta.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy == AdminAuthorization.SuperAdminPolicy, ruta.RoutePattern.RawText);
    }

    // ── Validaciones del caso de uso (sin base) ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("DERECHOS_TRANSITO", -1, TarifaPnErrores.ValorInvalido)]
    [InlineData("DERECHOS_TRANSITO", VersionarTarifasPnHandler.ValorMaximoCop + 1, TarifaPnErrores.ValorInvalido)]
    [InlineData("INVENTADO", 10, TarifaPnErrores.ConceptoInvalido)]
    public async Task AC1_RechazaValoresNegativosYConceptosDesconocidos_SinTocarNada(string concepto, long valor, string esperado)
    {
        var repo = new RepoFalso();
        var handler = new VersionarTarifasPnHandler(repo, new AuditoriaFalsa());

        var r = await handler.HandleAsync(Comando(Guid.NewGuid(), [new TarifaPnValor(concepto, valor)]), TestContext.Current.CancellationToken);

        r.Error.Should().Be(esperado);
        repo.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task AC1_LosRenglonesBaseExigenTipo_YLosAdicionalesPuedenValerParaCualquiera()
    {
        var repo = new RepoFalso();
        var handler = new VersionarTarifasPnHandler(repo, new AuditoriaFalsa());

        var base_ = await handler.HandleAsync(Comando(null, [new TarifaPnValor("SERVICIO_FLIT", 5)]), TestContext.Current.CancellationToken);
        var adicional = await handler.HandleAsync(Comando(null, [new TarifaPnValor("ENVIO_DOCUMENTO", 5)]), TestContext.Current.CancellationToken);

        base_.Error.Should().Be(TarifaPnErrores.TipoObligatorio);
        adicional.Error.Should().BeNull();
    }

    [Fact]
    public async Task AC3_LaFechaDeInicioNoPuedeSerAnteriorAHoy()
    {
        var repo = new RepoFalso();
        var handler = new VersionarTarifasPnHandler(repo, new AuditoriaFalsa());

        var r = await handler.HandleAsync(
            Comando(Guid.NewGuid(), [new TarifaPnValor("SERVICIO_FLIT", 5)]) with { VigenteDesde = repo.HoyColombia().AddDays(-1) },
            TestContext.Current.CancellationToken);

        r.Error.Should().Be(TarifaPnErrores.FechaInicioPasada);
    }

    [Fact]
    public async Task AC1_UnConceptoNoSePuedeRepetirEnLaMismaVersion()
    {
        var handler = new VersionarTarifasPnHandler(new RepoFalso(), new AuditoriaFalsa());

        var r = await handler.HandleAsync(
            Comando(Guid.NewGuid(), [new TarifaPnValor("SERVICIO_FLIT", 5), new TarifaPnValor("servicio_flit", 6)]), TestContext.Current.CancellationToken);

        r.Error.Should().Be(TarifaPnErrores.ConceptoRepetido);
    }

    // ── SQL real: versionado, solapes, inmutabilidad, vigente y auditoría ─────────────────────────────────────────────

    [Fact]
    public async Task AC3_UnaVersionNuevaCierraLaAnteriorElDiaAnterior_YLaAnteriorQuedaEnElHistorialSinPoderEditarse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var hoy = e.Repo.HoyColombia();
        var primera = await e.VersionarAsync(e.Tipo, null, hoy.AddDays(1), ("DERECHOS_TRANSITO", 100_000), ("SERVICIO_FLIT", 50_000));
        primera.Error.Should().BeNull();

        var segunda = await e.VersionarAsync(e.Tipo, null, hoy.AddDays(10), ("DERECHOS_TRANSITO", 120_000));
        segunda.Error.Should().BeNull();

        var derechosV1 = primera.Tarifas!.Single(t => t.Concepto == "DERECHOS_TRANSITO");
        var historial = (await e.Historial.HandleAsync(derechosV1.Id, ct))!;
        historial.Select(v => (v.ValorCop, v.VigenteDesde, v.VigenteHasta)).Should().Equal(
            (120_000L, hoy.AddDays(10), (DateOnly?)null),
            (100_000L, hoy.AddDays(1), hoy.AddDays(9)));
        // La versión cerrada es historial: la base la rechaza (valor y borrado).
        var editar = async () => await e.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE admin.persona_natural_tariffs SET amount_cop = 1 WHERE id = {derechosV1.Id}", ct);
        (await editar.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("cerrada");
        var borrar = async () => await e.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE id = {derechosV1.Id}", ct);
        await borrar.Should().ThrowAsync<Exception>();
        // El servicio FLIT, sin versión nueva, sigue abierto.
        (await e.Historial.HandleAsync(primera.Tarifas!.Single(t => t.Concepto == "SERVICIO_FLIT").Id, ct))!.Should().ContainSingle().Which.VigenteHasta.Should().BeNull();
    }

    [Fact]
    public async Task AC3_NoSePermitenDosVersionesQueSeCrucen_YElLoteCompletoSeDeshace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var hoy = e.Repo.HoyColombia();
        (await e.VersionarAsync(e.Tipo, null, hoy.AddDays(1), ("DERECHOS_TRANSITO", 100))).Error.Should().BeNull();
        (await e.VersionarAsync(e.Tipo, null, hoy.AddDays(10), ("DERECHOS_TRANSITO", 200))).Error.Should().BeNull();

        // Misma fecha de inicio que una existente.
        (await e.VersionarAsync(e.Tipo, null, hoy.AddDays(10), ("DERECHOS_TRANSITO", 300))).Error.Should().Be(TarifaPnErrores.Solapada);
        // Inicio que cae dentro de una versión ya cerrada: el segundo valor del lote falla y NADA queda del primero.
        var lote = await e.VersionarAsync(e.Tipo, null, hoy.AddDays(5), ("SERVICIO_FLIT", 10), ("DERECHOS_TRANSITO", 400));

        lote.Error.Should().Be(TarifaPnErrores.Solapada);
        (await e.Lista.HandleAsync(new TarifaPnFiltro(false, e.Tipo, null, "SERVICIO_FLIT", null), ct)).Should().BeEmpty();
        (await e.Lista.HandleAsync(new TarifaPnFiltro(false, e.Tipo, null, "DERECHOS_TRANSITO", null), ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AC2_LaExcepcionDelOrganismoMandaSobreElValorGeneral_EnLaTarifaVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var inicio = e.Repo.HoyColombia().AddDays(1);
        await e.VersionarAsync(e.Tipo, null, inicio, ("DERECHOS_TRANSITO", 100_000), ("SERVICIO_FLIT", 50_000));
        await e.VersionarAsync(e.Tipo, e.Organismo, inicio, ("DERECHOS_TRANSITO", 130_000));
        await e.VersionarAsync(null, null, inicio, ("ENVIO_DOCUMENTO", 20_000));

        var enOrganismo = await e.Vigente.HandleAsync(e.Tipo, e.Organismo, inicio, ct);
        var enOtro = await e.Vigente.HandleAsync(e.Tipo, e.OtroOrganismo, inicio, ct);
        var antes = await e.Vigente.HandleAsync(e.Tipo, e.Organismo, inicio.AddDays(-1), ct);

        enOrganismo.Single(t => t.Concepto == "DERECHOS_TRANSITO").ValorCop.Should().Be(130_000);
        enOtro.Single(t => t.Concepto == "DERECHOS_TRANSITO").ValorCop.Should().Be(100_000);
        enOrganismo.Single(t => t.Concepto == "ENVIO_DOCUMENTO").ValorCop.Should().Be(20_000, "el adicional sin tipo vale para cualquiera");
        antes.Should().NotContain(t => e.Ids.Contains(t.Id), "una versión futura aún no es la vigente");
    }

    [Fact]
    public async Task AC4_LaVersionAnteriorConservaSuIdYSuValor_ParaQueLaCotizacionEmitidaSePuedaReconstruir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var hoy = e.Repo.HoyColombia();
        var v1 = (await e.VersionarAsync(e.Tipo, null, hoy.AddDays(1), ("DERECHOS_TRANSITO", 100))).Tarifas!.Single();

        await e.VersionarAsync(e.Tipo, null, hoy.AddDays(3), ("DERECHOS_TRANSITO", 999));

        // La solicitud congela los ids de versión (Cotizacion.VersionTarifa): el id de v1 sigue existiendo con su valor.
        var guardada = await e.Repo.ObtenerAsync(v1.Id, ct);
        guardada!.ValorCop.Should().Be(100);
        guardada.VigenteHasta.Should().Be(hoy.AddDays(2));
    }

    [Fact]
    public async Task AC6_CadaCambioQuedaAuditadoConActorValorAnteriorYNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var hoy = e.Repo.HoyColombia();
        await e.VersionarAsync(e.Tipo, null, hoy.AddDays(1), ("DERECHOS_TRANSITO", 100));

        var segunda = await e.VersionarAsync(e.Tipo, null, hoy.AddDays(4), ("DERECHOS_TRANSITO", 150));

        var nuevaId = segunda.Tarifas!.Single().Id;
        var filas = await e.Db.Database.SqlQuery<AuditoriaFila>($"""
            SELECT changed_by, old_value, new_value, operation, module FROM admin.tenant_config_audit_logs
             WHERE entity_name = 'persona_natural_tariffs' AND target_entity_id = {nuevaId}
            """).ToListAsync(ct);
        var fila = filas.Should().ContainSingle().Subject;
        fila.ChangedBy.Should().Be(e.Actor);
        fila.Operation.Should().Be("create");
        fila.OldValue.Should().MatchRegex(@"""valorCop"":\s*100\b");
        fila.NewValue.Should().MatchRegex(@"""valorCop"":\s*150\b");
    }

    [Fact]
    public async Task AC5_ElTipoOElOrganismoInexistenteSeRechazan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var hoy = e.Repo.HoyColombia();

        (await e.VersionarAsync(Guid.NewGuid(), null, hoy.AddDays(1), ("SERVICIO_FLIT", 1))).Error.Should().Be(TarifaPnErrores.TipoNoExiste);
        (await e.VersionarAsync(e.Tipo, Guid.NewGuid(), hoy.AddDays(1), ("SERVICIO_FLIT", 1))).Error.Should().Be(TarifaPnErrores.OrganismoNoExiste);
    }

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static VersionarTarifasPnCommand Comando(Guid? tipo, IReadOnlyList<TarifaPnValor> valores) =>
        new(tipo, null, valores, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), Guid.NewGuid());

    private static string Token(string rol, bool conPermiso)
    {
        var claims = new List<Claim> { new("sub", "11111111-1111-1111-1111-111111111111"), new("role", rol), new("tenant_id", Guid.NewGuid().ToString()) };
        if (conPermiso)
            claims.Add(new Claim("permissions", PersonaNaturalAdminAuthorization.ManageSlug));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private HttpClient ClientWith(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class AuditoriaFila
    {
        public Guid? ChangedBy { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? Operation { get; set; }
        public string? Module { get; set; }
    }

    private sealed class AuditoriaFalsa : Flit.Admin.Application.Auditing.IAdminAuditWriter
    {
        public Task WriteAsync(Flit.Admin.Application.Auditing.AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Repositorio que cuenta las llamadas de escritura; acepta cualquier tipo u organismo.</summary>
    private sealed class RepoFalso : IPersonaNaturalTarifaRepository
    {
        public int Llamadas { get; private set; }
        public DateOnly HoyColombia() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        public Task<IReadOnlyList<TarifaPnView>> ListarAsync(TarifaPnFiltro filtro, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TarifaPnView>>([]);
        public Task<TarifaPnView?> ObtenerAsync(Guid id, CancellationToken ct = default) => Task.FromResult<TarifaPnView?>(null);
        public Task<IReadOnlyList<TarifaPnView>?> HistorialAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TarifaPnView>?>(null);
        public Task<bool> TipoExisteAsync(Guid tipoId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> OrganismoExisteAsync(Guid organismoId, CancellationToken ct = default) => Task.FromResult(true);

        public Task<TarifaPnVersionadaResult> VersionarAsync(
            Guid? tipoId, Guid? organismoId, IReadOnlyList<TarifaPnValor> valores, DateOnly vigenteDesde, Guid actor, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(new TarifaPnVersionadaResult([], []));
        }

        public Task<IReadOnlyList<TarifaPnView>> VigentesDelTipoEnOrganismoAsync(Guid tipoId, Guid organismoId, DateOnly fecha, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TarifaPnView>>([]);
    }

    /// <summary>Un organismo (y otro más) y un tipo reales de la prueba; las tarifas se retiran al terminar.</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        public FlitDbContext Db { get; }
        public IPersonaNaturalTarifaRepository Repo { get; }
        public ListarTarifasPnHandler Lista { get; }
        public HistorialTarifaPnHandler Historial { get; }
        public TarifaVigentePnHandler Vigente { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid Tipo { get; } = Guid.NewGuid();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OtroOrganismo { get; } = Guid.NewGuid();
        public HashSet<Guid> Ids { get; } = [];
        private readonly VersionarTarifasPnHandler _versionar;

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            var sp = scope.ServiceProvider;
            Db = sp.GetRequiredService<FlitDbContext>();
            Repo = sp.GetRequiredService<IPersonaNaturalTarifaRepository>();
            Lista = sp.GetRequiredService<ListarTarifasPnHandler>();
            Historial = sp.GetRequiredService<HistorialTarifaPnHandler>();
            Vigente = sp.GetRequiredService<TarifaVigentePnHandler>();
            _versionar = sp.GetRequiredService<VersionarTarifasPnHandler>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new Escenario(host.Services.CreateScope());
            foreach (var (id, n) in new[] { (e.Organismo, "A"), (e.OtroOrganismo, "B") })
            {
                var code = $"PNJ{e._sufijo}{n}";
                await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {code}, {"Organismo de prueba " + code}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }

            var correo = $"pn-j-{e._sufijo.ToLowerInvariant()}@example.test";
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES ({e.Actor}, {correo}, {"Actor de prueba"}, 'active', now())", ct);
            var codigoTipo = "PN_J_" + e._sufijo;
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({e.Tipo}, {codigoTipo}, {"Tipo de prueba " + codigoTipo}, 'TRASPASO', 1, {"{}"}::jsonb, 'published', now(), true, true, now())
                """, ct);
            return e;
        }

        public async Task<VersionarTarifasPnResult> VersionarAsync(Guid? tipo, Guid? organismo, DateOnly desde, params (string Concepto, long Valor)[] valores)
        {
            var r = await _versionar.HandleAsync(
                new VersionarTarifasPnCommand(tipo, organismo, valores.Select(v => new TarifaPnValor(v.Concepto, v.Valor)).ToList(), desde, Actor),
                TestContext.Current.CancellationToken);
            foreach (var t in r.Tarifas ?? [])
                Ids.Add(t.Id);
            return r;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                // Las versiones son historial inmutable por trigger: solo un superusuario (la base de pruebas) puede retirarlas.
                await using var tx = await Db.Database.BeginTransactionAsync();
                await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var orgs = new[] { Organismo, OtroOrganismo };
                var ids = Ids.ToArray();
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE id = ANY({ids}) OR procedure_type_id = {Tipo} OR transit_office_id = ANY({orgs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_config_audit_logs WHERE entity_name = 'persona_natural_tariffs' AND changed_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.users WHERE id = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = {Tipo}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = ANY({orgs})");
                await tx.CommitAsync();
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}

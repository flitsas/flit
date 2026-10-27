using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cliente = Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14043 (Feature B, Épica #14021) — catálogo de organismos, tipos con tarifa y cotización de 2 líneas por la fachada
/// gRPC, de punta a punta con el servidor OIDC real de los tests. Cada prueba arma sus propios organismos, tipos y tarifas
/// (con códigos únicos) y los retira al terminar; requiere la base con las migraciones aplicadas (DDL 153).
/// </summary>
public sealed class PersonaNaturalCotizacionGrpcTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scope = "platform.tramites.persona_natural";
    private const string Portal = "svc-personanatural";
    private const string Hoy = "2000-01-01"; // inicio de vigencia de todas las tarifas de prueba

    [Fact]
    public async Task ListarOrganismos_DevuelveSoloLosHabilitadosParaPersonaNatural()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var respuesta = await cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct);

        respuesta.Organismos.Select(o => o.Id).Should().Contain(e.Organismo.ToString()).And.NotContain(e.OrganismoSinGrant.ToString());
        respuesta.Organismos.Single(o => o.Id == e.Organismo.ToString()).Municipio.Should().Be("Municipio de prueba");
    }

    [Fact]
    public async Task ListarTipos_ConOrganismo_SoloOfreceLosElegiblesConTarifaVigente_YSinOrganismoTodosConMotivo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 100_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 50_000, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoApagado, null, 1, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoApagado, null, 1, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var enOrganismo = await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest { OrganismoId = e.Organismo.ToString() }, cancellationToken: ct);
        var todos = await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest(), cancellationToken: ct);

        enOrganismo.Tipos.Select(t => t.Codigo).Should().Contain(e.CodigoElegible)
            .And.NotContain(e.CodigoApagado, "no es elegible aunque tenga tarifa")
            .And.NotContain(e.CodigoSinTarifa, "es elegible pero no tiene tarifa vigente en el organismo");
        todos.Tipos.Single(t => t.Codigo == e.CodigoApagado).DisponiblePn.Should().BeFalse();
        todos.Tipos.Single(t => t.Codigo == e.CodigoApagado).MotivoNoDisponible.Should().Contain("no está habilitado en FLIT 2");
        todos.Tipos.Single(t => t.Codigo == e.CodigoSinTarifa).DisponiblePn.Should().BeTrue();
    }

    [Fact]
    public async Task Cotizar_DevuelveLasDosLineasYElTotal_ConElSubtituloDelServicioFlit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 185_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 181_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var r = await cliente.CotizarAsync(new CotizarRequest { TipoTramiteCodigo = e.CodigoElegible, OrganismoId = e.Organismo.ToString() }, cancellationToken: ct);

        r.Lineas.Select(l => (l.Codigo, l.ValorCop)).Should().Equal(("DERECHOS_TRANSITO", 185_000L), ("SERVICIO_FLIT", 181_000L));
        r.Lineas[0].Nombre.Should().Be("Valor del Servicio de tránsito");
        r.Lineas[1].Subtitulo.Should().NotBeNullOrWhiteSpace();
        r.TotalCop.Should().Be(366_000);
        r.VersionTarifa.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Cotizar_LaExcepcionDelOrganismoMandaSobreElValorGeneral()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 100_000, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, e.Organismo, 120_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 10_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var r = await cliente.CotizarAsync(new CotizarRequest { TipoTramiteCodigo = e.CodigoElegible, OrganismoId = e.Organismo.ToString() }, cancellationToken: ct);

        r.Lineas[0].ValorCop.Should().Be(120_000);
        r.TotalCop.Should().Be(130_000);
    }

    [Fact]
    public async Task Cotizar_LasTransformacionesSoloSumanSiElTipoLasPermite_YElEnvioSuma()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        foreach (var tipo in new[] { e.TipoElegible, e.TipoSinTransformaciones })
        {
            await e.TarifaAsync("DERECHOS_TRANSITO", tipo, null, 100, ct);
            await e.TarifaAsync("SERVICIO_FLIT", tipo, null, 10, ct);
        }
        await e.TarifaAsync("CAMBIO_COLOR", null, null, 1_000, ct);
        await e.TarifaAsync("ENVIO_DOCUMENTO", null, null, 20, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var adicionales = new[] { "CAMBIO_COLOR", "ENVIO_DOCUMENTO", "CONVERSION_COMBUSTIBLE" };

        var permite = await cliente.CotizarAsync(Pedir(e.CodigoElegible, e.Organismo, adicionales), cancellationToken: ct);
        var noPermite = await cliente.CotizarAsync(Pedir(e.CodigoSinTransformaciones, e.Organismo, adicionales), cancellationToken: ct);

        permite.TotalCop.Should().Be(100 + 10 + 1_000 + 20);
        permite.AdicionalesNoAplicados.Should().Equal("CONVERSION_COMBUSTIBLE").And.NotContain("CAMBIO_COLOR");
        noPermite.TotalCop.Should().Be(100 + 10 + 20, "las transformaciones no suman aunque tengan valor configurado");
        noPermite.AdicionalesNoAplicados.Should().BeEquivalentTo("CAMBIO_COLOR", "CONVERSION_COMBUSTIBLE");
    }

    [Fact]
    public async Task Cotizar_DesdeLaLandingYDesdeElPago_DaElMismoTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 77_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 33_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var landing = await cliente.CotizarAsync(Pedir(e.CodigoElegible, e.Organismo), cancellationToken: ct);
        var pago = await cliente.CotizarAsync(Pedir(e.CodigoElegible, e.Organismo), cancellationToken: ct);

        pago.TotalCop.Should().Be(landing.TotalCop);
        pago.VersionTarifa.Should().Be(landing.VersionTarifa);
    }

    [Fact]
    public async Task Cotizar_SinTarifaVigente_RespondeTarifaNoVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var error = await Error(() => cliente.CotizarAsync(Pedir(e.CodigoSinTarifa, e.Organismo), cancellationToken: ct).ResponseAsync);

        PlatformRpcErrors.Reason(error).Should().Be("tarifa_no_vigente");
    }

    [Fact]
    public async Task Cotizar_TipoNoHabilitado_TipoDesconocidoYOrganismoSinGrant_TienenSuPropioError()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoApagado, null, 1, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoApagado, null, 1, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        PlatformRpcErrors.Reason(await Error(() => cliente.CotizarAsync(Pedir(e.CodigoApagado, e.Organismo), cancellationToken: ct).ResponseAsync))
            .Should().Be("tipo_no_habilitado");
        PlatformRpcErrors.Reason(await Error(() => cliente.CotizarAsync(Pedir("NO_EXISTE_" + Guid.NewGuid().ToString("N"), e.Organismo), cancellationToken: ct).ResponseAsync))
            .Should().Be("tipo_no_disponible");
        PlatformRpcErrors.Reason(await Error(() => cliente.CotizarAsync(Pedir(e.CodigoElegible, e.OrganismoSinGrant), cancellationToken: ct).ResponseAsync))
            .Should().Be("organismo_no_habilitado");
    }

    [Fact]
    public async Task Cotizar_ValidaLaEntrada_YElCandadoVaPrimero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var bueno = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var ajeno = await ClienteAsync(host, Guid.NewGuid(), ct);

        (await Error(() => bueno.CotizarAsync(new CotizarRequest(), cancellationToken: ct).ResponseAsync)).StatusCode.Should().Be(StatusCode.InvalidArgument);
        (await Error(() => bueno.CotizarAsync(new CotizarRequest { TipoTramiteCodigo = "X", OrganismoId = "no-guid" }, cancellationToken: ct).ResponseAsync)).StatusCode.Should().Be(StatusCode.InvalidArgument);
        PlatformRpcErrors.Reason(await Error(() => ajeno.CotizarAsync(new CotizarRequest(), cancellationToken: ct).ResponseAsync)).Should().Be("invalid_tenant");
        PlatformRpcErrors.Reason(await Error(() => ajeno.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct).ResponseAsync)).Should().Be("invalid_tenant");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static CotizarRequest Pedir(string codigo, Guid organismo, params string[] adicionales)
    {
        var r = new CotizarRequest { TipoTramiteCodigo = codigo, OrganismoId = organismo.ToString() };
        r.Adicionales.AddRange(adicionales);
        return r;
    }

    private static async Task<RpcException> Error(Func<Task> llamada) => (await llamada.Should().ThrowAsync<RpcException>()).Which;

    private WebApplicationFactory<Program> Host() =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("PersonaNatural:Fachada:Habilitada", "true");
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Scopes:0", Scope);
        });

    private static async Task<Cliente> ClienteAsync(WebApplicationFactory<Program> host, Guid empresa, CancellationToken ct)
    {
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Portal,
            ["client_secret"] = OidcServerTests.ServiceSecret,
            ["scope"] = Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = json.RootElement.GetProperty("access_token").GetString()!;
        var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        return new Cliente(canal.CreateCallInvoker().Intercept(metadata =>
        {
            metadata.Add("authorization", $"Bearer {token}");
            metadata.Add("x-flit-tenant-id", empresa.ToString());
            return metadata;
        }));
    }

    /// <summary>Organismos, tipos y tarifas de una prueba, con códigos únicos; se retiran al terminar.</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly FlitDbContext _db;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OrganismoSinGrant { get; } = Guid.NewGuid();
        public Guid TipoElegible { get; } = Guid.NewGuid();
        public Guid TipoSinTransformaciones { get; } = Guid.NewGuid();
        public Guid TipoApagado { get; } = Guid.NewGuid();
        public Guid TipoSinTarifa { get; } = Guid.NewGuid();
        public string CodigoElegible => "PN_T_" + _sufijo + "_A";
        public string CodigoSinTransformaciones => "PN_T_" + _sufijo + "_B";
        public string CodigoApagado => "PN_T_" + _sufijo + "_C";
        public string CodigoSinTarifa => "PN_T_" + _sufijo + "_D";

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            _db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new Escenario(host.Services.CreateScope());
            var db = e._db;
            foreach (var (id, n) in new[] { (e.Organismo, "A"), (e.OrganismoSinGrant, "B") })
            {
                var code = $"PNT{e._sufijo}{n}";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {code}, {"Organismo de prueba " + code}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.tenant_transit_office_grants (tenant_id, transit_office_id, is_enabled, source)
                VALUES ({PlatformTenants.PersonaNatural}, {e.Organismo}, true, 'CLIENT')
                """, ct);
            // Familia TRASPASO acumula transformaciones; OTROS no (ProcedureTypeGateProfile.ComplementaryTransformationsAllowed).
            await e.TipoAsync(e.TipoElegible, e.CodigoElegible, "TRASPASO", wizard: true, ct);
            await e.TipoAsync(e.TipoSinTransformaciones, e.CodigoSinTransformaciones, "OTROS", wizard: true, ct);
            await e.TipoAsync(e.TipoApagado, e.CodigoApagado, "OTROS", wizard: false, ct);
            await e.TipoAsync(e.TipoSinTarifa, e.CodigoSinTarifa, "OTROS", wizard: true, ct);
            return e;
        }

        private Task<int> TipoAsync(Guid id, string code, string familia, bool wizard, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({id}, {code}, {"Tipo de prueba " + code}, {familia}, 1, {"{}"}::jsonb, 'published', now(), true, {wizard}, now())
                """, ct);

        public Task<int> TarifaAsync(string concepto, Guid? tipo, Guid? organismo, long valor, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.persona_natural_tariffs (concept, procedure_type_id, transit_office_id, amount_cop, valid_from)
                VALUES ({concepto}, {tipo}, {organismo}, {valor}, {Hoy}::date)
                """, ct);

        public async ValueTask DisposeAsync()
        {
            try
            {
                // Las versiones de tarifa son historial inmutable por trigger: solo un superusuario (la base de pruebas) puede retirarlas.
                await using var tx = await _db.Database.BeginTransactionAsync();
                await _db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var ids = new[] { TipoElegible, TipoSinTransformaciones, TipoApagado, TipoSinTarifa };
                var orgs = new[] { Organismo, OrganismoSinGrant };
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE procedure_type_id = ANY({ids}) OR transit_office_id = ANY({orgs})");
                // Las tarifas sin tipo (adicionales generales) de la prueba: las del escenario son de valor único por concepto y ámbito.
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE procedure_type_id IS NULL AND transit_office_id IS NULL AND valid_from = {Hoy}::date");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_transit_office_grants WHERE transit_office_id = ANY({orgs})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = ANY({ids})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = ANY({orgs})");
                await tx.CommitAsync();
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}

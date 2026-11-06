using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.OtProfile;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using PerfilOt = Flit.Admin.Domain.OtProfile.OtProfile;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14393 (Feature #14392, Épica #14297) — banderas de medios y switches de cobro del portal. Las reglas se prueban sin
/// base; los endpoints contra la base con las migraciones aplicadas (DDL 153, 181 y 182), con el perfil OT sustituido para
/// fijar el organismo del token.
/// </summary>
public sealed class PortalCobroTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private const string BaseOt = AdminPortalCobroEndpoints.BasePathOt;
    private const string BasePlataforma = AdminPortalCobroEndpoints.BasePathPlataforma;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Reglas (sin base) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SinConfigurar_LaPasarelaEstaEncendida_ElPinApagado_YElSwitchDependeDelMonto()
    {
        var tipo = Guid.NewGuid();
        var punto = ConfigCobroPn.Vacia(web: false);

        punto.MedioHabilitado(MediosPagoPn.Pasarela).Should().BeTrue("hoy el portal cobra por pasarela");
        punto.MedioHabilitado(MediosPagoPn.Pin).Should().BeFalse();
        punto.SwitchEncendido(tipo, MediosPagoPn.Pasarela, CobrosPn.Tramite, 1000).Should().BeTrue();
        punto.SwitchEncendido(tipo, MediosPagoPn.Pasarela, CobrosPn.Tramite, 0).Should().BeFalse();
    }

    [Fact]
    public void ElPinNoExisteEnLaWeb_NiAunqueSeGuardaraEncendido()
    {
        var web = new ConfigCobroPn(true, [new MedioPnFila(MediosPagoPn.Pin, true)], []);

        web.MedioHabilitado(MediosPagoPn.Pin).Should().BeFalse();
        MediosPagoPn.DelPortal(web: true).Should().Equal(MediosPagoPn.Pasarela);
    }

    [Fact]
    public void LoGuardadoManda_AunConMontoCero_YLaGestionAgrupaServicioFlitYEnvio()
    {
        var tipo = Guid.NewGuid();
        var cfg = new ConfigCobroPn(false, [new MedioPnFila(MediosPagoPn.Pasarela, false)], [new SwitchPnFila(tipo, MediosPagoPn.Pin, CobrosPn.Gestion, false)]);

        cfg.MedioHabilitado(MediosPagoPn.Pasarela).Should().BeFalse();
        cfg.SwitchEncendido(tipo, MediosPagoPn.Pin, CobrosPn.Gestion, 9999).Should().BeFalse();
        CobrosPn.DeConcepto("SERVICIO_FLIT").Should().Be(CobrosPn.Gestion);
        CobrosPn.DeConcepto("ENVIO_DOCUMENTO").Should().Be(CobrosPn.Gestion);
        CobrosPn.DeConcepto("DERECHOS_TRANSITO").Should().Be(CobrosPn.Tramite);
        CobrosPn.DeConcepto("CAMBIO_COLOR").Should().Be(CobrosPn.Tramite);
    }

    [Fact]
    public async Task AC2_EncenderSinMontoMayorQueCero_NoGuarda_YResponde_ElMotivo()
    {
        var repo = new CobroEnMemoria();
        var handler = new GuardarSwitchCobroPnHandler(repo, new TarifasFalsas(), new AuditoriaFalsa());
        var tipo = Guid.NewGuid();

        foreach (var monto in new long?[] { null, 0 })
        {
            repo.Montos.Clear();
            if (monto is { } m)
                repo.Montos[(tipo, CobrosPn.Tramite)] = m;
            var r = await handler.HandleAsync(new AlcanceCobroPn(Guid.NewGuid(), false), tipo, "PASARELA", "TRAMITE", true, Guid.NewGuid(), Ct);
            r.Should().Be(CobroPnErrores.SinMonto);
        }

        repo.Guardados.Should().Be(0);
    }

    [Fact]
    public async Task AC4_ElOrganismoNoMueveLaGestion_NiSiquieraSeConsultaNada()
    {
        var repo = new CobroEnMemoria();
        var handler = new GuardarSwitchCobroPnHandler(repo, new TarifasFalsas(), new AuditoriaFalsa());

        var r = await handler.HandleAsync(new AlcanceCobroPn(Guid.NewGuid(), false), Guid.NewGuid(), "PASARELA", "GESTION", false, Guid.NewGuid(), Ct);

        r.Should().Be(CobroPnErrores.GestionAjena);
        repo.Guardados.Should().Be(0);
        repo.Consultas.Should().Be(0);
    }

    [Fact]
    public async Task AC5_EnLaWebNoSeOfreceNiSeGuardaPin_NiComoBanderaNiComoSwitch()
    {
        var repo = new CobroEnMemoria();
        var web = new AlcanceCobroPn(null, true);

        (await new GuardarMedioCobroPnHandler(repo, new AuditoriaFalsa()).HandleAsync(web, "PIN", true, Guid.NewGuid(), Ct)).Should().Be(CobroPnErrores.PinEnWeb);
        (await new GuardarSwitchCobroPnHandler(repo, new TarifasFalsas(), new AuditoriaFalsa())
            .HandleAsync(web, Guid.NewGuid(), "PIN", "TRAMITE", false, Guid.NewGuid(), Ct)).Should().Be(CobroPnErrores.PinEnWeb);
        var vista = await new LeerCobroPnHandler(repo, new TarifasFalsas()).HandleAsync(web, Ct);

        vista.Medios.Select(m => m.Method).Should().Equal("PASARELA");
        repo.Guardados.Should().Be(0);
    }

    [Fact]
    public async Task Leer_ElOrganismoVeLaGestionEnSoloLectura_ElSuperAdminEditable()
    {
        var tipo = Guid.NewGuid();
        var repo = new CobroEnMemoria();
        repo.Montos[(tipo, CobrosPn.Tramite)] = 5000;
        repo.Montos[(tipo, CobrosPn.Gestion)] = 700;
        var tarifas = new TarifasFalsas { Tipos = [new TipoTarifaPnView(tipo, "T", "Tipo")] };

        var ot = await new LeerCobroPnHandler(repo, tarifas).HandleAsync(new AlcanceCobroPn(Guid.NewGuid(), false), Ct);
        var sa = await new LeerCobroPnHandler(repo, tarifas).HandleAsync(new AlcanceCobroPn(Guid.NewGuid(), true), Ct);

        ot.Tipos.Single().Medios.Select(m => m.Method).Should().Equal("PASARELA", "PIN");
        ot.Tipos.Single().Medios[0].Conceptos.Select(c => (c.Charge, c.Editable, c.MontoCop)).Should().Equal(("TRAMITE", true, 5000L), ("GESTION", false, 700L));
        sa.Tipos.Single().Medios[0].Conceptos.Should().OnlyContain(c => c.Editable);
    }

    // ── Autorización ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LasRutasDelOtExigenOtAdminOSuperAdmin_YLasDePlataformaSoloSuperAdmin()
    {
        var todas = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var ot = todas.Where(e => e.RoutePattern.RawText!.StartsWith(BaseOt, StringComparison.Ordinal)).ToList();
        var plataforma = todas.Where(e => e.RoutePattern.RawText!.StartsWith(BasePlataforma, StringComparison.Ordinal)).ToList();

        ot.Should().HaveCount(3);
        plataforma.Should().HaveCount(3);
        foreach (var ruta in ot)
            ruta.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy == AdminAuthorization.OtAdminOrSuperAdminPolicy, ruta.RoutePattern.RawText);
        foreach (var ruta in plataforma)
            ruta.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy == AdminAuthorization.SuperAdminPolicy, ruta.RoutePattern.RawText);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("Gerente")]
    public async Task UnRolQueNoEsOtNiSuperAdmin_RecibeForbidden(string rol)
    {
        var client = ClientWith(factory, Token(rol, Guid.NewGuid(), Guid.NewGuid()), new PerfilesFalsos());

        (await client.GetAsync(BaseOt, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(BasePlataforma, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SinSesion_RecibeUnauthorized() =>
        (await factory.CreateClient().GetAsync(BaseOt, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    // ── Endpoints contra la base ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ApagarUnMedioDelPunto_NoTocaOtrosPortales()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var ot = e.ClienteOt();
        var otro = e.ClienteOtroOt();

        (await ot.PutAsJsonAsync($"{BaseOt}/medios", new { method = "PASARELA", enabled = false }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ot.PutAsJsonAsync($"{BaseOt}/medios", new { method = "PIN", enabled = true }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var propio = await LeerAsync(ot);
        propio.GetProperty("medios").EnumerateArray().Select(m => (m.GetProperty("method").GetString(), m.GetProperty("enabled").GetBoolean()))
            .Should().Equal(("PASARELA", false), ("PIN", true));
        var delOtro = await LeerAsync(otro);
        delOtro.GetProperty("medios").EnumerateArray().Select(m => (m.GetProperty("method").GetString(), m.GetProperty("enabled").GetBoolean()))
            .Should().Equal(("PASARELA", true), ("PIN", false));
    }

    [Fact]
    public async Task AC2_EncenderSinMonto_Responde400ConElMotivo_YNoDejaFila()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var ot = e.ClienteOt();

        var r = await ot.PutAsJsonAsync($"{BaseOt}/switches", new { tipoTramiteId = e.Tipo, method = "PASARELA", charge = "TRAMITE", enabled = true }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var cuerpo = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct));
        cuerpo.GetProperty("code").GetString().Should().Be(CobroPnErrores.SinMonto);
        cuerpo.GetProperty("message").GetString().Should().Contain("monto mayor que cero");
        (await e.ContarSwitchesAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC3_ApagarYVolverAEncender_ConservaElMonto()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await e.SembrarAsync("DERECHOS_TRANSITO", e.Tipo, e.Organismo, 185_000, Ct);
        var ot = e.ClienteOt();
        var cuerpo = new { tipoTramiteId = e.Tipo, method = "PASARELA", charge = "TRAMITE" };

        (await ot.PutAsJsonAsync($"{BaseOt}/switches", new { cuerpo.tipoTramiteId, cuerpo.method, cuerpo.charge, enabled = true }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TramitePasarela(ot, e.Tipo)).Should().Be((true, 185_000L));
        (await ot.PutAsJsonAsync($"{BaseOt}/switches", new { cuerpo.tipoTramiteId, cuerpo.method, cuerpo.charge, enabled = false }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TramitePasarela(ot, e.Tipo)).Should().Be((false, 185_000L), "apagar no toca el monto, que vive en Precios");
        (await ot.PutAsJsonAsync($"{BaseOt}/switches", new { cuerpo.tipoTramiteId, cuerpo.method, cuerpo.charge, enabled = true }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await TramitePasarela(ot, e.Tipo)).Should().Be((true, 185_000L));
        (await e.ContarSwitchesAsync(Ct)).Should().Be(1, "es una sola fila por ámbito, tipo, medio y concepto");
        (await e.ContarTarifasAsync(Ct)).Should().Be(1, "el monto sigue siendo una sola versión");
    }

    [Fact]
    public async Task AC4_ElAdminDelOrganismoNoMueveLaGestion_Responde403_ElSuperAdminSi()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await e.SembrarAsync("SERVICIO_FLIT", e.Tipo, null, 90_000, Ct);

        var ot = await e.ClienteOt().PutAsJsonAsync($"{BaseOt}/switches", new { tipoTramiteId = e.Tipo, method = "PASARELA", charge = "GESTION", enabled = true }, Ct);
        ot.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ot.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be(CobroPnErrores.GestionAjena);
        (await e.ContarSwitchesAsync(Ct)).Should().Be(0);

        var sa = await e.ClienteSuperAdmin().PutAsJsonAsync(
            $"{BaseOt}/switches?transitOfficeId={e.Organismo}", new { tipoTramiteId = e.Tipo, method = "PASARELA", charge = "GESTION", enabled = true }, Ct);
        sa.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await e.ContarSwitchesAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task AC5_EnLaWebNoHayPin_NiComoBanderaNiComoSwitch_Responde400()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var sa = e.ClienteSuperAdmin();

        var bandera = await sa.PutAsJsonAsync($"{BasePlataforma}/medios", new { method = "PIN", enabled = true }, Ct);
        var sw = await sa.PutAsJsonAsync($"{BasePlataforma}/switches", new { tipoTramiteId = e.Tipo, method = "PIN", charge = "TRAMITE", enabled = false }, Ct);

        bandera.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bandera.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be(CobroPnErrores.PinEnWeb);
        sw.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var web = await LeerAsync(sa, BasePlataforma);
        web.GetProperty("medios").EnumerateArray().Select(m => m.GetProperty("method").GetString()).Should().Equal("PASARELA");
    }

    [Fact]
    public async Task ElSuperAdminDeLaWebGuardaSuSwitch_SinTocarAlOrganismo()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await e.SembrarAsync("DERECHOS_TRANSITO", e.Tipo, null, 120_000, Ct);

        (await e.ClienteSuperAdmin().PutAsJsonAsync(
            $"{BasePlataforma}/switches", new { tipoTramiteId = e.Tipo, method = "PASARELA", charge = "TRAMITE", enabled = true }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await TramitePasarela(e.ClienteSuperAdmin(), e.Tipo, BasePlataforma)).Should().Be((true, 120_000L));
        (await e.ContarSwitchesAsync(Ct, "transit_office_id IS NULL")).Should().Be(1);
        (await e.ContarSwitchesAsync(Ct, "transit_office_id IS NOT NULL")).Should().Be(0);
    }

    [Fact]
    public async Task UnAdminDeOtroOrganismo_NoLeeNiCambiaElCobroAjeno_Responde403()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var ot = e.ClienteOt();

        (await ot.GetAsync($"{BaseOt}?transitOfficeId={e.OtroOrganismo}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ot.PutAsJsonAsync($"{BaseOt}/medios?transitOfficeId={e.OtroOrganismo}", new { method = "PASARELA", enabled = false }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await e.ClienteSuperAdmin().GetAsync(BaseOt, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "el SuperAdmin debe indicar el organismo en la ruta del OT");
    }

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> LeerAsync(HttpClient client, string ruta = BaseOt)
    {
        var r = await client.GetAsync(ruta, Ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(Ct));
        return await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    /// <summary>Estado y monto del switch de trámite en pasarela del tipo, tal como lo ve el administrador.</summary>
    private static async Task<(bool Enabled, long Monto)> TramitePasarela(HttpClient client, Guid tipo, string ruta = BaseOt)
    {
        var json = await LeerAsync(client, ruta);
        var concepto = json.GetProperty("tipos").EnumerateArray().Single(t => t.GetProperty("tipoTramiteId").GetGuid() == tipo)
            .GetProperty("medios").EnumerateArray().Single(m => m.GetProperty("method").GetString() == "PASARELA")
            .GetProperty("conceptos").EnumerateArray().Single(c => c.GetProperty("charge").GetString() == "TRAMITE");
        return (concepto.GetProperty("enabled").GetBoolean(), concepto.GetProperty("montoCop").GetInt64());
    }

    private static string Token(string rol, Guid tenant, Guid usuario)
    {
        var claims = new List<Claim> { new("sub", usuario.ToString()), new("role", rol), new("tenant_id", tenant.ToString()) };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private static HttpClient ClientWith(WebApplicationFactory<Program> host, string token, IOtProfileRepository perfiles)
    {
        var client = host.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IOtProfileRepository>();
            s.AddScoped(_ => perfiles);
        })).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class PerfilesFalsos : IOtProfileRepository
    {
        public Guid Tenant { get; init; }
        public Guid Organismo { get; init; }

        public Task<PerfilOt?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == Tenant && Tenant != Guid.Empty
                ? new PerfilOt { Id = Guid.NewGuid(), TenantId = tenantId, TransitOfficeId = Organismo }
                : (PerfilOt?)null);

        public Task<PerfilOt?> GetByTransitOfficeAsync(Guid transitOfficeId, CancellationToken cancellationToken = default) => Task.FromResult<PerfilOt?>(null);

        public Task<PerfilOt> SaveAsync(Guid tenantId, string operationMode, bool quipuxReadOnly, Guid? changedBy, int? revocationWindowBusinessDays, Guid? transitOfficeId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class AuditoriaFalsa : Flit.Admin.Application.Auditing.IAdminAuditWriter
    {
        public Task WriteAsync(Flit.Admin.Application.Auditing.AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CobroEnMemoria : IPersonaNaturalCobroRepository
    {
        public Dictionary<(Guid, string), long> Montos { get; } = [];
        public int Guardados { get; private set; }
        public int Consultas { get; private set; }

        public Task<ConfigCobroPn> LeerAsync(Guid? ambitoOrganismoId, CancellationToken ct = default)
        {
            Consultas++;
            return Task.FromResult(ConfigCobroPn.Vacia(ambitoOrganismoId is null));
        }

        public Task<IReadOnlyDictionary<(Guid TipoId, string Cobro), long>> MontosAsync(Guid? ambitoOrganismoId, CancellationToken ct = default)
        {
            Consultas++;
            return Task.FromResult<IReadOnlyDictionary<(Guid TipoId, string Cobro), long>>(Montos);
        }

        public Task<bool?> GuardarMedioAsync(Guid? ambitoOrganismoId, string medio, bool enabled, Guid actor, CancellationToken ct = default)
        {
            Guardados++;
            return Task.FromResult<bool?>(null);
        }

        public Task<bool?> GuardarSwitchAsync(Guid? ambitoOrganismoId, Guid tipoId, string medio, string cobro, bool enabled, Guid actor, CancellationToken ct = default)
        {
            Guardados++;
            return Task.FromResult<bool?>(null);
        }
    }

    private sealed class TarifasFalsas : IPersonaNaturalTarifaRepository
    {
        public IReadOnlyList<TipoTarifaPnView> Tipos { get; init; } = [];

        public DateOnly HoyColombia() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        public Task<IReadOnlyList<TarifaPnView>> ListarAsync(TarifaPnFiltro filtro, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TarifaPnView>> ListarDelOrganismoAsync(Guid organismoId, Guid? tipoId, string? concepto, bool soloVigentes, DateOnly? fecha, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TipoTarifaPnView>> ListarTiposDelPortalAsync(CancellationToken ct = default) => Task.FromResult(Tipos);
        public Task<TarifaPnView?> ObtenerAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TarifaPnView>?> HistorialAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TipoExisteAsync(Guid tipoId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> OrganismoExisteAsync(Guid organismoId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<TarifaPnVersionadaResult> VersionarAsync(Guid? tipoId, Guid? organismoId, IReadOnlyList<TarifaPnValor> valores, DateOnly vigenteDesde, Guid actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TarifaPnView>> VigentesDelTipoEnOrganismoAsync(Guid tipoId, Guid organismoId, DateOnly fecha, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Dos organismos, un tipo habilitado y un actor reales; todo se retira al terminar.</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _host;
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        public FlitDbContext Db { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid TenantOt { get; } = Guid.NewGuid();
        public Guid TenantOtroOt { get; } = Guid.NewGuid();
        public Guid Tipo { get; } = Guid.NewGuid();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OtroOrganismo { get; } = Guid.NewGuid();

        private Escenario(WebApplicationFactory<Program> host)
        {
            _host = host;
            _scope = host.Services.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new Escenario(host);
            foreach (var (id, n) in new[] { (e.Organismo, "A"), (e.OtroOrganismo, "B") })
            {
                var code = $"PNC{e._sufijo}{n}";
                await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {code}, {"Organismo de prueba " + code}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }

            var correo = $"pn-cobro-{e._sufijo.ToLowerInvariant()}@example.test";
            await e.Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES ({e.Actor}, {correo}, 'Actor de cobro', 'active', now())", ct);
            var codigo = "PN_CB_" + e._sufijo;
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({e.Tipo}, {codigo}, {"Tipo de prueba " + codigo}, 'TRASPASO', 1, {"{}"}::jsonb, 'published', now(), true, true, now())
                """, ct);
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO admin.persona_natural_enabled_types (procedure_type_id, is_enabled) VALUES ({e.Tipo}, true)", ct);
            return e;
        }

        public HttpClient ClienteOt() => ClientWith(_host, Token("ot_admin", TenantOt, Actor), new PerfilesFalsos { Tenant = TenantOt, Organismo = Organismo });

        public HttpClient ClienteOtroOt() => ClientWith(_host, Token("ot_admin", TenantOtroOt, Actor), new PerfilesFalsos { Tenant = TenantOtroOt, Organismo = OtroOrganismo });

        public HttpClient ClienteSuperAdmin() => ClientWith(_host, Token("SuperAdmin", Guid.NewGuid(), Actor), new PerfilesFalsos());

        /// <summary>Siembra una versión de precio vigente desde hoy (por la misma función de versionado).</summary>
        public Task SembrarAsync(string concepto, Guid? tipo, Guid? organismo, long valor, CancellationToken ct)
        {
            var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
            return Db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT admin.persona_natural_tariff_new_version({concepto}, {tipo}::uuid, {organismo}::uuid, {valor}, {hoy}, {Actor})", ct);
        }

        public async Task<int> ContarSwitchesAsync(CancellationToken ct, string condicion = "TRUE") =>
            (await Db.Database.SqlQueryRaw<int>(
                $"""SELECT count(*)::int AS "Value" FROM admin.persona_natural_charge_switches WHERE procedure_type_id = '{Tipo}' AND {condicion}""").ToListAsync(ct)).Single();

        public async Task<int> ContarTarifasAsync(CancellationToken ct) =>
            (await Db.Database.SqlQueryRaw<int>(
                $"""SELECT count(*)::int AS "Value" FROM admin.persona_natural_tariffs WHERE procedure_type_id = '{Tipo}'""").ToListAsync(ct)).Single();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var tx = await Db.Database.BeginTransactionAsync();
                await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var orgs = new[] { Organismo, OtroOrganismo };
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_charge_switches WHERE procedure_type_id = {Tipo} OR transit_office_id = ANY({orgs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_payment_methods WHERE transit_office_id = ANY({orgs}) OR created_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE procedure_type_id = {Tipo} OR transit_office_id = ANY({orgs}) OR created_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_enabled_types WHERE procedure_type_id = {Tipo}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_config_audit_logs WHERE changed_by = {Actor}");
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

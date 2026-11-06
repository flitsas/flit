using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.OtProfile;
using PerfilOt = Flit.Admin.Domain.OtProfile.OtProfile;
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
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14388 (Feature #14387, Épica #14297) — precios del portal vistos desde el dashboard del organismo
/// (<c>/api/v1/admin/ot/portal-tarifas</c>). Las reglas de alcance se prueban sin base; los endpoints contra la base con
/// las migraciones aplicadas (DDL 153 y 181), con el perfil OT sustituido para fijar el organismo del token.
/// </summary>
public sealed class TarifasOtTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private const string Base = AdminOtPortalTarifasEndpoints.BasePath;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Reglas (sin base) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LosConceptosDelOrganismo_SonDerechosYTransformaciones_YNadaMas()
    {
        ConceptosTarifaPn.EditablesPorOrganismo.Should().BeEquivalentTo(
            "DERECHOS_TRANSITO", "CAMBIO_COLOR", "CONVERSION_COMBUSTIBLE", "CAMBIO_CARROCERIA");
        ConceptosTarifaPn.EditablePorOrganismo("SERVICIO_FLIT").Should().BeFalse();
        ConceptosTarifaPn.EditablePorOrganismo("ENVIO_DOCUMENTO").Should().BeFalse();
    }

    [Theory]
    [InlineData("DERECHOS_TRANSITO", true, false, true)]
    [InlineData("CAMBIO_COLOR", true, false, true)]
    [InlineData("SERVICIO_FLIT", true, false, false)]
    [InlineData("ENVIO_DOCUMENTO", true, false, false)]
    [InlineData("SERVICIO_FLIT", true, true, true)]
    [InlineData("DERECHOS_TRANSITO", false, false, false)]
    [InlineData("DERECHOS_TRANSITO", false, true, false)]
    public void Editable_SoloEnLoDelOrganismoYConLosConceptosPermitidos(string concepto, bool delOrganismo, bool superAdmin, bool esperado)
    {
        var org = Guid.NewGuid();
        var tarifa = Vista(concepto, delOrganismo ? org : null);

        TarifaOtView.De(tarifa, new AlcanceTarifasOt(org, superAdmin)).Editable.Should().Be(esperado);
    }

    [Fact]
    public async Task AC2_UnConceptoAjeno_NoLlegaAlRepositorio()
    {
        var repo = new RepoContador();
        var handler = new VersionarTarifasOtHandler(new VersionarTarifasPnHandler(repo, new AuditoriaFalsa()));
        var alcance = new AlcanceTarifasOt(Guid.NewGuid(), false);

        foreach (var concepto in new[] { "SERVICIO_FLIT", "envio_documento" })
        {
            var r = await handler.HandleAsync(alcance, Guid.NewGuid(), [new TarifaPnValor(concepto, 1000)], Hoy(), Guid.NewGuid(), Ct);
            r.Error.Should().Be(TarifaOtErrores.ConceptoAjeno);
        }

        // Un lote mixto tampoco pasa a medias.
        var mixto = await handler.HandleAsync(
            alcance, Guid.NewGuid(), [new TarifaPnValor("DERECHOS_TRANSITO", 1), new TarifaPnValor("SERVICIO_FLIT", 2)], Hoy(), Guid.NewGuid(), Ct);
        mixto.Error.Should().Be(TarifaOtErrores.ConceptoAjeno);
        repo.Versionados.Should().Be(0);
    }

    [Fact]
    public async Task AC1_ElOrganismoDeLaVersionEsElDelAlcance_NuncaElQueEnviaElCliente()
    {
        var repo = new RepoContador();
        var handler = new VersionarTarifasOtHandler(new VersionarTarifasPnHandler(repo, new AuditoriaFalsa()));
        var organismo = Guid.NewGuid();

        var r = await handler.HandleAsync(
            new AlcanceTarifasOt(organismo, false), Guid.NewGuid(), [new TarifaPnValor("DERECHOS_TRANSITO", 90_000)], Hoy(), Guid.NewGuid(), Ct);

        r.Error.Should().BeNull();
        repo.UltimoOrganismo.Should().Be(organismo);
    }

    [Fact]
    public async Task AC4_ElAdministradorDelOrganismoDebeIndicarElTipo_ElSuperAdminNo()
    {
        var repo = new RepoContador();
        var listar = new ListarTarifasOtHandler(repo);
        var versionar = new VersionarTarifasOtHandler(new VersionarTarifasPnHandler(repo, new AuditoriaFalsa()));

        (await listar.HandleAsync(new AlcanceTarifasOt(Guid.NewGuid(), false), false, null, null, null, Ct)).Error.Should().Be(TarifaOtErrores.TipoObligatorio);
        (await listar.HandleAsync(new AlcanceTarifasOt(Guid.NewGuid(), true), false, null, null, null, Ct)).Error.Should().BeNull();
        (await versionar.HandleAsync(
            new AlcanceTarifasOt(Guid.NewGuid(), false), null, [new TarifaPnValor("CAMBIO_COLOR", 5)], Hoy(), Guid.NewGuid(), Ct)).Error.Should().Be(TarifaOtErrores.TipoObligatorio);
    }

    [Fact]
    public async Task Historial_DeUnaTarifaDeOtroOrganismo_EsOtroOrganismo_YLaGeneralSeVe()
    {
        var mio = Guid.NewGuid();
        var otro = Guid.NewGuid();
        var repo = new RepoContador
        {
            Tarifa = id => id == Ids.DeOtro ? Vista("DERECHOS_TRANSITO", otro, id) : id == Ids.General ? Vista("DERECHOS_TRANSITO", null, id) : null,
        };
        var handler = new HistorialTarifaOtHandler(repo);
        var alcance = new AlcanceTarifasOt(mio, false);

        (await handler.HandleAsync(alcance, Ids.DeOtro, Ct)).Error.Should().Be(TarifaOtErrores.OtroOrganismo);
        (await handler.HandleAsync(alcance, Guid.NewGuid(), Ct)).Error.Should().Be(TarifaPnErrores.NoEncontrada);
        var general = await handler.HandleAsync(alcance, Ids.General, Ct);
        general.Error.Should().BeNull();
        general.Tarifas.Should().ContainSingle().Which.Editable.Should().BeFalse();
    }

    // ── Autorización ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TodasLasRutasExigenOtAdminOSuperAdmin_YLasDePlataformaSiguenSoloParaSuperAdmin()
    {
        var rutas = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(Base, StringComparison.Ordinal)).ToList();

        rutas.Should().HaveCount(4);
        foreach (var ruta in rutas)
            ruta.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy == AdminAuthorization.OtAdminOrSuperAdminPolicy, ruta.RoutePattern.RawText);

        var plataforma = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(AdminPlataformaPersonaNaturalEndpoints.BasePath + "/tarifas", StringComparison.Ordinal)).ToList();
        plataforma.Should().NotBeEmpty();
        foreach (var ruta in plataforma)
            ruta.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy == AdminAuthorization.SuperAdminPolicy, ruta.RoutePattern.RawText);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("Gerente")]
    public async Task UnRolQueNoEsOtNiSuperAdmin_RecibeForbidden(string rol)
    {
        var client = ClientWith(Token(rol, Guid.NewGuid(), Guid.NewGuid()), new PerfilesFalsos());

        (await client.GetAsync($"{Base}?tipoTramiteId={Guid.NewGuid()}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(Base, Json("{}"), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SinSesion_RecibeUnauthorized() =>
        (await factory.CreateClient().GetAsync(Base, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    // ── Endpoints contra la base ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ElAdminDelOrganismoRegistraDerechosYTransformaciones_ParaSuOrganismo()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var client = e.ClienteOt();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

        var r = await client.PostAsJsonAsync(Base, new
        {
            tipoTramiteId = e.Tipo,
            vigenteDesde = hoy.AddDays(1),
            valores = new object[] { new { concepto = "DERECHOS_TRANSITO", valorCop = 150_000 }, new { concepto = "CAMBIO_COLOR", valorCop = 40_000 } },
        }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var filas = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        filas.Should().HaveCount(2);
        filas.Should().OnlyContain(f => f.GetProperty("organismoId").GetGuid() == e.Organismo && f.GetProperty("editable").GetBoolean());
        (await e.ContarAsync($"transit_office_id = '{e.Organismo}'", Ct)).Should().Be(2);
    }

    [Theory]
    [InlineData("SERVICIO_FLIT")]
    [InlineData("ENVIO_DOCUMENTO")]
    public async Task AC2_ElAdminDelOrganismoNoPuedeCambiarServicioFlitNiEnvio_YNoCambiaNada(string concepto)
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var client = e.ClienteOt();

        var r = await client.PostAsJsonAsync(Base, new
        {
            tipoTramiteId = e.Tipo,
            vigenteDesde = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            valores = new object[] { new { concepto = "DERECHOS_TRANSITO", valorCop = 1 }, new { concepto, valorCop = 5 } },
        }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be(TarifaOtErrores.ConceptoAjeno);
        (await e.ContarAsync($"procedure_type_id = '{e.Tipo}' OR transit_office_id = '{e.Organismo}'", Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC3_ElAdminDelOrganismoA_ConElIdentificadorDelOrganismoB_RecibeForbidden_YNoCambiaNada()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var client = e.ClienteOt();
        var cuerpo = new
        {
            tipoTramiteId = e.Tipo,
            organismoId = e.OtroOrganismo,
            vigenteDesde = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            valores = new object[] { new { concepto = "DERECHOS_TRANSITO", valorCop = 1 } },
        };

        var enCuerpo = await client.PostAsJsonAsync(Base, cuerpo, Ct);
        var enRuta = await client.PostAsJsonAsync($"{Base}?transitOfficeId={e.OtroOrganismo}", new { cuerpo.tipoTramiteId, cuerpo.vigenteDesde, cuerpo.valores }, Ct);
        var lectura = await client.GetAsync($"{Base}?transitOfficeId={e.OtroOrganismo}&tipoTramiteId={e.Tipo}", Ct);

        foreach (var respuesta in new[] { enCuerpo, enRuta, lectura })
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be(TarifaOtErrores.OtroOrganismo);
        }

        (await e.ContarAsync($"procedure_type_id = '{e.Tipo}' OR transit_office_id IN ('{e.Organismo}', '{e.OtroOrganismo}')", Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC3_ElIdentificadorPropioSiSeAcepta()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);

        var r = await e.ClienteOt().GetAsync($"{Base}?transitOfficeId={e.Organismo}&tipoTramiteId={e.Tipo}", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AC4_ElAdminVeLasDeSuOrganismoYLosGeneralesQueLeAplican_SinPoderEditarEstosUltimos()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        var otroTipo = Guid.NewGuid();
        // Valores sembrados por el SuperAdmin: general del tipo, servicio FLIT del organismo, de OTRO organismo, de otro tipo y envío general.
        await e.SembrarAsync("DERECHOS_TRANSITO", e.Tipo, null, 100_000, hoy, Ct);
        await e.SembrarAsync("SERVICIO_FLIT", e.Tipo, null, 50_000, hoy, Ct);
        await e.SembrarAsync("DERECHOS_TRANSITO", e.Tipo, e.Organismo, 130_000, hoy, Ct);
        await e.SembrarAsync("SERVICIO_FLIT", e.Tipo, e.Organismo, 55_000, hoy, Ct);
        await e.SembrarAsync("DERECHOS_TRANSITO", e.Tipo, e.OtroOrganismo, 999_999, hoy, Ct);
        await e.SembrarAsync("ENVIO_DOCUMENTO", null, null, 20_000, hoy, Ct);
        _ = otroTipo;

        var r = await e.ClienteOt().GetAsync($"{Base}?tipoTramiteId={e.Tipo}&soloVigentes=true", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var filas = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray()
            .Select(f => (
                Concepto: f.GetProperty("concepto").GetString()!,
                Organismo: f.GetProperty("organismoId").ValueKind == JsonValueKind.Null ? (Guid?)null : f.GetProperty("organismoId").GetGuid(),
                Valor: f.GetProperty("valorCop").GetInt64(),
                Editable: f.GetProperty("editable").GetBoolean()))
            .ToList();
        filas.Should().NotContain(f => f.Valor == 999_999, "el valor de otro organismo no se ve");
        filas.Should().Contain((("DERECHOS_TRANSITO"), (Guid?)e.Organismo, 130_000L, true));
        filas.Should().Contain(("DERECHOS_TRANSITO", (Guid?)null, 100_000L, false));
        filas.Should().Contain(("SERVICIO_FLIT", (Guid?)e.Organismo, 55_000L, false));
        filas.Should().Contain(("SERVICIO_FLIT", (Guid?)null, 50_000L, false));
        filas.Should().Contain(("ENVIO_DOCUMENTO", (Guid?)null, 20_000L, false));
    }

    [Fact]
    public async Task AC4_SinTipo_ElAdminDelOrganismoRecibeBadRequest()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);

        var r = await e.ClienteOt().GetAsync(Base, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be(TarifaOtErrores.TipoObligatorio);
    }

    [Fact]
    public async Task AC4_ElHistorialTraeQuienHizoCadaCambio_YElMismoDiaReemplaza()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var client = e.ClienteOt();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        object Cuerpo(long valor) => new { tipoTramiteId = e.Tipo, vigenteDesde = hoy, valores = new object[] { new { concepto = "DERECHOS_TRANSITO", valorCop = valor } } };
        (await client.PostAsJsonAsync(Base, Cuerpo(100_000), Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var segunda = await client.PostAsJsonAsync(Base, Cuerpo(120_000), Ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.Created, "el mismo día reemplaza, sin error de tarifa solapada");
        var id = (await segunda.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Single().GetProperty("id").GetGuid();

        var h = await client.GetAsync($"{Base}/{id}/historial", Ct);

        h.StatusCode.Should().Be(HttpStatusCode.OK);
        var versiones = (await h.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        var version = versiones.Should().ContainSingle().Subject;
        version.GetProperty("valorCop").GetInt64().Should().Be(120_000);
        version.GetProperty("creadaPorNombre").GetString().Should().Be(EscenarioOt.NombreActor);
        version.GetProperty("editable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Historial_DeUnaTarifaDeOtroOrganismo_RecibeForbidden_YDeUnaInexistente_NotFound()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var ajena = await e.SembrarAsync("DERECHOS_TRANSITO", e.Tipo, e.OtroOrganismo, 1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Ct);
        var client = e.ClienteOt();

        (await client.GetAsync($"{Base}/{ajena}/historial", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{Base}/{Guid.NewGuid()}/historial", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tipos_ListaLosTiposHabilitadosParaElPortal()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        await e.HabilitarTipoAsync(Ct);

        var r = await e.ClienteOt().GetAsync($"{Base}/tipos", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var tipos = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        tipos.Should().Contain(t => t.GetProperty("id").GetGuid() == e.Tipo && t.GetProperty("codigo").GetString() == e.CodigoTipo);
    }

    [Fact]
    public async Task ElSuperAdmin_DebeIndicarElOrganismo_YEscribeTodosLosConceptos()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var admin = e.ClienteSuperAdmin();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

        (await admin.GetAsync(Base, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var r = await admin.PostAsJsonAsync($"{Base}?transitOfficeId={e.Organismo}", new
        {
            tipoTramiteId = e.Tipo,
            vigenteDesde = hoy.AddDays(1),
            valores = new object[] { new { concepto = "SERVICIO_FLIT", valorCop = 60_000 }, new { concepto = "ENVIO_DOCUMENTO", valorCop = 15_000 } },
        }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Should().OnlyContain(f => f.GetProperty("editable").GetBoolean());
    }

    [Fact]
    public async Task AC5_LosEndpointsDePlataformaFuncionanIgual_ParaElSuperAdmin_YElAdminDelOrganismoSigueSinAcceso()
    {
        await using var e = await EscenarioOt.CrearAsync(factory, Ct);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        var plataforma = AdminPlataformaPersonaNaturalEndpoints.BasePath;
        var admin = e.ClienteSuperAdmin(conPermiso: true);

        var crear = await admin.PostAsJsonAsync($"{plataforma}/tarifas", new
        {
            tipoTramiteId = e.Tipo,
            organismoId = e.Organismo,
            vigenteDesde = hoy.AddDays(1),
            valores = new object[] { new { concepto = "SERVICIO_FLIT", valorCop = 70_000 } },
        }, Ct);
        crear.StatusCode.Should().Be(HttpStatusCode.Created);
        var creada = (await crear.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Single();
        creada.GetProperty("valorCop").GetInt64().Should().Be(70_000);
        creada.GetProperty("organismoId").GetGuid().Should().Be(e.Organismo);
        var id = creada.GetProperty("id").GetGuid();

        var lista = await admin.GetAsync($"{plataforma}/tarifas?tipoTramiteId={e.Tipo}", Ct);
        lista.StatusCode.Should().Be(HttpStatusCode.OK);
        var historial = await admin.GetAsync($"{plataforma}/tarifas/{id}/historial", Ct);
        historial.StatusCode.Should().Be(HttpStatusCode.OK);
        // El historial de Plataforma suma creadaPorNombre sin quitar nada de lo que ya traía.
        var version = (await historial.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Single();
        version.GetProperty("creadaPorNombre").GetString().Should().Be(EscenarioOt.NombreActor);
        version.GetProperty("vigenteDesde").ValueKind.Should().NotBe(JsonValueKind.Null);

        (await e.ClienteOt().GetAsync($"{plataforma}/tarifas", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static DateOnly Hoy() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5)).AddDays(1);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string Token(string rol, Guid tenant, Guid usuario, bool conPermiso = false)
    {
        var claims = new List<Claim> { new("sub", usuario.ToString()), new("role", rol), new("tenant_id", tenant.ToString()) };
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

    private HttpClient ClientWith(string token, IOtProfileRepository perfiles) => ClientWith(factory, token, perfiles);

    private static TarifaPnView Vista(string concepto, Guid? organismo, Guid? id = null) => new(
        id ?? Guid.NewGuid(), concepto, Guid.NewGuid(), "T", "Tipo", organismo, null, null, 1000, DateOnly.FromDateTime(DateTime.UtcNow),
        null, EstadosTarifaPn.Vigente, DateTimeOffset.UtcNow, null, null, null);

    private static class Ids
    {
        public static readonly Guid DeOtro = Guid.NewGuid();
        public static readonly Guid General = Guid.NewGuid();
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

    /// <summary>Repositorio en memoria: cuenta los versionados y recuerda el organismo con que se registró.</summary>
    private sealed class RepoContador : IPersonaNaturalTarifaRepository
    {
        public int Versionados { get; private set; }
        public Guid? UltimoOrganismo { get; private set; }
        public Func<Guid, TarifaPnView?> Tarifa { get; init; } = _ => null;

        public DateOnly HoyColombia() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        public Task<IReadOnlyList<TarifaPnView>> ListarAsync(TarifaPnFiltro filtro, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TarifaPnView>>([]);
        public Task<IReadOnlyList<TarifaPnView>> ListarDelOrganismoAsync(Guid organismoId, Guid? tipoId, string? concepto, bool soloVigentes, DateOnly? fecha, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TarifaPnView>>([]);
        public Task<IReadOnlyList<TipoTarifaPnView>> ListarTiposDelPortalAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TipoTarifaPnView>>([]);
        public Task<TarifaPnView?> ObtenerAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Tarifa(id));
        public Task<IReadOnlyList<TarifaPnView>?> HistorialAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TarifaPnView>?>(Tarifa(id) is { } t ? [t] : null);
        public Task<bool> TipoExisteAsync(Guid tipoId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> OrganismoExisteAsync(Guid organismoId, CancellationToken ct = default) => Task.FromResult(true);

        public Task<TarifaPnVersionadaResult> VersionarAsync(
            Guid? tipoId, Guid? organismoId, IReadOnlyList<TarifaPnValor> valores, DateOnly vigenteDesde, Guid actor, CancellationToken ct = default)
        {
            Versionados++;
            UltimoOrganismo = organismoId;
            return Task.FromResult(new TarifaPnVersionadaResult(valores.Select(v => Vista(v.Concepto, organismoId)).ToList(), []));
        }

        public Task<IReadOnlyList<TarifaPnView>> VigentesDelTipoEnOrganismoAsync(Guid tipoId, Guid organismoId, DateOnly fecha, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TarifaPnView>>([]);
    }

    /// <summary>Dos organismos, un tipo y un actor reales; todo se retira al terminar.</summary>
    private sealed class EscenarioOt : IAsyncDisposable
    {
        public const string NombreActor = "Actor de precios OT";
        private readonly WebApplicationFactory<Program> _host;
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        public FlitDbContext Db { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid TenantOt { get; } = Guid.NewGuid();
        public Guid Tipo { get; } = Guid.NewGuid();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OtroOrganismo { get; } = Guid.NewGuid();
        public string CodigoTipo => "PN_OT_" + _sufijo;

        private EscenarioOt(WebApplicationFactory<Program> host)
        {
            _host = host;
            _scope = host.Services.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public static async Task<EscenarioOt> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new EscenarioOt(host);
            foreach (var (id, n) in new[] { (e.Organismo, "A"), (e.OtroOrganismo, "B") })
            {
                var code = $"PNO{e._sufijo}{n}";
                await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {code}, {"Organismo de prueba " + code}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }

            var correo = $"pn-ot-{e._sufijo.ToLowerInvariant()}@example.test";
            await e.Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES ({e.Actor}, {correo}, {NombreActor}, 'active', now())", ct);
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({e.Tipo}, {e.CodigoTipo}, {"Tipo de prueba " + e.CodigoTipo}, 'TRASPASO', 1, {"{}"}::jsonb, 'published', now(), true, true, now())
                """, ct);
            return e;
        }

        public HttpClient ClienteOt() => ClientWith(_host, Token("ot_admin", TenantOt, Actor), new PerfilesFalsos { Tenant = TenantOt, Organismo = Organismo });

        public HttpClient ClienteSuperAdmin(bool conPermiso = false) =>
            ClientWith(_host, Token("SuperAdmin", Guid.NewGuid(), Actor, conPermiso), new PerfilesFalsos());

        /// <summary>Siembra una versión como lo haría el SuperAdmin (por la misma función de versionado).</summary>
        public async Task<Guid> SembrarAsync(string concepto, Guid? tipo, Guid? organismo, long valor, DateOnly desde, CancellationToken ct) =>
            (await Db.Database.SqlQuery<Guid>($"""
                SELECT admin.persona_natural_tariff_new_version({concepto}, {tipo}::uuid, {organismo}::uuid, {valor}, {desde}, {Actor}) AS "Value"
                """).ToListAsync(ct)).Single();

        public Task HabilitarTipoAsync(CancellationToken ct) =>
            Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO admin.persona_natural_enabled_types (procedure_type_id, is_enabled) VALUES ({Tipo}, true)", ct);

        public async Task<int> ContarAsync(string condicion, CancellationToken ct) =>
            (await Db.Database.SqlQueryRaw<int>($"""SELECT count(*)::int AS "Value" FROM admin.persona_natural_tariffs WHERE {condicion}""").ToListAsync(ct)).Single();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var tx = await Db.Database.BeginTransactionAsync();
                await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var orgs = new[] { Organismo, OtroOrganismo };
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE procedure_type_id = {Tipo} OR transit_office_id = ANY({orgs}) OR created_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_enabled_types WHERE procedure_type_id = {Tipo}");
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

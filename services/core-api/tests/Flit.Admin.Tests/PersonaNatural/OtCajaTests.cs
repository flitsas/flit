using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Endpoints;
using Flit.Api.PersonaNatural;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// La caja del organismo como módulo de FLIT 2.0 (<c>/api/v1/ot/caja</c>, Feature #14453, ADR-0082): permisos (<c>ot_caja</c> y Admin OT, no
/// SuperAdmin; anular solo el administrador), organismo y actor desde el token, respuestas idénticas a las del portal anterior, portal
/// caído = 503 <c>portal_no_disponible</c> y bandera <c>PortalCaja:Habilitado</c>. El portal y la base se reemplazan por dobles (el host
/// arranca sin PostgreSQL); el guardián solo-caja se prueba también de punta a punta contra el pipeline real.
/// </summary>
public sealed class OtCajaTests : IClassFixture<OtCajaTests.Host>
{
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid OrganismoA = Guid.Parse("0199b7e0-0000-7000-8000-0000000000a1");
    private static readonly Guid OrganismoB = Guid.Parse("0199b7e0-0000-7000-8000-0000000000b2");
    private static readonly Guid TenantA = Guid.Parse("0199b7e0-0000-7000-8000-0000000001a1");
    private static readonly Guid TenantB = Guid.Parse("0199b7e0-0000-7000-8000-0000000001b2");
    private static readonly Guid CajeraA = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid CajeraDesactivada = Guid.Parse("00000000-0000-0000-0000-00000000a002");
    private static readonly Guid AdminA = Guid.Parse("00000000-0000-0000-0000-00000000ad01");
    private static readonly Guid AdminB = Guid.Parse("00000000-0000-0000-0000-00000000ad02");
    private static readonly Guid SuperAdmin = Guid.Parse("00000000-0000-0000-0000-00000000ff01");

    private readonly Host _host;

    public OtCajaTests(Host host)
    {
        _host = host;
        _host.Portal.Reiniciar();
    }

    // ── Bandera y autenticación ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConLaFuncionApagada_LasRutasDeCajaNoExisten_404()
    {
        using var client = ClienteDe(_host.Base, "ot_caja", TenantA, CajeraA);

        (await client.GetAsync("/api/v1/ot/caja/tramites/PN-2026-000101", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/ot/caja/cobros", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SinToken_401()
    {
        using var client = _host.Factory.CreateClient();

        (await client.GetAsync("/api/v1/ot/caja/cobros", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _host.Portal.Llamadas.Should().BeEmpty();
    }

    // ── Permisos ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("gestor_ot")]
    [InlineData("AdminCompany")]
    public async Task OtrosRoles_UnSuperAdminIncluido_403SinPermiso_YNoSeTocaElPortal(string rol)
    {
        using var client = ClienteDe(_host.Factory, rol, TenantA, SuperAdmin);

        var r = await client.GetAsync("/api/v1/ot/caja/cobros", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodigoAsync(r)).Should().Be("sin_permiso");
        _host.Portal.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task UnUsuarioDeCajaDesactivado_PierdeElAccesoEnLaSiguientePeticion_403()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraDesactivada);

        var r = await client.GetAsync("/api/v1/ot/caja/tramites/PN-2026-000101", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodigoAsync(r)).Should().Be("sin_permiso");
        _host.Portal.Llamadas.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ot_caja")]
    [InlineData("ot_admin")]
    public async Task CajaYAdminOt_PuedenBuscarCobrarYListar(string rol)
    {
        using var client = ClienteDe(_host.Factory, rol, TenantA, rol == "ot_caja" ? CajeraA : AdminA);

        (await client.GetAsync("/api/v1/ot/caja/tramites/PN-2026-000101", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/api/v1/ot/caja/tramites/PN-2026-000101/confirmar", Formulario("efectivo"), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/ot/caja/cobros", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ElOrganismoSaleDelToken_NuncaDelCliente()
    {
        using var client = ClienteDe(_host.Factory, "ot_admin", TenantB, AdminB);

        await client.GetAsync($"/api/v1/ot/caja/tramites/PN-2026-000101?transitOfficeId={OrganismoA}&organismoId={OrganismoA}", Ct);
        await client.GetAsync($"/api/v1/ot/caja/cobros?transitOfficeId={OrganismoA}", Ct);

        _host.Portal.Llamadas.Should().OnlyContain(l => l.Organismo == OrganismoB);
        _host.Portal.Llamadas.Should().HaveCount(2);
    }

    // ── Buscar ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Buscar_DevuelveElMismoJsonQueElPortalAnterior()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);

        var r = await client.GetAsync("/api/v1/ot/caja/tramites/PN-2026-000101", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = (await JsonAsync(r)).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("codigo", "tipoTramite", "organismo", "lineas", "totalCop", "versionTarifa", "venceEn");
        json.GetProperty("codigo").GetString().Should().Be("PN-2026-000101");
        json.GetProperty("tipoTramite").GetProperty("codigo").GetString().Should().Be("TRASPASO_STANDARD");
        json.GetProperty("tipoTramite").GetProperty("nombre").GetString().Should().Be("Traspaso");
        json.GetProperty("organismo").GetProperty("codigo").GetString().Should().Be("AB1");
        json.GetProperty("organismo").GetProperty("nombre").GetString().Should().Be("Tránsito A");
        json.GetProperty("totalCop").GetInt64().Should().Be(366_000);
        json.GetProperty("lineas").GetArrayLength().Should().Be(2);
        json.GetProperty("lineas")[0].GetProperty("concepto").GetString().Should().Be("Valor del Servicio de tránsito");
        json.GetProperty("lineas")[0].GetProperty("valorCop").GetInt64().Should().Be(300_000);
        json.GetProperty("venceEn").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 10, 27, 23, 59, 59, TimeSpan.FromHours(-5)));

        var llamada = _host.Portal.Llamadas.Should().ContainSingle().Subject;
        (llamada.Organismo, llamada.Usuario, llamada.Codigo).Should().Be((OrganismoA, CajeraA, "PN-2026-000101"));
        llamada.IpHash.Should().MatchRegex("^[0-9a-f]{64}$", "la IP en claro no viaja al portal");
    }

    [Theory]
    [InlineData("no_encontrado", 404, "No encontramos ese trámite.")]
    [InlineData("otro_organismo", 403, "Este trámite no es de este organismo.")]
    [InlineData("ya_pagado", 409, "Este trámite ya fue pagado.")]
    [InlineData("cobro_vencido", 409, "El pago en caja de este trámite venció.")]
    [InlineData("no_habilitado", 409, "Este trámite no tiene el pago en caja habilitado.")]
    public async Task Buscar_ElRechazoDelPortalLlegaConSuCodigoYSuMensaje(string codigo, int http, string mensaje)
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);
        _host.Portal.Rechazo = new CajaPortalRechazoException(codigo, mensaje, http, new Dictionary<string, string>());

        var r = await client.GetAsync("/api/v1/ot/caja/tramites/PN-2026-000101", Ct);

        ((int)r.StatusCode).Should().Be(http);
        var json = (await JsonAsync(r)).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("code", "message");
        (json.GetProperty("code").GetString(), json.GetProperty("message").GetString()).Should().Be((codigo, mensaje));
    }

    [Fact]
    public async Task Buscar_ElTopeDeBusquedas_429ConRetryAfter()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);
        _host.Portal.Rechazo = new CajaPortalRechazoException(
            "demasiados_intentos", "Demasiadas búsquedas sin resultado. Espere unos minutos.", 429, new Dictionary<string, string> { ["retryAfterSegundos"] = "187" });

        var r = await client.GetAsync("/api/v1/ot/caja/tramites/PN-2026-000101", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        r.Headers.GetValues("Retry-After").Single().Should().Be("187");
        var json = (await JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("demasiados_intentos");
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("code", "message");
    }

    // ── Confirmar ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirmar_ReenviaMedioDetalleTotalLlavePrefacturaYElActor_YResponde200ConElMismoJson()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nprefactura");
        using var form = Formulario("otro", detalle: "Nequi", totalVisto: 366_000, prefactura: ("p.pdf", pdf));

        var r = await EnviarConfirmarAsync(client, "PN-2026-000101", form, llave: "k-123");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await JsonAsync(r)).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("codigo", "valorCop", "medio", "medioDetalle", "pagadoEn", "anulableHasta", "mensaje");
        (json.GetProperty("codigo").GetString(), json.GetProperty("valorCop").GetInt64(), json.GetProperty("medio").GetString(), json.GetProperty("mensaje").GetString())
            .Should().Be(("PN-2026-000101", 366_000L, "otro", "Pago registrado."));
        json.GetProperty("medioDetalle").GetString().Should().Be("Nequi");
        var c = _host.Portal.Confirmaciones.Should().ContainSingle().Subject;
        (c.OrganismoId, c.UsuarioId, c.UsuarioNombre, c.Medio, c.Detalle, c.TotalVisto, c.Llave).Should().Be((OrganismoA, CajeraA, "Ana Caja", "otro", "Nequi", 366_000L, "k-123"));
        c.Prefactura.Should().Equal(pdf);
        c.PrefacturaNombre.Should().Be("p.pdf");
        c.IpHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Confirmar_AceptaLosNombresDelFormularioDelPortalAnterior()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("efectivo"), "medio" },
            { new StringContent("366000"), "totalVistoCop" },
        };

        var r = await EnviarConfirmarAsync(client, "PN-2026-000101", form, llave: "k-viejo");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        _host.Portal.Confirmaciones.Single().Should().Match<CajaConfirmacion>(c => c.Medio == "efectivo" && c.TotalVisto == 366_000);
    }

    [Fact]
    public async Task Confirmar_SinCuerpoDeFormulario_400MedioInvalido_SinTocarElPortal()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);

        var r = await client.PostAsJsonAsync("/api/v1/ot/caja/tramites/PN-2026-000101/confirmar", new { medioCaja = "efectivo" }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(r)).Should().Be("medio_invalido");
        _host.Portal.Confirmaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirmar_SinIdempotencyKey_ElPortalLaRechaza_400()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);
        _host.Portal.Rechazo = new CajaPortalRechazoException("idempotency_key_requerida", "Falta la clave de la operación.", 400, new Dictionary<string, string>());

        var r = await EnviarConfirmarAsync(client, "PN-2026-000101", Formulario("efectivo"), llave: null);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(r)).Should().Be("idempotency_key_requerida");
        _host.Portal.Confirmaciones.Single().Llave.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirmar_ElTotalCambio_409ConElNuevoTotal()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);
        _host.Portal.Rechazo = new CajaPortalRechazoException(
            "total_cambio", "El total cambió; revise el nuevo valor.", 409, new Dictionary<string, string> { ["totalActualCop"] = "381000" });

        var r = await EnviarConfirmarAsync(client, "PN-2026-000101", Formulario("efectivo", totalVisto: 366_000), llave: "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = (await JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("total_cambio");
        json.GetProperty("message").GetString().Should().Be("El total cambió; revise el nuevo valor.");
        json.GetProperty("totalActualCop").GetInt64().Should().Be(381_000);
    }

    [Theory]
    [InlineData("medio_invalido", 400)]
    [InlineData("detalle_requerido", 400)]
    [InlineData("prefactura_invalida", 400)]
    [InlineData("idempotency_key_reutilizada", 409)]
    [InlineData("ya_pagado", 409)]
    public async Task Confirmar_LosCodigosDelPortalSePasanTalCual(string codigo, int http)
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);
        _host.Portal.Rechazo = new CajaPortalRechazoException(codigo, "mensaje del portal", http, new Dictionary<string, string>());

        var r = await EnviarConfirmarAsync(client, "PN-2026-000101", Formulario("efectivo"), llave: "k-1");

        ((int)r.StatusCode).Should().Be(http);
        (await CodigoAsync(r)).Should().Be(codigo);
    }

    [Fact]
    public async Task Confirmar_UnaPrefacturaVaciaOMayorDe5MB_400PrefacturaInvalida_SinLlegarAlPortal()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);

        var vacia = await EnviarConfirmarAsync(client, "PN-2026-000101", Formulario("efectivo", prefactura: ("p.pdf", [])), llave: "k-1");
        var grande = await EnviarConfirmarAsync(client, "PN-2026-000101", Formulario("efectivo", prefactura: ("p.pdf", new byte[(5 * 1024 * 1024) + 1])), llave: "k-2");

        foreach (var r in new[] { vacia, grande })
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodigoAsync(r)).Should().Be("prefactura_invalida");
        }

        _host.Portal.Confirmaciones.Should().BeEmpty();
    }

    // ── Lista del día ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cobros_DevuelveLaListaDelDiaConLaForma_DelPortalAnterior_MasLoNecesarioParaAnular()
    {
        using var client = ClienteDe(_host.Factory, "ot_admin", TenantA, AdminA);
        var cobro = Guid.NewGuid();
        _host.Portal.Lista = new CajaCobrosLista(
            [new CajaCobroResumen(cobro.ToString(), "PN-2026-000101", 366_000, "otro", "Nequi", "2026-10-27T17:30:00.0000000+00:00", "Beto Caja", "pagado", true, string.Empty, string.Empty, string.Empty, string.Empty, 7)],
            Total: 1, TotalDiaCop: 366_000);

        var r = await client.GetAsync("/api/v1/ot/caja/cobros?date=2026-10-27&page=2&pageSize=500", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await JsonAsync(r)).RootElement;
        json.GetProperty("fecha").GetString().Should().Be("2026-10-27");
        json.GetProperty("totalDiaCop").GetInt64().Should().Be(366_000);
        json.GetProperty("total").GetInt32().Should().Be(1);
        json.GetProperty("pagina").GetInt32().Should().Be(2);
        json.GetProperty("tamano").GetInt32().Should().Be(50, "el tamaño de página tiene tope 50");
        var item = json.GetProperty("items")[0];
        (item.GetProperty("codigo").GetString(), item.GetProperty("valorCop").GetInt64(), item.GetProperty("medio").GetString(), item.GetProperty("medioDetalle").GetString())
            .Should().Be(("PN-2026-000101", 366_000L, "otro", "Nequi"));
        item.GetProperty("hora").GetString().Should().Be("12:30", "hora de Colombia");
        (item.GetProperty("usuario").GetString(), item.GetProperty("estado").GetString()).Should().Be(("Beto Caja", "pagado"));
        (item.GetProperty("cobroId").GetGuid(), item.GetProperty("anulable").GetBoolean(), item.GetProperty("rowVersion").GetInt64()).Should().Be((cobro, true, 7L));
        item.GetProperty("motivoNoAnulable").ValueKind.Should().Be(JsonValueKind.Null);
        var llamada = _host.Portal.Llamadas.Single();
        (llamada.Organismo, llamada.Fecha, llamada.Pagina, llamada.Tamano).Should().Be((OrganismoA, "2026-10-27", 2, 50));
    }

    [Fact]
    public async Task Cobros_SinParametros_HoyPagina1De10_YUnaFechaMalaDa400()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);

        var ok = await client.GetAsync("/api/v1/ot/caja/cobros", Ct);
        var mala = await client.GetAsync("/api/v1/ot/caja/cobros?date=27-10-2026", Ct);

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await JsonAsync(ok)).RootElement;
        json.GetProperty("fecha").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
        (json.GetProperty("pagina").GetInt32(), json.GetProperty("tamano").GetInt32()).Should().Be((1, 10));
        mala.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(mala)).Should().Be("fecha_invalida");
    }

    // ── Anular ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anular_ElUsuarioDeCajaNuncaAnula_403_SinTocarElPortal()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);

        var r = await client.PostAsJsonAsync($"/api/v1/ot/caja/cobros/{Guid.NewGuid()}/anular", new { motivo = "Cobrado por error en la caja", rowVersion = 3 }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodigoAsync(r)).Should().Be("sin_permiso");
        _host.Portal.Anulaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Anular_ElAdminOt_AnulaConSuIdYNombre_ElOrganismoDeSuToken()
    {
        using var client = ClienteDe(_host.Factory, "ot_admin", TenantA, AdminA);
        var cobro = Guid.NewGuid();

        var r = await client.PostAsJsonAsync($"/api/v1/ot/caja/cobros/{cobro}/anular?transitOfficeId={OrganismoB}", new { motivo = "  Cobrado por error en la caja  ", rowVersion = 7 }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await JsonAsync(r)).RootElement;
        (json.GetProperty("cobroId").GetGuid(), json.GetProperty("estado").GetString()).Should().Be((cobro, "anulado"));
        var a = _host.Portal.Anulaciones.Should().ContainSingle().Subject;
        (a.Organismo, a.Cobro, a.Motivo, a.Actor, a.ActorNombre, a.RowVersion).Should().Be((OrganismoA, cobro, "Cobrado por error en la caja", AdminA, "Admin A", 7L));
    }

    [Theory]
    [InlineData(null, 3, "motivo_requerido")]
    [InlineData("corto", 3, "motivo_longitud")]
    [InlineData("Motivo suficiente de anulación", null, "version_requerida")]
    [InlineData("Motivo suficiente de anulación", 0, "version_requerida")]
    public async Task Anular_ValidaMotivoDe10A300_YRowVersionMayorQueCero(string? motivo, int? rowVersion, string codigo)
    {
        using var client = ClienteDe(_host.Factory, "ot_admin", TenantA, AdminA);

        var r = await client.PostAsJsonAsync($"/api/v1/ot/caja/cobros/{Guid.NewGuid()}/anular", new { motivo, rowVersion }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(r)).Should().Be(codigo);
        _host.Portal.Anulaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Anular_MotivoDeMasDe300_400()
    {
        using var client = ClienteDe(_host.Factory, "ot_admin", TenantA, AdminA);

        var r = await client.PostAsJsonAsync($"/api/v1/ot/caja/cobros/{Guid.NewGuid()}/anular", new { motivo = new string('x', 301), rowVersion = 3 }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(r)).Should().Be("motivo_longitud");
    }

    [Fact]
    public async Task Anular_UnCobroQueYaLlegoAFlit2_409_YUnoAjeno404()
    {
        using var client = ClienteDe(_host.Factory, "ot_admin", TenantA, AdminA);
        var cuerpo = new { motivo = "Motivo suficiente de anulación", rowVersion = 3 };

        _host.Portal.ResultadoAnular = new CajaAnulacionResultado(CajaAnulacionResultado.NoAnulable, string.Empty);
        var noAnulable = await client.PostAsJsonAsync($"/api/v1/ot/caja/cobros/{Guid.NewGuid()}/anular", cuerpo, Ct);
        _host.Portal.ResultadoAnular = new CajaAnulacionResultado(CajaAnulacionResultado.NoEncontrado, string.Empty);
        var ajeno = await client.PostAsJsonAsync($"/api/v1/ot/caja/cobros/{Guid.NewGuid()}/anular", cuerpo, Ct);

        noAnulable.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(noAnulable)).Should().Be("cobro_no_anulable");
        ajeno.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodigoAsync(ajeno)).Should().Be("cobro_no_encontrado");
    }

    // ── Portal caído ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConElPortalCaido_TodasLasRutasResponden503PortalNoDisponible()
    {
        using var admin = ClienteDe(_host.Factory, "ot_admin", TenantA, AdminA);
        _host.Portal.Caido = true;

        var respuestas = new[]
        {
            await admin.GetAsync("/api/v1/ot/caja/tramites/PN-2026-000101", Ct),
            await EnviarConfirmarAsync(admin, "PN-2026-000101", Formulario("efectivo"), llave: "k-1"),
            await admin.GetAsync("/api/v1/ot/caja/cobros", Ct),
            await admin.PostAsJsonAsync($"/api/v1/ot/caja/cobros/{Guid.NewGuid()}/anular", new { motivo = "Motivo suficiente de anulación", rowVersion = 3 }, Ct),
        };

        foreach (var r in respuestas)
        {
            r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await CodigoAsync(r)).Should().Be("portal_no_disponible");
        }
    }

    // ── Guardián solo-caja de punta a punta ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v1/admin/ot/portal-caja/users")]
    [InlineData("/api/v1/tramites/instances")]
    [InlineData("/api/v1/admin/ot/users")]
    public async Task ElTokenSoloCaja_NoUsaElRestoDeLaApi_403CajaSinAcceso(string ruta)
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);

        var r = await client.GetAsync(ruta, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodigoAsync(r)).Should().Be("caja_sin_acceso");
    }

    [Fact]
    public async Task ElTokenSoloCaja_SiUsaLaCaja()
    {
        using var client = ClienteDe(_host.Factory, "ot_caja", TenantA, CajeraA);

        (await client.GetAsync("/api/v1/ot/caja/cobros", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Ayudas ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static MultipartFormDataContent Formulario(string? medio, string? detalle = null, long? totalVisto = null, (string Nombre, byte[] Bytes)? prefactura = null)
    {
        var form = new MultipartFormDataContent();
        if (medio is not null)
            form.Add(new StringContent(medio), "medioCaja");
        if (detalle is not null)
            form.Add(new StringContent(detalle), "detalle");
        if (totalVisto is { } t)
            form.Add(new StringContent(t.ToString(System.Globalization.CultureInfo.InvariantCulture)), "totalVisto");
        if (prefactura is { } p)
        {
            var archivo = new ByteArrayContent(p.Bytes);
            archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(archivo, "prefactura", p.Nombre);
        }

        return form;
    }

    private static async Task<HttpResponseMessage> EnviarConfirmarAsync(HttpClient client, string codigo, MultipartFormDataContent form, string? llave)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ot/caja/tramites/{codigo}/confirmar") { Content = form };
        if (llave is not null)
            peticion.Headers.Add("Idempotency-Key", llave);
        return await client.SendAsync(peticion, Ct);
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct));

    private static async Task<string?> CodigoAsync(HttpResponseMessage r) => (await JsonAsync(r)).RootElement.GetProperty("code").GetString();

    private static HttpClient ClienteDe(WebApplicationFactory<Program> factory, string role, Guid tenantId, Guid userId)
    {
        var client = factory.CreateClient();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("entity_type", "TRANSIT_OFFICE"),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Dobles ──────────────────────────────────────────────────────────────────────────────────────────────────

    public sealed class Host : HostPorClase
    {
        internal FakePortal Portal { get; } = new();

        protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> factory) =>
            factory.WithWebHostBuilder(b =>
            {
                b.UseSetting("PortalCaja:Habilitado", "true");
                b.UseSetting("PersonaNatural:Portal:Address", "http://localhost:9");
                b.ConfigureTestServices(s =>
                {
                    s.RemoveAll<ICajaAdminClient>();
                    s.AddSingleton<ICajaAdminClient>(Portal);
                    s.RemoveAll<IActorCajaStore>();
                    s.AddSingleton<IActorCajaStore>(new FakeActores());
                });
            });
    }

    internal sealed class FakeActores : IActorCajaStore
    {
        public Task<ActorCaja?> ResolverAsync(bool esAdmin, Guid usuarioId, Guid tenantId, CancellationToken ct)
        {
            ActorCaja? actor = (esAdmin, usuarioId, tenantId) switch
            {
                (false, var u, var t) when u == CajeraA && t == TenantA => new ActorCaja(u, "Ana Caja", OrganismoA, "AB1", "Tránsito A", EsAdmin: false),
                (true, var u, var t) when u == AdminA && t == TenantA => new ActorCaja(u, "Admin A", OrganismoA, "AB1", "Tránsito A", EsAdmin: true),
                (true, var u, var t) when u == AdminB && t == TenantB => new ActorCaja(u, "Admin B", OrganismoB, "ZZ9", "Tránsito B", EsAdmin: true),
                _ => null,
            };
            return Task.FromResult(actor);
        }
    }

    public sealed record LlamadaPortal(Guid Organismo, string Tipo, Guid? Usuario = null, string? Codigo = null, string? IpHash = null, string? Fecha = null, int Pagina = 0, int Tamano = 0);

    public sealed record AnulacionLlamada(Guid Organismo, Guid Cobro, string Motivo, Guid Actor, string ActorNombre, long RowVersion);

    internal sealed class FakePortal : ICajaAdminClient
    {
        public List<LlamadaPortal> Llamadas { get; } = [];

        public List<CajaConfirmacion> Confirmaciones { get; } = [];

        public List<AnulacionLlamada> Anulaciones { get; } = [];

        public CajaPortalRechazoException? Rechazo { get; set; }

        public bool Caido { get; set; }

        public CajaCobrosLista Lista { get; set; } = new([], 0, 0);

        public CajaAnulacionResultado ResultadoAnular { get; set; } = new(null, "anulado");

        public void Reiniciar()
        {
            Llamadas.Clear();
            Confirmaciones.Clear();
            Anulaciones.Clear();
            Rechazo = null;
            Caido = false;
            Lista = new([], 0, 0);
            ResultadoAnular = new(null, "anulado");
        }

        private void Verificar()
        {
            if (Caido)
                throw new CajaPortalNoDisponibleException(new InvalidOperationException("caído"));
            if (Rechazo is not null)
                throw Rechazo;
        }

        public Task<CajaTramite> BuscarAsync(Guid organismoId, string codigo, Guid usuarioId, string ipHash, CancellationToken ct)
        {
            Llamadas.Add(new LlamadaPortal(organismoId, "buscar", usuarioId, codigo, ipHash));
            Verificar();
            return Task.FromResult(new CajaTramite(
                codigo, "TRASPASO_STANDARD", "Traspaso", [new CajaLinea("Valor del Servicio de tránsito", 300_000), new CajaLinea("Servicio FLIT", 66_000)], 366_000, "2026-10-01",
                new DateTimeOffset(2026, 10, 27, 23, 59, 59, TimeSpan.FromHours(-5))));
        }

        public Task<CajaCobroConfirmado> ConfirmarAsync(CajaConfirmacion pedido, CancellationToken ct)
        {
            Confirmaciones.Add(pedido);
            Llamadas.Add(new LlamadaPortal(pedido.OrganismoId, "confirmar", pedido.UsuarioId, pedido.Codigo));
            Verificar();
            return Task.FromResult(new CajaCobroConfirmado(
                pedido.Codigo, 366_000, pedido.Medio, pedido.Medio == "otro" ? pedido.Detalle : null, new DateTimeOffset(2026, 10, 27, 12, 30, 0, TimeSpan.FromHours(-5))));
        }

        public Task<CajaCobrosLista> ListarAsync(Guid organismoId, string? fecha, int pagina, int tamano, CancellationToken ct)
        {
            Llamadas.Add(new LlamadaPortal(organismoId, "listar", Fecha: fecha, Pagina: pagina, Tamano: tamano));
            Verificar();
            return Task.FromResult(Lista);
        }

        public Task<CajaAnulacionResultado> AnularAsync(Guid organismoId, Guid cobroId, string motivo, Guid actorUserId, string actorNombre, long rowVersion, CancellationToken ct)
        {
            Llamadas.Add(new LlamadaPortal(organismoId, "anular", actorUserId));
            Verificar();
            Anulaciones.Add(new AnulacionLlamada(organismoId, cobroId, motivo, actorUserId, actorNombre, rowVersion));
            return Task.FromResult(ResultadoAnular);
        }
    }
}

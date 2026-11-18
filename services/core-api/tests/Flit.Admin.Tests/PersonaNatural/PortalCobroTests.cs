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
using Flit.Tramites.Domain.Tramites.PersonaNatural;
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
public sealed class PortalCobroTests(PortalCobroTests.Anfitrion anfitrion) : IClassFixture<PortalCobroTests.Anfitrion>
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
    public void MontoDeReferencia_CoincideConLoQueCobraLaCotizacion()
    {
        var tipo = Guid.NewGuid();
        var otroTipo = Guid.NewGuid();
        var organismoA = Guid.NewGuid();
        var organismoB = Guid.NewGuid();
        const string Concepto = "DERECHOS_TRANSITO";
        TarifaVigente T(Guid? t, Guid? o, long v) => new(Concepto, t, o, v, Guid.NewGuid());

        // Solo con valor de organismos: la web muestra el mayor; el organismo, el suyo.
        TarifaVigente[] soloOrganismo = [T(tipo, organismoA, 50_000), T(tipo, organismoB, 70_000), T(otroTipo, organismoA, 90_000)];
        CotizadorPersonaNatural.MontoDeReferencia(Concepto, tipo, null, soloOrganismo).Should().Be(70_000);
        CotizadorPersonaNatural.MontoDeReferencia(Concepto, tipo, organismoA, soloOrganismo).Should().Be(50_000);

        // Con valor general, la web usa ese y no el de un organismo.
        TarifaVigente[] conGeneral = [T(tipo, null, 30_000), T(tipo, organismoB, 70_000)];
        CotizadorPersonaNatural.MontoDeReferencia(Concepto, tipo, null, conGeneral).Should().Be(30_000);

        // Un valor general de cualquier tipo también cuenta, y sin ningún valor no hay monto.
        TarifaVigente[] general = [T(null, null, 20_000)];
        CotizadorPersonaNatural.MontoDeReferencia(Concepto, tipo, null, general).Should().Be(20_000);
        CotizadorPersonaNatural.MontoDeReferencia(Concepto, tipo, null, []).Should().BeNull();
    }

    [Fact]
    public async Task GuardarUnMedio_DeUnOrganismoInexistente_NoGuarda_YDiceElMotivo()
    {
        var repo = new CobroEnMemoria();
        var alcance = new AlcanceCobroPn(Guid.NewGuid(), EsSuperAdmin: true);
        var tarifas = new TarifasFalsas { OrganismoExiste = false };

        (await new GuardarMedioCobroPnHandler(repo, tarifas, new AuditoriaFalsa()).HandleAsync(alcance, "PASARELA", false, Guid.NewGuid(), Ct))
            .Should().Be(CobroPnErrores.OrganismoNoExiste);
        repo.Guardados.Should().Be(0);
    }

    [Fact]
    public async Task AC5_EnLaWebNoSeOfreceNiSeGuardaPin_NiComoBanderaNiComoSwitch()
    {
        var repo = new CobroEnMemoria();
        var web = new AlcanceCobroPn(null, true);

        (await new GuardarMedioCobroPnHandler(repo, new TarifasFalsas(), new AuditoriaFalsa()).HandleAsync(web, "PIN", true, Guid.NewGuid(), Ct)).Should().Be(CobroPnErrores.PinEnWeb);
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
        var todas = anfitrion.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var ot = todas.Where(e => e.RoutePattern.RawText!.StartsWith(BaseOt, StringComparison.Ordinal)).ToList();
        var plataforma = todas.Where(e => e.RoutePattern.RawText!.StartsWith(BasePlataforma, StringComparison.Ordinal)).ToList();

        ot.Should().HaveCount(5);
        plataforma.Should().HaveCount(5);
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
        var client = ClientWith(anfitrion, Token(rol, Guid.NewGuid(), Guid.NewGuid()));

        (await client.GetAsync(BaseOt, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(BasePlataforma, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SinSesion_RecibeUnauthorized() =>
        (await anfitrion.Factory.CreateClient().GetAsync(BaseOt, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    // ── Endpoints contra la base ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ApagarUnMedioDelPunto_NoTocaOtrosPortales()
    {
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
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
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
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
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
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
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
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
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
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
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
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
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
        var ot = e.ClienteOt();

        (await ot.GetAsync($"{BaseOt}?transitOfficeId={e.OtroOrganismo}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ot.PutAsJsonAsync($"{BaseOt}/medios?transitOfficeId={e.OtroOrganismo}", new { method = "PASARELA", enabled = false }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await e.ClienteSuperAdmin().GetAsync(BaseOt, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "el SuperAdmin debe indicar el organismo en la ruta del OT");
    }

    // ── Ventana de anulación de la caja por organismo (revisión 2026-11-17 de ADR-0082) ────────────────────────────────

    [Fact]
    public async Task CajaAnulacion_SinConfigurar_Es15PorDefecto_ConElMaximoVigente()
    {
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);

        var json = await LeerAsync(e.ClienteOt());

        var v = json.GetProperty("cajaAnulacion");
        (v.GetProperty("minutos").GetInt32(), v.GetProperty("porDefecto").GetBoolean(), v.GetProperty("maxMinutos").GetInt32()).Should().Be((15, true, 60));
    }

    [Fact]
    public async Task CajaAnulacion_ElOtAdminCambiaLaDeSuOrganismo_YQuedaEnElHistorial()
    {
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
        var ot = e.ClienteOt();

        var r = await ot.PutAsJsonAsync($"{BaseOt}/caja-anulacion", new { minutos = 10 }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (cuerpo.GetProperty("minutos").GetInt32(), cuerpo.GetProperty("porDefecto").GetBoolean()).Should().Be((10, false));
        (await LeerAsync(ot)).GetProperty("cajaAnulacion").GetProperty("minutos").GetInt32().Should().Be(10);
        (await LeerAsync(e.ClienteOtroOt())).GetProperty("cajaAnulacion").GetProperty("minutos").GetInt32().Should().Be(15, "no toca otros organismos");
        await ot.PutAsJsonAsync($"{BaseOt}/caja-anulacion", new { minutos = 0 }, Ct);
        var historial = (await ot.GetFromJsonAsync<JsonElement>($"{BaseOt}/caja-anulacion/historial", Ct)).GetProperty("items").EnumerateArray().ToList();
        historial.Select(h => (h.GetProperty("minutosAntes").GetInt32(), h.GetProperty("minutosDespues").GetInt32())).Should().Equal((10, 0), (15, 10));
        historial[0].GetProperty("actorNombre").GetString().Should().Be("Actor de cobro");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task CajaAnulacion_FueraDeRango_Responde400_YNoGuarda(int minutos)
    {
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);
        var ot = e.ClienteOt();

        var r = await ot.PutAsJsonAsync($"{BaseOt}/caja-anulacion", new { minutos }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("ventana_anulacion_invalida");
        (await LeerAsync(ot)).GetProperty("cajaAnulacion").GetProperty("porDefecto").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CajaAnulacion_SinMinutos_Responde400()
    {
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);

        var r = await e.ClienteOt().PutAsJsonAsync($"{BaseOt}/caja-anulacion", new { }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("ventana_anulacion_invalida");
    }

    [Fact]
    public async Task CajaAnulacion_ElSuperAdminCambiaCualquierOrganismo_PeroUnOtAdminAjenoRecibe403_YElCajeroTambien()
    {
        await using var e = await Escenario.CrearAsync(anfitrion, Ct);

        (await e.ClienteSuperAdmin().PutAsJsonAsync($"{BasePlataforma}/caja-anulacion?transitOfficeId={e.OtroOrganismo}", new { minutos = 30 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await e.ClienteSuperAdmin().PutAsJsonAsync($"{BaseOt}/caja-anulacion?transitOfficeId={e.Organismo}", new { minutos = 20 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await e.ClienteSuperAdmin().PutAsJsonAsync($"{BasePlataforma}/caja-anulacion", new { minutos = 20 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "la web de persona no tiene caja");
        (await LeerAsync(e.ClienteOtroOt())).GetProperty("cajaAnulacion").GetProperty("minutos").GetInt32().Should().Be(30);
        (await e.ClienteOt().PutAsJsonAsync($"{BaseOt}/caja-anulacion?transitOfficeId={e.OtroOrganismo}", new { minutos = 1 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var cajero = ClientWith(anfitrion, Token("ot_caja", e.TenantOt, e.Actor));
        (await cajero.PutAsJsonAsync($"{BaseOt}/caja-anulacion", new { minutos = 1 }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cajero.GetAsync($"{BaseOt}/caja-anulacion/historial", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Ventana de anulación (reglas, sin base) ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(-1, false)]
    public void CajaAnulacion_Rango_0AlMaximoConfigurado(int minutos, bool valido) =>
        new CajaAnulacionOpciones().Valida(minutos).Should().Be(valido);

    [Fact]
    public void CajaAnulacion_ElMaximoSaleDeLaConfiguracion()
    {
        var o = new CajaAnulacionOpciones(15, 120);

        o.Valida(120).Should().BeTrue();
        o.Valida(121).Should().BeFalse();
        o.Valida(null).Should().BeFalse();
    }

    [Fact]
    public async Task CajaAnulacion_Handlers_DefaultSinConfigurar_GuardaEHistorialSoloSiCambia()
    {
        var repo = new CajaAnulacionEnMemoria();
        var org = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var opciones = new CajaAnulacionOpciones();
        var leer = new LeerVentanaAnulacionCajaHandler(repo, opciones);
        var guardar = new GuardarVentanaAnulacionCajaHandler(repo, new TarifasFalsas(), new AuditoriaFalsa(), opciones);

        (await leer.HandleAsync(org, Ct)).Should().Be(new VentanaAnulacionCajaView(15, true, 60));
        (await guardar.HandleAsync(org, 61, actor, Ct)).Falla.Should().Be(CajaAnulacionErrores.VentanaInvalida);
        (await guardar.HandleAsync(org, null, actor, Ct)).Falla.Should().Be(CajaAnulacionErrores.VentanaInvalida);
        repo.Valores.Should().BeEmpty();
        (await guardar.HandleAsync(org, 10, actor, Ct)).Vista.Should().Be(new VentanaAnulacionCajaView(10, false, 60));
        (await guardar.HandleAsync(org, 10, actor, Ct)).Falla.Should().BeNull();
        (await guardar.HandleAsync(org, 0, actor, Ct)).Vista!.Minutos.Should().Be(0);

        (await leer.HandleAsync(org, Ct)).Should().Be(new VentanaAnulacionCajaView(0, false, 60));
        var historial = await new HistorialVentanaAnulacionCajaHandler(repo).HandleAsync(org, Ct);
        historial.Select(h => (h.MinutosAntes, h.MinutosDespues)).Should().Equal((10, 0), (15, 10));
    }

    [Fact]
    public async Task CajaAnulacion_Handler_OrganismoInexistente_NoGuarda()
    {
        var repo = new CajaAnulacionEnMemoria();
        var guardar = new GuardarVentanaAnulacionCajaHandler(repo, new TarifasFalsas { OrganismoExiste = false }, new AuditoriaFalsa(), new CajaAnulacionOpciones());

        (await guardar.HandleAsync(Guid.NewGuid(), 5, Guid.NewGuid(), Ct)).Falla.Should().Be(CobroPnErrores.OrganismoNoExiste);
        repo.Valores.Should().BeEmpty();
    }

    private sealed class CajaAnulacionEnMemoria : IPersonaNaturalCajaAnulacionRepository
    {
        public Dictionary<Guid, int> Valores { get; } = [];
        private readonly List<(Guid Org, CambioVentanaAnulacionView Cambio)> _historial = [];

        public Task<int?> LeerAsync(Guid organismoId, CancellationToken ct = default) =>
            Task.FromResult(Valores.TryGetValue(organismoId, out var v) ? (int?)v : null);

        public Task<int> GuardarAsync(Guid organismoId, int minutos, int minutosPorDefecto, Guid actor, CancellationToken ct = default)
        {
            var antes = Valores.TryGetValue(organismoId, out var v) ? v : minutosPorDefecto;
            Valores[organismoId] = minutos;
            if (antes != minutos)
                _historial.Add((organismoId, new CambioVentanaAnulacionView(antes, minutos, "Actor", DateTimeOffset.UtcNow)));
            return Task.FromResult(antes);
        }

        public Task<IReadOnlyList<CambioVentanaAnulacionView>> HistorialAsync(Guid organismoId, int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CambioVentanaAnulacionView>>([.. _historial.Where(h => h.Org == organismoId).Select(h => h.Cambio).Reverse().Take(max)]);
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

    /// <summary>
    /// Un solo host para la clase (antes cada cliente hacía <c>WithWebHostBuilder</c> y arrancaba un host que nunca se
    /// liberaba). El perfil OT falso se comparte; cada escenario registra sus tenants y un tenant sin registrar no tiene
    /// perfil, igual que antes.
    /// </summary>
    public sealed class Anfitrion : HostPorClase
    {
        internal PerfilesFalsos Perfiles { get; } = new();

        protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> factory) =>
            factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IOtProfileRepository>();
                s.AddSingleton<IOtProfileRepository>(Perfiles);
            }));
    }

    private static HttpClient ClientWith(Anfitrion host, string token)
    {
        var client = host.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Perfil OT falso compartido por la clase: solo los tenants registrados tienen organismo.</summary>
    internal sealed class PerfilesFalsos : IOtProfileRepository
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Guid> _organismos = new();

        public void Registrar(Guid tenant, Guid organismo) => _organismos[tenant] = organismo;

        public void Retirar(Guid tenant) => _organismos.TryRemove(tenant, out _);

        public Task<PerfilOt?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_organismos.TryGetValue(tenantId, out var organismo)
                ? new PerfilOt { Id = Guid.NewGuid(), TenantId = tenantId, TransitOfficeId = organismo }
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

        public Task<IReadOnlyDictionary<(Guid TipoId, string Cobro), long>> MontosAsync(Guid? ambitoOrganismoId, IReadOnlyCollection<Guid> tipos, CancellationToken ct = default)
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
        public bool OrganismoExiste { get; init; } = true;

        public Task<bool> OrganismoExisteAsync(Guid organismoId, CancellationToken ct = default) => Task.FromResult(OrganismoExiste);
        public Task<TarifaPnVersionadaResult> VersionarAsync(Guid? tipoId, Guid? organismoId, IReadOnlyList<TarifaPnValor> valores, DateOnly vigenteDesde, Guid actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TarifaPnView>> VigentesDelTipoEnOrganismoAsync(Guid tipoId, Guid organismoId, DateOnly fecha, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Dos organismos, un tipo habilitado y un actor reales; todo se retira al terminar.</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly Anfitrion _host;
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        public FlitDbContext Db { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid TenantOt { get; } = Guid.NewGuid();
        public Guid TenantOtroOt { get; } = Guid.NewGuid();
        public Guid Tipo { get; } = Guid.NewGuid();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OtroOrganismo { get; } = Guid.NewGuid();

        private Escenario(Anfitrion host)
        {
            _host = host;
            _scope = host.Factory.Services.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            host.Perfiles.Registrar(TenantOt, Organismo);
            host.Perfiles.Registrar(TenantOtroOt, OtroOrganismo);
        }

        public static async Task<Escenario> CrearAsync(Anfitrion host, CancellationToken ct)
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

        public HttpClient ClienteOt() => ClientWith(_host, Token("ot_admin", TenantOt, Actor));

        public HttpClient ClienteOtroOt() => ClientWith(_host, Token("ot_admin", TenantOtroOt, Actor));

        public HttpClient ClienteSuperAdmin() => ClientWith(_host, Token("SuperAdmin", Guid.NewGuid(), Actor));

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
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_caja_window_history WHERE transit_office_id = ANY({orgs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_caja_settings WHERE transit_office_id = ANY({orgs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_config_audit_logs WHERE changed_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.users WHERE id = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = {Tipo}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = ANY({orgs})");
                await tx.CommitAsync();
            }
            finally
            {
                _host.Perfiles.Retirar(TenantOt);
                _host.Perfiles.Retirar(TenantOtroOt);
                _scope.Dispose();
            }
        }
    }
}

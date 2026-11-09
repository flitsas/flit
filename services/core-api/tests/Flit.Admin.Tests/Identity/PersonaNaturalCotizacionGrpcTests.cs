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
        await e.PrecioBaseAsync(e.TipoElegible, null, ct); // HU #14391: sin un tipo con precio el organismo no se ofrece
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var respuesta = await cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct);

        respuesta.Organismos.Select(o => o.Id).Should().Contain(e.Organismo.ToString()).And.NotContain(e.OrganismoSinGrant.ToString());
        respuesta.Organismos.Single(o => o.Id == e.Organismo.ToString()).Municipio.Should().Be("Municipio de prueba");
    }

    [Fact]
    public async Task ListarOrganismos_NoOfreceUnOrganismoHabilitadoQueDejoDeEstarActivoPorFlit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.PrecioBaseAsync(e.TipoElegible, null, ct); // HU #14391: sin un tipo con precio el organismo no se ofrece
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        (await cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct)).Organismos
            .Select(o => o.Id).Should().Contain(e.Organismo.ToString());

        await e.DesactivarEmpresaOtAsync(ct);

        (await cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct)).Organismos
            .Select(o => o.Id).Should().NotContain(e.Organismo.ToString());
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

    [Fact]
    public async Task ListaBlanca_UnTipoElegibleSinHabilitarNoSeOfreceNiSeCotiza_AunConTarifa_YAlHabilitarloSi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoSinListaBlanca, null, 10_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoSinListaBlanca, null, 5_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var enOrganismo = await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest { OrganismoId = e.Organismo.ToString() }, cancellationToken: ct);
        var todos = await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest(), cancellationToken: ct);
        var error = await Error(() => cliente.CotizarAsync(Pedir(e.CodigoSinListaBlanca, e.Organismo), cancellationToken: ct).ResponseAsync);

        enOrganismo.Tipos.Select(t => t.Codigo).Should().NotContain(e.CodigoSinListaBlanca, "es elegible y tiene tarifa, pero el superadministrador no lo habilitó");
        todos.Tipos.Single(t => t.Codigo == e.CodigoSinListaBlanca).DisponiblePn.Should().BeFalse();
        todos.Tipos.Single(t => t.Codigo == e.CodigoSinListaBlanca).MotivoNoDisponible.Should().Contain("habilitado");
        PlatformRpcErrors.Reason(error).Should().Be("tipo_no_habilitado");

        await e.HabilitarAsync(e.TipoSinListaBlanca, ct);

        (await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest { OrganismoId = e.Organismo.ToString() }, cancellationToken: ct))
            .Tipos.Select(t => t.Codigo).Should().Contain(e.CodigoSinListaBlanca);
        (await cliente.CotizarAsync(Pedir(e.CodigoSinListaBlanca, e.Organismo), cancellationToken: ct)).TotalCop.Should().Be(15_000);
    }

    // ── HU #14391 — no ofrecer trámites ni organismos sin precio ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14391_AC1_UnTipoSinPrecioPropioNiGeneral_NoSeOfrece_AunqueOtroOrganismoLoTenga()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.PrecioBaseAsync(e.TipoSinTarifa, e.OrganismoSinGrant, ct); // precio propio de OTRO organismo: a este no le sirve
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var enOrganismo = await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest { OrganismoId = e.Organismo.ToString() }, cancellationToken: ct);

        enOrganismo.Tipos.Select(t => t.Codigo).Should().NotContain(e.CodigoSinTarifa);
    }

    [Fact]
    public async Task HU14391_AC4_UnTipoConValorGeneralYSinPrecioPropio_SeOfreceYSeCobraElGeneral()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.PrecioBaseAsync(e.TipoElegible, null, ct); // general: derechos 100.000 + servicio 50.000
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var tipos = await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest { OrganismoId = e.Organismo.ToString() }, cancellationToken: ct);
        var cotizacion = await cliente.CotizarAsync(Pedir(e.CodigoElegible, e.Organismo), cancellationToken: ct);

        tipos.Tipos.Select(t => t.Codigo).Should().Contain(e.CodigoElegible);
        cotizacion.TotalCop.Should().Be(150_000);
    }

    [Fact]
    public async Task HU14391_AC2_UnOrganismoSinNingunTipoConPrecio_NoSeOfrece_YConUnoSi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        // El único precio es de un tipo que el portal no ofrece (apagado) y otro solo con derechos: ningún tipo ofrecible.
        await e.PrecioBaseAsync(e.TipoApagado, null, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 100_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        (await cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct)).Organismos
            .Select(o => o.Id).Should().NotContain(e.Organismo.ToString());

        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 50_000, ct);

        (await cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct)).Organismos
            .Select(o => o.Id).Should().Contain(e.Organismo.ToString());
    }

    [Fact]
    public async Task HU14391_AC2_ElPrecioPropioDelOrganismoBasta_SinValorGeneral()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.PrecioBaseAsync(e.TipoElegible, e.Organismo, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        (await cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct)).Organismos
            .Select(o => o.Id).Should().Contain(e.Organismo.ToString());
        (await cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest { OrganismoId = e.Organismo.ToString() }, cancellationToken: ct))
            .Tipos.Select(t => t.Codigo).Should().Contain(e.CodigoElegible);
    }

    [Fact]
    public async Task HU14391_AC5_UnPrecioRecienRegistradoOCerrado_SeReflejaEnLaSiguienteConsulta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var organismos = () => cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), cancellationToken: ct).ResponseAsync;
        var tipos = () => cliente.ListarTiposDisponiblesAsync(new ListarTiposDisponiblesRequest { OrganismoId = e.Organismo.ToString() }, cancellationToken: ct).ResponseAsync;
        (await organismos()).Organismos.Select(o => o.Id).Should().NotContain(e.Organismo.ToString());
        (await tipos()).Tipos.Select(t => t.Codigo).Should().NotContain(e.CodigoElegible);

        await e.PrecioBaseAsync(e.TipoElegible, e.Organismo, ct); // recién registrado
        (await organismos()).Organismos.Select(o => o.Id).Should().Contain(e.Organismo.ToString());
        (await tipos()).Tipos.Select(t => t.Codigo).Should().Contain(e.CodigoElegible);

        await e.CerrarPreciosAsync(e.TipoElegible, ct); // recién cerrado
        (await organismos()).Organismos.Select(o => o.Id).Should().NotContain(e.Organismo.ToString());
        (await tipos()).Tipos.Select(t => t.Codigo).Should().NotContain(e.CodigoElegible);
    }

    [Fact]
    public async Task HU14391_UnPrecioReemplazadoElMismoDia_NoSeOfreceNiSeCobra_ElVigenteEsElNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.PrecioBaseAsync(e.TipoElegible, e.Organismo, ct);
        await e.ReemplazarDerechosAsync(e.TipoElegible, e.Organismo, 200_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var cotizacion = await cliente.CotizarAsync(Pedir(e.CodigoElegible, e.Organismo), cancellationToken: ct);

        cotizacion.TotalCop.Should().Be(250_000, "derechos 200.000 (el reemplazo) + servicio FLIT 50.000");
    }

    [Fact]
    public void HU14391_ConOferta_SoloDejaLosOrganismosConAlMenosUnTipoElegibleConPrecioBase()
    {
        var conGeneral = new Flit.Infrastructure.PersonaNatural.OrganismoHabilitadoPn(Guid.NewGuid(), "A", "A", string.Empty, string.Empty);
        var sinPrecio = new Flit.Infrastructure.PersonaNatural.OrganismoHabilitadoPn(Guid.NewGuid(), "B", "B", string.Empty, string.Empty);
        var tipoOk = Guid.NewGuid();
        var tipoNoElegible = Guid.NewGuid();
        var tipos = new[] { Tipo(tipoOk, true), Tipo(tipoNoElegible, false) };
        var tarifas = new[]
        {
            new Flit.Tramites.Domain.Tramites.PersonaNatural.TarifaVigente("DERECHOS_TRANSITO", tipoOk, null, 1, Guid.NewGuid()),
            new Flit.Tramites.Domain.Tramites.PersonaNatural.TarifaVigente("SERVICIO_FLIT", tipoOk, conGeneral.Id, 1, Guid.NewGuid()),
            new Flit.Tramites.Domain.Tramites.PersonaNatural.TarifaVigente("DERECHOS_TRANSITO", tipoNoElegible, sinPrecio.Id, 1, Guid.NewGuid()),
            new Flit.Tramites.Domain.Tramites.PersonaNatural.TarifaVigente("SERVICIO_FLIT", tipoNoElegible, sinPrecio.Id, 1, Guid.NewGuid()),
        };

        var r = Flit.Infrastructure.PersonaNatural.PersonaNaturalPortalReader.ConOferta([conGeneral, sinPrecio], tipos, tarifas);

        // A: derechos general + servicio propio = ofrece el tipo elegible. B: solo precio de un tipo no elegible = nada que ofrecer.
        r.Should().ContainSingle().Which.Id.Should().Be(conGeneral.Id);
    }

    private static Flit.Infrastructure.PersonaNatural.TipoPersonaNatural Tipo(Guid id, bool elegible) =>
        new(id, "T" + id.ToString("N")[..4], "Tipo", "OTROS", 1,
            new Flit.Tramites.Domain.Tramites.Services.ProcedureTypeGateProfile(),
            new Flit.Tramites.Domain.Tramites.PersonaNatural.ResultadoElegibilidad(elegible, elegible ? [] : ["motivo"]));

    // ── HU #14395: medios de pago y total por medio ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14395_AC4_SinMedio_LaRespuestaEsLaDeSiempre_AunConBanderasYSwitchesApagados()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 185_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 181_000, ct);
        await e.TarifaAsync("CAMBIO_COLOR", null, null, 1_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var pedido = Pedir(e.CodigoElegible, e.Organismo, "CAMBIO_COLOR");
        var antes = await cliente.CotizarAsync(pedido, cancellationToken: ct);

        await e.MedioAsync(e.Organismo, "PASARELA", false, ct);
        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PASARELA", "TRAMITE", false, ct);
        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PASARELA", "GESTION", false, ct);
        await e.SwitchAsync(null, e.TipoElegible, "PASARELA", "TRAMITE", false, ct);
        var despues = await cliente.CotizarAsync(pedido, cancellationToken: ct);

        despues.Should().Be(antes, "sin medio nada de lo configurado cambia la cotización");
        despues.Medio.Should().BeEmpty();
        despues.TotalCop.Should().Be(185_000 + 181_000 + 1_000);
    }

    [Fact]
    public async Task HU14395_AC2_ConMedio_SumaSoloLosConceptosEncendidos_YSinConfiguracionCobraComoHoy()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 185_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 181_000, ct);
        await e.TarifaAsync("CAMBIO_COLOR", null, null, 1_000, ct);
        await e.TarifaAsync("ENVIO_DOCUMENTO", null, null, 20, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var sinMedio = await cliente.CotizarAsync(Pedir(e.CodigoElegible, e.Organismo, "CAMBIO_COLOR", "ENVIO_DOCUMENTO"), cancellationToken: ct);

        var sinConfigurar = await cliente.CotizarAsync(Medio(e, "PASARELA", "CAMBIO_COLOR", "ENVIO_DOCUMENTO"), cancellationToken: ct);
        sinConfigurar.TotalCop.Should().Be(sinMedio.TotalCop, "sin configuración el medio cobra todo, como hoy");
        sinConfigurar.Lineas.Select(l => l.Codigo).Should().Equal(sinMedio.Lineas.Select(l => l.Codigo));
        sinConfigurar.VersionTarifa.Should().Be(sinMedio.VersionTarifa);
        sinConfigurar.Medio.Should().Be("PASARELA");

        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PASARELA", "GESTION", false, ct);
        var sinGestion = await cliente.CotizarAsync(Medio(e, "PASARELA", "CAMBIO_COLOR", "ENVIO_DOCUMENTO"), cancellationToken: ct);
        sinGestion.Lineas.Select(l => l.Codigo).Should().Equal("DERECHOS_TRANSITO", "CAMBIO_COLOR");
        sinGestion.TotalCop.Should().Be(185_000 + 1_000);
        sinGestion.AdicionalesNoAplicados.Should().Equal("ENVIO_DOCUMENTO");
        sinGestion.VersionTarifa.Should().NotBe(sinMedio.VersionTarifa, "la versión solo cuenta las tarifas que sumaron");
    }

    [Fact]
    public async Task HU14395_AC1_ListarMediosPago_SoloLosConBanderaYAlMenosUnConceptoEncendido()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.TarifaAsync("DERECHOS_TRANSITO", e.TipoElegible, null, 185_000, ct);
        await e.TarifaAsync("SERVICIO_FLIT", e.TipoElegible, null, 181_000, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var punto = e.Organismo.ToString();

        var web = await cliente.ListarMediosPagoAsync(Listar(e, null), cancellationToken: ct);
        var inicial = await cliente.ListarMediosPagoAsync(Listar(e, punto), cancellationToken: ct);
        web.Medios.Select(m => m.Medio).Should().Equal("PASARELA");
        inicial.Medios.Select(m => m.Medio).Should().Equal("PASARELA");
        inicial.Medios[0].TotalCop.Should().Be(366_000);
        inicial.Medios[0].Lineas.Select(l => l.Codigo).Should().Equal("DERECHOS_TRANSITO", "SERVICIO_FLIT");

        await e.MedioAsync(e.Organismo, "PIN", true, ct);
        (await cliente.ListarMediosPagoAsync(Listar(e, punto), cancellationToken: ct)).Medios.Select(m => m.Medio).Should().Equal("PASARELA", "PIN");

        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PIN", "TRAMITE", false, ct);
        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PIN", "GESTION", false, ct);
        (await cliente.ListarMediosPagoAsync(Listar(e, punto), cancellationToken: ct)).Medios.Select(m => m.Medio)
            .Should().Equal(["PASARELA"], "con los dos switches apagados el PIN no se ofrece");

        await e.MedioAsync(e.Organismo, "PASARELA", false, ct);
        (await cliente.ListarMediosPagoAsync(Listar(e, punto), cancellationToken: ct)).Medios.Should().BeEmpty("sin bandera ni conceptos no hay medios");
        (await cliente.ListarMediosPagoAsync(Listar(e, null), cancellationToken: ct)).Medios.Select(m => m.Medio)
            .Should().Equal(["PASARELA"], "apagar un medio del punto no toca la web");
    }

    [Fact]
    public async Task HU14395_AC5_UnaBanderaOUnSwitchRecienGuardados_SeReflejanEnLaSiguienteConsulta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.PrecioBaseAsync(e.TipoElegible, null, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var punto = e.Organismo.ToString();

        (await cliente.ListarMediosPagoAsync(Listar(e, punto), cancellationToken: ct)).Medios.Single().TotalCop.Should().Be(150_000);
        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PASARELA", "GESTION", false, ct);
        (await cliente.ListarMediosPagoAsync(Listar(e, punto), cancellationToken: ct)).Medios.Single().TotalCop.Should().Be(100_000);
        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PASARELA", "GESTION", true, ct);
        (await cliente.ListarMediosPagoAsync(Listar(e, punto), cancellationToken: ct)).Medios.Single().TotalCop.Should().Be(150_000);
    }

    [Fact]
    public async Task HU14395_UnMedioApagadoOSinConceptos_NoSeCotiza_ConElErrorEstable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.PrecioBaseAsync(e.TipoElegible, null, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        PlatformRpcErrors.Reason(await Error(() => cliente.CotizarAsync(Medio(e, "PIN"), cancellationToken: ct).ResponseAsync)).Should().Be("medio_no_disponible");
        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PASARELA", "TRAMITE", false, ct);
        await e.SwitchAsync(e.Organismo, e.TipoElegible, "PASARELA", "GESTION", false, ct);
        PlatformRpcErrors.Reason(await Error(() => cliente.CotizarAsync(Medio(e, "PASARELA"), cancellationToken: ct).ResponseAsync)).Should().Be("medio_no_disponible");
        (await Error(() => cliente.CotizarAsync(Medio(e, "TRANSFERENCIA"), cancellationToken: ct).ResponseAsync)).StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static CotizarRequest Pedir(string codigo, Guid organismo, params string[] adicionales)
    {
        var r = new CotizarRequest { TipoTramiteCodigo = codigo, OrganismoId = organismo.ToString() };
        r.Adicionales.AddRange(adicionales);
        return r;
    }

    private static CotizarRequest Medio(Escenario e, string medio, params string[] adicionales)
    {
        var r = Pedir(e.CodigoElegible, e.Organismo, adicionales);
        r.Medio = medio;
        r.AmbitoOrganismoId = e.Organismo.ToString();
        return r;
    }

    private static ListarMediosPagoRequest Listar(Escenario e, string? ambito) =>
        new() { TipoTramiteCodigo = e.CodigoElegible, OrganismoId = e.Organismo.ToString(), AmbitoOrganismoId = ambito ?? string.Empty };

    private static async Task<RpcException> Error(Func<Task> llamada) => (await llamada.Should().ThrowAsync<RpcException>()).Which;

    private WebApplicationFactory<Program> Host() =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("PersonaNatural:Fachada:Habilitada", "true");
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Scopes:0", Scope);
        }).ConEmpresaPersonaNatural();

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
        public Guid TenantOt { get; } = Guid.NewGuid();
        public Guid TipoElegible { get; } = Guid.NewGuid();
        public Guid TipoSinTransformaciones { get; } = Guid.NewGuid();
        public Guid TipoApagado { get; } = Guid.NewGuid();
        public Guid TipoSinTarifa { get; } = Guid.NewGuid();
        public Guid TipoSinListaBlanca { get; } = Guid.NewGuid();
        public string CodigoElegible => "PN_T_" + _sufijo + "_A";
        public string CodigoSinTransformaciones => "PN_T_" + _sufijo + "_B";
        public string CodigoApagado => "PN_T_" + _sufijo + "_C";
        public string CodigoSinTarifa => "PN_T_" + _sufijo + "_D";
        public string CodigoSinListaBlanca => "PN_T_" + _sufijo + "_E";

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
            // HU #14072: el organismo ofrecido debe estar activo por FLIT (perfil OT + empresa OT activa).
            var taxId = "8" + e._sufijo;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
                VALUES ({e.TenantOt}, {"OT de prueba " + e._sufijo}, {taxId}, 'FLIT', true, now())
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES ({e.TenantOt}, {e.Organismo})", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.tenant_transit_office_grants (tenant_id, transit_office_id, is_enabled, source)
                VALUES ({PlatformTenants.PersonaNatural}, {e.Organismo}, true, 'CLIENT')
                """, ct);
            // Familia TRASPASO acumula transformaciones; OTROS no (ProcedureTypeGateProfile.ComplementaryTransformationsAllowed).
            await e.TipoAsync(e.TipoElegible, e.CodigoElegible, "TRASPASO", wizard: true, ct);
            await e.TipoAsync(e.TipoSinTransformaciones, e.CodigoSinTransformaciones, "OTROS", wizard: true, ct);
            await e.TipoAsync(e.TipoApagado, e.CodigoApagado, "OTROS", wizard: false, ct);
            await e.TipoAsync(e.TipoSinTarifa, e.CodigoSinTarifa, "OTROS", wizard: true, ct);
            await e.TipoAsync(e.TipoSinListaBlanca, e.CodigoSinListaBlanca, "OTROS", wizard: true, ct);
            // HU #14072: «habilitado = elegible + habilitado por el superadministrador + tarifa vigente»; el E queda fuera a propósito.
            foreach (var habilitado in new[] { e.TipoElegible, e.TipoSinTransformaciones, e.TipoApagado, e.TipoSinTarifa })
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO admin.persona_natural_enabled_types (procedure_type_id) VALUES ({habilitado})", ct);
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

        /// <summary>Los dos renglones base (derechos 100.000 y servicio FLIT 50.000) de un tipo; general o de un organismo.</summary>
        public async Task PrecioBaseAsync(Guid tipo, Guid? organismo, CancellationToken ct)
        {
            await TarifaAsync("DERECHOS_TRANSITO", tipo, organismo, 100_000, ct);
            await TarifaAsync("SERVICIO_FLIT", tipo, organismo, 50_000, ct);
        }

        /// <summary>Cierra (valid_to en el pasado) todos los precios de un tipo: lo único que el trigger permite sobre una versión abierta.</summary>
        public Task<int> CerrarPreciosAsync(Guid tipo, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE admin.persona_natural_tariffs SET valid_to = valid_from WHERE procedure_type_id = {tipo} AND valid_to IS NULL", ct);

        /// <summary>Un valor nuevo de derechos con el mismo inicio: reemplaza al anterior por la función de versionado (HU #14389).</summary>
        public Task<int> ReemplazarDerechosAsync(Guid tipo, Guid? organismo, long valor, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT admin.persona_natural_tariff_new_version('DERECHOS_TRANSITO', {tipo}::uuid, {organismo}::uuid, {valor}, {Hoy}::date, NULL::uuid)
                """, ct);

        public Task<int> MedioAsync(Guid? ambito, string medio, bool enabled, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO admin.persona_natural_payment_methods (transit_office_id, method, enabled) VALUES ({ambito}::uuid, {medio}, {enabled})", ct);

        public Task<int> SwitchAsync(Guid? ambito, Guid tipo, string medio, string cobro, bool enabled, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.persona_natural_charge_switches (transit_office_id, procedure_type_id, method, charge, enabled)
                VALUES ({ambito}::uuid, {tipo}, {medio}, {cobro}, {enabled})
                ON CONFLICT (COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid), procedure_type_id, method, charge)
                DO UPDATE SET enabled = EXCLUDED.enabled
                """, ct);

        public Task<int> DesactivarEmpresaOtAsync(CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.tenants SET is_active = false WHERE id = {TenantOt}", ct);

        public Task<int> HabilitarAsync(Guid tipo, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO admin.persona_natural_enabled_types (procedure_type_id) VALUES ({tipo})", ct);

        public async ValueTask DisposeAsync()
        {
            try
            {
                // Las versiones de tarifa son historial inmutable por trigger: solo un superusuario (la base de pruebas) puede retirarlas.
                await using var tx = await _db.Database.BeginTransactionAsync();
                await _db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var ids = new[] { TipoElegible, TipoSinTransformaciones, TipoApagado, TipoSinTarifa, TipoSinListaBlanca };
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_enabled_types WHERE procedure_type_id = ANY({ids})");
                var orgs = new[] { Organismo, OrganismoSinGrant };
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_charge_switches WHERE procedure_type_id = ANY({ids}) OR transit_office_id = ANY({orgs})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_payment_methods WHERE transit_office_id = ANY({orgs})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE procedure_type_id = ANY({ids}) OR transit_office_id = ANY({orgs})");
                // Las tarifas sin tipo (adicionales generales) de la prueba: las del escenario son de valor único por concepto y ámbito.
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE procedure_type_id IS NULL AND transit_office_id IS NULL AND valid_from = {Hoy}::date");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_transit_office_grants WHERE transit_office_id = ANY({orgs})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = ANY({ids})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.transit_office_profiles WHERE tenant_id = {TenantOt}");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.tenants WHERE id = {TenantOt}");
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

using System.Net;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14216, #14219, #14221, #14231, #14236 y #14242 — canal del punto. El host mapea esas rutas (sin Postgres): la cookie, el
/// arranque, el diagnóstico, las consultas, abrir actores y la matriz de requisitos usan fachadas falsas. Abrir actores
/// materializa borrador vía <see cref="IAbrirBorradorDesdePunto"/> falso (sin DbContext).
/// </summary>
public sealed class PuntoCanalTests
{
    private static readonly Guid OrganismoId = Guid.Parse("0199b7e0-0000-7000-8000-0000000000aa");
    private const string Codigo = "AB1";
    private const string Nombre = "Tránsito de Prueba";
    private const string Base = SesionEndpoints.Base + "/punto";

    [Fact]
    public async Task Encendido_ResuelveYFijaLaSesion_YNoSePuedeCambiarElOrganismo()
    {
        await using var host = Host(Ok());
        var entrada = await host.PostAsync("/entrada", new { codigo = " ab1 " });

        entrada.Status.Should().Be(HttpStatusCode.OK);
        entrada.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);
        entrada.Json.GetProperty("codigo").GetString().Should().Be(Codigo);
        entrada.Json.GetProperty("nombre").GetString().Should().Be(Nombre);
        entrada.SetCookie.Should().NotBeNullOrEmpty();
        entrada.SetCookie!.ToLowerInvariant().Should().Contain("httponly").And.Contain("samesite=lax");

        var sesion = await host.GetAsync("/sesion", entrada.Cookie);
        sesion.Status.Should().Be(HttpStatusCode.OK);
        sesion.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);

        var alterada = await host.GetAsync("/sesion", entrada.Cookie + "x");
        alterada.Status.Should().Be(HttpStatusCode.Unauthorized);

        var otro = await host.GetAsync("/sesion?organismoId=0199b7e0-0000-7000-8000-0000000000bb", entrada.Cookie);
        otro.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);
    }

    [Fact]
    public async Task Apagado_NoAbre_YNoDejaCookie()
    {
        await using var host = Host(new ResolucionCanalPunto(PuntoEndpoints.CanalApagado, OrganismoId, Codigo, Nombre));

        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        entrada.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        entrada.Json.GetProperty("code").GetString().Should().Be("canal_apagado");
        CookieUtil(entrada.SetCookie).Should().BeNullOrEmpty();
        (await host.GetAsync("/sesion", entrada.Cookie)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CodigoInvalido_NoAbre()
    {
        await using var host = Host(new ResolucionCanalPunto(PuntoEndpoints.CodigoDesconocido, Guid.Empty, string.Empty, string.Empty));

        var entrada = await host.PostAsync("/entrada", new { codigo = "ZZZ" });

        entrada.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        entrada.Json.GetProperty("code").GetString().Should().Be("codigo_desconocido");
        CookieUtil(entrada.SetCookie).Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Arranque_Sigue_CuandoLaPlacaEsDelOrganismoDeLaSesion()
    {
        var runt = RuntCon(Nombre);
        var resultado = await Arranque(runt, new OrganismoPunto(OrganismoId, Codigo, Nombre), "ABC123", "1098765432", "CAMBIO_COLOR");

        resultado.Should().BeOfType<ResultadoArranque.Seguir>();
        runt.LlamadasVehiculo.Should().ContainSingle().Which.Placa.Should().Be("ABC123");
    }

    [Fact]
    public async Task Arranque_PlacaConEspacios_SeNormalizaYSigue()
    {
        var runt = RuntCon("tránsito de prueba");
        var resultado = await Arranque(runt, new OrganismoPunto(OrganismoId, Codigo, Nombre), " abc 123 ", "1098765432", "TRASPASO_STANDARD");

        resultado.Should().BeOfType<ResultadoArranque.Seguir>();
        runt.LlamadasVehiculo.Single().Placa.Should().Be("ABC123");
    }

    [Fact]
    public async Task Arranque_PlacaInexistente_NoSigue()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.VehiculoInexistente };
        var resultado = await Arranque(runt, Sesion(), "ZZZ999", "1098765432", "CAMBIO_COLOR");

        resultado.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be(TiposDelPunto.PlacaInexistente);
    }

    [Fact]
    public async Task Arranque_OtroOrganismo_NoSigue()
    {
        var runt = RuntCon("OT DE OTRO MUNICIPIO");
        var resultado = await Arranque(runt, Sesion(), "ABC123", "1098765432", "TRASPASO_STANDARD");

        resultado.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be(
            "Esta placa está registrada en OT DE OTRO MUNICIPIO, no en Tránsito de Prueba. Este trámite debe hacerse en OT DE OTRO MUNICIPIO.");
    }

    [Fact]
    public void MensajeOtroOrganismo_ConservaElNombreDelRunt_RecortaEspacios_YCaeAlGenericoSinNombre()
    {
        var sesion = Sesion();
        TiposDelPunto.MensajeOtroOrganismo("  STRIA TTOyTTE   MCPAL SABANETA ", sesion).Should().Be(
            "Esta placa está registrada en STRIA TTOyTTE MCPAL SABANETA, no en Tránsito de Prueba. Este trámite debe hacerse en STRIA TTOyTTE MCPAL SABANETA.");
        TiposDelPunto.MensajeOtroOrganismo(null, sesion).Should().Be(TiposDelPunto.OtroOrganismo);
        TiposDelPunto.MensajeOtroOrganismo("   ", sesion).Should().Be(TiposDelPunto.OtroOrganismo);
    }

    [Fact]
    public async Task Arranque_PlacaSinOrganismoEnElRunt_NoSigue_ConMensajeClaro()
    {
        var runt = RuntCon("");
        var resultado = await Arranque(runt, Sesion(), "ABC123", "1098765432", "TRASPASO_STANDARD");

        resultado.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be(TiposDelPunto.OrganismoDesconocido);
    }

    [Fact]
    public async Task Arranque_ComparaConLaReglaUnica_NombreSinTildesYMunicipioDelCatalogo_SinAceptarDeMas()
    {
        var sesion = new OrganismoPunto(OrganismoId, "SAB", "Secretaría de Tránsito de Sabaneta");
        var fachada = FachadaCon(new TipoTramiteDto("CAMBIO_COLOR", "Color", "OTROS", 1, true, string.Empty));
        fachada.Organismos.Add(new OrganismoDto(OrganismoId.ToString(), "SAB", "Secretaría de Tránsito de Sabaneta", "Sabaneta", "Antioquia", string.Empty));

        async Task<ResultadoArranque> Evaluar(string organismoRunt) =>
            await new ArranquePuntoService(RuntCon(organismoRunt), fachada).EvaluarAsync(sesion, "ABC123", "1098765432", "CAMBIO_COLOR", TestContext.Current.CancellationToken);

        (await Evaluar("SECRETARIA DE TRANSITO DE SABANETA")).Should().BeOfType<ResultadoArranque.Seguir>("el nombre coincide sin tildes ni mayúsculas");
        fachada.LlamadasOrganismos.Should().Be(0, "por nombre no hace falta pedir el catálogo");
        (await Evaluar("STRIA TTOyTTE MCPAL SABANETA")).Should().BeOfType<ResultadoArranque.Seguir>("coincide por el municipio del catálogo");
        (await Evaluar("STRIA TTOyTTE MCPAL ENVIGADO")).Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Contain("ENVIGADO");
        (await Evaluar("SECRETARIA DE TRANSITO DE SABANETAS")).Should().BeOfType<ResultadoArranque.Detener>("no acepta una palabra que solo contiene al municipio");
    }

    [Fact]
    public async Task Arranque_RuntCaido_NoSigue()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.ProveedorCaido };
        var resultado = await Arranque(runt, Sesion(), "ABC123", "1098765432", "DUPLICADO_PLACA");

        resultado.Should().BeOfType<ResultadoArranque.ProveedorCaido>();
    }

    [Fact]
    public async Task Arranque_TipoQueNoAdmite_DevuelveElMotivoDeFlit2_SinConsultarElRunt()
    {
        var runt = RuntCon(Nombre);
        var fachada = FachadaCon(new TipoTramiteDto("MATRICULA_INICIAL", "Matrícula", "MATRICULAS", 1, false, "El tipo de trámite no está habilitado en FLIT 2."));

        var resultado = await new ArranquePuntoService(runt, fachada).EvaluarAsync(Sesion(), "ABC123", "1098765432", "MATRICULA_INICIAL", TestContext.Current.CancellationToken);

        resultado.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be("El tipo de trámite no está habilitado en FLIT 2.");
        runt.LlamadasVehiculo.Should().BeEmpty();
    }

    [Fact]
    public async Task HU13_Arranque_UsaElCatalogoDelOrganismoDeLaSesion_YUnTipoFueraDelCatalogoNoSigue()
    {
        var runt = RuntCon(Nombre);
        var fachada = FachadaCon(new TipoTramiteDto("BLINDAJE", "Blindaje", "OTROS", 1, true, string.Empty));

        // Un tipo que antes no estaba en la lista fija ahora entra por el catálogo (y se pide con el organismo de la sesión).
        var seguir = await new ArranquePuntoService(runt, fachada).EvaluarAsync(Sesion(), "ABC123", "1098765432", "BLINDAJE", TestContext.Current.CancellationToken);
        seguir.Should().BeOfType<ResultadoArranque.Seguir>();
        fachada.UltimoOrganismo.Should().Be(OrganismoId.ToString());

        // Uno que el catálogo no ofrece en ese organismo (sin tarifa, no elegible…) se detiene sin consultar el RUNT.
        var otroRunt = RuntCon(Nombre);
        var fuera = await new ArranquePuntoService(otroRunt, fachada).EvaluarAsync(Sesion(), "ABC123", "1098765432", "CAMBIO_COLOR", TestContext.Current.CancellationToken);
        fuera.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Be(TiposDelPunto.TipoNoDisponible);
        otroRunt.LlamadasVehiculo.Should().BeEmpty();
    }

    [Fact]
    public async Task HU13_Arranque_ElOrganismoDelRuntNoSeCompara_CuandoLoEligeElOperador()
    {
        var runt = RuntCon("OT DE OTRO MUNICIPIO");
        var fachada = FachadaCon(new TipoTramiteDto("MATRICULA_NUEVA", "Matrícula", "MATRICULAS", 1, true, string.Empty), new TipoTramiteDto("CAMBIO_COLOR", "Color", "OTROS", 1, true, string.Empty));
        var definiciones = new FachadaDefinicionesFalsa();
        definiciones.PorCodigo["MATRICULA_NUEVA"] = DefinicionesDePrueba.UnTitular("MATRICULA_NUEVA") with
        {
            Flags = new FlagsTipoDto("VIN", false, false, false, false, true, true, false, OrganismoLoEligeOperador: true),
        };
        definiciones.PorCodigo["CAMBIO_COLOR"] = DefinicionesDePrueba.UnTitular();
        var servicio = new ArranquePuntoService(
            runt, fachada, new DefinicionesService(definiciones, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                Microsoft.Extensions.Options.Options.Create(new CotizacionesOptions())));

        var elige = await servicio.EvaluarAsync(Sesion(), "ABC123", "1098765432", "MATRICULA_NUEVA", TestContext.Current.CancellationToken);
        var impone = await servicio.EvaluarAsync(Sesion(), "ABC123", "1098765432", "CAMBIO_COLOR", TestContext.Current.CancellationToken);

        elige.Should().BeOfType<ResultadoArranque.Seguir>("el organismo de la sesión ES el elegido");
        impone.Should().BeOfType<ResultadoArranque.Detener>().Which.Mensaje.Should().Contain("OT DE OTRO MUNICIPIO").And.Contain(Nombre);
    }

    [Fact]
    public async Task HU13_TiposDelPunto_ResponderLaMismaFormaDelPortalWeb_ConElOrganismoDeLaCookie_YSinSesionSon401()
    {
        var fachada = FachadaCon(new TipoTramiteDto("CAMBIO_COLOR", "Color", "OTROS", 1, true, string.Empty), new TipoTramiteDto("BLINDAJE", "Blindaje", "OTROS", 1, true, string.Empty));
        await using var host = Host(Ok(), fachada: fachada);

        (await host.GetAsync("/tipos-tramite", null)).Status.Should().Be(HttpStatusCode.Unauthorized);

        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
        var r = await host.GetAsync("/tipos-tramite", entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.EnumerateArray().Select(t => t.GetProperty("code").GetString()).Should().Equal("CAMBIO_COLOR", "BLINDAJE");
        r.Json[0].EnumerateObject().Select(p => p.Name).Should().Equal("code", "name", "family", "version", "disponible", "motivoNoDisponible");
        fachada.UltimoOrganismo.Should().Be(OrganismoId.ToString());
    }

    [Fact]
    public async Task HU13_Completitud_ExigeLasDeclaracionesYLosDocumentosDeLoElegido()
    {
        var defs = DefsTraspaso();
        defs.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso() with
        {
            Documentos = [new DocumentoRequeridoDto("certificado_blindaje", "Certificado de blindaje", true, 1, string.Empty, string.Empty)],
            Flags = new FlagsTipoDto("PLATE", false, false, false, false, true, false),
            Declaraciones =
            [
                new DeclaracionTipoDto(
                    "blindaje", "seleccion", "Blindaje", true,
                    [new OpcionDeclaracionDto("NIVEL_1", "Blindaje nivel 1", ["certificado_blindaje"], true)],
                    "blindaje_nivel", ["blindaje"], "certificado_blindaje", true, string.Empty, string.Empty),
            ],
        };

        async Task<JsonElement> Evaluar(BorradorGuardadoFalso guardado)
        {
            await using var host = Host(Ok(), definiciones: defs, guardado: guardado);
            var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
            return (await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, guardado.Token)).Json;
        }

        // Sin declarar nada: falta la declaración (con el nombre legible) y el certificado NO se repite desde la matriz.
        var sin = await Evaluar(BorradorGuardado());
        sin.GetProperty("puedeContinuarPago").GetBoolean().Should().BeFalse();
        sin.GetProperty("pendientes").EnumerateArray().Select(p => (p.GetProperty("codigo").GetString(), p.GetProperty("nombre").GetString()))
            .Should().Equal(("blindaje", "Blindaje"));

        // Declarada, sin certificado: pide el documento por nombre.
        var sinCert = await Evaluar(BorradorGuardado(campos: new() { ["blindaje_nivel"] = "NIVEL_1" }));
        sinCert.GetProperty("pendientes").EnumerateArray().Select(p => (p.GetProperty("codigo").GetString(), p.GetProperty("nombre").GetString()))
            .Should().Equal(("certificado_blindaje", "Certificado de blindaje"));

        // Declarada y con certificado: el gate abre.
        var completo = await Evaluar(BorradorGuardado(docs: ["certificado_blindaje"], campos: new() { ["blindaje_nivel"] = "NIVEL_1" }));
        completo.GetProperty("puedeContinuarPago").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task HU13_Completitud_ConDecisionDePrenda_PideElDocumentoDeLaDecision()
    {
        var defs = DefsTraspaso();
        defs.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso() with
        {
            Documentos = [new DocumentoRequeridoDto("prenda_levantamiento", "Levantamiento de prenda", false, 1, string.Empty, string.Empty)],
            Flags = new FlagsTipoDto("PLATE", false, true, false, false, true, false),
        };
        var sinDoc = BorradorGuardado(prendaDatos: new PrendaDatos(true, "Banco", "900123456", "levantar"));
        await using var host = Host(Ok(), definiciones: defs, guardado: sinDoc);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, sinDoc.Token);

        r.Json.GetProperty("pendientes").EnumerateArray().Select(p => p.GetProperty("codigo").GetString()).Should().Equal("prenda_levantamiento");
    }

    [Fact]
    public async Task Arranque_SinCookie_Responde401_YElRuntCaidoSale503()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.ProveedorCaido };
        await using var host = Host(Ok(), runt, FachadaCon(new TipoTramiteDto("CAMBIO_COLOR", "Color", "OTROS", 1, true, string.Empty)));

        (await host.PostAsync("/arranque", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" })).Status
            .Should().Be(HttpStatusCode.Unauthorized);

        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
        var caido = await host.PostAsync("/arranque", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);
        caido.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        caido.Json.GetProperty("puedeSeguir").GetBoolean().Should().BeFalse();
        caido.Json.GetProperty("code").GetString().Should().Be("proveedor_no_disponible");
        caido.Json.GetProperty("message").GetString().Should().Be(TiposDelPunto.RuntCaido);
    }

    [Fact]
    public async Task ActoresAbrir_SinSesion_Responde401()
    {
        await using var host = Host(Ok(), RuntCon(Nombre));

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" });

        r.Status.Should().Be(HttpStatusCode.Unauthorized);
        r.Json.GetProperty("code").GetString().Should().Be("sesion_punto_ausente");
    }

    [Fact]
    public async Task Requisitos_SinSesion_Responde401()
    {
        await using var host = Host(Ok());

        var r = await host.GetAsync("/requisitos?tipoCodigo=TRASPASO_STANDARD", null);

        r.Status.Should().Be(HttpStatusCode.Unauthorized);
        r.Json.GetProperty("code").GetString().Should().Be("sesion_punto_ausente");
    }

    [Fact]
    public async Task Requisitos_MatrizOk_UnSoloGrupoSinDuplicarPorActor_YUsaOrganismoDeLaCookie()
    {
        var defs = new FachadaDefinicionesFalsa();
        defs.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso(huella: "matriz-viva-1");
        await using var host = Host(Ok(), definiciones: defs);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.GetAsync("/requisitos?tipoCodigo=TRASPASO_STANDARD", entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("tipoCodigo").GetString().Should().Be("TRASPASO_STANDARD");
        r.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);
        r.Json.GetProperty("huella").GetString().Should().Be("matriz-viva-1");
        var grupos = r.Json.GetProperty("grupos");
        // Traspaso tiene dos actores (vendedor, comprador): aun así, un único grupo con cada documento una sola vez.
        grupos.GetArrayLength().Should().Be(1);
        grupos[0].TryGetProperty("parte", out _).Should().BeFalse();
        grupos[0].GetProperty("etiqueta").GetString().Should().Be("Documentos");
        var items = grupos[0].GetProperty("items").EnumerateArray().ToArray();
        items.Select(i => i.GetProperty("docCode").GetString()).Should().Equal("compraventa", "soat", "rtm", "poder_tramitador");
        var clases = items.Select(i => i.GetProperty("clase").GetString()).ToArray();
        clases.Should().Contain("obligatorio");
        clases.Should().Contain("opcional");
        items[3].GetProperty("condicion").GetString().Should().NotBeNullOrWhiteSpace();
        defs.UltimoOrganismo.Should().Be(OrganismoId.ToString());
        defs.Llamadas.Should().Be(1);
    }

    [Fact]
    public async Task Requisitos_MatrizViva_ReflejaCambioSinConfigLocal()
    {
        var defs = new FachadaDefinicionesFalsa();
        defs.PorCodigo["CAMBIO_COLOR"] = DefinicionesDePrueba.UnTitular(huella: "antes");
        await using var host = Host(Ok(), definiciones: defs);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var primero = await host.GetAsync("/requisitos?tipoCodigo=CAMBIO_COLOR", entrada.Cookie);
        primero.Json.GetProperty("huella").GetString().Should().Be("antes");

        defs.PorCodigo["CAMBIO_COLOR"] = DefinicionesDePrueba.UnTitular(huella: "despues");
        var segundo = await host.GetAsync("/requisitos?tipoCodigo=CAMBIO_COLOR", entrada.Cookie);

        segundo.Status.Should().Be(HttpStatusCode.OK);
        segundo.Json.GetProperty("huella").GetString().Should().Be("despues");
        segundo.Json.GetProperty("grupos").GetArrayLength().Should().Be(1);
        defs.Llamadas.Should().Be(2);
    }

    [Fact]
    public async Task Requisitos_SinTipo_Responde400()
    {
        await using var host = Host(Ok());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.GetAsync("/requisitos", entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.BadRequest);
        r.Json.GetProperty("code").GetString().Should().Be("datos_invalidos");
    }

    [Fact]
    public async Task Requisitos_ValidarArchivo_FormatoInvalido_400()
    {
        await using var host = Host(Ok());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/validar-archivo", new { nombreArchivo = "x.exe", tamanoBytes = 100 }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.BadRequest);
        r.Json.GetProperty("valido").GetBoolean().Should().BeFalse();
        r.Json.GetProperty("code").GetString().Should().Be("formato_no_permitido");
    }

    [Fact]
    public async Task Requisitos_ValidarArchivo_Ok()
    {
        await using var host = Host(Ok());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/validar-archivo", new { nombreArchivo = "contrato.pdf", tamanoBytes = 1024 }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("valido").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Requisitos_ValorSugerido_SinFasecolda_QuedaVacio()
    {
        await using var host = Host(Ok());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.GetAsync("/requisitos/valor-sugerido?placa=ABC123", entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("fuente").GetString().Should().Be("sin_datos");
        // Json del punto omite nulls (WhenWritingNull): sin avalúo no viene valorSugerido.
        r.Json.TryGetProperty("valorSugerido", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Requisitos_ValorSugerido_ConAvaluo_PrecargaValor()
    {
        var runt = new ConsultasRuntFalso
        {
            Avaluos = () => new AvaluoSugeridoConsulta(18_500_000m, "fasecolda"),
        };
        await using var host = Host(Ok(), runt);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.GetAsync("/requisitos/valor-sugerido?placa=ABC123", entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("fuente").GetString().Should().Be("fasecolda");
        r.Json.GetProperty("valorSugerido").GetDecimal().Should().Be(18_500_000m);
        runt.LlamadasAvaluos.Should().ContainSingle(x => x.Placa == "ABC123");
    }

    [Fact]
    public async Task Requisitos_Completitud_SinBearerDeBorrador_Responde401()
    {
        var guardado = BorradorGuardado();
        await using var host = Host(Ok(), definiciones: DefsTraspaso(), guardado: guardado);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var sin = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie);
        sin.Status.Should().Be(HttpStatusCode.Unauthorized);
        sin.Json.GetProperty("code").GetString().Should().Be("session_invalid");

        var falso = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, "token-que-no-existe");
        falso.Status.Should().Be(HttpStatusCode.Unauthorized);

        var sinCookie = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, null, guardado.Token);
        sinCookie.Status.Should().Be(HttpStatusCode.Unauthorized);
        sinCookie.Json.GetProperty("code").GetString().Should().Be("sesion_punto_ausente");
    }

    [Fact]
    public async Task Requisitos_Completitud_IgnoraLoQueDiceElNavegador_YNombraLosPendientes()
    {
        var guardado = BorradorGuardado(); // sin adjuntos y sin valor guardado
        var mentira = new List<string> { "compraventa", "soat" };
        await using var host = Host(Ok(), definiciones: DefsTraspaso(), guardado: guardado);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/completitud", new
        {
            tipoCodigo = "TRASPASO_STANDARD",
            docCodesCargados = mentira,
            valorVenta = 99_000_000,
        }, entrada.Cookie, guardado.Token);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("puedeContinuarPago").GetBoolean().Should().BeFalse();
        r.Json.GetProperty("requiereValorVenta").GetBoolean().Should().BeTrue();
        var pendientes = r.Json.GetProperty("pendientes").EnumerateArray()
            .Select(p => (p.GetProperty("codigo").GetString(), p.GetProperty("nombre").GetString())).ToArray();
        pendientes.Should().Equal(
            ("compraventa", "Contrato de compraventa"),
            ("soat", "SOAT"),
            ("valor_venta", "Valor de venta"));
    }

    [Fact]
    public async Task Requisitos_Completitud_ValorMenorOIgualACero_PendienteValorVenta()
    {
        var guardado = BorradorGuardado(docs: ["compraventa", "soat"], valor: 0m);
        await using var host = Host(Ok(), definiciones: DefsTraspaso(), guardado: guardado);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, guardado.Token);

        r.Json.GetProperty("puedeContinuarPago").GetBoolean().Should().BeFalse();
        var codigos = r.Json.GetProperty("pendientes").EnumerateArray().Select(p => p.GetProperty("codigo").GetString()).ToArray();
        codigos.Should().Equal("valor_venta");
    }

    [Fact]
    public async Task Requisitos_Completitud_ExigeElCondicionalCuandoLaCondicionSeCumple()
    {
        var defs = DefsTraspaso();
        defs.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso() with
        {
            Documentos =
            [
                new DocumentoRequeridoDto("compraventa", "Contrato de compraventa", true, 1, string.Empty, string.Empty),
                new DocumentoRequeridoDto("levantar_prenda", "Levantamiento de prenda", true, 2, string.Empty, "Se pide si el vehículo tiene prenda."),
                new DocumentoRequeridoDto("rara", "Rara", true, 3, string.Empty, "Condición que el portal no conoce."),
                new DocumentoRequeridoDto("comprobante_pago", "Comprobante", true, 4, string.Empty, string.Empty),
            ],
        };

        // Sin prenda declarada: el condicional no se exige; la condición desconocida y el comprobante tampoco.
        var sinPrenda = BorradorGuardado(docs: ["compraventa"], valor: 1_000m);
        await using (var host = Host(Ok(), definiciones: defs, guardado: sinPrenda))
        {
            var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
            var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, sinPrenda.Token);
            r.Json.GetProperty("puedeContinuarPago").GetBoolean().Should().BeTrue();
        }

        // Con prenda guardada: el condicional pasa a ser obligatorio y se nombra.
        var conPrenda = BorradorGuardado(docs: ["compraventa"], valor: 1_000m, prenda: true);
        await using (var host = Host(Ok(), definiciones: defs, guardado: conPrenda))
        {
            var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
            var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, conPrenda.Token);
            r.Json.GetProperty("puedeContinuarPago").GetBoolean().Should().BeFalse();
            var p = r.Json.GetProperty("pendientes").EnumerateArray().Single();
            p.GetProperty("codigo").GetString().Should().Be("levantar_prenda");
            p.GetProperty("nombre").GetString().Should().Be("Levantamiento de prenda");
        }

        // Con el documento cargado en el borrador, el gate abre.
        var completo = BorradorGuardado(docs: ["compraventa", "levantar_prenda"], valor: 1_000m, prenda: true);
        await using (var host = Host(Ok(), definiciones: defs, guardado: completo))
        {
            var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
            var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, completo.Token);
            r.Json.GetProperty("puedeContinuarPago").GetBoolean().Should().BeTrue();
        }
    }

    [Fact]
    public async Task Requisitos_Completitud_TransformacionDeCarroceriaExigeSuDocumento()
    {
        var defs = DefsTraspaso();
        defs.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso() with
        {
            Documentos = [new DocumentoRequeridoDto("cert_carroceria", "Certificado de carrocería", true, 1, string.Empty, "Se pide si hay cambio de carrocería.")],
        };
        var guardado = BorradorGuardado(valor: 1_000m, transformaciones: ["CAMBIO_CARROCERIA"]);
        await using var host = Host(Ok(), definiciones: defs, guardado: guardado);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, guardado.Token);

        r.Json.GetProperty("pendientes").EnumerateArray().Single().GetProperty("codigo").GetString().Should().Be("cert_carroceria");
    }

    [Fact]
    public async Task Requisitos_Completitud_TodoCompleto_PuedeContinuarPago()
    {
        var guardado = BorradorGuardado(docs: ["compraventa", "soat"], valor: 15_000_000m);
        await using var host = Host(Ok(), definiciones: DefsTraspaso(), guardado: guardado);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        // El tipo puede omitirse: sale del borrador.
        var r = await host.PostAsync("/requisitos/completitud", new { }, entrada.Cookie, guardado.Token);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("puedeContinuarPago").GetBoolean().Should().BeTrue();
        r.Json.GetProperty("requiereValorVenta").GetBoolean().Should().BeTrue();
        r.Json.GetProperty("pendientes").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Requisitos_Completitud_BorradorDeOtroOrganismo_Responde403()
    {
        var guardado = BorradorGuardado(docs: ["compraventa", "soat"], valor: 1m, organismo: Guid.CreateVersion7());
        await using var host = Host(Ok(), definiciones: DefsTraspaso(), guardado: guardado);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, guardado.Token);

        r.Status.Should().Be(HttpStatusCode.Forbidden);
        r.Json.GetProperty("code").GetString().Should().Be("borrador_otro_organismo");
    }

    [Fact]
    public async Task Requisitos_Completitud_BorradorQueNoEsDelPunto_Responde409()
    {
        var guardado = BorradorGuardado(esPunto: false);
        await using var host = Host(Ok(), definiciones: DefsTraspaso(), guardado: guardado);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/requisitos/completitud", new { tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie, guardado.Token);

        r.Status.Should().Be(HttpStatusCode.Conflict);
        r.Json.GetProperty("code").GetString().Should().Be("no_es_canal_punto");
    }

    /// <summary>
    /// AC5 (#14243, solo radicante): una parte invitada no puede cargar archivos porque estructuralmente no obtiene sesión de
    /// borrador. Las únicas fuentes de token son crear la solicitud y reanudar con radicado + cédula del solicitante
    /// (la huella de la cédula del radicante); invitar solo devuelve <c>capturaUrl</c> de identidad, nunca un token.
    /// </summary>
    [Fact]
    public void Invitado_NoPuedeObtenerSesionDeBorrador_SoloCapturaDeIdentidad()
    {
        var emisores = typeof(Flit.PersonaNatural.Api.Sesiones.SesionService).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType && m.ReturnType.GetGenericArguments()[0] == typeof(Flit.PersonaNatural.Api.Sesiones.SesionEmitida))
            .Select(m => m.Name)
            .ToArray();
        emisores.Should().BeEquivalentTo(["IniciarAsync"]);
        typeof(Flit.PersonaNatural.Api.Sesiones.SesionService).GetMethod("ReanudarAsync")!.GetParameters().Select(p => p.Name)
            .Should().Equal("radicado", "cedula", "ipHash", "ct");

        var resultadoInvitar = typeof(Flit.PersonaNatural.Api.Identidad.ResultadoIdentidad).GetProperties().Select(p => p.Name);
        resultadoInvitar.Should().Contain("CapturaUrl").And.NotContain(n => n.Contains("Token", StringComparison.OrdinalIgnoreCase));
        typeof(InvitarPartePuntoRequest).GetProperties().Select(p => p.Name).Should().NotContain("Cedula");
    }

    [Fact]
    public async Task ActoresAbrir_DueñoOk_DevuelveBorradorYPuedeSerRadicante()
    {
        var borrador = new BorradorPuntoFalso();
        await using var host = Host(Ok(), RuntCon(Nombre), borrador: borrador, definiciones: DefsUnTitular());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("solicitudId").GetGuid().Should().Be(borrador.UltimaSolicitudId);
        r.Json.GetProperty("organismoId").GetGuid().Should().Be(OrganismoId);
        r.Json.GetProperty("puedeSerRadicante").GetBoolean().Should().BeTrue();
        r.Json.TryGetProperty("message", out _).Should().BeFalse();
        borrador.UltimoOrganismoId.Should().Be(OrganismoId);
        borrador.UltimaPlaca.Should().Be("ABC123");
    }

    [Fact]
    public async Task ActoresAbrir_NoDueño_PuedeSerRadicanteFalse_ConMensaje()
    {
        var runt = new ConsultasRuntFalso
        {
            Vehiculo = () =>
            {
                var r = ResultadosFalsos.VehiculoCompleto();
                r.Campos.First(c => c.Clave == TiposDelPunto.CampoOrganismoRunt).ValorTexto = Nombre;
                var campo = r.Campos.FirstOrDefault(c => c.Clave == "owner_document_number");
                if (campo is null)
                    r.Campos.Add(new Campo { Clave = "owner_document_number", ValorTexto = "9999999999" });
                else
                    campo.ValorTexto = "9999999999";
                return r;
            },
        };
        await using var host = Host(Ok(), runt, definiciones: DefsUnTitular());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("puedeSerRadicante").GetBoolean().Should().BeFalse();
        r.Json.GetProperty("message").GetString().Should().Be(ActoresPuntoService.MensajeNoDueño);
        r.Json.GetProperty("solicitudId").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task ActoresAbrir_SaltandoElArranque_PlacaDeOtroOrganismo_Responde422_SinCrearBorrador()
    {
        var borrador = new BorradorPuntoFalso();
        await using var host = Host(Ok(), RuntCon("STRIA TTOyTTE MCPAL SABANETA"), borrador: borrador, definiciones: DefsUnTitular());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        r.Json.GetProperty("code").GetString().Should().Be("organismo_no_corresponde");
        r.Json.GetProperty("detail").GetString().Should().Be(
            "Esta placa está registrada en STRIA TTOyTTE MCPAL SABANETA, no en Tránsito de Prueba. Este trámite debe hacerse en STRIA TTOyTTE MCPAL SABANETA.");
        r.Json.GetRawText().Should().NotContain("1098765432").And.NotContain("PROPIETARIO");
        borrador.UltimoOrganismoId.Should().BeNull("no se creó el borrador");
    }

    [Fact]
    public async Task ActoresAbrir_RuntSinOrganismo_Responde422_ConElMensajeClaro()
    {
        var borrador = new BorradorPuntoFalso();
        await using var host = Host(Ok(), RuntCon(""), borrador: borrador, definiciones: DefsUnTitular());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        r.Json.GetProperty("code").GetString().Should().Be("organismo_no_corresponde");
        r.Json.GetProperty("detail").GetString().Should().Be(TiposDelPunto.OrganismoDesconocido);
        borrador.UltimoOrganismoId.Should().BeNull();
    }

    [Fact]
    public async Task ActoresAbrir_CoincideYMarcaElOrganismoVerificado_YElOperadorQueEligeNoCompara()
    {
        var ok = new BorradorPuntoFalso();
        await using (var host = Host(Ok(), RuntCon(Nombre), borrador: ok, definiciones: DefsUnTitular()))
        {
            var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
            (await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie)).Status.Should().Be(HttpStatusCode.OK);
        }

        ok.UltimoVerificado.Should().BeTrue();

        var elige = new BorradorPuntoFalso();
        var defs = DefsUnTitular();
        defs.PorCodigo["CAMBIO_COLOR"] = defs.PorCodigo["CAMBIO_COLOR"] with { Flags = defs.PorCodigo["CAMBIO_COLOR"].Flags with { OrganismoLoEligeOperador = true } };
        await using (var host = Host(Ok(), RuntCon("OT DE OTRO MUNICIPIO"), borrador: elige, definiciones: defs))
        {
            var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
            (await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie)).Status.Should().Be(HttpStatusCode.OK);
        }

        elige.UltimoOrganismoId.Should().Be(OrganismoId);
        elige.UltimoVerificado.Should().BeFalse("no se comparó con el RUNT");
    }

    [Fact]
    public async Task ActoresAbrir_RuntCaido_Responde503_SinCrearBorrador()
    {
        var borrador = new BorradorPuntoFalso();
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.ProveedorCaido };
        await using var host = Host(Ok(), runt, borrador: borrador, definiciones: DefsUnTitular());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        r.Json.GetProperty("code").GetString().Should().Be("proveedor_no_disponible");
        borrador.UltimoOrganismoId.Should().BeNull();
    }

    private static FachadaDefinicionesFalsa DefsUnTitular()
    {
        var defs = new FachadaDefinicionesFalsa();
        defs.PorCodigo["CAMBIO_COLOR"] = DefinicionesDePrueba.UnTitular() with { Actores = [new ActorRolDto("titular", "Titular", 1, 1, true, true, true)] };
        return defs;
    }

    [Fact]
    public async Task ActoresAbrir_TipoDeUnSoloRol_ExponeSoloElTitularComoRadicante_SinComprador()
    {
        await using var host = Host(Ok(), RuntCon(Nombre), definiciones: DefsUnTitular());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        var roles = r.Json.GetProperty("roles").EnumerateArray().ToArray();
        roles.Should().ContainSingle();
        roles[0].GetProperty("rol").GetString().Should().Be("titular");
        roles[0].GetProperty("etiqueta").GetString().Should().Be("Titular");
        roles[0].GetProperty("esRadicante").GetBoolean().Should().BeTrue();
        roles[0].GetProperty("minimo").GetInt32().Should().Be(1);
        roles[0].GetProperty("maximo").GetInt32().Should().Be(1);
        r.Json.GetProperty("puedeSerRadicante").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ActoresAbrir_Traspaso_VendedorRadicanteYCompradorInvitable()
    {
        var defs = DefsTraspaso();
        await using var host = Host(Ok(), RuntCon(Nombre), definiciones: defs);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "TRASPASO_STANDARD" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        var roles = r.Json.GetProperty("roles").EnumerateArray().ToArray();
        roles.Select(x => x.GetProperty("rol").GetString()).Should().Equal("vendedor", "comprador");
        roles.Select(x => x.GetProperty("esRadicante").GetBoolean()).Should().Equal(true, false);
        roles[1].GetProperty("maximo").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task ActoresAbrir_TipoSinDefinicion_Responde404()
    {
        await using var host = Host(Ok(), RuntCon(Nombre), definiciones: new FachadaDefinicionesFalsa());
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/actores/abrir", new { placa = "ABC123", documento = "1098765432", tipoCodigo = "NO_EXISTE" }, entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void RolesPunto_DesdeSnapshot_ConservaElOrden_YElPrimeroEsRadicante()
    {
        var snapshot = DefinicionesService.Serializar(DefinicionesDePrueba.Traspaso());

        var roles = RolesPunto.Desde(snapshot);

        roles.Select(r => r.Rol).Should().Equal("vendedor", "comprador");
        roles.Select(r => r.EsRadicante).Should().Equal(true, false);
        RolesPunto.Desde("{}").Should().BeEmpty();
        RolesPunto.Desde("no json").Should().BeEmpty();
    }

    [Fact]
    public void Actores_EsPropietarioRunt_ComparaDocumentoDelPropietario()
    {
        var dueño = ResultadosFalsos.VehiculoCompleto();
        var propietario = new DocumentoConsulta("CC", "1098765432");
        ActoresPuntoService.EsPropietarioRunt(dueño, propietario).Should().BeTrue();

        var campo = dueño.Campos.First(c => c.Clave == "owner_document_number");
        campo.ValorTexto = "111";
        ActoresPuntoService.EsPropietarioRunt(dueño, propietario).Should().BeFalse();
        ActoresPuntoService.EsPropietarioRunt(ResultadosFalsos.VehiculoInexistente(), propietario).Should().BeFalse();
    }

    [Fact]
    public async Task Consultas_NoCreanTramite_YSinSesionResponden401()
    {
        typeof(ArranquePuntoService).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType.Name.Contains("Db") || p.ParameterType.Name.Contains("Procedure"));
        typeof(ConsultasPuntoService).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType.Name.Contains("Db") || p.ParameterType.Name.Contains("Procedure"));
        typeof(DiagnosticoPuntoService).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType.Name.Contains("Db") || p.ParameterType.Name.Contains("Procedure") || p.ParameterType.Name.Contains("Solicitud"));

        var runt = new ConsultasRuntFalso
        {
            Vehiculo = () => ResultadosFalsos.VehiculoCompleto(),
            Conductor = () => ResultadosFalsos.Conductor(),
            Multas = MultasConUnComparendo,
        };
        await using var host = Host(Ok(), runt, FachadaCon());

        (await host.PostAsync("/consultas/vehiculo", CuerpoVehiculo())).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await host.PostAsync("/consultas/persona", CuerpoPersona())).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await host.PostAsync("/consultas/multas", CuerpoMultas())).Status.Should().Be(HttpStatusCode.Unauthorized);

        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });
        (await host.PostAsync("/consultas/vehiculo", CuerpoVehiculo(), entrada.Cookie)).Status.Should().Be(HttpStatusCode.OK);
        (await host.PostAsync("/consultas/persona", CuerpoPersona(), entrada.Cookie)).Status.Should().Be(HttpStatusCode.OK);
        var multas = await host.PostAsync("/consultas/multas", CuerpoMultas(), entrada.Cookie);
        multas.Status.Should().Be(HttpStatusCode.OK);
        multas.Json.GetProperty("comparendos").GetInt32().Should().Be(1);

        runt.LlamadasVehiculo.Should().ContainSingle();
        runt.LlamadasConductor.Should().ContainSingle();
        runt.LlamadasMultas.Should().ContainSingle();
    }

    [Fact]
    public async Task Diagnostico_ConSesion_DevuelvePuedeContinuarYRequisitos()
    {
        var requisitos = new FachadaRequisitosPuntoFalsa
        {
            Respuesta = new DiagnosticoDto(true, [new RequisitoDto("soat", "SOAT vigente", "cumple")]),
        };
        await using var host = Host(Ok(), requisitos: requisitos);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/diagnostico", CuerpoDiagnostico(), entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("puedeContinuar").GetBoolean().Should().BeTrue();
        r.Json.GetProperty("requisitos")[0].GetProperty("codigo").GetString().Should().Be("soat");
        requisitos.UltimaOrganismoId.Should().Be(OrganismoId.ToString());
        requisitos.UltimaPlaca.Should().Be("ABC123");
        requisitos.UltimoTipoDocumento.Should().Be("CC");
        requisitos.UltimoDocumento.Should().Be("1098765432");
        requisitos.UltimoTipoCodigo.Should().Be("CAMBIO_COLOR");
    }

    [Fact]
    public async Task Diagnostico_Bloqueo_DevuelvePuedeContinuarFalse()
    {
        var requisitos = new FachadaRequisitosPuntoFalsa
        {
            Respuesta = new DiagnosticoDto(false, [new RequisitoDto("soat", "SOAT", "falta")]),
        };
        await using var host = Host(Ok(), requisitos: requisitos);
        var entrada = await host.PostAsync("/entrada", new { codigo = Codigo });

        var r = await host.PostAsync("/diagnostico", CuerpoDiagnostico(), entrada.Cookie);

        r.Status.Should().Be(HttpStatusCode.OK);
        r.Json.GetProperty("puedeContinuar").GetBoolean().Should().BeFalse();
        r.Json.GetProperty("requisitos").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Diagnostico_SinSesion_Responde401_DatosInvalidos400_YFachadaCaida503()
    {
        await using var hostOk = Host(Ok(), requisitos: new FachadaRequisitosPuntoFalsa());
        (await hostOk.PostAsync("/diagnostico", CuerpoDiagnostico())).Status.Should().Be(HttpStatusCode.Unauthorized);

        var entrada = await hostOk.PostAsync("/entrada", new { codigo = Codigo });
        var invalido = await hostOk.PostAsync("/diagnostico", new { placa = "ABC123" }, entrada.Cookie);
        invalido.Status.Should().Be(HttpStatusCode.BadRequest);

        var caida = new FachadaRequisitosPuntoFalsa { Lanzar = true };
        await using var hostCaida = Host(Ok(), requisitos: caida);
        var cookie = (await hostCaida.PostAsync("/entrada", new { codigo = Codigo })).Cookie;
        var r = await hostCaida.PostAsync("/diagnostico", CuerpoDiagnostico(), cookie);
        r.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        r.Json.GetProperty("code").GetString().Should().Be("proveedor_no_disponible");
        r.Json.GetProperty("message").GetString().Should().Be(DiagnosticoPuntoService.MensajeFachadaCaida);
    }

    private static FachadaDefinicionesFalsa DefsTraspaso()
    {
        var defs = new FachadaDefinicionesFalsa();
        defs.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
        return defs;
    }

    private static BorradorGuardadoFalso BorradorGuardado(
        string[]? docs = null,
        decimal? valor = null,
        bool prenda = false,
        string[]? transformaciones = null,
        Guid? organismo = null,
        bool esPunto = true,
        Dictionary<string, string>? campos = null,
        PrendaDatos? prendaDatos = null) =>
        new(new DatosBorradorPunto(
            Guid.CreateVersion7(),
            organismo ?? OrganismoId,
            "TRASPASO_STANDARD",
            esPunto,
            new DatosSolicitud(
                valor is null ? null : new ComercialDatos(valor),
                prendaDatos ?? (prenda ? new PrendaDatos(true, "Banco", "900123456") : null),
                transformaciones is null ? null : new RequisitosDatos(transformaciones, null)),
            campos ?? new Dictionary<string, string>(),
            new HashSet<string>(docs ?? [], StringComparer.Ordinal),
            false));

    /// <summary>Borrador "guardado" en servidor: el token solo abre ese borrador (la sesión real viene de <c>SesionService</c>).</summary>
    private sealed class BorradorGuardadoFalso(DatosBorradorPunto? datos = null) : IBorradorPunto
    {
        public string Token { get; } = "token-borrador-" + Guid.NewGuid().ToString("N");

        public Task<Guid?> ResolverSesionAsync(string? token, CancellationToken ct) =>
            Task.FromResult(datos is not null && token == Token ? datos.SolicitudId : (Guid?)null);

        public Task<DatosBorradorPunto?> LeerAsync(Guid solicitudId, CancellationToken ct) =>
            Task.FromResult(datos is not null && datos.SolicitudId == solicitudId ? datos : null);
    }

    private static OrganismoPunto Sesion() => new(OrganismoId, Codigo, Nombre);

    private static ResolucionCanalPunto Ok() => new("ok", OrganismoId, Codigo, Nombre);

    private static ConsultasRuntFalso RuntCon(string organismo)
    {
        return new ConsultasRuntFalso
        {
            Vehiculo = () =>
            {
                var r = ResultadosFalsos.VehiculoCompleto();
                // VehiculoCompleto ya trae transit_office_name; sustituir (no duplicar) o Arranque lee el primero y falla el match.
                var campo = r.Campos.FirstOrDefault(c => c.Clave == TiposDelPunto.CampoOrganismoRunt);
                if (campo is null)
                    r.Campos.Add(new Campo { Clave = TiposDelPunto.CampoOrganismoRunt, ValorTexto = organismo });
                else
                    campo.ValorTexto = organismo;
                return r;
            },
        };
    }

    private static Task<ResultadoArranque> Arranque(ConsultasRuntFalso runt, OrganismoPunto sesion, string placa, string documento, string tipo) =>
        new ArranquePuntoService(runt, FachadaCon(
            new TipoTramiteDto("CAMBIO_COLOR", "Color", "OTROS", 1, true, string.Empty),
            new TipoTramiteDto("TRASPASO_STANDARD", "Traspaso", "TRASPASO", 1, true, string.Empty),
            new TipoTramiteDto("DUPLICADO_PLACA", "Duplicado", "OTROS", 1, true, string.Empty))).EvaluarAsync(sesion, placa, documento, tipo, TestContext.Current.CancellationToken);

    private static FachadaTipos FachadaCon(params TipoTramiteDto[] tipos)
    {
        var f = new FachadaTipos();
        f.Tipos.AddRange(tipos);
        return f;
    }

    private static object CuerpoVehiculo() => new { placa = "ABC123", propietario = new { tipo = "CC", numero = "1098765432" }, consentimiento = true };

    private static object CuerpoPersona() => new { documento = new { tipo = "CC", numero = "1098765432" }, consentimiento = true };

    private static object CuerpoMultas() => new { documento = new { tipo = "CC", numero = "1098765432" }, consentimiento = true };

    private static object CuerpoDiagnostico() => new { placa = "ABC123", documento = "1098765432", tipoCodigo = "CAMBIO_COLOR" };

    private static ResultadoConsulta MultasConUnComparendo()
    {
        var r = new ResultadoConsulta { Semaforo = Semaforo.Verde };
        var chequeo = new Chequeo { Clave = "simit", Estado = EstadoChequeo.Ok };
        chequeo.Comparendos.Add(new Comparendo { Numero = "M1" });
        r.Chequeos.Add(chequeo);
        return r;
    }

    private static string? CookieUtil(string? setCookie)
    {
        if (string.IsNullOrEmpty(setCookie))
            return null;
        var semi = setCookie.IndexOf(';');
        var par = semi < 0 ? setCookie : setCookie[..semi];
        const string prefijo = "pn_punto=";
        return par.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) ? par[prefijo.Length..] : null;
    }

    private static HostPunto Host(
        ResolucionCanalPunto resolucion,
        ConsultasRuntFalso? runt = null,
        FachadaTipos? fachada = null,
        FachadaRequisitosPuntoFalsa? requisitos = null,
        BorradorPuntoFalso? borrador = null,
        FachadaDefinicionesFalsa? definiciones = null,
        BorradorGuardadoFalso? guardado = null) =>
        new(resolucion, runt ?? new ConsultasRuntFalso(), fachada ?? FachadaCon(), requisitos ?? new FachadaRequisitosPuntoFalsa(), borrador ?? new BorradorPuntoFalso(), definiciones ?? new FachadaDefinicionesFalsa(), guardado ?? new BorradorGuardadoFalso());

    private sealed class HostPunto : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http;

        public HostPunto(
            ResolucionCanalPunto resolucion,
            ConsultasRuntFalso runt,
            FachadaTipos fachada,
            FachadaRequisitosPuntoFalsa requisitos,
            BorradorPuntoFalso borrador,
            FachadaDefinicionesFalsa definiciones,
            BorradorGuardadoFalso guardado)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration["Pii:ClaveHmac"] = "0123456789abcdef0123456789abcdef";
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<ICanalPunto>(new CanalFijo(resolucion));
            builder.Services.AddSingleton<IConsultasRunt>(runt);
            builder.Services.AddSingleton<IFachadaTramites>(fachada);
            builder.Services.AddSingleton<IFachadaRequisitos>(requisitos);
            builder.Services.AddSingleton<IFachadaDefiniciones>(definiciones);
            builder.Services.AddSingleton(new ConsultaVehiculoService(runt));
            builder.Services.AddSingleton(new ConsultaConductorService(runt));
            builder.Services.AddDataProtection();
            builder.Services.AddSingleton<CookiePunto>();
            builder.Services.AddSingleton<PiiHasher>();
            builder.Services.AddSingleton<ArranquePuntoService>();
            builder.Services.AddSingleton<ConsultasPuntoService>();
            builder.Services.AddSingleton<DiagnosticoPuntoService>();
            builder.Services.AddSingleton<IAbrirBorradorDesdePunto>(borrador);
            builder.Services.AddSingleton<ActoresPuntoService>();
            builder.Services.AddSingleton<IBorradorPunto>(guardado);
            builder.Services.AddSingleton<RequisitosPuntoService>();
            builder.Services.AddSingleton<CompletitudRequisitosPuntoService>();
            builder.Services.AddRateLimiter(o => o.AddPolicy(AbusoEndpoints.PoliticaPorIp, _ => RateLimitPartition.GetNoLimiter("punto-test")));
            _app = builder.Build();
            _app.UseRateLimiter();
            _app.MapPuntoEndpoints();
            _app.StartAsync().GetAwaiter().GetResult();
            _http = _app.GetTestClient();
        }

        public async Task<RespuestaPunto> PostAsync(string ruta, object cuerpo, string? cookie = null, string? bearer = null)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, Base + ruta) { Content = JsonContent.Create(cuerpo) };
            if (bearer is not null)
                peticion.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            if (cookie is not null)
                peticion.Headers.TryAddWithoutValidation("Cookie", "pn_punto=" + cookie);
            return await LeerAsync(await _http.SendAsync(peticion, TestContext.Current.CancellationToken)).ConfigureAwait(false);
        }

        public async Task<RespuestaPunto> GetAsync(string ruta, string? cookie)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, Base + ruta);
            if (!string.IsNullOrEmpty(cookie))
                peticion.Headers.TryAddWithoutValidation("Cookie", "pn_punto=" + cookie);
            return await LeerAsync(await _http.SendAsync(peticion, TestContext.Current.CancellationToken)).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<RespuestaPunto> LeerAsync(HttpResponseMessage respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
            var setCookie = respuesta.Headers.TryGetValues("Set-Cookie", out var valores)
                ? valores.FirstOrDefault(v => v.StartsWith("pn_punto=", StringComparison.OrdinalIgnoreCase))
                : null;
            JsonElement json = default;
            if (texto.Length > 0)
            {
                try
                {
                    json = JsonDocument.Parse(texto).RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Respuesta no JSON ({(int)respuesta.StatusCode}): {texto[..Math.Min(texto.Length, 500)]}", ex);
                }
            }

            return new RespuestaPunto(respuesta.StatusCode, json, setCookie, CookieUtil(setCookie));
        }
    }

    private sealed record RespuestaPunto(HttpStatusCode Status, JsonElement Json, string? SetCookie, string? Cookie);

    private sealed class CanalFijo(ResolucionCanalPunto resolucion) : ICanalPunto
    {
        public Task<ResolucionCanalPunto> ResolverAsync(string codigo, CancellationToken ct) => Task.FromResult(resolucion);
    }

    private sealed class FachadaTipos : IFachadaTramites
    {
        public List<TipoTramiteDto> Tipos { get; } = [];
        public string? UltimoOrganismo { get; private set; }

        public List<OrganismoDto> Organismos { get; } = [];
        public int LlamadasOrganismos { get; private set; }

        public Task<IReadOnlyList<OrganismoDto>> ListarOrganismosAsync(CancellationToken ct)
        {
            LlamadasOrganismos++;
            return Task.FromResult<IReadOnlyList<OrganismoDto>>(Organismos);
        }

        public Task<IReadOnlyList<TipoTramiteDto>> ListarTiposAsync(string? organismoId, CancellationToken ct)
        {
            UltimoOrganismo = organismoId;
            return Task.FromResult<IReadOnlyList<TipoTramiteDto>>(Tipos);
        }

        public Task<CotizacionDto> CotizarAsync(string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FachadaRequisitosPuntoFalsa : IFachadaRequisitos
    {
        public DiagnosticoDto Respuesta { get; set; } = new(true, []);
        public bool Lanzar { get; set; }
        public string? UltimaOrganismoId { get; private set; }
        public string? UltimaPlaca { get; private set; }
        public string? UltimoTipoDocumento { get; private set; }
        public string? UltimoDocumento { get; private set; }
        public string? UltimoTipoCodigo { get; private set; }

        public Task<DiagnosticoDto> EvaluarAsync(
            string tipoCodigo,
            string organismoId,
            string? placa,
            string? vin,
            string? tipoDocumentoPropietario,
            string? documentoPropietario,
            CancellationToken ct)
        {
            UltimoTipoCodigo = tipoCodigo;
            UltimaOrganismoId = organismoId;
            UltimaPlaca = placa;
            UltimoTipoDocumento = tipoDocumentoPropietario;
            UltimoDocumento = documentoPropietario;
            if (Lanzar)
                throw new FachadaException("servicio_no_disponible", "El diagnóstico no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);
            return Task.FromResult(Respuesta);
        }
    }

    private sealed class BorradorPuntoFalso : IAbrirBorradorDesdePunto
    {
        public Guid UltimaSolicitudId { get; private set; } = Guid.CreateVersion7();
        public Guid? UltimoOrganismoId { get; private set; }
        public bool UltimoVerificado { get; private set; }
        public string? UltimaPlaca { get; private set; }

        public Task<SesionCreadaDto> AbrirAsync(
            OrganismoPunto organismo,
            string tipoCodigo,
            string placa,
            string documento,
            byte[]? ipHash,
            CancellationToken ct,
            bool organismoVerificado = false)
        {
            UltimoOrganismoId = organismo.OrganismoId;
            UltimoVerificado = organismoVerificado;
            UltimaPlaca = placa;
            UltimaSolicitudId = Guid.CreateVersion7();
            return Task.FromResult(new SesionCreadaDto(
                "token-falso",
                DateTimeOffset.UtcNow.AddHours(1),
                UltimaSolicitudId,
                "PN-2026-000001",
                EstadoSolicitud.Borrador,
                tipoCodigo,
                1));
        }
    }
}

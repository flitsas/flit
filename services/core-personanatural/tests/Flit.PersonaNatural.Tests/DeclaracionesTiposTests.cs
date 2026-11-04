using System.Text.Json;
using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Catalogos;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU 5, 6, 8 y 10 — el BFF valida y completa las declaraciones del tipo, las decisiones de prenda y el destino contra la
/// definición congelada. Reglas puras: sin host, sin base.
/// </summary>
public sealed class DeclaracionesTiposTests
{
    private static JsonElement J(object valor) => JsonSerializer.SerializeToElement(valor);

    private static DeclaracionTipoDto Color(bool obligatoria = true, string activadaPor = "", string clave = "transformacion_color") => new(
        clave, "seleccion_catalogo", "Nuevo color", obligatoria, [], "vehicle_color", ["cambio_color"], "soporte_cambio_color", false, "colores", activadaPor);

    private static DeclaracionTipoDto Combustible() => new(
        "transformacion_combustible", "seleccion_catalogo", "Nuevo combustible", true,
        [new OpcionDeclaracionDto("GASOLINA", "GASOLINA", [], false), new OpcionDeclaracionDto("GAS NATURAL", "GAS NATURAL", [], false)],
        "vehicle_fuel", ["cambio_combustible"], "soporte_conversion_combustible", false, "combustibles", string.Empty);

    private static DeclaracionTipoDto Blindaje() => new(
        "blindaje", "seleccion", "Blindaje", true,
        [new OpcionDeclaracionDto("NIVEL_1", "Blindaje nivel 1", ["certificado_blindaje"], true), new OpcionDeclaracionDto("DESMONTE", "Desmontar blindaje", ["certificado_blindaje"], true)],
        "blindaje_nivel", ["blindaje"], "certificado_blindaje", true, string.Empty, string.Empty);

    private static DeclaracionTipoDto Causal() => new(
        "cancelacion_causal", "seleccion", "Causal de cancelación", true,
        [
            new OpcionDeclaracionDto("DECISION_JUDICIAL", "Decisión judicial", ["oficio_judicial"], true),
            new OpcionDeclaracionDto("PERDIDA_TOTAL_ACCIDENTE", "Pérdida total por accidente", ["certificado_dijin", "certificado_aseguradora_perito", "certificado_autoridad_administrativa"], true),
        ],
        "cancelacion_causal", [], string.Empty, false, string.Empty, string.Empty);

    private static DeclaracionTipoDto Destino() => new(
        "organismo_destino", "organismo_destino", "Secretaría de destino", true, [],
        "transit_office_destino_id", ["transit_office_destino_code", "transit_office_destino_name", "transit_office_destino_city"], string.Empty, false, string.Empty, string.Empty);

    private static DefinicionTipoDto Def(params DeclaracionTipoDto[] declaraciones) => DefinicionesDePrueba.UnTitular() with
    {
        Pasos =
        [
            new PasoDto("consulta", "Consulta", ["vehicle_query"], []),
            new PasoDto("documentos", "Documentos", ["document_checklist"], []),
        ],
        Documentos = [],
        Declaraciones = declaraciones,
    };

    private static (IReadOnlyDictionary<string, string?> Salida, ErroresDeCampo Errores) Validar(DefinicionTipoDto def, params (string Clave, object Valor)[] valores)
    {
        var errores = new ErroresDeCampo();
        var salida = ReglasBorrador.ValidarCampos(def, valores.ToDictionary(v => v.Clave, v => J(v.Valor)), errores);
        return (salida, errores);
    }

    // ── HU 5: ValidarCampos ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LaClaveDeUnaDeclaracion_SeAcepta_YPrendeLaBanderaCambio()
    {
        var (salida, errores) = Validar(Def(Color()), ("vehicle_color", "ROJO"));

        errores.Hay.Should().BeFalse();
        salida.Should().Equal(new Dictionary<string, string?> { ["vehicle_color"] = "ROJO", ["cambio_color"] = "true" });
    }

    [Fact]
    public void UnaClaveQueNiElTipoNiSusDeclaracionesTienen_SigueRechazada()
    {
        var (_, errores) = Validar(Def(Color()), ("vehicle_fuel", "GASOLINA"));

        errores.Todos().Should().ContainKey("valores.vehicle_fuel");
    }

    [Fact]
    public void UnTipoSinDeclaraciones_NoAceptaLasClavesDeDeclaracion()
    {
        var (_, errores) = Validar(Def(), ("vehicle_color", "ROJO"));

        errores.Hay.Should().BeTrue();
    }

    [Fact]
    public void LasClavesDerivadas_SeIgnoran_PorqueLasCompletaElServidor()
    {
        var (salida, errores) = Validar(Def(Blindaje()), ("blindaje_nivel", "DESMONTE"), ("blindaje", "true"));

        errores.Hay.Should().BeFalse();
        salida.Should().Equal(new Dictionary<string, string?> { ["blindaje_nivel"] = "DESMONTE" }, "el desmonte deja la bandera derivada en el servidor, no la que mande el navegador");
    }

    [Theory]
    [InlineData("NIVEL_1", true)]
    [InlineData("NIVEL_9", false)]
    [InlineData("nivel_1", false)]
    public void UnaSeleccion_SoloAceptaUnaDeSusOpciones(string valor, bool valido)
    {
        var (_, errores) = Validar(Def(Blindaje()), ("blindaje_nivel", valor));

        errores.Hay.Should().Be(!valido);
    }

    [Fact]
    public void ElCombustible_SeValidaContraSusOpciones()
    {
        Validar(Def(Combustible()), ("vehicle_fuel", "GAS NATURAL")).Errores.Hay.Should().BeFalse();
        Validar(Def(Combustible()), ("vehicle_fuel", "KEROSENE")).Errores.Hay.Should().BeTrue();
    }

    [Fact]
    public void UnValorDeCatalogoSinOpciones_AceptaTextoDeHastaCienCaracteres()
    {
        Validar(Def(Color()), ("vehicle_color", new string('A', 100))).Errores.Hay.Should().BeFalse();
        Validar(Def(Color()), ("vehicle_color", new string('A', 101))).Errores.Hay.Should().BeTrue();
    }

    [Fact]
    public void VaciarUnaDeclaracionObligatoria_EsError_YVaciarUnaOpcional_ApagaLaBandera()
    {
        Validar(Def(Color()), ("vehicle_color", "")).Errores.Todos().Should().ContainKey("valores.vehicle_color");

        var (salida, errores) = Validar(Def(Color(obligatoria: false, activadaPor: "CAMBIO_COLOR", clave: "simultaneo_color")), ("vehicle_color", ""));
        errores.Hay.Should().BeFalse();
        salida.Should().Equal(new Dictionary<string, string?> { ["vehicle_color"] = null, ["cambio_color"] = null });
    }

    [Fact]
    public void ElDestino_DebeSerUnIdentificadorDeOrganismo()
    {
        var id = Guid.CreateVersion7();

        Validar(Def(Destino()), ("transit_office_destino_id", id.ToString().ToUpperInvariant())).Salida
            .Should().Equal(new Dictionary<string, string?> { ["transit_office_destino_id"] = id.ToString() });
        Validar(Def(Destino()), ("transit_office_destino_id", "no-es-un-guid")).Errores.Hay.Should().BeTrue();
        Validar(Def(Destino()), ("transit_office_destino_id", Guid.Empty.ToString())).Errores.Hay.Should().BeTrue();
    }

    // ── HU 5: Completitud ────────────────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<PendienteDto> Pendientes(
        DefinicionTipoDto def, Dictionary<string, string>? campos = null, string[]? docs = null, string[]? transformaciones = null, PrendaDatos? prenda = null) =>
        ReglasBorrador.Completitud(
            def,
            campos ?? [],
            new DatosSolicitud(null, prenda, transformaciones is null ? null : new RequisitosDatos(transformaciones, null)),
            [("comprador", 1)],
            new HashSet<string>(docs ?? [], StringComparer.Ordinal)).Pendientes;

    [Fact]
    public void SinElValorDeLaDeclaracion_Falta_ConElPasoDeRequisitos()
    {
        var p = Pendientes(Def(Color())).Should().ContainSingle().Which;

        p.Should().Be(new PendienteDto("declaracion_requerida", "transformacion_color", "documentos"));
        Pendientes(Def(Color()), new() { ["vehicle_color"] = "ROJO" }).Should().BeEmpty("el soporte del color es opcional");
    }

    [Fact]
    public void ElDestino_SePideEnElPasoDeLaConsulta()
    {
        Pendientes(Def(Destino())).Should().ContainSingle().Which.Should().Be(new PendienteDto("declaracion_requerida", "organismo_destino", "consulta"));
    }

    [Fact]
    public void Blindaje_ElegidoSinCertificado_PideElDocumento_YConCertificadoCompleta()
    {
        var def = Def(Blindaje()) with
        {
            Documentos = [new DocumentoRequeridoDto("certificado_blindaje", "Certificado de blindaje", true, 1, string.Empty, string.Empty)],
        };

        Pendientes(def).Should().Equal(new PendienteDto("declaracion_requerida", "blindaje", "documentos"));
        Pendientes(def, new() { ["blindaje_nivel"] = "DESMONTE" }).Should().Equal(new PendienteDto("documento_requerido", "certificado_blindaje", "documentos"));
        Pendientes(def, new() { ["blindaje_nivel"] = "DESMONTE" }, ["certificado_blindaje"]).Should().BeEmpty();
    }

    [Fact]
    public void Causal_ExigeTodosLosDocumentosDeLaCausalElegida_NoLosDeLasOtras()
    {
        var def = Def(Causal());

        Pendientes(def, new() { ["cancelacion_causal"] = "DECISION_JUDICIAL" }).Select(p => p.Referencia).Should().Equal("oficio_judicial");
        Pendientes(def, new() { ["cancelacion_causal"] = "PERDIDA_TOTAL_ACCIDENTE" }, ["certificado_dijin"]).Select(p => p.Referencia)
            .Should().Equal("certificado_aseguradora_perito", "certificado_autoridad_administrativa");
        Pendientes(def, new() { ["cancelacion_causal"] = "PERDIDA_TOTAL_ACCIDENTE" }, ["certificado_dijin", "certificado_aseguradora_perito", "certificado_autoridad_administrativa"]).Should().BeEmpty();
    }

    [Fact]
    public void UnaCausalQueYaNoExisteEnLaDefinicion_CuentaComoSinDeclarar()
    {
        Pendientes(Def(Causal()), new() { ["cancelacion_causal"] = "CAUSAL_RETIRADA" }).Should().ContainSingle()
            .Which.Codigo.Should().Be("declaracion_requerida");
    }

    [Fact]
    public void UnSimultaneo_SoloSePideSiElCiudadanoMarcoSuTransformacion()
    {
        var def = Def(Color(obligatoria: false, activadaPor: "CAMBIO_COLOR", clave: "simultaneo_color"));

        Pendientes(def).Should().BeEmpty();
        Pendientes(def, transformaciones: ["CAMBIO_CARROCERIA"]).Should().BeEmpty();
        Pendientes(def, transformaciones: ["CAMBIO_COLOR"]).Should().ContainSingle().Which.Referencia.Should().Be("simultaneo_color");
        Pendientes(def, new() { ["vehicle_color"] = "AZUL" }, transformaciones: ["CAMBIO_COLOR"]).Should().BeEmpty();
    }

    [Fact]
    public void UnBorradorAnterior_SinDeclaraciones_SigueValido()
    {
        // Un snapshot guardado antes de las declaraciones se lee con la lista vacía y se evalúa como siempre.
        var snapshot = JsonSerializer.Serialize(DefinicionesDePrueba.UnTitular(), DefinicionesService.Json);
        var sinCampo = snapshot.Replace(",\"declaraciones\":[]", string.Empty, StringComparison.Ordinal);
        sinCampo.Should().NotContain("declaraciones");

        var leida = DefinicionesService.Leer(sinCampo)!;

        leida.Declaraciones.Should().BeEmpty();
        Pendientes(leida, new() { ["color_nuevo"] = "ROJO" }, ["tarjeta_propiedad"]).Should().BeEmpty();
    }

    [Fact]
    public void LaDefinicionCongelada_ConservaLasDeclaracionesAlSerializarYLeer()
    {
        var def = Def(Blindaje(), Destino());

        var leida = DefinicionesService.Leer(DefinicionesService.Serializar(def))!;

        leida.Declaraciones.Select(d => d.Clave).Should().Equal("blindaje", "organismo_destino");
        leida.Declaraciones[0].Opciones[0].DocCodes.Should().Equal("certificado_blindaje");
        leida.Declaraciones[1].FieldKeysExtra.Should().HaveCount(3);
    }

    [Fact]
    public void LasClavesDeDeclaraciones_IncluyenLaPrincipalYLasDerivadas()
    {
        var claves = ReglasBorrador.ClavesDeDeclaraciones(Def(Color(), Blindaje()));

        claves.Keys.Should().BeEquivalentTo("vehicle_color", "cambio_color", "blindaje_nivel", "blindaje");
        claves["vehicle_color"].EsExtra.Should().BeFalse();
        claves["cambio_color"].EsExtra.Should().BeTrue();
    }

    // ── HU 5: adjuntos ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LosAdjuntos_AceptanLosDocumentosDeLasDeclaraciones_YDeLaPrendaSoloSiElTipoLaAdmite()
    {
        var def = Def(Color(), Blindaje(), Causal());

        AdjuntosService.DocumentoAdmitido(def, "soporte_cambio_color").Should().BeTrue();
        AdjuntosService.DocumentoAdmitido(def, "certificado_blindaje").Should().BeTrue();
        AdjuntosService.DocumentoAdmitido(def, "certificado_dijin").Should().BeTrue("lo reclama una opción de la causal");
        AdjuntosService.DocumentoAdmitido(def, "factura_carroceria").Should().BeFalse("ninguna declaración de este tipo lo reclama");
        AdjuntosService.DocumentoAdmitido(def, "prenda_registro").Should().BeFalse("el tipo no admite prenda");

        var conPrenda = def with { Flags = def.Flags with { PermitePrendaComplementaria = true } };
        AdjuntosService.DocumentoAdmitido(conPrenda, "prenda_registro").Should().BeTrue();
        AdjuntosService.DocumentoAdmitido(conPrenda, "prenda_levantamiento").Should().BeTrue();
        AdjuntosService.DocumentoAdmitido(conPrenda, "documento_inventado").Should().BeFalse();
    }

    // ── HU 8: prenda ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static (PrendaDatos? Prenda, ErroresDeCampo Errores) ValidarPrenda(DefinicionTipoDto def, PrendaRequest prenda)
    {
        var errores = new ErroresDeCampo();
        var (_, p) = ReglasBorrador.ValidarComercial(def, new ComercialRequest(null, null, prenda), errores);
        return (p, errores);
    }

    private static DefinicionTipoDto ConPrenda(bool exige = false) => Def() with
    {
        Flags = new FlagsTipoDto("PLATE", false, exige, false, true, true, false),
    };

    [Theory]
    [InlineData("registrar", true)]
    [InlineData("levantar", true)]
    [InlineData("LEVANTAR", true)]
    public void LaDecisionRegistrarOLevantar_ExigeAcreedorYNit_YQuedaEnElBorrador(string decision, bool tiene)
    {
        var (prenda, errores) = ValidarPrenda(ConPrenda(), new PrendaRequest(false, "Banco Uno", "900.123.456", decision));

        errores.Hay.Should().BeFalse();
        prenda.Should().Be(new PrendaDatos(tiene, "Banco Uno", "900123456", decision.ToLowerInvariant()));
    }

    [Fact]
    public void LaDecisionLevantar_SinAcreedor_Falla()
    {
        var (_, errores) = ValidarPrenda(ConPrenda(), new PrendaRequest(false, null, null, "levantar"));

        errores.Todos().Keys.Should().Contain(["prenda.acreedor", "prenda.nitAcreedor"]);
    }

    [Fact]
    public void SinPrenda_NoPideDatos_PeroNoSePuedeEnUnTipoQueExigeLaPrenda()
    {
        var (prenda, errores) = ValidarPrenda(ConPrenda(), new PrendaRequest(true, "Banco", "900123456", "sin_prenda"));
        errores.Hay.Should().BeFalse();
        prenda.Should().Be(new PrendaDatos(false, null, null, "sin_prenda"));

        ValidarPrenda(ConPrenda(exige: true), new PrendaRequest(false, null, null, "sin_prenda")).Errores.Todos().Should().ContainKey("prenda.decision");
    }

    [Fact]
    public void UnaDecisionDesconocida_Falla_YSinDecisionSeConservaElComportamientoAnterior()
    {
        ValidarPrenda(ConPrenda(), new PrendaRequest(true, "Banco", "900123456", "omitir")).Errores.Todos().Should().ContainKey("prenda.decision");

        var (prenda, errores) = ValidarPrenda(ConPrenda(), new PrendaRequest(true, "Banco", "900123456"));
        errores.Hay.Should().BeFalse();
        prenda.Should().Be(new PrendaDatos(true, "Banco", "900123456"));
        prenda!.Decision.Should().BeNull();
    }

    [Theory]
    [InlineData("registrar", "prenda_registro")]
    [InlineData("levantar", "prenda_levantamiento")]
    [InlineData("sin_prenda", null)]
    [InlineData(null, null)]
    public void CadaDecisionPideSuDocumento(string? decision, string? documento) =>
        ReglasBorrador.DocumentoDePrenda(decision).Should().Be(documento);

    [Fact]
    public void ConDecision_ElBorradorPideElDocumentoDeLaDecision()
    {
        var def = ConPrenda() with { Documentos = [Doc("prenda_levantamiento", false)] };

        Pendientes(def, prenda: new PrendaDatos(true, "Banco", "900123456", "levantar")).Should().Equal(new PendienteDto("documento_requerido", "prenda_levantamiento", "documentos"));
        Pendientes(def, docs: ["prenda_levantamiento"], prenda: new PrendaDatos(true, "Banco", "900123456", "levantar")).Should().BeEmpty();
        Pendientes(def, prenda: new PrendaDatos(true, "Banco", "900123456")).Should().BeEmpty("sin decisión no hay documento: comportamiento anterior");
    }

    private static DocumentoRequeridoDto Doc(string code, bool obligatorio) => new(code, code, obligatorio, 1, string.Empty, string.Empty);

    [Fact]
    public void ElDocumentoDeLaDecision_SoloSePideSiLaMatrizDelTipoLoTrae()
    {
        var prenda = new PrendaDatos(true, "Banco", "900123456", "levantar");

        // Tipo de acción única: su matriz trae paz_salvo_prenda, no prenda_levantamiento.
        var levantamiento = ConPrenda(exige: true) with { Documentos = [Doc("paz_salvo_prenda", true)] };
        Pendientes(levantamiento, prenda: prenda).Should().Equal(new PendienteDto("documento_requerido", "paz_salvo_prenda", "documentos"));
        Pendientes(levantamiento, docs: ["paz_salvo_prenda"], prenda: prenda).Should().BeEmpty();

        // Matriz con el documento de la decisión (cualquier mayúscula): sigue el pendiente, sin duplicarse.
        var conDoc = ConPrenda() with { Documentos = [Doc("PRENDA_LEVANTAMIENTO", false)] };
        Pendientes(conDoc, prenda: prenda).Should().Equal(new PendienteDto("documento_requerido", "prenda_levantamiento", "documentos"));
        var obligatorio = ConPrenda() with { Documentos = [Doc("prenda_levantamiento", true)] };
        Pendientes(obligatorio, prenda: prenda).Should().ContainSingle();
    }

    [Fact]
    public void PrendaDatos_ConDecision_SeSerializaYLeeConSuDecision()
    {
        var json = new DatosSolicitud(null, new PrendaDatos(true, "Banco", "900123456", "levantar")).Serializar();

        json.Should().Contain("\"decision\":\"levantar\"");
        DatosSolicitud.Leer(json).Prenda!.Decision.Should().Be("levantar");
        DatosSolicitud.Leer("{\"prenda\":{\"tiene\":true,\"acreedor\":\"B\",\"nitAcreedor\":\"1\"}}").Prenda!.Decision.Should().BeNull();
    }

    [Fact]
    public void ElFlagAccionPrenda_ViajaEnLaDefinicionYElSnapshot_YUnSnapshotAnteriorLoLeeNulo()
    {
        var def = DefinicionesDePrueba.UnTitular() with { Flags = new FlagsTipoDto("PLATE", false, true, false, false, true, false, AccionPrenda: "levantar") };

        var json = DefinicionesService.Serializar(def);

        json.Should().Contain("\"accionPrenda\":\"levantar\"");
        DefinicionesService.Leer(json)!.Flags.AccionPrenda.Should().Be("levantar");
        DefinicionesService.Leer(json.Replace(",\"accionPrenda\":\"levantar\"", string.Empty, StringComparison.Ordinal))!.Flags.AccionPrenda.Should().BeNull();
    }

    // ── Prenda de acción única: entidad del levantamiento ───────────────────────────────────────────────────────────

    private static DefinicionTipoDto AccionUnica(string accion) => DefinicionesDePrueba.UnTitular(accion == "levantar" ? "LEVANTAMIENTO_PRENDA" : "PRENDA_INSCRIPCION", "au") with
    {
        Pasos = [new PasoDto("prenda", "Prenda", ["prenda_decision"], []), new PasoDto("documentos", "Documentos", ["document_checklist"], [])],
        Documentos = [new DocumentoRequeridoDto(accion == "levantar" ? "prenda_levantamiento" : "prenda_registro", "Documento de la decisión", true, 1, string.Empty, string.Empty)],
        Flags = new FlagsTipoDto("PLATE", false, true, false, false, true, false, AccionPrenda: accion),
    };

    [Fact]
    public void Levantar_deAccionUnica_SinEntidad_PidePendiente_YConEntidadCompleta()
    {
        var def = AccionUnica("levantar");
        var sin = new PrendaDatos(true, "Banco Uno", "900123456", "levantar");
        var con = sin with { EntidadLevantamiento = "Secretaría de Tránsito" };

        Pendientes(def, docs: ["prenda_levantamiento"], prenda: sin).Should().ContainSingle().Which
            .Should().Be(new PendienteDto("prenda_entidad_requerida", "prenda.entidadLevantamiento", "prenda"));
        Pendientes(def, docs: ["prenda_levantamiento"], prenda: con).Should().BeEmpty();
    }

    [Fact]
    public void Levantar_deAccionUnica_ElDocumentoDeLaDecision_NoSeDuplicaComoPendiente()
    {
        var def = AccionUnica("levantar");
        var prenda = new PrendaDatos(true, "Banco Uno", "900123456", "levantar", "Tránsito");

        var pendientes = Pendientes(def, prenda: prenda);

        pendientes.Should().ContainSingle().Which.Referencia.Should().Be("prenda_levantamiento");
    }

    [Fact]
    public void Registrar_deAccionUnica_NoPideEntidad()
    {
        var def = AccionUnica("registrar");

        Pendientes(def, docs: ["prenda_registro"], prenda: new PrendaDatos(true, "Banco Uno", "900123456", "registrar")).Should().BeEmpty();
    }

    [Fact]
    public void ConPrendaNormal_LaEntidadNoSePide_NiSeGuarda()
    {
        var def = ConPrenda(exige: true);
        Pendientes(def, docs: ["tarjeta_propiedad"], prenda: new PrendaDatos(true, "Banco Uno", "900123456", "levantar")).Should().BeEmpty();

        var (prenda, errores) = ValidarPrenda(def, new PrendaRequest(true, "Banco Uno", "900123456", "levantar", "Tránsito"));
        errores.Hay.Should().BeFalse();
        prenda!.EntidadLevantamiento.Should().BeNull("solo el levantamiento de acción única la usa");
    }

    [Fact]
    public void ValidarEntidad_SeGuardaRecortada_SoloParaLevantarDeAccionUnica_YTopeDe200()
    {
        var (prenda, errores) = ValidarPrenda(AccionUnica("levantar"), new PrendaRequest(true, "Banco Uno", "900123456", null, "  Tránsito de Cali  "));
        errores.Hay.Should().BeFalse();
        prenda!.EntidadLevantamiento.Should().Be("Tránsito de Cali");

        var (ins, e2) = ValidarPrenda(AccionUnica("registrar"), new PrendaRequest(true, "Banco Uno", "900123456", null, "Tránsito"));
        e2.Hay.Should().BeFalse();
        ins!.EntidadLevantamiento.Should().BeNull();

        var (_, e3) = ValidarPrenda(AccionUnica("levantar"), new PrendaRequest(true, "Banco Uno", "900123456", null, new string('x', 201)));
        e3.Hay.Should().BeTrue();
        var (ok, e4) = ValidarPrenda(AccionUnica("levantar"), new PrendaRequest(true, "Banco Uno", "900123456", null, new string('x', 200)));
        e4.Hay.Should().BeFalse();
        ok!.EntidadLevantamiento.Should().HaveLength(200);
    }

    [Fact]
    public void ElBorradorAnteriorSinEntidad_SeLeeCompatible()
    {
        DatosSolicitud.Leer("{\"prenda\":{\"tiene\":true,\"acreedor\":\"B\",\"nitAcreedor\":\"1\",\"decision\":\"levantar\"}}").Prenda!.EntidadLevantamiento.Should().BeNull();
        DatosSolicitud.Leer(new DatosSolicitud(null, new PrendaDatos(true, "B", "1", "levantar", "Tránsito")).Serializar()).Prenda!.EntidadLevantamiento.Should().Be("Tránsito");
    }

    // ── HU 6 y 10: catálogos y destinos ──────────────────────────────────────────────────────────────────────────────

    private sealed class CatalogosFalsos : IFachadaCatalogosVehiculo
    {
        public int Llamadas;
        public (string Catalogo, string? Texto, string? Clase, int Limite) Ultima;
        public List<OrganismoDestinoDto> Organismos { get; } = [];

        public Task<IReadOnlyList<ItemCatalogoDto>> CatalogoAsync(string catalogo, string? texto, string? clase, int limite, CancellationToken ct)
        {
            Llamadas++;
            Ultima = (catalogo, texto, clase, limite);
            return Task.FromResult<IReadOnlyList<ItemCatalogoDto>>([new ItemCatalogoDto("ROJO", "ROJO")]);
        }

        public Task<IReadOnlyList<OrganismoDestinoDto>> DestinosAsync(string? texto, string? id, int limite, CancellationToken ct)
        {
            Ultima = ("destinos", texto, id, limite);
            return Task.FromResult<IReadOnlyList<OrganismoDestinoDto>>(
                [.. Organismos.Where(o => id is null ? true : o.Id == id)]);
        }
    }

    private static CatalogosVehiculoService Servicio(CatalogosFalsos falsos) =>
        new(falsos, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Catalogos_SinTexto_SeCachean_YConTextoVanAlOrigenAcotados()
    {
        var falsos = new CatalogosFalsos();
        var servicio = Servicio(falsos);

        await servicio.CatalogoAsync("colores", null, null, CancellationToken.None);
        await servicio.CatalogoAsync("colores", " ", null, CancellationToken.None);
        falsos.Llamadas.Should().Be(1, "la consulta inicial sin texto se repite al abrir el selector");

        await servicio.CatalogoAsync("carrocerias", new string('x', 200), "AUTOMOVIL", CancellationToken.None);
        falsos.Llamadas.Should().Be(2);
        falsos.Ultima.Texto!.Length.Should().Be(CatalogosVehiculoService.MaxTexto);
        falsos.Ultima.Clase.Should().Be("AUTOMOVIL");
        falsos.Ultima.Limite.Should().Be(CatalogosVehiculoService.LimiteCatalogo);
    }

    [Fact]
    public async Task Destinos_AcotanElLimite_YValidanUnDestinoPorSuId()
    {
        var falsos = new CatalogosFalsos();
        var existente = Guid.CreateVersion7();
        falsos.Organismos.Add(new OrganismoDestinoDto(existente.ToString(), "Tránsito de Prueba", "Bogotá", "Cundinamarca"));
        var servicio = Servicio(falsos);

        await servicio.DestinosAsync("trans", 9999, CancellationToken.None);
        falsos.Ultima.Limite.Should().Be(CatalogosVehiculoService.LimiteMaximoDestinos);
        await servicio.DestinosAsync(null, null, CancellationToken.None);
        falsos.Ultima.Limite.Should().Be(CatalogosVehiculoService.LimiteDestinos);

        (await servicio.DestinoValidoAsync(existente, CancellationToken.None)).Should().BeTrue();
        (await servicio.DestinoValidoAsync(Guid.CreateVersion7(), CancellationToken.None)).Should().BeFalse();
    }

    [Theory]
    [InlineData("colores", true)]
    [InlineData("combustibles", true)]
    [InlineData("carrocerias", false)]
    [InlineData("usuarios", false)]
    [InlineData("Colores", false)]
    public void SoloExistenDosCatalogosAnonimos_LasCarroceriasVanPorLaSesionDelBorrador(string nombre, bool existe) =>
        CatalogosVehiculoService.Catalogos.Contains(nombre).Should().Be(existe);
}

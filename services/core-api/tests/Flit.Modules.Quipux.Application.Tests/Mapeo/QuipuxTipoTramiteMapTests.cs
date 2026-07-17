using Flit.Modules.Quipux.Domain.Mapeo;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.Mapeo;

public sealed class QuipuxTipoTramiteMapTests
{
    /// <summary>Traspaso bilateral: 16/TR/35, identificado por placa. Variante unilateral → 213/TRU.</summary>
    private const string ExternalRefsTraspaso = """
        {"quipux": {
          "familia": "TRASPASO",
          "tipoTramite": 16,
          "tipoRequisito": 51,
          "prefijo": "TR",
          "campoPlaca": "plate",
          "campoVin": null,
          "maxLongitudEmpresa": 35,
          "variante": {
            "campo": "es_unilateral",
            "cuandoVerdadero": {"tipoTramite": 213, "prefijo": "TRU"}
          }
        }}
        """;

    /// <summary>Matrícula inicial: 13/MI/25, identificada por VIN (aún no hay placa). Variante leasing → solo prefijo MIL.</summary>
    private const string ExternalRefsMatricula = """
        {"quipux": {
          "familia": "MATRICULA",
          "tipoTramite": 13,
          "tipoRequisito": 51,
          "prefijo": "MI",
          "campoPlaca": null,
          "campoVin": "vin",
          "maxLongitudEmpresa": 25,
          "variante": {
            "campo": "es_leasing",
            "cuandoVerdadero": {"prefijo": "MIL"}
          }
        }}
        """;

    private static Dictionary<string, string?> Campos(params (string Clave, string? Valor)[] pares)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (clave, valor) in pares)
        {
            dict[clave] = valor;
        }

        return dict;
    }

    // ---------------------------------------------------------------------
    // Parse — traspaso y matrícula
    // ---------------------------------------------------------------------

    // --- Familia: decide QUÉ bandera de la secretaría manda ---------------------------------
    // Es lo que permite que una secretaría esté integrada para traspasos y no para matrículas,
    // igual que las tres columnas id_parinttrasec_* de FLIT 1.0.

    [Fact]
    public void Parse_Traspaso_DeclaraFamiliaTraspaso()
    {
        QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!.Familia.Should().Be(QuipuxFamilia.Traspaso);
    }

    [Fact]
    public void Parse_Matricula_DeclaraFamiliaMatricula()
    {
        QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula)!.Familia.Should().Be(QuipuxFamilia.Matricula);
    }

    [Theory]
    [InlineData("VEHICULAR")]  // el valor roto que hay hoy en procedure_types.family
    [InlineData("matricula")]  // sensible a mayúsculas: el vocabulario es cerrado
    [InlineData("")]
    public void Parse_FamiliaDesconocidaOAusente_NoEsElegible(string familia)
    {
        var json =
            """
            {"quipux": {"familia": "FAM", "tipoTramite": 16, "tipoRequisito": 51,
                        "prefijo": "TR", "campoPlaca": "plate", "maxLongitudEmpresa": 35}}
            """.Replace("FAM", familia, StringComparison.Ordinal);

        QuipuxTipoTramiteMap.Parse(json).Should().BeNull(
            "sin familia válida no hay bandera de secretaría que consultar, y radicar sin saber si está integrada es peor que no radicar");
    }

    [Fact]
    public void Resolve_VarianteAplicada_NoCambiaLaFamilia()
    {
        // Un traspaso unilateral sigue siendo un traspaso: manda la misma bandera del organismo.
        var resuelto = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!
            .Resolve(Campos(("es_unilateral", "true")));

        resuelto.Familia.Should().Be(QuipuxFamilia.Traspaso);
        resuelto.TipoTramite.Should().Be(213);
    }

    [Fact]
    public void Parse_ExternalRefsDeTraspaso_LeeCodigosPrefijoYTope()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso);

        mapa.Should().NotBeNull();
        mapa!.TipoTramite.Should().Be(16);
        mapa.TipoRequisito.Should().Be(51);
        mapa.Prefijo.Should().Be("TR");
        mapa.MaxLongitudEmpresa.Should().Be(35);
        mapa.CampoPlaca.Should().Be("plate");
        mapa.CampoVin.Should().BeNull();
    }

    [Fact]
    public void Parse_ExternalRefsDeMatricula_LeeCodigosPrefijoYTope()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula);

        mapa.Should().NotBeNull();
        mapa!.TipoTramite.Should().Be(13);
        mapa.Prefijo.Should().Be("MI");
        mapa.MaxLongitudEmpresa.Should().Be(25);
        mapa.CampoPlaca.Should().BeNull();
        mapa.CampoVin.Should().Be("vin");
    }

    [Fact]
    public void Resolve_TraspasoSinVariante_UsaPlacaComoIdentificador()
    {
        var resuelto = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!.Resolve(Campos());

        resuelto.UsaPlaca.Should().BeTrue();
        resuelto.UsaVin.Should().BeFalse();
        resuelto.CampoPlaca.Should().Be("plate");
    }

    [Fact]
    public void Resolve_MatriculaSinVariante_UsaVinComoIdentificador()
    {
        var resuelto = QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula)!.Resolve(Campos());

        resuelto.UsaVin.Should().BeTrue();
        resuelto.UsaPlaca.Should().BeFalse();
        resuelto.CampoVin.Should().Be("vin");
    }

    // ---------------------------------------------------------------------
    // Resolve — variante que cambia código Y prefijo (traspaso unilateral)
    // ---------------------------------------------------------------------

    [Fact]
    public void Resolve_EsUnilateralVerdadero_CambiaA213YPrefijoTru()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!;

        var resuelto = mapa.Resolve(Campos(("es_unilateral", "true")));

        resuelto.TipoTramite.Should().Be(213);
        resuelto.Prefijo.Should().Be("TRU");
        resuelto.VarianteAplicada.Should().BeTrue();
        resuelto.TipoRequisito.Should().Be(51);      // no lo redefine la variante: hereda
        resuelto.MaxLongitudEmpresa.Should().Be(35); // idem
    }

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_EsUnilateralFalso_SigueEn16YPrefijoTr(string? valor)
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!;

        var resuelto = mapa.Resolve(Campos(("es_unilateral", valor)));

        resuelto.TipoTramite.Should().Be(16);
        resuelto.Prefijo.Should().Be("TR");
        resuelto.VarianteAplicada.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EsUnilateralAusente_SigueEn16YPrefijoTr()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!;

        var resuelto = mapa.Resolve(Campos(("plate", "ABC123")));

        resuelto.TipoTramite.Should().Be(16);
        resuelto.Prefijo.Should().Be("TR");
        resuelto.VarianteAplicada.Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Resolve — variante que cambia SOLO el prefijo (matrícula en leasing)
    // ---------------------------------------------------------------------

    [Fact]
    public void Resolve_EsLeasingVerdadero_CambiaPrefijoAMilPeroTipoTramiteSigue13()
    {
        // El delta solo redefine el prefijo. Todo lo que no menciona se HEREDA del bloque base:
        // la matrícula en leasing sigue siendo el trámite 13 ante la secretaría.
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula)!;

        var resuelto = mapa.Resolve(Campos(("es_leasing", "true")));

        resuelto.Prefijo.Should().Be("MIL");
        resuelto.TipoTramite.Should().Be(13);
        resuelto.TipoRequisito.Should().Be(51);
        resuelto.MaxLongitudEmpresa.Should().Be(25);
        resuelto.VarianteAplicada.Should().BeTrue();
    }

    [Fact]
    public void Resolve_EsLeasingFalso_MantienePrefijoMi()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula)!;

        var resuelto = mapa.Resolve(Campos(("es_leasing", "false")));

        resuelto.Prefijo.Should().Be("MI");
        resuelto.TipoTramite.Should().Be(13);
        resuelto.VarianteAplicada.Should().BeFalse();
    }

    [Fact]
    public void Resolve_VarianteAplicada_NoAlteraElIdentificadorDelVehiculo()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula)!;

        var resuelto = mapa.Resolve(Campos(("es_leasing", "true")));

        resuelto.CampoVin.Should().Be("vin");
        resuelto.CampoPlaca.Should().BeNull();
        resuelto.UsaVin.Should().BeTrue();
    }

    [Fact]
    public void Resolve_SinVarianteParametrizada_DevuelveElBloqueBase()
    {
        var mapa = QuipuxTipoTramiteMap.Parse("""
            {"quipux": {"familia": "TRASPASO", "tipoTramite": 16, "tipoRequisito": 51, "prefijo": "TR",
                        "campoPlaca": "plate", "maxLongitudEmpresa": 35}}
            """)!;

        var resuelto = mapa.Resolve(Campos(("es_unilateral", "true")));

        resuelto.TipoTramite.Should().Be(16);
        resuelto.Prefijo.Should().Be("TR");
        resuelto.VarianteAplicada.Should().BeFalse();
    }

    [Fact]
    public void Resolve_FieldValuesNulo_Lanza()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!;

        var act = () => mapa.Resolve(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------------
    // Parse — gate de elegibilidad: sin bloque quipux, no se radica
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"rues": {"codigo": "X"}}""")]                 // otras claves, ninguna quipux
    [InlineData("""{"quipux": null}""")]                          // clave presente pero nula
    [InlineData("""{"quipux": "si"}""")]                          // clave presente pero no es objeto
    [InlineData("""{"quipux": []}""")]
    [InlineData("[]")]                                            // raíz que no es objeto
    [InlineData("\"texto\"")]
    public void Parse_SinBloqueQuipuxUtilizable_DevuelveNull(string json)
    {
        // La ausencia del bloque ES el gate: ese tipo de trámite no va a Quipux.
        QuipuxTipoTramiteMap.Parse(json).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_JsonNuloOVacio_DevuelveNull(string? json)
    {
        QuipuxTipoTramiteMap.Parse(json).Should().BeNull();
    }

    [Theory]
    [InlineData("no soy json")]
    [InlineData("{")]
    [InlineData("""{"quipux": {"tipoTramite": 16,}}""")]  // coma colgante
    [InlineData("""{"quipux": {"tipoTramite": "dieciséis"}}""")] // tipo incorrecto
    public void Parse_JsonInvalido_DevuelveNullSinLanzar(string json)
    {
        var act = () => QuipuxTipoTramiteMap.Parse(json);

        act.Should().NotThrow();
        QuipuxTipoTramiteMap.Parse(json).Should().BeNull();
    }

    [Theory]
    // Un bloque a medio llenar es NO elegible: radicar con tipoTramite=0 en una secretaría
    // cuesta más que no radicar, que además queda visible como pendiente.
    [InlineData("""{"quipux": {"familia": "TRASPASO", "tipoRequisito": 51, "prefijo": "TR", "maxLongitudEmpresa": 35}}""")]        // sin tipoTramite
    [InlineData("""{"quipux": {"tipoTramite": 16, "prefijo": "TR", "maxLongitudEmpresa": 35}}""")]          // sin tipoRequisito
    [InlineData("""{"quipux": {"tipoTramite": 16, "tipoRequisito": 51, "maxLongitudEmpresa": 35}}""")]      // sin prefijo
    [InlineData("""{"quipux": {"tipoTramite": 16, "tipoRequisito": 51, "prefijo": "  ", "maxLongitudEmpresa": 35}}""")] // prefijo en blanco
    [InlineData("""{"quipux": {"tipoTramite": 16, "tipoRequisito": 51, "prefijo": "TR"}}""")]               // sin maxLongitudEmpresa
    [InlineData("""{"quipux": {"tipoTramite": 0, "tipoRequisito": 51, "prefijo": "TR", "maxLongitudEmpresa": 35}}""")]  // tipoTramite 0
    public void Parse_BloqueIncompleto_DevuelveNull(string json)
    {
        QuipuxTipoTramiteMap.Parse(json).Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // EsVerdadero — los booleanos viven como texto en field_values
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("tRuE")]
    [InlineData("1")]
    [InlineData("si")]
    [InlineData("SI")]
    [InlineData("Si")]
    [InlineData("sí")]
    [InlineData("SÍ")]
    [InlineData("Sí")]
    [InlineData("  true  ")]
    [InlineData(" sí ")]
    public void EsVerdadero_VocabularioAfirmativo_DevuelveTrue(string valor)
    {
        // La escritura ha variado entre el portal y las cargas administrativas.
        QuipuxTipoTramiteMap.EsVerdadero(valor).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("NO")]
    [InlineData("null")]
    [InlineData("2")]
    [InlineData("s")]
    [InlineData("yes")]      // vocabulario cerrado: "yes" NO está contemplado
    [InlineData("verdadero")]
    public void EsVerdadero_CualquierOtraCosa_DevuelveFalse(string? valor)
    {
        // Ante un valor ambiguo se prefiere el trámite base, que es el caso mayoritario.
        QuipuxTipoTramiteMap.EsVerdadero(valor).Should().BeFalse();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("si")]
    [InlineData("SÍ")]
    [InlineData(" Sí ")]
    public void Resolve_FieldValueAfirmativoEnCualquierGrafia_ActivaLaVariante(string valor)
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!;

        var resuelto = mapa.Resolve(Campos(("es_unilateral", valor)));

        resuelto.TipoTramite.Should().Be(213);
        resuelto.Prefijo.Should().Be("TRU");
    }

    // ---------------------------------------------------------------------
    // Integración de las dos piezas del dominio
    // ---------------------------------------------------------------------

    [Fact]
    public void Resolve_TraspasoUnilateral_AlimentaElNombreDelDocumentoConPrefijoTru()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsTraspaso)!;
        var resuelto = mapa.Resolve(Campos(("es_unilateral", "SI"), ("plate", "ABC123")));

        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Leasing Bancolombia S.A.",
            prefijo: resuelto.Prefijo,
            momento: new DateTimeOffset(2026, 7, 15, 14, 30, 0, TimeSpan.Zero),
            sufijo: "ABC123",
            maxLongitudEmpresa: resuelto.MaxLongitudEmpresa);

        nombre.Should().Be("Leasing_Bancolombia_SA_TRU_20260715_1430_ABC123");
    }

    [Fact]
    public void Resolve_MatriculaEnLeasing_AlimentaElNombreDelDocumentoConPrefijoMilYTope25()
    {
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula)!;
        var resuelto = mapa.Resolve(Campos(("es_leasing", "true"), ("vin", "9BWZZZ377VT004251")));

        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Compañía Colombiana de Leasing y Servicios Financieros S.A.S.",
            prefijo: resuelto.Prefijo,
            momento: new DateTimeOffset(2026, 7, 15, 14, 30, 0, TimeSpan.Zero),
            sufijo: "9BWZZZ377VT004251",
            maxLongitudEmpresa: resuelto.MaxLongitudEmpresa);

        nombre.Should().Be("Compaa_Colombiana_de_Leas_MIL_20260715_1430_9BWZZZ377VT004251");
    }

    // --- Gate sobre el delta de la variante -------------------------------------------------
    // El delta usa int? y Resolve hace `delta ?? base`, así que un 0 explícito NO es null y GANA
    // sobre el base. Sin validarlo, una variante rota pasaría el gate (porque el base es válido) y
    // se radicaría con tipoTramite 0 en la secretaría. Como el diseño busca que añadir un trámite
    // sea un UPDATE de external_refs sin desplegar, este es justo el error que llega por UPDATE.

    [Theory]
    [InlineData(@"{""tipoTramite"": 0, ""prefijo"": ""TRU""}")]
    [InlineData(@"{""tipoRequisito"": 0}")]
    [InlineData(@"{""maxLongitudEmpresa"": 0}")]
    [InlineData(@"{""tipoTramite"": -1}")]
    [InlineData(@"{""prefijo"": ""  ""}")]
    public void Parse_VarianteConDeltaInvalido_NoEsElegible(string cuandoVerdadero)
    {
        var json =
            """
            {"quipux": {
              "tipoTramite": 16, "tipoRequisito": 51, "prefijo": "TR",
              "campoPlaca": "plate", "campoVin": null, "maxLongitudEmpresa": 35,
              "variante": {"campo": "es_unilateral", "cuandoVerdadero": DELTA}
            }}
            """.Replace("DELTA", cuandoVerdadero, StringComparison.Ordinal);

        QuipuxTipoTramiteMap.Parse(json).Should().BeNull(
            "una variante mal parametrizada debe dejar el tipo fuera de Quipux, no radicarse con códigos rotos");
    }

    [Fact]
    public void Parse_VarianteSinCampoActivador_NoEsElegible()
    {
        // Una variante sin campo nunca se aplicaría: es configuración muerta, y si alguien la
        // escribió es que esperaba que hiciera algo. Mejor delatarlo que ignorarlo en silencio.
        var json = """
            {"quipux": {
              "tipoTramite": 16, "tipoRequisito": 51, "prefijo": "TR",
              "campoPlaca": "plate", "campoVin": null, "maxLongitudEmpresa": 35,
              "variante": {"campo": "", "cuandoVerdadero": {"tipoTramite": 213, "prefijo": "TRU"}}
            }}
            """;

        QuipuxTipoTramiteMap.Parse(json).Should().BeNull();
    }

    [Fact]
    public void Parse_VarianteQueSoloRedefineElPrefijo_SigueSiendoElegible()
    {
        // Es el caso REAL de matrícula en leasing: MIL cambia el prefijo pero el tipoTramite sigue
        // 13. El gate no debe confundir "ausente, hereda" con "inválido".
        var mapa = QuipuxTipoTramiteMap.Parse(ExternalRefsMatricula);

        mapa.Should().NotBeNull();
        mapa!.Resolve(Campos(("es_leasing", "true"))).TipoTramite.Should().Be(13);
    }
}

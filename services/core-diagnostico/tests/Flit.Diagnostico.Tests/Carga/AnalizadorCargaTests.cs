using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Carga;

/// <summary>
/// HU #13749 (F02-H4) — el análisis local de la carga, sin HTTP: entradas demasiado largas (decisión del usuario 2026-10-21:
/// el valor limpio, antes de recortar, de más de 8 caracteres en placa o 17 en VIN es un error y nunca se acepta
/// recortado), duplicados consolidados (escenario 6), sin válidos (7), límite (8), documento enmascarado (9) y estimado
/// (10). Identificadores y documentos inventados.
/// </summary>
/// <example>
/// <code>
/// AnalizadorCarga.Analizar(new TablaCarga(encabezados, filas), new ParametrosAnalisis([Fuente.Runt], 500, MostrarEstimado: false))
/// </code>
/// </example>
public sealed class AnalizadorCargaTests
{
    private static readonly string?[] Encabezados = ["Identificador_Vehiculo", "Tipo_Documento_Locatario", "Documento_Locatario"];
    private static readonly ParametrosAnalisis Sin = new([Fuente.Runt], 500, MostrarEstimado: false);

    // Decisión 3 — AB1234567 (9 caracteres limpios) no se acepta como la maquinaria AB123456 recortada.
    [Theory]
    [InlineData("AB1234567", "AB1234567")]
    [InlineData("ab-123.4567", "AB1234567")]
    [InlineData("ABC1234", "ABC1234")]
    public void Placa_de_mas_de_8_caracteres_limpios_o_invalida_queda_erronea_sin_recortar(string identificador, string normalizado)
    {
        var resumen = AnalizadorCarga.Analizar(Tabla((identificador, null, null)), Sin);

        var fila = resumen.Filas.Should().ContainSingle().Subject;
        fila.Valida.Should().BeFalse();
        fila.IdentificadorNorm.Should().Be(normalizado);
        fila.Errores.Should().ContainSingle();
        resumen.Validos.Should().Be(0);
    }

    [Fact]
    public void AB1234567_da_error_de_demasiado_largo_con_un_mensaje_claro()
    {
        var fila = AnalizadorCarga.Analizar(Tabla(("AB1234567", null, null)), Sin).Filas.Single();

        fila.TipoIdentificador.Should().BeNull("no se clasifica una entrada recortada");
        fila.Errores.Single().Code.Should().Be(AnalizadorCarga.CodigoIdentificadorDemasiadoLargo);
        fila.Errores.Single().Campo.Should().Be(PlantillaCarga.ColumnaIdentificador);
        fila.Errores.Single().Mensaje.Should().Contain("8").And.Contain("no se recorta");
    }

    // Decisión 3 — un VIN de 18 caracteres no se acepta como el VIN de 17 recortado.
    [Theory]
    [InlineData("9FBTEST12345678901")]
    [InlineData("9fbtest1234567890 1")]
    public void VIN_de_18_caracteres_queda_erroneo_sin_recortar(string identificador)
    {
        var fila = AnalizadorCarga.Analizar(Tabla((identificador, null, null)), Sin).Filas.Single();

        fila.Valida.Should().BeFalse();
        fila.IdentificadorNorm.Should().Be("9FBTEST12345678901");
        fila.Errores.Single().Code.Should().Be(AnalizadorCarga.CodigoIdentificadorDemasiadoLargo);
        fila.Errores.Single().Mensaje.Should().Contain("17").And.Contain("no se recorta");
    }

    [Fact]
    public void Placa_y_VIN_en_su_largo_exacto_son_validos()
    {
        var resumen = AnalizadorCarga.Analizar(Tabla(("MC029554", null, null), ("9FBTEST1234567890", null, null), (" abc-123 ", null, null)), Sin);

        resumen.Filas.Select(f => (f.IdentificadorNorm, f.TipoIdentificador, f.Valida)).Should().Equal(
            ("MC029554", "PLACA_MAQUINARIA", true), ("9FBTEST1234567890", "VIN", true), ("ABC123", "PLACA_CARRO", true));
        resumen.Filas[2].Identificador.Should().Be(" abc-123 ", "el original se devuelve como llegó");
    }

    // Escenario 6 — duplicados consolidados en la primera fila; total = válidos + erróneos + duplicados.
    [Fact]
    public void Duplicados_se_consolidan_y_se_cuentan()
    {
        var resumen = AnalizadorCarga.Analizar(Tabla(("ABC123", null, null), ("NOPLACA", null, null), ("abc 123", null, null), ("ABC-123", null, null)), Sin);

        resumen.TotalFilas.Should().Be(4);
        resumen.Validos.Should().Be(1);
        resumen.Erroneos.Should().Be(1);
        resumen.Duplicados.Should().Be(2);
        resumen.Filas.Should().HaveCount(2);
        resumen.Filas[0].FilasDuplicadas.Should().Equal(4, 5);
    }

    // Escenario 7 — sin válidos: aviso y no se puede continuar; identificador vacío con documento es un error.
    [Fact]
    public void Sin_validos_avisa_y_no_permite_continuar()
    {
        var resumen = AnalizadorCarga.Analizar(Tabla((string.Empty, "CC", "1020304050"), ("XX", null, null)), Sin);

        resumen.Validos.Should().Be(0);
        resumen.PuedeContinuar.Should().BeFalse();
        resumen.Avisos.Select(a => a.Code).Should().Contain(AnalizadorCarga.CodigoSinValidos);
        resumen.Filas[0].Errores.Select(e => e.Code).Should().Contain(AnalizadorCarga.CodigoIdentificadorRequerido);
        resumen.Filas[1].Errores.Single().Code.Should().Be(AnalizadorCarga.CodigoIdentificadorInvalido);
    }

    // Escenario 8 — más válidos que el límite.
    [Fact]
    public void Mas_validos_que_el_limite_lo_informa()
    {
        var resumen = AnalizadorCarga.Analizar(Tabla(("ABC123", null, null), ("ABC124", null, null), ("ABC125", null, null)), new([Fuente.Runt], 2, false));

        resumen.SuperaLimite.Should().BeTrue();
        resumen.LimiteVehiculosLote.Should().Be(2);
        resumen.PuedeContinuar.Should().BeFalse();
        resumen.Avisos.Select(a => a.Code).Should().Equal(AnalizadorCarga.CodigoLimiteSuperado);
        resumen.Avisos[0].Mensaje.Should().Contain("2").And.Contain("3");
    }

    // Escenario 9 — documento válido enmascarado; con error, la fila es errónea y el documento no se devuelve en claro.
    [Fact]
    public void Documento_valido_se_enmascara_y_el_invalido_marca_la_fila()
    {
        var resumen = AnalizadorCarga.Analizar(Tabla(("ABC123", "CC", "1020304050"), ("XYZ987", "CC", "71A3B2"), ("DEF456", "PAS", "AB1")), Sin);

        resumen.Filas[0].DocumentoLocatario.Should().Be("****4050");
        resumen.Filas[0].TipoDocumentoLocatario.Should().Be("CC");
        resumen.Filas[1].Valida.Should().BeFalse();
        resumen.Filas[1].Errores.Single().Campo.Should().Be(PlantillaCarga.ColumnaDocumento);
        resumen.Filas[1].DocumentoLocatario.Should().Be("****A3B2");
        resumen.Filas[2].DocumentoLocatario.Should().Be("****");
        AnalizadorCarga.Enmascarar("1020304050").Should().Be("****4050");
        AnalizadorCarga.Enmascarar("1234").Should().Be("****");
    }

    // Escenario 10 — el estimado sale del alcance de la cabecera, solo si se muestra.
    [Fact]
    public void Estimado_por_fuente_solo_si_se_muestra()
    {
        var tabla = Tabla(("ABC123", "CC", "1020304050"), ("XYZ987", null, null), ("MAL", null, null));

        var con = AnalizadorCarga.Analizar(tabla, new([Fuente.Runt, Fuente.SimitLocatario], 500, MostrarEstimado: true));
        var sin = AnalizadorCarga.Analizar(tabla, new([Fuente.Runt, Fuente.SimitLocatario], 500, MostrarEstimado: false));

        con.EstimadoConsultas.Should().Equal(new EstimadoFuenteDto("RUNT", 2), new EstimadoFuenteDto("SIMIT_LOCATARIO", 1));
        sin.EstimadoConsultas.Should().BeNull();
    }

    // Encabezados sin distinguir mayúsculas ni espacios, en cualquier orden, y columnas opcionales ausentes.
    [Fact]
    public void Lee_los_encabezados_en_cualquier_orden_y_sin_las_columnas_opcionales()
    {
        var tabla = new TablaCarga([" documento_locatario ", "IDENTIFICADOR_VEHICULO"], [new FilaLeida(2, ["1020304050", "ABC123"])]);

        var fila = AnalizadorCarga.Analizar(tabla, Sin).Filas.Single();
        var soloIdentificador = AnalizadorCarga.Analizar(new TablaCarga(["Identificador_Vehiculo"], [new FilaLeida(2, ["ABC123"])]), Sin);

        fila.IdentificadorNorm.Should().Be("ABC123");
        fila.Errores.Single().Code.Should().Be("DOCUMENTO_INCOMPLETO", "el número sin tipo no es un documento completo");
        soloIdentificador.Validos.Should().Be(1);
    }

    private static TablaCarga Tabla(params (string Identificador, string? Tipo, string? Documento)[] filas) =>
        new(Encabezados, [.. filas.Select((f, i) => new FilaLeida(i + 2, [f.Identificador, f.Tipo, f.Documento]))]);
}

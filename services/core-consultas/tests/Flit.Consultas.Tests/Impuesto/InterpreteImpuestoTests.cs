using System.Text.RegularExpressions;
using FluentAssertions;
using Flit.Consultas.Api.Impuesto;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>
/// C3-2 — un caso por fixture. Nada no confirmado sale como paz y salvo, y la traza del servicio no viaja en el veredicto.
/// </summary>
public sealed class InterpreteImpuestoTests
{
    private const string Traza = "TRAZA-INTERNA-NO-COPIAR";

    public static IEnumerable<object[]> Casos() =>
    [
        F("deuda-completa", confirmado: true, codigo: "", deuda: true, monto: 615_000, parcial: false, vigencias: 1),
        F("deuda-sin-monto", true, "", true, -1, false, 0),
        F("sin-deuda-con-datos", true, "", false, -1, false, 0),
        F("sin-deuda-en-blanco", true, "", false, -1, false, 0),
        F("bandera-falsa-con-timeout", false, "TIMEOUT", false, -1, false, 0),
        F("error-con-monto", false, "NO_CONFIRMADO", true, 1_000_000, true, 0),
        F("documento-no-coincide", false, "DOCUMENTO_NO_COINCIDE", false, -1, false, 0),
        F("vehiculo-no-encontrado", false, "VEHICULO_NO_ENCONTRADO", false, -1, false, 0),
        F("vacia", false, "NO_CONFIRMADO", false, -1, false, 0),
        F("corta-sin-typeerror", false, "NO_CONFIRMADO", false, -1, false, 0),
        F("deuda-solo-total", true, "", true, 1_234_567, false, 0),
        F("corta-con-error", false, "HTTP_5XX", false, -1, false, 0),
        F("extendida-19-llaves", true, "", false, -1, false, 0),
        F("sin-detalle-15-llaves", false, "VEHICULO_NO_CONSULTABLE", false, -1, false, 0),
        F("requiere-actualizacion", false, "REQUIERE_ACTUALIZACION", false, -1, false, 0),
        F("vehiculo-cancelado", false, "VEHICULO_NO_CONSULTABLE", false, -1, false, 0),
        F("credenciales-faltantes", false, "AUTENTICACION", false, -1, false, 0),
        F("error-seleccionando-modelo", false, "MODELO_NO_ACEPTADO", false, -1, false, 0),
        F("documento-no-coincide-en-validity", false, "DOCUMENTO_NO_COINCIDE", false, -1, false, 0),
        F("cuerpo-ilegible", false, "RESPUESTA_INVALIDA", false, -1, false, 0),
    ];

    [Theory]
    [MemberData(nameof(Casos))]
    public void El_fixture_da_el_veredicto_esperado(string archivo, bool confirmado, string codigo, bool deuda, long monto, bool parcial, int vigencias)
    {
        string? codigoEsperado = codigo.Length == 0 ? null : codigo;
        long? montoEsperado = monto < 0 ? null : monto;
        var texto = File.ReadAllText(Ruta(archivo));
        var veredicto = InterpreteImpuesto.Interpretar(texto);
        veredicto.Confirmado.Should().Be(confirmado);
        veredicto.Codigo.Should().Be(codigoEsperado);
        veredicto.TieneDeuda.Should().Be(deuda);
        veredicto.MontoTotal.Should().Be(montoEsperado);
        veredicto.DatoParcial.Should().Be(parcial);
        veredicto.Vigencias.Should().HaveCount(vigencias);
        veredicto.ToString().Should().NotContain(Traza);
        if (confirmado && !deuda)
            veredicto.MontoInformado.Should().BeFalse();
        if (deuda && confirmado)
            veredicto.MontoInformado.Should().Be(montoEsperado is not null);
    }

    [Fact]
    public void La_deuda_completa_conserva_vigencia_pago_y_liquidacion()
    {
        var veredicto = InterpreteImpuesto.Interpretar(File.ReadAllText(Ruta("deuda-completa")));
        veredicto.Vigencias.Should().ContainSingle();
        veredicto.Vigencias[0].Anio.Should().Be(2025);
        veredicto.Vigencias[0].Valor.Should().Be(615_000);
        veredicto.Vigencias[0].LiquidacionId.Should().Be("liq-vigencia-tst");
        veredicto.InteresesMora.Should().Be(5_000);
        veredicto.Sanciones.Should().Be(10_000);
        veredicto.AvaluoComercial.Should().Be(20_000_000);
        veredicto.LiquidacionConsolidadaId.Should().Be("liq-consolidada-tst");
        veredicto.PeriodoCertificacion.Should().Be("2026");
        veredicto.FechaConsulta.Should().Be("2026-02-04");
        veredicto.UltimoPagoAnio.Should().Be(2024);
        veredicto.UltimoPagoValor.Should().Be(100_000);
        veredicto.UltimoPagoFecha.Should().Be("2025-02-09");
    }

    [Fact]
    public void Sin_deuda_no_inventa_ceros()
    {
        var veredicto = InterpreteImpuesto.Interpretar(File.ReadAllText(Ruta("sin-deuda-en-blanco")));
        veredicto.Confirmado.Should().BeTrue();
        veredicto.TieneDeuda.Should().BeFalse();
        veredicto.MontoTotal.Should().BeNull();
        veredicto.InteresesMora.Should().BeNull();
        veredicto.Sanciones.Should().BeNull();
        veredicto.AvaluoComercial.Should().BeNull();
        veredicto.FechaConsulta.Should().BeNull();
        veredicto.UltimoPagoAnio.Should().BeNull();
    }

    [Fact]
    public void Validity_se_lee_igual_que_validaty()
    {
        const string json = """
            {
              "pendingDebtTaxes": true,
              "pendingTotalToPay": "1000",
              "pendingDetailTax": [ { "validity": "2023", "total": "1000" } ],
              "observationError": { "typeError": 0, "descriptionError": "", "descriptionErrorDevelopment": "" }
            }
            """;
        var veredicto = InterpreteImpuesto.Interpretar(json);
        veredicto.Confirmado.Should().BeTrue();
        veredicto.Vigencias.Should().ContainSingle().Which.Anio.Should().Be(2023);
    }

    [Fact]
    public void Total_distinto_de_la_suma_no_informa_monto_y_conserva_vigencias()
    {
        const string json = """
            {
              "pendingDebtTaxes": true,
              "pendingTotalToPay": "$ 2.000",
              "pendingDetailTax": [ { "validaty": "2022", "total": "$ 1.000" } ],
              "observationError": { "typeError": 0, "descriptionError": "", "descriptionErrorDevelopment": "" }
            }
            """;
        var veredicto = InterpreteImpuesto.Interpretar(json);
        veredicto.Confirmado.Should().BeTrue();
        veredicto.TieneDeuda.Should().BeTrue();
        veredicto.MontoTotal.Should().BeNull();
        veredicto.MontoInformado.Should().BeFalse();
        veredicto.Vigencias.Should().ContainSingle().Which.Anio.Should().Be(2022);
    }

    [Fact]
    public void Una_vigencia_ilegible_se_descarta_y_queda_el_total()
    {
        const string json = """
            {
              "pendingDebtTaxes": true,
              "pendingTotalToPay": "$ 5.000",
              "pendingDetailTax": [ { "validaty": "no-es-anio", "total": "$ 5.000" } ],
              "observationError": { "typeError": 0, "descriptionError": "", "descriptionErrorDevelopment": "" }
            }
            """;
        var veredicto = InterpreteImpuesto.Interpretar(json);
        veredicto.Vigencias.Should().BeEmpty();
        veredicto.MontoTotal.Should().Be(5_000);
    }

    [Fact]
    public void La_raiz_vacia_no_cuenta_como_typeError()
    {
        const string json = """
            { "pendingDebtTaxes": false, "typeError": 0, "descriptionError": "", "descriptionErrorDevelopment": "" }
            """;
        var veredicto = InterpreteImpuesto.Interpretar(json);
        veredicto.Confirmado.Should().BeFalse();
        veredicto.Codigo.Should().Be("NO_CONFIRMADO");
    }

    [Fact]
    public void Los_fixtures_solo_usan_placas_de_prueba()
    {
        var dir = Path.GetDirectoryName(Ruta("vacia"))!;
        foreach (var archivo in Directory.GetFiles(dir, "proveedor-impuesto-*"))
        {
            var texto = File.ReadAllText(archivo);
            foreach (Match placa in Regex.Matches(texto, @"\b[A-Z]{3}\d{2,3}\b"))
                placa.Value.Should().StartWith("TST", archivo);
        }
    }

    private static object[] F(string nombre, bool confirmado, string codigo, bool deuda, long monto, bool parcial, int vigencias) =>
        [$"proveedor-impuesto-{nombre}.json", confirmado, codigo, deuda, monto, parcial, vigencias];

    private static string Ruta(string archivo)
    {
        if (!archivo.EndsWith(".json", StringComparison.Ordinal))
            archivo = $"proveedor-impuesto-{archivo}.json";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(dir.FullName, "src", "Flit.Consultas.Api", "Simulacion", "Fixtures", archivo);
            if (File.Exists(candidato))
                return candidato;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(archivo);
    }
}

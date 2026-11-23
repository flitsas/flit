using System.Text.RegularExpressions;
using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Api.Simulacion;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>
/// Diseño §2.5 y D6 — intérprete del cuerpo del <c>GET /consultas/{id}</c> en estado terminal. Un caso por fixture sintético
/// (<c>Simulacion/Fixtures/impuesto-v2</c>). Nada sin confirmar sale como paz y salvo, <c>error</c>/<c>failed</c> nunca es «sin
/// deuda», y ni <c>sent</c>, ni <c>portal</c>, ni <c>message</c> viajan en el veredicto.
/// <para>Uso de ejemplo: <c>InterpreteImpuesto.Interpretar(FixturesImpuesto.Texto("resultado-con-deuda.json"))</c>.</para>
/// </summary>
public sealed class InterpreteImpuestoTests
{
    public static TheoryData<string, bool, string, bool, long, bool, int> Casos => new()
    {
        // archivo, confirmado, código, deuda, monto (-1 = sin monto), dato parcial, vigencias
        { "resultado-con-deuda.json", true, "", true, 615_000, false, 2 },
        { "resultado-con-deuda-sin-total.json", true, "", true, -1, false, 1 },
        { "resultado-paz-y-salvo.json", true, "", false, -1, false, 0 },
        { "resultado-parcial-con-deuda.json", true, "", true, 219_501, true, 1 },
        { "resultado-parcial-sin-deuda.json", false, "NO_CONFIRMADO", false, -1, true, 0 },
        { "resultado-mismatch-ownername.json", true, "", false, -1, false, 0 },
        { "resultado-mismatch-vehiclemodel.json", true, "", true, 100_000, false, 1 },
        { "resultado-fallido.json", false, "NO_CONFIRMADO", false, -1, false, 0 },
        { "resultado-error.json", false, "NO_CONFIRMADO", false, -1, false, 0 },
        { "resultado-mismatch-servicio-con-avisos.json", true, "", true, 155_000, false, 1 },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void Cada_fixture_tiene_su_veredicto(string archivo, bool confirmado, string codigo, bool deuda, long monto, bool parcial, int vigencias)
    {
        var veredicto = InterpreteImpuesto.Interpretar(FixturesImpuesto.Texto(archivo));
        veredicto.Confirmado.Should().Be(confirmado, archivo);
        (veredicto.Codigo ?? "").Should().Be(codigo, archivo);
        veredicto.TieneDeuda.Should().Be(deuda, archivo);
        veredicto.MontoTotal.Should().Be(monto < 0 ? null : monto, archivo);
        veredicto.MontoInformado.Should().Be(confirmado && monto >= 0, archivo);
        veredicto.DatoParcial.Should().Be(parcial, archivo);
        veredicto.Vigencias.Should().HaveCount(vigencias, archivo);
    }

    [Fact]
    public void Con_deuda_lee_vigencias_totales_ultimo_pago_y_la_consolidada()
    {
        var v = InterpreteImpuesto.Interpretar(FixturesImpuesto.Texto("resultado-con-deuda.json"));
        v.Vigencias.Select(x => (x.Anio, x.Valor, x.Pagada, x.LiquidacionId, x.Avaluo, x.ReferenciaPago, x.FechaVencimiento)).Should().Equal(
            (2025, 375_000L, false, "archivo-sintetico-2025", (long?)30_000_000, "REF-SINTETICA-2025", "2025-07-31"),
            (2026, 240_000L, false, "archivo-sintetico-2026", (long?)28_000_000, "REF-SINTETICA-2026", "2026-07-31"));
        (v.InteresesMora, v.Sanciones, v.AvaluoComercial).Should().Be((55_000L, 60_000L, 28_000_000L));
        (v.UltimoPagoAnio, v.UltimoPagoValor, v.UltimoPagoFecha).Should().Be((2024, 280_000L, "2024-06-30"));
        v.LiquidacionConsolidadaId.Should().Be("archivo-sintetico-consolidado");
        (v.PeriodoCertificacion, v.FechaConsulta).Should().Be((null, null));
        v.Advertencias.Should().BeEmpty("match y not_available se ignoran");
    }

    [Fact]
    public void Un_mismatch_de_otro_campo_y_los_avisos_son_advertencias_sin_el_texto_del_proveedor()
    {
        var v = InterpreteImpuesto.Interpretar(FixturesImpuesto.Texto("resultado-mismatch-servicio-con-avisos.json"));
        v.Advertencias.Should().Equal(
            new AdvertenciaLeida(TipoAdvertenciaLeida.Validacion, "vehicleServiceType", "", null),
            new AdvertenciaLeida(TipoAdvertenciaLeida.Validacion, "otro", "", null),
            new AdvertenciaLeida(TipoAdvertenciaLeida.Aviso, "", "periodo_en_cobro_coactivo", 2026),
            new AdvertenciaLeida(TipoAdvertenciaLeida.Aviso, "", "otro", null));
        v.ToString().Should().NotContain("PUBLICO").And.NotContain("PARTICULAR").And.NotContain("texto sintetico");
    }

    [Fact]
    public void El_mismatch_de_ownername_no_bloquea_y_queda_como_advertencia()
    {
        var v = InterpreteImpuesto.Interpretar(FixturesImpuesto.Texto("resultado-mismatch-ownername.json"));
        v.Confirmado.Should().BeTrue();
        v.Advertencias.Should().ContainSingle().Which.Should().Be(new AdvertenciaLeida(TipoAdvertenciaLeida.Validacion, "OwnerName", "", null));
        v.ToString().Should().NotContain("PERSONA PRUEBA");
    }

    [Fact]
    public void Paz_y_salvo_con_mismatch_de_ownername_confirma_sin_deuda_y_conserva_la_advertencia()
    {
        const string json = """
            {"status":"succeeded","result":{"status":"paz_y_salvo","totals":{"totalToPay":0},"pendingPeriods":[],
             "validations":[{"field":"OwnerName","sent":"PERSONA PRUEBA UNO","portal":"PERSONA PRUEBA DOS","result":"mismatch"}],
             "warnings":[]}}
            """;
        var v = InterpreteImpuesto.Interpretar(json);
        (v.Confirmado, v.Codigo, v.TieneDeuda, v.MontoTotal, v.MontoInformado).Should().Be((true, (string?)null, false, (long?)null, false));
        v.Vigencias.Should().BeEmpty();
        v.Advertencias.Should().Equal(new AdvertenciaLeida(TipoAdvertenciaLeida.Validacion, "OwnerName", "", null));
        v.ToString().Should().NotContain("PERSONA PRUEBA");
    }

    [Fact]
    public void Con_deuda_con_mismatch_de_vehiclemodel_confirma_la_deuda_y_conserva_la_advertencia()
    {
        var v = InterpreteImpuesto.Interpretar(FixturesImpuesto.Texto("resultado-mismatch-vehiclemodel.json"));
        (v.Confirmado, v.TieneDeuda, v.MontoTotal).Should().Be((true, true, (long?)100_000));
        v.Advertencias.Should().Equal(new AdvertenciaLeida(TipoAdvertenciaLeida.Validacion, "vehicleModel", "", null));
    }

    [Fact]
    public void Un_error_de_la_fuente_conserva_sus_avisos()
    {
        var v = InterpreteImpuesto.Interpretar(FixturesImpuesto.Texto("resultado-error.json"));
        v.Advertencias.Should().ContainSingle().Which.Codigo.Should().Be("vehiculo_no_encontrado");
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("[]")]
    [InlineData("\"texto\"")]
    public void Un_cuerpo_ilegible_es_respuesta_invalida(string cuerpo) =>
        InterpreteImpuesto.Interpretar(cuerpo).Codigo.Should().Be("RESPUESTA_INVALIDA");

    [Fact]
    public void Paz_y_salvo_con_una_vigencia_pendiente_es_contradiccion_y_no_confirma()
    {
        const string json = """
            {"status":"succeeded","result":{"status":"paz_y_salvo","totals":{"totalToPay":0},
             "pendingPeriods":[{"period":2025,"totalToPay":1000}]}}
            """;
        var v = InterpreteImpuesto.Interpretar(json);
        (v.Confirmado, v.Codigo).Should().Be((false, "NO_CONFIRMADO"));
    }

    [Fact]
    public void Un_periodo_ilegible_con_deuda_se_omite_y_marca_dato_parcial()
    {
        const string json = """
            {"status":"succeeded","result":{"status":"con_deuda","totals":{"totalToPay":3000},
             "pendingPeriods":[{"period":"vigencia rara","totalToPay":1000},{"period":2026,"totalToPay":2000}]}}
            """;
        var v = InterpreteImpuesto.Interpretar(json);
        v.Confirmado.Should().BeTrue();
        v.DatoParcial.Should().BeTrue();
        v.Vigencias.Select(x => x.Anio).Should().Equal(2026);
    }

    [Fact]
    public void Una_referencia_o_un_archivo_con_caracteres_raros_no_se_copia()
    {
        const string json = """
            {"status":"succeeded","result":{"status":"con_deuda","totals":{"totalToPay":1000},
             "pendingPeriods":[{"period":2026,"totalToPay":1000,"paymentReference":"REF <script>","fileId":"../otro"}]}}
            """;
        var vigencia = InterpreteImpuesto.Interpretar(json).Vigencias.Single();
        (vigencia.ReferenciaPago, vigencia.LiquidacionId).Should().Be((null, null));
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("desconocido")]
    public void Un_estado_que_no_es_terminal_no_confirma(string estado) =>
        InterpreteImpuesto.Interpretar("{\"status\":\"" + estado + "\",\"result\":{\"status\":\"paz_y_salvo\"}}").Codigo.Should().Be("NO_CONFIRMADO");

    [Fact]
    public void Una_solicitud_rechazada_conserva_el_error_code_y_los_estados_de_la_fuente()
    {
        const string json = """
            {"id":"ext-1","status":"failed","error_code":"invalid_params","result":{"status":"error"}}
            """;
        var v = InterpreteImpuesto.Interpretar(json);
        (v.Confirmado, v.Codigo).Should().Be((false, "SOLICITUD_INVALIDA"));
        (v.EstadoFuente, v.EstadoResultadoFuente, v.CodigoErrorFuente).Should().Be(("failed", "error", "invalid_params"));
    }

    [Theory]
    [InlineData("parametros con espacios")]
    [InlineData("persona@example.invalid")]
    [InlineData("INVALID_PARAMS")]
    public void Un_error_code_con_forma_invalida_queda_no_reconocido_en_el_veredicto(string codigo)
    {
        var v = InterpreteImpuesto.Interpretar("{\"status\":\"failed\",\"error_code\":\"" + codigo + "\"}");
        v.CodigoErrorFuente.Should().Be("no_reconocido");
        v.ToString().Should().NotContain(codigo);
    }

    [Fact]
    public void Los_fixtures_v2_son_sinteticos()
    {
        FixturesImpuesto.Nombres().Should().HaveCount(18);
        foreach (var nombre in FixturesImpuesto.Nombres().Where(n => n.EndsWith(".json", StringComparison.Ordinal)))
        {
            var json = FixturesImpuesto.Texto(nombre);
            foreach (var placa in Regex.Matches(json, "[A-Z]{3}[0-9]{3}").Select(m => m.Value))
                placa.Should().StartWith("TST", $"{nombre}: placa sintética");
            foreach (var valor in Regex.Matches(json, "\"(?:sent|portal)\"\\s*:\\s*\"([^\"]+)\"").Select(m => m.Groups[1].Value)
                .Where(v => v.Contains(' ', StringComparison.Ordinal)))
                valor.Should().Contain("PRUEBA", $"{nombre}: sent/portal sintético");
            foreach (var dominio in Regex.Matches(json, "@[a-z0-9.-]+").Select(m => m.Value))
                dominio.Should().Be("@example.invalid", nombre);
            Regex.Matches(json, "\\b3\\d{9}\\b").Should().BeEmpty($"{nombre} no trae celulares");
        }

        FixturesImpuesto.Bytes("liquidacion-sintetica.pdf").AsSpan(0, 5).ToArray().Should().Equal("%PDF-"u8.ToArray());
    }
}

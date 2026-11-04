using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// C3-7 — cada clave de chequeo del impuesto es su <c>tipo_error</c>; la deuda sin monto no se guarda como cero;
/// el dato parcial no arma resultado y la respuesta OK del bus se conserva cifrada.
/// </summary>
public sealed class MapeoImpuestoTests
{
    [Theory]
    [InlineData("impuesto_no_confirmado", "NO_CONFIRMADO", ClaseError.Definitivo)]
    [InlineData("impuesto_timeout", "TIMEOUT", ClaseError.Transitorio)]
    [InlineData("impuesto_http_5xx", "HTTP_5XX", ClaseError.Transitorio)]
    [InlineData("impuesto_modelo_no_aceptado", "MODELO_NO_ACEPTADO", ClaseError.Definitivo)]
    [InlineData("impuesto_documento_no_coincide", "DOCUMENTO_NO_COINCIDE", ClaseError.Definitivo)]
    [InlineData("impuesto_vehiculo_no_encontrado", "VEHICULO_NO_ENCONTRADO", ClaseError.Definitivo)]
    [InlineData("impuesto_vehiculo_no_consultable", "VEHICULO_NO_CONSULTABLE", ClaseError.Definitivo)]
    [InlineData("impuesto_requiere_actualizacion", "REQUIERE_ACTUALIZACION", ClaseError.Definitivo)]
    [InlineData("impuesto_autenticacion", "AUTENTICACION", ClaseError.Definitivo)]
    [InlineData("impuesto_respuesta_invalida", "RESPUESTA_INVALIDA", ClaseError.Definitivo)]
    [InlineData("impuesto_documento_no_homologado", "DOCUMENTO_NO_HOMOLOGADO", ClaseError.Definitivo)]
    [InlineData("impuesto_red", "RED", ClaseError.Transitorio)]
    [InlineData("impuesto_limite", "LIMITE_PROVEEDOR", ClaseError.Transitorio)]
    [InlineData("impuesto_rechazo", "ESTADO_NO_OK", ClaseError.Definitivo)]
    [InlineData("impuesto_caida", "HTTP_5XX", ClaseError.Transitorio)]
    [InlineData("impuesto_estado_no_ok", "ESTADO_NO_OK", ClaseError.Definitivo)]
    public void Cada_clave_de_chequeo_produce_su_tipo_error_y_su_clase(string clave, string tipo, ClaseError clase)
    {
        var respuesta = ConChequeo(clave);
        respuesta.DatoParcial = false;
        respuesta.MontoInformado = true;
        respuesta.MontoTotal = 99_000;
        respuesta.TieneDeuda = true;

        var mapeada = MapeoImpuesto.Mapear(respuesta, "05");

        mapeada.Resultado.Should().BeNull();
        mapeada.Error!.TipoError.Should().Be(tipo);
        mapeada.Error.Clase.Should().Be(clase);
        mapeada.Error.Mensaje.Should().Be("El impuesto no quedó confirmado.");
    }

    [Fact]
    public void Deuda_sin_monto_informado_deja_monto_total_null()
    {
        var respuesta = Confirmada(tieneDeuda: true, montoInformado: false);
        respuesta.MontoTotal = 0;

        var json = JsonSerializer.Serialize(MapeoImpuesto.Mapear(respuesta, "50").Resultado, EjecutorPorPersonas.Json);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("tiene_deuda").GetBoolean().Should().BeTrue();
        doc.RootElement.TryGetProperty("monto_total", out _).Should().BeFalse();
        json.Should().NotContain("\"monto_total\":0");
    }

    [Fact]
    public void Resultado_conserva_el_vacio_como_null()
    {
        var respuesta = Confirmada(tieneDeuda: false, montoInformado: false);
        respuesta.PeriodoCertificacion = "  ";
        respuesta.FechaConsultaProveedor = "";
        respuesta.LiquidacionConsolidadaId = " ";
        respuesta.InteresesMora = -1;
        respuesta.UltimoPago = new UltimoPagoImpuesto { Fecha = "  " };

        var resultado = MapeoImpuesto.Mapear(respuesta, "52").Resultado!;

        resultado.TieneDeuda.Should().BeFalse();
        resultado.MontoTotal.Should().BeNull();
        resultado.InteresesMora.Should().BeNull();
        resultado.Sanciones.Should().BeNull();
        resultado.AvaluoComercial.Should().BeNull();
        resultado.UltimoPago.Should().BeNull();
        resultado.LiquidacionConsolidadaId.Should().BeNull();
        resultado.PeriodoCertificacion.Should().BeNull();
        resultado.FechaConsultaProveedor.Should().BeNull();
    }

    [Fact]
    public void Error_con_dato_parcial_no_arma_monto_y_la_respuesta_ok_se_puede_cifrar()
    {
        var respuesta = ConChequeo("impuesto_no_confirmado");
        respuesta.DatoParcial = true;
        respuesta.TieneDeuda = true;
        respuesta.MontoInformado = true;
        respuesta.MontoTotal = 120_000;

        var mapeada = MapeoImpuesto.Mapear(respuesta, "05");
        mapeada.Resultado.Should().BeNull();
        mapeada.Error!.TipoError.Should().Be("NO_CONFIRMADO");

        var cruda = Google.Protobuf.JsonFormatter.Default.Format(respuesta);
        using var documento = JsonDocument.Parse(cruda);
        var data = new ResultadoConsultaDiagnostico(
            Guid.NewGuid(), Guid.NewGuid(), null, "impuesto", ResultadoConsultaDiagnostico.EstadoOk, null,
            documento.RootElement.Clone(), 10, false);
        var protector = new ProtectorDePrueba();
        var cifrada = RespuestaFuenteCelda.AlAplicar(protector, Fuente.ImpuestoDepartamental, null, null, RespuestaFuenteCelda.Cruda(data), payloadResultado: null);

        cifrada.Should().NotBeNull();
        RespuestaFuenteCelda.TryLeerVehiculo(protector, cifrada, out var leida).Should().BeTrue();
        leida!.Value.GetProperty("datoParcial").GetBoolean().Should().BeTrue();
        leida.Value.GetProperty("montoTotal").GetString().Should().NotBeNull();
    }

    private static ConsultarImpuestoVehicularResponse Confirmada(bool tieneDeuda, bool montoInformado) => new()
    {
        Resultado = new ResultadoConsulta
        {
            Proveedor = "impuesto_simulado",
            Chequeos = { new Chequeo { Clave = "impuesto", Estado = tieneDeuda ? EstadoChequeo.Fail : EstadoChequeo.Ok } },
        },
        TieneDeuda = tieneDeuda,
        MontoInformado = montoInformado,
    };

    private static ConsultarImpuestoVehicularResponse ConChequeo(string clave) => new()
    {
        Resultado = new ResultadoConsulta
        {
            Proveedor = "impuesto_simulado",
            Chequeos = { new Chequeo { Clave = clave, Estado = EstadoChequeo.Error, Etiqueta = "texto del proveedor que no se copia" } },
        },
    };

    private sealed class ProtectorDePrueba : IProtectorRespuestaFuente
    {
        public byte[] Proteger(string texto) => System.Text.Encoding.UTF8.GetBytes(texto);

        public string Desproteger(byte[] cifrado) => System.Text.Encoding.UTF8.GetString(cifrado);
    }
}

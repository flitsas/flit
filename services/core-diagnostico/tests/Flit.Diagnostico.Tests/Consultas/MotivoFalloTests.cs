using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Resolvedor de errores (solo Diagnóstico): con <c>motivoFallo</c> en <c>consultas.resultado.diagnostico</c> el proveedor caído
/// deja de ser siempre <c>HTTP_5XX</c> transitorio. CREDENCIALES → <c>AUTENTICACION</c> (definitivo, sin reintento), LIMITE →
/// <c>LIMITE_PROVEEDOR</c> (transitorio), RECHAZO → <c>ESTADO_NO_OK</c>, CAIDA → <c>HTTP_5XX</c>, RED → <c>RED</c>, TIMEOUT →
/// <c>TIMEOUT</c>. Sin <c>motivoFallo</c> (mensajes viejos) es lo de hoy. Pruebas puras, sin base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~MotivoFalloTests
/// </code>
/// </example>
public sealed class MotivoFalloTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);

    private static ConsultarMultasResponse SimitCaido()
    {
        var resultado = new ResultadoConsulta { Proveedor = "verifik_simit", Semaforo = Semaforo.Rojo };
        resultado.Chequeos.Add(new Chequeo { Clave = "provider", Estado = EstadoChequeo.Error });
        return new ConsultarMultasResponse { Resultado = resultado };
    }

    private static RespuestaConsulta Respuesta(IMessage? respuesta, string? motivo) =>
        new(null, FuentesBus.SimitVehiculo, respuesta, null, Ahora.AddSeconds(-1), Ahora, false, motivo);

    private static ErrorConsulta? ErrorSimit(string? motivo) =>
        Respuesta(SimitCaido(), motivo).Mapear<ConsultarMultasResponse, ResultadoSimit>(MapeoSimit.Mapear, MapeoSimit.Error).Error;

    [Fact]
    public void CREDENCIALES_es_AUTENTICACION_definitivo_y_no_se_reintenta()
    {
        var error = ErrorSimit("CREDENCIALES")!;

        (error.TipoError, error.EsTransitorio).Should().Be(("AUTENTICACION", false));
        new PoliticaReintentos(3).Decidir(error.TipoError, 0, Ahora).Reintentar.Should().BeFalse("una credencial vencida no se arregla sola");
        error.Mensaje.Should().NotContain("token", "el mensaje es técnico y fijo, sin el detalle del proveedor");
    }

    [Fact]
    public void LIMITE_es_LIMITE_PROVEEDOR_transitorio_y_conocido()
    {
        var error = ErrorSimit("LIMITE")!;

        (error.TipoError, error.EsTransitorio).Should().Be(("LIMITE_PROVEEDOR", true));
        ClasificadorErrores.Clasificar("LIMITE_PROVEEDOR").Should().Be(ClaseError.Transitorio);
        ClasificadorErrores.EsConocido("LIMITE_PROVEEDOR").Should().BeTrue();
        new PoliticaReintentos(1).Decidir(error.TipoError, 0, Ahora).Reintentar.Should().BeTrue();
    }

    [Theory]
    [InlineData("RECHAZO", "ESTADO_NO_OK", false)]
    [InlineData("CAIDA", "HTTP_5XX", true)]
    [InlineData("RED", "RED", true)]
    [InlineData("TIMEOUT", "TIMEOUT", true)]
    public void El_resto_de_motivos_se_traduce_a_su_tipo_error(string motivo, string tipo, bool transitorio)
    {
        var error = ErrorSimit(motivo)!;

        (error.TipoError, error.EsTransitorio).Should().Be((tipo, transitorio), motivo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OTRO_DESCONOCIDO")]
    public void Sin_motivo_o_con_uno_desconocido_es_HTTP_5XX_como_hoy(string? motivo)
    {
        var error = ErrorSimit(motivo)!;

        (error.TipoError, error.EsTransitorio).Should().Be(("HTTP_5XX", true));
        error.Should().Be(TraductorErroresConsultas.Traducir(SimitCaido().Resultado), "exactamente el error de hoy");
    }

    [Fact]
    public void El_motivo_no_convierte_en_error_una_respuesta_con_veredicto()
    {
        var resultado = new ResultadoConsulta { Proveedor = "verifik_simit", Semaforo = Semaforo.Verde };
        resultado.Chequeos.Add(new Chequeo { Clave = "multas", Estado = EstadoChequeo.Ok });

        var r = Respuesta(new ConsultarMultasResponse { Resultado = resultado }, "CREDENCIALES")
            .Mapear<ConsultarMultasResponse, ResultadoSimit>(MapeoSimit.Mapear, MapeoSimit.Error);

        r.Error.Should().BeNull();
        r.Resultado.Should().NotBeNull();
    }

    [Fact]
    public void Aplica_tambien_a_la_RUNT_de_persona()
    {
        var resultado = new ResultadoConsulta { Proveedor = "verifik_conductor" };
        resultado.Chequeos.Add(new Chequeo { Clave = "provider", Estado = EstadoChequeo.Error });
        var respuesta = new RespuestaConsulta(Guid.NewGuid(), FuentesBus.RuntPropietario, new ConsultarConductorResponse { Resultado = resultado }, null,
            Ahora, Ahora, false, "CREDENCIALES");

        var error = respuesta.Mapear<ConsultarConductorResponse, ResultadoPersonaRunt>(MapeoRuntPersona.Mapear, MapeoRuntPersona.Error).Error!;

        error.TipoError.Should().Be("AUTENTICACION");
    }

    [Fact]
    public void El_sobre_lee_motivoFallo_y_lo_pasa_a_la_respuesta_y_sin_la_clave_es_null()
    {
        var datos = JsonSerializer.SerializeToElement(new
        {
            trabajoId = Guid.NewGuid(),
            consultaId = Guid.NewGuid(),
            personaId = (Guid?)null,
            fuente = FuentesBus.SimitVehiculo,
            estado = "OK",
            grpcStatus = (string?)null,
            respuesta = JsonDocument.Parse(JsonFormatter.Default.Format(SimitCaido())).RootElement,
            duracionMs = 10,
            desdeCache = false,
            motivoFallo = "CREDENCIALES",
        });
        var conMotivo = JsonSerializer.Deserialize<ResultadoConsultaDiagnostico>(datos.GetRawText(), EventEnvelope.JsonOptions)!;

        conMotivo.MotivoFallo.Should().Be("CREDENCIALES");
        conMotivo.Leer(Ahora).MotivoFallo.Should().Be("CREDENCIALES");
        conMotivo.Leer(Ahora).Mapear<ConsultarMultasResponse, ResultadoSimit>(MapeoSimit.Mapear, MapeoSimit.Error).Error!.TipoError
            .Should().Be("AUTENTICACION");

        var sinClave = JsonSerializer.Deserialize<ResultadoConsultaDiagnostico>(
            datos.GetRawText().Replace(",\"motivoFallo\":\"CREDENCIALES\"", string.Empty, StringComparison.Ordinal), EventEnvelope.JsonOptions)!;
        sinClave.MotivoFallo.Should().BeNull("mensajes viejos, sin la clave");
        sinClave.Leer(Ahora).Mapear<ConsultarMultasResponse, ResultadoSimit>(MapeoSimit.Mapear, MapeoSimit.Error).Error!.TipoError
            .Should().Be("HTTP_5XX");
    }
}

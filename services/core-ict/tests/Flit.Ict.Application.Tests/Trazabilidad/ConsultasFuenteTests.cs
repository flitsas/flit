using System.Text.Json;
using Flit.Ict.Domain.Trazabilidad;
using Flit.Ict.Infrastructure.Logging;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Trazabilidad;

/// <summary>
/// HU #11817 — vocabulario de las consultas a fuentes y regla de bloqueo.
/// </summary>
/// <remarks>
/// Los códigos son los reales de FLIT 1.0, tomados de
/// <c>public.external_integration_source_query</c>: nivel MAIN, LERE y VEHI; tipo DRIVER, VEHICLE,
/// VIDEN y VIN.
/// </remarks>
public sealed class ConsultasFuenteTests
{
    [Theory]
    [InlineData("MAIN", "Principal")]
    [InlineData("LERE", "Representante legal")]
    [InlineData("VEHI", "Vehículo")]
    [InlineData("main", "Principal")]
    public void El_nivel_del_actor_se_traduce_a_lenguaje_de_usuario(string codigo, string esperado)
    {
        // Se traduce en el servidor para que la API, la pantalla y la exportación digan lo mismo.
        EtiquetasConsultaFuente.NivelActor(codigo).Should().Be(esperado);
    }

    [Theory]
    [InlineData("DRIVER", "Conductor")]
    [InlineData("VEHICLE", "Vehículo")]
    [InlineData("VIDEN", "Validación de identidad")]
    [InlineData("VIN", "VIN")]
    public void El_tipo_de_consulta_se_traduce_a_lenguaje_de_usuario(string codigo, string esperado)
    {
        EtiquetasConsultaFuente.TipoConsulta(codigo).Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_codigo_ausente_se_nombra_en_vez_de_dejarse_en_blanco(string? codigo)
    {
        // Una celda vacía se lee como un fallo de la pantalla; «Sin especificar» dice que el dato no
        // vino, que es información distinta.
        EtiquetasConsultaFuente.NivelActor(codigo).Should().Be("Sin especificar");
        EtiquetasConsultaFuente.TipoConsulta(codigo).Should().Be("Sin especificar");
    }

    [Fact]
    public void Un_codigo_desconocido_se_muestra_tal_cual_en_vez_de_ocultarse()
    {
        // Si mañana el pipeline empieza a emitir un tipo nuevo, el analista tiene que verlo aunque no
        // esté traducido: taparlo con «Sin especificar» escondería justo la novedad que hay que mirar.
        EtiquetasConsultaFuente.TipoConsulta("SOAT").Should().Be("SOAT");
        EtiquetasConsultaFuente.NivelActor("XXXX").Should().Be("XXXX");
    }

    [Fact]
    public void Bloquea_la_consulta_que_se_intento_y_no_dio_un_dato_valido()
    {
        // Es el caso que atasca el trámite: se pidió al RUNT y no hubo respuesta utilizable.
        EtiquetasConsultaFuente.Bloquea(consultada: true, valida: false, intentos: 3).Should().BeTrue();
        EtiquetasConsultaFuente.Bloquea(consultada: false, valida: false, intentos: 3).Should().BeTrue();
    }

    [Fact]
    public void No_bloquea_la_consulta_que_todavia_no_se_ha_intentado()
    {
        // Estar en cola no es haber fallado. Marcarla como culpable mandaría a soporte a investigar
        // un problema que aún no existe.
        EtiquetasConsultaFuente.Bloquea(consultada: false, valida: false, intentos: 0).Should().BeFalse();
    }

    [Fact]
    public void No_bloquea_la_consulta_resuelta()
    {
        EtiquetasConsultaFuente.Bloquea(consultada: true, valida: true, intentos: 1).Should().BeFalse();
        EtiquetasConsultaFuente.Bloquea(consultada: true, valida: true, intentos: 5).Should().BeFalse();
    }

    /// <summary>
    /// Respuesta del RUNT con la forma real: un árbol con PII DENTRO de arreglos anidados.
    /// </summary>
    private const string RespuestaRunt = """
        {
          "fullName": "DANIEL AMADO GARCIA",
          "documentNumber": "1193552679",
          "owner": [
            { "name": "ANA MARIA RESTREPO", "documentNumber": "43128877", "phone": "3104558812" }
          ],
          "licenses": [
            { "category": "A2", "status": "ACTIVA", "dueDate": "23/07/2032" }
          ]
        }
        """;

    [Fact]
    public void La_respuesta_de_la_fuente_se_enmascara_tambien_dentro_de_los_arreglos()
    {
        // AC3, y es la razón por la que esta pantalla NO usa MaskJson: ese enmascarador está pensado
        // para objetos planos y solo mira las claves del primer nivel, así que el nombre y el documento
        // que viajan dentro de «owner» saldrían en claro. Es un escape de datos personales, no un
        // detalle de formato.
        var enmascarada = IctSensitiveDataMasker.MaskJsonBody(RespuestaRunt);

        enmascarada.Should().NotBeNull();
        enmascarada!.Should().NotContain("DANIEL AMADO GARCIA");
        enmascarada.Should().NotContain("ANA MARIA RESTREPO", "el nombre anidado también es PII");
        enmascarada.Should().NotContain("43128877", "el documento anidado también es PII");
        enmascarada.Should().NotContain("3104558812");
        // Se conservan los últimos cuatro para que soporte confirme contra lo que le dicta el cliente.
        enmascarada.Should().Contain("8877");
    }

    [Fact]
    public void El_enmascarado_conserva_la_estructura_del_arbol_y_los_datos_no_personales()
    {
        // Si el enmascarado aplastara los anidados a cadena, la pestaña mostraría JSON escapado
        // ilegible, y el dato que de verdad se consulta —el estado de la licencia— quedaría enterrado.
        var enmascarada = IctSensitiveDataMasker.MaskJsonBody(RespuestaRunt);

        using var doc = JsonDocument.Parse(enmascarada!);
        var licencias = doc.RootElement.GetProperty("licenses");
        licencias.ValueKind.Should().Be(JsonValueKind.Array, "un arreglo debe seguir siendo un arreglo");
        licencias[0].GetProperty("status").GetString().Should().Be("ACTIVA");
        doc.RootElement.GetProperty("owner").ValueKind.Should().Be(JsonValueKind.Array);
    }
}

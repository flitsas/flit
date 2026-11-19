using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13793 (F05-H4), AC2 (F05-C9) — normalización de la respuesta de <c>ConsultarMultas</c> a <see cref="ResultadoSimit"/>:
/// semáforo, cantidad de comparendos y, por comparendo, solo número, fecha, valor, estado y organismo. Los datos del infractor
/// (campos <c>person_*</c>, datos del chequeo) y la infracción no pasan. Prueba pura, sin base; datos inventados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ResultadoSimitTests
/// </code>
/// </example>
public sealed class ResultadoSimitTests
{
    private const string InfractorFicticio = "PERSONA PRUEBA INFRACTORA";
    private const string DocumentoFicticio = "9999900199";

    // AC2 (F05-C9) — una respuesta con datos del infractor: el payload solo trae número, fecha, valor, estado y organismo.
    [Fact]
    public void Normaliza_solo_numero_fecha_valor_estado_y_organismo()
    {
        var resultado = new ResultadoConsulta { Proveedor = "smartcloud_simit", Semaforo = Semaforo.Rojo };
        resultado.Campos.Add(new Campo { Clave = "person_full_name", ValorTexto = InfractorFicticio });
        resultado.Campos.Add(new Campo { Clave = "owner_document_number", ValorTexto = DocumentoFicticio });
        var multas = new Chequeo { Clave = "multas", Etiqueta = "Multas SIMIT", Estado = EstadoChequeo.Fail, Fuente = "SIMIT", Mensaje = $"{InfractorFicticio} tiene 2 comparendos" };
        multas.Datos.Add(new Dato { Etiqueta = "Infractor", Valor = InfractorFicticio });
        multas.Comparendos.Add(new Comparendo
        {
            Numero = "C-0001",
            Fecha = "2026-01-10",
            Valor = "604100.00",
            Estado = "PENDIENTE",
            Organismo = "SECRETARIA DE TRANSITO DE PRUEBA",
            Infraccion = "C29 VELOCIDAD",
        });
        multas.Comparendos.Add(new Comparendo { Numero = "C-0002", Fecha = "2026-02-11", Valor = "302050.00", Estado = "ACUERDO DE PAGO", Organismo = "TRANSITO FICTICIO" });
        resultado.Chequeos.Add(multas);

        var normalizado = ResultadoSimit.Normalizar(resultado);

        normalizado.Semaforo.Should().Be("ROJO");
        normalizado.CantidadComparendos.Should().Be(2);
        normalizado.Comparendos.Select(c => (c.Numero, c.Fecha, c.Valor, c.Estado, c.Organismo)).Should().Equal(
            ("C-0001", "2026-01-10", "604100.00", "PENDIENTE", "SECRETARIA DE TRANSITO DE PRUEBA"),
            ("C-0002", "2026-02-11", "302050.00", "ACUERDO DE PAGO", "TRANSITO FICTICIO"));
        var json = JsonSerializer.Serialize(normalizado, EjecutorPorPersonas.Json);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["semaforo", "cantidad_comparendos", "comparendos"]);
        doc.RootElement.GetProperty("comparendos")[0].EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["numero", "fecha", "valor", "estado", "organismo"], "nunca datos del infractor ni la infracción");
        json.Should().NotContainAny(InfractorFicticio, DocumentoFicticio, "C29", "smartcloud");
    }
}

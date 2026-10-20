using Flit.Comparendos.Api.Modulos.Ocr;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

/// <summary>HU #13469 — la salida del prompt v2 (con «_lectura» y valores con formato del documento) se interpreta bien.</summary>
public sealed class RespuestaOcrTests
{
    [Fact]
    public void LaNotaDeLecturaSeIgnora_YLosCamposSeLeen()
    {
        const string salida = """
            {
              "_lectura": "p. 1 evidencia con fecha y hora; p. 2 orden de burbujas con el número y el código",
              "numero_comparendo": {"valor": "D05001000000054280231", "confianza": 0.97},
              "valor": {"valor": "633232", "confianza": 0.9},
              "fecha_notificacion": {"valor": "2026-02-24", "confianza": 0.5},
              "placa": {"valor": null, "confianza": 0}
            }
            """;
        var campos = RespuestaOcr.Interpretar(salida)!;

        campos.Keys.Should().BeEquivalentTo([CamposOcr.Numero, CamposOcr.Valor, CamposOcr.FechaNotificacion, CamposOcr.Placa], "«_lectura» no es un campo");
        campos[CamposOcr.Numero].Valor.Should().Be("D05001000000054280231");
        campos[CamposOcr.FechaNotificacion].Confianza.Should().Be(0.5m, "la fecha del sello sale con confianza baja para que el operador la confirme");
        campos[CamposOcr.Placa].Valor.Should().BeNull();
    }
}

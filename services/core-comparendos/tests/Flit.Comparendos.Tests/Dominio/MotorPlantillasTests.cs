using Flit.Comparendos.Domain.Plantillas;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

public sealed class MotorPlantillasTests
{
    [Fact]
    public void Validar_detecta_variables_desconocidas()
    {
        var v = MotorPlantillas.Validar("<p>{{comparendo.numero}} y {{ comparendo.placa_vehiculo }}</p>", "Asunto {{dp.radicado}}");
        v.Usadas.Should().Equal("comparendo.numero", "dp.radicado");
        v.Desconocidas.Should().Equal("comparendo.placa_vehiculo");
        v.Valida.Should().BeFalse();
    }

    [Fact]
    public void Renderizar_escapa_html_salvo_la_firma()
    {
        var html = MotorPlantillas.Renderizar("<p>{{peticionario.nombre}}</p>{{rl.firma_imagen}}{{hoy}}", new Dictionary<string, string?>
        {
            ["peticionario.nombre"] = "A & B <S.A.S>",
            ["rl.firma_imagen"] = "<img src=\"data:image/png;base64,AAA\">",
        });
        html.Should().Be("<p>A &amp; B &lt;S.A.S&gt;</p><img src=\"data:image/png;base64,AAA\">");
    }

    [Fact]
    public void Todas_las_variables_tienen_ejemplo() => VariablesPlantilla.Todas.Should().OnlyContain(v => v.Ejemplo.Length > 0);
}

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
    public void Condicional_con_valor_y_sino()
    {
        const string t = "{{#si conductor.nombres}}Conductor: {{conductor.nombres}}{{#sino}}Sin conductor{{/si}}.";
        MotorPlantillas.Renderizar(t, new Dictionary<string, string?> { ["conductor.nombres"] = "ANA" }).Should().Be("Conductor: ANA.");
        MotorPlantillas.Renderizar(t, new Dictionary<string, string?> { ["conductor.nombres"] = "  " }).Should().Be("Sin conductor.");
        MotorPlantillas.Renderizar(t, new Dictionary<string, string?>()).Should().Be("Sin conductor.");
    }

    [Fact]
    public void Condicional_por_comparacion_y_anidado()
    {
        const string t = "{{#si comparendo.es_fotomulta == \"fotomulta\"}}Foto{{#si comparendo.placa}} de {{comparendo.placa}}{{/si}}{{#sino}}Físico{{/si}}"
            + "{{#si descuento.porcentaje != \"0\"}} con {{descuento.porcentaje}}%{{/si}}";
        MotorPlantillas.Renderizar(t, new Dictionary<string, string?> { ["comparendo.es_fotomulta"] = "Fotomulta", ["comparendo.placa"] = "ABC123", ["descuento.porcentaje"] = "50" })
            .Should().Be("Foto de ABC123 con 50%");
        MotorPlantillas.Renderizar(t, new Dictionary<string, string?> { ["comparendo.es_fotomulta"] = "comparendo físico", ["descuento.porcentaje"] = "0" })
            .Should().Be("Físico");
    }

    [Fact]
    public void Validar_bloques_registra_su_variable_y_rechaza_los_mal_formados()
    {
        var ok = MotorPlantillas.Validar("{{#si conductor.correo}}{{conductor.nombres}}{{#sino}}—{{/si}}");
        ok.Valida.Should().BeTrue();
        ok.Usadas.Should().Equal("conductor.correo", "conductor.nombres");

        MotorPlantillas.Validar("{{#si conductor.correo}}sin cerrar").Valida.Should().BeFalse();
        MotorPlantillas.Validar("suelto{{/si}}").Valida.Should().BeFalse();
        MotorPlantillas.Validar("{{#si conductor.apodo}}x{{/si}}").Desconocidas.Should().Equal("conductor.apodo");
    }

    [Fact]
    public void Todas_las_variables_tienen_ejemplo() => VariablesPlantilla.Todas.Should().OnlyContain(v => v.Ejemplo.Length > 0);
}

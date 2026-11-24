using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>Contacto del propietario desde el RUNT (D3): misma persona, dirección más reciente, inválidos → null.</summary>
public sealed class LectorContactoPropietarioTests
{
    private static readonly DocumentoPersona Doc = new("CC", "9999900001");

    [Fact]
    public void Contacto_y_vehiculo_del_mismo_propietario()
    {
        var json = """
            {"vehiculo":{"propietarios_actuales":[{
              "id_tipo_documento":"C","no_documento":"9999900001","nombre_completo":"PRIMERO",
              "direcciones":[{
                "telefono":"3001234567","email":"a@example.invalid","departamento":"ANTIOQUIA",
                "ciudad":"MEDELLIN","direccion":"CALLE 1 # 2-3","fecha_actualizacion":"15/01/2025"
              }]
            }]}}
            """;

        var c = LectorContactoPropietario.Leer(json, Doc);

        c.Should().BeEquivalentTo(new LectorContactoPropietario.ContactoRunt(
            "3001234567", "a@example.invalid", "ANTIOQUIA", "MEDELLIN", "CALLE 1 # 2-3"));
    }

    [Fact]
    public void Contacto_invalido_queda_null_y_no_bloquea()
    {
        var json = """
            {"vehiculo":{"propietarios_actuales":[{
              "id_tipo_documento":"C","no_documento":"9999900001",
              "direcciones":[{"telefono":"12","email":"sinarroba","direccion":"OK","fecha_actualizacion":"01/01/2024"}]
            }]}}
            """;

        var c = LectorContactoPropietario.Leer(json, Doc);

        c!.Telefono.Should().BeNull();
        c.Correo.Should().BeNull();
        c.Direccion.Should().Be("OK");
    }

    [Fact]
    public void Dos_personas_con_el_mismo_documento_no_mezclan_datos()
    {
        var json = """
            {"vehiculo":{"propietarios_actuales":[
              {"id_tipo_documento":"C","no_documento":"9999900001","direcciones":[{"telefono":"3001111111","direccion":"A","fecha_actualizacion":"01/01/2024"}]},
              {"id_tipo_documento":"C","no_documento":"9999900001","direcciones":[{"telefono":"3002222222","direccion":"B","fecha_actualizacion":"01/01/2024"}]}
            ]}}
            """;

        LectorContactoPropietario.Leer(json, Doc).Should().BeNull();
    }

    [Fact]
    public void Elige_la_direccion_con_fecha_mas_reciente()
    {
        var json = """
            {"vehiculo":{"propietarios_actuales":[{
              "id_tipo_documento":"C","no_documento":"9999900001",
              "direcciones":[
                {"telefono":"3001111111","direccion":"VIEJA","fecha_actualizacion":"01/01/2020"},
                {"telefono":"3009999999","direccion":"NUEVA","fecha_actualizacion":"20/06/2025"}
              ]
            }]}}
            """;

        LectorContactoPropietario.Leer(json, Doc)!.Direccion.Should().Be("NUEVA");
        LectorContactoPropietario.Leer(json, Doc)!.Telefono.Should().Be("3009999999");
    }
}

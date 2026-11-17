using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Diagnostico.Tests.Carga;

/// <summary>
/// HU #14582 AC6 (contrato C1/C2, SP-7) — plantilla y capturas con el propietario actual: cabeceras nuevas
/// <c>Tipo_Documento_Propietario</c>/<c>Documento_Propietario</c>, las viejas de locatario como alias, y un archivo con las dos
/// parejas se rechaza con 422 <c>ENCABEZADO_AMBIGUO</c> (no se fusiona en silencio). En las correcciones JSON, el nombre nuevo y el
/// alias <c>*_locatario</c> juntos son 400 <c>CAMPOS_EXCLUYENTES</c>. Sin base ni HTTP; documentos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EncabezadosPropietarioTests
/// </code>
/// </example>
public sealed class EncabezadosPropietarioTests
{
    private static readonly ParametrosAnalisis Sin = new([Fuente.Runt], 500, MostrarEstimado: false);

    // AC6 — la plantilla trae las cabeceras del propietario actual, en este orden.
    [Fact]
    public void HU14582_AC6_plantilla_con_cabeceras_del_propietario_actual() =>
        PlantillaCarga.Columnas.Should().Equal("Identificador_Vehiculo", "Tipo_Documento_Propietario", "Documento_Propietario");

    // AC6 — con las cabeceras nuevas el documento se lee y sale enmascarado.
    [Fact]
    public void HU14582_AC6_cabeceras_nuevas_se_leen()
    {
        var fila = AnalizadorCarga.Analizar(Tabla(["Identificador_Vehiculo", "Tipo_Documento_Propietario", "Documento_Propietario"]), Sin).Filas.Single();

        fila.Valida.Should().BeTrue();
        fila.TipoDocumentoPropietario.Should().Be("CC");
        fila.DocumentoPropietario.Should().Be("****0001");
    }

    // AC6 — las cabeceras viejas (plantillas ya descargadas) siguen valiendo como alias.
    [Fact]
    public void HU14582_AC6_cabeceras_viejas_de_locatario_son_alias()
    {
        var fila = AnalizadorCarga.Analizar(Tabla(["Identificador_Vehiculo", "Tipo_Documento_Locatario", "Documento_Locatario"]), Sin).Filas.Single();

        fila.Valida.Should().BeTrue();
        fila.TipoDocumentoPropietario.Should().Be("CC");
        fila.DocumentoPropietario.Should().Be("****0001");
    }

    // AC6 (SP-7) — las dos parejas en el mismo archivo: 422 ENCABEZADO_AMBIGUO, también en la confirmación desde el archivo.
    [Theory]
    [InlineData("Tipo_Documento_Locatario", "Documento_Propietario")]
    [InlineData("Tipo_Documento_Propietario", "Documento_Locatario")]
    public void HU14582_AC6_archivo_con_cabeceras_nuevas_y_viejas_se_rechaza(string tipoExtra, string documentoExtra)
    {
        var tabla = new TablaCarga(
            ["Identificador_Vehiculo", "Tipo_Documento_Propietario", "Documento_Propietario", tipoExtra, documentoExtra],
            [new FilaLeida(2, ["TST001", "CC", "9999900001", "CC", "9999900077"])]);

        var analizar = () => AnalizadorCarga.Analizar(tabla, Sin);
        var confirmar = () => LoteDesdeArchivo.Capturados(tabla, new Dictionary<int, CorreccionFila>());

        foreach (var accion in new Action[] { () => analizar(), () => confirmar() })
        {
            var error = accion.Should().Throw<ReglaException>().Which;
            error.Codigo.Should().Be("ENCABEZADO_AMBIGUO");
            error.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
            error.Message.Should().NotContain("9999900001").And.NotContain("9999900077");
        }
    }

    // AC6 (C1) — en las correcciones el nombre nuevo vale y el alias obsoleto también.
    [Theory]
    [InlineData("tipo_documento_propietario", "documento_propietario")]
    [InlineData("tipo_documento_locatario", "documento_locatario")]
    public void HU14582_AC6_correccion_con_nombre_nuevo_o_alias(string tipo, string documento)
    {
        var correcciones = LoteDesdeArchivo.LeerCorrecciones($$"""[{"fila": 2, "{{tipo}}": "CC", "{{documento}}": "9999900001"}]""");

        var capturado = LoteDesdeArchivo.Capturados(Tabla(["Identificador_Vehiculo", "Tipo_Documento_Propietario", "Documento_Propietario"], "TST001", null, null), correcciones)
            .Single();
        capturado.TipoDocumentoPropietario.Should().Be("CC");
        capturado.DocumentoPropietario.Should().Be("9999900001");
    }

    // AC6 (C1) — nombre nuevo y alias en la misma corrección: 400 CAMPOS_EXCLUYENTES, sin repetir el documento.
    [Theory]
    [InlineData("""[{"fila": 2, "tipo_documento_propietario": "CC", "tipo_documento_locatario": "CC"}]""", "tipo_documento_propietario")]
    [InlineData("""[{"fila": 2, "documento_propietario": "9999900001", "documento_locatario": null}]""", "documento_propietario")]
    public void HU14582_AC6_correccion_con_nombre_nuevo_y_alias_es_400(string json, string campo)
    {
        var error = FluentActions.Invoking(() => LoteDesdeArchivo.LeerCorrecciones(json)).Should().Throw<ReglaException>().Which;

        error.Codigo.Should().Be("CAMPOS_EXCLUYENTES");
        error.Status.Should().Be(StatusCodes.Status400BadRequest);
        error.Campo.Should().Be(campo);
        error.Message.Should().NotContain("9999900001");
    }

    private static TablaCarga Tabla(string?[] encabezados, string identificador = "TST001", string? tipo = "CC", string? documento = "9999900001") =>
        new(encabezados, [new FilaLeida(2, [identificador, tipo, documento])]);
}

using System.Text;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Plataforma;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Carga;

/// <summary>
/// HU #13749 (F02-H4), escenario 2 (F02-C6) — lector propio de .csv: UTF-8 (con o sin BOM), separador coma o punto y coma
/// detectado en el encabezado, comillas con comillas dobles escapadas, saltos CRLF o LF y filas vacías omitidas sin
/// perder el número de fila. Un archivo que no es UTF-8 es ilegible.
/// </summary>
/// <example>
/// <code>
/// LectorCsv.Leer(Encoding.UTF8.GetBytes("Identificador_Vehiculo\nABC123\n")) // → 1 fila, número 2
/// </code>
/// </example>
public sealed class LectorCsvTests
{
    [Theory]
    [InlineData(";")]
    [InlineData(",")]
    public void Detecta_el_separador_y_conserva_el_numero_de_fila(string separador)
    {
        var texto = $"﻿Identificador_Vehiculo{separador}Documento_Locatario\r\nABC123{separador}10\r\n{separador}\r\n\r\nXYZ987{separador}\r\n";

        var tabla = LectorCsv.Leer(Encoding.UTF8.GetBytes(texto));

        tabla.Encabezados.Should().Equal("Identificador_Vehiculo", "Documento_Locatario");
        tabla.Filas.Select(f => f.Numero).Should().Equal(2, 5);
        tabla.Filas[0].Celdas.Should().Equal("ABC123", "10");
        tabla.Filas[1].Celdas[0].Should().Be("XYZ987");
    }

    [Fact]
    public void Respeta_comillas_con_separadores_y_comillas_escapadas()
    {
        var tabla = LectorCsv.Leer(Encoding.UTF8.GetBytes("Identificador_Vehiculo,Nota\n\"ABC,123\",\"dice \"\"hola\"\"\"\nXYZ987,\"dos\nlineas\"\nDEF456,x\n"));

        tabla.Filas.Select(f => f.Celdas[0]).Should().Equal("ABC,123", "XYZ987", "DEF456");
        tabla.Filas[0].Celdas[1].Should().Be("dice \"hola\"");
        tabla.Filas[1].Celdas[1].Should().Be("dos\nlineas");
        tabla.Filas.Select(f => f.Numero).Should().Equal([2, 3, 5], "el número es la línea donde empieza el registro");
    }

    [Fact]
    public void Un_archivo_que_no_es_UTF8_es_ilegible()
    {
        var latin1 = new byte[] { 0x49, 0x64, 0x3B, 0xD1, 0x0A };

        var leer = () => LectorCsv.Leer(latin1);

        leer.Should().Throw<ReglaException>().Which.Codigo.Should().Be(FormatoCarga.CodigoIlegible);
    }
}

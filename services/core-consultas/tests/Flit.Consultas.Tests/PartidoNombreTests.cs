using Flit.Consultas.Api.RuntIntempo;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13874 (F10-H2) — partido de nombre §6.2, puerto literal de <c>getSeparateNameDriver</c>
/// (BackCrudTransfer_master <c>src/infrastructure/utils/string-conversion/separateNameDriver.ts</c>). Cada caso dorado es
/// lo que devuelve el TypeScript para la misma entrada, salvo dos defectos corregidos por decisión del usuario
/// (2026-10-23): 4 palabras con prefijos en la 2.ª y 3.ª conservan el nombre y la última rama de 6 o más ya no antepone
/// «0» al segundo apellido. Nombres inventados.
/// </summary>
/// <example>
/// <code>
/// var p = PartidoNombre.Partir("JUAN CARLOS PRUEBA EJEMPLO"); // JUAN / CARLOS / PRUEBA / EJEMPLO
/// </code>
/// </example>
public sealed class PartidoNombreTests
{
    private static void Esperar(string entrada, string nombre, string segundoNombre, string apellido, string segundoApellido) =>
        PartidoNombre.Partir(entrada).Should().Be(new NombrePartido(nombre, segundoNombre, apellido, segundoApellido), $"«{entrada}»");

    [Fact]
    public void Dos_palabras_nombre_y_primer_apellido()
    {
        Esperar("PEDRO PRUEBA", "PEDRO", "", "PRUEBA", "");
        Esperar("EMPRESA PRUEBA", "EMPRESA", "", "PRUEBA", "");
    }

    [Theory]
    [InlineData("PEDRO DE PRUEBA", "PEDRO", "", "DE PRUEBA", "")]
    [InlineData("PEDRO DEL EJEMPLO", "PEDRO", "", "DEL EJEMPLO", "")]
    [InlineData("PEDRO LA PRUEBA", "PEDRO", "", "LA PRUEBA", "")]
    [InlineData("PEDRO CARLOS PRUEBA", "PEDRO", "CARLOS", "PRUEBA", "")]
    [InlineData("PERSONA PRUEBA UNO", "PERSONA", "", "PRUEBA", "UNO")]
    public void Tres_palabras_con_prefijo_DE_DEL_LA_o_segundo_nombre_de_la_lista(
        string entrada, string nombre, string segundoNombre, string apellido, string segundoApellido) =>
        Esperar(entrada, nombre, segundoNombre, apellido, segundoApellido);

    [Theory]
    // 4 palabras
    [InlineData("JUAN CARLOS PRUEBA EJEMPLO", "JUAN", "CARLOS", "PRUEBA", "EJEMPLO")]
    [InlineData("EMPRESA PRUEBA UNO SAS", "EMPRESA", "PRUEBA", "UNO", "SAS")]
    // Corregido (decisión del usuario 2026-10-23): con prefijos encadenados en la 2.ª y 3.ª el nombre se conserva.
    [InlineData("ANA DE LA PRUEBA", "ANA", "", "DE LA PRUEBA", "")]
    [InlineData("PEDRO DE LOS EJEMPLO", "PEDRO", "", "DE LOS EJEMPLO", "")]
    [InlineData("LUIS DEL LA PRUEBA", "LUIS", "", "DEL LA PRUEBA", "")]
    [InlineData("JUAN PABLO DE PRUEBA", "JUAN", "PABLO", "DE PRUEBA", "")]
    [InlineData("MARIA DEL CARMEN PRUEBA", "MARIA", "DEL CARMEN", "PRUEBA", "")]
    [InlineData("JUAN DE PRUEBA EJEMPLO", "JUAN", "", "DE PRUEBA", "EJEMPLO")]
    // Ninguna rama aplica: todo vacío, igual que el original.
    [InlineData("DE JUAN PRUEBA EJEMPLO", "", "", "", "")]
    // 5 palabras
    [InlineData("MARIA DE LOS ANGELES PRUEBA", "MARIA", "DE LOS ANGELES", "PRUEBA", "")]
    [InlineData("JUAN DE LA PRUEBA EJEMPLO", "JUAN", "", "DE LA PRUEBA", "EJEMPLO")]
    [InlineData("PEDRO PRUEBA DE LA EJEMPLO", "PEDRO", "PRUEBA", "DE LA EJEMPLO", "")]
    [InlineData("MARIA DEL CARMEN PRUEBA EJEMPLO", "MARIA", "DEL CARMEN", "PRUEBA", "EJEMPLO")]
    [InlineData("JUAN DE PRUEBA DE EJEMPLO", "JUAN", "", "DE PRUEBA", "DE EJEMPLO")]
    [InlineData("EMPRESA PRUEBA UNO SAS LTDA", "", "", "", "")]
    // 6 o más palabras
    [InlineData("MARIA DE LOS ANGELES PRUEBA EJEMPLO", "MARIA", "DE LOS ANGELES", "PRUEBA", "EJEMPLO")]
    [InlineData("JUAN DE LA PRUEBA EJEMPLO OTRO", "JUAN", "", "DE LA PRUEBA", "EJEMPLO OTRO")]
    [InlineData("MARIA DEL CARMEN PRUEBA EJEMPLO OTRO", "MARIA", "DEL CARMEN", "PRUEBA", "EJEMPLO OTRO")]
    // Corregido (decisión del usuario 2026-10-23): sin el «0» que anteponía el original (`= + ' ' + lastPosition`).
    [InlineData("JUAN DEL CARMEN DE PRUEBA EJEMPLO", "JUAN", "DEL CARMEN", "DE PRUEBA", "EJEMPLO")]
    [InlineData("JUAN DEL CARMEN DE PRUEBA EJEMPLO OTRO", "JUAN", "DEL CARMEN", "DE PRUEBA", "EJEMPLO OTRO")]
    public void Cuatro_o_mas_palabras_con_compuestos_y_prefijos(
        string entrada, string nombre, string segundoNombre, string apellido, string segundoApellido) =>
        Esperar(entrada, nombre, segundoNombre, apellido, segundoApellido);

    [Fact]
    public void Espacios_repetidos_y_minusculas_se_normalizan()
    {
        Esperar(" luis   prueba ", "LUIS", "", "PRUEBA", "");
        Esperar("juan\tcarlos  prueba   ejemplo", "JUAN", "CARLOS", "PRUEBA", "EJEMPLO");
        // Una palabra o vacío: el original cae en el default sin ninguna rama y devuelve todo vacío.
        Esperar("PRUEBA", "", "", "", "");
        Esperar("", "", "", "", "");
        PartidoNombre.Partir(null).Should().Be(new NombrePartido("", "", "", ""));
    }
}

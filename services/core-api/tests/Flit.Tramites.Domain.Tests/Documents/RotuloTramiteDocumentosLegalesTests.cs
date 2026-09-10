using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// ADR-0050 — el rótulo del trámite en los documentos legales es el NOMBRE del tipo.
///
/// <para>El mandato y la solicitud virtual elegían entre dos literales: todo lo que no fuera
/// traspaso se firmaba como "MATRÍCULA INICIAL". El mandato de un blindaje o de un levantamiento de
/// prenda nombraba un trámite distinto del que se estaba radicando, en un documento que el otorgante
/// firma y el organismo archiva.</para>
///
/// <para>Estas pruebas se escribieron primero contra un helper propio de <c>MandatoPdfGenerator</c>.
/// Al integrar <c>develop</c> resultó que allí se había resuelto el mismo problema con
/// <see cref="MandatoTramiteIdentity"/>, que además cubre la tabla de copys y los respaldos; se
/// conservan los casos y se apuntan a ese componente, que es el que gobierna hoy.</para>
/// </summary>
public sealed class RotuloTramiteDocumentosLegalesTests
{
    private static string Rotulo(string? nombre, string? code = null, string? family = "OTROS") =>
        MandatoTramiteIdentity.NombreObjeto(nombre, code, family, null, null);

    [Theory]
    [InlineData("Blindaje", "BLINDAJE")]
    [InlineData("Cambio de color", "CAMBIO DE COLOR")]
    [InlineData("Duplicado de tarjeta", "DUPLICADO DE TARJETA")]
    public void NombraElTramiteReal(string nombre, string esperado)
    {
        Rotulo(nombre).Should().Be(esperado);
    }

    [Fact]
    public void UnTramiteDeOtrosYaNoSeFirmaComoMatriculaInicial()
    {
        // El defecto concreto que ADR-0050 corrige.
        Rotulo("Blindaje").Should().NotBe("MATRÍCULA INICIAL");
    }

    [Fact]
    public void ElNombreSeNormalizaAMayusculas_ComoElRestoDelDocumento()
    {
        Rotulo("  traspaso unilateral  ", family: "TRASPASO").Should().Be("TRASPASO UNILATERAL");
    }

    [Fact]
    public void SinNombreDelTipo_LaFamiliaDecideElRotuloHeredado()
    {
        // Respaldo para los documentos que aún no traen el nombre del catálogo.
        Rotulo(null, family: "TRASPASO").Should().NotBe(Rotulo(null, family: "MATRICULAS"));
    }
}

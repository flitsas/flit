using Flit.Modules.Quipux.Domain.Mapeo;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.Mapeo;

public sealed class QuipuxTipoDocumentoTests
{
    [Theory]
    [InlineData("C", 2)]   // cédula de ciudadanía
    [InlineData("N", 3)]   // NIT
    [InlineData("X", 20)]  // registro civil
    [InlineData("E", 4)]   // cédula de extranjería
    [InlineData("P", 6)]   // pasaporte
    [InlineData("T", 5)]   // tarjeta de identidad
    [InlineData("U", 7)]   // permiso especial de permanencia
    [InlineData("D", 8)]   // diplomático
    public void TryMap_TipoConocido_DevuelveCodigoQuipux(string tipo, int esperado)
    {
        var ok = QuipuxTipoDocumento.TryMap(tipo, out var codigo);

        ok.Should().BeTrue();
        codigo.Should().Be(esperado);
    }

    [Theory]
    [InlineData("c", 2)]
    [InlineData("n", 3)]
    [InlineData("x", 20)]
    [InlineData("d", 8)]
    public void TryMap_TipoEnMinuscula_MapeaIgualQueMayuscula(string tipo, int esperado)
    {
        // En 1.0 la entrada era `vehicleOwnerDocumentType || ''`: un valor de formulario sin normalizar.
        QuipuxTipoDocumento.TryMap(tipo, out var codigo).Should().BeTrue();
        codigo.Should().Be(esperado);
    }

    [Theory]
    [InlineData(" C ")]
    [InlineData("\tC")]
    [InlineData("C\n")]
    [InlineData("  c  ")]
    public void TryMap_TipoConEspaciosAlrededor_SeNormalizaYMapea(string tipo)
    {
        QuipuxTipoDocumento.TryMap(tipo, out var codigo).Should().BeTrue();
        codigo.Should().Be(2);
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("CC")]
    [InlineData("cedula")]
    [InlineData("1")]
    [InlineData("?")]
    public void TryMap_TipoDesconocido_DevuelveFalseYNoProduceCodigo(string tipo)
    {
        // BUG DE 1.0 QUE ESTE TEST BLINDA: mapTypeDocument devolvía el STRING
        // "Se desconoce el tipo de documento" y ese texto viajaba dentro de un campo
        // numérico del payload de Quipux. Aquí no hay centinela: o hay código, o no se radica.
        var ok = QuipuxTipoDocumento.TryMap(tipo, out var codigo);

        ok.Should().BeFalse();
        codigo.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMap_TipoNuloOVacio_NoMapea(string? tipo)
    {
        var ok = QuipuxTipoDocumento.TryMap(tipo, out var codigo);

        ok.Should().BeFalse();
        codigo.Should().Be(0);
    }

    [Theory]
    [InlineData("C", 2)]
    [InlineData("N", 3)]
    [InlineData("X", 20)]
    [InlineData("E", 4)]
    [InlineData("P", 6)]
    [InlineData("T", 5)]
    [InlineData("U", 7)]
    [InlineData("D", 8)]
    public void Map_TipoConocido_DevuelveCodigoQuipux(string tipo, int esperado)
    {
        QuipuxTipoDocumento.Map(tipo).Should().Be(esperado);
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("Se desconoce el tipo de documento")]
    [InlineData("")]
    public void Map_TipoDesconocido_Lanza(string desconocido)
    {
        var act = () => QuipuxTipoDocumento.Map(desconocido);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tipo");
    }

    [Fact]
    public void Map_TipoNulo_Lanza()
    {
        var act = () => QuipuxTipoDocumento.Map(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TiposSoportados_ExponeExactamenteLasOchoLetrasDeQuipux()
    {
        QuipuxTipoDocumento.TiposSoportados
            .Should().BeEquivalentTo(["C", "N", "X", "E", "P", "T", "U", "D"]);
    }
}

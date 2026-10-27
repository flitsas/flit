using Flit.Modules.Quipux.Domain.Mapeo;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.Mapeo;

public sealed class QuipuxTipoDocumentoTests
{
    [Theory]
    [InlineData("C", 2)]   // cédula de ciudadanía
    [InlineData("N", 3)]   // NIT
    [InlineData("X", 20)]  // sin documento
    [InlineData("E", 4)]   // cédula de extranjería
    [InlineData("P", 6)]   // pasaporte
    [InlineData("T", 5)]   // tarjeta de identidad
    [InlineData("U", 7)]   // NUIP / permiso
    [InlineData("D", 8)]   // diplomático
    public void TryMap_TipoConocido_DevuelveCodigoQuipux(string tipo, int esperado)
    {
        var ok = QuipuxTipoDocumento.TryMap(tipo, out var codigo);

        ok.Should().BeTrue();
        codigo.Should().Be(esperado);
    }

    /// <summary>
    /// P0 #13912: actores FLIT 2.0 guardan <c>NIT</c>/<c>CC</c> (no solo <c>N</c>/<c>C</c>).
    /// Sin aliases la radicación aborta con <c>tipo_documento_propietario_no_mapea</c>.
    /// </summary>
    [Theory]
    [InlineData("CC", 2)]
    [InlineData("CEDULA", 2)]
    [InlineData("NIT", 3)]
    [InlineData("CE", 4)]
    [InlineData("TI", 5)]
    [InlineData("PA", 6)]
    [InlineData("PAS", 6)]
    [InlineData("PASAPORTE", 6)]
    [InlineData("NUIP", 7)]
    [InlineData("PEP", 7)]
    [InlineData("CD", 8)]
    [InlineData("RUT", 9)]
    [InlineData("NN", 1)]
    [InlineData("SD", 20)]
    [InlineData("RC", 21)]
    [InlineData("CV", 22)]
    [InlineData("PPT", 7)]
    public void TryMap_AliasDominioFlit_DevuelveCodigoQuipux(string alias, int esperado)
    {
        var ok = QuipuxTipoDocumento.TryMap(alias, out var codigo);

        ok.Should().BeTrue();
        codigo.Should().Be(esperado);
    }

    [Theory]
    [InlineData("nit", 3)]
    [InlineData("cc", 2)]
    [InlineData("Ce", 4)]
    public void TryMap_AliasEnMinusculaOMixto_MapeaIgual(string alias, int esperado)
    {
        QuipuxTipoDocumento.TryMap(alias, out var codigo).Should().BeTrue();
        codigo.Should().Be(esperado);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("9", 9)]
    [InlineData("21", 21)]
    public void TryMap_CodigoNumericoCatalogo_SeAcepta(string codigoTexto, int esperado)
    {
        QuipuxTipoDocumento.TryMap(codigoTexto, out var codigo).Should().BeTrue();
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
    [InlineData(" NIT ")]
    [InlineData("\tCC")]
    public void TryMap_TipoConEspaciosAlrededor_SeNormalizaYMapea(string tipo)
    {
        QuipuxTipoDocumento.TryMap(tipo, out var codigo).Should().BeTrue();
        codigo.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("cedula ciudadana")]
    [InlineData("99")]
    [InlineData("?")]
    [InlineData("0")]
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
    [InlineData("NIT", 3)]
    [InlineData("CC", 2)]
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
    public void TiposSoportados_IncluyeLetrasYAliasesDominio()
    {
        QuipuxTipoDocumento.TiposSoportados.Should().Contain(["C", "N", "X", "E", "P", "T", "U", "D"]);
        QuipuxTipoDocumento.TiposSoportados.Should().Contain(["CC", "NIT", "CE", "TI", "RUT"]);
    }
}

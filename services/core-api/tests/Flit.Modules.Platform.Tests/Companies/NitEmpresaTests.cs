using Flit.Modules.Platform.Domain.Companies;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Platform.Tests.Companies;

/// <summary>
/// HU #14694 (D-TAXID, DA-8; plan §1.3) — vector de la normalización del NIT base que se envía al alta del proveedor.
/// <para>Uso de ejemplo: <c>NitEmpresa.NormalizarBase("900.123.456-7")</c> → <c>(Valido, "900123456")</c>.</para>
/// </summary>
public sealed class NitEmpresaTests
{
    [Theory]
    // Regla 1: se quitan puntos y espacios.
    [InlineData("900.123.456-7", "900123456")]
    [InlineData(" 900 123 456 - 7 ", "900123456")]
    // Regla 2: con guion, la base es lo que va antes (3 a 20 dígitos) y tras el guion hay exactamente un dígito.
    [InlineData("900123456-7", "900123456")]
    [InlineData("123-4", "123")]
    [InlineData("12345678901234567890-1", "12345678901234567890")]
    // Regla 3: sin guion, de 3 a 9 dígitos, la base es el valor tal cual.
    [InlineData("900123456", "900123456")]
    [InlineData("123", "123")]
    [InlineData("8.001", "8001")]
    public void NitValido_DevuelveLaBaseSinDigitoDeVerificacion(string taxId, string esperado)
    {
        var nit = NitEmpresa.NormalizarBase(taxId);

        nit.Estado.Should().Be(EstadoNit.Valido);
        nit.Base.Should().Be(esperado);
    }

    [Theory]
    [InlineData("9001234567")]              // 10 dígitos sin guion: no se sabe si el último es el DV
    [InlineData("90012345678")]
    [InlineData("900123456-")]              // tras el guion no hay exactamente un dígito
    [InlineData("900123456-12")]
    [InlineData("900123456-k")]
    [InlineData("-7")]
    [InlineData("12-3")]                    // base de menos de 3 dígitos
    [InlineData("123456789012345678901-1")] // base de más de 20 dígitos
    [InlineData("900A23456-1")]             // letras
    [InlineData("ABC123")]
    [InlineData("12")]                      // sin guion y menos de 3 dígitos
    [InlineData("1-2-3")]                   // más de un guion
    public void NitAmbiguo_NoTieneBase(string taxId)
    {
        var nit = NitEmpresa.NormalizarBase(taxId);

        nit.Estado.Should().Be(EstadoNit.Ambiguo);
        nit.Base.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(". .")]
    public void NitVacio_EsAusente(string? taxId)
    {
        var nit = NitEmpresa.NormalizarBase(taxId);

        nit.Estado.Should().Be(EstadoNit.Ausente);
        nit.Base.Should().BeNull();
    }

    // DA-8: la hija necesita NIT propio; la comparación es por base normalizada, no por el texto.
    [Theory]
    [InlineData("900123456-7", "900.123.456-7")]
    [InlineData("900123456-7", "900123456")]
    [InlineData("900.123.456", "900123456-1")]
    public void BaseIgualALaDeUnAncestro_EsIgualACabeza(string taxId, string ancestro)
    {
        var nit = NitEmpresa.NormalizarBase(taxId, [null, "800111222-3", ancestro]);

        nit.Estado.Should().Be(EstadoNit.IgualACabeza);
        nit.Base.Should().BeNull();
    }

    [Fact]
    public void AncestrosConNitDistintoOAmbiguo_NoAfectan()
    {
        var nit = NitEmpresa.NormalizarBase("800111222-3", ["9001234567", null, "", "900123456-7"]);

        nit.Should().Be(new NitEmpresa(EstadoNit.Valido, "800111222"));
    }

    [Fact]
    public void NitAmbiguo_GanaSobreLaComparacionConLaCabeza()
    {
        NitEmpresa.NormalizarBase("9001234567", ["9001234567"]).Estado.Should().Be(EstadoNit.Ambiguo);
    }

    [Theory]
    [InlineData(EstadoNit.Valido, "VALIDO")]
    [InlineData(EstadoNit.Ambiguo, "AMBIGUO")]
    [InlineData(EstadoNit.Ausente, "AUSENTE")]
    [InlineData(EstadoNit.IgualACabeza, "IGUAL_A_CABEZA")]
    public void Codigo_EsElDelContrato(EstadoNit estado, string codigo) =>
        NitEmpresa.Codigo(estado).Should().Be(codigo);
}

using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.ExternalSync;

/// <summary>
/// HU #13081 AC3 — enmascarado de los datos personales de los compradores sin el permiso
/// <c>external.tramites.pii.read</c>, con las reglas aprobadas por el PO.
/// <code>ProcedureSyncPiiMasker.Mask(item)</code>
/// </summary>
public sealed class ProcedureSyncPiiMaskerTests
{
    [Theory]
    [InlineData("900000000", "9****0000")]
    [InlineData("1000000000", "1*****0000")]
    [InlineData("12345", "*****")]
    [InlineData("123", "***")]
    public void Documento(string valor, string esperado) => ProcedureSyncPiiMasker.Documento(valor).Should().Be(esperado);

    [Theory]
    [InlineData("contacto@ejemplo.test", "c***@ejemplo.test")]
    [InlineData("sin-arroba", "***")]
    [InlineData("@ejemplo.test", "***")]
    public void Correo(string valor, string esperado) => ProcedureSyncPiiMasker.Correo(valor).Should().Be(esperado);

    [Theory]
    [InlineData("3000000000", "******0000")]
    [InlineData("123", "***")]
    public void Celular(string valor, string esperado) => ProcedureSyncPiiMasker.Celular(valor).Should().Be(esperado);

    [Theory]
    [InlineData("PERSONA EJEMPLO", "P*** E***")]
    [InlineData("EMPRESA  EJEMPLO SAS", "E*** E*** S***")]
    public void Nombre(string valor, string esperado) => ProcedureSyncPiiMasker.Nombre(valor).Should().Be(esperado);

    [Fact]
    public void AC3_ElItemEnmascaradoConservaLasClavesYLosNulos()
    {
        var comprador = new ProcedureSyncComprador(1, null, "comprador", "natural", "CC", "900000000", "PERSONA EJEMPLO",
            "CALLE 1 # 2-3", "PALMIRA", "3000000000", null);
        var item = Item([comprador]);

        var enmascarado = ProcedureSyncPiiMasker.Mask(item).Compradores.Single();

        enmascarado.Should().Be(comprador with
        {
            NumeroDocumento = "9****0000",
            NombreCompleto = "P*** E***",
            Direccion = "***",
            Celular = "******0000",
            Correo = null,
        });
        item.Compradores.Single().NumeroDocumento.Should().Be("900000000", "el ítem original no se toca");
    }

    [Fact]
    public void AC3_SinCompradoresNoFalla()
    {
        ProcedureSyncPiiMasker.Mask(Item([])).Compradores.Should().BeEmpty();
    }

    private static ProcedureSyncItem Item(IReadOnlyList<ProcedureSyncComprador> compradores) => new(
        Guid.NewGuid(), "FT1-0000001", 1, 1, DateTimeOffset.UnixEpoch, false, "entregado",
        new ProcedureSyncTramite("X", "X", null), DateTimeOffset.UnixEpoch, null, null, null, null, compradores, null,
        new ProcedureSyncCompania(Guid.NewGuid(), null, null));
}

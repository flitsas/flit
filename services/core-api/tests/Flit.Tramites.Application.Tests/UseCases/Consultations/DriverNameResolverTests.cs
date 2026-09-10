using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// Separación del nombre del conductor. Los casos salen de las cinco cédulas capturadas contra el
/// RUNT real (un nombre, dos nombres, tres nombres, segundo nombre con partícula, y el caso de
/// control), que son las que ejercitan cada rama.
/// </summary>
public sealed class DriverNameResolverTests
{
    // ── Enmascarado: la razón de ser del resolver ────────────────────────────────────────────

    [Theory]
    [InlineData("S****L")]
    [InlineData("C****S G****Z")]
    [InlineData("SAMUEL*")]
    public void Clean_DescartaValoresEnmascarados(string masked) =>
        DriverNameResolver.Clean(masked).Should().BeEmpty();

    [Fact]
    public void Clean_NormalizaMayusculasYEspaciosRedundantes() =>
        DriverNameResolver.Clean("  daniel   amado  ").Should().Be("DANIEL AMADO");

    [Fact]
    public void FromParts_ConTodoEnmascarado_NoResuelveNada() =>
        DriverNameResolver.FromParts("S****L", "", "C****S", "G****Z")
            .Should().Be(DriverNames.Empty);

    // ── Campos desglosados (Kyverum) ─────────────────────────────────────────────────────────

    [Fact]
    public void FromParts_UsaLosComponentesTalCual()
    {
        var names = DriverNameResolver.FromParts("JOSE", "GABRIEL JAIME", "ACOSTA", "MADRID");

        names.FirstName.Should().Be("JOSE");
        names.SecondName.Should().Be("GABRIEL JAIME");
        names.FirstLastName.Should().Be("ACOSTA");
        names.SecondLastName.Should().Be("MADRID");
        names.FullName.Should().Be("JOSE GABRIEL JAIME ACOSTA MADRID");
        names.Surnames.Should().Be("ACOSTA MADRID");
    }

    [Fact]
    public void FromParts_SegundoNombreVacio_NoDejaEspaciosSueltosEnElNombreCompleto() =>
        DriverNameResolver.FromParts("SAMUEL", "", "CARDENAS", "GUTIERREZ")
            .FullName.Should().Be("SAMUEL CARDENAS GUTIERREZ");

    // ── Campos combinados (Verifik) ──────────────────────────────────────────────────────────

    // Verifik manda TODOS los nombres de pila en firstName y todos los apellidos en lastName.
    [Fact]
    public void FromCombined_SinApellidosSeparados_ParteElLastName()
    {
        var names = DriverNameResolver.FromCombined("SAMUEL", "CARDENAS GUTIERREZ");

        names.FirstName.Should().Be("SAMUEL");
        names.SecondName.Should().BeEmpty();
        names.FirstLastName.Should().Be("CARDENAS");
        names.SecondLastName.Should().Be("GUTIERREZ");
    }

    [Fact]
    public void FromCombined_VariosNombresDePila_ElRestoQuedaComoSegundoNombre()
    {
        var names = DriverNameResolver.FromCombined("JOSE GABRIEL JAIME", "ACOSTA MADRID");

        names.FirstName.Should().Be("JOSE");
        names.SecondName.Should().Be("GABRIEL JAIME");
        names.FirstLastName.Should().Be("ACOSTA");
        names.SecondLastName.Should().Be("MADRID");
    }

    // Cuando Verifik sí manda los apellidos separados, mandan ellos y no se adivina.
    [Fact]
    public void FromCombined_ConApellidosSeparados_LosPrefiereSobreLaSeparacion()
    {
        var names = DriverNameResolver.FromCombined(
            "DAVID ALEJANDRO", "CHICA HERNANDEZ", "CHICA", "HERNANDEZ");

        names.FirstLastName.Should().Be("CHICA");
        names.SecondLastName.Should().Be("HERNANDEZ");
    }

    [Fact]
    public void FromCombined_ApellidoCompuestoConParticula_NoLoParteALaMitad()
    {
        var names = DriverNameResolver.FromCombined("MARIA", "DE LA CRUZ PEREZ");

        names.FirstLastName.Should().Be("DE LA CRUZ");
        names.SecondLastName.Should().Be("PEREZ");
    }

    // ── Separación heurística del nombre completo (último recurso) ───────────────────────────

    [Theory]
    // Los cinco documentos reales capturados contra el RUNT.
    [InlineData("SAMUEL CARDENAS GUTIERREZ", "SAMUEL", "", "CARDENAS", "GUTIERREZ")]
    [InlineData("JOSE GABRIEL JAIME ACOSTA MADRID", "JOSE", "GABRIEL JAIME", "ACOSTA", "MADRID")]
    [InlineData("DAVID ALEJANDRO CHICA HERNANDEZ", "DAVID", "ALEJANDRO", "CHICA", "HERNANDEZ")]
    [InlineData("HECTOR DE JESUS CARDENAS LARREA", "HECTOR", "DE JESUS", "CARDENAS", "LARREA")]
    [InlineData("GLORIA AMPARO GUTIERREZ BERRIO", "GLORIA", "AMPARO", "GUTIERREZ", "BERRIO")]
    // Bordes: un apellido, un solo nombre.
    [InlineData("JUAN PEREZ", "JUAN", "", "PEREZ", "")]
    [InlineData("MADONNA", "MADONNA", "", "", "")]
    public void FromFullName_SeparaSegunLaConvencionColombiana(
        string fullName, string first, string second, string firstLast, string secondLast)
    {
        var names = DriverNameResolver.FromFullName(fullName);

        names.FirstName.Should().Be(first);
        names.SecondName.Should().Be(second);
        names.FirstLastName.Should().Be(firstLast);
        names.SecondLastName.Should().Be(secondLast);
    }

    [Fact]
    public void FromFullName_NombreEnmascarado_NoDevuelveNada() =>
        DriverNameResolver.FromFullName("S****L C****S G****Z").Should().Be(DriverNames.Empty);

    // ── Nombre completo publicado ────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveFullName_PrefiereElDelProveedorCuandoEsUtilizable() =>
        DriverNameResolver.ResolveFullName(
                "SAMUEL CARDENAS GUTIERREZ",
                DriverNameResolver.FromParts("SAMUEL", "", "CARDENAS", "GUTIERREZ"))
            .Should().Be("SAMUEL CARDENAS GUTIERREZ");

    // Si el proveedor solo tiene el enmascarado, se recompone desde los componentes ya resueltos
    // en vez de propagar asteriscos a formularios y documentos.
    [Fact]
    public void ResolveFullName_EnmascaradoOVacio_LoRecomponeDesdeLosComponentes() =>
        DriverNameResolver.ResolveFullName(
                "S****L C****S G****Z",
                DriverNameResolver.FromParts("SAMUEL", "", "CARDENAS", "GUTIERREZ"))
            .Should().Be("SAMUEL CARDENAS GUTIERREZ");
}

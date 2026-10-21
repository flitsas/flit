using System.Text.RegularExpressions;
using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

/// <summary>HU #13653 / #13666 — el código de un rol nuevo se genera a partir del nombre.</summary>
public sealed partial class RoleCodeGeneratorTests
{
    // El mismo patrón que valida la creación de roles propios (2 a 50, empieza por letra o número).
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{1,49}$")]
    private static partial Regex BackendPattern();

    [Theory]
    [InlineData("Contador", "contador")]
    [InlineData("Auditor de Trámites", "auditor_de_tramites")]
    [InlineData("Coordinación Señal", "coordinacion_senal")]
    [InlineData("AÑO ÁÉÍÓÚ ü", "ano_aeiou_u")]
    [InlineData("  Gestor / OT (Bogotá)!  ", "gestor_ot_bogota")]
    [InlineData("rol--de..prueba", "rol_de_prueba")]
    [InlineData("Analista 2", "analista_2")]
    [InlineData("_Interno_", "interno")]
    public void FromName_NormalizaTildesEnieYSignos(string name, string expected)
    {
        var code = RoleCodeGenerator.FromName(name);

        code.Should().Be(expected);
        BackendPattern().IsMatch(code).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("¡¿?!")]
    [InlineData("中文")]
    [InlineData("A")]
    public void FromName_SinNadaUtilizable_UsaRol(string? name)
    {
        RoleCodeGenerator.FromName(name).Should().Be("rol");
    }

    [Fact]
    public void FromName_NombreLargo_SeRecortaAlMaximoSinGuionBajoFinal()
    {
        var code = RoleCodeGenerator.FromName(new string('a', 49) + " bcd");

        code.Should().Be(new string('a', 49));
        code.Length.Should().BeLessThanOrEqualTo(RoleCodeGenerator.MaxLength);
        BackendPattern().IsMatch(code).Should().BeTrue();
    }

    [Fact]
    public void FirstFree_SinColision_DevuelveLaBase()
    {
        RoleCodeGenerator.FirstFree("contador", ["otro"]).Should().Be("contador");
    }

    [Fact]
    public void FirstFree_ConColision_AgregaSufijoConsecutivo_SinDistinguirMayusculas()
    {
        RoleCodeGenerator.FirstFree("contador", ["Contador", "contador_2"]).Should().Be("contador_3");
    }

    [Fact]
    public void FirstFree_BaseAlLimite_RecortaParaQueQuepaElSufijo()
    {
        var baseCode = new string('a', 50);

        var code = RoleCodeGenerator.FirstFree(baseCode, [baseCode]);

        code.Should().Be(new string('a', 48) + "_2");
        code.Length.Should().Be(RoleCodeGenerator.MaxLength);
        BackendPattern().IsMatch(code).Should().BeTrue();
    }

    [Fact]
    public void SearchPrefix_CubreTodosLosCandidatos()
    {
        var baseCode = new string('b', 50);
        var prefix = RoleCodeGenerator.SearchPrefix(baseCode);

        RoleCodeGenerator.Candidate(baseCode, 1).Should().StartWith(prefix);
        RoleCodeGenerator.Candidate(baseCode, 2).Should().StartWith(prefix);
        RoleCodeGenerator.Candidate(baseCode, RoleCodeGenerator.MaxSuffix).Should().StartWith(prefix);
    }

    [Fact]
    public void FirstFree_TodosTomados_LanzaDuplicado()
    {
        var taken = Enumerable.Range(1, RoleCodeGenerator.MaxSuffix).Select(n => RoleCodeGenerator.Candidate("rol", n));

        var act = () => RoleCodeGenerator.FirstFree("rol", taken);

        act.Should().Throw<RoleCodeDuplicateException>();
    }
}

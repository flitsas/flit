using Flit.Modules.Security.Application.Products;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Products;

/// <summary>
/// HU #12898 (B-00) — los códigos deben coincidir con el contrato de plataforma v1, §1: se usan
/// tal cual en hosts, schemas, <c>aud</c> del token y roles, así que un cambio aquí es un cambio
/// de contrato.
/// </summary>
public sealed class ProductCodesTests
{
    [Fact]
    public void All_son_los_cinco_codigos_del_contrato_v1()
    {
        ProductCodes.All.Should().Equal("plataforma", "tramites", "comparendos", "diagnostico", "demo");
    }

    [Fact]
    public void All_no_repite_codigos_y_todos_van_en_minusculas_sin_espacios()
    {
        ProductCodes.All.Should().OnlyHaveUniqueItems();
        ProductCodes.All.Should().AllSatisfy(code =>
            code.Should().MatchRegex("^[a-z]+$"));
    }
}

using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Uso de ejemplo:
/// ParteCompletaRule.EstaCompleta(new ParteDatos("N","D","e@x.co","Bogotá","Calle 1","3001234567"))
/// → true; ParteCompletaRule.CamposFaltantes(...) → lista de campos faltantes reales.
/// </summary>
public sealed class ParteCompletaRuleTests
{
    private static ParteDatos Completa() =>
        new("N", "D", "e@x.co", "Bogotá", "Calle 1 # 2-3", "3001234567");

    [Fact]
    public void EstaCompleta_ConSeisDatos_True()
    {
        ParteCompletaRule.EstaCompleta(Completa()).Should().BeTrue();
        ParteCompletaRule.CamposFaltantes(Completa()).Should().BeEmpty();
    }

    [Fact]
    public void EstaCompleta_Null_False_YListaTodosLosCampos()
    {
        ParteCompletaRule.EstaCompleta(null).Should().BeFalse();
        ParteCompletaRule.CamposFaltantes(null).Should()
            .BeEquivalentTo(["nombre", "documento", "email", "ciudad", "dirección", "teléfono"]);
    }

    [Fact]
    public void CamposFaltantes_SoloTelefonoVacio_ListaSoloTelefono()
    {
        var parte = Completa() with { Telefono = "  " };
        ParteCompletaRule.CamposFaltantes(parte).Should().BeEquivalentTo(["teléfono"]);
    }

    [Fact]
    public void CamposFaltantes_EmailInvalido_ListaEmail()
    {
        var parte = Completa() with { Email = "no-es-email" };
        ParteCompletaRule.CamposFaltantes(parte).Should().Contain("email");
    }

    [Fact]
    public void MensajeFaltantes_EnumeraRolYCampos()
    {
        var parte = Completa() with { Ciudad = null, Direccion = null };
        var mensaje = ParteCompletaRule.MensajeFaltantes("comprador", parte);
        mensaje.Should().Contain("comprador");
        mensaje.Should().Contain("ciudad");
        mensaje.Should().Contain("dirección");
    }
}

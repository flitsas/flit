using Flit.Modules.Security.Application.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// Bug nov. 26 — el correo de soporte en el pie de <see cref="FlitBrandedEmailLayout"/> apuntaba
/// a <c>soporte@flit.com</c> (dominio incorrecto); debe ser <c>soporte@flitsas.com</c>, igual
/// que en <see cref="Flit.Infrastructure.Notifications.Tramites.AsignacionPlacaEmailComposer"/>.
///
/// Uso de ejemplo:
///   FlitBrandedEmailLayout.SupportEmail → "soporte@flitsas.com"
///   FlitBrandedEmailLayout.Wrap(headline, body) → HTML que incluye ese correo en el pie
/// </summary>
public sealed class FlitBrandedEmailLayoutTests
{
    [Fact]
    public void SupportEmail_ApuntaAlDominioFlitsas()
    {
        FlitBrandedEmailLayout.SupportEmail.Should().Be("soporte@flitsas.com");
        FlitBrandedEmailLayout.SupportEmail.Should().NotBe("soporte@flit.com");
    }

    [Fact]
    public void Wrap_IncluyeCorreoDeSoporteFlitsasEnElPie()
    {
        var html = FlitBrandedEmailLayout.Wrap(
            "Título de prueba",
            FlitBrandedEmailLayout.Paragraph("Cuerpo de prueba"));

        html.Should().Contain("soporte@flitsas.com");
        html.Should().NotContain("soporte@flit.com");
    }
}

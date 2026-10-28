using Flit.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>Bug #14010 H-02: el 400 de un parámetro ausente no lleva el texto interno del binder.</summary>
public sealed class BadHttpRequestMessagesTests
{
    [Fact]
    public void FechaAusente_RespondeEnEspanolSinElTipoDeLaExcepcion()
    {
        var ex = new BadHttpRequestException("Required parameter \"DateOnly from\" was not provided from query string.");

        var message = BadHttpRequestMessages.For(ex);

        message.Should().Be("Debes indicar la fecha inicial y la fecha final.");
        message.Should().NotContain("BadHttpRequestException");
        message.Should().NotContain("DateOnly");
    }

    [Fact]
    public void MensajeDeProducto_SeConserva()
    {
        var ex = new BadHttpRequestException("No hay registros para exportar con los filtros indicados.");

        BadHttpRequestMessages.For(ex).Should().Be("No hay registros para exportar con los filtros indicados.");
    }
}

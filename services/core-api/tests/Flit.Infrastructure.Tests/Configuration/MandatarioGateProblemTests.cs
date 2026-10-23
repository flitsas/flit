using Flit.Api.Endpoints.Tramites;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Flit.Infrastructure.Tests.Configuration;

/// <summary>
/// HU #13144 (ADR-0066) — los dos códigos del gate de mandatario se mapean a 409 en /submit y /transition
/// (ambos usan <see cref="MandatarioGateProblem"/>); sin el mapeo el <c>default</c> los daría como 422.
/// </summary>
public sealed class MandatarioGateProblemTests
{
    [Theory]
    [InlineData(TramiteEstadoErrores.MandatarioNoConfigurado, "mandatario activo")]
    [InlineData(TramiteEstadoErrores.MandatarioFirmaInvalida, "identidad aprobada")]
    public void LosDosCodigos_SonConflict409_ConTituloIgualAlCodigo_YMensajeExplicativo(string code, string fragmento)
    {
        var result = MandatarioGateProblem.For(code, null);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Title.Should().Be(code);
        problem.ProblemDetails.Detail.Should().Contain(fragmento);
    }

    [Fact]
    public void ConDetalleDelServicio_LoConserva()
    {
        var result = MandatarioGateProblem.For(TramiteEstadoErrores.MandatarioNoConfigurado, "detalle propio");

        result.Should().BeOfType<ProblemHttpResult>().Which.ProblemDetails.Detail.Should().Be("detalle propio");
    }

    [Fact]
    public void LosCodigosSonLosDelContrato()
    {
        TramiteEstadoErrores.MandatarioNoConfigurado.Should().Be("mandatario_no_configurado");
        TramiteEstadoErrores.MandatarioFirmaInvalida.Should().Be("mandatario_firma_invalida");
    }
}

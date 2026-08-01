using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Exigencia de consulta RUNT por actor.
///
/// <para>El wizard daba la consulta por hecha con solo tener el documento digitado: bastaba escribir
/// una cédula para que el gate del actor pasara. Ahora, en los actores que el tipo de trámite marca
/// con <c>requiresRunt</c>, hace falta evidencia de que ESE documento se consultó en este trámite.</para>
/// </summary>
public sealed class RuntConsultaExigidaTests
{
    private static HashSet<string> Consultados(params (string Tipo, string Numero)[] docs) =>
        docs.Select(d => RuntPersonaConsultada.Key(d.Tipo, d.Numero)).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void SinActoresExigidos_noAplica()
    {
        RuntConsultaExigida.Ninguna.Aplica.Should().BeFalse();
        RuntConsultaExigida.Ninguna.Exige("comprador").Should().BeFalse();
    }

    [Fact]
    public void ExigeSoloALosActoresMarcados()
    {
        var sut = new RuntConsultaExigida(["comprador"], Consultados());

        sut.Aplica.Should().BeTrue();
        sut.Exige("comprador").Should().BeTrue();
        sut.Exige("vendedor").Should().BeFalse();
    }

    [Fact]
    public void ReconoceElDocumentoConsultado()
    {
        var sut = new RuntConsultaExigida(["comprador"], Consultados(("CC", "1098765432")));

        sut.FueConsultado("CC", "1098765432").Should().BeTrue();
    }

    [Fact]
    public void UnDocumentoDistintoNoCuentaComoConsultado()
    {
        var sut = new RuntConsultaExigida(["comprador"], Consultados(("CC", "1098765432")));

        // Escribir otra cédula no puede heredar la evidencia de la anterior: ese era el agujero.
        sut.FueConsultado("CC", "1111111111").Should().BeFalse();
        sut.FueConsultado("CE", "1098765432").Should().BeFalse();
    }

    [Theory]
    [InlineData(" cc ", " 1098765432 ")]
    [InlineData("cc", "1098765432")]
    public void LaComparacionIgnoraEspaciosYMayusculas(string tipo, string numero)
    {
        var sut = new RuntConsultaExigida(["comprador"], Consultados(("CC", "1098765432")));

        sut.FueConsultado(tipo, numero).Should().BeTrue();
    }

    [Fact]
    public void MapeaLosCodigosDelCatalogoAlActorTypeDeLaInstancia()
    {
        // El catálogo no tiene SELLER: quien vende es el propietario registrado.
        RuntConsultaExigida.ActorTypeDeEntidad("OWNER").Should().Be("vendedor");
        RuntConsultaExigida.ActorTypeDeEntidad("BUYER").Should().Be("comprador");
        RuntConsultaExigida.ActorTypeDeEntidad("LESSEE").Should().Be("locatario");
        RuntConsultaExigida.ActorTypeDeEntidad("VEHICLE").Should().BeNull();
        RuntConsultaExigida.ActorTypeDeEntidad(null).Should().BeNull();
    }

    [Fact]
    public void LaEvidenciaSeLeeDelPayloadDelEvento()
    {
        var payload = RuntPersonaConsultada.Payload("CC", "1098765432");

        RuntPersonaConsultada.KeyFromPayload(payload)
            .Should().Be(RuntPersonaConsultada.Key("CC", "1098765432"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("{\"documentType\":\"CC\"}")]
    public void UnPayloadIlegibleNoCuentaComoEvidencia(string? payload)
    {
        RuntPersonaConsultada.KeyFromPayload(payload).Should().BeNull();
    }
}

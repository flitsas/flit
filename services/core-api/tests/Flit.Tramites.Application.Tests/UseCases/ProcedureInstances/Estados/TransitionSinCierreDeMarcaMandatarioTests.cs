using System.Reflection;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// HU #13157 (AC4) — la transición a aprobado o entregado ya no consulta ni cierra marcas de firma
/// posterior del mandatario: el handler no depende del repositorio de marcas.
/// </summary>
public sealed class TransitionSinCierreDeMarcaMandatarioTests
{
    [Fact]
    public void ElHandlerDeTransicion_NoDependeDelRepositorioDeMarcas()
    {
        var parametros = typeof(TransitionProcedureInstanceHandler).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        parametros.Should().NotContain(typeof(IDeferredSignatureMarkRepository));
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Entregado)]
    public async Task TransicionAAprobadoOEntregado_ResuelveSinTocarMarcas(string destino)
    {
        var ct = TestContext.Current.CancellationToken;
        var lifecycle = Substitute.For<ITramiteLifecycleService>();
        var instance = new ProcedureInstance { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(TramiteTransitionOutcome.Ok(instance));
        var handler = new TransitionProcedureInstanceHandler(lifecycle);

        var (result, code, _) = await handler.HandleAsync(instance.Id, instance.TenantId, destino, null, null, ct: ct);

        code.Should().BeNull();
        result.Should().NotBeNull();
    }
}

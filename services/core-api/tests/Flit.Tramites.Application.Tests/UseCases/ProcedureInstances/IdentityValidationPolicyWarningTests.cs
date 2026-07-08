using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// RF40 — política de improntas por IA (informar, no bloquear): al aplicar un resultado con score bajo el
/// umbral, el applier deja un evento de bitácora <c>validacion_biometrica_advertencia</c> SIN alterar el
/// veredicto ni bloquear. Con score en/ sobre el umbral no se emite advertencia.
/// </summary>
public sealed class IdentityValidationPolicyWarningTests
{
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    // Umbral 0.8 (default), política "advertir" (no bloquea).
    private IdentityValidationResultApplier Applier() =>
        new(_events, _repo, new ImprontaValidationPolicyOptions { MatchThreshold = 0.8, BlockBelowThreshold = false });

    private static ProcedureInstanceBiometricValidation EnProceso() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureInstanceId = Guid.NewGuid(),
        PartyRole = "comprador",
        Status = BiometricEstados.EnProceso,
        Provider = BiometricProviders.Kyverum,
    };

    private static IdentityValidationTerminalResult Aprobado(int? score) =>
        new(Approved: true, ProviderStatus: "approved", SanitizedPayload: "{}", Score: score);

    [Fact]
    public async Task ScoreBajoUmbral_EmiteAdvertencia_SinBloquear()
    {
        var v = EnProceso();
        var applied = await Applier().ApplyAsync(v, Aprobado(50), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        applied.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado); // NO bloquea: el veredicto se mantiene.
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == "validacion_biometrica_advertencia" &&
                e.ProcedureInstanceId == v.ProcedureInstanceId &&
                e.TenantId == v.TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoreSobreUmbral_NoEmiteAdvertencia()
    {
        var v = EnProceso();
        await Applier().ApplyAsync(v, Aprobado(95), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        v.Status.Should().Be(BiometricEstados.Aprobado);
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinScore_NoEmiteAdvertencia()
    {
        var v = EnProceso();
        await Applier().ApplyAsync(v, Aprobado(null), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinRepoNiPolitica_ComportamientoPrevio()
    {
        // Applier construido solo con el publisher (como en los tests de webhook/reconciliación): no toca el repo.
        var v = EnProceso();
        var applier = new IdentityValidationResultApplier(_events);

        var applied = await applier.ApplyAsync(v, Aprobado(10), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        applied.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }
}

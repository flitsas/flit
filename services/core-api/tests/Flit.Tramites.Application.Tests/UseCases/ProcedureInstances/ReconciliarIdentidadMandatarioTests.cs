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
/// «Consultar estado» de la validación propia del mandatario: misma reconciliación que la pantalla de espera del trámite.
/// Caso de QA: el intento 1 falló, el intento 2 aprobó y el webhook no llegó; la ficha quedaba «pendiente».
/// </summary>
public sealed class ReconciliarIdentidadMandatarioTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly ReconciliarIdentidadHandler _handler;

    private readonly Guid _signer = Guid.NewGuid();

    public ReconciliarIdentidadMandatarioTests() =>
        _handler = new ReconciliarIdentidadHandler(
            _repo, _kyverum, new IdentityValidationResultApplier(_events), Substitute.For<IIdentityValidationAuditLog>());

    private ProcedureInstanceBiometricValidation Propia(
        string status = BiometricEstados.EnProceso, string provider = BiometricProviders.Kyverum, int attempts = 1)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = null,
            MandateSignerId = _signer,
            PartyRole = BiometricRules.ParteMandatario,
            Status = status,
            Provider = provider,
            KyverumVerificationId = provider == BiometricProviders.Kyverum ? "kyv-m" : null,
            Attempts = attempts,
            MaxAttempts = BiometricRules.KyverumMaxIntentos,
            ReconcilePollCount = BiometricRules.KyverumMaxReconcilePolls,
            Name = "Mandatario",
            DocumentType = "CC",
            DocumentNumber = "1000098455",
            Email = "m@x.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(20),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        _repo.ListMandatarioValidationsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(_signer)), Arg.Any<CancellationToken>())
            .Returns([v]);
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    [Fact]
    public async Task Segundo_intento_aprobado_sin_webhook_la_consulta_lo_aplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Propia(attempts: 1);
        _kyverum.GetStatusAsync("kyv-m", BiometricRules.ParteMandatario, Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("aprobado", 91, "{}"));

        var (result, error) = await _handler.HandleMandatarioAsync(_signer, ct);

        error.Should().BeNull();
        result!.Updated.Should().BeTrue();
        result.Status.Should().Be(BiometricEstados.Aprobado);
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().NotBeNull();
        // La consulta del gestor recarga el presupuesto del worker, igual que en el trámite.
        v.ReconcilePollCount.Should().Be(0);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Intento_rechazado_con_reintentos_disponibles_sigue_en_proceso()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Propia(attempts: 1);
        _kyverum.GetStatusAsync("kyv-m", BiometricRules.ParteMandatario, Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("rechazado_intento", 20, "{\"motivo\":\"rostro\"}"));

        var (result, error) = await _handler.HandleMandatarioAsync(_signer, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(BiometricEstados.EnProceso);
        v.Status.Should().Be(BiometricEstados.EnProceso);
    }

    [Fact]
    public async Task Sin_validacion_propia_responde_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.ListMandatarioValidationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var (result, error) = await _handler.HandleMandatarioAsync(_signer, ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
        await _kyverum.DidNotReceive().GetStatusAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ya_aprobada_no_consulta_al_proveedor()
    {
        var ct = TestContext.Current.CancellationToken;
        Propia(status: BiometricEstados.Aprobado);

        var (result, error) = await _handler.HandleMandatarioAsync(_signer, ct);

        error.Should().BeNull();
        result!.Updated.Should().BeFalse();
        result.Status.Should().Be(BiometricEstados.Aprobado);
        await _kyverum.DidNotReceive().GetStatusAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Proveedor_mock_devuelve_el_estado_sin_consultar()
    {
        var ct = TestContext.Current.CancellationToken;
        Propia(status: BiometricEstados.Enviado, provider: BiometricProviders.Mock);

        var (result, error) = await _handler.HandleMandatarioAsync(_signer, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(BiometricEstados.Enviado);
        await _kyverum.DidNotReceive().GetStatusAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}

using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class IdentityAuditHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetIdentityAuditHandler _handler;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _validation = Guid.NewGuid();

    public IdentityAuditHandlerTests() => _handler = new GetIdentityAuditHandler(_repo);

    private void SeedValidation(Guid? tenant = null, Guid? instance = null) =>
        _repo.GetBiometricByIdAsync(_validation, Arg.Any<CancellationToken>()).Returns(
            new ProcedureInstanceBiometricValidation
            {
                Id = _validation,
                TenantId = tenant ?? _tenant,
                ProcedureInstanceId = instance ?? _instance,
                Provider = BiometricProviders.Kyverum,
            });

    [Fact]
    public async Task Audit_ReturnsEventsInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedValidation();
        _repo.ListIdentityAuditByValidationAsync(_validation, Arg.Any<CancellationToken>()).Returns(new List<IdentityValidationAuditEvent>
        {
            new() { Stage = IdentityValidationAuditStages.WebhookReceived, Outcome = "received", SecretPresent = true },
            new() { Stage = IdentityValidationAuditStages.WebhookNotVerifiable, Outcome = "decrypt_failed", DecryptOk = false, ErrorType = "CryptographicException" },
        });

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.Events.Should().HaveCount(2);
        result.Events[1].DecryptOk.Should().BeFalse();
        result.Events[1].ErrorType.Should().Be("CryptographicException");
    }

    [Fact]
    public async Task Audit_NotFound_WhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Audit_NotFound_WhenTenantMismatch()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedValidation(tenant: Guid.NewGuid());

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Bug14634_ManualRechazado_HomologaElMotivoDesdeElDetalleDeLaBitacora()
    {
        // El Detail que escribe RechazarValidacionManualHandler es "usuario=...; motivo=<codigo>; expira_at=...".
        // Sin esta extracción, el hito histórico de un rechazo manual ya aprobado/reintentado se queda sin
        // motivo (BiometricValidationDto.RejectionReason ya se limpió con la nueva captura).
        var ct = TestContext.Current.CancellationToken;
        SeedValidation();
        _repo.ListIdentityAuditByValidationAsync(_validation, Arg.Any<CancellationToken>()).Returns(new List<IdentityValidationAuditEvent>
        {
            new()
            {
                Stage = IdentityValidationAuditStages.ManualRechazado,
                Outcome = IdentityValidationAuditOutcomes.Rejected,
                Detail = $"usuario={Guid.NewGuid()}; motivo=documento_ilegible_o_incompleto; expira_at=2026-11-20T15:40:58Z",
            },
        });

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.Events.Should().ContainSingle().Which.RejectionReasonLabel.Should().Be("Documento ilegible o incompleto");
    }

    [Fact]
    public async Task Bug14634_OtrasEtapas_NuncaLlevanMotivoHomologado()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedValidation();
        _repo.ListIdentityAuditByValidationAsync(_validation, Arg.Any<CancellationToken>()).Returns(new List<IdentityValidationAuditEvent>
        {
            new() { Stage = IdentityValidationAuditStages.WebhookApplied, Outcome = "aprobado", Detail = "motivo=no_deberia_leerse" },
        });

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.Events.Should().ContainSingle().Which.RejectionReasonLabel.Should().BeNull();
    }

    [Fact]
    public async Task Audit_Referenced_WhenSameTenantOtherInstance_ReturnsEventsWithFlag()
    {
        // HU #10350 — identidad "apalancada"/reutilizada: mismo tenant, OTRO trámite. No es error:
        // devuelve la bitácora real (misma fila) y marca ReferencedFromOtherProcedure = true.
        var ct = TestContext.Current.CancellationToken;
        SeedValidation(instance: Guid.NewGuid());
        _repo.ListIdentityAuditByValidationAsync(_validation, Arg.Any<CancellationToken>()).Returns(new List<IdentityValidationAuditEvent>
        {
            new() { Stage = IdentityValidationAuditStages.WebhookApplied, Outcome = "approved" },
        });

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.ReferencedFromOtherProcedure.Should().BeTrue();
        result.Events.Should().HaveCount(1);
    }
}

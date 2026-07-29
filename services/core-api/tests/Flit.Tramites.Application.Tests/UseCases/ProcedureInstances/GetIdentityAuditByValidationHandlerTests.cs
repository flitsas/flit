using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Tests unitarios de <see cref="GetIdentityAuditByValidationHandler"/> — CF-07 (HU #11005,
/// Feature #11004, ADR-0036). Bitácora de una validación de identidad SIN depender de instanceId:
/// sirve tanto a prevalidaciones standalone como a validaciones de trámite; el tenant es la frontera dura.
/// </summary>
public sealed class GetIdentityAuditByValidationHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private GetIdentityAuditByValidationHandler BuildHandler() => new(_repo);

    private static ProcedureInstanceBiometricValidation Validation(Guid tenantId, Guid? instanceId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Name = "Juan Pérez",
            DocumentType = "CC",
            DocumentNumber = "1234567890",
            Email = "juan@example.com",
            Status = BiometricEstados.EnProceso,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static IdentityValidationAuditEvent AuditEvent(Guid validationId, string stage, string outcome) =>
        new()
        {
            Id = Guid.NewGuid(),
            ValidationId = validationId,
            OccurredAt = DateTimeOffset.UtcNow,
            Stage = stage,
            Outcome = outcome,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Standalone_ReturnsEvents_WhenSameTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var validation = Validation(_tenantId, instanceId: null);
        _repo.GetBiometricByIdAsync(validation.Id, ct).Returns(validation);
        _repo.ListIdentityAuditByValidationAsync(validation.Id, ct).Returns(new List<IdentityValidationAuditEvent>
        {
            AuditEvent(validation.Id, IdentityValidationAuditStages.Send, IdentityValidationAuditOutcomes.Ok),
            AuditEvent(validation.Id, IdentityValidationAuditStages.WebhookApplied, IdentityValidationAuditOutcomes.Approved),
        });
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.ValidationId.Should().Be(validation.Id);
        result.Events.Should().HaveCount(2);
        result.ReferencedFromOtherProcedure.Should().BeFalse("sin instanceId no aplica el concepto de identidad reutilizada");
    }

    [Fact]
    public async Task Instance_ReturnsEvents_WhenSameTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var validation = Validation(_tenantId, instanceId: Guid.NewGuid());
        _repo.GetBiometricByIdAsync(validation.Id, ct).Returns(validation);
        _repo.ListIdentityAuditByValidationAsync(validation.Id, ct).Returns(new List<IdentityValidationAuditEvent>
        {
            AuditEvent(validation.Id, IdentityValidationAuditStages.Send, IdentityValidationAuditOutcomes.Ok),
        });
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result!.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenValidationDoesNotExist()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _repo.GetBiometricByIdAsync(id, ct).Returns((ProcedureInstanceBiometricValidation?)null);
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, id, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNotFound_WhenValidationBelongsToOtherTenant_CrossTenantIsolation()
    {
        var ct = TestContext.Current.CancellationToken;
        var otherTenant = Guid.NewGuid();
        var validation = Validation(otherTenant, instanceId: null);
        _repo.GetBiometricByIdAsync(validation.Id, ct).Returns(validation);
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
        // Defensa en profundidad: no debe siquiera consultar la bitácora de otro tenant.
        await _repo.DidNotReceive().ListIdentityAuditByValidationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}

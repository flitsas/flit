using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>
/// Tests unitarios de <see cref="GetPrevalidacionDetailHandler"/> — CF-06 (HU #11005, Feature #11004,
/// ADR-0036). Detalle de UNA validación por id, tenant-scoped, para poll (standalone o de trámite).
/// </summary>
public sealed class GetPrevalidacionDetailHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private GetPrevalidacionDetailHandler BuildHandler() => new(_repo);

    private static ProcedureInstanceBiometricValidation Validation(
        Guid tenantId, Guid? instanceId = null, string status = BiometricEstados.EnProceso) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            PersonId = instanceId is null ? Guid.NewGuid() : null,
            Name = "Juan Pérez",
            DocumentType = "CC",
            DocumentNumber = "1234567890",
            Email = "juan@example.com",
            Status = status,
            Provider = BiometricProviders.Kyverum,
            Attempts = 1,
            MaxAttempts = 3,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Standalone_ReturnsDetailDto_WhenSameTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var validation = Validation(_tenantId, instanceId: null);
        _repo.GetBiometricByIdAsync(validation.Id, ct).Returns(validation);
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Id.Should().Be(validation.Id);
        result.Status.Should().Be(BiometricEstados.EnProceso);
        result.Intentos.Should().Be(1);
        result.MaxIntentos.Should().Be(3);
        result.Email.Should().Be("juan@example.com");
    }

    [Fact]
    public async Task Instance_ReturnsDetailDto_WhenSameTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var validation = Validation(_tenantId, instanceId);
        _repo.GetBiometricByIdAsync(validation.Id, ct).Returns(validation);
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result!.Id.Should().Be(validation.Id);
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
    public async Task ReturnsNotFound_WhenValidationBelongsToOtherTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var otherTenant = Guid.NewGuid();
        var validation = Validation(otherTenant, instanceId: null);
        _repo.GetBiometricByIdAsync(validation.Id, ct).Returns(validation);
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }
}

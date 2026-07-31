using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>
/// Tests unitarios de <see cref="GetPrevalidacionDetailHandler"/> — CF-06 (HU #11005, Feature #11004,
/// ADR-0036). Detalle de UNA validación por id, tenant-scoped, para poll (standalone o de trámite).
/// HU #11069 — trámites vinculados a la identidad.
/// </summary>
public sealed class GetPrevalidacionDetailHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();

    public GetPrevalidacionDetailHandlerTests()
    {
        _repo.ListLinkedProceduresByIdentityDocumentsAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<(string DocumentType, string DocumentNumber)>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());
    }

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
            ProcedureInstance = instanceId is { } iid
                ? new ProcedureInstance
                {
                    Id = iid,
                    TenantId = tenantId,
                    ReferenceNumber = "TRM-2026-000001",
                    ModalidadEntrada = "traspaso",
                    Status = TramiteEstado.Borrador,
                }
                : null,
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
        result.LinkedProcedures.Should().BeEmpty();
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
        result.ProcedureInstanceId.Should().Be(instanceId);
        result.ReferenceNumber.Should().Be("TRM-2026-000001");
        result.Modalidad.Should().Be("traspaso");
    }

    [Fact]
    public async Task ReturnsLinkedProcedures_ExcludingPrimary()
    {
        var ct = TestContext.Current.CancellationToken;
        var primaryId = Guid.NewGuid();
        var linkedId = Guid.NewGuid();
        var validation = Validation(_tenantId, primaryId);
        var identityKey = BiometricRules.IdentidadKey(
            _tenantId, validation.DocumentType, validation.DocumentNumber);

        _repo.GetBiometricByIdAsync(validation.Id, ct).Returns(validation);
        _repo.ListLinkedProceduresByIdentityDocumentsAsync(
                _tenantId,
                Arg.Any<IReadOnlyCollection<(string DocumentType, string DocumentNumber)>>(),
                ct)
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>
            {
                [identityKey] =
                [
                    new LinkedProcedureSummary(primaryId, "TRM-2026-000001", TramiteEstado.Borrador, "traspaso"),
                    new LinkedProcedureSummary(linkedId, "TRM-2026-000099", TramiteEstado.Preparado, "matricula_inicial"),
                ],
            });

        var (result, error) = await BuildHandler().HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result!.LinkedProcedures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new LinkedProcedureDto(
                linkedId, "TRM-2026-000099", TramiteEstado.Preparado, "matricula_inicial"));
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

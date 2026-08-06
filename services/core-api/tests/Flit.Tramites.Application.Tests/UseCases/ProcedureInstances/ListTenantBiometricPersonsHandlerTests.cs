using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #11270 — listado agrupado por persona (CF-05).</summary>
public sealed class ListTenantBiometricPersonsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationOutboxRepository _outbox =
        Substitute.For<IIdentityValidationOutboxRepository>();

    private ListTenantBiometricPersonsHandler Handler() => new(_repo, _outbox);

    public ListTenantBiometricPersonsHandlerTests()
    {
        _outbox.ListStuckAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repo.CountBiometricValidationsByEstadoAsync(
                Arg.Any<Guid>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { [BiometricEstados.Aprobado] = 7 });
    }

    [Fact]
    public async Task Agrupa_SieteValidaciones_MismaPersona_UnaFilaConCount7()
    {
        var tenantId = Guid.NewGuid();
        var latestId = Guid.NewGuid();
        _repo.ListBiometricValidationsGroupedByPersonAsync(
                tenantId, 0, 20, Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((
                (IReadOnlyList<BiometricPersonGroupProjection>)
                [
                    new BiometricPersonGroupProjection
                    {
                        LatestValidationId = latestId,
                        DocumentType = "CC",
                        DocumentNumber = "123",
                        DocumentTypeNorm = "CC",
                        DocumentNumberNorm = "123",
                        Name = "Ana",
                        Status = BiometricEstados.Aprobado,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        Email = "a@b.co",
                        Provider = BiometricProviders.Mock,
                        ValidationCount = 7,
                    },
                ],
                1));

        _repo.ListBiometricValidationsForPersonAlertScanAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), 30, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var (result, error) = await Handler().HandleAsync(tenantId, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Persons.Should().HaveCount(1);
        result.Persons[0].ValidationCount.Should().Be(7);
        result.Persons[0].LatestValidationId.Should().Be(latestId);
        result.Stats.Total.Should().Be(7);
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task PeorAlerta_PriorizaAtascadaSobreRechazada()
    {
        var tenantId = Guid.NewGuid();
        var stuckId = Guid.NewGuid();
        var rejectedId = Guid.NewGuid();

        _repo.ListBiometricValidationsGroupedByPersonAsync(
                tenantId, 0, 20, Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((
                (IReadOnlyList<BiometricPersonGroupProjection>)
                [
                    new BiometricPersonGroupProjection
                    {
                        LatestValidationId = rejectedId,
                        DocumentType = "CC",
                        DocumentNumber = "999",
                        DocumentTypeNorm = "CC",
                        DocumentNumberNorm = "999",
                        Name = "Bob",
                        Status = BiometricEstados.Rechazado,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        Email = "b@b.co",
                        Provider = BiometricProviders.Mock,
                        ValidationCount = 2,
                    },
                ],
                1));

        _repo.ListBiometricValidationsForPersonAlertScanAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), 30, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ProcedureInstanceBiometricValidation
                {
                    Id = stuckId,
                    TenantId = tenantId,
                    DocumentType = "CC",
                    DocumentNumber = "999",
                    Name = "Bob",
                    Email = "b@b.co",
                    Status = BiometricEstados.ErrorEnvio,
                    TokenHash = "h",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                },
                new ProcedureInstanceBiometricValidation
                {
                    Id = rejectedId,
                    TenantId = tenantId,
                    DocumentType = "CC",
                    DocumentNumber = "999",
                    Name = "Bob",
                    Email = "b@b.co",
                    Status = BiometricEstados.Rechazado,
                    TokenHash = "h2",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            ]);

        _outbox.ListStuckAsync(tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new StuckIdentityValidationRow(
                    Guid.NewGuid(), stuckId, "identity.validation.send", 5,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Bob", "CC", "999",
                    StuckIdentityValidationKinds.Envio),
            ]);

        var (result, error) = await Handler().HandleAsync(tenantId, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Persons[0].WorstAlertKind.Should().Be(IdentityValidationAlertKinds.Atascada);
    }
}

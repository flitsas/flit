using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>HU #11272 — historial multi-validación por persona.</summary>
public sealed class ListPersonBiometricValidationsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private ListPersonBiometricValidationsHandler Handler() => new(_repo);

    [Fact]
    public async Task DevuelveHistorial_OrdenadoYAllTerminal()
    {
        var tenantId = Guid.NewGuid();
        var older = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentType = "CC",
            DocumentNumber = "123",
            Name = "Ana",
            Email = "a@b.co",
            Status = BiometricEstados.Rechazado,
            TokenHash = "a",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        };
        var newer = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentType = "CC",
            DocumentNumber = "123",
            Name = "Ana",
            Email = "a@b.co",
            Status = BiometricEstados.Aprobado,
            TokenHash = "b",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            ValidatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "123", 0, 20, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[newer, older], 2, false));

        _repo.ListLinkedProceduresByIdentityDocumentsAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());

        var (result, error) = await Handler().HandleAsync(
            tenantId, " cc ", " 123 ", page: 1, pageSize: 20, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Total.Should().Be(2);
        result.Validations.Should().HaveCount(2);
        result.Validations[0].Id.Should().Be(newer.Id);
        result.AllTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task DocumentoVacio_ReturnsError()
    {
        var (result, error) = await Handler().HandleAsync(
            Guid.NewGuid(), "  ", null, ct: TestContext.Current.CancellationToken);
        result.Should().BeNull();
        error.Should().Be("documento_requerido");
    }
}

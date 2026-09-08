using Flit.Tramites.Application.UseCases.ImprintSignatures;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ImprintSignatures;

public sealed class ListImprintSignaturesByPlacaHandlerTests
{
    private readonly IVehicleSignatureImprintRepository _imprints = Substitute.For<IVehicleSignatureImprintRepository>();
    private readonly IImprintSignatureValidationRepository _validations = Substitute.For<IImprintSignatureValidationRepository>();
    private readonly ListImprintSignaturesByPlacaHandler _sut;

    public ListImprintSignaturesByPlacaHandlerTests()
    {
        _sut = new ListImprintSignaturesByPlacaHandler(_imprints, _validations);
    }

    [Fact]
    public async Task HandleAsync_AttachesLastValidation_WhenPresent()
    {
        var imprintId = Guid.NewGuid();
        _imprints.ListByPlacaAsync("ABC123", Arg.Any<CancellationToken>()).Returns(
        [
            new VehicleSignatureImprintListRow
            {
                Id = imprintId,
                TenantId = Guid.NewGuid(),
                ProcedureInstanceId = Guid.NewGuid(),
                Placa = "ABC123",
                ModuleCode = "impronta_manual",
                PublicKey = "pk",
                DocumentHash = "hash",
                Signature = "sig",
                SignedAt = DateTimeOffset.UtcNow,
            },
        ]);

        var validationId = Guid.NewGuid();
        var validatedBy = Guid.NewGuid();
        var validatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        _validations.GetLatestByImprintIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ImprintSignatureValidation>
            {
                [imprintId] = new ImprintSignatureValidation
                {
                    Id = validationId,
                    TenantId = Guid.NewGuid(),
                    VehicleSignatureImprintId = imprintId,
                    ProcedureInstanceId = Guid.NewGuid(),
                    Placa = "ABC123",
                    ValidatedBy = validatedBy,
                    ValidatedAt = validatedAt,
                    Result = "invalid",
                    FailureReason = "no coincide",
                },
            });

        var result = await _sut.HandleAsync(
            new ListImprintSignaturesByPlacaQuery { TenantId = Guid.NewGuid(), Placa = "ABC123" },
            TestContext.Current.CancellationToken);

        result.Data.Should().HaveCount(1);
        var last = result.Data[0].LastValidation;
        last.Should().NotBeNull();
        last!.Id.Should().Be(validationId);
        last.Result.Should().Be("invalid");
        last.FailureReason.Should().Be("no coincide");
        last.ValidatedBy.Should().Be(validatedBy);
    }

    [Fact]
    public async Task HandleAsync_LastValidationNull_WhenNoHistory()
    {
        var imprintId = Guid.NewGuid();
        _imprints.ListByPlacaAsync("ZZZ999", Arg.Any<CancellationToken>()).Returns(
        [
            new VehicleSignatureImprintListRow
            {
                Id = imprintId,
                TenantId = Guid.NewGuid(),
                ProcedureInstanceId = Guid.NewGuid(),
                Placa = "ZZZ999",
                ModuleCode = "impronta_manual",
                PublicKey = "pk",
                DocumentHash = "hash",
                Signature = "sig",
                SignedAt = DateTimeOffset.UtcNow,
            },
        ]);
        _validations.GetLatestByImprintIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ImprintSignatureValidation>());

        var result = await _sut.HandleAsync(
            new ListImprintSignaturesByPlacaQuery { TenantId = Guid.NewGuid(), Placa = "ZZZ999" },
            TestContext.Current.CancellationToken);

        result.Data[0].LastValidation.Should().BeNull();
    }
}

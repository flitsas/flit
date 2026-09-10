using Flit.Tramites.Application.UseCases.ImprintSignatures;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ImprintSignatures;

public sealed class ListImprintSignatureValidationsHandlerTests
{
    private readonly IVehicleSignatureImprintRepository _imprints = Substitute.For<IVehicleSignatureImprintRepository>();
    private readonly IImprintSignatureValidationRepository _validations = Substitute.For<IImprintSignatureValidationRepository>();
    private readonly ListImprintSignatureValidationsHandler _sut;

    public ListImprintSignatureValidationsHandlerTests()
    {
        _sut = new ListImprintSignatureValidationsHandler(_imprints, _validations);
    }

    [Fact]
    public async Task HandleAsync_NotFound_WhenImprintMissing()
    {
        var id = Guid.NewGuid();
        _imprints.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((VehicleSignatureImprint?)null);

        var result = await _sut.HandleAsync(
            new ListImprintSignatureValidationsQuery { VehicleSignatureImprintId = id },
            TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ReturnsHistory_OrderedAsRepository()
    {
        var id = Guid.NewGuid();
        _imprints.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new VehicleSignatureImprint
        {
            Id = id,
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            DocumentHash = "h",
            PublicKey = "pk",
            PrivateKey = "sk",
            Signature = "sig",
            SignedAt = DateTimeOffset.UtcNow,
        });

        var newer = new ImprintSignatureValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            VehicleSignatureImprintId = id,
            ProcedureInstanceId = Guid.NewGuid(),
            Placa = "ABC123",
            ValidatedBy = Guid.NewGuid(),
            ValidatedAt = DateTimeOffset.UtcNow,
            Result = "valid",
        };
        var older = new ImprintSignatureValidation
        {
            Id = Guid.NewGuid(),
            TenantId = newer.TenantId,
            VehicleSignatureImprintId = id,
            ProcedureInstanceId = newer.ProcedureInstanceId,
            Placa = "ABC123",
            ValidatedBy = Guid.NewGuid(),
            ValidatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Result = "invalid",
            FailureReason = "no coincide",
        };
        _validations.ListByImprintIdAsync(id, Arg.Any<CancellationToken>()).Returns([newer, older]);

        var result = await _sut.HandleAsync(
            new ListImprintSignatureValidationsQuery { VehicleSignatureImprintId = id },
            TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data[0].Result.Should().Be("valid");
        result.Data[1].Result.Should().Be("invalid");
        result.Data[1].FailureReason.Should().Be("no coincide");
    }
}

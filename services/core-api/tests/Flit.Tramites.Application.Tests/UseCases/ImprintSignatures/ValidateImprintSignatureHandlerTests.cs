using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ImprintSignatures;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ImprintSignatures;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ImprintSignatures;

public sealed class ValidateImprintSignatureHandlerTests
{
    private readonly IVehicleSignatureImprintRepository _imprints = Substitute.For<IVehicleSignatureImprintRepository>();
    private readonly IImprintSignatureValidationRepository _validations = Substitute.For<IImprintSignatureValidationRepository>();
    private readonly IProcedureInstanceRepository _instances = Substitute.For<IProcedureInstanceRepository>();
    private readonly IImprontaManualSignatureVerifier _verifier = Substitute.For<IImprontaManualSignatureVerifier>();

    private readonly ValidateImprintSignatureHandler _sut;

    public ValidateImprintSignatureHandlerTests()
    {
        _sut = new ValidateImprintSignatureHandler(_imprints, _validations, _instances, _verifier);
    }

    [Fact]
    public async Task HandleAsync_InvalidSignature_PersistsInvalidLog()
    {
        var tenantId = Guid.NewGuid();
        var imprintId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _imprints.GetByIdAsync(imprintId, Arg.Any<CancellationToken>())
            .Returns(new VehicleSignatureImprint
            {
                Id = imprintId,
                TenantId = tenantId,
                ProcedureInstanceId = instanceId,
                PublicKey = "pub",
                DocumentHash = "hash",
                Signature = "sig",
                PrivateKey = "must-not-leak",
                SignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _instances.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance
            {
                Id = instanceId,
                TenantId = tenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "FLIT-1",
                Plate = " pov420 ",
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _verifier.Verify("pub", "hash", "sig").Returns(false);

        ImprintSignatureValidation? captured = null;
        _validations.When(v => v.Add(Arg.Any<ImprintSignatureValidation>()))
            .Do(call => captured = call.Arg<ImprintSignatureValidation>());

        var result = await _sut.HandleAsync(
            new ValidateImprintSignatureCommand
            {
                TenantId = tenantId,
                VehicleSignatureImprintId = imprintId,
                ValidatedBy = userId,
            },
            TestContext.Current.CancellationToken);

        result.Result.Should().Be(ImprintSignatureValidationResults.Invalid);
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
        result.ValidationId.Should().NotBe(Guid.Empty);

        captured.Should().NotBeNull();
        captured!.Result.Should().Be(ImprintSignatureValidationResults.Invalid);
        captured.Placa.Should().Be("POV420");
        captured.ValidatedBy.Should().Be(userId);

        _validations.Received(1).Add(Arg.Any<ImprintSignatureValidation>());
        await _validations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidSignature_PersistsValidLog()
    {
        var tenantId = Guid.NewGuid();
        var imprintId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        _imprints.GetByIdAsync(imprintId, Arg.Any<CancellationToken>())
            .Returns(new VehicleSignatureImprint
            {
                Id = imprintId,
                TenantId = tenantId,
                ProcedureInstanceId = instanceId,
                PublicKey = "pub",
                DocumentHash = "hash",
                Signature = "sig",
                PrivateKey = "secret",
                SignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _instances.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance
            {
                Id = instanceId,
                TenantId = tenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "FLIT-2",
                Plate = "ABC123",
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _verifier.Verify("pub", "hash", "sig").Returns(true);

        var result = await _sut.HandleAsync(
            new ValidateImprintSignatureCommand
            {
                TenantId = tenantId,
                VehicleSignatureImprintId = imprintId,
                ValidatedBy = Guid.NewGuid(),
            },
            TestContext.Current.CancellationToken);

        result.Result.Should().Be(ImprintSignatureValidationResults.Valid);
        result.FailureReason.Should().BeNull();
        _validations.Received(1).Add(Arg.Is<ImprintSignatureValidation>(v =>
            v.Result == ImprintSignatureValidationResults.Valid));
    }

    [Fact]
    public async Task HandleAsync_ValidatesAcrossCompanyTenant_WhenCallerIsOtTenant()
    {
        var companyTenantId = Guid.NewGuid();
        var otTenantId = Guid.NewGuid();
        var imprintId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        _imprints.GetByIdAsync(imprintId, Arg.Any<CancellationToken>())
            .Returns(new VehicleSignatureImprint
            {
                Id = imprintId,
                TenantId = companyTenantId,
                ProcedureInstanceId = instanceId,
                PublicKey = "pub",
                DocumentHash = "hash",
                Signature = "sig",
                PrivateKey = "secret",
                SignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _instances.GetByIdAsync(instanceId, companyTenantId, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance
            {
                Id = instanceId,
                TenantId = companyTenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "FLIT-3",
                Plate = "HHH123",
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _verifier.Verify("pub", "hash", "sig").Returns(true);

        ImprintSignatureValidation? captured = null;
        _validations.When(v => v.Add(Arg.Any<ImprintSignatureValidation>()))
            .Do(call => captured = call.Arg<ImprintSignatureValidation>());

        var result = await _sut.HandleAsync(
            new ValidateImprintSignatureCommand
            {
                TenantId = otTenantId,
                VehicleSignatureImprintId = imprintId,
                ValidatedBy = Guid.NewGuid(),
            },
            TestContext.Current.CancellationToken);

        result.Result.Should().Be(ImprintSignatureValidationResults.Valid);
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(otTenantId);
        captured.Placa.Should().Be("HHH123");
    }

    [Fact]
    public async Task HandleAsync_NotFound_DoesNotPersistLog()
    {
        var tenantId = Guid.NewGuid();
        var imprintId = Guid.NewGuid();

        _imprints.GetByIdAsync(imprintId, Arg.Any<CancellationToken>())
            .Returns((VehicleSignatureImprint?)null);

        var result = await _sut.HandleAsync(
            new ValidateImprintSignatureCommand
            {
                TenantId = tenantId,
                VehicleSignatureImprintId = imprintId,
                ValidatedBy = Guid.NewGuid(),
            },
            TestContext.Current.CancellationToken);

        result.Result.Should().Be(ImprintSignatureValidationResults.NotFound);
        _validations.DidNotReceive().Add(Arg.Any<ImprintSignatureValidation>());
        await _validations.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

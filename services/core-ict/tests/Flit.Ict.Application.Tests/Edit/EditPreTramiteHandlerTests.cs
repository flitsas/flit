using Flit.Ict.Application.Edit;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Edit;

public sealed class EditPreTramiteHandlerTests
{
    private readonly IPreTramiteRepository _repository = Substitute.For<IPreTramiteRepository>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public EditPreTramiteHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _tenant.IntegrationClientId.Returns(Guid.NewGuid());
    }

    private EditPreTramiteHandler CreateHandler() => new(_repository, _tenant);

    private ExternalIntegrationMaster Editable(Guid id, long rowVersion = 3) => new()
    {
        Id = id,
        TenantId = _tenantId,
        RowVersion = rowVersion,
        BusinessValidation = 2,
        ExternalValidation = 0,
        ProcessStatusId = 2,
        TrafficSecretaryCode = "5001000",
    };

    [Fact]
    public async Task Validation_affecting_change_resets_business_validation()
    {
        var id = Guid.NewGuid();
        var master = Editable(id, rowVersion: 3);
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, TrafficSecretaryCode: "11001000"), Ct);

        error.Should().BeNull();
        result!.ValidationReset.Should().BeTrue();
        master.BusinessValidation.Should().Be(0);
        master.ProcessStatusId.Should().Be(1);
        await _repository.Received(1).SaveAsync(_tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stale_row_version_returns_stale()
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(Editable(id, rowVersion: 4));

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, DeliveryAddress: "Calle 1"), Ct);

        result.Should().BeNull();
        error.Should().Be("stale");
    }

    [Fact]
    public async Task Materialized_pretramite_cannot_be_edited()
    {
        var id = Guid.NewGuid();
        var master = Editable(id);
        master.ProcedureInstanceId = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(master);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 3, DeliveryAddress: "Calle 1"), Ct);

        result.Should().BeNull();
        error.Should().Be("already_materialized");
    }

    [Fact]
    public async Task Missing_pretramite_returns_not_found()
    {
        var id = Guid.NewGuid();
        _repository.GetAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns((ExternalIntegrationMaster?)null);

        var (result, error) = await CreateHandler().HandleAsync(
            new EditPreTramiteCommand(id, RowVersion: 1), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }
}
